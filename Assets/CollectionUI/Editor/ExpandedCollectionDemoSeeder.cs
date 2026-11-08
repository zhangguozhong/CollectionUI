// 普通节点演示资源生成器。两个集合均不使用 ScrollRect；集合负责子项矩形，演示脚本负责两个集合的兄弟位置关系。
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
    public static class ExpandedCollectionDemoSeeder
    {
        public const string PrefabPath = CollectionViewDemoSeeder.Folder + "/ExpandedCollectionDemo.prefab";
        public const string ScenePath = CollectionViewDemoSeeder.Folder + "/ExpandedCollectionDemo.unity";
        const float Inset = 24, TitleHeight = 60, StatsHeight = 68, ControlGap = 12, ControlHeight = 96, HeaderGap = 16;
        const float HeaderHeight = TitleHeight + StatsHeight + ControlGap + ControlHeight + HeaderGap;
        const float FooterHeight = 72, FooterInset = 16, FooterGap = 16;
        /// <summary>复用已有模板，并创建普通节点演示。只在缺少资源或 schema 不匹配时重建。</summary>
        [MenuItem("Tools/Collection UI/Generate Expanded Demo")]
        public static void Generate()
        {
            // 先确保共享模板存在；滚动演示生成器不会反向调用本方法，因此不会递归。
            CollectionViewDemoSeeder.Generate();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<ExpandedCollectionDemo>()?.Schema == ExpandedCollectionDemo.CurrentSchema && File.Exists(ScenePath))
                return;
            var root = new GameObject("ExpandedCollectionDemo", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0;
            var bg = Rect("Background", root.transform);
            Stretch(bg);
            Background(bg, new Color(.035f, .055f, .085f));
            var safe = Rect("SafeAreaContent", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<DemoSafeArea>();
            var header = Rect("Header", safe);
            Top(header, HeaderHeight);
            header.sizeDelta = new Vector2(-Inset * 2, HeaderHeight);
            Top((RectTransform)Label("Title", header, "COLLECTION UI / EXPANDED", 32).transform, TitleHeight);
            var stats = Label("Statistics", header, "Ready", 22);
            Top((RectTransform)stats.transform, StatsHeight, TitleHeight);
            var controls = Rect("Controls", header);
            Top(controls, ControlHeight, TitleHeight + StatsHeight + ControlGap);
            var row = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            var count = Button(controls, "ChangeCount", "SHOW 4 TAGS");
            var height = Button(controls, "AutoHeight", "AUTO HEIGHT: ON");
            var footer = Rect("Footer", safe);
            footer.anchorMin = Vector2.zero;
            footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(.5f, 0);
            footer.sizeDelta = new Vector2(-Inset * 2, FooterHeight);
            footer.anchoredPosition = new Vector2(0, FooterInset);
            var note = Label("Note", footer, "No ScrollRect on either node.\nFor 10,000 items, open CollectionViewDemo.", 22);
            Stretch((RectTransform)note.transform);
            var middle = Rect("Middle", safe);
            Stretch(middle);
            middle.offsetMin = new Vector2(Inset, FooterHeight + FooterInset + FooterGap);
            middle.offsetMax = new Vector2(-Inset, -HeaderHeight);
            var firstTitle = (RectTransform)Label("FlowTitle", middle, "FLOW / CONFIGURABLE AUTO HEIGHT", 24).transform;
            Top(firstTitle, 36);
            var secondTitle = (RectTransform)Label("ColumnsTitle", middle, "COLUMNS / EXTERNAL HEIGHT = 184", 24).transform;
            Top(secondTitle, 36);
            var first = Host("ExpandedFlow", middle, 180, true);
            var second = Host("ExpandedColumns", middle, 184, false);
            var demo = root.AddComponent<ExpandedCollectionDemo>();
            demo.Configure(first, second, firstTitle, secondTitle, stats, count, height);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(root.transform, false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!Application.isBatchMode)
                EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            Debug.Log("Generated expanded collection demo: " + ScenePath);
        }

        [MenuItem("Tools/Collection UI/Open Expanded Demo")]
        public static void OpenDemo()
        {
            Generate();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>普通宿主以自身 RectTransform 为内容容器；模式和模板在首次提交数据之前配置。</summary>
        static CollectionView Host(string name, Transform parent, float height, bool autoHeight)
        {
            var host = Rect(name, parent);
            Top(host, height);
            Background(host, new Color(.06f, .085f, .12f));
            var view = host.gameObject.AddComponent<CollectionView>();
            view.DisplayMode = CollectionDisplayMode.Expanded;
            view.AutoHeight = autoHeight;
            var templates = new CollectionView.Template[3];
            var names = new[]
            {
                "card",
                "header",
                "tag"
            };
            var assets = new[]
            {
                "Card",
                "Header",
                "Tag"
            };
            for (int i = 0; i < 3; i++)
                templates[i] = new CollectionView.Template
                {
                    Id = names[i],
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionViewDemoSeeder.Folder + "/" + assets[i] + ".prefab").GetComponent<CollectionCell>()
                };
            view.SetSerializedTemplates(templates);
            return view;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            return r;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static void Top(RectTransform r, float height, float top = 0)
        {
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(.5f, 1);
            r.sizeDelta = new Vector2(0, height);
            r.anchoredPosition = new Vector2(0, -top);
        }

        static Image Background(RectTransform r, Color color)
        {
            var image = r.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text Label(string name, Transform parent, string value, int fontSize)
        {
            var r = Rect(name, parent);
            var text = r.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
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
            var image = Background(r, new Color(.12f, .30f, .43f));
            image.raycastTarget = true;
            var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var size = r.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 96;
            size.minHeight = 96;
            size.flexibleWidth = 1;
            Stretch((RectTransform)Label("Label", r, value, 24).transform);
            return button;
        }
    }
}
