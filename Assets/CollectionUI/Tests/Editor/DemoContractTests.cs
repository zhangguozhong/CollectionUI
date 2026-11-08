// 滚动演示资源契约：检查 Seeder 重复执行不改文件、资源独立性、安全区内的区域布局和按钮最小热区。
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CollectionUI.Editor;
using CollectionUI.Samples;

namespace CollectionUI.Tests
{
    public class DemoContractTests
    {
        [Test]
        public void SeededSceneAndPrefabAreStandaloneAndIdempotent()
        {
            Assert.That(File.Exists(CollectionViewDemoSeeder.ScenePath));
            var paths = Directory.GetFiles(CollectionViewDemoSeeder.Folder);
            var hashes = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var p in paths)
                hashes[p] = System.Convert.ToBase64String(File.ReadAllBytes(p));
            CollectionViewDemoSeeder.Generate();
            foreach (var p in paths)
                Assert.That(System.Convert.ToBase64String(File.ReadAllBytes(p)), Is.EqualTo(hashes[p]), p);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionViewDemoSeeder.PrefabPath);
            Assert.That(prefab.GetComponentsInChildren<DemoSafeArea>(true).Length, Is.EqualTo(1));
            var safe = prefab.transform.Find("SafeAreaContent");
            Assert.That(safe.Find("Header"), Is.Not.Null);
            Assert.That(safe.Find("Middle"), Is.Not.Null);
            Assert.That(safe.Find("Footer"), Is.Not.Null);
            Assert.That(File.Exists(CollectionViewDemoSeeder.Folder + "/Tag.prefab"));
            Assert.That(File.Exists(CollectionViewDemoSeeder.Folder + "/Product.prefab"));
            var view = prefab.GetComponentInChildren<CollectionView>(true);
            var scroll = view.GetComponent<ScrollRect>();
            Assert.That(scroll.content.GetComponent<LayoutGroup>(), Is.Null);
            Assert.That(scroll.content.GetComponent<ContentSizeFitter>(), Is.Null);
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.That(component, Is.Not.Null, "Missing script");
            foreach (var path in AssetDatabase.GetDependencies(CollectionViewDemoSeeder.PrefabPath, true))
                Assert.That(path.StartsWith("Assets/ParcelSorter/"), Is.False, path);
        }

        [TestCase(720, 1280, 0, 0)]
        [TestCase(1080, 1920, 0, 0)]
        [TestCase(1080, 2400, 0, 0)]
        [TestCase(1080, 1920, 120, 80)]
        public void ParentRegionsAndClickTargetsFit(int width, int height, int topInset, int bottomInset)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionViewDemoSeeder.PrefabPath);
            var root = Object.Instantiate(prefab);
            try
            {
                root.GetComponentInChildren<DemoSafeArea>().enabled = false;
                root.GetComponent<CanvasScaler>().enabled = false;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
                Canvas.ForceUpdateCanvases();
                var safe = (RectTransform)root.transform.Find("SafeAreaContent");
                safe.anchorMin = new Vector2(0, (float)bottomInset / height);
                safe.anchorMax = new Vector2(1, 1f - (float)topInset / height);
                safe.offsetMin = safe.offsetMax = Vector2.zero;
                LayoutRebuilder.ForceRebuildLayoutImmediate(safe);
                var header = (RectTransform)safe.Find("Header");
                var middle = (RectTransform)safe.Find("Middle");
                var footer = (RectTransform)safe.Find("Footer");
                Assert.That(middle.rect.width, Is.GreaterThan(0));
                Assert.That(middle.rect.height, Is.GreaterThan(0));
                var corners = new Vector3[4];
                header.GetWorldCorners(corners);
                float headerBottom = corners[0].y;
                middle.GetWorldCorners(corners);
                Assert.That(corners[1].y, Is.LessThanOrEqualTo(headerBottom + 0.01f));
                float middleBottom = corners[0].y;
                footer.GetWorldCorners(corners);
                Assert.That(corners[1].y, Is.LessThanOrEqualTo(middleBottom + 0.01f));
                foreach (var button in root.GetComponentsInChildren<Button>())
                {
                    var r = (RectTransform)button.transform;
                    Assert.That(r.rect.width, Is.GreaterThanOrEqualTo(96));
                    Assert.That(r.rect.height, Is.GreaterThanOrEqualTo(96));
                    r.GetWorldCorners(corners);
                    foreach (var point in corners)
                        Assert.That(new Rect(safe.rect.xMin - .01f, safe.rect.yMin - .01f, safe.rect.width + .02f, safe.rect.height + .02f).Contains(safe.InverseTransformPoint(point)), Is.True, button.name);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
