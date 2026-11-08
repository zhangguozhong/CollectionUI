// 普通节点演示验收。覆盖按钮操作、文本容纳、最窄/最长画面、安全区和 Canvas 缩放；同时检查所有 Cell 完整生成。
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
    public class ExpandedDemoTests
    {
        GameObject root;
        ExpandedCollectionDemo demo;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/ExpandedCollectionDemo.prefab"));
            root.GetComponent<CanvasScaler>().enabled = false;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            root.GetComponent<Canvas>().scaleFactor = 1;
            root.GetComponentInChildren<DemoSafeArea>().enabled = false;
            demo = root.GetComponent<ExpandedCollectionDemo>();
            ((RectTransform)root.transform).sizeDelta = new Vector2(720, 1280);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OrdinaryDemoFitsWidthsSafeAreaAndLongestText()
        {
            foreach (var size in new[]
            {
                new Vector2(720, 1280),
                new Vector2(1080, 1920),
                new Vector2(1080, 2400)
            }

            )
            {
                ((RectTransform)root.transform).sizeDelta = size;
                var safe = (RectTransform)root.transform.Find("SafeAreaContent");
                safe.anchorMin = new Vector2(0, size.x == 1080 ? 80 / size.y : 0);
                safe.anchorMax = new Vector2(1, size.x == 1080 ? 1 - 120 / size.y : 1);
                safe.offsetMin = safe.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                demo.Load(8);
                yield return null;
                yield return null;
                demo.Arrange();
                Canvas.ForceUpdateCanvases();
                Assert.That(root.GetComponentsInChildren<ScrollRect>(), Is.Empty);
                Assert.That(demo.Flow.ActiveCellCount, Is.EqualTo(demo.Flow.Layout.Entries.Count));
                Assert.That(demo.Columns.ActiveCellCount, Is.EqualTo(2));
                Assert.That(((RectTransform)demo.Columns.transform).rect.height, Is.EqualTo(184));
                var middle = (RectTransform)safe.Find("Middle");
                var corners = new Vector3[4];
                foreach (var view in new[]
                {
                    demo.Flow,
                    demo.Columns
                }

                )
                {
                    var r = (RectTransform)view.transform;
                    r.GetWorldCorners(corners);
                    foreach (var point in corners)
                        Assert.That(Inside(middle, point), Is.True, view.name + " must fit Middle at " + size);
                }

                foreach (var label in root.GetComponentsInChildren<Text>())
                {
                    var r = (RectTransform)label.transform;
                    Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(r.rect.height + 1), label.name);
                }

                bool found = false;
                foreach (var cell in demo.Flow.GetComponentsInChildren<TagCell>())
                {
                    var label = cell.GetComponentInChildren<Text>();
                    Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(((RectTransform)label.transform).rect.width + 1));
                    if (cell.ItemId == "tag-7")
                    {
                        found = true;
                        Assert.That(label.text, Does.EndWith("…"));
                    }
                }

                Assert.That(found, Is.True);
                foreach (var button in root.GetComponentsInChildren<Button>())
                {
                    var r = (RectTransform)button.transform;
                    Assert.That(r.rect.width, Is.GreaterThanOrEqualTo(96));
                    Assert.That(r.rect.height, Is.GreaterThanOrEqualTo(96));
                    r.GetWorldCorners(corners);
                    foreach (var point in corners)
                        Assert.That(Inside(safe, point), Is.True);
                }

                var first = (RectTransform)demo.Flow.transform;
                var second = (RectTransform)safe.Find("Middle/ColumnsTitle");
                Assert.That(-second.anchoredPosition.y - (-first.anchoredPosition.y + first.rect.height), Is.EqualTo(24).Within(.01f));
            }
        }

        [UnityTest]
        public IEnumerator ControlsChangeDataAndHeightWithoutScrolling()
        {
            var safe = root.transform.Find("SafeAreaContent");
            var change = safe.Find("Header/Controls/ChangeCount").GetComponent<Button>();
            var height = safe.Find("Header/Controls/AutoHeight").GetComponent<Button>();
            float before = ((RectTransform)demo.Flow.transform).rect.height;
            height.onClick.Invoke();
            Assert.That(demo.Flow.AutoHeight, Is.False);
            change.onClick.Invoke();
            yield return null;
            Assert.That(demo.Count, Is.EqualTo(4));
            Assert.That(((RectTransform)demo.Flow.transform).rect.height, Is.EqualTo(before));
            Assert.That(demo.Flow.Layout.ContentHeight, Is.LessThan(before));
            Assert.That(demo.Flow.ActiveCellCount, Is.EqualTo(6));
            height.onClick.Invoke();
            yield return null;
            Assert.That(demo.Flow.AutoHeight, Is.True);
            Assert.That(((RectTransform)demo.Flow.transform).rect.height, Is.EqualTo(demo.Flow.Layout.ContentHeight));
            change.onClick.Invoke();
            yield return null;
            Assert.That(demo.Count, Is.EqualTo(8));
            Assert.That(demo.Flow.ActiveCellCount, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator ScaledCanvasRemeasuresAtProductionReferenceWidth()
        {
            // 屏幕 720×1280、参考 1080×1920 时，CanvasScaler 对应下列逻辑尺寸及 2/3 缩放。
            ((RectTransform)root.transform).sizeDelta = new Vector2(1080, 1920);
            root.GetComponent<Canvas>().scaleFactor = 2f / 3f;
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(demo.Flow.AutoHeight, Is.True);
            Assert.That(((RectTransform)demo.Flow.transform).rect.height, Is.EqualTo(demo.Flow.Layout.ContentHeight));
            foreach (var cell in demo.Flow.GetComponentsInChildren<TagCell>())
            {
                var label = cell.GetComponentInChildren<Text>();
                var rect = (RectTransform)label.transform;
                Assert.That(label.pixelsPerUnit, Is.EqualTo(2f / 3f).Within(.001));
                Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(rect.rect.width + 1));
                Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(rect.rect.height + 1));
            }
        }

        [UnityTest]
        public IEnumerator ItemSizeChangeNotifiesAndRepositionsFollowingPanel()
        {
            Assert.That(demo.Flow.Layout.TryGetItem("tag-7", out var item), Is.True);
            var title = (RectTransform)root.transform.Find("SafeAreaContent/Middle/ColumnsTitle");
            float previousTop = -title.anchoredPosition.y;
            int notifications = 0;
            demo.Flow.ContentSizeChanged += size => notifications++;
            ((TagData)item.Item.Data).Text = "Short";
            demo.Flow.InvalidateItemSize("tag-7");
            yield return null;
            yield return null;
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(-title.anchoredPosition.y, Is.LessThan(previousTop));
            var first = (RectTransform)demo.Flow.transform;
            Assert.That(-title.anchoredPosition.y - (-first.anchoredPosition.y + first.rect.height), Is.EqualTo(24).Within(.01));
        }

        static bool Inside(RectTransform parent, Vector3 point)
        {
            var rect = parent.rect;
            rect.xMin -= .1f;
            rect.yMin -= .1f;
            rect.xMax += .1f;
            rect.yMax += .1f;
            return rect.Contains(parent.InverseTransformPoint(point));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Object.Destroy(root);
            yield return null;
        }
    }
}
#endif

