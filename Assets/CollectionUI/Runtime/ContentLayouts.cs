using System;
using System.Collections.Generic;
using UnityEngine;

namespace CollectionUI
{
    /// <summary>单行内容的测量结果，包含单元格尺寸和经过规范化、省略处理的显示文本。</summary>
    public struct ContentMetrics
    {
        /// <summary>包含左右内边距的内容宽度及固定内容高度。</summary>
        public Vector2 Size;
        /// <summary>用于实际显示的单行文本，可能包含末尾省略号。</summary>
        public string DisplayText;
    }

    /// <summary>用于等高单行内容的测量；先对过长文字添加省略号，再由流式布局放置所得矩形。</summary>
    public sealed class SingleLineMeasurer
    {
        readonly UguiTextMeasurer text;
        readonly float height, padding, minWidth;
        readonly Dictionary<Key, ContentMetrics> cache = new Dictionary<Key, ContentMetrics>();
        readonly Queue<Key> cacheOrder = new Queue<Key>();
        const int CacheLimit = 512;
        struct Key : IEquatable<Key>
        {
            public string Text;
            public float Width;
            public bool Equals(Key other) => Text == other.Text && Width == other.Width;
            public override bool Equals(object obj) => obj is Key key && Equals(key);
            public override int GetHashCode() => Text.GetHashCode() * 397 ^ Width.GetHashCode();
        }

        /// <summary>创建固定高度的单行内容测量器；水平内边距按左右各一份计算，最小宽度必须留出正的文字空间。</summary>
        public SingleLineMeasurer(UguiTextMeasurer text, float height = 96, float horizontalPadding = 20, float minWidth = 96)
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
            UguiTextMeasurer.Positive(height, nameof(height));
            UguiTextMeasurer.Positive(horizontalPadding, nameof(horizontalPadding));
            UguiTextMeasurer.Positive(minWidth, nameof(minWidth));
            if (minWidth <= horizontalPadding * 2)
                throw new ArgumentException("Minimum content width must leave positive text space after padding.");
            if (height < text.WrappedHeight("Mg", 10000))
                throw new ArgumentException("Content height cannot be smaller than its font line height.");
            this.height = height;
            padding = horizontalPadding;
            this.minWidth = minWidth;
        }

        /// <summary>在可用宽度内测量内容，换行符替换为空格，超长文本省略；结果按原始文本与可用宽度缓存。</summary>
        public ContentMetrics Measure(string value, float availableWidth)
        {
            UguiTextMeasurer.Positive(availableWidth, nameof(availableWidth));
            if (availableWidth <= padding * 2)
                throw new ArgumentException("Content width must leave positive space inside its horizontal padding.");
            value = value ?? string.Empty;
            var key = new Key
            {
                Text = value,
                Width = availableWidth
            };
            if (cache.TryGetValue(key, out var result))
                return result;
            // 先规范化换行再测量；宽度同时受最小宽度和实际可用宽度约束。
            string normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            float width = Mathf.Min(availableWidth, Mathf.Max(minWidth, text.SingleLineWidth(normalized) + padding * 2));
            result = new ContentMetrics
            {
                Size = new Vector2(width, height),
                DisplayText = text.Ellipsize(normalized, width - padding * 2)
            };
            // 按写入顺序逐项淘汰，满载时仍保留其余测量结果，避免整批失效后的重测峰值。
            if (cache.Count >= CacheLimit)
                cache.Remove(cacheOrder.Dequeue());
            cache.Add(key, result);
            cacheOrder.Enqueue(key);
            return result;
        }
    }

    /// <summary>竖直组合中的一个子元素，可使用文字测量、固定尺寸或宽高比。</summary>
    public struct ContentElement
    {
        internal enum MeasureMode
        {
            Unspecified,
            Text,
            Fixed,
            Aspect
        }

        internal MeasureMode Mode;
        internal UguiTextMeasurer Measurer;
        internal string Value;
        internal Vector2 FixedSize;
        internal float Ratio;
        /// <summary>创建文字元素；竖直组合中自动换行，并至少保留测量字体的一行高度。</summary>
        public static ContentElement Text(UguiTextMeasurer measurer, string value) => new ContentElement
        {
            Mode = MeasureMode.Text,
            Measurer = measurer,
            Value = value
        };
        /// <summary>创建固定尺寸元素；零宽度占满组合的内部宽度，高度必须为正数。</summary>
        public static ContentElement Fixed(Vector2 size) => new ContentElement
        {
            Mode = MeasureMode.Fixed,
            FixedSize = size
        };
        /// <summary>创建占满内部宽度的比例元素；参数表示宽除以高，测量时要求有限且为正。</summary>
        public static ContentElement Aspect(float widthOverHeight) => new ContentElement
        {
            Mode = MeasureMode.Aspect,
            Ratio = widthOverHeight
        };
    }

    /// <summary>竖直组合的测量尺寸及左上坐标系下的子元素矩形；输入未变化时可复用该结果。</summary>
    public struct LayoutMetrics
    {
        /// <summary>整个竖直组合的尺寸，包含四边内边距。</summary>
        public Vector2 Size;
        /// <summary>与输入元素顺序对应的子元素矩形，采用左上原点、Y 轴向下的坐标。</summary>
        public IReadOnlyList<Rect> ElementRects;
    }

    /// <summary>左对齐的竖直内容测量器；文字和比例元素占满扣除左右内边距后的内部宽度。</summary>
    public static class VerticalContentMeasurer
    {
        /// <summary>
        /// 按输入顺序测量，仅在相邻元素之间添加间距。空输入仅保留上下内边距，允许零高度。
        /// 非空输入会分配一个矩形数组；内容与宽度未变化时应保留测量结果，避免滚动中反复测量。
        /// </summary>
        public static LayoutMetrics Measure(IReadOnlyList<ContentElement> elements, float availableWidth, Insets padding, float spacing)
        {
            if (elements == null)
                throw new ArgumentNullException(nameof(elements));
            UguiTextMeasurer.Positive(availableWidth, nameof(availableWidth));
            NonNegative(padding.Left, "Left padding");
            NonNegative(padding.Top, "Top padding");
            NonNegative(padding.Right, "Right padding");
            NonNegative(padding.Bottom, "Bottom padding");
            NonNegative(spacing, nameof(spacing));
            float innerWidth = availableWidth - padding.Left - padding.Right;
            UguiTextMeasurer.Positive(innerWidth, "Inner width");
            var rects = elements.Count == 0 ? Array.Empty<Rect>() : new Rect[elements.Count];
            float y = padding.Top;
            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                Vector2 size;
                switch (element.Mode)
                {
                    case ContentElement.MeasureMode.Text:
                        if (element.Measurer == null)
                            throw new ArgumentNullException("Text measurer");
                        // 空文本也保留至少一行高度，防止元素高度退化为零。
                        size = new Vector2(innerWidth, Mathf.Max(element.Measurer.WrappedHeight(element.Value, innerWidth), element.Measurer.WrappedHeight("Mg", innerWidth)));
                        break;
                    case ContentElement.MeasureMode.Fixed:
                        NonNegative(element.FixedSize.x, "Fixed width");
                        size = new Vector2(element.FixedSize.x == 0 ? innerWidth : element.FixedSize.x, element.FixedSize.y);
                        break;
                    case ContentElement.MeasureMode.Aspect:
                        UguiTextMeasurer.Positive(element.Ratio, "Aspect ratio");
                        size = new Vector2(innerWidth, innerWidth / element.Ratio);
                        break;
                    default:
                        throw new ArgumentException("An element must be created with Text, Fixed, or Aspect.", nameof(elements));
                }

                UguiTextMeasurer.Positive(size.x, "Element width");
                UguiTextMeasurer.Positive(size.y, "Element height");
                if (size.x > innerWidth)
                    throw new ArgumentException("Element width exceeds the inner width.", nameof(elements));
                // 间距只属于相邻兄弟之间，不叠加在组合的首尾内边距上。
                if (i > 0)
                    y += spacing;
                NonNegative(y, "Element top");
                UguiTextMeasurer.Positive(padding.Left + size.x, "Element right");
                rects[i] = new Rect(padding.Left, y, size.x, size.y);
                y += size.y;
                UguiTextMeasurer.Positive(y, "Element bottom");
            }

            float height = y + padding.Bottom;
            NonNegative(height, "Content height");
            return new LayoutMetrics
            {
                Size = new Vector2(availableWidth, height),
                ElementRects = rects
            };
        }

        static void NonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentException(name + " must be finite and non-negative.");
        }
    }
}
