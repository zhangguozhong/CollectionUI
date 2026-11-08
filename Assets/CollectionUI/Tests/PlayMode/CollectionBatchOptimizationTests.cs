using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

namespace CollectionUI.Tests
{
    public sealed class CollectionBatchOptimizationTests
    {
        static readonly MethodInfo ApplyMethod = typeof(CollectionChangeSet).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
        static CollectionItem Item(string id) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(100, 30)
        };
        static CollectionSnapshot Apply(CollectionChangeSet changes, CollectionSnapshot source, out HashSet<string> forced)
        {
            var arguments = new object[]
            {
                source,
                null
            };
            try
            {
                var result = (CollectionSnapshot)ApplyMethod.Invoke(changes, arguments);
                forced = (HashSet<string>)arguments[1];
                return result;
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException;
            }
        }

        [Test]
        public void LargeUpdateBatchAllocatesOnceForSnapshotRatherThanForEachOperation()
        {
            var source = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "s"
            };
            source.Sections.Add(section);
            for (int i = 0; i < 10000; i++)
                section.Items.Add(Item("item-" + i));
            var changes = new CollectionChangeSet();
            for (int i = 9000; i < 10000; i++)
            {
                var replacement = Item("item-" + i);
                replacement.Version = 7;
                changes.UpdateItem(replacement.Id, replacement);
            }

            // 先预热反射及事务入口；阈值只约束分配量，不依赖编辑器计时抖动。
            Apply(new CollectionChangeSet().UpdateItem("item-0", Item("item-0")), source, out _);
            long probeBefore = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[4096]);
            bool allocationCounterSupported = GC.GetAllocatedBytesForCurrentThread() > probeBefore;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var result = Apply(changes, source, out var forced);
            watch.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            UnityEngine.Debug.Log($"CollectionChangeSet 10000 items / 1000 updates: {watch.Elapsed.TotalMilliseconds:F2} ms, " + (allocationCounterSupported ? $"{bytes} allocated bytes" : "allocation counter unsupported on this runtime"));
            Assert.That(result.Sections[0].Items[9999].Version, Is.EqualTo(7));
            Assert.That(source.Sections[0].Items[9999].Version, Is.EqualTo(0));
            Assert.That(forced.Count, Is.EqualTo(1000));
            if (allocationCounterSupported)
                Assert.That(bytes, Is.LessThan(32L * 1024 * 1024), "事务不应为每次更新重建全快照身份集合。");
        }

        [Test]
        public void MixedOperationsKeepCurrentLocationsAndCopyQueuedMetadata()
        {
            var source = new CollectionSnapshot();
            source.Sections.Add(new CollectionSection { Id = "a", Items = new List<CollectionItem> { Item("x"), Item("y"), Item("z") } });
            source.Sections.Add(new CollectionSection { Id = "b", Items = new List<CollectionItem> { Item("w") } });
            var payload = new object ();
            var replacement = Item("x");
            replacement.Version = 5;
            replacement.Data = payload;
            var changes = new CollectionChangeSet().MoveItem("x", "a", 2).MoveItem("y", "b", 1).RemoveItem("z").InsertItem("a", 0, Item("z")).UpdateItem("x", replacement).MoveSection("a", 1);
            replacement.Version = 99;
            var result = Apply(changes, source, out var forced);
            Assert.That(result.Sections[0].Id, Is.EqualTo("b"));
            Assert.That(result.Sections[0].Items.ConvertAll(item => item.Id), Is.EqualTo(new[] { "w", "y" }));
            Assert.That(result.Sections[1].Items.ConvertAll(item => item.Id), Is.EqualTo(new[] { "z", "x" }));
            Assert.That(result.Sections[1].Items[1].Version, Is.EqualTo(5));
            Assert.That(result.Sections[1].Items[1].Data, Is.SameAs(payload));
            Assert.That(result.Sections[1].Items[1].LayoutKey, Is.EqualTo("I1:x"));
            Assert.That(result.Sections[1].Items[0].LayoutKey, Is.EqualTo("I1:z"));
            Assert.That(forced.Count, Is.EqualTo(2));
            Assert.That(source.Sections[0].Items.Count, Is.EqualTo(3));
        }

        [Test]
        public void IntermediateDuplicateFailsAndLeavesSourceUnchanged()
        {
            var source = new CollectionSnapshot();
            source.Sections.Add(new CollectionSection { Id = "a", Items = new List<CollectionItem> { Item("x") } });
            Assert.Throws<ArgumentException>(() => Apply(new CollectionChangeSet().InsertItem("a", 1, Item("x")).RemoveItem("x"), source, out _));
            Assert.That(source.Sections[0].Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void InvalidMoveIndexDiscardsEarlierChangesAndTransactionCanBeReused()
        {
            var source = new CollectionSnapshot();
            source.Sections.Add(new CollectionSection { Id = "a", Items = new List<CollectionItem> { Item("x"), Item("y") } });
            var invalid = new CollectionChangeSet().RemoveItem("y").MoveItem("x", "a", 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => Apply(invalid, source, out _));
            Assert.That(source.Sections[0].Items.ConvertAll(item => item.Id), Is.EqualTo(new[] { "x", "y" }));
            var section = new CollectionSection
            {
                Id = "b",
                Header = Item("h"),
                Items = new List<CollectionItem>
                {
                    Item("z")
                }
            };
            var changes = new CollectionChangeSet().InsertSection(1, section).MoveItem("z", "a", 0);
            section.Header.Id = "changed";
            section.Items.Clear();
            var first = Apply(changes, source, out var firstForced);
            first.Sections[0].Items[0].Version = 99;
            var second = Apply(changes, source, out var secondForced);
            Assert.That(second.Sections[0].Items[0].Version, Is.EqualTo(0));
            Assert.That(second.Sections[1].Header.Id, Is.EqualTo("h"));
            Assert.That(secondForced, Is.EquivalentTo(firstForced));
            Assert.That(secondForced.Count, Is.EqualTo(2));
        }

        [Test]
        public void SectionReplacementReleasesOldIdsAndRegistersNewLocations()
        {
            var source = new CollectionSnapshot();
            source.Sections.Add(new CollectionSection { Id = "a", Items = new List<CollectionItem> { Item("x") } });
            source.Sections.Add(new CollectionSection { Id = "b", Items = new List<CollectionItem> { Item("y") } });
            var replacement = new CollectionSection
            {
                Id = "a",
                Header = Item("h"),
                Footer = Item("f"),
                Items = new List<CollectionItem>
                {
                    Item("z")
                }
            };
            var changes = new CollectionChangeSet().UpdateSection("a", replacement).InsertItem("b", 1, Item("x")).MoveItem("z", "b", 0).RemoveSection("a").InsertSection(0, replacement).RemoveItem("y");
            // z 仍在 b 中，重新插入含 z 的分区应立即拒绝，不能被后续操作掩盖。
            Assert.Throws<ArgumentException>(() => Apply(changes, source, out _));
            var valid = new CollectionChangeSet().UpdateSection("a", replacement).InsertItem("b", 1, Item("x")).MoveItem("z", "b", 0).RemoveSection("a").InsertSection(0, new CollectionSection { Id = "a" }).RemoveItem("y");
            var result = Apply(valid, source, out var forced);
            Assert.That(result.Sections[1].Items.ConvertAll(item => item.Id), Is.EqualTo(new[] { "z", "x" }));
            Assert.That(forced.Count, Is.EqualTo(4));
            Assert.That(source.Sections[0].Items[0].Id, Is.EqualTo("x"));
        }
    }
}
