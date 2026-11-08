using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Samples
{
    /// <summary>业务拥有的 loading 视觉，只接收开始和结束，不展示拖动、可松手或失败状态。</summary>
    public sealed class DemoLoadingView : MonoBehaviour, ICollectionLoadingView
    {
        [SerializeField]
        RectTransform spinner;
        [SerializeField]
        Text label;
        bool loading;
        public void Configure(RectTransform indicator, Text text)
        {
            spinner = indicator;
            label = text;
        }

        public void SetLoading(bool value)
        {
            loading = value;
            if (label != null)
                label.text = value ? "Loading..." : "";
            if (spinner != null)
                spinner.localRotation = Quaternion.identity;
        }

        void Update()
        {
            if (loading && spinner != null)
                spinner.Rotate(0, 0, -240 * Time.unscaledDeltaTime);
        }
    }
}
