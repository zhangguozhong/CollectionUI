using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

namespace CollectionUI
{
    /// <summary>业务 loading 节点可选实现。仅通知动画开始/结束；不分发拖动、可松手、失败等展示状态。</summary>
    public interface ICollectionLoadingView
    {
        void SetLoading(bool loading);
    }

    /// <summary>手势刷新、加载更多和预加载控制器。只分发请求，不访问网络；业务提交数据后显式完成请求。</summary>
    [MovedFrom(true, "CollectionUI", "CollectionUI", "CollectionScrollInteraction")]
    [RequireComponent(typeof(CollectionView))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class CollectionLoader : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField, FormerlySerializedAs("refreshLoadingView"), Tooltip("业务提供的顶部 loading Prefab 资源。组件自动在 Viewport 下创建实例并复用，销毁组件时清理实例；不修改或销毁资源。")]
        RectTransform refreshLoadingPrefab;
        [SerializeField, FormerlySerializedAs("loadMoreLoadingView"), Tooltip("业务提供的底部 loading Prefab 资源。允许与顶部使用同一资源，两个方向会分别创建独立实例。")]
        RectTransform loadMoreLoadingPrefab;
        RectTransform refreshLoadingView, loadMoreLoadingView;
        [SerializeField, Tooltip("允许下拉超过阈值并松手后刷新。加载持续到业务调用 CompleteRefresh。")]
        bool refreshEnabled = true;
        [SerializeField, Tooltip("允许上拉超过阈值并松手后加载更多；与预加载共用请求锁。")]
        bool loadMoreEnabled = true;
        [SerializeField, Tooltip("允许手势上拉且接近底部时预加载。惯性、程序滚动、下拉和单纯停在底部不触发。预加载不显示 loading。")]
        bool preloadEnabled;
        [SerializeField, Min(1), Tooltip("松手触发刷新/加载所需的越界距离，单位为 Canvas UI 单位。")]
        float triggerDistance = 96;
        [SerializeField, Min(0), Tooltip("预加载距离：手势上拉时，距列表底部不超过此值即可触发。同一手势最多一次，且已有未完成请求时不重复触发。")]
        float preloadDistance = 300;
        [SerializeField, Min(.01f), Tooltip("loading 保持位置与完成回弹的平滑时间，使用非缩放时间；不是请求超时时间。")]
        float returnDuration = .18f;
        // 分页状态由业务维护，不写入场景/Prefab，也不在 Inspector 显示。
        [NonSerialized]
        bool hasMore = true;
        enum RequestKind
        {
            None,
            Refresh,
            LoadMore,
            Preload
        }

        static long sequence;
        CollectionView collection;
        ScrollRect scroll;
        MonoBehaviour[] refreshBehaviours, loadMoreBehaviours;
        RequestKind kind;
        long requestId;
        bool initialized, dragging, gestureEligible, gestureRequested, holding, settling;
        int pointerId;
        float velocity;
        bool ownsScrollMotion;
        ScrollRect.MovementType previousMovementType;
        public event Action<long> RefreshRequested;
        public event Action<long> LoadMoreRequested;
        public event Action<long> PreloadRequested;
        public bool RefreshEnabled { get => refreshEnabled; set => refreshEnabled = value; }
        public bool LoadMoreEnabled { get => loadMoreEnabled; set => loadMoreEnabled = value; }
        public bool PreloadEnabled { get => preloadEnabled; set => preloadEnabled = value; }
        /// <summary>是否还有下一页，初始为 true；业务通过此属性或完成 API 更新。false 不影响下拉刷新。</summary>
        public bool HasMore { get => hasMore; set => hasMore = value; }

        public float TriggerDistance
        {
            get => triggerDistance;
            set
            {
                Positive(value, nameof(value));
                triggerDistance = value;
                if (refreshLoadingView != null)
                    refreshLoadingView.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, value);
                if (loadMoreLoadingView != null)
                    loadMoreLoadingView.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, value);
                if (initialized)
                    UpdateViews();
            }
        }

        public float PreloadDistance
        {
            get => preloadDistance;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                preloadDistance = value;
            }
        }

        public long ActiveRequestId => requestId;
        public bool IsRefreshing => kind == RequestKind.Refresh;
        public bool IsLoadingMore => kind == RequestKind.LoadMore;
        public bool IsPreloading => kind == RequestKind.Preload;

        float Offset => scroll.content.anchoredPosition.y;

        float Bottom => Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);

        /// <summary>传入两个 Prefab 资源（可使用同一资源）。组件拥有生成的实例，业务不需要提前实例化。
        /// 忙碌或拖动时不能更换；空闲更换会销毁旧实例，下次使用时创建新实例。</summary>
        public void Configure(RectTransform refreshPrefab, RectTransform loadMorePrefab)
        {
            if (requestId != 0 || dragging)
                throw new InvalidOperationException("Configure loading prefabs outside active requests and gestures.");
            ValidatePrefab(refreshPrefab, refreshEnabled, "refresh");
            ValidatePrefab(loadMorePrefab, loadMoreEnabled, "load more");
            RestoreScrollMotion();
            ReleaseInstances();
            refreshLoadingPrefab = refreshPrefab;
            loadMoreLoadingPrefab = loadMorePrefab;
            initialized = false;
            holding = settling = false;
            velocity = 0;
        }

        void Start()
        {
            EnsureInitialized();
        }

        void EnsureInitialized()
        {
            if (initialized)
                return;
            collection = GetComponent<CollectionView>();
            scroll = GetComponent<ScrollRect>();
            if (scroll == null || scroll.viewport == null || scroll.content == null || collection.ScrollRect != scroll)
                throw new InvalidOperationException("Scroll interaction requires a configured CollectionView in Scroll mode and a local ScrollRect.");
            if (scroll.movementType != ScrollRect.MovementType.Elastic)
                throw new InvalidOperationException("Use ScrollRect Elastic movement for pull interaction.");
            Positive(triggerDistance, nameof(triggerDistance));
            Positive(returnDuration, nameof(returnDuration));
            if (float.IsNaN(preloadDistance) || float.IsInfinity(preloadDistance) || preloadDistance < 0)
                throw new ArgumentOutOfRangeException(nameof(preloadDistance));
            ValidatePrefab(refreshLoadingPrefab, refreshEnabled, "refresh");
            ValidatePrefab(loadMoreLoadingPrefab, loadMoreEnabled, "load more");
            refreshLoadingView = CreateInstance(refreshLoadingPrefab, "RefreshLoading");
            loadMoreLoadingView = CreateInstance(loadMoreLoadingPrefab, "LoadMoreLoading");
            refreshBehaviours = Prepare(refreshLoadingView);
            loadMoreBehaviours = Prepare(loadMoreLoadingView);
            initialized = true;
            SetView(refreshLoadingView, refreshBehaviours, false);
            SetView(loadMoreLoadingView, loadMoreBehaviours, false);
            UpdateViews();
        }

        static void ValidatePrefab(RectTransform prefab, bool required, string label)
        {
            if (prefab == null)
            {
                if (required)
                    throw new ArgumentException("Assign the business " + label + " loading prefab.");
                return;
            }

            if (prefab.gameObject.scene.IsValid() || prefab.parent != null)
                throw new ArgumentException("Loading views must reference root RectTransforms of prefab assets, not scene instances.");
        }

        RectTransform CreateInstance(RectTransform prefab, string instanceName)
        {
            if (prefab == null)
                return null;
            var instance = Instantiate(prefab, scroll.viewport, false);
            instance.name = instanceName;
            instance.gameObject.SetActive(false);
            return instance;
        }

        // 只清理组件生成的实例，资源引用始终由业务/资源系统拥有。
        void ReleaseInstances()
        {
            SetView(refreshLoadingView, refreshBehaviours, false);
            SetView(loadMoreLoadingView, loadMoreBehaviours, false);
            DestroyInstance(refreshLoadingView);
            DestroyInstance(loadMoreLoadingView);
            refreshLoadingView = loadMoreLoadingView = null;
            refreshBehaviours = loadMoreBehaviours = null;
        }

        static void DestroyInstance(RectTransform instance)
        {
            if (instance == null)
                return;
            if (Application.isPlaying)
                Destroy(instance.gameObject);
            else
                DestroyImmediate(instance.gameObject);
        }

        void OnDestroy()
        {
            RestoreScrollMotion();
            ReleaseInstances();
        }

        // loading 保持/回弹期间只有一个位置写入者，避免与 ScrollRect 的弹簧同时拉扯。
        void TakeScrollMotion()
        {
            if (!ownsScrollMotion)
            {
                previousMovementType = scroll.movementType;
                ownsScrollMotion = true;
            }

            scroll.movementType = ScrollRect.MovementType.Unrestricted;
            scroll.StopMovement();
        }

        void RestoreScrollMotion()
        {
            if (!ownsScrollMotion)
                return;
            ownsScrollMotion = false;
            if (scroll == null)
                return;
            // 不覆盖业务在接管期间主动改写的新配置。
            if (scroll.movementType == ScrollRect.MovementType.Unrestricted)
                scroll.movementType = previousMovementType;
            scroll.Rebuild(CanvasUpdate.PostLayout);
        }

        MonoBehaviour[] Prepare(RectTransform node)
        {
            if (node == null)
                return Array.Empty<MonoBehaviour>();
            node.anchorMin = new Vector2(0, 1);
            node.anchorMax = Vector2.one;
            node.pivot = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(0, triggerDistance);
            // loading 仅展示，不拦截列表的新拖动；其业务动画脚本仍保留。
            var group = node.GetComponent<CanvasGroup>();
            if (group == null)
                group = node.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            node.SetAsLastSibling();
            return node.GetComponents<MonoBehaviour>();
        }

        static void SetView(RectTransform node, MonoBehaviour[] behaviours, bool loading)
        {
            if (node == null)
                return;
            if (loading)
                node.gameObject.SetActive(true);
            if (behaviours != null)
                foreach (var behaviour in behaviours)
                    if (behaviour != null && behaviour is ICollectionLoadingView renderer)
                        renderer.SetLoading(loading);
            if (!loading)
                node.gameObject.SetActive(false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left)
                return;
            EnsureInitialized();
            if (dragging)
                return;
            dragging = true;
            pointerId = eventData.pointerId;
            gestureEligible = requestId == 0;
            gestureRequested = false;
            // 新手势立即交还原生拖动和阻尼；当前请求仍然锁定。
            RestoreScrollMotion();
            holding = false;
            settling = false;
            velocity = 0;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || !dragging || eventData.pointerId != pointerId || eventData.button != PointerEventData.InputButton.Left)
                return;
            // 两层限制分别负责同一手势次数与跨手势的请求互斥；完成请求不重置手势标记。
            if (gestureRequested || requestId != 0 || !preloadEnabled || !hasMore || eventData.delta.y <= 0)
                return;
            if (Bottom - Offset <= preloadDistance && Offset >= 0)
            {
                gestureRequested = true;
                Begin(RequestKind.Preload);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || !dragging || eventData.pointerId != pointerId || eventData.button != PointerEventData.InputButton.Left)
                return;
            dragging = false;
            if (!gestureEligible || gestureRequested || requestId != 0)
                return;
            if (refreshEnabled && Offset <= -triggerDistance)
            {
                gestureRequested = true;
                Begin(RequestKind.Refresh);
            }
            else if (loadMoreEnabled && hasMore && Offset >= Bottom + triggerDistance)
            {
                gestureRequested = true;
                Begin(RequestKind.LoadMore);
            }
        }

        /// <summary>可由业务主动开始刷新；返回本次请求标识，已有请求或未启用时返回零。</summary>
        public long BeginRefresh() => Begin(RequestKind.Refresh);
        public long BeginLoadMore() => Begin(RequestKind.LoadMore);
        /// <summary>程序定位接管滚动位置；仅解除位置保持，不结束业务请求或 loading。</summary>
        internal void ReleaseScrollHold()
        {
            RestoreScrollMotion();
            holding = settling = false;
            velocity = 0;
        }

        long Begin(RequestKind next)
        {
            if (!isActiveAndEnabled || requestId != 0)
                return 0;
            if ((next == RequestKind.Refresh && !refreshEnabled) || (next == RequestKind.LoadMore && (!loadMoreEnabled || !hasMore)) || (next == RequestKind.Preload && (!preloadEnabled || !hasMore)))
                return 0;
            EnsureInitialized();
            if (next == RequestKind.Refresh && refreshLoadingView == null || next == RequestKind.LoadMore && loadMoreLoadingView == null)
                throw new InvalidOperationException("Assign a loading view before enabling this direction.");
            kind = next;
            requestId = ++sequence;
            if (requestId == 0)
                requestId = ++sequence;
            long dispatched = requestId;
            holding = next != RequestKind.Preload;
            settling = false;
            velocity = 0;
            if (holding)
            {
                collection.CancelSectionNavigation();
                TakeScrollMotion();
            }

            UpdateViews();
            if (next == RequestKind.Refresh)
                SetView(refreshLoadingView, refreshBehaviours, true);
            else if (next == RequestKind.LoadMore)
                SetView(loadMoreLoadingView, loadMoreBehaviours, true);
            // 所有内部状态先就绪，业务可在回调内同步提交快照并立即完成。
            if (requestId == dispatched)
            {
                if (next == RequestKind.Refresh)
                    RefreshRequested?.Invoke(dispatched);
                else if (next == RequestKind.LoadMore)
                    LoadMoreRequested?.Invoke(dispatched);
                else
                    PreloadRequested?.Invoke(dispatched);
            }

            return dispatched;
        }

        /// <summary>业务先提交新数据，再显式完成。过期或不匹配的请求返回 false，不影响当前动画。</summary>
        public bool CompleteRefresh(long id) => Complete(id, RequestKind.Refresh, hasMore);
        public bool CompleteLoadMore(long id, bool hasMore = true) => Complete(id, RequestKind.LoadMore, hasMore);
        public bool CompletePreload(long id, bool hasMore = true) => Complete(id, RequestKind.Preload, hasMore);
        bool Complete(long id, RequestKind expected, bool more)
        {
            if (id == 0 || requestId != id || kind != expected)
                return false;
            bool hadIndicator = kind != RequestKind.Preload;
            requestId = 0;
            kind = RequestKind.None;
            hasMore = more;
            holding = false;
            settling = hadIndicator && !dragging && (Offset < 0 || Offset > Bottom);
            if (settling)
                TakeScrollMotion();
            else
                RestoreScrollMotion();
            velocity = 0;
            SetView(refreshLoadingView, refreshBehaviours, false);
            SetView(loadMoreLoadingView, loadMoreBehaviours, false);
            return true;
        }

        void LateUpdate()
        {
            if (!initialized)
                return;
            if (holding && !dragging)
                AnimateOffset(IsRefreshing ? -triggerDistance : Bottom + triggerDistance);
            else if (settling && !dragging)
            {
                float target = Mathf.Clamp(Offset, 0, Bottom);
                AnimateOffset(target);
                if (Mathf.Abs(Offset - target) < .5f)
                {
                    scroll.content.anchoredPosition = new Vector2(0, target);
                    settling = false;
                    RestoreScrollMotion();
                }
            }

            UpdateViews();
        }

        void AnimateOffset(float target)
        {
            scroll.StopMovement();
            float y = Mathf.SmoothDamp(Offset, target, ref velocity, returnDuration, Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(y - target) < .05f)
            {
                y = target;
                velocity = 0;
            }

            if (Offset != y)
            {
                scroll.content.anchoredPosition = new Vector2(0, y);
                collection.RefreshVisible();
            }
        }

        void UpdateViews()
        {
            // 隐藏状态不写 Transform；显示状态也只在坐标变化时写入。
            if (refreshLoadingView != null && IsRefreshing)
            {
                var position = new Vector2(0, Offset + triggerDistance * .5f);
                if (refreshLoadingView.anchoredPosition != position)
                    refreshLoadingView.anchoredPosition = position;
            }

            if (loadMoreLoadingView != null && IsLoadingMore)
            {
                var position = new Vector2(0, -(Mathf.Max(scroll.content.rect.height, scroll.viewport.rect.height) - Offset + triggerDistance * .5f));
                if (loadMoreLoadingView.anchoredPosition != position)
                    loadMoreLoadingView.anchoredPosition = position;
            }
        }

        void OnDisable()
        {
            RestoreScrollMotion();
            requestId = 0;
            kind = RequestKind.None;
            dragging = holding = settling = gestureEligible = gestureRequested = false;
            velocity = 0;
            if (!initialized)
                return;
            scroll.StopMovement();
            scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(Offset, 0, Bottom));
            collection.RefreshVisible();
            SetView(refreshLoadingView, refreshBehaviours, false);
            SetView(loadMoreLoadingView, loadMoreBehaviours, false);
        }

        static void Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
