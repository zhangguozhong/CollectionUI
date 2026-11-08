using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CollectionUI.Tests
{
    public class DeltaProbeCell : CollectionCell
    {
        public int LastItemIndex;
        public int LayoutUpdates;
        protected override void OnBind(LayoutEntry entry) => LastItemIndex = entry.ItemIndex;
        protected override void OnLayoutUpdated(LayoutEntry entry)
        {
            LastItemIndex = entry.ItemIndex;
            LayoutUpdates++;
        }
    }

    public class CollectionDeltaTests
    {
        GameObject node, template;
        CollectionView view;
        [SetUp]
        public void SetUp()
        {
            node = new GameObject("Delta", typeof(RectTransform));
            ((RectTransform)node.transform).sizeDelta = new Vector2(300, 100);
            view = node.AddComponent<CollectionView>();
            template = new GameObject("Template", typeof(RectTransform), typeof(DeltaProbeCell));
            template.SetActive(false);
            view.RegisterTemplate("card", template.GetComponent<CollectionCell>());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(node);
            Object.DestroyImmediate(template);
        }

        static CollectionItem Item(string id) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "card",
            Size = ItemSize.Fixed(0, 50)
        };
        static CollectionSnapshot Data()
        {
            var data = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "s"
            };
            section.Layout.ColumnCount = 1;
            section.Items.Add(Item("a"));
            section.Items.Add(Item("b"));
            data.Sections.Add(section);
            return data;
        }

        CollectionCell Cell(string id) => node.GetComponentsInChildren<CollectionCell>().Single(c => c.ItemId == id);
        [Test]
        public void DeltaApiSupportsTransactionalSequentialChanges()
        {
            var type = typeof(CollectionView).Assembly.GetType("CollectionUI.CollectionChangeSet");
            Assert.That(type, Is.Not.Null, "Sequential changes API must be supplied.");
            var changes = System.Activator.CreateInstance(type);
            type.GetMethod("InsertItem").Invoke(changes, new object[] { "s", 0, Item("new") });
            type.GetMethod("MoveItem").Invoke(changes, new object[] { "a", "s", 2 });
            view.ApplySnapshot(Data());
            typeof(CollectionView).GetMethod("ApplyChanges").Invoke(view, new[] { changes });
            Assert.That(view.GetVisibleItems(), Is.EqualTo(new[] { "new", "b", "a" }));
        }

        [Test]
        public void InvalidBatchLeavesCommittedLayoutAndBindingsUntouched()
        {
            var type = typeof(CollectionView).Assembly.GetType("CollectionUI.CollectionChangeSet");
            Assert.That(type, Is.Not.Null);
            view.ApplySnapshot(Data());
            var layout = view.Layout;
            var cell = Cell("a");
            var version = cell.BindingVersion;
            var changes = System.Activator.CreateInstance(type);
            type.GetMethod("RemoveItem").Invoke(changes, new object[] { "a" });
            type.GetMethod("InsertItem").Invoke(changes, new object[] { "s", 99, Item("new") });
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => typeof(CollectionView).GetMethod("ApplyChanges").Invoke(view, new[] { changes }));
            Assert.That(error.InnerException, Is.InstanceOf<System.ArgumentException>());
            Assert.That(view.Layout, Is.SameAs(layout));
            Assert.That(Cell("a"), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.EqualTo(version));
        }

        [Test]
        public void SectionOperationsAreSequentialAndCopyEnqueuedMetadata()
        {
            var type = typeof(CollectionView).Assembly.GetType("CollectionUI.CollectionChangeSet");
            Assert.That(type, Is.Not.Null);
            view.ApplySnapshot(Data());
            var changes = System.Activator.CreateInstance(type);
            var section = new CollectionSection
            {
                Id = "next"
            };
            section.Items.Add(Item("c"));
            type.GetMethod("InsertSection").Invoke(changes, new object[] { 1, section });
            section.Items.Clear();
            section.Id = "mutated";
            type.GetMethod("MoveSection").Invoke(changes, new object[] { "next", 0 });
            type.GetMethod("RemoveItem").Invoke(changes, new object[] { "b" });
            var replacement = Item("a");
            replacement.Version = 2;
            type.GetMethod("UpdateItem").Invoke(changes, new object[] { "a", replacement });
            typeof(CollectionView).GetMethod("ApplyChanges").Invoke(view, new[] { changes });
            Assert.That(view.GetVisibleItems(), Is.EqualTo(new[] { "c", "a" }));
            Assert.That(view.Layout.Entries[0].SectionId, Is.EqualTo("next"));
            Assert.That(view.Layout.Entries[1].Item.Version, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateIdentityAndTemplateFailureDoNotChangeCommittedData()
        {
            view.ApplySnapshot(Data());
            var layout = view.Layout;
            Assert.Throws<System.ArgumentException>(() => view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 0, Item("a"))));
            var invalid = Item("x");
            invalid.ReuseIdentifier = "unknown";
            Assert.Throws<System.ArgumentException>(() => view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 0, invalid)));
            Assert.That(view.Layout, Is.SameAs(layout));
            Assert.That(view.GetSnapshotCopy().Sections[0].Items.Select(i => i.Id), Is.EqualTo(new[] { "a", "b" }));
            Assert.Throws<System.ArgumentException>(() => new CollectionChangeSet().UpdateItem("a", Item("renamed")));
        }

        [Test]
        public void CrossSectionMovePreservesBindingAndSnapshotCopyIsIndependent()
        {
            view.ApplySnapshot(Data());
            var cell = Cell("a");
            int version = cell.BindingVersion;
            var next = new CollectionSection
            {
                Id = "next"
            };
            view.ApplyChanges(new CollectionChangeSet().InsertSection(1, next).MoveItem("a", "next", 0));
            Assert.That(Cell("a"), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.EqualTo(version));
            var copy = view.GetSnapshotCopy();
            copy.Sections[1].Items.Clear();
            Assert.That(view.GetSnapshotCopy().Sections[1].Items[0].Id, Is.EqualTo("a"));
        }

        [Test]
        public void ExplicitReloadAndPendingSizeInvalidationStillRebindOnlyTarget()
        {
            var data = Data();
            view.ApplySnapshot(data);
            var a = Cell("a");
            var b = Cell("b");
            int av = a.BindingVersion, bv = b.BindingVersion;
            view.ReloadItems(new[] { "a" });
            Assert.That(a.BindingVersion, Is.GreaterThan(av));
            av = a.BindingVersion;
            view.InvalidateItemSize("a");
            view.ApplySnapshot(data);
            Assert.That(a.BindingVersion, Is.GreaterThan(av));
            Assert.That(b.BindingVersion, Is.EqualTo(bv));
        }

        sealed class MeasuredData
        {
            public float Height;
        }

        CollectionSnapshot MeasuredSnapshot(out MeasuredData payload)
        {
            var data = Data();
            payload = new MeasuredData
            {
                Height = 50
            };
            data.Sections[0].Items[0].Data = payload;
            data.Sections[0].Items[0].Size = ItemSize.Measured();
            view.MeasureItem = (item, width) => new Vector2(width, ((MeasuredData)item.Data).Height);
            return data;
        }

        [Test]
        public void ExplicitUpdateItemRemeasuresNewDataWithoutChangingBusinessVersion()
        {
            var data = MeasuredSnapshot(out _);
            view.ApplySnapshot(data);
            var replacement = data.Sections[0].Items[0].Copy();
            replacement.Data = new MeasuredData
            {
                Height = 120
            };
            view.ApplyChanges(new CollectionChangeSet().UpdateItem("a", replacement));
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(120));
            Assert.That(view.Layout.Entries[0].Item.Version, Is.Zero);
        }

        [Test]
        public void ExplicitUpdateItemRemeasuresMutatedPayloadAndRebindsOnlyTarget()
        {
            var data = MeasuredSnapshot(out var payload);
            view.ApplySnapshot(data);
            var a = Cell("a");
            int av = a.BindingVersion;
            var b = Cell("b");
            int bv = b.BindingVersion;
            payload.Height = 110;
            view.ApplyChanges(new CollectionChangeSet().UpdateItem("a", data.Sections[0].Items[0]));
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(110));
            Assert.That(a.BindingVersion, Is.GreaterThan(av));
            Assert.That(b.BindingVersion, Is.EqualTo(bv));
        }

        [Test]
        public void ExplicitUpdateSectionRemeasuresHeaderWithoutChangingVersion()
        {
            var data = MeasuredSnapshot(out var payload);
            data.Sections[0].Header = Item("header");
            data.Sections[0].Header.Size = ItemSize.Measured();
            data.Sections[0].Header.Data = payload;
            view.ApplySnapshot(data);
            var header = Cell("header");
            int version = header.BindingVersion;
            payload.Height = 95;
            view.ApplyChanges(new CollectionChangeSet().UpdateSection("s", data.Sections[0]));
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(95));
            Assert.That(header.BindingVersion, Is.GreaterThan(version));
            Assert.That(view.Layout.Entries[0].Item.Version, Is.Zero);
        }

        [Test]
        public void FailedExplicitUpdateDoesNotLeakForcedMeasurementOrBinding()
        {
            var data = MeasuredSnapshot(out var payload);
            int measured = 0;
            view.MeasureItem = (item, width) =>
            {
                measured++;
                return new Vector2(width, ((MeasuredData)item.Data).Height);
            };
            view.ApplySnapshot(data);
            var original = view.Layout;
            var a = Cell("a");
            int version = a.BindingVersion;
            var replacement = data.Sections[0].Items[0].Copy();
            replacement.Data = new MeasuredData
            {
                Height = 140
            };
            var invalid = Item("bad");
            invalid.ReuseIdentifier = "unknown";
            Assert.Throws<System.ArgumentException>(() => view.ApplyChanges(new CollectionChangeSet().UpdateItem("a", replacement).InsertItem("s", 0, invalid)));
            Assert.That(view.Layout, Is.SameAs(original));
            Assert.That(a.BindingVersion, Is.EqualTo(version));
            int afterFailure = measured;
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EqualTo(afterFailure));
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(50));
            Assert.That(a.BindingVersion, Is.EqualTo(version));
        }

        [Test]
        public void RemoveThenInsertSameItemIdentityRemeasuresReplacement()
        {
            var data = MeasuredSnapshot(out _);
            view.ApplySnapshot(data);
            var replacement = data.Sections[0].Items[0].Copy();
            replacement.Data = new MeasuredData
            {
                Height = 135
            };
            view.ApplyChanges(new CollectionChangeSet().RemoveItem("a").InsertItem("s", 0, replacement));
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(135));
            Assert.That(view.Layout.Entries[0].Item.Version, Is.Zero);
        }

        [Test]
        public void RemoveThenInsertSameSectionIdentityRemeasuresHeaderAndItems()
        {
            var data = MeasuredSnapshot(out var payload);
            data.Sections[0].Header = Item("header");
            data.Sections[0].Header.Size = ItemSize.Measured();
            data.Sections[0].Header.Data = payload;
            view.ApplySnapshot(data);
            var replacement = data.Copy().Sections[0];
            replacement.Header.Data = new MeasuredData
            {
                Height = 75
            };
            replacement.Items[0].Data = new MeasuredData
            {
                Height = 125
            };
            view.ApplyChanges(new CollectionChangeSet().RemoveSection("s").InsertSection(0, replacement));
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(75));
            Assert.That(view.Layout.Entries[1].Rect.height, Is.EqualTo(125));
        }

        [Test]
        public void UnchangedSnapshotPreservesBindingAndCancellation()
        {
            var data = Data();
            view.ApplySnapshot(data);
            var cell = Cell("a");
            var version = cell.BindingVersion;
            var token = cell.BindingCancellation;
            view.ApplySnapshot(data);
            Assert.That(Cell("a"), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.EqualTo(version));
            Assert.That(token.IsCancellationRequested, Is.False);
        }

        [Test]
        public void VersionChangeRebindsOnlyChangedItem()
        {
            var data = Data();
            view.ApplySnapshot(data);
            var a = Cell("a");
            var b = Cell("b");
            var av = a.BindingVersion;
            var bv = b.BindingVersion;
            data.Sections[0].Items[0].Version++;
            view.ApplySnapshot(data);
            Assert.That(Cell("a"), Is.SameAs(a));
            Assert.That(a.BindingVersion, Is.GreaterThan(av));
            Assert.That(Cell("b"), Is.SameAs(b));
            Assert.That(b.BindingVersion, Is.EqualTo(bv));
        }

        [Test]
        public void InsertRepositionsExistingFrameWithoutRebinding()
        {
            var data = Data();
            view.ApplySnapshot(data);
            var a = Cell("a");
            var version = a.BindingVersion;
            data.Sections[0].Items.Insert(0, Item("new"));
            view.ApplySnapshot(data);
            Assert.That(Cell("a"), Is.SameAs(a));
            Assert.That(a.BindingVersion, Is.EqualTo(version));
            Assert.That(((RectTransform)a.transform).anchoredPosition.y, Is.EqualTo(-50));
            Assert.That(((DeltaProbeCell)a).LastItemIndex, Is.EqualTo(1));
            Assert.That(((DeltaProbeCell)a).LayoutUpdates, Is.EqualTo(1));
        }
    }
}
