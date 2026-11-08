using System.IO;
using System.Collections.Generic;
using CollectionUI.Samples;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    /// <summary>交互演示的数据、资源隔离与父区布局契约。</summary>
    public class InteractionDemoContractTests
    {
        [SetUp]
        public void EnsureWritableDemo() => CollectionUI.Editor.CollectionInteractionDemoSeeder.Generate();
        [Test]
        public void HasMoreIsBusinessStateAndFeatureSwitchesRemainSerialized()
        {
            var root = new GameObject("Loader state", typeof(RectTransform));
            root.SetActive(false);
            try
            {
                var loader = root.AddComponent<CollectionLoader>();
                Assert.That(loader.HasMore, Is.True);
                using var serialized = new SerializedObject(loader);
                Assert.That(serialized.FindProperty("hasMore"), Is.Null);
                foreach (string field in new[]
                {
                    "refreshEnabled",
                    "loadMoreEnabled",
                    "preloadEnabled"
                }

                )
                    Assert.That(serialized.FindProperty(field), Is.Not.Null);
                loader.HasMore = false;
                Assert.That(loader.HasMore, Is.False);
                Assert.That(EditorJsonUtility.ToJson(loader), Does.Not.Contain("hasMore"));
                loader.HasMore = true;
                Assert.That(loader.HasMore, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InteractionDataHasThirtyUniqueVariableHeightCards()
        {
            var data = CollectionInteractionDemo.CreateData();
            Assert.That(data.Sections.Count, Is.EqualTo(3));
            var ids = new HashSet<string>();
            var heights = new HashSet<float>();
            foreach (var section in data.Sections)
            {
                Assert.That(section.Items.Count, Is.EqualTo(10));
                Assert.That(section.Header, Is.Not.Null);
                foreach (var item in section.Items)
                {
                    Assert.That(ids.Add(item.Id), Is.True);
                    Assert.That(item.Size.Height, Is.GreaterThanOrEqualTo(96));
                    heights.Add(item.Size.Height);
                }
            }

            Assert.That(ids.Count, Is.EqualTo(30));
            Assert.That(heights.Count, Is.GreaterThan(1));
        }

        [Test]
        public void InteractionPrefabReferencesLoadingAssetsAndSafeArea()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionUI.Editor.CollectionInteractionDemoSeeder.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<DemoSafeArea>(true).Length, Is.EqualTo(1));
            var collection = prefab.GetComponentInChildren<CollectionView>(true);
            var scroll = collection.GetComponent<ScrollRect>();
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Elastic));
            Assert.That(collection.GetComponent<CollectionStickyHeaders>(), Is.Not.Null);
            Assert.That(collection.GetComponent<CollectionLoader>().PreloadEnabled, Is.True);
            Assert.That(prefab.GetComponentsInChildren<DemoLoadingView>(true), Is.Empty);
            using var data = new SerializedObject(collection.GetComponent<CollectionLoader>());
            foreach (string field in new[]
            {
                "refreshLoadingPrefab",
                "loadMoreLoadingPrefab"
            }

            )
            {
                var asset = data.FindProperty(field).objectReferenceValue as RectTransform;
                Assert.That(asset, Is.Not.Null);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(asset), Is.True);
                Assert.That(asset.GetComponent<DemoLoadingView>(), Is.Not.Null);
            }

            Assert.That(prefab.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(9));
            foreach (var button in prefab.GetComponentsInChildren<Button>(true))
            {
                var layout = button.GetComponent<LayoutElement>();
                Assert.That(layout.minWidth, Is.GreaterThanOrEqualTo(96));
                Assert.That(layout.minHeight, Is.GreaterThanOrEqualTo(96));
            }

            foreach (var path in AssetDatabase.GetDependencies(CollectionUI.Editor.CollectionInteractionDemoSeeder.PrefabPath, true))
                Assert.That(path.StartsWith("Assets/ParcelSorter/"), Is.False, path);
        }

        [Test]
        public void InteractionSeederIsIdempotent()
        {
            string prefab = CollectionUI.Editor.CollectionInteractionDemoSeeder.PrefabPath;
            string scene = CollectionUI.Editor.CollectionInteractionDemoSeeder.ScenePath;
            Assert.That(File.Exists(prefab), Is.True);
            var beforePrefab = File.ReadAllBytes(prefab);
            var beforeScene = File.ReadAllBytes(scene);
            CollectionUI.Editor.CollectionInteractionDemoSeeder.Generate();
            Assert.That(File.ReadAllBytes(prefab), Is.EqualTo(beforePrefab));
            Assert.That(File.ReadAllBytes(scene), Is.EqualTo(beforeScene));
        }
    }
}
