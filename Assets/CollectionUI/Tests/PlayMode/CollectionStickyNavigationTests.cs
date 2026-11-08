#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    public class ToggleStickyDuringBindCell : CollectionCell
    {
        public static bool ToggleNext;
        protected override void OnBind(LayoutEntry entry)
        {
            if (!ToggleNext || transform.parent.name != "StickyHeaders")
                return;
            ToggleNext = false;
            var sticky = GetComponentInParent<CollectionStickyHeaders>();
            sticky.StickyEnabled = false;
            sticky.StickyEnabled = true;
        }
    }

    public class CollectionStickyNavigationTests
    {
        GameObject root, template;
        CollectionView view;
        ScrollRect scroll;
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("View", typeof(RectTransform), typeof(ScrollRect));
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 100);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var vp = (RectTransform)viewport.transform;
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.sizeDelta = Vector2.zero;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp, false);
            scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = vp;
            scroll.content = (RectTransform)content.transform;
            view = root.AddComponent<CollectionView>();
            template = new GameObject("Template", typeof(RectTransform), typeof(CollectionCell));
            template.SetActive(false);
            view.RegisterTemplate("cell", template.GetComponent<CollectionCell>());
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(template);
        }

        static CollectionItem Item(string id, float height) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(0, height)
        };
        static CollectionSnapshot Data(bool secondHeader = true)
        {
            var data = new CollectionSnapshot();
            for (int i = 0; i < 3; i++)
            {
                var section = new CollectionSection
                {
                    Id = "s" + i,
                    Header = i == 1 && !secondHeader ? null : Item("h" + i, 40)
                };
                section.Layout.ColumnCount = 1;
                section.Layout.SectionSpacing = 20;
                section.Items.Add(Item("i" + i, 200));
                data.Sections.Add(section);
            }

            return data;
        }

        void Jump(string id, bool animated = false, float duration = .25f)
        {
            var method = typeof(CollectionView).GetMethod("ScrollToSection");
            Assert.That(method, Is.Not.Null, "ScrollToSection API must exist");
            method.Invoke(view, new object[] { id, animated, duration });
        }

        Component Sticky()
        {
            var type = typeof(CollectionView).Assembly.GetType("CollectionUI.CollectionStickyHeaders");
            Assert.That(type, Is.Not.Null, "Optional sticky header component must exist");
            return root.AddComponent(type);
        }

        CollectionCell OverlayCell() => scroll.viewport.Find("StickyHeaders")?.GetComponentInChildren<CollectionCell>();
        void Offset(float y)
        {
            scroll.StopMovement();
            scroll.content.anchoredPosition = new Vector2(0, y);
        }

        [Test]
        public void SectionNavigationUsesStableIdAndHeaderOrFirstItem()
        {
            view.ApplySnapshot(Data(false));
            Jump("s1");
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(260).Within(.01));
            Jump("missing");
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(260).Within(.01));
        }

        [UnityTest]
        public IEnumerator StickyPushesAtNextSectionAndDoesNotRebindWhileScrolling()
        {
            view.ApplySnapshot(Data());
            Sticky();
            Offset(220);
            yield return null;
            var cell = OverlayCell();
            Assert.That(cell, Is.Not.Null);
            Assert.That(cell.ItemId, Is.EqualTo("h0"));
            int binding = cell.BindingVersion;
            Offset(240);
            yield return null;
            Assert.That(OverlayCell(), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.EqualTo(binding));
            Assert.That(((RectTransform)cell.transform).anchoredPosition.y, Is.EqualTo(20).Within(.01));
            Offset(260);
            yield return null;
            Assert.That(OverlayCell().ItemId, Is.EqualTo("h1"));
        }

        [UnityTest]
        public IEnumerator HeaderlessSectionClearsPreviousStickyAtItsBoundary()
        {
            view.ApplySnapshot(Data(false));
            Sticky();
            Offset(250);
            yield return null;
            Assert.That(OverlayCell(), Is.Not.Null);
            Offset(260);
            yield return null;
            Assert.That(OverlayCell(), Is.Null);
        }

        [UnityTest]
        public IEnumerator StickyWaitsForHeaderTopAndDisableReleasesOverlay()
        {
            var data = Data();
            data.Sections[0].Layout.Insets = new Insets(0, 30, 0, 0);
            view.ApplySnapshot(data);
            var sticky = (Behaviour)Sticky();
            Offset(0);
            yield return null;
            Assert.That(OverlayCell(), Is.Null);
            Offset(50);
            yield return null;
            Assert.That(OverlayCell(), Is.Not.Null);
            sticky.enabled = false;
            yield return null;
            Assert.That(OverlayCell(), Is.Null);
            sticky.enabled = true;
            yield return null;
            Assert.That(OverlayCell(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator NewNavigationAndDisableCancelAnimation()
        {
            view.ApplySnapshot(Data());
            Jump("s2", true, .4f);
            yield return null;
            Jump("s1");
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(260).Within(.01));
            Jump("s2", true, .4f);
            yield return null;
            view.enabled = false;
            float y = scroll.content.anchoredPosition.y;
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(y).Within(.01));
        }

        [UnityTest]
        public IEnumerator StickyPreservesBindingAcrossEquivalentSnapshotAndRebindsChangedVersion()
        {
            var data = Data();
            view.ApplySnapshot(data);
            Sticky();
            Offset(80);
            yield return null;
            var cell = OverlayCell();
            int binding = cell.BindingVersion;
            view.ApplySnapshot(data);
            yield return null;
            Assert.That(OverlayCell(), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.EqualTo(binding));
            data.Sections[0].Header.Version++;
            view.ApplySnapshot(data);
            yield return null;
            Assert.That(OverlayCell(), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.GreaterThan(binding));
        }

        [UnityTest]
        public IEnumerator StickyRebindsExplicitSectionUpdateAndStyleInvalidation()
        {
            var data = Data();
            var payload = new[]
            {
                1
            };
            data.Sections[0].Header.Data = payload;
            view.ApplySnapshot(data);
            Sticky();
            Offset(80);
            yield return null;
            var cell = OverlayCell();
            int binding = cell.BindingVersion;
            payload[0] = 2;
            view.ApplyChanges(new CollectionChangeSet().UpdateSection("s0", data.Sections[0]));
            // 同帧后续等价提交不能抹掉尚未呈现的强制刷新。
            view.ApplySnapshot(data);
            yield return null;
            Assert.That(OverlayCell(), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.GreaterThan(binding), "Explicit section update refreshes a same-version, same-reference header");
            binding = cell.BindingVersion;
            view.InvalidateStyle();
            yield return null;
            Assert.That(OverlayCell(), Is.SameAs(cell));
            Assert.That(cell.BindingVersion, Is.GreaterThan(binding), "Style invalidation also refreshes the sticky clone");
        }

        [UnityTest]
        public IEnumerator ReloadHeaderRefreshesPinnedAndNaturalCellWithoutNewLayout()
        {
            var data = Data();
            var payload = new[]
            {
                1
            };
            data.Sections[0].Header.Data = payload;
            view.ApplySnapshot(data);
            Sticky();
            Offset(80);
            yield return null;
            var pinned = OverlayCell();
            CollectionCell natural = null;
            foreach (var cell in scroll.content.GetComponentsInChildren<CollectionCell>())
                if (cell.ItemId == "h0")
                    natural = cell;
            Assert.That(natural, Is.Not.Null);
            int pinnedBinding = pinned.BindingVersion, naturalBinding = natural.BindingVersion;
            var layout = view.Layout;
            payload[0] = 2;
            view.ReloadItems(new[] { "h0" });
            yield return null;
            Assert.That(view.Layout, Is.SameAs(layout));
            Assert.That(pinned.BindingVersion, Is.GreaterThan(pinnedBinding));
            Assert.That(natural.BindingVersion, Is.GreaterThan(naturalBinding));
            // 页眉退出原始可见范围后，显式重载仍须刷新吸顶副本。
            view.Overscan = 0;
            view.RefreshVisible();
            pinnedBinding = pinned.BindingVersion;
            payload[0] = 3;
            view.ReloadItems(new[] { "h0" });
            yield return null;
            Assert.That(pinned.BindingVersion, Is.GreaterThan(pinnedBinding));
        }

        [UnityTest]
        public IEnumerator StickyToggledFromBindingRecreatesBoundVisibleHeader()
        {
            var special = new GameObject("Callback template", typeof(RectTransform), typeof(ToggleStickyDuringBindCell));
            special.SetActive(false);
            try
            {
                view.RegisterTemplate("callback", special.GetComponent<ToggleStickyDuringBindCell>());
                var data = Data();
                data.Sections[0].Header.ReuseIdentifier = "callback";
                view.ApplySnapshot(data);
                Sticky();
                scroll.content.anchoredPosition = new Vector2(0, 20);
                ToggleStickyDuringBindCell.ToggleNext = true;
                yield return null;
                yield return null;
                yield return null;
                var overlay = scroll.viewport.Find("StickyHeaders");
                Assert.That(overlay, Is.Not.Null);
                var cell = overlay.GetComponentInChildren<CollectionCell>();
                Assert.That(cell, Is.Not.Null);
                Assert.That(cell.IsBound, Is.True);
                Assert.That(((RectTransform)cell.transform).rect.height, Is.EqualTo(40));
            }
            finally
            {
                ToggleStickyDuringBindCell.ToggleNext = false;
                UnityEngine.Object.DestroyImmediate(special);
            }
        }

        [UnityTest]
        public IEnumerator NavigationDuringLoadingReleasesScrollHoldButKeepsRequest()
        {
            view.ApplySnapshot(Data());
            var loadingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionUI.Samples.DemoAssetPaths.Asset("Card.prefab")).GetComponent<RectTransform>();
            var interaction = root.AddComponent<CollectionLoader>();
            interaction.Configure(loadingPrefab, loadingPrefab);
            long request = interaction.BeginRefresh();
            yield return null;
            Jump("s1", true, .1f);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(260).Within(.1));
            Assert.That(interaction.ActiveRequestId, Is.EqualTo(request));
            Assert.That(scroll.viewport.Find("RefreshLoading").gameObject.activeSelf, Is.True);
            Assert.That(interaction.CompleteRefresh(request), Is.True);
        }

        [UnityTest]
        public IEnumerator ExpandedModeCreatesNoStickyAndNavigationDoesNotMoveHost()
        {
            UnityEngine.Object.DestroyImmediate(root);
            root = new GameObject("Expanded", typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 100);
            view = root.AddComponent<CollectionView>();
            view.DisplayMode = CollectionDisplayMode.Expanded;
            view.RegisterTemplate("cell", template.GetComponent<CollectionCell>());
            view.ApplySnapshot(Data());
            Sticky();
            var position = ((RectTransform)root.transform).anchoredPosition;
            Jump("s2", true);
            yield return null;
            Assert.That(((RectTransform)root.transform).anchoredPosition, Is.EqualTo(position));
            Assert.That(root.transform.Find("StickyHeaders"), Is.Null);
        }

        sealed class UnorderedHeaderLayout : ICollectionLayout
        {
            public LayoutResult Build(CollectionSnapshot data, float width, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, System.Collections.Generic.ISet<string> dirtySections = null)
            {
                var first = data.Sections[0];
                var second = data.Sections[1];
                return LayoutResult.Create(new[] { new LayoutEntry(first.Items[0], first.Id, 7, 0, ElementKind.Item, new Rect(0, 70, width, 190)), new LayoutEntry(second.Header, second.Id, 9, -1, ElementKind.Header, new Rect(0, 300, width, 40)), new LayoutEntry(first.Header, first.Id, 7, -1, ElementKind.Header, new Rect(0, 30, width, 40)), new LayoutEntry(second.Items[0], second.Id, 9, 0, ElementKind.Item, new Rect(0, 340, width, 300)) }, width, 640);
            }
        }

        [UnityTest]
        public IEnumerator CustomLayoutFindsHeadersAwayFromFirstEntryAndWithSparseSectionIndices()
        {
            view.LayoutEngine = new UnorderedHeaderLayout();
            view.ApplySnapshot(Data());
            Sticky();
            Offset(60);
            yield return null;
            Assert.That(OverlayCell().ItemId, Is.EqualTo("h0"));
            Jump("s1");
            yield return null;
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(300).Within(.01));
            Assert.That(OverlayCell().ItemId, Is.EqualTo("h1"));
        }

        [UnityTest]
        public IEnumerator NavigationCancelsOnDragAndNewLayout()
        {
            view.ApplySnapshot(Data());
            Jump("s2", true, .4f);
            yield return null;
            var handler = (object)view as IBeginDragHandler;
            Assert.That(handler, Is.Not.Null);
            handler.OnBeginDrag(null);
            float y = scroll.content.anchoredPosition.y;
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(y).Within(.01));
            Jump("s2", true, .4f);
            yield return null;
            view.ApplySnapshot(Data());
            y = scroll.content.anchoredPosition.y;
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(y).Within(.01));
        }
    }
}
#endif

