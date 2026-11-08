// 演示性能回归。使用真实演示和千/万条数据记录布局及刷新耗时，同时检查活跃实例数量；结果不是整帧或真机性能。
#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using CollectionUI.Samples;

namespace CollectionUI.Tests
{
    public class DemoPerformanceTests
    {
        // 通过真实按钮切换，检查四种场景和两档数据量的方向、顺序、尺寸缓存及滚动锚点。
        [UnityTest]
        public IEnumerator DirectionButtonPreservesDataMeasurementsAndScrollAcrossScenarios()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/CollectionViewDemo.prefab"));
            try
            {
                yield return null;
                var demo = root.GetComponent<CollectionViewDemo>();
                var buttonTransform = root.transform.Find("SafeAreaContent/Header/Controls/LayoutDirection");
                Assert.That(buttonTransform, Is.Not.Null, "主演示需要方向切换按钮。");
                var button = buttonTransform.GetComponent<UnityEngine.UI.Button>();
                foreach (DemoScenario scenario in System.Enum.GetValues(typeof(DemoScenario)))
                    foreach (int count in new[]
                    {
                        1000,
                        10000
                    }

                    )
                    {
                        demo.SetScenario(scenario);
                        demo.Load(count);
                        var view = demo.Collection;
                        var first = view.GetSnapshotCopy().Sections[0].Items[0];
                        if (first.Data is TagData tag)
                            Assert.That(tag.Text, Does.StartWith("#0 "));
                        if (first.Data is ProductData product)
                            Assert.That(product.ImageLabel, Does.StartWith("#0 "));
                        view.ScrollToItem("item-25");
                        yield return null;
                        view.ScrollRect.StopMovement();
                        var entries = view.Layout.Entries.ToArray();
                        float offset = view.ScrollRect.content.anchoredPosition.y;
                        float height = view.Layout.ContentHeight;
                        int measurements = 0;
                        var measure = view.MeasureItem;
                        view.MeasureItem = (item, width) =>
                        {
                            measurements++;
                            return measure(item, width);
                        };
                        button.onClick.Invoke();
                        Assert.That(view.GetSnapshotCopy().Sections.All(section => section.Layout.Direction == LayoutDirection.RightToLeft), Is.True);
                        Assert.That(button.GetComponentInChildren<UnityEngine.UI.Text>().text, Does.Contain("RTL"));
                        Assert.That(view.Layout.ContentHeight, Is.EqualTo(height).Within(.01f));
                        Assert.That(view.ScrollRect.content.anchoredPosition.y, Is.EqualTo(offset).Within(.01f));
                        Assert.That(measurements, Is.Zero, "只切换方向不应重新测量内容。");
                        Assert.That(view.Layout.Entries.Count, Is.EqualTo(entries.Length));
                        for (int i = 0; i < entries.Length; i++)
                        {
                            var actual = view.Layout.Entries[i];
                            Assert.That(actual.Item.Id, Is.EqualTo(entries[i].Item.Id));
                            Assert.That(actual.Item.Data, Is.SameAs(entries[i].Item.Data));
                            Assert.That(actual.Rect.size, Is.EqualTo(entries[i].Rect.size));
                            Assert.That(actual.Rect.y, Is.EqualTo(entries[i].Rect.y).Within(.01f));
                            Assert.That(actual.Rect.x, Is.EqualTo(view.ScrollRect.viewport.rect.width - entries[i].Rect.xMax).Within(.01f));
                        }

                        view.ScrollToItem("item-" + (count - 1), 1);
                        yield return null;
                        Assert.That(view.GetVisibleItems(), Does.Contain("item-" + (count - 1)));
                        demo.Load(count);
                        Assert.That(view.GetSnapshotCopy().Sections.All(section => section.Layout.Direction == LayoutDirection.RightToLeft), Is.True, "更换数据量应保留方向。");
                        demo.SetScenario(scenario);
                        Assert.That(view.GetSnapshotCopy().Sections.All(section => section.Layout.Direction == LayoutDirection.RightToLeft), Is.True, "更换场景应保留方向。");
                        button.onClick.Invoke();
                        Assert.That(view.GetSnapshotCopy().Sections.All(section => section.Layout.Direction == LayoutDirection.LeftToRight), Is.True);
                    }
            }
            finally
            {
                Object.Destroy(root);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RealDemoScrollsAllScenariosAtOneAndTenThousandItems()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/CollectionViewDemo.prefab"));
            var cameraObject = new GameObject("DemoBenchmarkCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            var target = new RenderTexture(1080, 1920, 24);
            target.Create();
            camera.targetTexture = target;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            canvas.scaleFactor = 1;
            root.GetComponent<UnityEngine.UI.CanvasScaler>().enabled = false;
            var safe = root.GetComponentInChildren<DemoSafeArea>();
            safe.enabled = false;
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            try
            {
                yield return null;
                yield return null;
                var demo = root.GetComponent<CollectionViewDemo>();
                var view = demo.Collection;
                Assert.That(view.ScrollRect.viewport.rect.height, Is.EqualTo(1420).Within(1));
                foreach (var scenario in new[]
                {
                    DemoScenario.Columns,
                    DemoScenario.FreeRectangles,
                    DemoScenario.Tags,
                    DemoScenario.Products
                }

                )
                    foreach (int count in new[]
                    {
                        1000,
                        10000
                    }

                    )
                    {
                        demo.SetScenario(scenario);
                        view.InvalidateStyle();
                        demo.Load(count);
                        double layoutMs = view.LastLayoutMilliseconds;
                        var scroll = view.ScrollRect;
                        // 先滚动多个屏幕预热文字测量、对象池和索引，避免把首次创建成本混入稳定滚动数据。
                        for (int f = 0; f < 30; f++)
                        {
                            scroll.content.anchoredPosition = new Vector2(0, f * 50);
                            yield return null;
                        }

                        int builds = view.LayoutBuildCount, maxActive = 0;
                        var timings = new double[120];
                        for (int f = 0; f < 120; f++)
                        {
                            scroll.content.anchoredPosition = new Vector2(0, 1500 + f * 25);
                            view.RefreshVisible();
                            timings[f] = view.LastRefreshMilliseconds;
                            maxActive = Mathf.Max(maxActive, view.ActiveCellCount);
                            yield return null;
                        }

                        System.Array.Sort(timings);
                        double sum = 0;
                        foreach (double ms in timings)
                            sum += ms;
                        Debug.Log("COLLECTION_UI_DEMO_BENCHMARK viewport=" + scroll.viewport.rect.size + " scenario=" + scenario + " items=" + count + " layoutMs=" + layoutMs.ToString("F3") + " refreshMeanMs=" + (sum / timings.Length).ToString("F4") + " refreshP95Ms=" + timings[113].ToString("F4") + " activeMax=" + maxActive + " pooled=" + view.PooledCellCount + " createdCumulative=" + view.CreatedCellCount);
                        Assert.That(view.LayoutBuildCount, Is.EqualTo(builds));
                        Assert.That(maxActive, Is.LessThan(180));
                        if (count == 10000)
                        {
                            // 四类真实演示按大跨度往返，检查最终渲染帧前已绑定全部可见内容。
                            float bottom = Mathf.Max(0, view.Layout.ContentHeight - scroll.viewport.rect.height);
                            for (int step = 0; step < 24; step++)
                            {
                                float fraction = step < 12 ? step / 11f : (23 - step) / 11f;
                                scroll.StopMovement();
                                scroll.content.anchoredPosition = new Vector2(0, bottom * fraction);
                                yield return null;
                                var viewportRect = new Rect(0, scroll.content.anchoredPosition.y, scroll.viewport.rect.width, scroll.viewport.rect.height);
                                var cells = scroll.content.GetComponentsInChildren<CollectionCell>();
                                Assert.That(cells.All(c => c.IsBound), Is.True, scenario + " 出现未绑定 Cell");
                                foreach (var entry in view.Layout.Entries.Where(e => e.Kind == ElementKind.Item && e.Rect.Overlaps(viewportRect)))
                                {
                                    var cell = cells.SingleOrDefault(c => c.ItemId == entry.Item.Id);
                                    Assert.That(cell, Is.Not.Null, scenario + " 漏显 " + entry.Item.Id);
                                    Assert.That(cell.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => !string.IsNullOrEmpty(t.text)), Is.True, "绑定后文字为空");
                                }
                            }

                            Assert.That(view.LayoutBuildCount, Is.EqualTo(builds), "快速滚动不应重建布局。");
                        }

                        view.ScrollToItem("item-" + (count - 1), 1);
                        yield return null;
                        Assert.That(view.GetVisibleItems(), Does.Contain("item-" + (count - 1)));
                    }
            }
            finally
            {
                Object.Destroy(root);
                camera.targetTexture = null;
                Object.Destroy(cameraObject);
                target.Release();
                Object.Destroy(target);
            }

            yield return null;
        }
    }
}
#endif

