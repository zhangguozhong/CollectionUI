using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CollectionUI.Tests
{
    // 追加路径必须与完整计算保持一致，并且不能再次测量未变的前缀。
    public class CollectionAppendTests
    {
        static CollectionItem Item(string id) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Measured()
        };
        static Vector2 Measure(CollectionItem item, float width) => new Vector2(Mathf.Min(width, 20 + int.Parse(item.Id) % 4 * 15), int.Parse(item.Id) % 7 == 0 ? 360 : 15 + int.Parse(item.Id) % 5 * 13);
        static CollectionSnapshot Data(LayoutMode mode)
        {
            var section = new CollectionSection
            {
                Id = "s",
                Header = Item("100"),
                Footer = Item("101"),
                Layout = new SectionLayoutSettings
                {
                    Mode = mode,
                    ColumnCount = 3,
                    HorizontalSpacing = 7,
                    VerticalSpacing = 11,
                    SectionSpacing = 19,
                    Insets = new Insets(3, 5, 9, 13)
                }
            };
            for (int i = 0; i < 20; i++)
                section.Items.Add(Item(i.ToString()));
            return new CollectionSnapshot
            {
                Sections = new List<CollectionSection>
                {
                    section
                }
            };
        }

        static void Same(LayoutResult expected, LayoutResult actual)
        {
            Assert.AreEqual(expected.ContentHeight, actual.ContentHeight);
            Assert.AreEqual(expected.Entries.Count, actual.Entries.Count);
            for (int i = 0; i < expected.Entries.Count; i++)
            {
                Assert.AreEqual(expected.Entries[i].Rect, actual.Entries[i].Rect, "entry " + i);
                Assert.AreEqual(expected.Entries[i].Key, actual.Entries[i].Key);
                Assert.AreEqual(expected.Entries[i].ItemIndex, actual.Entries[i].ItemIndex);
                Assert.AreEqual(expected.Entries[i].SectionIndex, actual.Entries[i].SectionIndex);
            }

            var a = new List<int>();
            var b = new List<int>();
            for (int y = 0; y < expected.ContentHeight; y += 31)
            {
                var viewport = new Rect(0, y, 240, 70);
                expected.Query(viewport, a);
                actual.Query(viewport, b);
                CollectionAssert.AreEqual(a, b);
            }
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        public void AppendMeasuresOnlyNewItemsAndFooterAndPreservesOldResult(LayoutMode mode)
        {
            var data = Data(mode);
            var old = SectionedLayout.Build(data, 240, Measure);
            var original = SectionedLayout.Build(data, 240, Measure);
            for (int step = 0; step < 3; step++)
            {
                data.Sections[0].Items.Add(Item((20 + step).ToString()));
                var measured = new List<string>();
                var next = SectionedLayout.Build(data, 240, (item, width) =>
                {
                    measured.Add(item.Id);
                    return Measure(item, width);
                }, old, new HashSet<string>());
                CollectionAssert.AreEquivalent(new[] { (20 + step).ToString(), "101" }, measured);
                Same(SectionedLayout.Build(data, 240, Measure), next);
                if (step == 0)
                    Same(original, old);
                old = next;
            }
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        public void AppendAfterSectionShiftAndReuseKeepsContinuationCorrect(LayoutMode mode)
        {
            var data = Data(mode);
            var preceding = new CollectionSection
            {
                Id = "before",
                Layout = new SectionLayoutSettings
                {
                    Mode = LayoutMode.Columns
                },
                Items = new List<CollectionItem>
                {
                    Item("200")
                }
            };
            data.Sections.Insert(0, preceding);
            var old = SectionedLayout.Build(data, 240, Measure);
            preceding.Items[0].Size = ItemSize.Fixed(0, 50);
            old = SectionedLayout.Build(data, 240, Measure, old, new HashSet<string>());
            Same(SectionedLayout.Build(data, 240, Measure), old);
            old = SectionedLayout.Build(data, 240, Measure, old, new HashSet<string>());
            data.Sections[1].Items.Add(Item("20"));
            var next = SectionedLayout.Build(data, 240, Measure, old, new HashSet<string>());
            Same(SectionedLayout.Build(data, 240, Measure), next);
            Assert.IsTrue(next.TryGetItem("20", out var appended));
            Assert.AreEqual(1, appended.SectionIndex);
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        public void AppendKeepsNewDataReferencesWithoutMutatingPreviousEntries(LayoutMode mode)
        {
            var data = Data(mode);
            object previousData = new object (), nextData = new object ();
            data.Sections[0].Items[0].Data = previousData;
            var old = SectionedLayout.Build(data, 240, Measure);
            data.Sections[0].Items[0].Data = nextData;
            data.Sections[0].Items.Add(Item("20"));
            var next = SectionedLayout.Build(data, 240, Measure, old, new HashSet<string>());
            Assert.IsTrue(old.TryGetItem("0", out var oldItem));
            Assert.IsTrue(next.TryGetItem("0", out var newItem));
            Assert.AreSame(previousData, oldItem.Item.Data);
            Assert.AreSame(nextData, newItem.Item.Data);
            Assert.AreEqual(oldItem.Rect, newItem.Rect);
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        public void TenThousandItemsAppendMeasuresOnlyNewTail(LayoutMode mode)
        {
            var data = Data(mode);
            data.Sections[0].Header = data.Sections[0].Footer = null;
            for (int i = 20; i < 10000; i++)
                data.Sections[0].Items.Add(Item(i.ToString()));
            var old = SectionedLayout.Build(data, 240, Measure);
            for (int i = 10000; i < 10020; i++)
                data.Sections[0].Items.Add(Item(i.ToString()));
            int calls = 0;
            var next = SectionedLayout.Build(data, 240, (item, width) =>
            {
                calls++;
                return Measure(item, width);
            }, old, new HashSet<string>());
            Assert.AreEqual(20, calls);
            Assert.AreEqual(10000, old.Entries.Count);
            Assert.AreEqual(10020, next.Entries.Count);
            Assert.AreSame(old.Entries[9999], next.Entries[9999]);
            var expected = SectionedLayout.Build(data, 240, Measure);
            Assert.AreEqual(expected.ContentHeight, next.ContentHeight);
            for (int i = 9990; i < 10020; i++)
                Assert.AreEqual(expected.Entries[i].Rect, next.Entries[i].Rect);
            var queried = new List<int>();
            var viewport = new Rect(0, next.ContentHeight - 100, 240, 100);
            next.Query(viewport, queried);
            var reference = new List<int>();
            expected.Query(viewport, reference);
            CollectionAssert.AreEqual(reference, queried);
        }

        [Test]
        public void SkylineAndWidthChangeUseFullCalculation()
        {
            var data = Data(LayoutMode.Skyline);
            var old = SectionedLayout.Build(data, 240, Measure);
            data.Sections[0].Items.Add(Item("20"));
            int calls = 0;
            var next = SectionedLayout.Build(data, 240, (item, width) =>
            {
                calls++;
                return Measure(item, width);
            }, old, new HashSet<string>());
            Assert.AreEqual(23, calls);
            Same(SectionedLayout.Build(data, 240, Measure), next);
            data.Sections[0].Layout.Mode = LayoutMode.Flow;
            old = SectionedLayout.Build(data, 240, Measure);
            data.Sections[0].Items.Add(Item("21"));
            calls = 0;
            next = SectionedLayout.Build(data, 260, (item, width) =>
            {
                calls++;
                return Measure(item, width);
            }, old, new HashSet<string>());
            Assert.AreEqual(24, calls);
            Same(SectionedLayout.Build(data, 260, Measure), next);
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        public void DirtyOrChangedPrefixMustRecompute(LayoutMode mode)
        {
            var data = Data(mode);
            var old = SectionedLayout.Build(data, 240, Measure);
            data.Sections[0].Items.Add(Item("20"));
            int calls = 0;
            var dirty = SectionedLayout.Build(data, 240, (item, width) =>
            {
                calls++;
                return Measure(item, width);
            }, old, new HashSet<string> { "s" });
            Assert.AreEqual(23, calls);
            Same(SectionedLayout.Build(data, 240, Measure), dirty);
            data.Sections[0].Items[0].Version++;
            calls = 0;
            SectionedLayout.Build(data, 240, (item, width) =>
            {
                calls++;
                return Measure(item, width);
            }, old, new HashSet<string>());
            Assert.AreEqual(23, calls);
        }
    }
}
