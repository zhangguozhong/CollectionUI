using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CollectionUI.Tests
{
    public class CollectionIncrementalIndexTests
    {
        static CollectionItem Item(int id) => new CollectionItem
        {
            Id = id.ToString(),
            ReuseIdentifier = "cell",
            Size = ItemSize.Measured()
        };
        static Vector2 Measure(CollectionItem item, float width)
        {
            int id = int.Parse(item.Id);
            return new Vector2(Mathf.Min(width, 19 + id % 5 * 11), id % 11 == 0 ? 600 : 13 + id % 7 * 9);
        }

        static LayoutResult Append(LayoutResult old, CollectionSection section, int index, List<CollectionItem> added, Func<CollectionItem, float, Vector2> measure)
        {
            var method = typeof(SectionedLayout).GetMethod("AppendTail", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method, "Incremental tail API must exist");
            var prepare = typeof(CollectionItem).GetMethod("PrepareLayoutKey", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < added.Count; i++)
                added[i] = (CollectionItem)prepare.Invoke(added[i], new object[] { ElementKind.Item, section.Id });
            try
            {
                return (LayoutResult)method.Invoke(null, new object[] { old, section, index, added, measure });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        static void Same(LayoutResult expected, LayoutResult actual)
        {
            Assert.AreEqual(expected.ContentHeight, actual.ContentHeight);
            Assert.AreEqual(expected.Entries.Count, actual.Entries.Count);
            for (int i = 0; i < expected.Entries.Count; i++)
            {
                Assert.AreEqual(expected.Entries[i].Key, actual.Entries[i].Key);
                Assert.AreEqual(expected.Entries[i].Rect, actual.Entries[i].Rect, "rect " + i);
                Assert.True(actual.TryGetKey(expected.Entries[i].Key, out var entry));
                Assert.AreSame(actual.Entries[i], entry);
            }

            var a = new List<int>();
            var b = new List<int>();
            for (float y = 0; y < expected.ContentHeight; y += 137)
            {
                var viewport = new Rect(8, y, 200, 71);
                a.Clear();
                for (int i = 0; i < expected.Entries.Count; i++)
                    if (expected.Entries[i].Rect.Overlaps(viewport))
                        a.Add(i);
                a.Sort((x, z) =>
                {
                    int c = expected.Entries[x].Rect.yMin.CompareTo(expected.Entries[z].Rect.yMin);
                    return c == 0 ? x.CompareTo(z) : c;
                });
                actual.Query(viewport, b);
                CollectionAssert.AreEqual(a, b, "query " + y);
            }
        }

        [TestCase(LayoutMode.Columns, LayoutDirection.LeftToRight)]
        [TestCase(LayoutMode.Columns, LayoutDirection.RightToLeft)]
        [TestCase(LayoutMode.Flow, LayoutDirection.LeftToRight)]
        [TestCase(LayoutMode.Flow, LayoutDirection.RightToLeft)]
        public void HundredPagesPreserveOldIndexesAndMatchFullBuild(LayoutMode mode, LayoutDirection direction)
        {
            var section = new CollectionSection
            {
                Id = "tail",
                Header = Item(0),
                Footer = Item(0),
                Layout = new SectionLayoutSettings
                {
                    Mode = mode,
                    Direction = direction,
                    ColumnCount = 3,
                    Insets = new Insets(3, 5, 9, 13),
                    HorizontalSpacing = 7,
                    VerticalSpacing = 11
                }
            };
            for (int i = 0; i < 20; i++)
                section.Items.Add(Item(i));
            var data = new CollectionSnapshot
            {
                Sections = new List<CollectionSection>
                {
                    section
                }
            };
            var initial = SectionedLayout.Build(data, 240, Measure);
            var savedData = data.Copy();
            var current = initial;
            for (int page = 0; page < 100; page++)
            {
                // 新分区持有独立的列表；已发布结果保存的历史分区不得被后续追加修改。
                data = data.Copy();
                section = data.Sections[0];
                var added = new List<CollectionItem>();
                for (int n = 0; n < 20; n++)
                    added.Add(Item(section.Items.Count + n));
                section.Items.AddRange(added);
                int calls = 0;
                var next = Append(current, section, 0, added, (item, width) =>
                {
                    calls++;
                    return Measure(item, width);
                });
                Assert.AreEqual(20, calls, "Header and footer must not be measured again");
                Assert.AreSame(current.Entries[1], next.Entries[1]);
                Assert.True(next.TryGetItem((section.Items.Count - 1).ToString(), out var last));
                Assert.AreEqual(section.Items.Count - 1, last.ItemIndex);
                Assert.False(current.TryGetItem((section.Items.Count - 1).ToString(), out _));
                current = next;
            }

            Same(SectionedLayout.Build(savedData, 240, Measure), initial);
            var oldBranch = savedData.Copy();
            oldBranch.Sections[0].Items.Add(Item(20));
            Same(SectionedLayout.Build(oldBranch, 240, Measure), SectionedLayout.Build(oldBranch, 240, Measure, initial, new HashSet<string>()));
            Same(SectionedLayout.Build(data, 240, Measure), current);
            var indices = new List<int>();
            typeof(LayoutResult).GetMethod("AddIndicesForId", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(current, new object[] { "0", indices });
            CollectionAssert.AreEquivalent(new[] { 0, 1, current.Entries.Count - 1 }, indices);
            // 全量回退必须能继续消费持久化 Entries，并保留可续排状态。
            data.Sections[0].Items[0].Version++;
            Same(SectionedLayout.Build(data, 240, Measure), SectionedLayout.Build(data, 240, Measure, current, new HashSet<string>()));
        }

        [TestCase(255, false)]
        [TestCase(256, false)]
        [TestCase(257, false)]
        [TestCase(255, true)]
        [TestCase(256, true)]
        [TestCase(257, true)]
        public void BlockBoundariesPreserveEarlierSectionsAndSupplementaryIdentities(int count, bool footer)
        {
            var before = new CollectionSection
            {
                Id = "before",
                Header = Item(0),
                Footer = Item(0)
            };
            before.Items.Add(Item(90000));
            var tail = new CollectionSection
            {
                Id = "tail",
                Footer = footer ? Item(0) : null
            };
            for (int i = 0; i < count - 3 - (footer ? 1 : 0); i++)
                tail.Items.Add(Item(i));
            var data = new CollectionSnapshot
            {
                Sections = new List<CollectionSection>
                {
                    before,
                    tail
                }
            };
            var old = SectionedLayout.Build(data, 240, Measure);
            Assert.AreEqual(count, old.Entries.Count);
            data = data.Copy();
            tail = data.Sections[1];
            var added = new List<CollectionItem>();
            for (int i = 0; i < 50; i++)
                added.Add(Item(tail.Items.Count + i));
            tail.Items.AddRange(added);
            var next = Append(old, tail, 1, added, Measure);
            for (int i = 0; i < count - (footer ? 1 : 0); i++)
                Assert.AreSame(old.Entries[i], next.Entries[i]);
            Same(SectionedLayout.Build(data, 240, Measure), next);
            var indices = new List<int>();
            typeof(LayoutResult).GetMethod("AddIndicesForId", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(next, new object[] { "0", indices });
            CollectionAssert.AreEquivalent(footer ? new[] { 0, 2, 3, next.Entries.Count - 1 } : new[] { 0, 2, 3 }, indices);
        }

        [Test]
        public void InvalidNewSizeLeavesPreviousResultUntouched()
        {
            var section = new CollectionSection
            {
                Id = "s"
            };
            section.Items.Add(Item(0));
            var data = new CollectionSnapshot
            {
                Sections = new List<CollectionSection>
                {
                    section
                }
            };
            var previous = SectionedLayout.Build(data, 240, Measure);
            var added = new List<CollectionItem>
            {
                Item(1)
            };
            section = data.Copy().Sections[0];
            section.Items.AddRange(added);
            Assert.Throws<ArgumentException>(() => Append(previous, section, 0, added, (item, width) => new Vector2(width, float.NaN)));
            Same(SectionedLayout.Build(data, 240, Measure), previous);
            Assert.False(previous.TryGetItem("1", out _));
        }
    }
}
