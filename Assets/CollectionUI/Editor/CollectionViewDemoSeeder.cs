// 滚动演示资源生成器。负责模板、Canvas、安全区、页头/内容区/页脚及场景；通过 schema 判断是否需要重建，保留已有资源路径与 GUID。
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CollectionUI.Samples;

namespace CollectionUI.Editor
{
    public static class CollectionViewDemoSeeder
    {
        const float TitleHeight = 80, StatisticsHeight = 100, ControlsGap = 24, ControlsHeight = 104, HeaderBottomGap = 32;
        const float HeaderHeight = TitleHeight + StatisticsHeight + ControlsGap + ControlsHeight + HeaderBottomGap;
        const float PageInset = 24, FooterHeight = 128, FooterInset = 16, FooterGap = 16;
        public const string Folder = "Assets/CollectionUI/Samples/Generated";
        public const string ScenePath = Folder + "/CollectionViewDemo.unity";
        public const string PrefabPath = Folder + "/CollectionViewDemo.prefab";
        /// <summary>按明确的父子区域生成演示资源；schema 一致且资源完整时直接返回，确保重复生成不产生无意义变更。</summary>
        [MenuItem("Tools/Collection UI/Generate Demo Assets")]
        public static void Generate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<CollectionViewDemo>()?.Schema == CollectionViewDemo.CurrentSchema && File.Exists(ScenePath) && File.Exists(Folder + "/Card.prefab") && File.Exists(Folder + "/Header.prefab") && File.Exists(Folder + "/Tag.prefab") && File.Exists(Folder + "/Product.prefab"))
            {
                Debug.Log("Collection UI demo assets already match schema; no changes.");
                return;
            }

            Directory.CreateDirectory(Folder);
            var card = CreateCell("Card", 24);
            var header = CreateCell("Header", 28);
            var tag = CreateTag();
            var product = CreateProduct();
            var root = new GameObject("CollectionViewDemo", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0;
            var background = Rect("Background", root.transform);
            Stretch(background);
            Image(background, new Color(0.035f, 0.055f, 0.085f));
            var safe = Rect("SafeAreaContent", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<DemoSafeArea>();
            var top = Rect("Header", safe);
            Top(top, HeaderHeight);
            top.offsetMin = new Vector2(PageInset, top.offsetMin.y);
            top.offsetMax = new Vector2(-PageInset, top.offsetMax.y);
            var title = Text("Title", top, "COLLECTION UI", 40);
            Top((RectTransform)title.transform, TitleHeight);
            var stats = Text("Statistics", top, "Ready", 26);
            Top((RectTransform)stats.transform, StatisticsHeight, TitleHeight);
            var row = Rect("Controls", top);
            Top(row, ControlsHeight, TitleHeight + StatisticsHeight + ControlsGap);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 16;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            var small = Button(row, "OneThousand", "1,000 ITEMS");
            var large = Button(row, "TenThousand", "10,000 ITEMS");
            var auto = Button(row, "AutoScroll", "AUTO SCROLL");
            var bottom = Rect("Footer", safe);
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = new Vector2(1, 0);
            bottom.pivot = new Vector2(.5f, 0);
            bottom.sizeDelta = new Vector2(-2 * PageInset, FooterHeight);
            bottom.anchoredPosition = new Vector2(0, FooterInset);
            var mode = Button(bottom, "LayoutMode", "TAGS / FLOW  >  PRODUCTS / MEASURED");
            Stretch((RectTransform)mode.transform);
            var middle = Rect("Middle", safe);
            Stretch(middle);
            middle.offsetMin = new Vector2(PageInset, FooterHeight + FooterInset + FooterGap);
            middle.offsetMax = new Vector2(-PageInset, -HeaderHeight);
            var region = Rect("Collection", middle);
            Stretch(region);
            region.gameObject.SetActive(false);
            var scroll = region.gameObject.AddComponent<ScrollRect>();
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", region);
            Stretch(viewport);
            Image(viewport, new Color(.06f, .085f, .12f), true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1);
            content.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            var collection = region.gameObject.AddComponent<CollectionView>();
            collection.SetSerializedTemplates(new[] { new CollectionView.Template { Id = "card", Prefab = card }, new CollectionView.Template { Id = "header", Prefab = header }, new CollectionView.Template { Id = "tag", Prefab = tag }, new CollectionView.Template { Id = "product", Prefab = product } });
            region.gameObject.SetActive(true);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(root.transform, false);
            var demo = root.AddComponent<CollectionViewDemo>();
            demo.Configure(collection, stats, small, large, auto, mode);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!Application.isBatchMode)
                EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            Debug.Log("Generated standalone collection demo: " + ScenePath);
        }

        /// <summary>导出前确保两种演示均已生成；仅递归导出组件目录，不包含 ParcelSorter 业务资源。</summary>
        [MenuItem("Tools/Collection UI/Export Package")]
        public static void ExportPackage()
        {
            Generate();
            ExpandedCollectionDemoSeeder.Generate();
            CollectionInteractionDemoSeeder.Generate();
            Directory.CreateDirectory("Builds");
            AssetDatabase.ExportPackage("Assets/CollectionUI", "Builds/CollectionUI.unitypackage", ExportPackageOptions.Recurse);
            Debug.Log("Exported Builds/CollectionUI.unitypackage without external asset dependencies.");
        }

        [MenuItem("Tools/Collection UI/Open Demo")]
        public static void OpenDemo()
        {
            Generate();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        static DemoCell CreateCell(string name, int size)
        {
            var r = Rect(name, null);
            r.sizeDelta = new Vector2(200, 200);
            var image = Image(r, new Color(.16f, .24f, .32f));
            var text = Text("Label", r, name, size);
            Stretch((RectTransform)text.transform);
            ((RectTransform)text.transform).offsetMin = new Vector2(12, 12);
            ((RectTransform)text.transform).offsetMax = new Vector2(-12, -12);
            var cell = r.gameObject.AddComponent<DemoCell>();
            cell.Configure(text, image);
            var prefab = PrefabUtility.SaveAsPrefabAsset(r.gameObject, Folder + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(r.gameObject);
            return prefab.GetComponent<DemoCell>();
        }

        static TagCell CreateTag()
        {
            var r = Rect("Tag", null);
            r.sizeDelta = new Vector2(180, 96);
            Image(r, new Color(.15f, .28f, .37f));
            var label = Text("Label", r, "Tag", 28);
            Stretch((RectTransform)label.transform);
            ((RectTransform)label.transform).offsetMin = new Vector2(20, 12);
            ((RectTransform)label.transform).offsetMax = new Vector2(-20, -12);
            var cell = r.gameObject.AddComponent<TagCell>();
            cell.Configure(label);
            var prefab = PrefabUtility.SaveAsPrefabAsset(r.gameObject, Folder + "/Tag.prefab");
            UnityEngine.Object.DestroyImmediate(r.gameObject);
            return prefab.GetComponent<TagCell>();
        }

        static ProductCell CreateProduct()
        {
            var r = Rect("Product", null);
            r.sizeDelta = new Vector2(480, 600);
            Image(r, new Color(.94f, .95f, .96f));
            var picture = Rect("Artwork", r);
            var artwork = Image(picture, new Color(.4f, .5f, .6f));
            var imageLabel = Text("ImageLabel", picture, "PRODUCT", 32);
            Stretch((RectTransform)imageLabel.transform);
            var title = Text("Title", r, "Product title", 28);
            title.color = new Color(.1f, .13f, .17f);
            var price = Text("Price", r, "$19.90", 32);
            price.color = new Color(.75f, .22f, .08f);
            var cell = r.gameObject.AddComponent<ProductCell>();
            cell.Configure(title, price, artwork, imageLabel);
            var prefab = PrefabUtility.SaveAsPrefabAsset(r.gameObject, Folder + "/Product.prefab");
            UnityEngine.Object.DestroyImmediate(r.gameObject);
            return prefab.GetComponent<ProductCell>();
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            if (parent != null)
                r.SetParent(parent, false);
            return r;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static void Top(RectTransform r, float height, float inset = 0)
        {
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(.5f, 1);
            r.sizeDelta = new Vector2(0, height);
            r.anchoredPosition = new Vector2(0, -inset);
        }

        static Image Image(RectTransform r, Color color, bool raycast = false)
        {
            var image = r.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static Text Text(string name, Transform parent, string value, int size)
        {
            var r = Rect(name, parent);
            var text = r.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static Button Button(Transform parent, string name, string value)
        {
            var r = Rect(name, parent);
            r.sizeDelta = new Vector2(96, 104);
            var image = Image(r, new Color(.12f, .30f, .43f), true);
            var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var element = r.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 96;
            element.minHeight = 96;
            element.flexibleWidth = 1;
            var text = Text("Label", r, value, 26);
            Stretch((RectTransform)text.transform);
            return button;
        }
    }
}
