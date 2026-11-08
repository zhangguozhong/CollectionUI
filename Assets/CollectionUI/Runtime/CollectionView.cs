using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI
{
    /// <summary>显示方式：Auto 在首次使用时按本节点是否有 ScrollRect 选择；Expanded 完整生成；Scroll 按视口虚拟化。</summary>
    public enum CollectionDisplayMode
    {
        Auto = 0,
        Expanded = 1,
        Scroll = 2
    }

    /// <summary>数据驱动的 UGUI 集合视图：负责尺寸缓存、布局提交、Cell 绑定和对象池。
    /// 所有调用应在 Unity 主线程进行；业务通过 MeasureItem 提供尺寸，通过 CollectionCell 显示数据。</summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UniversalWaterfall", "UniversalWaterfall", "WaterfallCollectionView")]
    public sealed partial class CollectionView : MonoBehaviour
    {
        /// <summary>复用标识与 Cell 预制体的映射；相同标识的实例共享一个对象池。</summary>
        [Serializable]
        public struct Template
        {
            [Tooltip("模板的唯一标识，须与 CollectionItem.ReuseIdentifier 一致，例如 card。不同预制体应使用不同标识。")]
            public string Id;
            [Tooltip("此模板对应的 Cell 预制体，需要挂载 CollectionCell 或其子类。组件按需创建并复用它。")]
            public CollectionCell Prefab;
        }

        [Tooltip("Auto：本节点有 ScrollRect 时滚动，否则完整展开。Expanded：使用自身 RectTransform 完整生成。Scroll：必须配置本节点的 ScrollRect。请在首次使用前设置，初始化后不支持切换。")]
        [SerializeField]
        CollectionDisplayMode displayMode = CollectionDisplayMode.Auto;
        [Tooltip("仅影响 Expanded：开启时容器高度跟随内容，可缩小至零；关闭时保持外部指定高度，不会自动裁剪。开启时避免父布局或 ContentSizeFitter 同时控制高度。Scroll 的 Content 高度始终由布局维护。运行时请通过 AutoHeight 属性修改。")]
        [SerializeField]
        bool autoHeight = true;
        [Tooltip("可用的 Cell 模板列表。每项配置唯一 ID 和预制体，数据的 ReuseIdentifier 用该 ID 选择模板。请在首次使用前配置，也可调用 RegisterTemplate 注册。")]
        [SerializeField]
        Template[] templates = Array.Empty<Template>();
        [Tooltip("仅滚动模式生效：在视口上方和下方各额外准备多少 UI 单位的 Cell。240 表示上下各扩展 240。数值越大，提前绑定的 Cell 越多，内存和绑定开销也越大；不是屏幕像素或 Cell 数量。")]
        [SerializeField, Min(0)]
        float overscan = 240;
        [Tooltip("每种模板最多保留多少个闲置 Cell，超出即销毁。64 表示每种模板最多缓存 64 个闲置实例，不限制正在显示的 Cell 数量。两种显示模式都可使用对象池。")]
        [SerializeField, Min(0)]
        int maxPooledPerTemplate = 64;
        readonly Dictionary<string, CollectionCell> prefabs = new Dictionary<string, CollectionCell>();
        readonly Dictionary<string, Stack<CollectionCell>> pools = new Dictionary<string, Stack<CollectionCell>>();
        readonly Dictionary<int, CollectionCell> active = new Dictionary<int, CollectionCell>();
        readonly HashSet<int> pendingBindings = new HashSet<int>();
        readonly HashSet<int> pendingLayouts = new HashSet<int>();
        readonly List<int> query = new List<int>();
        readonly List<int> removals = new List<int>();
        readonly List<MonoBehaviour> layoutControllers = new List<MonoBehaviour>();
        readonly List<RectTransform> ancestorLayoutRoots = new List<RectTransform>();
        readonly HashSet<int> wanted = new HashSet<int>();
        Dictionary<string, long> presentationRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
        long presentationRevision;
        Dictionary<SizeKey, Vector2> sizes = new Dictionary<SizeKey, Vector2>();
        readonly HashSet<string> dirtyItems = new HashSet<string>(StringComparer.Ordinal);
        bool hasNotifiedContentSize;
        Vector2 notifiedContentSize;
        ScrollRect scroll;
        RectTransform poolRoot, content;
        bool initialized, expanded, expandedDirty;
        CollectionSnapshot snapshot;
        float layoutWidth = -1;
        bool refreshing, sizesDirty, applying;
        LayoutResult refreshedLayout;
        Rect refreshedRegion;
        bool hasRefreshedRegion;
        int styleVersion;
        ICollectionLayout layoutEngine = new DefaultCollectionLayout();
        /// <summary>可替换的布局策略。更换后清除尺寸缓存，下次更新重建布局；禁止在测量或绑定回调中更换。</summary>
        public ICollectionLayout LayoutEngine
        {
            get => layoutEngine;
            set
            {
                if (refreshing || applying)
                    throw new InvalidOperationException("Change layout engines outside callbacks.");
                layoutEngine = value ?? throw new ArgumentNullException(nameof(value));
                sizes.Clear();
                sizesDirty = true;
            }
        }

        static readonly ProfilerMarker RefreshMarker = new ProfilerMarker("CollectionUI.RefreshVisible");
        static readonly ProfilerMarker LayoutMarker = new ProfilerMarker("CollectionUI.BuildLayout");
        static readonly ProfilerMarker ApplyMarker = new ProfilerMarker("CollectionUI.ApplySnapshot");
        static readonly ProfilerMarker ChangesMarker = new ProfilerMarker("CollectionUI.ApplyChanges");
        /// <summary>最近一次成功提交的布局结果；首次提交前为 null，包含内容总尺寸及所有元素的矩形。</summary>
        public LayoutResult Layout { get; private set; }

        /// <summary>首次成功布局及之后内容宽高改变时，在几何和可见 Cell 提交后通知。
        /// 可调整外部 UI；处理器内不可同步提交快照、重绑、使尺寸/样式失效、滚动或更换布局策略及显示模式/高度设置。
        /// 上述入口会拒绝重入，处理器异常会在提交后向调用方传播。</summary>
        public event Action<Vector2> ContentSizeChanged;
        /// <summary>当前已绑定并由组件管理的 Cell 数量，包含分组头尾。</summary>
        public int ActiveCellCount => active.Count;
        /// <summary>组件生命周期内累计创建次数；从池中取出不会增加该值。</summary>
        public int CreatedCellCount { get; private set; }

        /// <summary>所有模板池中的闲置实例总数，不包含正在显示的 Cell。</summary>
        public int PooledCellCount
        {
            get
            {
                int n = 0;
                foreach (var pool in pools.Values)
                    n += pool.Count;
                return n;
            }
        }

        /// <summary>最近一次构建布局的 同步执行耗时（毫秒），不代表整帧耗时或 FPS。</summary>
        public double LastLayoutMilliseconds { get; private set; }
        /// <summary>最近一次刷新可见 Cell 的 同步执行耗时（毫秒），不包含 GPU 渲染。</summary>
        public double LastRefreshMilliseconds { get; private set; }
        /// <summary>累计布局构建次数，可用于确认单纯滚动未触发重新排版。</summary>
        public int LayoutBuildCount { get; private set; }
        /// <summary>实际执行可见性查询/协调的次数；同一状态的重复调用不增加，用于性能验证。</summary>
        public int VisibleRefreshCount { get; private set; }

        /// <summary>初始化前配置显示方式；Auto 根据本节点的 ScrollRect 选择。读取 ScrollRect 属性或提交数据会触发初始化。</summary>
        public CollectionDisplayMode DisplayMode
        {
            get => displayMode;
            set
            {
                if (value == displayMode)
                    return;
                if (initialized || refreshing || applying)
                    throw new InvalidOperationException("Configure DisplayMode before initializing CollectionView.");
                if (!Enum.IsDefined(typeof(CollectionDisplayMode), value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                displayMode = value;
            }
        }

        /// <summary>完整展开时是否由组件控制宿主高度。开启后立即采用现有内容高度；关闭后保持当前高度。
        /// 不改变锚点、pivot 和位置；希望顶部固定时由使用方设置顶部 pivot。滚动模式始终维护 Content 高度。</summary>
        public bool AutoHeight
        {
            get => autoHeight;
            set
            {
                if (value == autoHeight)
                    return;
                if (refreshing || applying)
                    throw new InvalidOperationException("Change AutoHeight outside binding and measurement callbacks.");
                autoHeight = value;
                if (initialized && expanded && autoHeight && Layout != null)
                    content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Layout.ContentHeight);
            }
        }

        float AvailableWidth => expanded ? content.rect.width : scroll.viewport.rect.width;

        /// <summary>读取时完成延迟初始化；Expanded 返回 null。Auto 遇到未配置完整的 ScrollRect 会报错，不会静默退回展开模式。</summary>
        public ScrollRect ScrollRect
        {
            get
            {
                EnsureInitialized();
                return scroll;
            }
        }

        /// <summary>业务尺寸回调：(数据项, 布局分配的可用宽度) → 最终尺寸。内置布局仅为 Measured 类型调用；自定义布局可主动调用。缓存命中时直接复用尺寸。
        /// 在创建 Cell 前同步执行；请根据数据计算，不要为测量而实例化所有 Cell，也不要在回调内重新提交数据。</summary>
        public Func<CollectionItem, float, Vector2> MeasureItem { get; set; }
        /// <summary>滚动模式下视口上下各扩展的 UI 距离；完整展开模式忽略此值。</summary>
        public float Overscan { get => overscan; set => overscan = Mathf.Max(0, value); }

        // 尺寸还取决于模板、业务版本、样式版本和可用宽度，不能仅按 item ID 缓存。
        struct SizeKey : IEquatable<SizeKey>
        {
            public string Id, Template;
            public int Version, Style;
            public float Width;
            public bool Equals(SizeKey other) => Id == other.Id && Template == other.Template && Version == other.Version && Style == other.Style && Width == other.Width;
            public override bool Equals(object obj) => obj is SizeKey key && Equals(key);
            public override int GetHashCode()
            {
                unchecked
                {
                    return (((Id.GetHashCode() * 397 ^ Template.GetHashCode()) * 397 ^ Version) * 397 ^ Style) * 397 ^ Width.GetHashCode();
                }
            }
        }

        internal bool IsExpandedPresentation
        {
            get
            {
                EnsureInitialized();
                return expanded;
            }
        }

        internal RectTransform PresentationContent
        {
            get
            {
                EnsureInitialized();
                return content;
            }
        }

        internal bool TryGetTemplate(string id, out CollectionCell prefab)
        {
            EnsureInitialized();
            return prefabs.TryGetValue(id, out prefab);
        }

        // 叠层视图保存绑定时的修订号，即使一帧内连续提交多次，也不会漏掉显式更新。
        internal long GetPresentationBindingRevision(string key) => presentationRevisions.TryGetValue(key, out var revision) ? revision : 0;
        partial void OnPresentationDisabled();
        partial void OnPresentationDestroyed();
        partial void OnLayoutGeometryCommitted();
        void OnEnable()
        {
            // 不在首次 OnEnable 中初始化，保证活动节点 AddComponent 后仍可配置模式和模板。
            if (!initialized)
                return;
            if (scroll != null)
                scroll.onValueChanged.AddListener(OnScroll);
            expandedDirty = true;
            RefreshVisible();
        }

        void OnDisable()
        {
            OnPresentationDisabled();
            if (scroll != null)
                scroll.onValueChanged.RemoveListener(OnScroll);
        }

        void OnDestroy()
        {
            OnPresentationDestroyed();
            applying = true;
            try
            {
                RecycleAll();
            }
            finally
            {
                // 单独移除组件时宿主仍然存在，池和生成的 Cell 也必须随组件释放。
                if (poolRoot != null)
                    Destroy(poolRoot.gameObject);
                pools.Clear();
                applying = false;
            }
        }

        void EnsureInitialized()
        {
            if (initialized)
                return;
            var candidate = displayMode == CollectionDisplayMode.Expanded ? null : GetComponent<ScrollRect>();
            if (displayMode == CollectionDisplayMode.Scroll && candidate == null)
                throw new InvalidOperationException("Scroll display mode requires a local ScrollRect.");
            if (candidate != null && (candidate.viewport == null || candidate.content == null))
                throw new InvalidOperationException("Assign ScrollRect viewport and content before initializing CollectionView.");
            // 先验证全部模板，再修改变换或创建池；配置错误后可修正并重试，不留下半初始化状态。
            var validated = new Dictionary<string, CollectionCell>(prefabs);
            foreach (var template in templates)
            {
                if (string.IsNullOrEmpty(template.Id) || template.Prefab == null)
                    throw new ArgumentException("Template ID and prefab are required.");
                if (validated.TryGetValue(template.Id, out var previous) && previous != template.Prefab)
                    throw new InvalidOperationException("Register a new template ID when changing a prefab.");
                validated[template.Id] = template.Prefab;
            }

            scroll = candidate;
            expanded = scroll == null;
            content = expanded ? (RectTransform)transform : scroll.content;
            if (!expanded)
            {
                scroll.horizontal = false;
                scroll.vertical = true;
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0, 1);
                content.sizeDelta = new Vector2(0, content.sizeDelta.y);
                content.anchoredPosition = Vector2.zero;
            }

            var go = new GameObject("CellPool", typeof(RectTransform));
            poolRoot = (RectTransform)go.transform;
            poolRoot.SetParent(transform, false);
            go.SetActive(false);
            for (int i = 0; i < templates.Length; i++)
                RegisterTemplate(templates[i].Id, templates[i].Prefab);
            initialized = true;
            if (isActiveAndEnabled && scroll != null)
                scroll.onValueChanged.AddListener(OnScroll);
        }

        /// <summary>注册模板，可重复注册相同映射；若要更换预制体，请使用新的 ID，避免旧池实例与新模板混用。</summary>
        public void RegisterTemplate(string id, CollectionCell prefab)
        {
            if (string.IsNullOrEmpty(id) || prefab == null)
                throw new ArgumentException("Template ID and prefab are required.");
            if (prefabs.TryGetValue(id, out var previous) && previous != prefab)
                throw new InvalidOperationException("Register a new template ID when changing a prefab.");
            prefabs[id] = prefab;
            if (!pools.ContainsKey(id))
                pools.Add(id, new Stack<CollectionCell>());
        }

        /// <summary>设置待初始化的模板配置，供 Seeder 等创建 Prefab 时使用；已初始化后新增模板请用 RegisterTemplate。</summary>
        public void SetSerializedTemplates(Template[] value)
        {
            templates = value ?? Array.Empty<Template>();
        }

        /// <summary>取得可修改的当前数据副本；业务 Data 对象保持原引用。首次提交前返回空快照。</summary>
        public CollectionSnapshot GetSnapshotCopy() => snapshot?.Copy() ?? new CollectionSnapshot();
        /// <summary>按顺序应用增量操作。任何索引、标识、模板或布局验证失败都不会替换已提交的显示状态。</summary>
        public void ApplyChanges(CollectionChangeSet changes)
        {
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));
            if (refreshing || applying)
                throw new InvalidOperationException("Submit changes outside collection callbacks.");
            if (changes.Count == 0)
                return;
            using var changesScope = ChangesMarker.Auto();
            if (TryApplyTailAppend(changes))
                return;
            var candidate = changes.Apply(snapshot ?? new CollectionSnapshot(), out var forced);
            ApplySnapshotCore(candidate, forced, true);
        }

        /// <summary>复制并提交一份完整数据快照。先完成布局和模板验证，再替换显示状态；滚动模式尽量保持原可见项的位置。
        /// 宿主宽度必须已由父布局确定且大于零；不要在绑定、解绑或测量回调内递归调用。</summary>
        public void ApplySnapshot(CollectionSnapshot value) => ApplySnapshotCore(value, null);
        void ApplySnapshotCore(CollectionSnapshot value, HashSet<string> forced, bool ownsData = false)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (refreshing || applying)
                throw new InvalidOperationException("Submit snapshots outside binding and measurement callbacks.");
            using var applyScope = ApplyMarker.Auto();
            applying = true;
            try
            {
                EnsureInitialized();
                UpdateAncestorLayout();
                float width = AvailableWidth;
                if (width <= 0)
                    throw new InvalidOperationException("Collection viewport must have positive width before applying data.");
                var copy = ownsData ? value : value.Copy();
                CollectionChangeSet.ValidateIdentities(copy);
                var next = Build(copy, width, out var nextSizes, forced, true); // 先验证新布局，失败时保留原显示数据。
                foreach (var entry in next.Entries)
                    if (!prefabs.ContainsKey(entry.Item.ReuseIdentifier))
                        throw new ArgumentException("Unregistered template: " + entry.Item.ReuseIdentifier);
                var anchor = CaptureAnchor();
                var revisions = BuildPresentationRevisions(next, forced);
                ReconcileActive(next, forced);
                snapshot = copy;
                Layout = next;
                presentationRevisions = revisions;
                presentationRevision++;
                layoutWidth = width;
                sizes = nextSizes; // 只保留当前快照和测量宽度；失败构建不会污染已提交缓存。
                dirtyItems.Clear();
                sizesDirty = false;
                CommitLayout(anchor);
            }
            finally
            {
                applying = false;
            }
        }

        LayoutResult Build(CollectionSnapshot data, float width, out Dictionary<SizeKey, Vector2> nextSizes, HashSet<string> forced = null, bool ownsData = false)
        {
            // 统计包含缓存筛选、布局计算和结果缓存整理的整次构建耗时。
            long start = Stopwatch.GetTimestamp();
            // 候选缓存随本次事务提交；旧 ID、版本、模板、样式和历史测量宽度不会持续累积。
            var cache = new Dictionary<SizeKey, Vector2>();
            var dirtySections = new HashSet<string>(StringComparer.Ordinal);
            var customItems = new Dictionary<string, CollectionItem>(StringComparer.Ordinal);
            bool builtIn = layoutEngine is DefaultCollectionLayout;
            bool sameWidth = width == layoutWidth;
            foreach (var section in data.Sections)
            {
                float available = width - section.Layout.Insets.Left - section.Layout.Insets.Right;
                bool headerDirty = section.Header != null && forced != null && forced.Contains(section.Header.LayoutKey);
                bool footerDirty = section.Footer != null && forced != null && forced.Contains(section.Footer.LayoutKey);
                if (headerDirty || footerDirty)
                    dirtySections.Add(section.Id);
                Retain(section.Header, available, headerDirty);
                Retain(section.Footer, available, footerDirty);
                float itemWidth = section.Layout.Mode == LayoutMode.Columns ? (available - (section.Layout.ColumnCount - 1) * section.Layout.HorizontalSpacing) / section.Layout.ColumnCount : available;
                foreach (var item in section.Items)
                {
                    bool dirty = dirtyItems.Contains(item.Id) || (forced != null && forced.Contains(item.LayoutKey));
                    if (dirty)
                        dirtySections.Add(section.Id);
                    Retain(item, itemWidth, dirty);
                }
            }

            void Retain(CollectionItem item, float available, bool dirty)
            {
                if (item == null || string.IsNullOrEmpty(item.LayoutKey) || string.IsNullOrEmpty(item.ReuseIdentifier) || dirty || !sameWidth || sizesDirty)
                    return;
                if (builtIn)
                {
                    // 与内置 Resolve 一致：显式宽度优先，其次是分区或列分配的宽度。
                    if (item.Size.Mode != ItemSizeMode.Measured)
                        return;
                    var key = MeasurementKey(item, item.Size.Width == 0 ? available : item.Size.Width);
                    if (sizes.TryGetValue(key, out var size))
                        cache[key] = size;
                }
                else
                    customItems[item.LayoutKey] = item;
            }

            // 自定义测量宽度由布局引擎定义；按身份建立索引后单次扫描，避免逐条目扫描整个缓存。
            if (!builtIn)
                foreach (var pair in sizes)
                    if (customItems.TryGetValue(pair.Key.Id, out var item) && pair.Key.Template == item.ReuseIdentifier && pair.Key.Version == item.Version && pair.Key.Style == styleVersion)
                        cache[pair.Key] = pair.Value;
            var used = new HashSet<SizeKey>();
            LayoutResult result;
            using (LayoutMarker.Auto())
            {
                var previous = !sizesDirty && sameWidth ? Layout : null;
                var dirty = !sizesDirty && sameWidth ? dirtySections : null;
                Func<CollectionItem, float, Vector2> measure = (item, w) => Measure(item, w, cache, used);
                // 自定义引擎可修改其输入；隔离已提交的分区与列表，避免失败重排污染当前快照。
                result = ownsData && layoutEngine is DefaultCollectionLayout builtInLayout ? builtInLayout.BuildOwned(data, width, measure, previous, dirty) : layoutEngine.Build(builtIn ? data : data.Copy(), width, measure, previous, dirty);
            }

            if (!builtIn)
            {
                // 自定义布局可任意请求宽度；仅保留本次实际请求的键，不保留历史宽度。
                var pruned = new Dictionary<SizeKey, Vector2>();
                foreach (var key in used)
                    pruned[key] = cache[key];
                cache = pruned;
            }

            nextSizes = cache;
            LastLayoutMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            LayoutBuildCount++;
            return result;
        }

        // 从实际宽度来源逐层同步相关控制器，避免全场景 Canvas 更新。
        void UpdateAncestorLayout()
        {
            ancestorLayoutRoots.Clear();
            var widthSource = expanded ? content : scroll.viewport;
            for (var parent = (Transform)widthSource; parent != null; parent = parent.parent)
            {
                parent.GetComponents(layoutControllers);
                foreach (var component in layoutControllers)
                    if (component is ILayoutController && component.isActiveAndEnabled && ControlsLayout(component) && parent is RectTransform rect)
                    {
                        ancestorLayoutRoots.Add(rect);
                        break;
                    }
            }

            layoutControllers.Clear();
            // 计算首选尺寸与分配尺寸采用不同的 UGUI 递归条件，不能只重建最高节点。
            // 从外到内逐个同步，覆盖普通节点或仅控制自身尺寸的组件隔开的内层布局。
            for (int i = ancestorLayoutRoots.Count - 1; i >= 0; i--)
                LayoutRebuilder.ForceRebuildLayoutImmediate(ancestorLayoutRoots[i]);
            ancestorLayoutRoots.Clear();
            widthSource.ForceUpdateRectTransforms();
        }

        static bool ControlsLayout(MonoBehaviour component)
        {
            if (!(component is ScrollRect rectangle))
                return true;
            // 普通 ScrollRect 不分配视口尺寸；自动扩展滚动条会改变视口宽高，必须先同步。
            return (rectangle.verticalScrollbar != null && rectangle.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport) || (rectangle.horizontalScrollbar != null && rectangle.horizontalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport);
        }

        SizeKey MeasurementKey(CollectionItem item, float width) => new SizeKey
        {
            Id = item.LayoutKey,
            Template = item.ReuseIdentifier,
            Version = item.Version,
            Style = styleVersion,
            Width = width
        };
        Vector2 Measure(CollectionItem item, float width, Dictionary<SizeKey, Vector2> cache, HashSet<SizeKey> used)
        {
            if (string.IsNullOrEmpty(item.LayoutKey))
                throw new ArgumentException("Measure an item from the supplied snapshot with valid section and item IDs.");
            var key = MeasurementKey(item, width);
            used.Add(key);
            if (cache.TryGetValue(key, out var result))
                return result;
            if (MeasureItem == null)
                throw new InvalidOperationException("Measured sizing requires MeasureItem.");
            result = MeasureItem(item, width);
            cache[key] = result;
            return result;
        }

        /// <summary>使全局普通条目 ID 对应的测量失效，下一次提交仅重算其分区并尽量保持滚动锚点。
        /// 重复通知会合并；未知 ID 不做操作，空 ID 抛出异常。分组头尾尺寸变化请调用 InvalidateStyle。</summary>
        public void InvalidateItemSize(string id)
        {
            if (refreshing || applying)
                throw new InvalidOperationException("Invalidate sizes outside collection callbacks.");
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Item ID is required.", nameof(id));
            if (Layout == null || !Layout.TryGetItem(id, out var entry))
                return;
            // 提交时 Retain 会统一排除脏项缓存；入口只记录 ID，避免逐次全表扫描和临时集合。
            dirtyItems.Add(id);
        }

        /// <summary>字体、字号、Canvas 缩放等样式变化后调用，使尺寸缓存和布局在下一次更新失效。</summary>
        public void InvalidateStyle()
        {
            if (refreshing || applying)
                throw new InvalidOperationException("Invalidate styles outside collection callbacks.");
            styleVersion++;
            sizes.Clear();
            sizesDirty = true;
        }

        /// <summary>仅重新绑定指定 ID 的当前活跃 Cell，不重算矩形；若内容变化影响尺寸，还需使尺寸失效。</summary>
        public void ReloadItems(IEnumerable<string> ids)
        {
            if (Layout == null)
                return;
            if (refreshing || applying)
                throw new InvalidOperationException("Reload items outside binding callbacks.");
            applying = true;
            try
            {
                var dirty = new HashSet<string>(ids);
                // 按 ID 索引找到普通条目及同名分组头尾，吸顶副本也能收到离屏页眉的修订。
                long revision = ++presentationRevision;
                var indices = new List<int>();
                foreach (var id in dirty)
                    Layout.AddIndicesForId(id, indices);
                foreach (int index in indices)
                {
                    var entry = Layout.Entries[index];
                    presentationRevisions[entry.Key] = revision;
                    if (active.ContainsKey(index))
                        pendingBindings.Add(index);
                }

                foreach (int index in indices)
                {
                    if (!active.TryGetValue(index, out var cell))
                        continue;
                    try
                    {
                        cell.Bind(Layout.Entries[index]);
                        pendingBindings.Remove(index);
                    }
                    catch
                    {
                        active.Remove(index);
                        DiscardFailedCell(cell);
                        hasRefreshedRegion = false;
                        expandedDirty = true;
                        throw;
                    }
                }
            }
            finally
            {
                applying = false;
            }
        }

        void LateUpdate()
        {
            if (snapshot == null)
                return;
            float width = AvailableWidth;
            if (width <= 0)
                return;
            if (Mathf.Abs(width - layoutWidth) > 0.1f || sizesDirty || dirtyItems.Count > 0)
            {
                applying = true;
                try
                {
                    var next = Build(snapshot, width, out var nextSizes);
                    var anchor = CaptureAnchor();
                    var revisions = BuildPresentationRevisions(next, null);
                    ReconcileActive(next);
                    Layout = next;
                    presentationRevisions = revisions;
                    presentationRevision++;
                    layoutWidth = width;
                    sizes = nextSizes;
                    dirtyItems.Clear();
                    sizesDirty = false;
                    CommitLayout(anchor);
                }
                finally
                {
                    applying = false;
                }
            }
            else if (!expanded)
                RefreshVisible(); // 也处理没有滚动事件的视口高度变化；Expanded 未变化时无需每帧遍历。
        }

        struct Anchor
        {
            public string Key;
            public int Index;
            public float Offset, Absolute, Bottom;
            public Vector2 Velocity;
        }

        // 记录最靠近视口顶部的元素及其相对偏移，重新排版后尽量让用户仍看到原来的内容。
        Anchor CaptureAnchor()
        {
            if (expanded)
                return new Anchor
                {
                    Index = -1
                };
            var a = new Anchor
            {
                Index = -1,
                Absolute = scroll.content.anchoredPosition.y,
                Bottom = Mathf.Max(0, (Layout?.ContentHeight ?? 0) - scroll.viewport.rect.height),
                Velocity = scroll.velocity
            };
            if (Layout == null)
                return a;
            float distance = float.MaxValue;
            Layout.Query(new Rect(0, a.Absolute, layoutWidth, scroll.viewport.rect.height), query);
            for (int i = 0; i < query.Count; i++)
            {
                var e = Layout.Entries[query[i]];
                float d = Mathf.Abs(e.Rect.yMin - a.Absolute);
                if (d < distance)
                {
                    distance = d;
                    a.Key = e.Key;
                    a.Index = query[i];
                    a.Offset = e.Rect.yMin - a.Absolute;
                }
            }

            return a;
        }

        // 只有明确拥有高度的模式才写回尺寸；普通节点的位置和锚点由父布局/业务保留。
        void CommitLayout(Anchor anchor)
        {
            if (!expanded || autoHeight)
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Layout.ContentHeight);
            if (expanded)
            {
                expandedDirty = true;
                RefreshVisibleCore();
                OnLayoutGeometryCommitted();
                NotifyContentSize();
                return;
            }

            float y = anchor.Absolute;
            if (anchor.Key != null && Layout.TryGetKey(anchor.Key, out var entry))
                y = entry.Rect.yMin - anchor.Offset;
            else if (anchor.Index >= 0 && Layout.Entries.Count > 0)
                y = Layout.Entries[Mathf.Min(anchor.Index, Layout.Entries.Count - 1)].Rect.yMin - anchor.Offset;
            float bottom = Mathf.Max(0, Layout.ContentHeight - scroll.viewport.rect.height);
            // 下拉和回弹中的越界量不能在数据提交瞬间被钳成零，否则会出现单帧闪跳。
            if (scroll.movementType != ScrollRect.MovementType.Clamped && anchor.Absolute < 0)
                y = anchor.Absolute;
            else if (scroll.movementType != ScrollRect.MovementType.Clamped && anchor.Absolute > anchor.Bottom)
                y = Mathf.Min(anchor.Absolute, bottom + anchor.Absolute - anchor.Bottom);
            else
                y = Mathf.Clamp(y, 0, bottom);
            content.anchoredPosition = new Vector2(0, y);
            RebaseScrollAfterLayout(anchor.Velocity);
            RefreshVisibleCore();
            OnLayoutGeometryCommitted();
            NotifyContentSize();
        }

        void NotifyContentSize()
        {
            var size = new Vector2(Layout.ContentWidth, Layout.ContentHeight);
            if (hasNotifiedContentSize && notifiedContentSize.Equals(size))
                return;
            // 先记录再调用，异常不会让同一尺寸在后续提交重复通知；applying 在整个回调期间保持为 true。
            hasNotifiedContentSize = true;
            notifiedContentSize = size;
            ContentSizeChanged?.Invoke(size);
        }

        void SetOffset(float y)
        {
            scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(y, 0, Mathf.Max(0, Layout.ContentHeight - scroll.viewport.rect.height)));
            RebaseScrollAfterLayout(Vector2.zero);
        }

        /// <summary>滚动到指定业务项；alignment 为 0 时靠顶部、1 时靠底部。Expanded 无滚动位置，此调用不做操作。</summary>
        public void ScrollToItem(string id, float alignment = 0)
        {
            if (refreshing || applying)
                throw new InvalidOperationException("Scroll outside binding and measurement callbacks.");
            CancelSectionNavigation();
            if (expanded || Layout == null || !Layout.TryGetItem(id, out var entry))
                return;
            GetComponent<CollectionLoader>()?.ReleaseScrollHold();
            scroll.StopMovement();
            SetOffset(entry.Rect.yMin - (scroll.viewport.rect.height - entry.Rect.height) * Mathf.Clamp01(alignment));
            RefreshVisible();
        }

        /// <summary>Scroll 返回真实视口内的 item ID（不含 Overscan 和分组头尾）；Expanded 返回所有 item ID，
        /// 不检测祖先遮罩是否遮住它们。每次调用会创建结果列表，可在绑定回调中读取。</summary>
        public List<string> GetVisibleItems()
        {
            var result = new List<string>();
            if (Layout == null)
                return result;
            if (expanded)
            {
                foreach (var entry in Layout.Entries)
                    if (entry.ItemIndex >= 0)
                        result.Add(entry.Item.Id);
                return result;
            }

            var visible = new List<int>(); // 独立缓冲区，避免 OnBind / OnUnbind 读取可见项时破坏正在进行的复用迭代。
            Layout.Query(new Rect(0, scroll.content.anchoredPosition.y, layoutWidth, scroll.viewport.rect.height), visible);
            for (int i = 0; i < visible.Count; i++)
                if (Layout.Entries[visible[i]].ItemIndex >= 0)
                    result.Add(Layout.Entries[visible[i]].Item.Id);
            return result;
        }

        void OnScroll(Vector2 unused)
        {
            RefreshVisible();
        }

        /// <summary>按当前滚动位置同步刷新 Cell；Expanded 无脏标记时跳过，数据提交会自动设置脏标记。</summary>
        public void RefreshVisible()
        {
            if (applying)
                return;
            RefreshVisibleCore();
        }

        void RefreshVisibleCore()
        {
            if (Layout == null || refreshing || !isActiveAndEnabled || (expanded && !expandedDirty))
                return;
            Rect region = expanded ? default : new Rect(0, content.anchoredPosition.y - overscan, layoutWidth, scroll.viewport.rect.height + overscan * 2);
            if (!expanded && !expandedDirty && pendingBindings.Count == 0 && pendingLayouts.Count == 0 && hasRefreshedRegion && ReferenceEquals(refreshedLayout, Layout) && refreshedRegion == region)
                return;
            // 只有完整刷新成功才缓存区域；回调异常后同一区域仍需重试。
            hasRefreshedRegion = false;
            expandedDirty = true;
            refreshing = true;
            VisibleRefreshCount++;
            long start = Stopwatch.GetTimestamp();
            try
            {
                using (RefreshMarker.Auto())
                {
                    if (expanded)
                    {
                        query.Clear();
                        for (int i = 0; i < Layout.Entries.Count; i++)
                            query.Add(i);
                    }
                    else
                    {
                        Layout.Query(region, query);
                    }

                    // 先收集目标索引，再回收离开范围的实例，最后补齐进入范围的实例。
                    // 复用容器避免滚动过程为每个 Cell 创建临时集合。
                    wanted.Clear();
                    for (int i = 0; i < query.Count; i++)
                        wanted.Add(query[i]);
                    removals.Clear();
                    foreach (var pair in active)
                        if (!wanted.Contains(pair.Key))
                            removals.Add(pair.Key);
                    for (int i = 0; i < removals.Count; i++)
                    {
                        int index = removals[i];
                        var removed = active[index];
                        active.Remove(index);
                        pendingBindings.Remove(index);
                        pendingLayouts.Remove(index);
                        Return(removed, Layout.Entries[index].Item.ReuseIdentifier);
                    }

                    for (int i = 0; i < query.Count; i++)
                    {
                        int index = query[i];
                        var entry = Layout.Entries[index];
                        if (active.TryGetValue(index, out var existing))
                        {
                            bool layoutPending = pendingLayouts.Contains(index);
                            bool bindingPending = pendingBindings.Contains(index);
                            try
                            {
                                if (layoutPending)
                                    SetCellFrame(existing, entry);
                                if (bindingPending)
                                    existing.Bind(entry);
                                else if (layoutPending)
                                    existing.UpdateLayout(entry);
                                pendingLayouts.Remove(index);
                                pendingBindings.Remove(index);
                            }
                            catch
                            {
                                active.Remove(index);
                                hasRefreshedRegion = false;
                                expandedDirty = true;
                                DiscardFailedCell(existing);
                                throw;
                            }

                            continue;
                        }

                        var cell = Take(entry.Item.ReuseIdentifier);
                        try
                        {
                            SetCellFrame(cell, entry);
                            cell.Bind(entry);
                            cell.gameObject.SetActive(true);
                            active.Add(index, cell);
                            pendingLayouts.Remove(index);
                            pendingBindings.Remove(index);
                        }
                        catch
                        {
                            DiscardFailedCell(cell);
                            throw;
                        }
                    }

                    expandedDirty = false;
                    refreshedLayout = Layout;
                    refreshedRegion = region;
                    hasRefreshedRegion = true;
                }
            }
            finally
            {
                refreshing = false;
                LastRefreshMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
        }

        static void SetCellFrame(CollectionCell cell, LayoutEntry entry)
        {
            var rt = (RectTransform)cell.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = entry.Rect.size;
            rt.anchoredPosition = new Vector2(entry.Rect.x, -entry.Rect.y);
        }

        bool RequiresBinding(LayoutEntry old, LayoutEntry next, HashSet<string> forced)
        {
            return old == null || sizesDirty || (forced != null && forced.Contains(next.Key)) || (next.Kind == ElementKind.Item && dirtyItems.Contains(next.Item.Id)) || old.Item.ReuseIdentifier != next.Item.ReuseIdentifier || old.Item.Version != next.Item.Version || !ReferenceEquals(old.Item.Data, next.Item.Data) || !SameSize(old.Item.Size, next.Item.Size);
        }

        static bool SameSize(ItemSize a, ItemSize b) => a.Mode == b.Mode && a.Width == b.Width && a.Height == b.Height && a.AspectRatio == b.AspectRatio;
        Dictionary<string, long> BuildPresentationRevisions(LayoutResult next, HashSet<string> forced)
        {
            var revisions = new Dictionary<string, long>(next.Entries.Count, StringComparer.Ordinal);
            foreach (var entry in next.Entries)
            {
                LayoutEntry old = null;
                if (Layout != null)
                    Layout.TryGetKey(entry.Key, out old);
                bool changed = RequiresBinding(old, entry, forced);
                revisions.Add(entry.Key, !changed && presentationRevisions.TryGetValue(entry.Key, out var revision) ? revision : presentationRevision + 1);
            }

            return revisions;
        }

        // 先按稳定身份重建索引；几何提交后再刷新绑定，避免回调看到旧帧和新布局混杂。
        void ReconcileActive(LayoutResult next, HashSet<string> forced = null)
        {
            if (!isActiveAndEnabled)
            {
                RecycleAll();
                return;
            }

            var retained = new Dictionary<int, CollectionCell>();
            var bindings = new HashSet<int>();
            var layouts = new HashSet<int>();
            foreach (var pair in new List<KeyValuePair<int, CollectionCell>>(active))
            {
                var old = Layout.Entries[pair.Key];
                if (next.TryGetIndex(old.Key, out int index) && next.Entries[index].Item.ReuseIdentifier == old.Item.ReuseIdentifier)
                {
                    retained.Add(index, pair.Value);
                    var entry = next.Entries[index];
                    if (pendingLayouts.Contains(pair.Key) || old.Rect != entry.Rect || old.SectionIndex != entry.SectionIndex || old.ItemIndex != entry.ItemIndex || old.Kind != entry.Kind || old.SectionId != entry.SectionId)
                        layouts.Add(index);
                    if (pendingBindings.Contains(pair.Key) || RequiresBinding(old, next.Entries[index], forced))
                        bindings.Add(index);
                }
                else
                {
                    active.Remove(pair.Key);
                    pendingBindings.Remove(pair.Key);
                    pendingLayouts.Remove(pair.Key);
                    try
                    {
                        Return(pair.Value, old.Item.ReuseIdentifier);
                    }
                    catch
                    {
                        hasRefreshedRegion = false;
                        expandedDirty = true;
                        throw;
                    }
                }
            }

            active.Clear();
            pendingBindings.Clear();
            pendingLayouts.Clear();
            foreach (var pair in retained)
            {
                active.Add(pair.Key, pair.Value);
            }

            pendingLayouts.UnionWith(layouts);
            pendingBindings.UnionWith(bindings);
        }

        // 优先使用同模板闲置实例，只有池为空才 Instantiate；子矩形由统一布局负责。
        CollectionCell Take(string id)
        {
            var pool = pools[id];
            CollectionCell cell;
            if (pool.Count > 0)
                cell = pool.Pop();
            else
            {
                cell = Instantiate(prefabs[id], poolRoot);
                cell.gameObject.SetActive(false);
                CreatedCellCount++;
            }

            cell.transform.SetParent(content, false);
            return cell;
        }

        // Release 先取消绑定及异步任务，再隐藏归池；池上限仅限制闲置实例。
        void Return(CollectionCell cell, string id)
        {
            try
            {
                cell.Release();
            }
            catch
            {
                cell.gameObject.SetActive(false);
                Destroy(cell.gameObject);
                throw;
            }

            cell.gameObject.SetActive(false);
            cell.transform.SetParent(poolRoot, false);
            if (pools[id].Count < maxPooledPerTemplate)
                pools[id].Push(cell);
            else
                Destroy(cell.gameObject);
        }

        // 清理失败的业务绑定，清理异常不得覆盖最初的业务异常；坏实例不再进入池。
        void DiscardFailedCell(CollectionCell cell)
        {
            try
            {
                cell.Release();
            }
            catch
            {
            }

            cell.gameObject.SetActive(false);
            Destroy(cell.gameObject);
        }

        void RecycleAll()
        {
            if (Layout == null)
                return;
            Exception failure = null;
            foreach (var pair in active)
                if (pair.Value != null)
                {
                    try
                    {
                        Return(pair.Value, Layout.Entries[pair.Key].Item.ReuseIdentifier);
                    }
                    catch (Exception exception)
                    {
                        if (failure == null)
                            failure = exception;
                    }
                }

            active.Clear();
            pendingBindings.Clear();
            pendingLayouts.Clear();
            if (failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
