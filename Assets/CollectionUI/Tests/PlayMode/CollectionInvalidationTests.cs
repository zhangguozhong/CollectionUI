// 局部失效集成测试：尺寸缓存、分区复用与提交时序。
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CollectionUI.Tests
{
    public class CollectionInvalidationTests
    {
        GameObject node, template;
        CollectionView view;
        readonly List<string> measured = new List<string>();
        readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        RectTransform Host => (RectTransform)node.transform;

        [SetUp]
        public void SetUp()
        {
            node = new GameObject("Collection", typeof(RectTransform));
            Host.sizeDelta = new Vector2(300, 100);
            view = node.AddComponent<CollectionView>();
            template = new GameObject("Template", typeof(RectTransform), typeof(CollectionCell));
            template.SetActive(false);
            view.RegisterTemplate("card", template.GetComponent<CollectionCell>());
            measured.Clear();
            heights.Clear();
            view.MeasureItem = (item, width) =>
            {
                measured.Add(item.Id);
                return new Vector2(width, heights.TryGetValue(item.Id, out var height) ? height : 50);
            };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(node);
            Object.Destroy(template);
            yield return null;
        }

        static CollectionSnapshot Data()
        {
            var data = new CollectionSnapshot();
            for (int s = 0; s < 3; s++)
            {
                var section = new CollectionSection
                {
                    Id = "section" + s
                };
                section.Layout.ColumnCount = 1;
                for (int i = 0; i < 2; i++)
                    section.Items.Add(new CollectionItem { Id = "item" + (s * 2 + i), ReuseIdentifier = "card", Size = ItemSize.Measured() });
                data.Sections.Add(section);
            }

            return data;
        }

        [UnityTest]
        public IEnumerator InvalidationMeasuresOnlyTargetAndRepositionsUntouchedSections()
        {
            view.ApplySnapshot(Data());
            measured.Clear();
            heights["item0"] = 100;
            view.InvalidateItemSize("item0");
            view.InvalidateItemSize("item0");
            yield return null;
            yield return null;
            Assert.That(measured, Is.EqualTo(new[] { "item0" }));
            Assert.That(view.Layout.ReusedSectionCount, Is.EqualTo(2));
            Assert.That(view.Layout.RecomputedSectionCount, Is.EqualTo(1));
            Assert.That(view.Layout.Entries[2].Rect.y, Is.EqualTo(150));
            Assert.That(view.Layout.ContentHeight, Is.EqualTo(350));
        }

        [Test]
        public void PendingInvalidationsSurviveSnapshotAndKeepOtherMeasurementsWarm()
        {
            var data = Data();
            view.ApplySnapshot(data);
            measured.Clear();
            heights["item0"] = 100;
            view.InvalidateItemSize("item0");
            view.InvalidateItemSize("item4");
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EquivalentTo(new[] { "item0", "item4" }));
            Assert.That(view.Layout.ReusedSectionCount, Is.EqualTo(1));
            Assert.That(view.Layout.ContentHeight, Is.EqualTo(350));
        }

        [UnityTest]
        public IEnumerator UnknownIdsDoNotBuildAndEmptyIdsAreRejected()
        {
            view.ApplySnapshot(Data());
            int builds = view.LayoutBuildCount;
            view.InvalidateItemSize("missing");
            Assert.Throws<ArgumentException>(() => view.InvalidateItemSize(null));
            Assert.Throws<ArgumentException>(() => view.InvalidateItemSize(""));
            yield return null;
            yield return null;
            Assert.That(view.LayoutBuildCount, Is.EqualTo(builds));
        }

        [Test]
        public void CacheRetainsCurrentWidthsAndVersionsButNotHistoricalMeasurements()
        {
            var data = Data();
            view.ApplySnapshot(data);
            measured.Clear();
            data.Sections[0].Items[0].Version++;
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EqualTo(new[] { "item0" }));
            measured.Clear();
            data.Sections[0].Layout.ColumnCount = 2;
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EquivalentTo(new[] { "item0", "item1" }));
            measured.Clear();
            data.Sections[0].Layout.ColumnCount = 1;
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EquivalentTo(new[] { "item0", "item1" }), "Old column widths must be pruned.");
            data.Sections[0].Items[0].Size = ItemSize.Fixed(0, 10);
            view.ApplySnapshot(data);
            measured.Clear();
            data.Sections[0].Items[0].Size = ItemSize.Measured();
            heights["item0"] = 90;
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EqualTo(new[] { "item0" }));
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(90));
        }

        [Test]
        public void PendingInvalidationFollowsItemIntoNewSection()
        {
            var data = Data();
            view.ApplySnapshot(data);
            measured.Clear();
            heights["item0"] = 90;
            view.InvalidateItemSize("item0");
            var moved = data.Sections[0].Items[0];
            data.Sections[0].Items.RemoveAt(0);
            data.Sections[1].Items.Add(moved);
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EqualTo(new[] { "item0" }));
            Assert.That(view.Layout.TryGetItem("item0", out var entry), Is.True);
            Assert.That(entry.Rect.height, Is.EqualTo(90));
        }

        [Test]
        public void InvalidationsAreRejectedInsideMeasurement()
        {
            view.MeasureItem = (item, width) =>
            {
                Assert.Throws<InvalidOperationException>(() => view.InvalidateItemSize(item.Id));
                Assert.Throws<InvalidOperationException>(() => view.InvalidateStyle());
                return new Vector2(width, 50);
            };
            view.ApplySnapshot(Data());
        }

        [Test]
        public void FailedSnapshotsDoNotPolluteCommittedMeasurementCache()
        {
            view.ApplySnapshot(Data());
            for (int i = 0; i < 10; i++)
            {
                var bad = Data();
                bad.Sections[0].Items[0].Id = "failed" + i;
                bad.Sections[2].Items[1].ReuseIdentifier = "unregistered";
                Assert.Throws<ArgumentException>(() => view.ApplySnapshot(bad));
            }

            var field = typeof(CollectionView).GetField("sizes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var cache = (System.Collections.IDictionary)field.GetValue(view);
            Assert.That(cache.Count, Is.EqualTo(6), "Failed transactions must not retain fresh measurement keys.");
            measured.Clear();
            var data = Data();
            data.Sections[0].Items[0].Version++;
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EqualTo(new[] { "item0" }));
        }

        [Test]
        public void SizeEventObservesCommittedCellsAndOnlyChangedContentSize()
        {
            var events = new List<Vector2>();
            view.ContentSizeChanged += size =>
            {
                Assert.That(view.Layout.ContentHeight, Is.EqualTo(size.y));
                Assert.That(Host.rect.height, Is.EqualTo(size.y));
                Assert.That(view.ActiveCellCount, Is.EqualTo(view.Layout.Entries.Count));
                events.Add(size);
            };
            view.ApplySnapshot(new CollectionSnapshot());
            view.ApplySnapshot(new CollectionSnapshot());
            view.ApplySnapshot(Data());
            view.ApplySnapshot(Data());
            view.ReloadItems(new[] { "item0" });
            view.AutoHeight = false;
            view.AutoHeight = true;
            view.RefreshVisible();
            Assert.That(events, Is.EqualTo(new[] { new Vector2(300, 0), new Vector2(300, 300) }));
        }

        [Test]
        public void SizeEventRunsWhenDisabledAndWithAutoHeightOff()
        {
            view.AutoHeight = false;
            view.enabled = false;
            int events = 0;
            view.ContentSizeChanged += size =>
            {
                events++;
                Assert.That(size, Is.EqualTo(new Vector2(300, 300)));
                Assert.That(view.ActiveCellCount, Is.Zero);
                Assert.That(Host.rect.height, Is.EqualTo(100));
            };
            view.ApplySnapshot(Data());
            Assert.That(events, Is.EqualTo(1));
            view.enabled = true;
            Assert.That(view.ActiveCellCount, Is.EqualTo(6));
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void EventMutationsAreRejectedAndExceptionDoesNotRepeatNotification()
        {
            int events = 0;
            Action<Vector2> handler = size =>
            {
                events++;
                Assert.Throws<InvalidOperationException>(() => view.InvalidateItemSize("item0"));
                Assert.Throws<InvalidOperationException>(() => view.InvalidateStyle());
                Assert.Throws<InvalidOperationException>(() => view.ApplySnapshot(Data()));
                Assert.Throws<InvalidOperationException>(() => view.ReloadItems(new[] { "item0" }));
                Assert.Throws<InvalidOperationException>(() => view.AutoHeight = false);
                Assert.Throws<InvalidOperationException>(() => view.LayoutEngine = new DefaultCollectionLayout());
                throw new ApplicationException("consumer failure");
            };
            view.ContentSizeChanged += handler;
            Assert.Throws<ApplicationException>(() => view.ApplySnapshot(Data()));
            Assert.That(view.ActiveCellCount, Is.EqualTo(6));
            Assert.That(view.Layout.ContentHeight, Is.EqualTo(300));
            Assert.DoesNotThrow(() => view.ApplySnapshot(Data()));
            Assert.That(events, Is.EqualTo(1));
            view.ContentSizeChanged -= handler;
            view.InvalidateItemSize("item0");
            Assert.DoesNotThrow(() => view.ApplySnapshot(Data()));
        }

        [UnityTest]
        public IEnumerator WidthAndStyleFullyRebuildAndWidthNotifies()
        {
            var events = new List<Vector2>();
            view.ContentSizeChanged += size => events.Add(size);
            view.ApplySnapshot(Data());
            measured.Clear();
            Host.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 240);
            yield return null;
            yield return null;
            Assert.That(measured.Count, Is.EqualTo(6));
            Assert.That(view.Layout.RecomputedSectionCount, Is.EqualTo(3));
            Assert.That(events, Is.EqualTo(new[] { new Vector2(300, 300), new Vector2(240, 300) }));
            measured.Clear();
            view.InvalidateStyle();
            yield return null;
            yield return null;
            Assert.That(measured.Count, Is.EqualTo(6));
            Assert.That(view.Layout.RecomputedSectionCount, Is.EqualTo(3));
            Assert.That(events.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ScrollAnchorSurvivesChangeAboveViewport()
        {
            var scroll = node.AddComponent<ScrollRect>();
            scroll.enabled = false;
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(node.transform, false);
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.viewport.sizeDelta = new Vector2(300, 80);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            scroll.content = (RectTransform)content.transform;
            view.ApplySnapshot(Data());
            view.ScrollToItem("item3");
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(150));
            int events = 0;
            view.ContentSizeChanged += size =>
            {
                events++;
                Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(200));
                Assert.That(scroll.content.rect.height, Is.EqualTo(350));
            };
            heights["item0"] = 100;
            view.InvalidateItemSize("item0");
            yield return null;
            yield return null;
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(200));
            Assert.That(events, Is.EqualTo(1));
            view.ScrollToItem("item4");
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void OrdinaryIdDoesNotInvalidateSameNamedHeaders()
        {
            var data = Data();
            foreach (var section in data.Sections)
                section.Header = new CollectionItem
                {
                    Id = "item0",
                    ReuseIdentifier = "card",
                    Size = ItemSize.Measured()
                };
            view.ApplySnapshot(data);
            measured.Clear();
            view.InvalidateItemSize("item0");
            view.ApplySnapshot(data);
            Assert.That(measured, Is.EqualTo(new[] { "item0" }));
            Assert.That(view.Layout.ReusedSectionCount, Is.EqualTo(2));
        }

        [Test]
        public void InvalidationsAreRejectedInsideBinding()
        {
            var probe = template.AddComponent<InvalidationProbeCell>();
            view.RegisterTemplate("probe", probe);
            var data = Data();
            int bound = 0;
            foreach (var section in data.Sections)
                foreach (var item in section.Items)
                {
                    item.ReuseIdentifier = "probe";
                    item.Data = (Action)(() =>
                    {
                        bound++;
                        Assert.Throws<InvalidOperationException>(() => view.InvalidateItemSize("item0"));
                        Assert.Throws<InvalidOperationException>(() => view.InvalidateStyle());
                    });
                }

            view.ApplySnapshot(data);
            Assert.That(bound, Is.EqualTo(6));
        }
    }

    public class InvalidationProbeCell : CollectionCell
    {
        protected override void OnBind(LayoutEntry entry) => (entry.Item.Data as Action)?.Invoke();
    }
}
