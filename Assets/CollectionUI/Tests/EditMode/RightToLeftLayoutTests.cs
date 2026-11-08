using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CollectionUI.Tests
{
    // 数据顺序不反转；RTL 只在分区可用宽度内镜像物理矩形。
    public class RightToLeftLayoutTests
    {
        static CollectionItem Item(string id, float width, float height) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(width, height)
        };
        static CollectionSnapshot Data(LayoutMode mode)
        {
            var section = new CollectionSection
            {
                Id = "s",
                Header = Item("h", 70, 20),
                Footer = Item("f", 90, 20),
                Layout = new SectionLayoutSettings
                {
                    Mode = mode,
                    ColumnCount = 3,
                    Insets = new Insets(13, 7, 29, 11),
                    HorizontalSpacing = 9,
                    VerticalSpacing = 5,
                    SectionSpacing = 17
                }
            };
            for (int i = 0; i < 12; i++)
                section.Items.Add(Item("i" + i, 35 + i % 3 * 9, i == 0 ? 400 : 25 + i % 4 * 13));
            return new CollectionSnapshot
            {
                Sections = new List<CollectionSection>
                {
                    section
                }
            };
        }

        static void Equivalent(LayoutResult full, LayoutResult incremental)
        {
            Assert.AreEqual(full.ContentHeight, incremental.ContentHeight);
            Assert.AreEqual(full.Entries.Count, incremental.Entries.Count);
            for (int i = 0; i < full.Entries.Count; i++)
            {
                Assert.AreEqual(full.Entries[i].Rect, incremental.Entries[i].Rect, "rect " + i);
                Assert.AreEqual(full.Entries[i].Key, incremental.Entries[i].Key);
                Assert.AreEqual(full.Entries[i].SectionIndex, incremental.Entries[i].SectionIndex);
                Assert.AreEqual(full.Entries[i].ItemIndex, incremental.Entries[i].ItemIndex);
            }

            var found = new List<int>();
            for (int y = 0; y < full.ContentHeight; y += 37)
            {
                // 窄视口同时检查横向索引行为，包含从视口上方延伸进来的高条目。
                var region = new Rect(150, y, 130, 60);
                incremental.Query(region, found);
                var expected = new List<int>();
                for (int i = 0; i < full.Entries.Count; i++)
                    if (full.Entries[i].Rect.Overlaps(region))
                        expected.Add(i);
                CollectionAssert.AreEquivalent(expected, found);
            }
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        [TestCase(LayoutMode.Skyline)]
        public void MirrorsAllElementsWithinPhysicalInsetsWithoutChangingOrder(LayoutMode mode)
        {
            var data = Data(mode);
            Assert.AreEqual(LayoutDirection.LeftToRight, data.Sections[0].Layout.Direction);
            var left = SectionedLayout.Build(data, 300);
            data.Sections[0].Layout.Direction = LayoutDirection.RightToLeft;
            var right = SectionedLayout.Build(data, 300);
            Assert.AreEqual(left.ContentHeight, right.ContentHeight);
            for (int i = 0; i < left.Entries.Count; i++)
            {
                var l = left.Entries[i];
                var r = right.Entries[i];
                Assert.AreEqual(300 - 29 - (l.Rect.x - 13) - l.Rect.width, r.Rect.x, .001f);
                Assert.AreEqual(l.Rect.y, r.Rect.y);
                Assert.AreEqual(l.Rect.size, r.Rect.size);
                Assert.AreEqual(l.Key, r.Key);
                Assert.AreEqual(l.ItemIndex, r.ItemIndex);
                Assert.GreaterOrEqual(r.Rect.xMin, 13);
                Assert.LessOrEqual(r.Rect.xMax, 271);
            }
        }

        [Test]
        public void FlowExactFitThenWrapStartsNextRowAtRightEdge()
        {
            var data = Data(LayoutMode.Flow);
            var section = data.Sections[0];
            section.Header = section.Footer = null;
            section.Layout.Direction = LayoutDirection.RightToLeft;
            section.Items = new List<CollectionItem>
            {
                Item("a", 100, 20),
                Item("b", 149, 30),
                Item("c", 40, 10)
            };
            var result = SectionedLayout.Build(data, 300);
            Assert.AreEqual(new Rect(171, 7, 100, 20), result.Entries[0].Rect);
            Assert.AreEqual(new Rect(13, 7, 149, 30), result.Entries[1].Rect);
            Assert.AreEqual(new Rect(231, 42, 40, 10), result.Entries[2].Rect);
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        [TestCase(LayoutMode.Skyline)]
        public void DirectionSwitchInvalidatesGeometryAndAppendContinuation(LayoutMode mode)
        {
            var data = Data(mode);
            var left = SectionedLayout.Build(data, 300);
            data.Sections[0].Layout.Direction = LayoutDirection.RightToLeft;
            data.Sections[0].Items.Add(Item("tail", 40, 18));
            var right = SectionedLayout.Build(data, 300, null, left, new HashSet<string>());
            Equivalent(SectionedLayout.Build(data, 300), right);
            Assert.AreEqual(0, right.ReusedSectionCount);
            Assert.AreEqual(13, left.Entries[0].Rect.x);
            data.Sections[0].Layout.Direction = LayoutDirection.LeftToRight;
            Equivalent(SectionedLayout.Build(data, 300), SectionedLayout.Build(data, 300, null, right, new HashSet<string>()));
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        [TestCase(LayoutMode.Skyline)]
        public void MixedSectionsReuseAndShiftDoNotMirrorTwice(LayoutMode mode)
        {
            var data = Data(mode);
            var section = data.Sections[0];
            section.Layout.Direction = LayoutDirection.RightToLeft;
            var before = new CollectionSection
            {
                Id = "before",
                Items = new List<CollectionItem>
                {
                    Item("before", 40, 20)
                }
            };
            data.Sections.Insert(0, before);
            var old = SectionedLayout.Build(data, 300);
            before.Items[0].Size = ItemSize.Fixed(40, 70);
            var shifted = SectionedLayout.Build(data, 300, null, old, new HashSet<string>());
            Equivalent(SectionedLayout.Build(data, 300), shifted);
            var reused = SectionedLayout.Build(data, 300, null, shifted, new HashSet<string>());
            Equivalent(shifted, reused);
            Assert.AreEqual(2, reused.ReusedSectionCount);
            Assert.AreEqual(0, reused.Entries[0].Rect.x);
            Assert.AreEqual(201, reused.Entries[1].Rect.x);
        }

        [TestCase(LayoutMode.Columns)]
        [TestCase(LayoutMode.Flow)]
        public void RepeatedRtlAppendKeepsContinuationAndOldEntries(LayoutMode mode)
        {
            var data = Data(mode);
            data.Sections[0].Layout.Direction = LayoutDirection.RightToLeft;
            var old = SectionedLayout.Build(data, 300);
            for (int i = 0; i < 3; i++)
            {
                var oldRect = old.Entries[1].Rect;
                data.Sections[0].Items.Add(Item("tail" + i, 37 + i * 8, 21));
                var next = SectionedLayout.Build(data, 300, null, old, new HashSet<string>());
                Equivalent(SectionedLayout.Build(data, 300), next);
                Assert.AreSame(old.Entries[1], next.Entries[1]);
                Assert.AreEqual(oldRect, old.Entries[1].Rect);
                old = next;
            }
        }

        [TestCase(100f, 2, 3.2f)]
        [TestCase(375f, 3, 7f)]
        [TestCase(1032f, 6, 9.7f)]
        public void FractionalColumnsCannotCrossLeftBoundaryThroughRounding(float width, int columns, float gap)
        {
            var data = Data(LayoutMode.Columns);
            var section = data.Sections[0];
            section.Header = section.Footer = null;
            section.Layout.Insets = new Insets();
            section.Layout.Direction = LayoutDirection.RightToLeft;
            section.Layout.ColumnCount = columns;
            section.Layout.HorizontalSpacing = gap;
            section.Items.Clear();
            for (int i = 0; i < columns; i++)
                section.Items.Add(Item("i" + i, 0, 20));
            var result = SectionedLayout.Build(data, width);
            foreach (var entry in result.Entries)
                Assert.GreaterOrEqual(entry.Rect.x, 0);
            Assert.That(result.Entries[columns - 1].Rect.x, Is.EqualTo(0).Within(.001f));
        }

        [Test]
        public void InvalidDirectionIsRejectedEvenForHiddenEmptySection()
        {
            var data = Data(LayoutMode.Flow);
            data.Sections[0].Items.Clear();
            data.Sections[0].Layout.Direction = (LayoutDirection)99;
            Assert.Throws<ArgumentException>(() => SectionedLayout.Build(data, 300));
        }
    }
}
