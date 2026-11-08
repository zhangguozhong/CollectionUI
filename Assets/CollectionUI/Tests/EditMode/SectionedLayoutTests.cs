// 纯几何布局测试。无需实例化 Cell，验证列布局/天际线布局、分组、增量复用、可见性索引及输入校验。
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CollectionUI.Tests
{
    public class SectionedLayoutTests
    {
        static CollectionItem Item(string id, float w, float h) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(w, h)
        };
        static CollectionSnapshot Snapshot(params CollectionItem[] items) => new CollectionSnapshot
        {
            Sections = new List<CollectionSection>
            {
                new CollectionSection
                {
                    Id = "s",
                    Items = new List<CollectionItem>(items),
                    Layout = new SectionLayoutSettings
                    {
                        ColumnCount = 2,
                        HorizontalSpacing = 10,
                        VerticalSpacing = 5
                    }
                }
            }
        };
        [Test]
        public void ColumnsUseShortestColumnAndPreserveTallVisibility()
        {
            var s = Snapshot(Item("a", 0, 1000), Item("b", 0, 10), Item("c", 0, 20));
            var l = SectionedLayout.Build(s, 210);
            Assert.AreEqual(new Rect(110, 15, 100, 20), l.Entries[2].Rect);
            var found = new List<int>();
            l.Query(new Rect(0, 500, 210, 20), found);
            Assert.AreEqual(new[] { 0 }, found);
            Assert.AreEqual(1000, l.ContentHeight);
        }

        [Test]
        public void BuildCopiesInputsAndRejectsDuplicateIdentifiers()
        {
            var s = Snapshot(Item("a", 0, 20));
            var l = SectionedLayout.Build(s, 210);
            s.Sections[0].Items[0].Id = "changed";
            s.Sections[0].Items.Clear();
            Assert.AreEqual("a", l.Entries[0].Item.Id);
            Assert.Throws<ArgumentException>(() => SectionedLayout.Build(Snapshot(Item("x", 0, 1), Item("x", 0, 2)), 210));
        }

        [Test]
        public void SectionsHeadersFootersAndInsetsHaveOwnedSpacing()
        {
            var s = Snapshot(Item("a", 0, 20));
            var sec = s.Sections[0];
            sec.Layout.Insets = new Insets(3, 7, 11, 13);
            sec.Header = Item("h", 0, 10);
            sec.Footer = Item("f", 0, 12);
            var l = SectionedLayout.Build(s, 210);
            Assert.AreEqual(new Rect(3, 7, 196, 10), l.Entries[0].Rect);
            Assert.AreEqual(22, l.Entries[1].Rect.y);
            Assert.AreEqual(47, l.Entries[2].Rect.y);
            Assert.AreEqual(72, l.ContentHeight);
        }

        [Test]
        public void SkylineSupportsArbitraryWidthsWithoutOverlap()
        {
            var s = Snapshot(Item("a", 120, 30), Item("b", 70, 50), Item("c", 90, 40), Item("d", 200, 10));
            s.Sections[0].Layout.Mode = LayoutMode.Skyline;
            var l = SectionedLayout.Build(s, 210);
            Assert.AreEqual(new Rect(130, 0, 70, 50), l.Entries[1].Rect);
            Assert.AreEqual(new Rect(0, 35, 90, 40), l.Entries[2].Rect);
            for (int i = 0; i < l.Entries.Count; i++)
                for (int j = i + 1; j < l.Entries.Count; j++)
                    Assert.IsFalse(l.Entries[i].Rect.Overlaps(l.Entries[j].Rect));
        }

        [Test]
        public void SkylineReservesRightSpacingBeforeHigherNeighbors()
        {
            var s = Snapshot(Item("a", 120, 30), Item("b", 70, 100), Item("c", 125, 1));
            s.Sections[0].Layout.Mode = LayoutMode.Skyline;
            var l = SectionedLayout.Build(s, 210);
            Assert.AreEqual(105, l.Entries[2].Rect.y);
        }

        [Test]
        public void AspectAndMeasuredSizesAreResolvedAgainstAvailableWidth()
        {
            var a = Item("a", 0, 1);
            a.Size = ItemSize.Aspect(0, 2);
            var b = Item("b", 0, 1);
            b.Size = ItemSize.Measured();
            var l = SectionedLayout.Build(Snapshot(a, b), 210, (item, width) => new Vector2(width, 37));
            Assert.AreEqual(50, l.Entries[0].Rect.height);
            Assert.AreEqual(37, l.Entries[1].Rect.height);
        }

        [Test]
        public void RejectsInvalidWidthsAndNonFiniteValues()
        {
            Assert.Throws<ArgumentException>(() => SectionedLayout.Build(Snapshot(Item("a", 101, 10)), 210));
            Assert.Throws<ArgumentException>(() => SectionedLayout.Build(Snapshot(Item("a", 0, float.NaN)), 210));
            Assert.Throws<ArgumentException>(() => SectionedLayout.Build(Snapshot(Item("a", 0, 10)), float.PositiveInfinity));
        }

        [Test]
        public void EmptySectionsAreHiddenUnlessRequestedAndKeysAreScoped()
        {
            var s = Snapshot();
            var section = s.Sections[0];
            section.Header = Item("same", 0, 10);
            section.Footer = Item("same", 0, 12);
            Assert.AreEqual(0, SectionedLayout.Build(s, 210).Entries.Count);
            section.Layout.ShowEmptySection = true;
            var second = new CollectionSection
            {
                Id = "second",
                Header = Item("same", 0, 15),
                Layout = new SectionLayoutSettings
                {
                    ShowEmptySection = true
                }
            };
            s.Sections.Add(second);
            var l = SectionedLayout.Build(s, 210);
            Assert.AreEqual(3, l.Entries.Count);
            var keys = new HashSet<string>();
            foreach (var entry in l.Entries)
            {
                Assert.IsTrue(keys.Add(entry.Key));
                Assert.IsTrue(l.TryGetKey(entry.Key, out var found));
                Assert.AreSame(entry, found);
            }
        }

        [Test]
        public void MeasurementsReceiveStableScopedKeysAndCopiedVersion()
        {
            var s = Snapshot(Item("a", 0, 10));
            s.Sections[0].Header = Item("same", 0, 1);
            s.Sections[0].Header.Size = ItemSize.Measured();
            s.Sections[0].Header.Version = 7;
            var seen = new List<string>();
            var l = SectionedLayout.Build(s, 210, (item, width) =>
            {
                seen.Add(item.LayoutKey);
                Assert.AreEqual(7, item.Version);
                return new Vector2(width, 20);
            });
            Assert.AreEqual(l.Entries[0].Key, seen[0]);
        }

        [Test]
        public void PublishedItemMetadataIsFrozenButCopiesRemainWritable()
        {
            var data = new List<int>();
            var input = Item("a", 0, 10);
            input.Data = data;
            var l = SectionedLayout.Build(Snapshot(input), 210);
            var item = l.Entries[0].Item;
            Assert.Throws<InvalidOperationException>(() => item.Id = "changed");
            Assert.Throws<InvalidOperationException>(() => item.ReuseIdentifier = "changed");
            Assert.Throws<InvalidOperationException>(() => item.Size = ItemSize.Fixed(1, 1));
            Assert.Throws<InvalidOperationException>(() => item.Version++);
            Assert.Throws<InvalidOperationException>(() => item.Data = new object ());
            Assert.AreSame(data, item.Data);
            data.Add(1);
            Assert.AreEqual(1, ((List<int>)item.Data).Count);
            var copy = item.Copy();
            copy.Id = "copy";
            Assert.AreEqual("a", item.Id);
            input.Id = "input";
        }

        [Test]
        public void UnchangedSectionsReuseGeometryAndRefreshDataAfterEarlierSectionChanges()
        {
            var s = Snapshot(Item("a", 0, 10));
            var b = Item("b", 0, 1);
            b.Size = ItemSize.Measured();
            s.Sections.Add(new CollectionSection { Id = "second", Items = new List<CollectionItem> { b } });
            int calls = 0;
            Func<CollectionItem, float, Vector2> measure = (item, width) =>
            {
                calls++;
                return new Vector2(width, 30);
            };
            var first = SectionedLayout.Build(s, 210, measure);
            Assert.AreEqual(1, calls);
            s.Sections[0].Items[0].Size = ItemSize.Fixed(0, 100);
            var data = new object ();
            b.Data = data;
            var next = SectionedLayout.Build(s, 210, measure, first, new HashSet<string>());
            Assert.AreEqual(1, calls);
            Assert.AreEqual(100, next.Entries[1].Rect.y);
            Assert.AreSame(data, next.Entries[1].Item.Data);
            Assert.AreEqual(1, next.ReusedSectionCount);
            Assert.AreEqual(1, next.RecomputedSectionCount);
            Assert.AreEqual(10, first.Entries[1].Rect.y);
        }

        [Test]
        public void DirtySectionsVersionsAndWidthChangesInvalidateReuse()
        {
            var a = Item("a", 0, 1);
            a.Size = ItemSize.Measured();
            var s = Snapshot(a);
            int calls = 0;
            Func<CollectionItem, float, Vector2> measure = (item, width) =>
            {
                calls++;
                return new Vector2(width, 20);
            };
            var first = SectionedLayout.Build(s, 210, measure);
            var reused = SectionedLayout.Build(s, 210, measure, first, new HashSet<string>());
            Assert.AreEqual(1, calls);
            var forced = SectionedLayout.Build(s, 210, measure, reused, new HashSet<string> { "s" });
            Assert.AreEqual(2, calls);
            a.Version++;
            var versioned = SectionedLayout.Build(s, 210, measure, forced, new HashSet<string>());
            Assert.AreEqual(3, calls);
            var resized = SectionedLayout.Build(s, 310, measure, versioned, new HashSet<string>());
            Assert.AreEqual(4, calls);
            SectionedLayout.Build(s, 310, measure, resized);
            Assert.AreEqual(5, calls);
        }

        [Test]
        public void ReusedReorderedSectionsMatchAFullRebuildIncludingSupplementaries()
        {
            var s = Snapshot(Item("a", 0, 20));
            var first = s.Sections[0];
            first.Header = Item("h", 0, 8);
            first.Footer = Item("f", 0, 7);
            first.Layout.Insets = new Insets(2, 3, 4, 5);
            first.Layout.SectionSpacing = 11;
            var second = new CollectionSection
            {
                Id = "second",
                Items = new List<CollectionItem>
                {
                    Item("b", 50, 12)
                },
                Layout = new SectionLayoutSettings
                {
                    Mode = LayoutMode.Skyline,
                    Insets = new Insets(3, 2, 3, 2),
                    SectionSpacing = 6
                }
            };
            s.Sections.Add(second);
            var old = SectionedLayout.Build(s, 210);
            s.Sections.Reverse();
            var reused = SectionedLayout.Build(s, 210, null, old, new HashSet<string>());
            var full = SectionedLayout.Build(s, 210);
            Assert.AreEqual(2, reused.ReusedSectionCount);
            Assert.AreEqual(full.ContentHeight, reused.ContentHeight);
            for (int i = 0; i < full.Entries.Count; i++)
            {
                Assert.AreEqual(full.Entries[i].Key, reused.Entries[i].Key);
                Assert.AreEqual(full.Entries[i].Rect, reused.Entries[i].Rect);
                Assert.AreEqual(full.Entries[i].SectionIndex, reused.Entries[i].SectionIndex);
            }
        }

        [Test]
        public void RandomSkylineMaintainsBoundsAndSpacingAcrossWidths()
        {
            var random = new System.Random(7231);
            foreach (float width in new[]
            {
                177f,
                320f,
                1024f
            }

            )
            {
                var items = new List<CollectionItem>();
                for (int i = 0; i < 200; i++)
                    items.Add(Item(i.ToString(), 10 + (float)random.NextDouble() * (width - 20), 1 + random.Next(200)));
                var s = Snapshot(items.ToArray());
                s.Sections[0].Layout.Mode = LayoutMode.Skyline;
                var l = SectionedLayout.Build(s, width);
                for (int i = 0; i < l.Entries.Count; i++)
                {
                    var a = l.Entries[i].Rect;
                    Assert.GreaterOrEqual(a.x, 0);
                    Assert.LessOrEqual(a.xMax, width);
                    for (int j = 0; j < i; j++)
                    {
                        var b = l.Entries[j].Rect;
                        const float epsilon = 0.001f;
                        Assert.IsTrue(a.xMax + 10 <= b.x + epsilon || b.xMax + 10 <= a.x + epsilon || a.yMax + 5 <= b.y + epsilon || b.yMax + 5 <= a.y + epsilon, "Spacing violation at " + i + " / " + j);
                    }
                }
            }
        }

        [Test]
        public void TenThousandItemQueriesMatchBruteForceWithTallOutliers()
        {
            var items = new List<CollectionItem>();
            for (int i = 0; i < 10000; i++)
                items.Add(Item(i.ToString(), 0, i % 257 == 0 ? 10000 : 30));
            var l = SectionedLayout.Build(Snapshot(items.ToArray()), 210);
            var actual = new List<int>();
            for (int q = 0; q < 25; q++)
            {
                var rect = new Rect(0, l.ContentHeight * q / 25, 210, 300);
                l.Query(rect, actual);
                var expected = new List<int>();
                for (int i = 0; i < l.Entries.Count; i++)
                    if (l.Entries[i].Rect.Overlaps(rect))
                        expected.Add(i);
                CollectionAssert.AreEquivalent(expected, actual);
            }
        }

        sealed class CustomLayout : ICollectionLayout
        {
            public LayoutResult Build(CollectionSnapshot snapshot, float width, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, ISet<string> dirtySections = null)
            {
                var entry = new LayoutEntry(snapshot.Sections[0].Items[0], "custom", 0, 0, ElementKind.Item, new Rect(0, 20, width, 30));
                return LayoutResult.Create(new[] { entry }, width, 50);
            }
        }

        [Test]
        public void ExternalLayoutCanPublishFrozenIndexedGeometry()
        {
            var s = Snapshot(Item("a", 0, 10));
            ICollectionLayout engine = new CustomLayout();
            var result = engine.Build(s, 210);
            s.Sections[0].Items[0].Id = "changed";
            Assert.AreEqual("a", result.Entries[0].Item.Id);
            Assert.Throws<InvalidOperationException>(() => result.Entries[0].Item.Id = "changed");
            var found = new List<int>();
            result.Query(new Rect(0, 25, 210, 10), found);
            CollectionAssert.AreEqual(new[] { 0 }, found);
            Assert.IsTrue(result.TryGetItem("a", out var entry));
            Assert.AreEqual(entry.Key, entry.Item.LayoutKey);
            Assert.AreEqual(10, new DefaultCollectionLayout().Build(Snapshot(Item("b", 0, 10)), 210).ContentHeight);
        }

        [Test]
        public void ExternalLayoutFactoryRejectsInvalidGeometryAndDuplicateIdentity()
        {
            var item = Item("a", 0, 10);
            Assert.Throws<ArgumentException>(() => new LayoutEntry(item, "s", 0, 0, ElementKind.Item, new Rect(0, 0, float.NaN, 10)));
            Assert.Throws<ArgumentException>(() => new LayoutEntry(item, "", 0, 0, ElementKind.Item, new Rect(0, 0, 10, 10)));
            var entry = new LayoutEntry(item, "s", 0, 0, ElementKind.Item, new Rect(0, 0, 10, 10));
            Assert.Throws<ArgumentException>(() => LayoutResult.Create(new[] { entry, entry }, 100, 100));
            Assert.Throws<ArgumentException>(() => LayoutResult.Create(new LayoutEntry[] { null }, 100, 100));
            Assert.Throws<ArgumentException>(() => LayoutResult.Create(new[] { entry }, float.PositiveInfinity, 100));
        }

        [Test]
        public void IndexedQueryMatchesBruteForceAcrossManyHeights()
        {
            var items = new List<CollectionItem>();
            for (int i = 0; i < 300; i++)
                items.Add(Item(i.ToString(), 0, i % 29 == 0 ? 2000 : 13 + i % 17));
            var l = SectionedLayout.Build(Snapshot(items.ToArray()), 210);
            var found = new List<int>();
            for (int y = 0; y < l.ContentHeight; y += 53)
            {
                var r = new Rect(0, y, 210, 60);
                l.Query(r, found);
                var expected = new List<int>();
                for (int i = 0; i < l.Entries.Count; i++)
                    if (l.Entries[i].Rect.Overlaps(r))
                        expected.Add(i);
                CollectionAssert.AreEquivalent(expected, found);
            }
        }
    }
}
