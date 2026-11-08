// 滚动演示控制器。切换四种布局场景和千/万条数据，自动滚动验证虚拟化；显示的耗时是 同步方法耗时，不是 FPS。
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Samples
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UniversalWaterfall.Samples", "UniversalWaterfall.Samples", "WaterfallDemo")]
    public sealed class CollectionViewDemo : MonoBehaviour
    {
        public const int CurrentSchema = 4;
        [SerializeField]
        int schema = CurrentSchema;
        [SerializeField]
        CollectionView collection;
        [SerializeField]
        Text statistics, modeLabel, autoLabel;
        [SerializeField]
        Button thousand, tenThousand, autoScroll, toggleMode;
        public int Schema => schema;
        public CollectionView Collection => collection;
        public bool AutoScroll { get; set; }
        public int Count { get; private set; } = 1000;
        public DemoScenario Scenario { get; private set; } = DemoScenario.Tags;
        public LayoutMode Mode => Scenario == DemoScenario.Tags ? LayoutMode.Flow : Scenario == DemoScenario.FreeRectangles ? LayoutMode.Skyline : LayoutMode.Columns;

        float nextStatistics, direction = 1, measurementScale = -1;
        Canvas hostCanvas;
        /// <summary>供 Seeder 连接序列化引用。控件事件在 Start 注册，在销毁时解除。</summary>
        public void Configure(CollectionView view, Text stats, Button small, Button large, Button auto, Button mode)
        {
            collection = view;
            statistics = stats;
            thousand = small;
            tenThousand = large;
            autoScroll = auto;
            toggleMode = mode;
            modeLabel = mode.GetComponentInChildren<Text>();
            autoLabel = auto.GetComponentInChildren<Text>();
        }

        void Start()
        {
            thousand.onClick.AddListener(LoadThousand);
            tenThousand.onClick.AddListener(LoadTenThousand);
            autoScroll.onClick.AddListener(ToggleAuto);
            toggleMode.onClick.AddListener(SwitchMode);
            Load(Count);
        }

        void OnDestroy()
        {
            if (thousand != null)
                thousand.onClick.RemoveListener(LoadThousand);
            if (tenThousand != null)
                tenThousand.onClick.RemoveListener(LoadTenThousand);
            if (autoScroll != null)
                autoScroll.onClick.RemoveListener(ToggleAuto);
            if (toggleMode != null)
                toggleMode.onClick.RemoveListener(SwitchMode);
        }

        void LoadThousand()
        {
            Load(1000);
        }

        void LoadTenThousand()
        {
            Load(10000);
        }

        void ToggleAuto()
        {
            AutoScroll = !AutoScroll;
            autoLabel.text = AutoScroll ? "STOP SCROLL" : "AUTO SCROLL";
        }

        void SwitchMode()
        {
            SetScenario((DemoScenario)(((int)Scenario + 1) % 4));
        }

        public void SetMode(LayoutMode mode)
        {
            SetScenario(mode == LayoutMode.Flow ? DemoScenario.Tags : mode == LayoutMode.Columns ? DemoScenario.Columns : DemoScenario.FreeRectangles);
        }

        public void SetScenario(DemoScenario scenario)
        {
            Scenario = scenario;
            Load(Count);
        }

        public static string ScenarioName(DemoScenario scenario)
        {
            switch (scenario)
            {
                case DemoScenario.Tags:
                    return "TAGS / FLOW";
                case DemoScenario.Products:
                    return "PRODUCTS / MEASURED";
                case DemoScenario.Columns:
                    return "FIXED COLUMNS";
                default:
                    return "FREE RECTANGLES";
            }
        }

        /// <summary>重新构建演示数据；先更新 Canvas，再使用匹配像素比例的测量器，避免缩放后尺寸与文字不一致。</summary>
        public void Load(int count)
        {
            Count = count;
            Canvas.ForceUpdateCanvases();
            if (hostCanvas == null)
                hostCanvas = collection.GetComponentInParent<Canvas>();
            float scale = hostCanvas == null ? 1 : hostCanvas.scaleFactor;
            if (!Mathf.Approximately(scale, measurementScale))
            {
                collection.InvalidateStyle();
                measurementScale = scale;
            }

            collection.MeasureItem = DemoContent.Measure;
            collection.ApplySnapshot(DemoContent.Create(count, Scenario, DemoContent.ForScale(scale)));
            modeLabel.text = ScenarioName(Scenario) + "  >  " + ScenarioName((DemoScenario)(((int)Scenario + 1) % 4));
            UpdateStatistics();
        }

        /// <summary>生成可重复的分组几何数据。特意加入超高项，验证其顶部离开视口后仍能被可见性索引命中。</summary>
        public static CollectionSnapshot CreateData(int count, LayoutMode mode)
        {
            if (mode == LayoutMode.Flow)
                return DemoContent.Create(count, DemoScenario.Tags);
            var data = new CollectionSnapshot();
            for (int s = 0; s < 5; s++)
            {
                var section = new CollectionSection
                {
                    Id = "section-" + s
                };
                section.Layout = new SectionLayoutSettings
                {
                    Mode = mode,
                    ColumnCount = 3,
                    Insets = new Insets(16, 16, 16, 16),
                    HorizontalSpacing = 16,
                    VerticalSpacing = 20,
                    SectionSpacing = 32
                };
                section.Header = new CollectionItem
                {
                    Id = "header",
                    ReuseIdentifier = "header",
                    Size = ItemSize.Fixed(0, 88),
                    Data = new DemoCard
                    {
                        Title = "SECTION " + (s + 1) + "  /  " + (mode == LayoutMode.Skyline ? "FREE RECTANGLES" : "SHORTEST COLUMN")
                    }
                };
                section.Footer = new CollectionItem
                {
                    Id = "footer",
                    ReuseIdentifier = "header",
                    Size = ItemSize.Fixed(0, 56),
                    Data = new DemoCard
                    {
                        Title = "End of section " + (s + 1)
                    }
                };
                int from = count * s / 5, to = count * (s + 1) / 5;
                for (int i = from; i < to; i++)
                {
                    // 独立指定宽高；这些演示宽度均可放入 360 UI 单位宽的视口。
                    float w = mode == LayoutMode.Columns ? 0 : 100 + (i * 37 % 181);
                    float h = 112 + (i * 71 % 229);
                    if (i == 13)
                        h = 1400; // 超高 Cell：顶部离开视口后仍可能可见，用于验证区间查询。
                    section.Items.Add(new CollectionItem { Id = "item-" + i, ReuseIdentifier = "card", Size = ItemSize.Fixed(w, h), Data = new DemoCard { Title = "CARD " + i } });
                }

                data.Sections.Add(section);
            }

            return data;
        }

        /// <summary>自动滚动只改变 Content 偏移，不逐帧重建数据；样式缩放改变时才重新测量。</summary>
        void Update()
        {
            if (collection == null || collection.Layout == null)
                return;
            if (hostCanvas != null && !Mathf.Approximately(hostCanvas.scaleFactor, measurementScale))
                Load(Count);
            if (AutoScroll)
            {
                var scroll = collection.ScrollRect;
                float max = Mathf.Max(0, collection.Layout.ContentHeight - scroll.viewport.rect.height);
                float y = scroll.content.anchoredPosition.y + direction * 900 * Time.unscaledDeltaTime;
                if (y >= max)
                {
                    y = max;
                    direction = -1;
                }
                else if (y <= 0)
                {
                    y = 0;
                    direction = 1;
                }

                scroll.StopMovement();
                scroll.content.anchoredPosition = new Vector2(0, y);
            }

            if (Time.unscaledTime >= nextStatistics)
            {
                nextStatistics = Time.unscaledTime + 0.5f;
                UpdateStatistics();
            }
        }

        void UpdateStatistics()
        {
            if (statistics == null)
                return;
            statistics.text = Count.ToString("N0") + " items   |   5 sections   |   Active " + collection.ActiveCellCount + " / pooled " + collection.PooledCellCount + "\nLayout " + collection.LastLayoutMilliseconds.ToString("F2") + " ms   |   Visible refresh " + collection.LastRefreshMilliseconds.ToString("F3") + " ms";
        }
    }
}
