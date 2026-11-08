using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine;

namespace CollectionUI
{
    /// <summary>可复用的单元格视图；在 OnUnbind 中释放监听器和资源，异步结果回填前必须检查 IsCurrent。</summary>
    [RequireComponent(typeof(RectTransform))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UniversalWaterfall", "UniversalWaterfall", "CollectionCell")]
    public class CollectionCell : MonoBehaviour
    {
        /// <summary>当前绑定条目的 ID；解绑后为空。</summary>
        public string ItemId { get; private set; }
        /// <summary>单元格绑定世代；绑定和解绑都会递增，与数据条目的内容版本不同。</summary>
        public int BindingVersion { get; private set; }
        /// <summary>当前是否持有有效绑定。</summary>
        public bool IsBound { get; private set; }

        CancellationTokenSource cancellation;
        /// <summary>当前绑定的取消令牌，首次访问时延迟创建；未绑定时返回已取消的令牌。</summary>
        public CancellationToken BindingCancellation
        {
            get
            {
                if (!IsBound)
                    return new CancellationToken(true);
                if (cancellation == null)
                    cancellation = new CancellationTokenSource();
                return cancellation.Token;
            }
        }

        /// <summary>检查异步操作捕获的条目 ID 和绑定世代是否仍对应当前绑定。</summary>
        public bool IsCurrent(string id, int version) => IsBound && ItemId == id && BindingVersion == version;
        internal void Bind(LayoutEntry entry)
        {
            // 先解除旧绑定，确保复用后旧任务无法通过绑定世代校验。
            Release();
            ItemId = entry.Item.Id;
            BindingVersion++;
            IsBound = true;
            OnBind(entry);
        }

        internal void Release()
        {
            if (!IsBound)
                return;
            // 先使绑定失效，再取消任务；取消回调即使同步执行也会观察到失效状态。
            IsBound = false;
            BindingVersion++;
            Exception failure = null;
            var previousCancellation = cancellation;
            cancellation = null;
            try
            {
                previousCancellation?.Cancel();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                previousCancellation?.Dispose();
            }

            try
            {
                OnUnbind();
            }
            catch (Exception exception)
            {
                if (failure == null)
                    failure = exception;
            }
            finally
            {
                ItemId = null;
            }

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>在绑定状态和世代更新后调用；派生类在此刷新内容或发起异步加载。</summary>
        protected virtual void OnBind(LayoutEntry entry)
        {
        }

        internal void UpdateLayout(LayoutEntry entry) => OnLayoutUpdated(entry);
        /// <summary>绑定内容未变但矩形、索引或所属分区改变时调用；刷新索引相关显示，不取消当前异步任务或递增绑定世代。</summary>
        protected virtual void OnLayoutUpdated(LayoutEntry entry)
        {
        }

        /// <summary>解绑时释放监听器和外部资源；调用前绑定已失效且已有取消令牌已取消。</summary>
        protected virtual void OnUnbind()
        {
        }

        /// <summary>销毁时释放尚未解除的绑定；派生类重写时应调用基类实现。</summary>
        protected virtual void OnDestroy()
        {
            Release();
        }
    }
}
