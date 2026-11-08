// 普通节点完整展开示例。Flow 可切换数据量和高度自适应；列布局使用外部固定高度。一个控制器统一维护相邻区域的位置。
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Samples
{
    /// <summary>两个普通 UI 宿主，兄弟节点之间的位置关系由此演示脚本统一维护。</summary>
    public sealed class ExpandedCollectionDemo : MonoBehaviour
    {
        public const int CurrentSchema = 1;
        [SerializeField]
        int schema = CurrentSchema;
        [SerializeField]
        CollectionView flow, columns;
        [SerializeField]
        RectTransform flowTitle, columnsTitle;
        [SerializeField]
        Text statistics, heightLabel, countLabel;
        [SerializeField]
        Button changeCount, changeHeight;
        Canvas canvas;
        float scale = -1;
        int count = 8;
        public int Schema => schema;
        public CollectionView Flow => flow;
        public CollectionView Columns => columns;
        public int Count => count;

        public void Configure(CollectionView first, CollectionView second, RectTransform firstTitle, RectTransform secondTitle, Text stats, Button countButton, Button heightButton)
        {
            UnsubscribeSizeChanges();
            flow = first;
            columns = second;
            flowTitle = firstTitle;
            columnsTitle = secondTitle;
            statistics = stats;
            changeCount = countButton;
            changeHeight = heightButton;
            countLabel = countButton.GetComponentInChildren<Text>();
            heightLabel = heightButton.GetComponentInChildren<Text>();
            if (isActiveAndEnabled)
                SubscribeSizeChanges();
        }

        void OnEnable()
        {
            SubscribeSizeChanges();
            if (flow != null)
                Arrange();
        }

        void OnDisable()
        {
            UnsubscribeSizeChanges();
        }

        // 尺寸事件通知外层排版，不需要每帧重复设置兄弟节点的位置和说明文字。
        void SubscribeSizeChanges()
        {
            UnsubscribeSizeChanges();
            if (flow != null)
                flow.ContentSizeChanged += OnContentSizeChanged;
            if (columns != null)
                columns.ContentSizeChanged += OnContentSizeChanged;
        }

        void UnsubscribeSizeChanges()
        {
            if (flow != null)
                flow.ContentSizeChanged -= OnContentSizeChanged;
            if (columns != null)
                columns.ContentSizeChanged -= OnContentSizeChanged;
        }

        void OnContentSizeChanged(Vector2 size)
        {
            // 回调中只调整外层布局，不向当前集合再次提交快照或使尺寸失效。
            Arrange();
        }

        void Start()
        {
            changeCount.onClick.AddListener(ToggleCount);
            changeHeight.onClick.AddListener(ToggleHeight);
            Load(count);
        }

        void OnDestroy()
        {
            UnsubscribeSizeChanges();
            if (changeCount != null)
                changeCount.onClick.RemoveListener(ToggleCount);
            if (changeHeight != null)
                changeHeight.onClick.RemoveListener(ToggleHeight);
        }

        public void ToggleCount()
        {
            Load(count == 8 ? 4 : 8);
        }

        /// <summary>关闭后保留当前高度；重新开启会立即采用现有布局的内容高度。</summary>
        public void ToggleHeight()
        {
            flow.AutoHeight = !flow.AutoHeight;
            Arrange();
        }

        /// <summary>业务先提供测量规则和快照；普通模式会生成全部元素，因此示例使用少量标签和卡片。</summary>
        public void Load(int itemCount)
        {
            count = itemCount;
            Canvas.ForceUpdateCanvases();
            canvas = GetComponent<Canvas>();
            scale = canvas.scaleFactor;
            flow.InvalidateStyle();
            flow.MeasureItem = DemoContent.Measure;
            var data = new CollectionSnapshot();
            var measurements = DemoContent.ForScale(scale);
            string[] names =
            {
                "New",
                "Outdoor",
                "Books",
                "Music",
                "Home & living",
                "Travel essentials",
                "Weekend",
                "A deliberately long single-line label is measured from its full content and shortened at a complete text element to fit the available width, without changing its font size or overflowing the collection host"
            };
            for (int s = 0; s < 2; s++)
            {
                var section = new CollectionSection
                {
                    Id = "group-" + s,
                    Layout = new SectionLayoutSettings
                    {
                        Mode = LayoutMode.Flow,
                        Insets = new Insets(12, 12, 12, 12),
                        HorizontalSpacing = 16,
                        VerticalSpacing = 12,
                        SectionSpacing = 16
                    }
                };
                section.Header = new CollectionItem
                {
                    Id = "header",
                    ReuseIdentifier = "header",
                    Size = ItemSize.Fixed(0, 56),
                    Data = new DemoCard
                    {
                        Title = "GROUP " + (s + 1)
                    }
                };
                for (int i = s * count / 2; i < (s + 1) * count / 2; i++)
                    section.Items.Add(new CollectionItem { Id = "tag-" + i, ReuseIdentifier = "tag", Size = ItemSize.Measured(), Data = new TagData { Text = names[i % names.Length], Measurements = measurements } });
                data.Sections.Add(section);
            }

            flow.ApplySnapshot(data);
            var cards = new CollectionSnapshot();
            var group = new CollectionSection
            {
                Id = "cards",
                Layout = new SectionLayoutSettings
                {
                    Mode = LayoutMode.Columns,
                    ColumnCount = 2,
                    Insets = new Insets(12, 12, 12, 12),
                    HorizontalSpacing = 16,
                    VerticalSpacing = 12
                }
            };
            for (int i = 0; i < 2; i++)
                group.Items.Add(new CollectionItem { Id = "card-" + i, ReuseIdentifier = "card", Size = ItemSize.Fixed(0, i % 2 == 0 ? 96 : 144), Data = new DemoCard { Title = "CELL " + (i + 1) } });
            cards.Sections.Add(group);
            columns.ApplySnapshot(cards);
            Arrange();
        }

        void LateUpdate()
        {
            if (flow.Layout == null)
                return;
            if (canvas != null && !Mathf.Approximately(canvas.scaleFactor, scale))
                Load(count);
        }

        /// <summary>即使关闭 AutoHeight，也为实际渲染范围保留空间；固定高度并不自动裁剪超出的 Cell。</summary>
        public void Arrange()
        {
            if (flow.Layout == null)
                return;
            Position(flowTitle, 0);
            var first = (RectTransform)flow.transform;
            Position(first, flowTitle.rect.height + 8);
            float next = flowTitle.rect.height + 8 + Mathf.Max(first.rect.height, flow.Layout.ContentHeight) + 24;
            Position(columnsTitle, next);
            Position((RectTransform)columns.transform, next + columnsTitle.rect.height + 8);
            statistics.text = "Ordinary nodes | " + count + " tags / 2 groups | All cells instantiated\nFlow height " + first.rect.height.ToString("0") + " / content " + flow.Layout.ContentHeight.ToString("0");
            heightLabel.text = "AUTO HEIGHT: " + (flow.AutoHeight ? "ON" : "OFF");
            countLabel.text = count == 8 ? "SHOW 4 TAGS" : "SHOW 8 TAGS";
        }

        static void Position(RectTransform rect, float top)
        {
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -top);
        }
    }
}
