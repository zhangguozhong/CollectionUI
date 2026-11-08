#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace CollectionUI.Tests
{
    public class CollectionLoaderTests
    {
        GameObject root, template, events;
        CollectionView view;
        ScrollRect scroll;
        CollectionLoader interaction;
        RectTransform top, bottom, loadingPrefab;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            root = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 500);
            scroll = root.GetComponent<ScrollRect>();
            scroll.movementType = ScrollRect.MovementType.Elastic;
            var vp = new GameObject("Viewport", typeof(RectTransform));
            vp.transform.SetParent(root.transform, false);
            var viewport = (RectTransform)vp.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = Vector2.zero;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp.transform, false);
            scroll.viewport = viewport;
            scroll.content = (RectTransform)content.transform;
            view = root.AddComponent<CollectionView>();
            template = new GameObject("Cell", typeof(RectTransform), typeof(CollectionCell));
            template.SetActive(false);
            view.RegisterTemplate("cell", template.GetComponent<CollectionCell>());
            view.ApplySnapshot(Data(30));
            loadingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionUI.Samples.DemoAssetPaths.Asset("Card.prefab")).GetComponent<RectTransform>();
            interaction = root.AddComponent<CollectionLoader>();
            interaction.Configure(loadingPrefab, loadingPrefab);
            events = new GameObject("Events", typeof(EventSystem));
            yield return null;
            var instances = viewport.Cast<Transform>().Where(t => t != scroll.content).ToArray();
            Assert.That(instances.Length, Is.EqualTo(2));
            top = (RectTransform)instances[0];
            bottom = (RectTransform)instances[1];
        }

        static CollectionSnapshot Data(int count)
        {
            var data = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "section"
            };
            section.Layout.ColumnCount = 1;
            for (int i = 0; i < count; i++)
                section.Items.Add(new CollectionItem { Id = "item" + i, ReuseIdentifier = "cell", Size = ItemSize.Fixed(0, 100) });
            data.Sections.Add(section);
            return data;
        }

        PointerEventData Pointer(int id = 1) => new PointerEventData(events.GetComponent<EventSystem>())
        {
            button = PointerEventData.InputButton.Left,
            pointerId = id
        };
        void Pull(float y)
        {
            var pointer = Pointer();
            interaction.OnBeginDrag(pointer);
            scroll.content.anchoredPosition = new Vector2(0, y);
            interaction.OnEndDrag(pointer);
        }

        [UnityTest]
        public IEnumerator ReleaseTriggersOnceAndLoadingWaitsForBusinessCompletion()
        {
            int count = 0;
            long id = 0;
            interaction.RefreshRequested += value =>
            {
                count++;
                id = value;
            };
            var pointer = Pointer();
            interaction.OnBeginDrag(pointer);
            scroll.content.anchoredPosition = new Vector2(0, -120);
            Assert.That(count, Is.Zero);
            interaction.OnEndDrag(pointer);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(interaction.IsRefreshing, Is.True);
            Assert.That(top.gameObject.activeSelf, Is.True);
            Assert.That(bottom.gameObject.activeSelf, Is.False);
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(interaction.IsRefreshing, Is.True, "No timed completion is allowed.");
            view.ApplySnapshot(Data(20));
            Assert.That(interaction.IsRefreshing, Is.True);
            Pull(-150);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(interaction.CompleteRefresh(id), Is.True);
            Assert.That(top.gameObject.activeSelf, Is.False);
            Assert.That(interaction.CompleteRefresh(id), Is.False);
            Assert.That(top, Is.Not.SameAs(loadingPrefab));
        }

        [UnityTest]
        public IEnumerator ThresholdProgrammaticOffsetAndWrongPointerDoNotRequest()
        {
            int count = 0;
            interaction.RefreshRequested += id => count++;
            scroll.content.anchoredPosition = new Vector2(0, -150);
            yield return null;
            Assert.That(count, Is.Zero);
            Pull(-40);
            Assert.That(count, Is.Zero);
            interaction.OnBeginDrag(Pointer(1));
            scroll.content.anchoredPosition = new Vector2(0, -150);
            interaction.OnEndDrag(Pointer(2));
            Assert.That(count, Is.Zero);
            interaction.OnEndDrag(Pointer(1));
            Assert.That(count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LoadMoreHasMoreAndShortContentAreSupported()
        {
            view.ApplySnapshot(Data(0));
            int count = 0;
            long id = 0;
            interaction.LoadMoreRequested += value =>
            {
                count++;
                id = value;
            };
            Pull(130);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(bottom.gameObject.activeSelf, Is.True);
            view.ApplySnapshot(Data(50));
            Assert.That(interaction.IsLoadingMore, Is.True);
            Assert.That(interaction.CompleteLoadMore(id, false), Is.True);
            Assert.That(interaction.HasMore, Is.False);
            Pull(5000);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(interaction.BeginLoadMore(), Is.Zero);
            interaction.HasMore = true;
            long completed = id;
            Assert.That(interaction.BeginLoadMore(), Is.GreaterThan(completed));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SynchronousCompletionAndLateResponsesAreSafe()
        {
            interaction.RefreshRequested += id =>
            {
                view.ApplySnapshot(Data(4));
                interaction.CompleteRefresh(id);
            };
            long old = interaction.BeginRefresh();
            Assert.That(old, Is.GreaterThan(0));
            Assert.That(interaction.IsRefreshing, Is.False);
            long current = interaction.BeginLoadMore();
            Assert.That(current, Is.GreaterThan(old));
            Assert.That(interaction.CompleteRefresh(old), Is.False);
            Assert.That(interaction.CompleteLoadMore(old), Is.False);
            Assert.That(interaction.ActiveRequestId, Is.EqualTo(current));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableCancelsRequestAndRetainsOwnedInstancesForReuse()
        {
            long old = interaction.BeginRefresh();
            interaction.enabled = false;
            Assert.That(interaction.ActiveRequestId, Is.Zero);
            Assert.That(interaction.CompleteRefresh(old), Is.False);
            Assert.That(top, Is.Not.Null);
            Assert.That(top.gameObject.activeSelf, Is.False);
            Assert.That(scroll.enabled, Is.True);
            interaction.enabled = true;
            Assert.That(interaction.BeginRefresh(), Is.GreaterThan(old));
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadingAllowsNewDragWithoutDispatchingSecondRequest()
        {
            int count = 0;
            interaction.RefreshRequested += id => count++;
            interaction.BeginRefresh();
            yield return null;
            interaction.OnBeginDrag(Pointer());
            scroll.content.anchoredPosition = new Vector2(0, 200);
            yield return null;
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0));
            interaction.OnEndDrag(Pointer());
            Assert.That(count, Is.EqualTo(1));
            Assert.That(interaction.IsRefreshing, Is.True);
        }

        [UnityTest]
        public IEnumerator PreloadRequiresUpwardGestureAndIsMutuallyExclusiveWithLoadMore()
        {
            interaction.PreloadEnabled = true;
            interaction.PreloadDistance = 200;
            int preload = 0, load = 0;
            long id = 0;
            interaction.PreloadRequested += value =>
            {
                preload++;
                id = value;
            };
            interaction.LoadMoreRequested += value => load++;
            float max = view.Layout.ContentHeight - scroll.viewport.rect.height;
            scroll.content.anchoredPosition = new Vector2(0, max - 100);
            yield return null;
            Assert.That(preload, Is.Zero);
            var pointer = Pointer();
            interaction.OnBeginDrag(pointer);
            pointer.delta = new Vector2(0, -10);
            interaction.OnDrag(pointer);
            Assert.That(preload, Is.Zero);
            pointer.delta = new Vector2(0, 10);
            scroll.content.anchoredPosition = new Vector2(0, max - 300);
            interaction.OnDrag(pointer);
            Assert.That(preload, Is.Zero);
            scroll.content.anchoredPosition = new Vector2(0, max - 100);
            interaction.OnDrag(pointer);
            Assert.That(preload, Is.EqualTo(1));
            Assert.That(interaction.IsPreloading, Is.True);
            Assert.That(top.gameObject.activeSelf, Is.False);
            Assert.That(bottom.gameObject.activeSelf, Is.False);
            Assert.That(interaction.BeginLoadMore(), Is.Zero);
            scroll.content.anchoredPosition = new Vector2(0, max + 150);
            interaction.OnDrag(pointer);
            interaction.OnEndDrag(pointer);
            Assert.That(load, Is.Zero);
            Assert.That(preload, Is.EqualTo(1));
            Assert.That(interaction.CompletePreload(id, false), Is.True);
            interaction.OnBeginDrag(pointer);
            interaction.OnDrag(pointer);
            interaction.OnEndDrag(pointer);
            Assert.That(preload, Is.EqualTo(1));
            Assert.That(load, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SynchronousCompletionStillOnlyOncePerGesture()
        {
            interaction.PreloadEnabled = true;
            int count = 0;
            interaction.PreloadRequested += id =>
            {
                count++;
                interaction.CompletePreload(id);
            };
            var pointer = Pointer();
            pointer.delta = new Vector2(0, 20);
            interaction.OnBeginDrag(pointer);
            scroll.content.anchoredPosition = new Vector2(0, view.Layout.ContentHeight - 500);
            interaction.OnDrag(pointer);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(1));
            scroll.content.anchoredPosition += new Vector2(0, 150);
            interaction.OnEndDrag(pointer);
            Assert.That(interaction.IsLoadingMore, Is.False);
            interaction.OnBeginDrag(pointer);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PreloadRequiresBothIdleRequestAndUnusedGesture()
        {
            interaction.PreloadEnabled = true;
            int count = 0;
            long id = 0;
            interaction.PreloadRequested += value =>
            {
                count++;
                id = value;
            };
            var pointer = Pointer();
            pointer.delta = new Vector2(0, 20);
            interaction.OnBeginDrag(pointer);
            scroll.content.anchoredPosition = new Vector2(0, view.Layout.ContentHeight - 600);
            interaction.OnDrag(pointer);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(1));
            interaction.CompletePreload(id);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(1), "Completed request does not reset gesture quota.");
            interaction.OnEndDrag(pointer);
            interaction.OnBeginDrag(pointer);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(2));
            long pending = id;
            interaction.OnEndDrag(pointer);
            interaction.OnBeginDrag(pointer);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(2), "New gesture cannot bypass pending request.");
            interaction.CompletePreload(pending);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(3), "An unused gesture may request after previous work completes.");
            interaction.CompletePreload(id, false);
            interaction.OnEndDrag(pointer);
            interaction.OnBeginDrag(pointer);
            interaction.OnDrag(pointer);
            Assert.That(count, Is.EqualTo(3));
            interaction.OnEndDrag(pointer);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PrefabInstancesAreIndependentReusedAndDestroyedWithController()
        {
            var originalSize = loadingPrefab.sizeDelta;
            Assert.That(top.GetInstanceID(), Is.Not.EqualTo(bottom.GetInstanceID()));
            Assert.That(top.parent, Is.EqualTo(scroll.viewport));
            Assert.That(bottom.parent, Is.EqualTo(scroll.viewport));
            long id = interaction.BeginRefresh();
            interaction.CompleteRefresh(id);
            interaction.BeginLoadMore();
            Assert.That(scroll.viewport.childCount, Is.EqualTo(3), "Two instances are reused across requests.");
            Object.Destroy(interaction);
            yield return null;
            yield return null;
            Assert.That(top == null && bottom == null, Is.True, "Controller owns generated instances.");
            Assert.That(loadingPrefab, Is.Not.Null);
            Assert.That(loadingPrefab.sizeDelta, Is.EqualTo(originalSize));
        }

        [UnityTest]
        public IEnumerator ReconfigureReplacesOwnedInstancesAndRejectsSceneReferences()
        {
            Assert.Throws<System.ArgumentException>(() => interaction.Configure(top, bottom));
            long id = interaction.BeginRefresh();
            Assert.That(top.gameObject.activeSelf, Is.True);
            interaction.CompleteRefresh(id);
            var oldTop = top;
            var oldBottom = bottom;
            interaction.Configure(loadingPrefab, loadingPrefab);
            yield return null;
            Assert.That(oldTop == null && oldBottom == null, Is.True);
            Assert.That(scroll.viewport.childCount, Is.EqualTo(1));
            interaction.BeginLoadMore();
            Assert.That(scroll.viewport.childCount, Is.EqualTo(3));
            Assert.That(scroll.viewport.Find("LoadMoreLoading").gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator RealUpwardPointerDragScrollsTowardBottomAndPreloads()
        {
            interaction.PreloadEnabled = true;
            interaction.PreloadDistance = 200;
            int count = 0;
            interaction.PreloadRequested += id => count++;
            float max = view.Layout.ContentHeight - scroll.viewport.rect.height;
            scroll.content.anchoredPosition = new Vector2(0, max - 250);
            Canvas.ForceUpdateCanvases();
            var pointer = Pointer();
            pointer.position = new Vector2(100, 100);
            scroll.OnInitializePotentialDrag(pointer);
            scroll.OnBeginDrag(pointer);
            interaction.OnBeginDrag(pointer);
            float before = scroll.content.anchoredPosition.y;
            pointer.position += new Vector2(0, 100);
            pointer.delta = new Vector2(0, 100);
            scroll.OnDrag(pointer);
            interaction.OnDrag(pointer);
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(before), "Screen Y increases when the finger moves upward.");
            Assert.That(count, Is.EqualTo(1));
            scroll.OnEndDrag(pointer);
            interaction.OnEndDrag(pointer);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadingHoldReservesFullIndicatorHeightUntilExplicitCompletion()
        {
            long id = interaction.BeginRefresh();
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(-interaction.TriggerDistance).Within(3));
            Assert.That(interaction.IsRefreshing, Is.True);
            interaction.CompleteRefresh(id);
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0).Within(3));
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Elastic));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Object.Destroy(root);
            Object.Destroy(template);
            Object.Destroy(events);
            yield return null;
        }
    }
}
#endif

