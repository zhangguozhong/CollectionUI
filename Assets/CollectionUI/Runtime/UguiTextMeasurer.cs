using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI
{
    /// <summary>通过 TextGenerator 在主线程测量 UGUI 文字，无需隐藏的 Text 对象；构造后的样式参数固定，缓存容量有界。</summary>
    public sealed class UguiTextMeasurer
    {
        readonly Font font;
        readonly int fontSize, capacity;
        readonly FontStyle style;
        readonly float lineSpacing, pixelsPerUnit;
        readonly TextGenerator generator = new TextGenerator();
        readonly Dictionary<Key, float> cache = new Dictionary<Key, float>();
        readonly Queue<Key> cacheOrder = new Queue<Key>();
        /// <summary>当前缓存的测量结果数；满载时逐项淘汰最早写入的结果。</summary>
        public int CachedCount => cache.Count;

        struct Key : IEquatable<Key>
        {
            public string Text;
            public float Width;
            public bool Height;
            public bool Equals(Key other) => Text == other.Text && Width == other.Width && Height == other.Height;
            public override bool Equals(object obj) => obj is Key key && Equals(key);
            public override int GetHashCode()
            {
                unchecked
                {
                    return (Text.GetHashCode() * 397 ^ Width.GetHashCode()) * 397 ^ Height.GetHashCode();
                }
            }
        }

        /// <summary>创建固定样式的主线程文字测量器；要求动态字体、正字号、正缓存容量及有限正数缩放参数。</summary>
        public UguiTextMeasurer(Font font, int fontSize, FontStyle style = FontStyle.Normal, float lineSpacing = 1, int cacheCapacity = 2048, float pixelsPerUnit = 1)
        {
            if (font == null || !font.dynamic)
                throw new ArgumentException("A dynamic UGUI font is required.");
            if (fontSize <= 0 || cacheCapacity <= 0)
                throw new ArgumentException("Font size and cache capacity must be positive.");
            Positive(lineSpacing, nameof(lineSpacing));
            Positive(pixelsPerUnit, nameof(pixelsPerUnit));
            this.font = font;
            this.fontSize = fontSize;
            this.style = style;
            this.lineSpacing = lineSpacing;
            capacity = cacheCapacity;
            this.pixelsPerUnit = pixelsPerUnit;
        }

        /// <summary>把测量所用字体样式应用到 Text，关闭富文本和自动缩放，并设置换行与对齐方式。</summary>
        public void ApplyTo(Text label, bool wrap, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            if (label == null)
                throw new ArgumentNullException(nameof(label));
            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.lineSpacing = lineSpacing;
            label.supportRichText = false;
            label.resizeTextForBestFit = false;
            label.alignByGeometry = false;
            label.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.alignment = alignment;
        }

        TextGenerationSettings Settings(float width, bool wrap)
        {
            return new TextGenerationSettings
            {
                font = font,
                fontSize = fontSize,
                fontStyle = style,
                lineSpacing = lineSpacing,
                scaleFactor = pixelsPerUnit,
                color = Color.white,
                pivot = new Vector2(0, 1),
                textAnchor = TextAnchor.UpperLeft,
                generationExtents = new Vector2(width, 0),
                horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                richText = false,
                resizeTextForBestFit = false,
                updateBounds = false
            };
        }

        /// <summary>测量不换行文字的向上取整宽度；空引用按空字符串处理。</summary>
        public float SingleLineWidth(string text) => Measure(text ?? string.Empty, 0, false);
        /// <summary>测量指定正宽度下自动换行后的向上取整高度，返回内容坐标单位。</summary>
        public float WrappedHeight(string text, float width)
        {
            Positive(width, nameof(width));
            return Measure(text ?? string.Empty, width, true);
        }

        float Measure(string text, float width, bool height, bool retain = true)
        {
            var key = new Key
            {
                Text = text,
                Width = width,
                Height = height
            };
            if (cache.TryGetValue(key, out var result))
                return result;
            // TextGenerator 输出按像素密度还原为内容单位，再向上取整，避免小数截断导致裁切。
            result = Mathf.Ceil(height ? generator.GetPreferredHeight(text, Settings(width, true)) / pixelsPerUnit : generator.GetPreferredWidth(text, Settings(0, false)) / pixelsPerUnit);
            if (float.IsNaN(result) || float.IsInfinity(result) || result < 0)
                throw new InvalidOperationException("Font returned invalid text metrics.");
            // 二分省略搜索的临时前缀只测量、不写入，避免挤走常用文字的缓存。
            if (!retain)
                return result;
            // 按写入顺序逐项淘汰，缓存容量始终有界且不会整批失效。
            if (cache.Count >= capacity)
                cache.Remove(cacheOrder.Dequeue());
            cache.Add(key, result);
            cacheOrder.Enqueue(key);
            return result;
        }

        /// <summary>在宽度内保留完整文本元素并追加省略号，避免拆开代理项对或组合字符；连省略号也放不下时返回空字符串。</summary>
        public string Ellipsize(string text, float width)
        {
            Positive(width, nameof(width));
            text = text ?? string.Empty;
            if (SingleLineWidth(text) <= width)
                return text;
            const string ellipsis = "…";
            if (SingleLineWidth(ellipsis) > width)
                return string.Empty;
            // 按文本元素边界二分搜索可容纳的最长前缀，测量时始终把末尾省略号计入宽度。
            var boundaries = StringInfo.ParseCombiningCharacters(text);
            int low = 0, high = boundaries.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                int end = mid == boundaries.Length ? text.Length : boundaries[mid];
                if (Measure(text.Substring(0, end) + ellipsis, 0, false, false) <= width)
                    low = mid;
                else
                    high = mid - 1;
            }

            return text.Substring(0, low == boundaries.Length ? text.Length : boundaries[low]) + ellipsis;
        }

        internal static void Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
                throw new ArgumentException(name + " must be finite and positive.");
        }
    }
}
