using System;
using System.Collections.Generic;
using UnityEngine;

namespace CollectionUI
{
    /// <summary>保留 O(N) 几何数据；使用带子树最大底边的平衡 Y 区间索引，也能查到起点在视口上方的高条目。</summary>
    public sealed class LayoutResult
    {
        internal sealed class SectionGeometry
        {
            internal CollectionSection Metadata;
            internal int FirstEntry, Count;
            internal float Start, Height;
        }

        internal readonly Dictionary<string, SectionGeometry> Sections;
        readonly List<LayoutEntry> entries;
        readonly Dictionary<string, int> byKey;
        readonly Dictionary<string, LayoutEntry> byItem;
        readonly int[] sorted;
        readonly float[] maxBottom;
        /// <summary>按构建顺序保存的只读元素列表；可见性查询返回此列表中的索引。</summary>
        public IReadOnlyList<LayoutEntry> Entries { get; }
        /// <summary>布局内容总高度，包含显示分区的内边距和分区之间的间距。</summary>
        public float ContentHeight { get; }
        /// <summary>构建布局时使用的内容宽度。</summary>
        public float ContentWidth { get; }
        /// <summary>本次构建中复用了历史尺寸和位置关系的分区数。</summary>
        public int ReusedSectionCount { get; internal set; }
        /// <summary>本次构建中重新计算几何信息的分区数。</summary>
        public int RecomputedSectionCount { get; internal set; }

        internal LayoutResult(List<LayoutEntry> source, float width, float height, Dictionary<string, SectionGeometry> sections)
        {
            byKey = new Dictionary<string, int>(source.Count, StringComparer.Ordinal);
            byItem = new Dictionary<string, LayoutEntry>(source.Count, StringComparer.Ordinal);
            Sections = sections;
            entries = source;
            Entries = source.AsReadOnly();
            ContentWidth = width;
            ContentHeight = height;
            sorted = new int[source.Count];
            maxBottom = new float[source.Count];
            bool orderedByTop = true;
            for (int i = 0; i < source.Count; i++)
            {
                sorted[i] = i;
                if (i > 0 && source[i].Rect.yMin < source[i - 1].Rect.yMin)
                    orderedByTop = false;
                byKey.Add(source[i].Key, i);
                if (source[i].Kind == ElementKind.Item)
                    byItem.Add(source[i].Item.Id, source[i]);
            }

            // 列布局及流式布局通常已按顶部排序；实际顺序有回退时才支付排序成本。
            if (!orderedByTop)
                Array.Sort(sorted, (a, b) =>
                {
                    int c = entries[a].Rect.yMin.CompareTo(entries[b].Rect.yMin);
                    return c != 0 ? c : a.CompareTo(b);
                });
            BuildIndex(0, sorted.Length);
        }

        /// <summary>为自定义布局建立查询索引，并复制元素列表及各元素的元数据。
        /// 元素必须位于有限的内容边界内，布局键唯一，普通条目 ID 在整个结果中唯一。</summary>
        public static LayoutResult Create(IReadOnlyList<LayoutEntry> entries, float width, float height)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0 || float.IsNaN(height) || float.IsInfinity(height) || height < 0)
                throw new ArgumentException("Content bounds must be finite, with positive width and nonnegative height.");
            var source = new List<LayoutEntry>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry == null)
                    throw new ArgumentException("Layout entries cannot be null.");
                if (entry.Rect.xMax > width || entry.Rect.yMax > height)
                    throw new ArgumentException("Entry rect exceeds content bounds.");
                source.Add(new LayoutEntry(entry.Item, entry.SectionId, entry.SectionIndex, entry.ItemIndex, entry.Kind, entry.Rect));
            }

            return new LayoutResult(source, width, height, new Dictionary<string, SectionGeometry>(StringComparer.Ordinal));
        }

        float BuildIndex(int lo, int hi)
        {
            if (lo >= hi)
                return float.NegativeInfinity;
            // 排序数组的中点隐式构成平衡树，每个节点保存整个子树的最大底边。
            int mid = (lo + hi) / 2;
            return maxBottom[mid] = Math.Max(entries[sorted[mid]].Rect.yMax, Math.Max(BuildIndex(lo, mid), BuildIndex(mid + 1, hi)));
        }

        /// <summary>清空并填充调用方提供的列表，返回与视口相交的元素索引；复用列表容量可避免每次查询分配。</summary>
        public void Query(Rect viewport, List<int> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            results.Clear();
            if (viewport.width <= 0 || viewport.height <= 0)
                return;
            QueryRange(0, sorted.Length, viewport, results);
        }

        void QueryRange(int lo, int hi, Rect viewport, List<int> output)
        {
            if (lo >= hi)
                return;
            int mid = (lo + hi) / 2;
            // 子树全在视口上方，或最早的顶边已在视口下方时，整个区间均可剪枝。
            if (maxBottom[mid] <= viewport.yMin || entries[sorted[lo]].Rect.yMin >= viewport.yMax)
                return;
            QueryRange(lo, mid, viewport, output);
            if (entries[sorted[mid]].Rect.Overlaps(viewport))
                output.Add(sorted[mid]);
            QueryRange(mid + 1, hi, viewport, output);
        }

        /// <summary>按全局普通条目 ID 查找；不查找页眉或页脚，传入空引用时返回 false。</summary>
        public bool TryGetItem(string id, out LayoutEntry entry)
        {
            entry = null;
            return id != null && byItem.TryGetValue(id, out entry);
        }

        /// <summary>按完整布局键查找任意元素；传入空引用时返回 false。</summary>
        public bool TryGetKey(string key, out LayoutEntry entry)
        {
            entry = null;
            if (!TryGetIndex(key, out int index))
                return false;
            entry = entries[index];
            return true;
        }

        // 可见单元格协调复用结果内已有索引，避免每次提交重新遍历构建字典。
        internal bool TryGetIndex(string key, out int index)
        {
            if (key != null && byKey.TryGetValue(key, out index))
                return true;
            index = -1;
            return false;
        }
    }

    /// <summary>内置分区布局计算器，只处理数据与几何信息，不创建或修改 Unity 视图对象。</summary>
    public static class SectionedLayout
    {
        struct Segment
        {
            public float Left, Right, Height;
            public Segment(float l, float r, float h)
            {
                Left = l;
                Right = r;
                Height = h;
            }
        }

        /// <summary>同时传入历史结果和非空引用的脏分区集合，可复用元数据等价的分区；空集合启用自动比较，
        /// null 则强制全部重算，宽度变化也会使全部分区重算。业务 Data 的变化影响测量时，
        /// 必须递增条目 Version 或将分区标记为脏。每次调用仍会重建元素副本和可见性索引。</summary>
        public static LayoutResult Build(CollectionSnapshot snapshot, float viewportWidth, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, ISet<string> dirtySections = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            Positive(viewportWidth, "Viewport width");
            // 公开入口始终复制，隔离调用方后续修改。
            return BuildOwned(snapshot.Copy(), viewportWidth, measure, previous, dirtySections);
        }

        // 仅供 CollectionView 提交私有候选快照；发布后条目被冻结，后续修改必须重新复制。
        internal static LayoutResult BuildOwned(CollectionSnapshot copy, float viewportWidth, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, ISet<string> dirtySections = null)
        {
            if (copy == null)
                throw new ArgumentNullException(nameof(copy));
            Positive(viewportWidth, "Viewport width");
            Validate(copy);
            var entries = new List<LayoutEntry>();
            float y = 0, previousSectionSpacing = 0;
            bool hasSection = false;
            var sectionGeometry = new Dictionary<string, LayoutResult.SectionGeometry>(StringComparer.Ordinal);
            bool canReuse = previous != null && previous.ContentWidth == viewportWidth && dirtySections != null;
            int reused = 0, recomputed = 0;
            for (int si = 0; si < copy.Sections.Count; si++)
            {
                var section = copy.Sections[si];
                var settings = section.Layout;
                if (section.Items.Count == 0 && !settings.ShowEmptySection)
                    continue;
                float available = viewportWidth - settings.Insets.Left - settings.Insets.Right;
                Positive(available, "Section available width");
                if (hasSection)
                    y += previousSectionSpacing;
                hasSection = true;
                previousSectionSpacing = settings.SectionSpacing;
                float sectionStart = y;
                int firstEntry = entries.Count;
                if (canReuse && !dirtySections.Contains(section.Id) && previous.Sections.TryGetValue(section.Id, out var old) && Equivalent(section, old.Metadata))
                {
                    // 前面分区的高度可能改变：复用内部几何，只整体平移，并绑定本次快照中的元数据。
                    float delta = sectionStart - old.Start;
                    for (int i = old.FirstEntry; i < old.FirstEntry + old.Count; i++)
                    {
                        var entry = previous.Entries[i];
                        var item = entry.Kind == ElementKind.Header ? section.Header : entry.Kind == ElementKind.Footer ? section.Footer : section.Items[entry.ItemIndex];
                        Add(entries, section, si, item, entry.ItemIndex, entry.Kind, entry.Rect.x, entry.Rect.y + delta, new Vector2(entry.Rect.width, entry.Rect.height));
                    }

                    y = sectionStart + old.Height;
                    NonNegative(y, "Content height");
                    sectionGeometry.Add(section.Id, new LayoutResult.SectionGeometry { Metadata = section, FirstEntry = firstEntry, Count = entries.Count - firstEntry, Start = sectionStart, Height = old.Height });
                    reused++;
                    continue;
                }

                recomputed++;
                y += settings.Insets.Top;
                bool hasBlock = false;
                if (section.Header != null)
                {
                    var size = Resolve(section.Header, available, measure);
                    Add(entries, section, si, section.Header, -1, ElementKind.Header, settings.Insets.Left, y, size);
                    y += size.y;
                    hasBlock = true;
                }

                if (section.Items.Count > 0)
                {
                    if (hasBlock)
                        y += settings.VerticalSpacing;
                    float bottom = y;
                    if (settings.Mode == LayoutMode.Columns)
                    {
                        float width = (available - (settings.ColumnCount - 1) * settings.HorizontalSpacing) / settings.ColumnCount;
                        Positive(width, "Column width");
                        // 每个条目放入当前最短列；等高时保留最左列，保证相同输入得到稳定结果。
                        var heights = new float[settings.ColumnCount];
                        for (int i = 0; i < heights.Length; i++)
                            heights[i] = y;
                        for (int i = 0; i < section.Items.Count; i++)
                        {
                            int column = 0;
                            for (int c = 1; c < heights.Length; c++)
                                if (heights[c] < heights[column])
                                    column = c;
                            var size = Resolve(section.Items[i], width, measure);
                            float x = settings.Insets.Left + column * (width + settings.HorizontalSpacing);
                            Add(entries, section, si, section.Items[i], i, ElementKind.Item, x, heights[column], size);
                            bottom = Math.Max(bottom, heights[column] + size.y);
                            heights[column] += size.y + settings.VerticalSpacing;
                        }
                    }
                    else if (settings.Mode == LayoutMode.Flow)
                    {
                        // 按输入顺序从左向右排列；当前行最高元素决定下一行顶部，超宽时整项换行。
                        float rowY = y, rowHeight = 0, nextX = 0;
                        bool hasRowItem = false;
                        for (int i = 0; i < section.Items.Count; i++)
                        {
                            var size = Resolve(section.Items[i], available, measure);
                            float x = hasRowItem ? nextX + settings.HorizontalSpacing : 0;
                            if (hasRowItem && x + size.x > available)
                            {
                                rowY += rowHeight + settings.VerticalSpacing;
                                rowHeight = 0;
                                x = 0;
                            }

                            Add(entries, section, si, section.Items[i], i, ElementKind.Item, settings.Insets.Left + x, rowY, size);
                            rowHeight = Math.Max(rowHeight, size.y);
                            bottom = rowY + rowHeight;
                            nextX = x + size.x;
                            hasRowItem = true;
                        }
                    }
                    else
                    {
                        // 以分段常量记录每段横向范围的可放置顶部；已放置元素后的水平、竖直间距也纳入预留范围。
                        var skyline = new List<Segment>
                        {
                            new Segment(0, available, y)
                        };
                        // 缓冲区仅属于本次分区构建，双列表交换复用，允许测量回调重入其他布局。
                        var next = new List<Segment>();
                        var deque = new int[16];
                        for (int i = 0; i < section.Items.Count; i++)
                        {
                            var size = Resolve(section.Items[i], available, measure);
                            float bestX = 0, bestY = float.PositiveInfinity;
                            // 单调队列维护预留横向范围内的最大高度；单个条目的所有候选位置合计为 O(分段数)。
                            if (deque.Length < skyline.Count)
                                Array.Resize(ref deque, Math.Max(skyline.Count, deque.Length * 2));
                            int head = 0, tail = 0, end = 0;
                            for (int c = 0; c < skyline.Count; c++)
                            {
                                float x = skyline[c].Left;
                                if (x + size.x > available)
                                    break;
                                while (head < tail && deque[head] < c)
                                    head++;
                                float reservedRight = Math.Min(available, x + size.x + settings.HorizontalSpacing);
                                while (end < skyline.Count && skyline[end].Left < reservedRight)
                                {
                                    while (head < tail && skyline[deque[tail - 1]].Height <= skyline[end].Height)
                                        tail--;
                                    deque[tail++] = end++;
                                }

                                float candidate = Math.Max(y, skyline[deque[head]].Height);
                                if (candidate < bestY)
                                {
                                    bestY = candidate;
                                    bestX = x;
                                }
                            }

                            Add(entries, section, si, section.Items[i], i, ElementKind.Item, settings.Insets.Left + bestX, bestY, size);
                            bottom = Math.Max(bottom, bestY + size.y);
                            float right = Math.Min(available, bestX + size.x + settings.HorizontalSpacing);
                            // 替换被新元素覆盖的天际线范围，保留左右未覆盖部分，供下一个条目继续搜索。
                            next.Clear();
                            bool inserted = false;
                            foreach (var seg in skyline)
                            {
                                if (seg.Right <= bestX || seg.Left >= right)
                                    next.Add(seg);
                                else
                                {
                                    if (seg.Left < bestX)
                                        next.Add(new Segment(seg.Left, bestX, seg.Height));
                                    if (!inserted)
                                    {
                                        next.Add(new Segment(bestX, right, bestY + size.y + settings.VerticalSpacing));
                                        inserted = true;
                                    }

                                    if (seg.Right > right)
                                        next.Add(new Segment(right, seg.Right, seg.Height));
                                }
                            }

                            var previousSkyline = skyline;
                            skyline = next;
                            next = previousSkyline;
                        }
                    }

                    y = bottom;
                    hasBlock = true;
                }

                if (section.Footer != null)
                {
                    if (hasBlock)
                        y += settings.VerticalSpacing;
                    var size = Resolve(section.Footer, available, measure);
                    Add(entries, section, si, section.Footer, -2, ElementKind.Footer, settings.Insets.Left, y, size);
                    y += size.y;
                }

                y += settings.Insets.Bottom;
                NonNegative(y, "Content height");
                sectionGeometry.Add(section.Id, new LayoutResult.SectionGeometry { Metadata = section, FirstEntry = firstEntry, Count = entries.Count - firstEntry, Start = sectionStart, Height = y - sectionStart });
            }

            return new LayoutResult(entries, viewportWidth, y, sectionGeometry)
            {
                ReusedSectionCount = reused,
                RecomputedSectionCount = recomputed
            };
        }

        static bool Equivalent(CollectionSection a, CollectionSection b)
        {
            var x = a.Layout;
            var y = b.Layout;
            if (x.Mode != y.Mode || x.ColumnCount != y.ColumnCount || x.HorizontalSpacing != y.HorizontalSpacing || x.VerticalSpacing != y.VerticalSpacing || x.SectionSpacing != y.SectionSpacing || x.ShowEmptySection != y.ShowEmptySection || x.Insets.Left != y.Insets.Left || x.Insets.Top != y.Insets.Top || x.Insets.Right != y.Insets.Right || x.Insets.Bottom != y.Insets.Bottom || a.Items.Count != b.Items.Count || !Equivalent(a.Header, b.Header) || !Equivalent(a.Footer, b.Footer))
                return false;
            for (int i = 0; i < a.Items.Count; i++)
                if (!Equivalent(a.Items[i], b.Items[i]))
                    return false;
            return true;
        }

        // 不比较调用方拥有的 Data 内部状态；尺寸变化需通过版本或脏分区显式通知。
        static bool Equivalent(CollectionItem a, CollectionItem b)
        {
            if (a == null || b == null)
                return a == b;
            return a.Id == b.Id && a.Version == b.Version && a.ReuseIdentifier == b.ReuseIdentifier && a.Size.Mode == b.Size.Mode && a.Size.Width == b.Size.Width && a.Size.Height == b.Size.Height && a.Size.AspectRatio == b.Size.AspectRatio;
        }

        static void Add(List<LayoutEntry> entries, CollectionSection section, int si, CollectionItem item, int ii, ElementKind kind, float x, float y, Vector2 size)
        {
            NonNegative(x, "Entry x");
            NonNegative(y, "Entry y");
            Positive(x + size.x, "Entry right");
            Positive(y + size.y, "Entry bottom");
            item.Freeze();
            entries.Add(new LayoutEntry { Item = item, SectionId = section.Id, SectionIndex = si, ItemIndex = ii, Kind = kind, Rect = new Rect(x, y, size.x, size.y) });
        }

        static Vector2 Resolve(CollectionItem item, float available, Func<CollectionItem, float, Vector2> measure)
        {
            // 先解析自动宽度，再按尺寸模式求值；测量回调返回的实际尺寸也必须通过边界和有限性校验。
            var spec = item.Size;
            NonNegative(spec.Width, "Item width (zero means auto)");
            float width = spec.Width == 0 ? available : spec.Width;
            Vector2 size;
            if (width > available)
                throw new ArgumentException("Item width exceeds available width: " + item.Id);
            switch (spec.Mode)
            {
                case ItemSizeMode.Fixed:
                    size = new Vector2(width, spec.Height);
                    break;
                case ItemSizeMode.Aspect:
                    Positive(spec.AspectRatio, "Aspect ratio");
                    size = new Vector2(width, width / spec.AspectRatio);
                    break;
                case ItemSizeMode.Measured:
                    if (measure == null)
                        throw new ArgumentException("Measured item requires a measurement delegate: " + item.Id);
                    size = measure(item, width);
                    break;
                default:
                    throw new ArgumentException("Unknown size mode.");
            }

            Positive(size.x, "Resolved item width");
            Positive(size.y, "Resolved item height");
            if (size.x > available)
                throw new ArgumentException("Measured item width exceeds available width: " + item.Id);
            return size;
        }

        static void Validate(CollectionSnapshot snapshot)
        {
            // 普通条目 ID 全局唯一；页眉、页脚则由元素类型和分区 ID 共同限定身份。
            var sections = new HashSet<string>(StringComparer.Ordinal);
            var items = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in snapshot.Sections)
            {
                if (string.IsNullOrEmpty(section.Id) || !sections.Add(section.Id))
                    throw new ArgumentException("Section IDs must be nonempty and unique.");
                var s = section.Layout;
                if (s.Mode != LayoutMode.Columns && s.Mode != LayoutMode.Skyline && s.Mode != LayoutMode.Flow)
                    throw new ArgumentException("Unknown layout mode.");
                if (s.ColumnCount < 1)
                    throw new ArgumentException("Column count must be positive.");
                NonNegative(s.HorizontalSpacing, "Horizontal spacing");
                NonNegative(s.VerticalSpacing, "Vertical spacing");
                NonNegative(s.SectionSpacing, "Section spacing");
                NonNegative(s.Insets.Left, "Left inset");
                NonNegative(s.Insets.Right, "Right inset");
                NonNegative(s.Insets.Top, "Top inset");
                NonNegative(s.Insets.Bottom, "Bottom inset");
                if (section.Header != null)
                {
                    ValidateItem(section.Header);
                    section.Header = section.Header.PrepareLayoutKey(ElementKind.Header, section.Id);
                }

                if (section.Footer != null)
                {
                    ValidateItem(section.Footer);
                    section.Footer = section.Footer.PrepareLayoutKey(ElementKind.Footer, section.Id);
                }

                for (int i = 0; i < section.Items.Count; i++)
                {
                    var item = section.Items[i];
                    ValidateItem(item);
                    section.Items[i] = item.PrepareLayoutKey(ElementKind.Item, section.Id);
                    if (!items.Add(item.Id))
                        throw new ArgumentException("Item IDs must be globally unique: " + item.Id);
                }
            }
        }

        static void ValidateItem(CollectionItem item)
        {
            if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.ReuseIdentifier))
                throw new ArgumentException("Item ID and reuse identifier must be nonempty.");
        }

        static void Positive(float n, string name)
        {
            if (float.IsNaN(n) || float.IsInfinity(n) || n <= 0)
                throw new ArgumentException(name + " must be finite and positive.");
        }

        static void NonNegative(float n, string name)
        {
            if (float.IsNaN(n) || float.IsInfinity(n) || n < 0)
                throw new ArgumentException(name + " must be finite and nonnegative.");
        }
    }
}
