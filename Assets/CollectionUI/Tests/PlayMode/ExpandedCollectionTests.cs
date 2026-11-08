// 完整展开集成测试。验证无 ScrollRect、全量绑定、高度开关、宿主变换保护、宽度重测、生命周期与配置失败重试。
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    public class ExpandedCollectionTests
    {
        GameObject node, template;
        CollectionView view;
        RectTransform Host => (RectTransform)node.transform;

        [SetUp]
        public void SetUp()
        {
            node = new GameObject("Plain collection", typeof(RectTransform));
            Host.sizeDelta = new Vector2(300, 80);
            view = node.AddComponent<CollectionView>();
            template = new GameObject("Template", typeof(RectTransform), typeof(CollectionCell));
            template.SetActive(false);
            view.RegisterTemplate("card", template.GetComponent<CollectionCell>());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(node);
            Object.Destroy(template);
            yield return null;
        }

        static CollectionItem Item(string id, float height = 50) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "card",
            Size = ItemSize.Fixed(0, height)
        };
        static CollectionSnapshot Data(int count)
        {
            var data = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "section"
            };
            section.Layout.ColumnCount = 1;
            for (int i = 0; i < count; i++)
                section.Items.Add(Item("item" + i));
            data.Sections.Add(section);
            return data;
        }

        [UnityTest]
        public IEnumerator PlainNodeDoesNotRequireScrollRect()
        {
            Assert.That(node.GetComponent<ScrollRect>(), Is.Null, "A plain collection must not auto-add ScrollRect.");
            view.ApplySnapshot(Data(20));
            Assert.That(view.ActiveCellCount, Is.EqualTo(20));
            Assert.That(view.ScrollRect, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FixedHeightPreservesTransformAndExpandsAllSupplementaryEntries()
        {
            Host.anchorMin = new Vector2(.2f, .3f);
            Host.anchorMax = new Vector2(.8f, .9f);
            Host.pivot = new Vector2(.7f, .4f);
            Host.anchoredPosition = new Vector2(31, -27);
            var size = Host.sizeDelta;
            var position = Host.anchoredPosition;
            view.AutoHeight = false;
            var data = Data(30);
            data.Sections[0].Header = Item("header", 20);
            data.Sections[0].Footer = Item("footer", 30);
            view.ApplySnapshot(data);
            Assert.That(view.ActiveCellCount, Is.EqualTo(32));
            Assert.That(view.GetVisibleItems().Count, Is.EqualTo(30));
            Assert.That(view.GetVisibleItems(), Does.Not.Contain("header"));
            foreach (var cell in node.GetComponentsInChildren<CollectionCell>())
                Assert.That(cell.ItemId, Is.Not.Null.And.Not.Empty);
            view.ScrollToItem("item29");
            Assert.That(Host.sizeDelta, Is.EqualTo(size));
            Assert.That(Host.anchoredPosition, Is.EqualTo(position));
            Assert.That(Host.anchorMin, Is.EqualTo(new Vector2(.2f, .3f)));
            Assert.That(Host.anchorMax, Is.EqualTo(new Vector2(.8f, .9f)));
            Assert.That(Host.pivot, Is.EqualTo(new Vector2(.7f, .4f)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AutoHeightCanToggleShrinkAndBecomeEmptyWithoutMovingHost()
        {
            Host.pivot = new Vector2(.3f, .6f);
            Host.anchoredPosition = new Vector2(12, 19);
            var position = Host.anchoredPosition;
            var anchors = Host.anchorMin;
            view.ApplySnapshot(Data(20));
            Assert.That(Host.rect.height, Is.EqualTo(1000));
            view.AutoHeight = false;
            view.ApplySnapshot(Data(2));
            Assert.That(Host.rect.height, Is.EqualTo(1000));
            Assert.That(view.Layout.ContentHeight, Is.EqualTo(100));
            view.AutoHeight = true;
            Assert.That(Host.rect.height, Is.EqualTo(100));
            view.ApplySnapshot(new CollectionSnapshot());
            Assert.That(Host.rect.height, Is.Zero);
            Assert.That(view.ActiveCellCount, Is.Zero);
            Assert.That(Host.rect.width, Is.EqualTo(300));
            Assert.That(Host.anchoredPosition, Is.EqualTo(position));
            Assert.That(Host.anchorMin, Is.EqualTo(anchors));
            Assert.That(Host.pivot, Is.EqualTo(new Vector2(.3f, .6f)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator WidthChangesRemeasureButSteadyFramesDoNotRebuildOrRefresh()
        {
            int measurements = 0;
            view.MeasureItem = (item, width) =>
            {
                measurements++;
                return new Vector2(width, width / 3);
            };
            var data = Data(4);
            foreach (var item in data.Sections[0].Items)
                item.Size = ItemSize.Measured();
            view.ApplySnapshot(data);
            yield return null;
            int builds = view.LayoutBuildCount;
            int created = view.CreatedCellCount;
            double refresh = view.LastRefreshMilliseconds;
            yield return null;
            yield return null;
            Assert.That(view.LayoutBuildCount, Is.EqualTo(builds));
            Assert.That(measurements, Is.EqualTo(4));
            Assert.That(view.LastRefreshMilliseconds, Is.EqualTo(refresh));
            Host.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 180);
            yield return null;
            yield return null;
            Assert.That(view.LayoutBuildCount, Is.EqualTo(builds + 1));
            Assert.That(measurements, Is.EqualTo(8));
            Assert.That(Host.rect.height, Is.EqualTo(240));
            Assert.That(view.CreatedCellCount, Is.EqualTo(created));
            view.RefreshVisible();
            Assert.That(view.CreatedCellCount, Is.EqualTo(created));
        }

        [UnityTest]
        public IEnumerator GroupedFlowUsesSharedLayoutAndEveryEntryHasItsFrame()
        {
            var data = Data(7);
            var first = data.Sections[0];
            first.Layout.Mode = LayoutMode.Flow;
            first.Layout.Insets = new Insets(10, 12, 10, 14);
            first.Layout.HorizontalSpacing = 8;
            first.Layout.VerticalSpacing = 9;
            first.Layout.SectionSpacing = 17;
            first.Header = Item("header", 21);
            first.Footer = Item("footer", 23);
            foreach (var item in first.Items)
                item.Size = ItemSize.Fixed(90, 40);
            var second = new CollectionSection
            {
                Id = "second",
                Header = Item("header", 25)
            };
            second.Items.Add(Item("last", 30));
            data.Sections.Add(second);
            var expected = new DefaultCollectionLayout().Build(data.Copy(), 300);
            view.ApplySnapshot(data);
            Assert.That(view.ActiveCellCount, Is.EqualTo(expected.Entries.Count));
            Assert.That(Host.rect.height, Is.EqualTo(expected.ContentHeight));
            var cells = node.GetComponentsInChildren<CollectionCell>();
            for (int i = 0; i < expected.Entries.Count; i++)
            {
                var rect = (RectTransform)cells[i].transform;
                var frame = expected.Entries[i].Rect;
                Assert.That(rect.sizeDelta, Is.EqualTo(frame.size));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(frame.x, -frame.y)));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledSubmissionRefreshesOnEnableAndPreservesUnrelatedChildren()
        {
            var ornament = new GameObject("Decoration", typeof(RectTransform));
            ornament.transform.SetParent(node.transform, false);
            view.ApplySnapshot(Data(3));
            view.enabled = false;
            view.ApplySnapshot(Data(8));
            Assert.That(view.ActiveCellCount, Is.Zero);
            view.enabled = true;
            Assert.That(view.ActiveCellCount, Is.EqualTo(8));
            view.enabled = false;
            view.enabled = true;
            view.ApplySnapshot(Data(1));
            Assert.That(ornament.transform.parent, Is.EqualTo(node.transform));
            Assert.That(ornament.activeSelf, Is.True);
            int pools = 0;
            foreach (Transform child in node.transform)
                if (child.name == "CellPool")
                    pools++;
            Assert.That(pools, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExplicitExpandedIgnoresLocalScrollRectAndModeIsFrozenAfterInitialization()
        {
            var scroll = node.AddComponent<ScrollRect>();
            scroll.enabled = false;
            view.DisplayMode = CollectionDisplayMode.Expanded;
            view.ApplySnapshot(Data(10));
            Assert.That(view.ScrollRect, Is.Null);
            Assert.That(view.ActiveCellCount, Is.EqualTo(10));
            Assert.DoesNotThrow(() => view.DisplayMode = CollectionDisplayMode.Expanded);
            Assert.Throws<System.InvalidOperationException>(() => view.DisplayMode = CollectionDisplayMode.Auto);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidScrollConfigurationIsRetryableAndDoesNotMutateHost()
        {
            view.DisplayMode = CollectionDisplayMode.Scroll;
            Assert.Throws<System.InvalidOperationException>(() => view.ApplySnapshot(Data(1)));
            var scroll = node.AddComponent<ScrollRect>();
            scroll.enabled = false;
            Assert.Throws<System.InvalidOperationException>(() => view.ApplySnapshot(Data(1)));
            Assert.That(node.transform.childCount, Is.Zero);
            Assert.That(Host.sizeDelta, Is.EqualTo(new Vector2(300, 80)));
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(node.transform, false);
            var vp = (RectTransform)viewport.transform;
            vp.sizeDelta = new Vector2(300, 80);
            scroll.viewport = vp;
            Assert.Throws<System.InvalidOperationException>(() => view.ApplySnapshot(Data(1)));
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp, false);
            scroll.content = (RectTransform)content.transform;
            view.AutoHeight = false;
            view.ApplySnapshot(Data(100));
            Assert.That(view.ScrollRect, Is.EqualTo(scroll));
            Assert.That(scroll.content.rect.height, Is.EqualTo(5000));
            Assert.That(view.ActiveCellCount, Is.LessThan(100));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AutoScrollInitializedAfterEnableRespondsToEventsAndReenable()
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
            view.ApplySnapshot(Data(100));
            Assert.That(view.ScrollRect, Is.EqualTo(scroll));
            scroll.content.anchoredPosition = new Vector2(0, 4920);
            scroll.onValueChanged.Invoke(Vector2.zero);
            bool found = false;
            foreach (var cell in content.GetComponentsInChildren<CollectionCell>())
                if (cell.ItemId == "item99")
                    found = true;
            Assert.That(found, Is.True, "Initialization after OnEnable must subscribe to scroll events.");
            view.enabled = false;
            scroll.content.anchoredPosition = Vector2.zero;
            scroll.onValueChanged.Invoke(Vector2.zero);
            view.enabled = true;
            found = false;
            foreach (var cell in content.GetComponentsInChildren<CollectionCell>())
                if (cell.ItemId == "item0")
                    found = true;
            Assert.That(found, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidSerializedTemplatesDoNotCreatePoolAndCanBeCorrected()
        {
            view.SetSerializedTemplates(new[] { new CollectionView.Template { Id = "broken", Prefab = null } });
            Assert.Throws<System.ArgumentException>(() => view.ApplySnapshot(Data(1)));
            Assert.That(node.transform.childCount, Is.Zero);
            view.SetSerializedTemplates(System.Array.Empty<CollectionView.Template>());
            view.DisplayMode = CollectionDisplayMode.Expanded;
            view.ApplySnapshot(Data(1));
            Assert.That(view.ActiveCellCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MeasurementCannotChangeHeightOrModeDuringApply()
        {
            bool heightRejected = false, modeRejected = false;
            var data = Data(1);
            data.Sections[0].Items[0].Size = ItemSize.Measured();
            view.MeasureItem = (item, width) =>
            {
                try
                {
                    view.AutoHeight = false;
                }
                catch (System.InvalidOperationException)
                {
                    heightRejected = true;
                }

                try
                {
                    view.DisplayMode = CollectionDisplayMode.Scroll;
                }
                catch (System.InvalidOperationException)
                {
                    modeRejected = true;
                }

                return new Vector2(width, 100);
            };
            view.ApplySnapshot(data);
            Assert.That(heightRejected, Is.True);
            Assert.That(modeRejected, Is.True);
            yield return null;
        }
    }
}
