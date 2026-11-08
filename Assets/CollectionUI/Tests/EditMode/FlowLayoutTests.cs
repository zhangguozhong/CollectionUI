// Flow 几何测试：验证输入顺序、恰好容纳时不换行、行高取最大值、行列间距和跨组布局。
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CollectionUI.Tests
{
    public class FlowLayoutTests
    {
        static CollectionItem Item(string id, float width, float height = 20) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "tag",
            Size = ItemSize.Fixed(width, height)
        };
        static CollectionSection Section(string id, params CollectionItem[] items) => new CollectionSection
        {
            Id = id,
            Items = new List<CollectionItem>(items),
            Layout = new SectionLayoutSettings
            {
                Mode = LayoutMode.Flow,
                HorizontalSpacing = 10,
                VerticalSpacing = 5
            }
        };
        static CollectionSnapshot Snapshot(params CollectionItem[] items) => new CollectionSnapshot
        {
            Sections = new List<CollectionSection>
            {
                Section("first", items)
            }
        };
        [Test]
        public void ExactFitStaysOnRowWithoutTrailingHorizontalSpacing()
        {
            var result = SectionedLayout.Build(Snapshot(Item("a", 40), Item("b", 50)), 100);
            Assert.AreEqual(new Rect(0, 0, 40, 20), result.Entries[0].Rect);
            Assert.AreEqual(new Rect(50, 0, 50, 20), result.Entries[1].Rect);
            Assert.AreEqual(20, result.ContentHeight);
        }

        [Test]
        public void WidthPlusGapWrapsAndKeepsSourceOrderInsteadOfFillingEarlierHoles()
        {
            var result = SectionedLayout.Build(Snapshot(Item("a", 60, 10), Item("b", 50, 30), Item("c", 20, 10), Item("d", 30, 10)), 100);
            Assert.AreEqual(new Rect(0, 15, 50, 30), result.Entries[1].Rect);
            Assert.AreEqual(new Rect(60, 15, 20, 10), result.Entries[2].Rect);
            Assert.AreEqual(new Rect(0, 50, 30, 10), result.Entries[3].Rect);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(((char)('a' + i)).ToString(), result.Entries[i].Item.Id);
                Assert.AreEqual(i, result.Entries[i].ItemIndex);
            }
        }

        [Test]
        public void RowsUseMaximumHeightAndTopAlignment()
        {
            var result = SectionedLayout.Build(Snapshot(Item("a", 40, 80), Item("b", 50, 10), Item("c", 100, 15)), 100);
            Assert.AreEqual(0, result.Entries[1].Rect.y);
            Assert.AreEqual(85, result.Entries[2].Rect.y);
            Assert.AreEqual(100, result.ContentHeight);
        }

        [Test]
        public void GroupsResetRowsAndOwnInsetsSupplementariesAndSectionSpacing()
        {
            var snapshot = Snapshot(Item("a", 40, 20));
            var first = snapshot.Sections[0];
            first.Layout.Insets = new Insets(3, 7, 11, 13);
            first.Layout.SectionSpacing = 17;
            first.Header = Item("header", 0, 10);
            first.Footer = Item("footer", 0, 12);
            var second = Section("second", Item("b", 30, 25));
            second.Layout.Insets = new Insets(5, 9, 7, 4);
            snapshot.Sections.Add(second);
            var result = SectionedLayout.Build(snapshot, 114);
            Assert.AreEqual(new Rect(3, 7, 100, 10), result.Entries[0].Rect);
            Assert.AreEqual(new Rect(3, 22, 40, 20), result.Entries[1].Rect);
            Assert.AreEqual(new Rect(3, 47, 100, 12), result.Entries[2].Rect);
            Assert.AreEqual(new Rect(5, 98, 30, 25), result.Entries[3].Rect);
            Assert.AreEqual(127, result.ContentHeight);
        }

        [Test]
        public void ResizingReflowsPreviouslyReusedSections()
        {
            var snapshot = Snapshot(Item("a", 50), Item("b", 50));
            var wide = SectionedLayout.Build(snapshot, 110);
            var narrow = SectionedLayout.Build(snapshot, 100, null, wide, new HashSet<string>());
            Assert.AreEqual(0, wide.Entries[1].Rect.y);
            Assert.AreEqual(25, narrow.Entries[1].Rect.y);
            Assert.AreEqual(0, narrow.ReusedSectionCount);
            Assert.AreEqual(1, narrow.RecomputedSectionCount);
        }

        [Test]
        public void MeasuredWidthsUseSectionWidthAndResolvedWidthForWrapping()
        {
            var a = Item("a", 0);
            a.Size = ItemSize.Measured();
            var b = Item("b", 0);
            b.Size = ItemSize.Measured(80);
            var c = Item("c", 0);
            c.Size = ItemSize.Aspect(0, 2);
            var snapshot = Snapshot(a, b, c);
            snapshot.Sections[0].Layout.Insets = new Insets(4, 0, 6, 0);
            var widths = new List<float>();
            var result = SectionedLayout.Build(snapshot, 110, (item, width) =>
            {
                widths.Add(width);
                return new Vector2(item.Id == "a" ? 40 : 50, 20);
            });
            CollectionAssert.AreEqual(new[] { 100f, 80f }, widths);
            Assert.AreEqual(new Rect(54, 0, 50, 20), result.Entries[1].Rect);
            Assert.AreEqual(new Rect(4, 25, 100, 50), result.Entries[2].Rect);
        }

        [Test]
        public void OversizedFixedAndMeasuredWidthsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => SectionedLayout.Build(Snapshot(Item("a", 101)), 100));
            var measured = Item("b", 0);
            measured.Size = ItemSize.Measured();
            Assert.Throws<ArgumentException>(() => SectionedLayout.Build(Snapshot(measured), 100, (item, width) => new Vector2(101, 20)));
        }

        [Test]
        public void UnchangedGroupsReuseGeometryAfterEarlierGroupChanges()
        {
            var snapshot = Snapshot(Item("a", 60));
            var measured = Item("b", 0);
            measured.Size = ItemSize.Measured();
            snapshot.Sections.Add(Section("second", measured, Item("c", 40)));
            int calls = 0;
            Func<CollectionItem, float, Vector2> measure = (item, width) =>
            {
                calls++;
                return new Vector2(50, 30);
            };
            var previous = SectionedLayout.Build(snapshot, 100, measure);
            snapshot.Sections[0].Items.Add(Item("d", 60));
            var reused = SectionedLayout.Build(snapshot, 100, measure, previous, new HashSet<string>());
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, reused.ReusedSectionCount);
            var full = SectionedLayout.Build(snapshot, 100, measure);
            Assert.AreEqual(full.ContentHeight, reused.ContentHeight);
            for (int i = 0; i < full.Entries.Count; i++)
                Assert.AreEqual(full.Entries[i].Rect, reused.Entries[i].Rect);
        }

        [Test]
        public void TenThousandItemsQueriesMatchBruteForceIncludingTallRows()
        {
            var snapshot = Snapshot();
            for (int i = 0; i < 10000; i++)
                snapshot.Sections[0].Items.Add(Item(i.ToString(), 20 + i % 61, i % 257 == 0 ? 1000 : 20 + i % 19));
            var result = SectionedLayout.Build(snapshot, 320);
            var actual = new List<int>();
            for (int q = 0; q < 40; q++)
            {
                var viewport = new Rect(q % 2 == 0 ? 0 : 65, result.ContentHeight * q / 40, 220, 300);
                result.Query(viewport, actual);
                var expected = new List<int>();
                for (int i = 0; i < result.Entries.Count; i++)
                    if (result.Entries[i].Rect.Overlaps(viewport))
                        expected.Add(i);
                CollectionAssert.AreEquivalent(expected, actual);
            }
        }
    }
}
