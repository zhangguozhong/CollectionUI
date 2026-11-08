// 常用场景验收。检查标签按序换行、省略文本不越界、商品子元素矩形，以及模板复用和四种场景切换。
#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using CollectionUI.Samples;

namespace CollectionUI.Tests
{
    public class CommonScenarioTests
    {
        GameObject root;
        CollectionViewDemo demo;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CollectionUI.Samples.DemoAssetPaths.Asset("CollectionViewDemo.prefab")));
            root.GetComponent<CanvasScaler>().enabled = false;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            root.GetComponent<Canvas>().scaleFactor = 1;
            ((RectTransform)root.transform).sizeDelta = new Vector2(720, 1280);
            var safe = root.GetComponentInChildren<DemoSafeArea>();
            safe.enabled = false;
            var r = (RectTransform)safe.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            demo = root.GetComponent<CollectionViewDemo>();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TagsWrapInSourceOrderAndLongLabelFits()
        {
            demo.SetScenario(DemoScenario.Tags);
            demo.Load(1000);
            yield return null;
            var entries = demo.Collection.Layout.Entries;
            LayoutEntry previous = null;
            float rowY = -1, rowBottom = 0;
            foreach (var entry in entries)
            {
                if (entry.Kind != ElementKind.Item)
                {
                    previous = null;
                    rowY = -1;
                    continue;
                }

                Assert.That(entry.Rect.height, Is.EqualTo(96));
                if (previous != null)
                {
                    Assert.That(entry.ItemIndex, Is.EqualTo(previous.ItemIndex + 1));
                    if (entry.Rect.y == rowY)
                        Assert.That(entry.Rect.xMin - previous.Rect.xMax, Is.EqualTo(16).Within(.01));
                    else
                    {
                        Assert.That(entry.Rect.y - rowBottom, Is.EqualTo(20).Within(.01));
                        Assert.That(entry.Rect.x, Is.EqualTo(16));
                    }
                }

                rowY = entry.Rect.y;
                rowBottom = entry.Rect.yMax;
                previous = entry;
            }

            root.GetComponent<Canvas>().scaleFactor = 2f / 3f;
            yield return null;
            yield return null;
            demo.Collection.ScrollToItem("item-14");
            yield return null;
            Canvas.ForceUpdateCanvases();
            bool found = false;
            foreach (var cell in demo.Collection.ScrollRect.content.GetComponentsInChildren<TagCell>())
            {
                var label = cell.GetComponentInChildren<Text>();
                var rect = (RectTransform)label.transform;
                Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(rect.rect.width + 1));
                Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(rect.rect.height + 1));
                if (cell.ItemId == "item-14")
                {
                    found = true;
                    Assert.That(label.text, Does.EndWith("…"));
                }
            }

            Assert.That(found, Is.True);
        }

        [UnityTest]
        public IEnumerator ProductChildrenFitAfterNarrowingAndPoolReuse()
        {
            demo.SetScenario(DemoScenario.Products);
            demo.Load(1000);
            yield return null;
            foreach (int width in new[]
            {
                720,
                540,
                1080
            }

            )
            {
                root.GetComponent<Canvas>().scaleFactor = width / 1080f;
                ((RectTransform)root.transform).sizeDelta = new Vector2(width, 1920);
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return null;
                demo.Collection.ScrollToItem("item-120");
                yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var cell in demo.Collection.ScrollRect.content.GetComponentsInChildren<ProductCell>())
                {
                    var title = cell.transform.Find("Title").GetComponent<Text>();
                    var price = cell.transform.Find("Price").GetComponent<Text>();
                    var image = (RectTransform)cell.transform.Find("Artwork");
                    var tr = (RectTransform)title.transform;
                    var pr = (RectTransform)price.transform;
                    Assert.That(title.preferredHeight, Is.LessThanOrEqualTo(tr.rect.height + 1));
                    Assert.That(price.preferredHeight, Is.LessThanOrEqualTo(pr.rect.height + 1));
                    Assert.That(-tr.anchoredPosition.y - (-image.anchoredPosition.y + image.rect.height), Is.EqualTo(12).Within(.1));
                    Assert.That(-pr.anchoredPosition.y - (-tr.anchoredPosition.y + tr.rect.height), Is.EqualTo(12).Within(.1));
                    Assert.That(((RectTransform)cell.transform).rect.height - (-pr.anchoredPosition.y + pr.rect.height), Is.EqualTo(16).Within(.1));
                    Assert.That(title.resizeTextForBestFit, Is.False);
                }

                Assert.That(demo.Collection.ActiveCellCount, Is.LessThan(80));
            }
        }

        [UnityTest]
        public IEnumerator FooterCyclesAllFourScenariosWithWorkingMeasuredTemplates()
        {
            var button = root.transform.Find("SafeAreaContent/Footer/LayoutMode").GetComponent<Button>();
            foreach (var expected in new[]
            {
                DemoScenario.Products,
                DemoScenario.FreeRectangles,
                DemoScenario.Columns,
                DemoScenario.Tags
            }

            )
            {
                button.onClick.Invoke();
                yield return null;
                Assert.That(demo.Scenario, Is.EqualTo(expected));
                Assert.That(demo.Collection.ActiveCellCount, Is.GreaterThan(0));
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            yield return null;
        }
    }
}
#endif

