// 几何与文字缓存回归：身份读取不重复分配，缓存满载时保留其余常用结果。
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CollectionUI.Tests
{
    public class CollectionGeometryOptimizationTests
    {
        Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [TestCase(ElementKind.Item, 0)]
        [TestCase(ElementKind.Header, -1)]
        [TestCase(ElementKind.Footer, -2)]
        public void PublishedEntryReusesItsIdentityString(ElementKind kind, int index)
        {
            var entry = new LayoutEntry(Item("identity"), "section", 0, index, kind, new Rect(0, 0, 100, 20));
            Assert.That(entry.Key, Is.SameAs(entry.Key));
            Assert.That(entry.Key, Is.EqualTo(entry.Item.LayoutKey));
        }

        [Test]
        public void TextCacheEvictsOneEntryInsteadOfDiscardingTheWorkingSet()
        {
            var text = new UguiTextMeasurer(Font, 20, cacheCapacity: 4);
            for (int i = 0; i < 5; i++)
                text.SingleLineWidth("word " + i);
            Assert.That(text.CachedCount, Is.EqualTo(4));
            Assert.That(ContainsText(text, "word 3"), Is.True);
        }

        [Test]
        public void TagCacheEvictsOneEntryInsteadOfDiscardingTheWorkingSet()
        {
            var tags = new SingleLineMeasurer(new UguiTextMeasurer(Font, 20));
            for (int i = 0; i < 513; i++)
                tags.Measure("tag " + i, 300);
            Assert.That(Cache(tags).Count, Is.EqualTo(512));
            Assert.That(ContainsText(tags, "tag 511"), Is.True);
        }

        [Test]
        public void EllipsisSearchDoesNotFillTheCacheWithSpeculativePrefixes()
        {
            var text = new UguiTextMeasurer(Font, 20, cacheCapacity: 4);
            text.SingleLineWidth("keep this metric");
            var result = text.Ellipsize("A very long sentence with many candidate prefixes that must be shortened", 100);
            Assert.That(result, Does.EndWith("…"));
            Assert.That(ContainsText(text, "keep this metric"), Is.True);
            Assert.That(text.SingleLineWidth(result), Is.LessThanOrEqualTo(100));
        }

        [Test]
        public void SkylineAllocationBaselineAndPublicBuildIsolation()
        {
            var snapshot = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "skyline",
                Layout = new SectionLayoutSettings
                {
                    Mode = LayoutMode.Skyline,
                    HorizontalSpacing = 3,
                    VerticalSpacing = 5
                }
            };
            snapshot.Sections.Add(section);
            for (int i = 0; i < 1000; i++)
                section.Items.Add(Item("item " + i));
            SectionedLayout.Build(snapshot, 400);
            long start = GC.GetAllocatedBytesForCurrentThread();
            var result = SectionedLayout.Build(snapshot, 400);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            if (allocated == 0)
                TestContext.WriteLine("当前 Unity 运行时未提供线程分配计数，不能据此报告零分配。");
            else
                TestContext.WriteLine("Skyline 1000-item build allocated bytes: " + allocated);
            section.Items[0].Id = "changed";
            Assert.That(result.Entries[0].Item.Id, Is.EqualTo("item 0"));
            Assert.That(result.Entries.Count, Is.EqualTo(1000));
            Assert.That(result.Entries[3].Rect, Is.EqualTo(new Rect(0, 25, 100, 20)));
        }

        [Test]
        public void TransactionCopySharesOnlyFrozenItemsAndIsolatesMutableContainers()
        {
            var frozen = new LayoutEntry(Item("frozen"), "section", 0, 0, ElementKind.Item, new Rect(0, 0, 100, 20)).Item;
            var mutable = Item("mutable");
            var source = new CollectionSnapshot();
            source.Sections.Add(new CollectionSection { Id = "section" });
            source.Sections[0].Items.Add(frozen);
            source.Sections[0].Items.Add(mutable);
            var method = typeof(CollectionSnapshot).GetMethod("CopyForTransaction", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "私有事务复制入口尚未实现。");
            var copy = (CollectionSnapshot)method.Invoke(source, null);
            Assert.That(copy.Sections[0].Items[0], Is.SameAs(frozen));
            Assert.That(copy.Sections[0].Items[1], Is.Not.SameAs(mutable));
            copy.Sections[0].Items[1].Id = "candidate";
            copy.Sections[0].Layout.ColumnCount = 7;
            copy.Sections[0].Items.RemoveAt(0);
            Assert.That(mutable.Id, Is.EqualTo("mutable"));
            Assert.That(source.Sections[0].Layout.ColumnCount, Is.EqualTo(2));
            Assert.That(source.Sections[0].Items.Count, Is.EqualTo(2));
            var publicCopy = source.Copy();
            Assert.That(publicCopy.Sections[0].Items[0], Is.Not.SameAs(frozen));
            Assert.DoesNotThrow(() => publicCopy.Sections[0].Items[0].Id = "editable");
        }

        [Test]
        public void OwnedBuildAcceptsSharedFrozenItemsAndCopiesMismatchedSupplementaryIdentity()
        {
            var frozen = new LayoutEntry(Item("frozen"), "section", 0, 0, ElementKind.Item, new Rect(0, 0, 100, 20)).Item;
            var header = new LayoutEntry(Item("header"), "original", 0, -1, ElementKind.Header, new Rect(0, 0, 100, 20)).Item;
            var source = new CollectionSnapshot();
            source.Sections.Add(new CollectionSection { Id = "section", Header = header });
            source.Sections[0].Items.Add(frozen);
            var build = typeof(SectionedLayout).GetMethod("BuildOwned", BindingFlags.Static | BindingFlags.NonPublic);
            LayoutResult result = null;
            Assert.DoesNotThrow(() => result = (LayoutResult)build.Invoke(null, new object[] { source, 400f, null, null, null }));
            Assert.That(result.Entries[1].Item, Is.SameAs(frozen));
            Assert.That(result.Entries[0].Item, Is.Not.SameAs(header));
            Assert.That(result.Entries[0].Item.LayoutKey, Is.Not.EqualTo(header.LayoutKey));
            Assert.That(result.Entries[0].Key, Is.EqualTo(result.Entries[0].Item.LayoutKey));
            Assert.Throws<InvalidOperationException>(() => header.Id = "mutation");
        }

        static CollectionItem Item(string id) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(100, 20)
        };
        static IDictionary Cache(object owner) => (IDictionary)owner.GetType().GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        static bool ContainsText(object owner, string text)
        {
            foreach (var key in Cache(owner).Keys)
                if ((string)key.GetType().GetField("Text").GetValue(key) == text)
                    return true;
            return false;
        }
    }
}
