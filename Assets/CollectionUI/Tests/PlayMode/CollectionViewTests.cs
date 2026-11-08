// 滚动视图集成测试。构造真实 ScrollRect/Viewport/Content，验证万条虚拟化、锚点保持、模板复用、尺寸缓存及回调边界。
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    public class VisibilityReadingCell : CollectionCell
    {
        public CollectionView Owner;
        protected override void OnBind(LayoutEntry entry)
        {
            Owner.GetVisibleItems();
        }
    }

    public class MeasuredCustomLayout : ICollectionLayout
    {
        public LayoutResult Build(CollectionSnapshot data, float width, System.Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, System.Collections.Generic.ISet<string> dirtySections = null)
        {
            var section = data.Sections[0];
            var item = section.Items[0];
            var size = measure(item, width);
            var entry = new LayoutEntry(item, section.Id, 0, 0, ElementKind.Item, new Rect(0, 0, size.x, size.y));
            return LayoutResult.Create(new[] { entry }, width, size.y);
        }
    }

    public class CollectionViewTests
    {
        GameObject root, template;
        CollectionView view;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            root = new GameObject("Canvas", typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var region = new GameObject("View", typeof(RectTransform), typeof(ScrollRect));
            region.transform.SetParent(root.transform, false);
            var rt = (RectTransform)region.transform;
            rt.sizeDelta = new Vector2(600, 800);
            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            vp.transform.SetParent(rt, false);
            Stretch((RectTransform)vp.transform);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(vp.transform, false);
            var scroll = region.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)vp.transform;
            scroll.content = (RectTransform)content.transform;
            view = region.AddComponent<CollectionView>();
            template = new GameObject("Template", typeof(RectTransform), typeof(CollectionCell));
            template.SetActive(false);
            view.RegisterTemplate("card", template.GetComponent<CollectionCell>());
            yield return null;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.sizeDelta = Vector2.zero;
        }

        static CollectionSnapshot Data(int count)
        {
            var snapshot = new CollectionSnapshot();
            var section = new CollectionSection
            {
                Id = "section"
            };
            section.Layout.Mode = LayoutMode.Columns;
            section.Layout.ColumnCount = 3;
            for (int i = 0; i < count; i++)
                section.Items.Add(new CollectionItem { Id = "item" + i, ReuseIdentifier = "card", Size = ItemSize.Fixed(0, 100 + i % 4 * 25), Data = i });
            snapshot.Sections.Add(section);
            return snapshot;
        }

        [UnityTest]
        public IEnumerator TenThousandItemsRemainVirtualAndScrollToEnd()
        {
            view.ApplySnapshot(Data(10000));
            yield return null;
            Assert.That(view.ActiveCellCount, Is.InRange(1, 100));
            view.ScrollToItem("item9999");
            yield return null;
            Assert.That(view.GetVisibleItems(), Does.Contain("item9999"));
            Assert.That(view.ActiveCellCount, Is.LessThan(100));
            Assert.That(view.CreatedCellCount, Is.LessThan(160));
        }

        [UnityTest]
        public IEnumerator InvalidSnapshotDoesNotReplaceWorkingData()
        {
            view.ApplySnapshot(Data(20));
            yield return null;
            var broken = Data(2);
            broken.Sections[0].Items[1].Id = "item0";
            Assert.Throws<System.ArgumentException>(() => view.ApplySnapshot(broken));
            Assert.That(view.Layout.Entries.Count, Is.EqualTo(20));
        }

        [UnityTest]
        public IEnumerator SnapshotIsCopiedAndRemovalReleasesCells()
        {
            var data = Data(30);
            view.ApplySnapshot(data);
            data.Sections[0].Items.Clear();
            yield return null;
            Assert.That(view.Layout.Entries.Count, Is.EqualTo(30));
            view.ApplySnapshot(new CollectionSnapshot());
            yield return null;
            Assert.That(view.ActiveCellCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ResizeRebuildsAndPreservesAnchor()
        {
            view.ApplySnapshot(Data(1000));
            yield return null;
            view.ScrollToItem("item500");
            yield return null;
            ((RectTransform)view.transform).sizeDelta = new Vector2(420, 800);
            yield return null;
            yield return null;
            Assert.That(view.GetVisibleItems(), Does.Contain("item500"));
            Assert.That(view.Layout.Entries[0].Rect.width, Is.LessThan(150));
        }

        [UnityTest]
        public IEnumerator MeasuredTemplateChangeDoesNotReuseOldHeight()
        {
            view.RegisterTemplate("other", template.GetComponent<CollectionCell>());
            view.MeasureItem = (item, width) => new Vector2(width, item.ReuseIdentifier == "card" ? 120 : 240);
            var data = Data(1);
            data.Sections[0].Items[0].Size = ItemSize.Measured();
            view.ApplySnapshot(data);
            yield return null;
            // 先填充提交后的尺寸缓存，再保持 ID/版本不变更换模板，检查是否错误复用旧尺寸。
            view.InvalidateItemSize("item0");
            yield return null;
            data.Sections[0].Items[0].ReuseIdentifier = "other";
            view.ApplySnapshot(data);
            yield return null;
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(240));
        }

        [UnityTest]
        public IEnumerator WarmScrollingDoesNotRebuildOrAllocateCoreBuffers()
        {
            view.ApplySnapshot(Data(10000));
            yield return null;
            // 先经过代表性的滚动范围，预热对象池和查询缓冲区。
            var scroll = view.ScrollRect;
            for (int i = 0; i < 150; i++)
            {
                scroll.content.anchoredPosition = new Vector2(0, i * 31);
                view.RefreshVisible();
            }

            int builds = view.LayoutBuildCount, maxActive = 0;
            long probeBefore = System.GC.GetAllocatedBytesForCurrentThread();
            var allocationProbe = new byte[4096];
            bool allocationCounterSupported = System.GC.GetAllocatedBytesForCurrentThread() > probeBefore;
            System.GC.KeepAlive(allocationProbe);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                scroll.content.anchoredPosition = new Vector2(0, 1000 + (i % 150) * 31);
                view.RefreshVisible();
                maxActive = Mathf.Max(maxActive, view.ActiveCellCount);
            }

            watch.Stop();
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Debug.Log("COLLECTION_UI_CORE_BENCHMARK 10000 items / 1000 refreshes: " + watch.Elapsed.TotalMilliseconds.ToString("F3") + " ms; allocated=" + (allocationCounterSupported ? bytes.ToString() : "unsupported") + "; activeMax=" + maxActive + "; created=" + view.CreatedCellCount);
            Assert.That(view.LayoutBuildCount, Is.EqualTo(builds));
            Assert.That(maxActive, Is.LessThan(100));
            // Stopwatch 自身可能产生少量分配；循环中的每次刷新不应持续分配临时对象。
            if (allocationCounterSupported)
                Assert.That(bytes, Is.LessThan(1024));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PendingStyleInvalidationSurvivesSnapshotSubmission()
        {
            float height = 100;
            view.MeasureItem = (item, width) => new Vector2(width, height);
            var data = Data(1);
            data.Sections[0].Items[0].Size = ItemSize.Measured();
            view.ApplySnapshot(data);
            height = 250;
            view.InvalidateStyle();
            view.ApplySnapshot(data);
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(250));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BinderCanReadVisibilityWithoutChangingOverscanIteration()
        {
            var reader = template.AddComponent<VisibilityReadingCell>();
            reader.Owner = view;
            view.RegisterTemplate("reader", reader);
            var data = Data(100);
            foreach (var item in data.Sections[0].Items)
                item.ReuseIdentifier = "reader";
            view.ApplySnapshot(data);
            var expected = new System.Collections.Generic.List<int>();
            view.Layout.Query(new Rect(0, -view.Overscan, 600, 800 + 2 * view.Overscan), expected);
            Assert.That(view.ActiveCellCount, Is.EqualTo(expected.Count));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecycledBindingCancelsAsyncWorkAndInvalidatesItsToken()
        {
            view.ApplySnapshot(Data(1000));
            yield return null;
            var cell = view.ScrollRect.content.GetComponentInChildren<CollectionCell>();
            string id = cell.ItemId;
            int version = cell.BindingVersion;
            var token = cell.BindingCancellation;
            Assert.That(cell.IsCurrent(id, version), Is.True);
            view.ScrollToItem("item999");
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(cell.IsCurrent(id, version), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CustomLayoutCanUseProvidedMeasurementDelegate()
        {
            view.LayoutEngine = new MeasuredCustomLayout();
            view.MeasureItem = (item, width) => new Vector2(width, 123);
            var data = Data(1);
            data.Sections[0].Items[0].Size = ItemSize.Measured();
            view.ApplySnapshot(data);
            Assert.That(view.Layout.Entries[0].Rect.height, Is.EqualTo(123));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MeasurementCannotReenterSnapshotDuringInvalidationRebuild()
        {
            bool attempt = false, rejected = false;
            var data = Data(1);
            data.Sections[0].Items[0].Size = ItemSize.Measured();
            view.MeasureItem = (item, width) =>
            {
                if (attempt)
                {
                    attempt = false;
                    try
                    {
                        view.ApplySnapshot(data);
                    }
                    catch (System.InvalidOperationException)
                    {
                        rejected = true;
                    }
                }

                return new Vector2(width, 100);
            };
            view.ApplySnapshot(data);
            attempt = true;
            view.InvalidateStyle();
            yield return null;
            Assert.That(rejected, Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            Object.Destroy(template);
            yield return null;
        }
    }
}
