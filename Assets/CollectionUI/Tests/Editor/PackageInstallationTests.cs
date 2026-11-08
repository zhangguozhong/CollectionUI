using System.IO;
using NUnit.Framework;
using CollectionUI.Samples;
using CollectionUI.Editor;
using System.Collections.Generic;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CollectionUI.Tests
{
    /// <summary>同一组契约同时在 Assets 导入和 UPM 消费工程中运行。</summary>
    public class PackageInstallationTests
    {
        [System.Serializable]
        class Manifest
        {
            public string name, version, unity;
        }

        [TestCase("CollectionViewDemo")]
        [TestCase("ExpandedCollectionDemo")]
        [TestCase("InteractionDemo")]
        public void BundledScenesAndPrefabsResolveInBothInstallationModes(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoAssetPaths.Asset(name + ".prefab"));
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoAssetPaths.Asset(name + ".unity")));
            foreach (var script in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.IsNotNull(script, "演示中不能有 Missing Script。");
            Assert.IsTrue(DemoAssetPaths.WritableFolder.StartsWith("Assets/"));
            if (DemoAssetPaths.IsPackage)
                Assert.IsTrue(DemoAssetPaths.SourceRoot.StartsWith("Packages/"));
        }

        [Test]
        public void GeneratingEditableDemosPreservesBundledAssetsAndIsIdempotent()
        {
            // Assets 下沿用已有资源；UPM 下生成可写副本，不能覆盖包缓存。
            var package = PackageInfo.FindForAssembly(typeof(CollectionView).Assembly);
            string source = (package == null ? DemoAssetPaths.SourceRoot : package.resolvedPath) + "/Samples/Generated";
            var original = ReadFiles(source);
            CollectionViewDemoSeeder.Generate();
            ExpandedCollectionDemoSeeder.Generate();
            CollectionInteractionDemoSeeder.Generate();
            foreach (var pair in original)
                CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key), pair.Key);
            var generated = ReadFiles(DemoAssetPaths.WritableFolder);
            CollectionViewDemoSeeder.Generate();
            ExpandedCollectionDemoSeeder.Generate();
            CollectionInteractionDemoSeeder.Generate();
            foreach (var pair in generated)
                CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key), pair.Key);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(CollectionViewDemoSeeder.PrefabPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(ExpandedCollectionDemoSeeder.PrefabPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(CollectionInteractionDemoSeeder.PrefabPath));
        }

        static Dictionary<string, byte[]> ReadFiles(string folder)
        {
            var result = new Dictionary<string, byte[]>();
            foreach (string file in Directory.GetFiles(folder))
                result.Add(file, File.ReadAllBytes(file));
            return result;
        }

        [Test]
        public void PackageManifestDeclaresStableIdentityAndSupportedUnityVersion()
        {
            var package = PackageInfo.FindForAssembly(typeof(CollectionView).Assembly);
            string root = package == null ? "Assets/CollectionUI" : package.resolvedPath;
            string file = root + "/package.json";
            Assert.IsTrue(File.Exists(file), "Git 子目录必须包含 UPM package.json。");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
            Assert.AreEqual("com.zhangguozhong.collectionui", manifest.name);
            Assert.AreEqual("1.0.0", manifest.version);
            Assert.AreEqual("2022.3", manifest.unity);
        }
    }
}
