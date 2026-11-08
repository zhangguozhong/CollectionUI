using System;
using System.Collections.Generic;
using UnityEngine;

namespace CollectionUI
{
    /// <summary>分区内部的排列策略：等宽列、天际线紧凑放置或按输入顺序换行。</summary>
    public enum LayoutMode
    {
        Columns = 0,
        Skyline = 1,
        Flow = 2
    }

    /// <summary>分区的水平排列方向；只影响元素位置，不改变数据顺序或文字排版。</summary>
    public enum LayoutDirection
    {
        LeftToRight = 0,
        RightToLeft = 1
    }

    /// <summary>布局元素的角色；页眉、页脚与普通条目使用不同的标识规则。</summary>
    public enum ElementKind
    {
        Item,
        Header,
        Footer
    }

    /// <summary>尺寸来源：固定尺寸、宽高比或外部测量回调。</summary>
    public enum ItemSizeMode
    {
        Fixed,
        Aspect,
        Measured
    }

    /// <summary>布局区域四边的内边距，单位与内容坐标一致。</summary>
    [Serializable]
    public struct Insets
    {
        /// <summary>左、上、右、下内边距；布局计算要求各值有限且非负。</summary>
        public float Left, Top, Right, Bottom;
        /// <summary>按左、上、右、下的顺序初始化内边距。</summary>
        public Insets(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    /// <summary>条目的尺寸描述；零宽度表示采用当前列或分区的可用宽度。</summary>
    [Serializable]
    public struct ItemSize
    {
        /// <summary>决定布局引擎如何解析尺寸。</summary>
        public ItemSizeMode Mode;
        /// <summary>期望宽度、固定高度以及宽除以高的比例；仅当前模式需要的字段参与计算。</summary>
        public float Width, Height, AspectRatio;
        /// <summary>创建固定尺寸描述；零宽度采用当前列或分区宽度，高度必须为正数。</summary>
        public static ItemSize Fixed(float width, float height) => new ItemSize
        {
            Mode = ItemSizeMode.Fixed,
            Width = width,
            Height = height
        };
        /// <summary>创建按宽高比计算高度的尺寸描述；比例必须为有限正数。</summary>
        public static ItemSize Aspect(float width, float widthOverHeight) => new ItemSize
        {
            Mode = ItemSizeMode.Aspect,
            Width = width,
            AspectRatio = widthOverHeight
        };
        /// <summary>创建由测量回调返回实际尺寸的描述；零宽度把可用宽度传给回调。</summary>
        public static ItemSize Measured(float width = 0) => new ItemSize
        {
            Mode = ItemSizeMode.Measured,
            Width = width
        };
    }

    /// <summary>单个条目的身份、复用类型、尺寸与业务数据；发布到布局结果后元数据被冻结。</summary>
    [Serializable]
    public sealed class CollectionItem
    {
        [SerializeField]
        string id;
        [SerializeField]
        string reuseIdentifier;
        [SerializeField]
        int version;
        [SerializeField]
        ItemSize size;
        [NonSerialized]
        object data;
        [NonSerialized]
        bool frozen;
        string layoutKey;
        /// <summary>条目标识；普通条目必须在整个快照中唯一，页眉和页脚按分区区分。</summary>
        public string Id
        {
            get => id;
            set
            {
                EnsureMutable();
                id = value;
            }
        }

        /// <summary>用于选择单元格注册信息和复用池的非空标识。</summary>
        public string ReuseIdentifier
        {
            get => reuseIdentifier;
            set
            {
                EnsureMutable();
                reuseIdentifier = value;
            }
        }

        /// <summary>内容版本；业务数据影响尺寸时递增此值，使布局比较识别变化。</summary>
        public int Version
        {
            get => version;
            set
            {
                EnsureMutable();
                version = value;
            }
        }

        /// <summary>条目尺寸描述，实际几何尺寸由布局构建时解析。</summary>
        public ItemSize Size
        {
            get => size;
            set
            {
                EnsureMutable();
                size = value;
            }
        }

        /// <summary>发布后不能替换此引用；业务数据对象自身仍由调用方持有并负责管理。</summary>
        public object Data
        {
            get => data;
            set
            {
                EnsureMutable();
                data = value;
            }
        }

        /// <summary>快照复制或布局校验时生成的测量身份；复制单条目时不会保留此值。</summary>
        public string LayoutKey
        {
            get => layoutKey;
            internal set
            {
                EnsureMutable();
                layoutKey = value;
            }
        }

        internal void Freeze()
        {
            frozen = true;
        }

        internal bool IsFrozen => frozen;

        // 身份不变的已发布普通条目可跨分区共享；其 ID 和全局布局键均已冻结。
        internal CollectionItem PrepareLayoutKey(ElementKind kind, string sectionId)
        {
            if (frozen && kind == ElementKind.Item && !string.IsNullOrEmpty(layoutKey) && layoutKey[0] == 'I')
                return this;
            string key = LayoutEntry.MakeKey(kind, sectionId, id);
            if (layoutKey == key)
                return this;
            // 页眉/页脚换分区或角色时必须复制，不能改写历史布局持有的身份。
            var item = frozen ? Copy() : this;
            item.LayoutKey = key;
            return item;
        }

        void EnsureMutable()
        {
            if (frozen)
                throw new InvalidOperationException("Published layout items are immutable. Modify a copy and apply a new snapshot.");
        }

        /// <summary>复制可修改的条目元数据；业务数据保留原引用，不复制其内部状态。</summary>
        public CollectionItem Copy() => new CollectionItem
        {
            Id = Id,
            ReuseIdentifier = ReuseIdentifier,
            Version = Version,
            Data = Data,
            Size = Size
        };
    }

    /// <summary>单个分区的排列方式、内边距及元素间距。</summary>
    [Serializable]
    public sealed class SectionLayoutSettings
    {
        /// <summary>分区条目的排列策略。</summary>
        public LayoutMode Mode = LayoutMode.Columns;
        /// <summary>水平排列起点；默认从左向右，RTL 从右侧开始，左右内边距仍保持物理含义。</summary>
        [Tooltip("水平排列方向：LeftToRight 从左开始，RightToLeft 从右开始；不改变数据顺序、文字方向或左右内边距。")]
        public LayoutDirection Direction = LayoutDirection.LeftToRight;
        /// <summary>等宽列模式使用的列数；所有模式均要求该值大于零。</summary>
        public int ColumnCount = 2;
        /// <summary>分区外围内边距，页眉、条目及页脚均位于其内部。</summary>
        public Insets Insets;
        /// <summary>水平元素间距、竖直元素间距，以及本分区与下一个显示分区之间的间距。</summary>
        public float HorizontalSpacing, VerticalSpacing, SectionSpacing;
        /// <summary>是否保留没有普通条目的分区；关闭时该分区的页眉、页脚及内边距也被跳过。</summary>
        public bool ShowEmptySection;
        /// <summary>复制分区配置；当前字段均为值类型，因此浅复制即可隔离配置修改。</summary>
        public SectionLayoutSettings Copy() => (SectionLayoutSettings)MemberwiseClone();
    }

    /// <summary>有序分区，包含可选页眉、普通条目列表、可选页脚及独立布局配置。</summary>
    [Serializable]
    public sealed class CollectionSection
    {
        /// <summary>快照内唯一且非空的分区标识。</summary>
        public string Id;
        /// <summary>可选页眉和页脚；为空时不生成对应布局元素。</summary>
        public CollectionItem Header, Footer;
        /// <summary>按输入顺序保存普通条目，不允许列表或其中的条目为空。</summary>
        public List<CollectionItem> Items = new List<CollectionItem>();
        /// <summary>本分区的布局配置，不允许为空。</summary>
        public SectionLayoutSettings Layout = new SectionLayoutSettings();
    }

    /// <summary>一次完整的数据快照；布局引擎复制元数据后再进行校验和计算。</summary>
    [Serializable]
    public sealed class CollectionSnapshot
    {
        /// <summary>按显示顺序排列的分区；列表及分区对象均不允许为空。</summary>
        public List<CollectionSection> Sections = new List<CollectionSection>();
        /// <summary>复制所有布局元数据及列表，隔离调用方后续修改；Data 业务对象仍保留调用方拥有的原引用。</summary>
        public CollectionSnapshot Copy() => CopyCore(false);
        // 事务只共享已冻结的元数据，分区、配置和列表仍独立；公开 Copy 始终返回可修改副本。
        internal CollectionSnapshot CopyForTransaction() => CopyCore(true);
        CollectionSnapshot CopyCore(bool shareFrozen)
        {
            var copy = new CollectionSnapshot();
            if (Sections == null)
                throw new ArgumentException("Sections cannot be null.");
            foreach (var section in Sections)
            {
                if (section == null || section.Layout == null || section.Items == null)
                    throw new ArgumentException("Sections, settings and item lists cannot be null.");
                var target = new CollectionSection
                {
                    Id = section.Id,
                    Header = CopyItem(section.Header, shareFrozen),
                    Footer = CopyItem(section.Footer, shareFrozen),
                    Layout = section.Layout.Copy(),
                    Items = new List<CollectionItem>(section.Items.Count)
                };
                foreach (var item in section.Items)
                {
                    if (item == null)
                        throw new ArgumentException("Items cannot be null.");
                    target.Items.Add(CopyItem(item, shareFrozen));
                }

                // 为外部布局引擎补齐测量键，使其与内置布局使用一致的元素身份规则。
                if (!string.IsNullOrEmpty(target.Id))
                {
                    if (target.Header != null && !string.IsNullOrEmpty(target.Header.Id))
                        target.Header = target.Header.PrepareLayoutKey(ElementKind.Header, target.Id);
                    if (target.Footer != null && !string.IsNullOrEmpty(target.Footer.Id))
                        target.Footer = target.Footer.PrepareLayoutKey(ElementKind.Footer, target.Id);
                    for (int i = 0; i < target.Items.Count; i++)
                        if (!string.IsNullOrEmpty(target.Items[i].Id))
                            target.Items[i] = target.Items[i].PrepareLayoutKey(ElementKind.Item, target.Id);
                }

                copy.Sections.Add(target);
            }

            return copy;
        }

        static CollectionItem CopyItem(CollectionItem item, bool shareFrozen) => item == null ? null : shareFrozen && item.IsFrozen ? item : item.Copy();
    }

    /// <summary>已发布的单个元素几何信息，附带冻结的条目元数据和所属分区位置。</summary>
    public sealed class LayoutEntry
    {
        internal LayoutEntry()
        {
        }

        /// <summary>供自定义布局发布元素；先校验标识、索引和矩形，再复制并冻结元数据。矩形 Y 轴向下为正。</summary>
        public LayoutEntry(CollectionItem item, string sectionId, int sectionIndex, int itemIndex, ElementKind kind, Rect rect)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.ReuseIdentifier) || string.IsNullOrEmpty(sectionId))
                throw new ArgumentException("Item, reuse and section identifiers must be nonempty.");
            if (sectionIndex < 0 || (kind == ElementKind.Item && itemIndex < 0) || (kind == ElementKind.Header && itemIndex != -1) || (kind == ElementKind.Footer && itemIndex != -2))
                throw new ArgumentException("Invalid section or item index.");
            if (kind != ElementKind.Item && kind != ElementKind.Header && kind != ElementKind.Footer)
                throw new ArgumentException("Unknown element kind.");
            if (!Finite(rect.x) || !Finite(rect.y) || !Finite(rect.width) || !Finite(rect.height) || !Finite(rect.xMax) || !Finite(rect.yMax) || rect.x < 0 || rect.y < 0 || rect.width <= 0 || rect.height <= 0)
                throw new ArgumentException("Entry rect must have finite nonnegative coordinates and positive dimensions.");
            Item = item.Copy();
            Item.LayoutKey = MakeKey(kind, sectionId, item.Id);
            Item.Freeze();
            SectionId = sectionId;
            SectionIndex = sectionIndex;
            ItemIndex = itemIndex;
            Kind = kind;
            Rect = rect;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        /// <summary>冻结的条目元数据；其中的业务数据对象仍由调用方管理。</summary>
        public CollectionItem Item { get; internal set; }
        /// <summary>所属分区的稳定标识。</summary>
        public string SectionId { get; internal set; }
        /// <summary>所属分区在输入快照中的索引，包含被跳过的空分区。</summary>
        public int SectionIndex { get; internal set; }
        /// <summary>普通条目在分区内的索引；页眉为 -1，页脚为 -2。</summary>
        public int ItemIndex { get; internal set; }
        /// <summary>当前几何信息对应普通条目、页眉或页脚。</summary>
        public ElementKind Kind { get; internal set; }
        /// <summary>内容局部坐标系中的矩形，原点位于顶部，Y 轴向下为正。</summary>
        public Rect Rect { get; internal set; }
        /// <summary>布局元素查找键；普通条目按全局条目 ID 区分，页眉和页脚还包含分区 ID。</summary>
         // 构建时已按元素身份填充并冻结 LayoutKey；滚动查询复用同一字符串。
        public string Key => Item.LayoutKey;

        internal static string MakeKey(ElementKind kind, string sectionId, string itemId) => kind == ElementKind.Item ? "I" + itemId.Length + ":" + itemId : ((int)kind) + ":" + sectionId.Length + ":" + sectionId + itemId.Length + ":" + itemId;
    }
}
