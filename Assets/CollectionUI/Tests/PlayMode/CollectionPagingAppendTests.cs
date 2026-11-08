using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CollectionUI.Tests
{
    public class PagingAppendCell : CollectionCell
    {
        public static int LayoutCalls;
        protected override void OnLayoutUpdated(LayoutEntry entry) => LayoutCalls++;
    }

    public class CollectionPagingAppendTests
    {
        GameObject root, template;
        CollectionView view;
        int measurements;
        [SetUp]
        public void Setup()
        {
            root = new GameObject("Paging", typeof(RectTransform), typeof(ScrollRect));
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 200);
            var vp = (RectTransform)new GameObject("Viewport", typeof(RectTransform)).transform;
            vp.SetParent(root.transform, false);
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.sizeDelta = Vector2.zero;
            var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
            content.SetParent(vp, false);
            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = vp;
            scroll.content = content;
            template = new GameObject("Template", typeof(RectTransform), typeof(PagingAppendCell));
            template.SetActive(false);
            view = root.AddComponent<CollectionView>();
            view.RegisterTemplate("cell", template.GetComponent<CollectionCell>());
            measurements = PagingAppendCell.LayoutCalls = 0;
            view.MeasureItem = (item, width) =>
            {
                measurements++;
                return new Vector2(Mathf.Min(85, width), 45 + int.Parse(item.Id.Substring(1)) % 5 * 13);
            };
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(template);
        }

        static CollectionItem Item(int index) => new CollectionItem
        {
            Id = "i" + index,
            ReuseIdentifier = "cell",
            Size = ItemSize.Measured()
        };
        static CollectionSnapshot Data(LayoutMode mode = LayoutMode.Flow, LayoutDirection direction = LayoutDirection.LeftToRight, bool footer = true)
        {
            var section = new CollectionSection
            {
                Id = "s",
                Layout = new SectionLayoutSettings
                {
                    Mode = mode,
                    Direction = direction,
                    ColumnCount = 3,
                    Insets = new Insets(7, 11, 19, 13),
                    HorizontalSpacing = 9,
                    VerticalSpacing = 5
                }
            };
            section.Header = new CollectionItem
            {
                Id = "shared",
                ReuseIdentifier = "cell",
                Size = ItemSize.Fixed(120, 30)
            };
            if (footer)
                section.Footer = new CollectionItem
                {
                    Id = "shared",
                    ReuseIdentifier = "cell",
                    Size = ItemSize.Fixed(140, 40)
                };
            for (int i = 0; i < 30; i++)
                section.Items.Add(Item(i));
            return new CollectionSnapshot
            {
                Sections = new List<CollectionSection>
                {
                    section
                }
            };
        }

        static bool IsIncremental(LayoutResult result)
        {
            var flag = typeof(LayoutResult).GetProperty("IsIncrementalAppend");
            Assert.IsNotNull(flag, "布局应提供是否使用增量追加的诊断信息");
            return (bool)flag.GetValue(result);
        }

        static CollectionChangeSet Page(int first, int count)
        {
            var changes = new CollectionChangeSet();
            for (int i = first; i < first + count; i++)
                changes.InsertItem("s", i, Item(i));
            return changes;
        }

        void SameAsFull()
        {
            var full = SectionedLayout.Build(view.GetSnapshotCopy(), 400, (item, width) => new Vector2(Mathf.Min(85, width), 45 + int.Parse(item.Id.Substring(1)) % 5 * 13));
            Assert.AreEqual(full.ContentHeight, view.Layout.ContentHeight);
            Assert.AreEqual(full.Entries.Count, view.Layout.Entries.Count);
            for (int i = 0; i < full.Entries.Count; i++)
                Assert.AreEqual(full.Entries[i].Rect, view.Layout.Entries[i].Rect, "entry " + i);
        }

        [TestCase(LayoutMode.Columns, LayoutDirection.LeftToRight)]
        [TestCase(LayoutMode.Columns, LayoutDirection.RightToLeft)]
        [TestCase(LayoutMode.Flow, LayoutDirection.LeftToRight)]
        [TestCase(LayoutMode.Flow, LayoutDirection.RightToLeft)]
        public void TailAppendUsesIncrementalCommitPreservingBindingsAndOldSnapshot(LayoutMode mode, LayoutDirection direction)
        {
            view.ApplySnapshot(Data(mode, direction));
            var old = view.Layout;
            var copy = view.GetSnapshotCopy();
            var cells = view.ScrollRect.content.GetComponentsInChildren<CollectionCell>().Where(c => c.ItemId != "shared").ToArray();
            var versions = cells.Select(c => c.BindingVersion).ToArray();
            float offset = view.ScrollRect.content.anchoredPosition.y;
            view.ScrollRect.velocity = new Vector2(0, 321);
            view.ApplyChanges(Page(30, 20));
            Assert.IsTrue(IsIncremental(view.Layout));
            Assert.AreEqual(50, measurements);
            Assert.AreEqual(32, old.Entries.Count);
            Assert.AreEqual(30, copy.Sections[0].Items.Count);
            Assert.IsFalse(old.TryGetItem("i49", out _));
            Assert.IsTrue(view.Layout.TryGetItem("i49", out _));
            Assert.AreEqual(offset, view.ScrollRect.content.anchoredPosition.y);
            Assert.AreEqual(321, view.ScrollRect.velocity.y);
            for (int i = 0; i < cells.Length; i++)
                Assert.AreEqual(versions[i], cells[i].BindingVersion);
            Assert.AreEqual(0, PagingAppendCell.LayoutCalls);
            SameAsFull();
            copy.Sections[0].Items.Clear();
            view.ApplyChanges(Page(50, 20));
            Assert.AreEqual(70, view.GetSnapshotCopy().Sections[0].Items.Count);
            SameAsFull();
        }

        [TestCase(20, LayoutMode.Flow, LayoutDirection.LeftToRight)]
        [TestCase(20, LayoutMode.Flow, LayoutDirection.RightToLeft)]
        [TestCase(50, LayoutMode.Flow, LayoutDirection.LeftToRight)]
        [TestCase(50, LayoutMode.Flow, LayoutDirection.RightToLeft)]
        [TestCase(20, LayoutMode.Columns, LayoutDirection.LeftToRight)]
        [TestCase(20, LayoutMode.Columns, LayoutDirection.RightToLeft)]
        [TestCase(50, LayoutMode.Columns, LayoutDirection.LeftToRight)]
        [TestCase(50, LayoutMode.Columns, LayoutDirection.RightToLeft)]
        public void RepeatedPagesToTenThousandKeepIncrementalLookupAndCanReturnToNormalUpdates(int page, LayoutMode mode, LayoutDirection direction)
        {
            var data = Data(mode, direction, false);
            data.Sections[0].Items.Clear();
            data.Sections[0].Header = null;
            view.ApplySnapshot(data);
            LayoutResult first = null;
            for (int count = 0; count < 10000; count += page)
            {
                view.ApplyChanges(Page(count, page));
                if (count == 0)
                    first = view.Layout;
                else
                    Assert.IsTrue(IsIncremental(view.Layout));
            }

            Assert.AreEqual(10000, measurements);
            Assert.AreEqual(page, first.Entries.Count);
            Assert.IsTrue(view.Layout.TryGetItem("i9999", out var last));
            view.ScrollToItem("i9999", 1);
            CollectionAssert.Contains(view.GetVisibleItems(), "i9999");
            var old = view.Layout;
            view.ApplyChanges(new CollectionChangeSet().RemoveItem("i5").MoveItem("i10", "s", 0));
            Assert.IsFalse(IsIncremental(view.Layout));
            Assert.AreEqual(10000, old.Entries.Count);
            Assert.IsTrue(old.TryGetItem("i5", out _));
            Assert.IsFalse(view.Layout.TryGetItem("i5", out _));
        }

        [TestCase(LayoutMode.Flow, LayoutDirection.LeftToRight)]
        [TestCase(LayoutMode.Flow, LayoutDirection.RightToLeft)]
        [TestCase(LayoutMode.Columns, LayoutDirection.LeftToRight)]
        [TestCase(LayoutMode.Columns, LayoutDirection.RightToLeft)]
        public void RepeatedAppendPreservesStickyHeaderInstanceBindingAndPosition(LayoutMode mode, LayoutDirection direction)
        {
            view.ApplySnapshot(Data(mode, direction));
            var sticky = root.AddComponent<CollectionStickyHeaders>();
            view.ScrollRect.content.anchoredPosition = new Vector2(0, 100);
            sticky.SendMessage("LateUpdate");
            var overlay = view.ScrollRect.viewport.Find("StickyHeaders");
            var header = overlay.GetComponentInChildren<CollectionCell>();
            Assert.IsNotNull(header);
            var version = header.BindingVersion;
            var position = ((RectTransform)header.transform).anchoredPosition;
            for (int count = 30; count < 530; count += 50)
            {
                view.ApplyChanges(Page(count, 50));
                sticky.SendMessage("LateUpdate");
                Assert.IsTrue(IsIncremental(view.Layout));
                Assert.AreSame(header, overlay.GetComponentInChildren<CollectionCell>());
                Assert.AreEqual(version, header.BindingVersion);
                Assert.IsTrue(header.IsBound);
                Assert.AreEqual(position, ((RectTransform)header.transform).anchoredPosition);
            }
        }

        [Test]
        public void FailedAppendDoesNotPublishIdsGeometryOrMeasurements()
        {
            view.ApplySnapshot(Data());
            var before = view.Layout;
            bool fail = true;
            var calls = new List<string>();
            view.MeasureItem = (item, width) =>
            {
                calls.Add(item.Id);
                if (fail && item.Id == "i31")
                    throw new InvalidOperationException("business measurement");
                return new Vector2(85, 50);
            };
            var page = Page(30, 2);
            Assert.Throws<InvalidOperationException>(() => view.ApplyChanges(page));
            Assert.AreSame(before, view.Layout);
            Assert.AreEqual(30, view.GetSnapshotCopy().Sections[0].Items.Count);
            Assert.IsFalse(view.Layout.TryGetItem("i30", out _));
            fail = false;
            view.ApplyChanges(page);
            CollectionAssert.AreEqual(new[] { "i30", "i31", "i30", "i31" }, calls);
            Assert.IsTrue(IsIncremental(view.Layout));
            Assert.Throws<ArgumentException>(() => view.ApplyChanges(page));
            Assert.AreEqual(32, view.GetSnapshotCopy().Sections[0].Items.Count);
        }

        [Test]
        public void DuplicateWithinPageAndMissingTemplateLeaveOldResultUnchanged()
        {
            view.ApplySnapshot(Data());
            var before = view.Layout;
            Assert.Throws<ArgumentException>(() => view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 30, Item(30)).InsertItem("s", 31, Item(30))));
            Assert.AreSame(before, view.Layout);
            var missing = Item(31);
            missing.ReuseIdentifier = "missing";
            Assert.Throws<ArgumentException>(() => view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 30, Item(30)).InsertItem("s", 31, missing)));
            Assert.AreSame(before, view.Layout);
            Assert.AreEqual(30, measurements);
        }

        [Test]
        public void DirtyItemsStyleAndNonTailChangesUseCompleteValidationPath()
        {
            view.ApplySnapshot(Data());
            view.InvalidateItemSize("i0");
            view.ApplyChanges(Page(30, 1));
            Assert.IsFalse(IsIncremental(view.Layout));
            Assert.AreEqual(32, measurements);
            view.InvalidateStyle();
            view.ApplyChanges(Page(31, 1));
            Assert.IsFalse(IsIncremental(view.Layout));
            Assert.AreEqual(64, measurements);
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 0, Item(100)));
            Assert.IsFalse(IsIncremental(view.Layout));
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 33, Item(101)).UpdateItem("i0", Item(0)));
            Assert.IsFalse(IsIncremental(view.Layout));
        }
    }
}
