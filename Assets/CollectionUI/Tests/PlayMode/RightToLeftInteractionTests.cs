#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
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
    public class RtlInteractionCell : CollectionCell
    {
        public string DisplayedContent { get; private set; }

        protected override void OnBind(LayoutEntry entry) => DisplayedContent = (string)entry.Item.Data;
        protected override void OnUnbind() => DisplayedContent = null;
    }

    // 六种组合时序分别覆盖 Columns、Flow、Skyline，共 18 项正式回归。
    // 使用真实拖动事件，并以全量布局和可见矩形独立校验增量结果，不依赖 Query 生成预期。
    [TestFixture(LayoutMode.Columns)]
    [TestFixture(LayoutMode.Flow)]
    [TestFixture(LayoutMode.Skyline)]
    public class RightToLeftInteractionTests
    {
        readonly LayoutMode mode;
        GameObject root, template, events;
        CollectionView view;
        ScrollRect scroll;
        CollectionLoader loader;
        int refreshes, loads, preloads;
        public RightToLeftInteractionTests(LayoutMode mode) => this.mode = mode;
        [SetUp]
        public void Setup()
        {
            root = new GameObject("RTL interaction", typeof(RectTransform), typeof(ScrollRect));
            ((RectTransform)root.transform).sizeDelta = new Vector2(420, 500);
            scroll = root.GetComponent<ScrollRect>();
            scroll.movementType = ScrollRect.MovementType.Elastic;
            var viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform)).transform;
            viewport.SetParent(root.transform, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = Vector2.zero;
            var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
            content.SetParent(viewport, false);
            scroll.viewport = viewport;
            scroll.content = content;
            view = root.AddComponent<CollectionView>();
            template = new GameObject("Template", typeof(RectTransform), typeof(RtlInteractionCell));
            template.SetActive(false);
            view.RegisterTemplate("cell", template.GetComponent<CollectionCell>());
            events = new GameObject("Events", typeof(EventSystem));
            loader = root.AddComponent<CollectionLoader>();
            var indicator = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/LoadingIndicator.prefab").GetComponent<RectTransform>();
            loader.Configure(indicator, indicator);
            loader.PreloadEnabled = false;
            refreshes = loads = preloads = 0;
            loader.RefreshRequested += id => refreshes++;
            loader.LoadMoreRequested += id => loads++;
            loader.PreloadRequested += id => preloads++;
            view.ApplySnapshot(Data(120));
        }

        CollectionSnapshot Data(int count)
        {
            var data = new CollectionSnapshot();
            for (int s = 0; s < 2; s++)
            {
                var section = new CollectionSection
                {
                    Id = "s" + s,
                    Header = Item("h" + s, 100, 35),
                    Footer = Item("f" + s, 110, 25),
                    Layout = new SectionLayoutSettings
                    {
                        Mode = mode,
                        Direction = LayoutDirection.RightToLeft,
                        ColumnCount = 3,
                        Insets = new Insets(13, 7, 29, 11),
                        HorizontalSpacing = 9,
                        VerticalSpacing = 8,
                        SectionSpacing = 17
                    }
                };
                for (int i = s * count / 2; i < (s + 1) * count / 2; i++)
                    section.Items.Add(Item("i" + i, 80 + i % 3 * 13, i == 13 ? 1400 : 60 + i % 7 * 21));
                data.Sections.Add(section);
            }

            return data;
        }

        static CollectionItem Item(string id, float width = 90, float height = 100) => new CollectionItem
        {
            Id = id,
            ReuseIdentifier = "cell",
            Size = ItemSize.Fixed(width, height),
            Data = "content:" + id
        };
        float Offset => scroll.content.anchoredPosition.y;

        float Bottom => Mathf.Max(0, view.Layout.ContentHeight - scroll.viewport.rect.height);

        PointerEventData Begin()
        {
            var p = new PointerEventData(events.GetComponent<EventSystem>())
            {
                pointerId = 1,
                button = PointerEventData.InputButton.Left,
                position = new Vector2(100, 100)
            };
            ExecuteEvents.Execute(root, p, ExecuteEvents.beginDragHandler);
            return p;
        }

        void Drag(PointerEventData p, float dy)
        {
            p.delta = new Vector2(0, dy);
            p.position += p.delta;
            ExecuteEvents.Execute(root, p, ExecuteEvents.dragHandler);
        }

        void End(PointerEventData p) => ExecuteEvents.Execute(root, p, ExecuteEvents.endDragHandler);
        void SwitchDirection(bool firstSectionOnly = false)
        {
            float offset = Offset, height = view.Layout.ContentHeight;
            var velocity = scroll.velocity;
            var data = view.GetSnapshotCopy();
            foreach (var section in data.Sections)
            {
                section.Layout.Direction = section.Layout.Direction == LayoutDirection.RightToLeft ? LayoutDirection.LeftToRight : LayoutDirection.RightToLeft;
                if (firstSectionOnly)
                    break;
            }

            view.ApplySnapshot(data);
            Assert.That(Offset, Is.EqualTo(offset).Within(.02f));
            Assert.That(view.Layout.ContentHeight, Is.EqualTo(height).Within(.02f));
            Assert.AreEqual(velocity, scroll.velocity);
            AssertPresentation();
        }

        void AssertPresentation()
        {
            var entries = view.Layout.Entries.ToDictionary(e => e.Item.Id);
            var cells = scroll.content.GetComponentsInChildren<RtlInteractionCell>();
            Assert.AreEqual(cells.Length, cells.Select(c => c.ItemId).Distinct().Count(), "复用后不能显示重复条目");
            var byId = cells.ToDictionary(c => c.ItemId);
            foreach (var cell in cells)
            {
                Assert.IsTrue(cell.IsBound, "活动 Cell 不能未绑定");
                Assert.IsTrue(entries.TryGetValue(cell.ItemId, out var entry));
                Assert.AreEqual(entry.Item.Data, cell.DisplayedContent, "绑定内容不能残留为上一个条目");
                var rt = (RectTransform)cell.transform;
                Assert.That(rt.anchoredPosition.x, Is.EqualTo(entry.Rect.x).Within(.02f), cell.ItemId + " x");
                Assert.That(rt.anchoredPosition.y, Is.EqualTo(-entry.Rect.y).Within(.02f), cell.ItemId + " y");
                Assert.AreEqual(entry.Rect.size, rt.rect.size);
            }

            var visible = new Rect(0, Offset, scroll.viewport.rect.width, scroll.viewport.rect.height);
            foreach (var entry in view.Layout.Entries)
                if (entry.Rect.Overlaps(visible))
                    Assert.IsTrue(byId.ContainsKey(entry.Item.Id), "漏显：" + entry.Item.Id);
            Assert.Less(view.ActiveCellCount, 100);
        }

        void AssertMatchesFullLayout()
        {
            var full = SectionedLayout.Build(view.GetSnapshotCopy(), view.Layout.ContentWidth);
            Assert.AreEqual(full.ContentHeight, view.Layout.ContentHeight);
            for (int i = 0; i < full.Entries.Count; i++)
            {
                Assert.AreEqual(full.Entries[i].Key, view.Layout.Entries[i].Key);
                Assert.AreEqual(full.Entries[i].Rect, view.Layout.Entries[i].Rect);
            }
        }

        [UnityTest]
        public IEnumerator FastDragReversalsWithAppendAndDirectionChangesKeepCellsCorrect()
        {
            view.ApplySnapshot(Data(10000));
            view.ScrollToItem("i500");
            var p = Begin();
            int added = 0;
            for (int frame = 0; frame < 60; frame++)
            {
                Drag(p, frame % 20 < 10 ? 950 : -800);
                if (frame % 10 == 0)
                {
                    float before = Offset;
                    view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 5000 + added, Item("tail" + added++)));
                    Assert.AreEqual(mode != LayoutMode.Skyline, view.Layout.IsIncrementalAppend);
                    Assert.That(Offset, Is.EqualTo(before).Within(.02f));
                    SwitchDirection();
                    Drag(p, 0);
                    Assert.That(Offset, Is.EqualTo(before).Within(.05f), "同一指针位置不能回跳");
                }

                yield return null;
                AssertPresentation();
            }

            End(p);
            scroll.velocity = new Vector2(0, 50000);
            for (int frame = 0; frame < 20; frame++)
            {
                if (frame % 5 == 0)
                    SwitchDirection();
                yield return null;
                AssertPresentation();
            }

            AssertMatchesFullLayout();
        }

        [UnityTest]
        public IEnumerator MixedDeltaChangesDuringDragKeepAnchorAndReusedPositions()
        {
            AppendPage(60, 20, "warm-mixed");
            view.ScrollToItem("i35");
            var p = Begin();
            Drag(p, 12);
            for (int step = 0; step < 6; step++)
            {
                var visible = new Rect(0, Offset, 420, 500);
                var anchor = view.Layout.Entries.Where(e => e.Rect.Overlaps(visible)).OrderBy(e => Mathf.Abs(e.Rect.y - Offset)).First();
                float relative = anchor.Rect.y - Offset;
                var changes = new CollectionChangeSet().InsertItem("s0", 0, Item("new" + step, 84, 110 + step)).UpdateItem("i" + step, Item("i" + step, 94, 170 + step)).MoveItem("i" + (60 + step), "s0", 3).RemoveItem("i" + (10 + step));
                view.ApplyChanges(changes);
                Assert.IsTrue(view.Layout.TryGetKey(anchor.Key, out var moved));
                Assert.That(Offset, Is.EqualTo(Mathf.Clamp(moved.Rect.y - relative, 0, Bottom)).Within(.02f));
                AssertMatchesFullLayout();
                float committed = Offset;
                Drag(p, 10);
                Assert.That(Offset, Is.EqualTo(committed + 10).Within(.05f));
                SwitchDirection(true);
                yield return null;
                AssertPresentation();
            }

            End(p);
        }

        [UnityTest]
        public IEnumerator GroupInsertionMoveRemovalAndVisibleUpdateKeepGeometryConsistent()
        {
            view.ScrollToItem("i90");
            var visible = new Rect(0, Offset, 420, 500);
            var anchor = view.Layout.Entries.Where(e => e.Kind == ElementKind.Item && e.Rect.Overlaps(visible)).OrderBy(e => Mathf.Abs(e.Rect.y - Offset)).First();
            float relative = anchor.Rect.y - Offset;
            var extra = new CollectionSection
            {
                Id = "extra",
                Header = Item("extra-header", 110, 25),
                Footer = Item("extra-footer", 120, 30),
                Layout = new SectionLayoutSettings
                {
                    Mode = mode,
                    Direction = LayoutDirection.LeftToRight,
                    ColumnCount = 3,
                    HorizontalSpacing = 7,
                    VerticalSpacing = 9
                }
            };
            for (int i = 0; i < 9; i++)
                extra.Items.Add(Item("extra" + i));
            view.ApplyChanges(new CollectionChangeSet().InsertSection(0, extra).MoveSection("s0", 2));
            Assert.IsTrue(view.Layout.TryGetKey(anchor.Key, out var moved));
            Assert.That(Offset, Is.EqualTo(moved.Rect.y - relative).Within(.02f));
            AssertMatchesFullLayout();
            AssertPresentation();
            var replacement = Item(anchor.Item.Id, 100, 240);
            replacement.Data = "updated:" + anchor.Item.Id;
            view.ApplyChanges(new CollectionChangeSet().RemoveSection("extra").UpdateItem(anchor.Item.Id, replacement));
            AssertMatchesFullLayout();
            AssertPresentation();
            SwitchDirection(true);
            yield return null;
            AssertPresentation();
        }

        [UnityTest]
        public IEnumerator RefreshAndBottomLoadRemainContinuousAcrossDirectionChanges()
        {
            var p = Begin();
            Drag(p, -600);
            End(p);
            Assert.AreEqual(1, refreshes);
            long refresh = loader.ActiveRequestId;
            Assert.Greater(refresh, 0);
            yield return new WaitForSecondsRealtime(1);
            SwitchDirection();
            float held = Offset;
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 60, Item("refresh-tail")));
            Assert.IsTrue(loader.CompleteRefresh(refresh));
            Assert.IsFalse(loader.CompleteRefresh(refresh));
            Assert.That(Offset, Is.EqualTo(held).Within(.02f));
            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
                AssertPresentation();
            }

            view.ScrollToItem("refresh-tail", 1);
            p = Begin();
            Drag(p, 600);
            End(p);
            Assert.AreEqual(1, loads);
            long load = loader.ActiveRequestId;
            yield return new WaitForSecondsRealtime(1);
            SwitchDirection();
            p = Begin();
            Drag(p, 0);
            held = Offset;
            var changes = new CollectionChangeSet();
            for (int i = 0; i < 12; i++)
                changes.InsertItem("s1", 61 + i, Item("load-tail" + i));
            view.ApplyChanges(changes);
            Assert.That(Offset, Is.EqualTo(held).Within(.05f));
            Assert.IsTrue(loader.CompleteLoadMore(load, false));
            Assert.IsFalse(loader.CompleteLoadMore(load));
            float committed = Offset;
            Drag(p, 0);
            Assert.That(Offset, Is.EqualTo(committed).Within(.05f));
            Drag(p, -20);
            Assert.That(Offset, Is.EqualTo(committed - 20).Within(.05f));
            End(p);
            Assert.AreEqual(0, preloads);
            Assert.IsFalse(loader.HasMore);
            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
                AssertPresentation();
            }
        }

        [UnityTest]
        public IEnumerator PreloadAndDirectionSwitchStayMutuallyExclusiveWithBottomLoading()
        {
            loader.PreloadEnabled = true;
            view.ScrollToItem("i119", 1);
            var p = Begin();
            Drag(p, -20);
            Assert.AreEqual(0, preloads, "向下拖动不能预加载");
            Drag(p, 30);
            Assert.AreEqual(1, preloads);
            long request = loader.ActiveRequestId;
            SwitchDirection();
            Drag(p, 600);
            Assert.AreEqual(1, preloads);
            Assert.AreEqual(0, loader.BeginLoadMore());
            view.ApplyChanges(new CollectionChangeSet().InsertItem("s1", 60, Item("preloaded")));
            Assert.IsTrue(loader.CompletePreload(request));
            SwitchDirection();
            Drag(p, 600);
            End(p);
            Assert.AreEqual(1, preloads);
            Assert.AreEqual(0, loads);
            scroll.StopMovement();
            view.ScrollToItem("preloaded", 1);
            p = Begin();
            Drag(p, 10);
            Assert.AreEqual(2, preloads);
            long current = loader.ActiveRequestId;
            Assert.IsFalse(loader.CompletePreload(request, false));
            Assert.AreEqual(current, loader.ActiveRequestId);
            Assert.IsTrue(loader.CompletePreload(current, false));
            SwitchDirection();
            Drag(p, 600);
            End(p);
            Assert.AreEqual(2, preloads);
            Assert.AreEqual(0, loads);
            Assert.IsFalse(loader.HasMore);
            yield return null;
            AssertPresentation();
        }

        // 普通分页必须命中增量路径；Skyline 使用通用路径。两者都应保留锚点、惯性和已有绑定。
        void AppendPage(int start, int count, string prefix)
        {
            var cells = scroll.content.GetComponentsInChildren<RtlInteractionCell>();
            var versions = cells.ToDictionary(c => c.ItemId, c => c.BindingVersion);
            var changes = new CollectionChangeSet();
            for (int i = 0; i < count; i++)
                changes.InsertItem("s1", start + i, Item(prefix + i));
            float offset = Offset;
            var velocity = scroll.velocity;
            view.ApplyChanges(changes);
            Assert.AreEqual(mode != LayoutMode.Skyline, view.Layout.IsIncrementalAppend);
            Assert.That(Offset, Is.EqualTo(offset).Within(.02f));
            Assert.AreEqual(velocity, scroll.velocity);
            foreach (var c in cells)
                if (c != null && c.IsBound && versions.TryGetValue(c.ItemId, out var version))
                    Assert.AreEqual(version, c.BindingVersion);
            AssertMatchesFullLayout();
            AssertPresentation();
        }

        [UnityTest]
        public IEnumerator RepeatedPagesRefreshReplacementMixedChangesAndReloadKeepStateConsistent()
        {
            // 先建立多页增量索引，再用刷新替换数据，验证旧索引和请求状态不会泄漏。
            for (int i = 0; i < 10; i++)
                AppendPage(60 + i * 20, 20, "warm" + i + "-");
            view.ScrollToItem("i0");
            var p = Begin();
            Drag(p, -600);
            End(p);
            Assert.AreEqual(1, refreshes);
            long refresh = loader.ActiveRequestId;
            yield return new WaitForSecondsRealtime(.4f);
            float held = Offset;
            view.ApplySnapshot(Data(120));
            Assert.IsFalse(view.Layout.IsIncrementalAppend);
            Assert.That(Offset, Is.EqualTo(held).Within(.02f));
            SwitchDirection();
            Assert.AreEqual(refresh, loader.ActiveRequestId);
            Assert.AreEqual(0, loader.BeginLoadMore());
            Assert.IsTrue(loader.CompleteRefresh(refresh));
            Assert.IsFalse(loader.CompleteRefresh(refresh));
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                AssertPresentation();
            }

            // 刷新后继续分页，穿插更新、移动、删除，再切换方向恢复分页。
            AppendPage(60, 50, "after-refresh");
            view.ApplyChanges(new CollectionChangeSet().UpdateItem("i70", Item("i70", 100, 180)).MoveItem("i65", "s1", 5).RemoveItem("after-refresh0"));
            Assert.IsFalse(view.Layout.IsIncrementalAppend);
            AssertMatchesFullLayout();
            AssertPresentation();
            SwitchDirection();
            AppendPage(109, 20, "after-mixed");
            view.ScrollToItem("after-mixed19", 1);
            p = Begin();
            Drag(p, 600);
            End(p);
            Assert.AreEqual(1, loads);
            long load = loader.ActiveRequestId;
            yield return new WaitForSecondsRealtime(.4f);
            SwitchDirection();
            p = Begin();
            Drag(p, 0);
            // 加载期间重新抓住列表，数据到达和完成请求都不能破坏手势基准。
            AppendPage(129, 50, "load");
            Assert.AreEqual(load, loader.ActiveRequestId);
            Assert.AreEqual(0, loader.BeginLoadMore());
            Assert.IsFalse(loader.CompleteRefresh(refresh));
            Assert.IsTrue(loader.CompleteLoadMore(load, false));
            float offset = Offset;
            Drag(p, 0);
            Assert.That(Offset, Is.EqualTo(offset).Within(.05f));
            End(p);
            // 没有更多数据后，新手势也不能再触发预加载或上拉加载。
            loader.PreloadEnabled = true;
            view.ScrollToItem("load49", 1);
            p = Begin();
            Drag(p, 600);
            End(p);
            Assert.AreEqual(1, loads);
            Assert.AreEqual(0, preloads);
            Assert.IsFalse(loader.HasMore);
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                AssertPresentation();
            }
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

