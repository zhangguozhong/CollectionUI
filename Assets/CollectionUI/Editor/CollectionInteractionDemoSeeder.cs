using System.IO;
using CollectionUI.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CollectionUI.Editor
{
    /// <summary>独立交互页面：所有尺寸从安全区分区派生，控件行负责同级间距与等分。</summary>
    public static class CollectionInteractionDemoSeeder
    {
        public const string PrefabPath = CollectionViewDemoSeeder.Folder + "/InteractionDemo.prefab";
        public const string LoadingPrefabPath = CollectionViewDemoSeeder.Folder + "/LoadingIndicator.prefab";
        public const string ScenePath = CollectionViewDemoSeeder.Folder + "/InteractionDemo.unity";
        public const float TitleHeight = 60, StatisticsHeight = 64, RowHeight = 96, Gap = 12;
        public const float HeaderHeight = TitleHeight + StatisticsHeight + Gap + RowHeight + Gap + RowHeight + Gap;
        public const float FooterHeight = RowHeight, Inset = 24;
        [MenuItem("Tools/Collection UI/Generate Interaction Demo")]
        public static void Generate()
        {
            CollectionViewDemoSeeder.Generate();
            var loadingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LoadingPrefabPath);
            if (loadingPrefab == null)
            {
                var loading = Loading("LoadingIndicator", null);
                loading.gameObject.SetActive(false);
                loadingPrefab = PrefabUtility.SaveAsPrefabAsset(loading.gameObject, LoadingPrefabPath);
                Object.DestroyImmediate(loading.gameObject);
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<CollectionInteractionDemo>()?.Schema == CollectionInteractionDemo.CurrentSchema && File.Exists(ScenePath))
                return;
            var root = new GameObject("InteractionDemo", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0;
            var background = Rect("Background", root.transform);
            Stretch(background);
            Image(background, new Color(.035f, .055f, .085f));
            var safe = Rect("SafeAreaContent", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<DemoSafeArea>();
            var header = Rect("Header", safe);
            Top(header, HeaderHeight);
            header.offsetMin = new Vector2(Inset, header.offsetMin.y);
            header.offsetMax = new Vector2(-Inset, 0);
            var title = Text("Title", header, "COLLECTION / INTERACTIONS", 36);
            Top((RectTransform)title.transform, TitleHeight);
            var stats = Text("Statistics", header, "30 items | Complete requests manually", 24);
            Top((RectTransform)stats.transform, StatisticsHeight, TitleHeight);
            var row1 = Row("RequestControls", header);
            Top(row1, RowHeight, TitleHeight + StatisticsHeight + Gap);
            var row2 = Row("CompletionControls", header);
            Top(row2, RowHeight, TitleHeight + StatisticsHeight + Gap + RowHeight + Gap);
            var footer = Row("Footer", safe);
            footer.anchorMin = Vector2.zero;
            footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(.5f, 0);
            footer.sizeDelta = new Vector2(-Inset * 2, FooterHeight);
            footer.anchoredPosition = new Vector2(0, Inset);
            var controls = new[]
            {
                Button(row1, "Refresh", "REFRESH"),
                Button(row1, "LoadMore", "LOAD MORE"),
                Button(row1, "Preload", "PRELOAD ON"),
                Button(row2, "Complete", "COMPLETE"),
                Button(row2, "NoMore", "NO MORE"),
                Button(row2, "NextSection", "NEXT SECTION"),
                Button(footer, "Insert", "INSERT"),
                Button(footer, "Move", "MOVE"),
                Button(footer, "Remove", "REMOVE")
            };
            var middle = Rect("Middle", safe);
            Stretch(middle);
            middle.offsetMin = new Vector2(Inset, Inset + FooterHeight + Gap);
            middle.offsetMax = new Vector2(-Inset, -HeaderHeight);
            var region = Rect("Collection", middle);
            Stretch(region);
            region.gameObject.SetActive(false);
            var scroll = region.gameObject.AddComponent<ScrollRect>();
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.horizontal = false;
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
            var collection = region.gameObject.AddComponent<CollectionView>();
            collection.SetSerializedTemplates(new[] { new CollectionView.Template { Id = "card", Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionViewDemoSeeder.Folder + "/Card.prefab").GetComponent<DemoCell>() }, new CollectionView.Template { Id = "header", Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectionViewDemoSeeder.Folder + "/Header.prefab").GetComponent<DemoCell>() } });
            region.gameObject.AddComponent<CollectionStickyHeaders>();
            region.gameObject.SetActive(true);
            var interaction = region.gameObject.AddComponent<CollectionLoader>();
            interaction.Configure(loadingPrefab.GetComponent<RectTransform>(), loadingPrefab.GetComponent<RectTransform>());
            interaction.PreloadEnabled = true;
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(root.transform, false);
            root.AddComponent<CollectionInteractionDemo>().Configure(collection, interaction, stats, controls);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!Application.isBatchMode)
                EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
        }

        static RectTransform Loading(string name, Transform parent)
        {
            var node = Rect(name, parent);
            Top(node, 96);
            Image(node, new Color(.08f, .14f, .20f));
            var spinner = Rect("Spinner", node);
            spinner.anchorMin = spinner.anchorMax = new Vector2(.5f, .5f);
            spinner.sizeDelta = new Vector2(56, 56);
            spinner.anchoredPosition = new Vector2(-90, 0);
            // 十二个独立圆点由业务动画旋转，组件只负责整体 loading 生命周期。
            for (int i = 0; i < 12; i++)
            {
                var dot = Rect("Dot" + i, spinner);
                dot.anchorMin = dot.anchorMax = new Vector2(.5f, .5f);
                dot.sizeDelta = new Vector2(8, 8);
                float angle = i * Mathf.PI / 6;
                dot.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 22;
                Image(dot, new Color(.35f, .8f, 1, .25f + .75f * i / 11)).sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            }

            var label = Text("Label", node, "Loading...", 26);
            var r = (RectTransform)label.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.sizeDelta = new Vector2(180, 64);
            r.anchoredPosition = new Vector2(40, 0);
            node.gameObject.AddComponent<DemoLoadingView>().Configure(spinner, label);
            return node;
        }

        static RectTransform Row(string name, Transform parent)
        {
            var row = Rect(name, parent);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = Gap;
            group.childControlWidth = group.childControlHeight = group.childForceExpandWidth = group.childForceExpandHeight = true;
            return row;
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

        static void Top(RectTransform r, float height, float inset = 0)
        {
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(.5f, 1);
            r.sizeDelta = new Vector2(0, height);
            r.anchoredPosition = new Vector2(0, -inset);
        }

        static Image Image(RectTransform r, Color color, bool hit = false)
        {
            var image = r.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = hit;
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

        static Button Button(Transform parent, string name, string label)
        {
            var r = Rect(name, parent);
            r.sizeDelta = new Vector2(96, RowHeight);
            var image = Image(r, new Color(.12f, .30f, .43f), true);
            var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var layout = r.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.minHeight = 96;
            layout.flexibleWidth = 1;
            var text = Text("Label", r, label, 26);
            Stretch((RectTransform)text.transform);
            return button;
        }
    }
}
