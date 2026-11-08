// 内容测量测试：覆盖字体缩放、文本换行/省略、通用纵向元素组合及非法输入，确保测量规则和渲染契约一致。
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    public class ContentMeasurementTests
    {
        Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [Test]
        public void TagWidthFollowsContentAndLongTagEllipsizesAtAvailableWidth()
        {
            var text = new UguiTextMeasurer(Font, 28);
            var tags = new SingleLineMeasurer(text);
            var small = tags.Measure("Hat", 400);
            var large = tags.Measure("Outdoor equipment", 400);
            Assert.That(large.Size.x, Is.GreaterThan(small.Size.x));
            Assert.That(small.Size.y, Is.EqualTo(large.Size.y));
            var longTag = tags.Measure("A very long tag that cannot possibly fit on one narrow row", 170);
            Assert.That(longTag.Size.x, Is.LessThanOrEqualTo(170));
            Assert.That(longTag.DisplayText, Does.EndWith("…"));
            Assert.That(text.SingleLineWidth(longTag.DisplayText), Is.LessThanOrEqualTo(longTag.Size.x - 40));
        }

        [Test]
        public void VerticalCompositionMatchesActualTextPreferredHeightAndAspectRatio()
        {
            var title = new UguiTextMeasurer(Font, 28);
            var price = new UguiTextMeasurer(Font, 32, FontStyle.Bold);
            string value = "Lightweight outdoor travel backpack with multiple pockets and adjustable shoulder straps";
            var elements = new[]
            {
                ContentElement.Aspect(1.25f),
                ContentElement.Text(title, value),
                ContentElement.Text(price, "$129.90")
            };
            var wide = VerticalContentMeasurer.Measure(elements, 420, new Insets(16, 16, 16, 16), 12);
            var narrow = VerticalContentMeasurer.Measure(elements, 220, new Insets(16, 16, 16, 16), 12);
            Assert.That(narrow.ElementRects[1].height, Is.GreaterThan(wide.ElementRects[1].height));
            Assert.That(wide.ElementRects[0].width / wide.ElementRects[0].height, Is.EqualTo(1.25f).Within(.001));
            Assert.That(wide.ElementRects[1].yMin - wide.ElementRects[0].yMax, Is.EqualTo(12));
            Assert.That(wide.ElementRects[2].yMin - wide.ElementRects[1].yMax, Is.EqualTo(12));
            Assert.That(wide.Size.y - wide.ElementRects[2].yMax, Is.EqualTo(16));
            var go = new GameObject("MeasuredText", typeof(RectTransform), typeof(Text));
            try
            {
                var label = go.GetComponent<Text>();
                title.ApplyTo(label, true);
                label.text = value;
                ((RectTransform)go.transform).sizeDelta = narrow.ElementRects[1].size;
                Assert.That(narrow.ElementRects[1].height, Is.EqualTo(Mathf.Ceil(label.preferredHeight)).Within(1));
                Assert.That(label.resizeTextForBestFit, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TextMeasurementCacheIsBoundedAndRejectsInvalidGeometry()
        {
            var text = new UguiTextMeasurer(Font, 28, FontStyle.Normal, 1, 32);
            for (int i = 0; i < 1000; i++)
                text.WrappedHeight("Item " + i, 100 + i);
            Assert.That(text.CachedCount, Is.LessThanOrEqualTo(32));
            Assert.Throws<System.ArgumentException>(() => text.WrappedHeight("Title", 0));
            Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Aspect(0) }, 300, default(Insets), 0));
        }

        [Test]
        public void VerticalCompositionSupportsArbitraryOrderAndCount()
        {
            var elements = new[]
            {
                ContentElement.Fixed(new Vector2(30, 10)),
                ContentElement.Aspect(2),
                ContentElement.Fixed(new Vector2(0, 20)),
                ContentElement.Fixed(new Vector2(40, 15))
            };
            var result = VerticalContentMeasurer.Measure(elements, 120, new Insets(7, 11, 13, 17), 3);
            Assert.That(result.Size, Is.EqualTo(new Vector2(120, 132)));
            Assert.That(result.ElementRects.Count, Is.EqualTo(4));
            Assert.That(result.ElementRects[0], Is.EqualTo(new Rect(7, 11, 30, 10)));
            Assert.That(result.ElementRects[1], Is.EqualTo(new Rect(7, 24, 100, 50)));
            Assert.That(result.ElementRects[2], Is.EqualTo(new Rect(7, 77, 100, 20)));
            Assert.That(result.ElementRects[3], Is.EqualTo(new Rect(7, 100, 40, 15)));
        }

        [Test]
        public void EmptyCompositionHasOnlyPaddingAndNoSpacing()
        {
            var empty = new ContentElement[0];
            var result = VerticalContentMeasurer.Measure(empty, 120, new Insets(7, 11, 13, 17), 30);
            Assert.That(result.Size, Is.EqualTo(new Vector2(120, 28)));
            Assert.That(result.ElementRects, Is.Empty);
            Assert.That(VerticalContentMeasurer.Measure(empty, 120, default(Insets), 0).Size.y, Is.Zero);
        }

        [Test]
        public void ZeroPaddingAndSpacingPlaceAdjacentElementsWithinBounds()
        {
            var result = VerticalContentMeasurer.Measure(new[] { ContentElement.Fixed(new Vector2(20, 10)), ContentElement.Aspect(2) }, 100, default(Insets), 0);
            Assert.That(result.ElementRects[0], Is.EqualTo(new Rect(0, 0, 20, 10)));
            Assert.That(result.ElementRects[1], Is.EqualTo(new Rect(0, 10, 100, 50)));
            Assert.That(result.Size, Is.EqualTo(new Vector2(100, 60)));
        }

        [Test]
        public void EmptyTextReservesOneFontLine()
        {
            var text = new UguiTextMeasurer(Font, 28);
            var result = VerticalContentMeasurer.Measure(new[] { ContentElement.Text(text, null) }, 100, default(Insets), 0);
            Assert.That(result.Size.y, Is.EqualTo(text.WrappedHeight("Mg", 100)));
            Assert.That(result.Size.y, Is.GreaterThan(0));
        }

        [Test]
        public void VerticalCompositionRejectsInvalidBoundsAndOverflow()
        {
            var valid = new[]
            {
                ContentElement.Fixed(new Vector2(20, 10))
            };
            Assert.Throws<System.ArgumentNullException>(() => VerticalContentMeasurer.Measure(null, 100, default(Insets), 0));
            foreach (float n in new[]
            {
                -1f,
                float.NaN,
                float.PositiveInfinity
            }

            )
            {
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(valid, 100, default(Insets), n));
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(valid, 100, new Insets(n, 0, 0, 0), 0));
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(valid, 100, new Insets(0, n, 0, 0), 0));
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(valid, 100, new Insets(0, 0, n, 0), 0));
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(valid, 100, new Insets(0, 0, 0, n), 0));
            }

            foreach (float n in new[]
            {
                0f,
                -1f,
                float.NaN,
                float.PositiveInfinity
            }

            )
            {
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(valid, n, default(Insets), 0));
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Aspect(n) }, 100, default(Insets), 0));
                Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Fixed(new Vector2(10, n)) }, 100, default(Insets), 0));
            }

            Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(valid, 100, new Insets(50, 0, 50, 0), 0));
            Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Fixed(new Vector2(101, 10)) }, 100, default(Insets), 0));
            Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Fixed(new Vector2(-1, 10)) }, 100, default(Insets), 0));
            Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { default(ContentElement) }, 100, default(Insets), 0));
            Assert.Throws<System.ArgumentNullException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Text(null, "x") }, 100, default(Insets), 0));
            Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Aspect(float.Epsilon) }, 100, default(Insets), 0));
            Assert.Throws<System.ArgumentException>(() => VerticalContentMeasurer.Measure(new[] { ContentElement.Fixed(new Vector2(10, float.MaxValue)), ContentElement.Fixed(new Vector2(10, float.MaxValue)) }, 100, default(Insets), 0));
        }

        [TestCase(.5f)]
        [TestCase(.6666667f)]
        [TestCase(1.5f)]
        [TestCase(2f)]
        public void MeasurementMatchesScaledCanvas(float scale)
        {
            var root = new GameObject("Canvas", typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            root.GetComponent<Canvas>().scaleFactor = scale;
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(root.transform, false);
            try
            {
                var measurer = new UguiTextMeasurer(Font, 28, pixelsPerUnit: scale);
                var label = go.GetComponent<Text>();
                measurer.ApplyTo(label, true);
                label.text = "A lightweight travel backpack with many organizer pockets for a long weekend journey";
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(187, 500);
                Assert.That(measurer.WrappedHeight(label.text, 187), Is.EqualTo(Mathf.Ceil(label.preferredHeight)).Within(1));
                measurer.ApplyTo(label, false);
                label.text = "Outdoor essentials";
                Assert.That(measurer.SingleLineWidth(label.text), Is.GreaterThanOrEqualTo(label.preferredWidth - 1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TagMinimumWidthMustLeaveTextSpace()
        {
            Assert.Throws<System.ArgumentException>(() => new SingleLineMeasurer(new UguiTextMeasurer(Font, 28), minWidth: 40));
        }

        [Test]
        public void TagsNormalizeLineBreaksAndKeepEqualHeights()
        {
            var tags = new SingleLineMeasurer(new UguiTextMeasurer(Font, 28));
            var tag = tags.Measure("Spring\nSummer\r\nAutumn", 220);
            Assert.That(tag.DisplayText, Does.Not.Contain("\n"));
            Assert.That(tag.DisplayText, Does.Not.Contain("\r"));
            Assert.That(tag.Size.y, Is.EqualTo(96));
        }
    }
}
