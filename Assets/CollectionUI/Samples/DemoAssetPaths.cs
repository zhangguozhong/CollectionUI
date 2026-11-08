#if UNITY_EDITOR
using UnityEditor.PackageManager;

namespace CollectionUI.Samples
{
    /// <summary>在 Assets 导入和 UPM 安装之间解析演示资源路径；仅参与编辑器操作。</summary>
    public static class DemoAssetPaths
    {
        public const string PackageName = "com.zhangguozhong.collectionui";
        public static bool IsPackage => PackageInfo.FindForAssembly(typeof(CollectionView).Assembly) != null;
        /// <summary>UPM 使用逻辑资源路径，由 AssetDatabase 解析到只读缓存。</summary>
        public static string SourceRoot => PackageInfo.FindForAssembly(typeof(CollectionView).Assembly)?.assetPath ?? "Assets/CollectionUI";
        public static string GeneratedFolder => SourceRoot + "/Samples/Generated";
        /// <summary>生成器永远写入 Assets，不修改 Packages 或 Library/PackageCache。</summary>
        public static string WritableFolder => IsPackage ? "Assets/CollectionUIDemos/Generated" : GeneratedFolder;

        public static string Asset(string filename) => GeneratedFolder + "/" + filename;
    }
}
#endif

