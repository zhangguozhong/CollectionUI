// 普通节点演示资源契约：检查生成幂等性、两个集合不带 ScrollRect、脚本引用完整及业务资源隔离。
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    public class ExpandedDemoContractTests
    {
        public const string PrefabPath = "Assets/CollectionUI/Samples/Generated/ExpandedCollectionDemo.prefab";
        [Test]
        public void ExpandedSeederIsIdempotent()
        {
            var files = System.IO.Directory.GetFiles(CollectionUI.Editor.CollectionViewDemoSeeder.Folder);
            var bytes = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var file in files)
                bytes[file] = System.Convert.ToBase64String(System.IO.File.ReadAllBytes(file));
            CollectionUI.Editor.ExpandedCollectionDemoSeeder.Generate();
            foreach (var file in files)
                Assert.That(System.Convert.ToBase64String(System.IO.File.ReadAllBytes(file)), Is.EqualTo(bytes[file]), file);
        }

        [Test]
        public void OrdinaryNodeDemoContainsTwoCollectionsWithoutScrollRect()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "Package needs an ordinary-node demo prefab.");
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(PrefabPath.Replace(".prefab", ".unity")), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<CollectionView>(true).Length, Is.EqualTo(2));
            Assert.That(prefab.GetComponentsInChildren<ScrollRect>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<ContentSizeFitter>(true), Is.Empty);
            foreach (var view in prefab.GetComponentsInChildren<CollectionView>(true))
                Assert.That(view.GetComponent<LayoutGroup>(), Is.Null);
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.That(component, Is.Not.Null, "Missing script");
            foreach (var path in AssetDatabase.GetDependencies(PrefabPath, true))
                Assert.That(path.StartsWith("Assets/ParcelSorter/"), Is.False, path);
        }
    }
}
