#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CollectionUI.Tests
{
    /// <summary>真实 ScrollRect 连续滚动、锚点、重复工作与组件资源清理回归。</summary>
    public class CollectionSmoothnessTests
    {
        GameObject root, template, events;
        CollectionView view;
        ScrollRect scroll;
        [SetUp]
        public void Setup()
        {
            root = new GameObject("Smooth collection", typeof(RectTransform), typeof(ScrollRect));
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 500);
            scroll = root.GetComponent<ScrollRect>();
            scroll.movementType = ScrollRect.MovementType.Elastic;
            var vp = (RectTransform)new GameObject("Viewport", typeof(RectTransform)).transform;
            vp.SetParent(root.transform, false);
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.sizeDelta = Vector2.zero;
            var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
            content.SetParent(vp, false);
            scroll.viewport = vp;
            scroll.content = content;
            template = new GameObject("Template", typeof(RectTransform), typeof(CollectionCell));
            template.SetActive(false);
            view = root.AddComponent<CollectionView>();
            view.RegisterTemplate("cell", template.GetComponent<CollectionCell>());
            var data = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "s"
            };
            section.Layout.ColumnCount = 1;
            for (int i = 0; i < 30; i++)
                section.Items.Add(Item("i" + i));
            data.Sections.Add(section);
            view.ApplySnapshot(data);
            events = new GameObject("Events", typeof(EventSystem));
        }

        static CollectionItem Item(string id) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(0, 100)
        };
        void Begin(PointerEventData p) => ExecuteEvents.Execute(root, p, ExecuteEvents.beginDragHandler);
        void Drag(PointerEventData p) => ExecuteEvents.Execute(root, p, ExecuteEvents.dragHandler);
        void End(PointerEventData p) => ExecuteEvents.Execute(root, p, ExecuteEvents.endDragHandler);
        PointerEventData Pointer() => new PointerEventData(events.GetComponent<EventSystem>())
        {
            button = PointerEventData.InputButton.Left,
            pointerId = 1,
            position = new Vector2(100, 100)
        };
        sealed class CapturingLayout : ICollectionLayout
        {
            public CollectionSnapshot Input;
            public LayoutResult Build(CollectionSnapshot data, float width, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, System.Collections.Generic.ISet<string> dirtySections = null)
            {
                Input = data;
                return SectionedLayout.Build(data, width, measure, previous, dirtySections);
            }
        }

        [UnityTest]
        public IEnumerator CustomLayoutCannotMutateCommittedSnapshotThroughItsInput()
        {
            var custom = new CapturingLayout();
            view.LayoutEngine = custom;
            yield return null;
            Assert.That(custom.Input, Is.Not.Null);
            custom.Input.Sections[0].Items.Clear();
            custom.Input.Sections[0].Layout.ColumnCount = 7;
            var committed = view.GetSnapshotCopy();
            Assert.That(committed.Sections[0].Items.Count, Is.EqualTo(30));
            Assert.That(committed.Sections[0].Layout.ColumnCount, Is.EqualTo(1));
            Assert.That(view.Layout.Entries.Count, Is.EqualTo(30));
        }

        [UnityTest]
        public IEnumerator AppendKeepsInertiaAndVisibleCellBinding()
        {
            scroll.content.anchoredPosition = new Vector2(0, 500);
            view.RefreshVisible();
            var cell = root.GetComponentsInChildren<CollectionCell>().Single(c => c.ItemId == "i5");
            int version = cell.BindingVersion;
            scroll.velocity = new Vector2(0, 200);
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 30, Item("added")));
            Assert.That(scroll.velocity.y, Is.EqualTo(200));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(500));
            Assert.That(cell.BindingVersion, Is.EqualTo(version));
            Assert.That(cell.gameObject.activeInHierarchy, Is.True);
            float previous = 500;
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThanOrEqualTo(previous));
                Assert.That(view.GetVisibleItems(), Is.Not.Empty);
                Assert.That(cell.gameObject.activeInHierarchy, Is.True);
                Assert.That(cell.BindingVersion, Is.EqualTo(version));
                previous = scroll.content.anchoredPosition.y;
            }

            Assert.That(previous, Is.GreaterThan(500));
        }

        [UnityTest]
        public IEnumerator InsertDuringDragPreservesAnchorAndRebasesNextPointerDelta()
        {
            scroll.content.anchoredPosition = new Vector2(0, 500);
            view.RefreshVisible();
            var pointer = Pointer();
            Begin(pointer);
            pointer.position += new Vector2(0, 20);
            pointer.delta = new Vector2(0, 20);
            Drag(pointer);
            float before = scroll.content.anchoredPosition.y;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 0, Item("inserted")));
            float anchored = scroll.content.anchoredPosition.y;
            Assert.That(anchored - before, Is.EqualTo(100).Within(.01f));
            pointer.position += new Vector2(0, 10);
            pointer.delta = new Vector2(0, 10);
            Drag(pointer);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(anchored + 10).Within(.01f));
            End(pointer);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ElasticDragAfterDataArrivalDoesNotApplyRubberCompressionTwice()
        {
            foreach (bool atBottom in new[]
            {
                false,
                true
            }

            )
            {
                root.transform.localScale = atBottom ? new Vector3(.75f, .75f, 1) : Vector3.one;
                scroll.content.anchoredPosition = new Vector2(0, atBottom ? view.Layout.ContentHeight - scroll.viewport.rect.height : 0);
                var pointer = Pointer();
                Begin(pointer);
                float delta = atBottom ? 320 : -160;
                pointer.position += new Vector2(0, delta);
                pointer.delta = new Vector2(0, delta);
                Drag(pointer);
                float before = scroll.content.anchoredPosition.y;
                int count = view.GetSnapshotCopy().Sections[0].Items.Count;
                view.ApplyChanges(new CollectionChangeSet().InsertItem("s", count, Item("extra" + count)));
                Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(before).Within(.01f));
                pointer.delta = Vector2.zero;
                Drag(pointer);
                Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(before).Within(.05f), "Same finger position must keep the same displayed offset.");
                End(pointer);
            }

            yield return null;
        }

        [Test]
        public void SnapshotDuringPullDoesNotSnapOverscrollToZero()
        {
            scroll.content.anchoredPosition = new Vector2(0, -70);
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 30, Item("added")));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(-70));
        }

        [UnityTest]
        public IEnumerator DestroyingOnlyViewDestroysItsPoolAndGeneratedCells()
        {
            var pool = root.transform.Find("CellPool");
            Object.Destroy(view);
            yield return null;
            yield return null;
            Assert.That(pool == null, Is.True);
            Assert.That(root.GetComponentsInChildren<CollectionCell>(true), Is.Empty);
        }

        [UnityTest]
        public IEnumerator UnchangedPresentationSkipsQueriesButViewportChangesRefresh()
        {
            var counter = typeof(CollectionView).GetProperty("VisibleRefreshCount");
            Assert.That(counter, Is.Not.Null, "Expose real visibility passes for profiling.");
            view.RefreshVisible();
            int baseline = (int)counter.GetValue(view);
            for (int i = 0; i < 100; i++)
                view.RefreshVisible();
            yield return null;
            yield return null;
            Assert.That((int)counter.GetValue(view), Is.EqualTo(baseline));
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 600);
            view.RefreshVisible();
            Assert.That((int)counter.GetValue(view), Is.GreaterThan(baseline));
            baseline = (int)counter.GetValue(view);
            view.Overscan += 100;
            view.RefreshVisible();
            Assert.That((int)counter.GetValue(view), Is.GreaterThan(baseline));
        }

        [UnityTest]
        public IEnumerator RepeatedInvalidationCoalescesIntoSingleLayout()
        {
            int builds = view.LayoutBuildCount;
            for (int i = 0; i < 1000; i++)
                view.InvalidateItemSize("i0");
            Assert.That(view.LayoutBuildCount, Is.EqualTo(builds));
            yield return null;
            Assert.That(view.LayoutBuildCount, Is.EqualTo(builds + 1));
        }

        [UnityTest]
        public IEnumerator LoadingThresholdChangeUpdatesGeometryAndHoldConvergesWithoutJitter()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            var loader = root.AddComponent<CollectionLoader>();
            loader.Configure(prefab, prefab);
            yield return null;
            loader.TriggerDistance = 160;
            long id = loader.BeginRefresh();
            Assert.That(((RectTransform)scroll.viewport.Find("RefreshLoading")).rect.height, Is.EqualTo(160));
            yield return new WaitForSecondsRealtime(1.5f);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(-160).Within(.15f));
            }

            loader.CompleteRefresh(id);
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0).Within(.15f));
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Elastic));
        }

        [UnityTest]
        public IEnumerator GrabbingHeldLoadingDoesNotRecompressTheVisiblePull()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            var loader = root.AddComponent<CollectionLoader>();
            loader.Configure(prefab, prefab);
            yield return null;
            long id = loader.BeginRefresh();
            yield return new WaitForSecondsRealtime(1.5f);
            float before = scroll.content.anchoredPosition.y;
            var pointer = Pointer();
            Begin(pointer);
            Drag(pointer);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(before).Within(.05f));
            pointer.position += new Vector2(0, 10);
            pointer.delta = new Vector2(0, 10);
            Drag(pointer);
            Assert.That(scroll.content.anchoredPosition.y - before, Is.InRange(0f, 10f));
            Assert.That(loader.ActiveRequestId, Is.EqualTo(id));
            End(pointer);
            loader.CompleteRefresh(id);
        }

        [UnityTest]
        public IEnumerator LoaderReturnsMotionControlOnDragNavigationAndDisable()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            var loader = root.AddComponent<CollectionLoader>();
            loader.Configure(prefab, prefab);
            yield return null;
            long id = loader.BeginRefresh();
            var pointer = Pointer();
            Begin(pointer);
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Elastic));
            Assert.That(loader.ActiveRequestId, Is.EqualTo(id));
            End(pointer);
            loader.CompleteRefresh(id);
            id = loader.BeginLoadMore();
            view.ScrollToItem("i5");
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Elastic));
            Assert.That(loader.ActiveRequestId, Is.EqualTo(id));
            float y = scroll.content.anchoredPosition.y;
            yield return null;
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(y).Within(.01f));
            loader.CompleteLoadMore(id);
            loader.BeginRefresh();
            loader.enabled = false;
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Elastic));
            Assert.That(loader.ActiveRequestId, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DataAndRefreshCompletionKeepCellsBoundDuringReturn()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            var loader = root.AddComponent<CollectionLoader>();
            loader.Configure(prefab, prefab);
            yield return null;
            long id = loader.BeginRefresh();
            yield return new WaitForSecondsRealtime(1);
            var cell = root.GetComponentsInChildren<CollectionCell>().Single(c => c.ItemId == "i0");
            int version = cell.BindingVersion;
            float before = scroll.content.anchoredPosition.y;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 30, Item("appended")));
            loader.CompleteRefresh(id);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(before).Within(.01f));
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                Assert.That(cell.gameObject.activeInHierarchy, Is.True);
                Assert.That(cell.IsBound, Is.True);
                Assert.That(cell.BindingVersion, Is.EqualTo(version));
                Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThanOrEqualTo(before));
                before = scroll.content.anchoredPosition.y;
            }
        }

        [UnityTest]
        public IEnumerator TenThousandItemsPagedAppendReportsWholeCommitAndRetainsVisibleCells()
        {
            var data = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "large"
            };
            section.Layout.ColumnCount = 2;
            for (int i = 0; i < 10000; i++)
                section.Items.Add(Item("large" + i));
            data.Sections.Add(section);
            view.ApplySnapshot(data);
            view.ScrollToItem("large9900");
            var watch = new System.Diagnostics.Stopwatch();
            var timings = new double[10];
            for (int page = 0; page < timings.Length; page++)
            {
                var current = root.GetComponentsInChildren<CollectionCell>().First(c => c.IsBound);
                int version = current.BindingVersion;
                var changes = new CollectionChangeSet();
                for (int i = 0; i < 20; i++)
                    changes.InsertItem("large", 10000 + page * 20 + i, Item("page" + page + "-" + i));
                scroll.velocity = new Vector2(0, 200);
                watch.Restart();
                view.ApplyChanges(changes);
                watch.Stop();
                timings[page] = watch.Elapsed.TotalMilliseconds;
                Assert.That(current.BindingVersion, Is.EqualTo(version));
                Assert.That(current.gameObject.activeInHierarchy, Is.True);
                Assert.That(scroll.velocity.y, Is.EqualTo(200));
                yield return null;
                Assert.That(view.GetVisibleItems(), Is.Not.Empty);
            }

            Array.Sort(timings);
            Debug.Log("COLLECTION_UI_APPEND_BENCHMARK 10000 items / 10 pages x20: meanMs=" + timings.Average().ToString("F3") + "; maxMs=" + timings[9].ToString("F3"));
            Assert.That(view.Layout.Entries.Count, Is.EqualTo(10200));
        }

        [UnityTest]
        public IEnumerator BottomLoadingAppendAndCompletionDuringDragKeepPointerContinuous()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            var loader = root.AddComponent<CollectionLoader>();
            loader.Configure(prefab, prefab);
            yield return null;
            view.ScrollToItem("i29", 1);
            long id = loader.BeginLoadMore();
            yield return new WaitForSecondsRealtime(1);
            float held = scroll.content.anchoredPosition.y;
            var pointer = Pointer();
            Begin(pointer);
            Drag(pointer);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(held).Within(.1f));
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s", 30, Item("next-page")));
            Assert.That(loader.CompleteLoadMore(id), Is.True);
            float after = scroll.content.anchoredPosition.y;
            Drag(pointer);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(after).Within(.1f));
            pointer.delta = new Vector2(0, -30);
            pointer.position += pointer.delta;
            Drag(pointer);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(after - 30).Within(.1f));
            End(pointer);
            yield return null;
            AssertVisibleBindings();
        }

        [UnityTest]
        public IEnumerator PendingPreloadAcrossRealGesturesDoesNotDuplicateAndRejectsOldCompletion()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            var loader = root.AddComponent<CollectionLoader>();
            loader.Configure(prefab, prefab);
            loader.PreloadEnabled = true;
            int requests = 0, loadMore = 0;
            loader.PreloadRequested += id => requests++;
            loader.LoadMoreRequested += id => loadMore++;
            yield return null;
            scroll.content.anchoredPosition = new Vector2(0, 2400);
            var first = Pointer();
            Begin(first);
            first.delta = new Vector2(0, 50);
            first.position += first.delta;
            Drag(first);
            long old = loader.ActiveRequestId;
            Assert.That(requests, Is.EqualTo(1));
            End(first);
            var second = Pointer();
            Begin(second);
            for (int i = 0; i < 10; i++)
            {
                second.delta = new Vector2(0, 40);
                second.position += second.delta;
                Drag(second);
                yield return null;
                Assert.That(loader.ActiveRequestId, Is.EqualTo(old));
                Assert.That(requests, Is.EqualTo(1));
            }

            loader.CompletePreload(old);
            second.position += second.delta;
            Drag(second);
            long current = loader.ActiveRequestId;
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(current, Is.Not.EqualTo(old));
            Assert.That(loader.CompletePreload(old, false), Is.False);
            Assert.That(loader.ActiveRequestId, Is.EqualTo(current));
            loader.CompletePreload(current, false);
            for (int i = 0; i < 10; i++)
            {
                second.position += second.delta;
                Drag(second);
                yield return null;
            }

            End(second);
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(loadMore, Is.Zero);
            Assert.That(loader.HasMore, Is.False);
        }

        // 真实事件派发跨越多屏，不显式调用 RefreshVisible，验证滚动链路自行补齐 Cell。
        [UnityTest]
        public IEnumerator FastDragReversalAndInertiaKeepEveryVisibleItemBound()
        {
            var data = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "fast"
            };
            section.Layout.ColumnCount = 3;
            for (int i = 0; i < 10000; i++)
                section.Items.Add(new CollectionItem { Id = "fast" + i, ReuseIdentifier = "cell", Size = ItemSize.Fixed(0, i == 13 ? 1400 : 80 + i % 9 * 30) });
            data.Sections.Add(section);
            view.ApplySnapshot(data);
            scroll.content.anchoredPosition = new Vector2(0, 20000);
            var pointer = Pointer();
            Begin(pointer);
            int count = 10000;
            for (int frame = 0; frame < 90; frame++)
            {
                float delta = frame % 30 < 15 ? 1300 : -1100;
                pointer.delta = new Vector2(0, delta);
                pointer.position += pointer.delta;
                Drag(pointer);
                if (frame % 15 == 0)
                    view.ApplyChanges(new CollectionChangeSet().InsertItem("fast", count++, Item("append" + frame)));
                yield return null;
                AssertVisibleBindings();
            }

            End(pointer);
            scroll.velocity = new Vector2(0, 60000);
            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
                AssertVisibleBindings();
            }
        }

        void AssertVisibleBindings()
        {
            var rect = new Rect(0, scroll.content.anchoredPosition.y, scroll.viewport.rect.width, scroll.viewport.rect.height);
            var cells = scroll.content.GetComponentsInChildren<CollectionCell>();
            Assert.That(cells.All(c => c.IsBound), Is.True, "活动节点不能出现未绑定的空 Cell。");
            Assert.That(cells.Select(c => c.ItemId).Distinct().Count(), Is.EqualTo(cells.Length));
            // 独立全量矩形相交扫描，避免用同一个 Query 实现同时生成预期与实际结果。
            foreach (var entry in view.Layout.Entries.Where(e => e.Rect.Overlaps(rect)))
            {
                var cell = cells.SingleOrDefault(c => c.ItemId == entry.Item.Id);
                Assert.That(cell, Is.Not.Null, "漏显条目：" + entry.Item.Id);
                var rt = (RectTransform)cell.transform;
                Assert.That(rt.anchoredPosition.y, Is.EqualTo(-entry.Rect.y).Within(.01f));
                Assert.That(rt.rect.size, Is.EqualTo(entry.Rect.size));
            }
        }

        [UnityTest]
        public IEnumerator RealGesturesWithSynchronousPreloadAppendNeverRequestTwice()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            var loader = root.AddComponent<CollectionLoader>();
            loader.Configure(prefab, prefab);
            loader.PreloadEnabled = true;
            int requests = 0, loadMore = 0, count = 30;
            loader.LoadMoreRequested += id => loadMore++;
            loader.PreloadRequested += id =>
            {
                requests++;
                view.ApplyChanges(new CollectionChangeSet().InsertItem("s", count++, Item("page" + requests)));
                Assert.That(loader.CompletePreload(id), Is.True);
            };
            yield return null;
            for (int gesture = 0; gesture < 12; gesture++)
            {
                scroll.content.anchoredPosition = new Vector2(0, view.Layout.ContentHeight - scroll.viewport.rect.height - 150);
                var pointer = Pointer();
                Begin(pointer);
                for (int sample = 0; sample < 20; sample++)
                {
                    pointer.delta = new Vector2(0, 60);
                    pointer.position += pointer.delta;
                    Drag(pointer);
                    Assert.That(requests, Is.EqualTo(gesture + 1));
                    yield return null;
                }

                End(pointer);
                Assert.That(loadMore, Is.Zero);
                Assert.That(loader.ActiveRequestId, Is.Zero);
            }

            loader.HasMore = false;
            var finalPointer = Pointer();
            Begin(finalPointer);
            finalPointer.delta = new Vector2(0, 1000);
            finalPointer.position += finalPointer.delta;
            Drag(finalPointer);
            End(finalPointer);
            Assert.That(requests, Is.EqualTo(12));
            Assert.That(loadMore, Is.Zero);
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

