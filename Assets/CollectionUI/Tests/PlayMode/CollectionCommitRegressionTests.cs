using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    public class CommitRegressionCell : CollectionCell
    {
        public static int Layouts, Binds;
        public static bool FailBind, FailRelease;
        protected override void OnBind(LayoutEntry entry)
        {
            Binds++;
            if (FailBind)
                throw new InvalidOperationException("bind failure");
        }

        protected override void OnLayoutUpdated(LayoutEntry entry)
        {
            Layouts++;
        }

        protected override void OnUnbind()
        {
            if (FailRelease)
                throw new InvalidOperationException("release failure");
        }
    }

    public class CollectionCommitRegressionTests
    {
        GameObject root, template, events;
        CollectionView view;
        ScrollRect scroll;
        [SetUp]
        public void SetUp()
        {
            CommitRegressionCell.Layouts = CommitRegressionCell.Binds = 0;
            CommitRegressionCell.FailBind = CommitRegressionCell.FailRelease = false;
            events = new GameObject("Events", typeof(EventSystem));
            root = new GameObject("View", typeof(RectTransform), typeof(ScrollRect));
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 100);
            var vp = new GameObject("Viewport", typeof(RectTransform));
            vp.transform.SetParent(root.transform, false);
            var rect = (RectTransform)vp.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp.transform, false);
            scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = rect;
            scroll.content = (RectTransform)content.transform;
            view = root.AddComponent<CollectionView>();
            view.Overscan = 0;
            template = new GameObject("Template", typeof(RectTransform), typeof(CommitRegressionCell));
            template.SetActive(false);
            view.RegisterTemplate("cell", template.GetComponent<CollectionCell>());
        }

        [TearDown]
        public void TearDown()
        {
            CommitRegressionCell.FailBind = CommitRegressionCell.FailRelease = false;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(events);
        }

        static CollectionItem Item(string id) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(0, 100)
        };
        static CollectionSnapshot Data()
        {
            var data = new CollectionSnapshot();
            for (int s = 0; s < 2; s++)
            {
                var section = new CollectionSection
                {
                    Id = "s" + s
                };
                section.Layout.ColumnCount = 1;
                for (int i = 0; i < 20; i++)
                    section.Items.Add(Item("i" + (s * 20 + i)));
                data.Sections.Add(section);
            }

            return data;
        }

        PointerEventData Begin()
        {
            var p = new PointerEventData(events.GetComponent<EventSystem>())
            {
                button = PointerEventData.InputButton.Left,
                position = new Vector2(30, 30)
            };
            ExecuteEvents.Execute(root, p, ExecuteEvents.beginDragHandler);
            Drag(p);
            return p;
        }

        void Drag(PointerEventData p)
        {
            p.position += new Vector2(0, 10);
            ExecuteEvents.Execute(root, p, ExecuteEvents.dragHandler);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ImmediateNavigationDuringDragKeepsNextDelta(bool section)
        {
            view.ApplySnapshot(Data());
            scroll.content.anchoredPosition = new Vector2(0, 500);
            var p = Begin();
            if (section)
                view.ScrollToSection("s1");
            else
                view.ScrollToItem("i20");
            float target = scroll.content.anchoredPosition.y;
            Drag(p);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(target + 10).Within(.1f));
            ExecuteEvents.Execute(root, p, ExecuteEvents.endDragHandler);
        }

        [UnityTest]
        public IEnumerator AnimatedNavigationDuringDragKeepsNextDelta()
        {
            view.ApplySnapshot(Data());
            scroll.content.anchoredPosition = new Vector2(0, 500);
            var p = Begin();
            view.ScrollToSection("s1", true, .04f);
            yield return new WaitForSecondsRealtime(.08f);
            float target = scroll.content.anchoredPosition.y;
            Drag(p);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(target + 10).Within(.1f));
            ExecuteEvents.Execute(root, p, ExecuteEvents.endDragHandler);
        }

        [UnityTest]
        public IEnumerator PointerMovementCancelsNavigationStartedDuringDrag()
        {
            view.ApplySnapshot(Data());
            scroll.content.anchoredPosition = new Vector2(0, 500);
            var pointer = Begin();
            view.ScrollToSection("s1", true, 10);
            yield return null;
            yield return null;
            float before = scroll.content.anchoredPosition.y;
            Drag(pointer);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(before + 10).Within(.1f));
            ExecuteEvents.Execute(root, pointer, ExecuteEvents.endDragHandler);
            scroll.StopMovement();
            float stopped = scroll.content.anchoredPosition.y;
            yield return null;
            yield return null;
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(stopped).Within(.1f));
        }

        [Test]
        public void PendingLayoutSurvivesFailureAndConsecutiveCommit()
        {
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 200);
            view.ApplySnapshot(Data());
            CommitRegressionCell.FailBind = true;
            Assert.Throws<InvalidOperationException>(() => view.ApplyChanges(new CollectionChangeSet().InsertItem("s0", 0, Item("head")).UpdateItem("i0", Item("i0"))));
            CommitRegressionCell.FailBind = false;
            CommitRegressionCell.Layouts = 0;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
            foreach (var cell in scroll.content.GetComponentsInChildren<CommitRegressionCell>())
            {
                view.Layout.TryGetItem(cell.ItemId, out var entry);
                Assert.That(((RectTransform)cell.transform).anchoredPosition.y, Is.EqualTo(-entry.Rect.y));
            }

            Assert.That(CommitRegressionCell.Layouts, Is.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedBindCanRetryWithoutHiddenActiveCell(bool failRelease)
        {
            CommitRegressionCell.FailBind = true;
            CommitRegressionCell.FailRelease = failRelease;
            Assert.That(Assert.Throws<InvalidOperationException>(() => view.ApplySnapshot(Data())).Message, Is.EqualTo("bind failure"));
            Assert.That(view.ActiveCellCount, Is.Zero);
            CommitRegressionCell.FailBind = CommitRegressionCell.FailRelease = false;
            view.RefreshVisible();
            Assert.That(view.ActiveCellCount, Is.GreaterThan(0));
            Assert.That(scroll.content.GetComponentsInChildren<CommitRegressionCell>().Length, Is.EqualTo(view.ActiveCellCount));
        }

        [Test]
        public void TailAppendDoesNotNotifyUnchangedVisibleGeometryButUpdateStillBinds()
        {
            view.ApplySnapshot(Data());
            CommitRegressionCell.Layouts = CommitRegressionCell.Binds = 0;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
            Assert.That(CommitRegressionCell.Layouts, Is.Zero);
            Assert.That(CommitRegressionCell.Binds, Is.Zero);
            view.ApplyChanges(new CollectionChangeSet().UpdateItem("i0", Item("i0")));
            Assert.That(CommitRegressionCell.Binds, Is.EqualTo(1));
        }

        [Test]
        public void DirtyAncestorLayoutDeterminesWidthBeforeCommit()
        {
            var parent = new GameObject("Parent", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            try
            {
                var rect = (RectTransform)parent.transform;
                rect.sizeDelta = new Vector2(600, 100);
                root.transform.SetParent(parent.transform, false);
                var group = parent.GetComponent<HorizontalLayoutGroup>();
                group.childControlWidth = true;
                group.childForceExpandWidth = true;
                view.ApplySnapshot(Data());
                Assert.That(view.Layout.ContentWidth, Is.EqualTo(600).Within(.1f));
                group.padding = new RectOffset(35, 65, 0, 0);
                view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
                Assert.That(view.Layout.ContentWidth, Is.EqualTo(500).Within(.1f));
            }
            finally
            {
                root.transform.SetParent(null, false);
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void FailedRetainedBindingRetriesAcrossAnotherCommit()
        {
            view.ApplySnapshot(Data());
            CommitRegressionCell.FailBind = true;
            Assert.Throws<InvalidOperationException>(() => view.ApplyChanges(new CollectionChangeSet().UpdateItem("i0", Item("i0"))));
            CommitRegressionCell.FailBind = false;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
            Assert.That(scroll.content.GetComponentsInChildren<CommitRegressionCell>().Length, Is.EqualTo(view.ActiveCellCount));
            Assert.That(view.ActiveCellCount, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator ReloadSharedSupplementaryIdAlsoRefreshesOffscreenStickyCopy()
        {
            var data = Data();
            data.Sections[0].Header = Item("shared");
            data.Sections[0].Footer = Item("shared");
            data.Sections[1].Header = Item("shared");
            root.AddComponent<CollectionStickyHeaders>();
            view.ApplySnapshot(data);
            view.ScrollToItem("i5");
            yield return null;
            int before = CommitRegressionCell.Binds;
            view.ReloadItems(new[] { "shared", "shared", "missing" });
            yield return null;
            Assert.That(CommitRegressionCell.Binds, Is.EqualTo(before + 1));
            view.ScrollToSection("s1");
            yield return null;
            before = CommitRegressionCell.Binds;
            view.ReloadItems(new[] { "shared" });
            yield return null;
            Assert.That(CommitRegressionCell.Binds, Is.EqualTo(before + 2));
        }

        [Test]
        public void ReleaseFailureClearsTokenAndBindingAndCanRetryVisibility()
        {
            view.ApplySnapshot(Data());
            var cell = scroll.content.GetComponentInChildren<CommitRegressionCell>();
            var token = cell.BindingCancellation;
            CommitRegressionCell.FailRelease = true;
            scroll.content.anchoredPosition = new Vector2(0, 1000);
            Assert.That(Assert.Throws<InvalidOperationException>(() => view.RefreshVisible()).Message, Is.EqualTo("release failure"));
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(cell.IsBound, Is.False);
            Assert.That(cell.ItemId, Is.Null);
            CommitRegressionCell.FailRelease = false;
            view.RefreshVisible();
            Assert.That(view.ActiveCellCount, Is.GreaterThan(0));
            Assert.That(scroll.content.GetComponentsInChildren<CommitRegressionCell>().Length, Is.EqualTo(view.ActiveCellCount));
        }

        [Test]
        public void CancellationFailureStillRunsUnbindAndClearsIdentity()
        {
            view.ApplySnapshot(Data());
            var cell = scroll.content.GetComponentInChildren<CommitRegressionCell>();
            cell.BindingCancellation.Register(() => throw new InvalidOperationException("cancel failure"));
            scroll.content.anchoredPosition = new Vector2(0, 1000);
            Assert.Throws<AggregateException>(() => view.RefreshVisible());
            Assert.That(cell.IsBound, Is.False);
            Assert.That(cell.ItemId, Is.Null);
            Assert.That(cell.BindingCancellation.IsCancellationRequested, Is.True);
            view.RefreshVisible();
            Assert.That(view.ActiveCellCount, Is.GreaterThan(0));
        }

        [Test]
        public void HostContentSizeFitterResolvesPreferredWidthBeforeCommit()
        {
            var element = root.AddComponent<LayoutElement>();
            element.preferredWidth = 600;
            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.ApplySnapshot(Data());
            Assert.That(view.Layout.ContentWidth, Is.EqualTo(600).Within(.1f));
            element.preferredWidth = 500;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
            Assert.That(view.Layout.ContentWidth, Is.EqualTo(500).Within(.1f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ViewportFitterAcrossPlainWrapperResolvesBeforeCommit(bool disconnected)
        {
            GameObject outer = null;
            if (disconnected)
            {
                outer = new GameObject("Outer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                ((RectTransform)outer.transform).sizeDelta = new Vector2(800, 200);
                var wrapper = new GameObject("Wrapper", typeof(RectTransform));
                wrapper.transform.SetParent(outer.transform, false);
                root.transform.SetParent(wrapper.transform, false);
            }

            try
            {
                var element = scroll.viewport.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 600;
                var fitter = scroll.viewport.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                view.ApplySnapshot(Data());
                Assert.That(view.Layout.ContentWidth, Is.EqualTo(600).Within(.1f));
                element.preferredWidth = 500;
                view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
                Assert.That(view.Layout.ContentWidth, Is.EqualTo(500).Within(.1f));
            }
            finally
            {
                root.transform.SetParent(null, false);
                if (outer != null)
                    UnityEngine.Object.DestroyImmediate(outer);
            }
        }

        [Test]
        public void SelfControllerAncestorDoesNotSkipViewportPreferredSizeCalculation()
        {
            var outer = new GameObject("AspectParent", typeof(RectTransform), typeof(AspectRatioFitter));
            ((RectTransform)outer.transform).sizeDelta = new Vector2(800, 200);
            root.transform.SetParent(outer.transform, false);
            try
            {
                var group = scroll.viewport.gameObject.AddComponent<HorizontalLayoutGroup>();
                group.childControlWidth = true;
                group.childForceExpandWidth = false;
                var fitter = scroll.viewport.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                var element = scroll.content.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 600;
                view.ApplySnapshot(Data());
                Assert.That(view.Layout.ContentWidth, Is.EqualTo(600).Within(.1f));
                element.preferredWidth = 500;
                view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
                Assert.That(view.Layout.ContentWidth, Is.EqualTo(500).Within(.1f));
            }
            finally
            {
                root.transform.SetParent(null, false);
                UnityEngine.Object.DestroyImmediate(outer);
            }
        }

        [Test]
        public void AutoExpandingScrollbarResolvesViewportWidthBeforeCommit()
        {
            view.ApplySnapshot(Data());
            var bar = new GameObject("VerticalScrollbar", typeof(RectTransform), typeof(Scrollbar));
            bar.transform.SetParent(root.transform, false);
            var rect = (RectTransform)bar.transform;
            rect.sizeDelta = new Vector2(20, 100);
            scroll.verticalScrollbar = bar.GetComponent<Scrollbar>();
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
            Assert.That(view.Layout.ContentWidth, Is.EqualTo(380).Within(.1f));
            rect.sizeDelta = new Vector2(40, 100);
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 21, Item("tail2")));
            Assert.That(view.Layout.ContentWidth, Is.EqualTo(360).Within(.1f));
        }

        [Test]
        public void StableCommitDoesNotForceGlobalCanvasCallbacks()
        {
            view.ApplySnapshot(Data());
            Canvas.ForceUpdateCanvases();
            int calls = 0;
            Canvas.WillRenderCanvases callback = () => calls++;
            Canvas.willRenderCanvases += callback;
            try
            {
                view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 20, Item("tail")));
            }
            finally
            {
                Canvas.willRenderCanvases -= callback;
            }

            Assert.That(calls, Is.Zero);
        }
    }
}
