using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CollectionUI.Editor;

namespace CollectionUI.Tests
{
    /// <summary>配置诊断必须只读，并准确区分组件拥有的轴与外部布局拥有的轴。</summary>
    public class CollectionConfigurationTests
    {
        GameObject root, cellObject;
        CollectionView view;
        CollectionCell cell;
        [SetUp]
        public void Setup()
        {
            root = new GameObject("Collection", typeof(RectTransform));
            view = root.AddComponent<CollectionView>();
            cellObject = new GameObject("Template", typeof(RectTransform));
            cell = cellObject.AddComponent<CollectionCell>();
            view.SetSerializedTemplates(new[] { new CollectionView.Template { Id = "item", Prefab = cell } });
        }

        [TearDown]
        public void Cleanup()
        {
            var parent = root.transform.parent;
            Object.DestroyImmediate(root);
            if (parent != null)
                Object.DestroyImmediate(parent.gameObject);
            Object.DestroyImmediate(cellObject);
        }

        bool Has(string code) => CollectionViewConfigurationValidator.Collect(view).Any(x => x.Code == code);
        [Test]
        public void DuplicateIdsAndMissingMappingsAreReported()
        {
            view.SetSerializedTemplates(new[] { new CollectionView.Template { Id = "same", Prefab = cell }, new CollectionView.Template { Id = "same", Prefab = cell }, new CollectionView.Template { Id = "", Prefab = null } });
            Assert.That(Has("template.duplicate_id"), Is.True);
            Assert.That(Has("template.empty_id"), Is.True);
            Assert.That(Has("template.missing_prefab"), Is.True);
        }

        [Test]
        public void RuntimeRegistrationIsOnlyAWarningAndInspectionDoesNotInitialize()
        {
            view.SetSerializedTemplates(null);
            var before = EditorJsonUtility.ToJson(view);
            var issues = CollectionViewConfigurationValidator.Collect(view);
            Assert.That(issues.Single(x => x.Code == "template.empty_list").Severity, Is.EqualTo(MessageType.Warning));
            Assert.That(root.transform.childCount, Is.Zero);
            Assert.That(root.GetComponent<ScrollRect>(), Is.Null);
            Assert.That(EditorJsonUtility.ToJson(view), Is.EqualTo(before));
            Assert.DoesNotThrow(() => view.DisplayMode = CollectionDisplayMode.Expanded);
        }

        [Test]
        public void ExplicitScrollRequiresComponentAndReferences()
        {
            view.DisplayMode = CollectionDisplayMode.Scroll;
            Assert.That(Has("scroll.missing_component"), Is.True);
            var scroll = root.AddComponent<ScrollRect>();
            Assert.That(Has("scroll.missing_viewport"), Is.True);
            Assert.That(Has("scroll.missing_content"), Is.True);
            Configure(scroll);
            Assert.That(CollectionViewConfigurationValidator.Collect(view), Is.Empty);
        }

        [Test]
        public void AutoUsesLocalScrollButExplicitExpandedIgnoresIt()
        {
            root.AddComponent<ScrollRect>();
            Assert.That(Has("scroll.missing_viewport"), Is.True);
            view.DisplayMode = CollectionDisplayMode.Expanded;
            Assert.That(Has("scroll.missing_viewport"), Is.False);
        }

        [Test]
        public void InvalidScrollHierarchyIsReported()
        {
            var scroll = root.AddComponent<ScrollRect>();
            Configure(scroll);
            scroll.content.SetParent(root.transform, false);
            Assert.That(Has("scroll.content_hierarchy"), Is.True);
            scroll.content = scroll.viewport;
            Assert.That(Has("scroll.content_hierarchy"), Is.True);
        }

        [Test]
        public void HostLayoutGroupConflictsOnlyWhenEnabled()
        {
            var layout = root.AddComponent<VerticalLayoutGroup>();
            Assert.That(Has("layout.child_positions"), Is.True);
            layout.enabled = false;
            Assert.That(Has("layout.child_positions"), Is.False);
        }

        [Test]
        public void HeightFitterConflictsOnlyForOwnedHeightButWidthAlwaysConflicts()
        {
            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Assert.That(Has("layout.content_fitter"), Is.True);
            view.AutoHeight = false;
            Assert.That(Has("layout.content_fitter"), Is.False);
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Assert.That(Has("layout.content_fitter"), Is.True);
            fitter.enabled = false;
            Assert.That(Has("layout.content_fitter"), Is.False);
        }

        [Test]
        public void ParentHeightControlRespectsIgnoreLayoutAndAutoHeight()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            root.transform.SetParent(parent.transform, false);
            var group = parent.AddComponent<VerticalLayoutGroup>();
            group.childControlHeight = true;
            Assert.That(Has("layout.parent_height"), Is.True);
            var element = root.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            element.enabled = false;
            Assert.That(Has("layout.parent_height"), Is.False);
            element.ignoreLayout = false;
            view.AutoHeight = false;
            Assert.That(Has("layout.parent_height"), Is.False);
            view.AutoHeight = true;
            group.enabled = false;
            Assert.That(Has("layout.parent_height"), Is.False);
        }

        [Test]
        public void ParentThatDoesNotControlHeightIsAllowed()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            root.transform.SetParent(parent.transform, false);
            var group = parent.AddComponent<HorizontalLayoutGroup>();
            group.childControlHeight = false;
            group.childForceExpandHeight = true;
            Assert.That(Has("layout.parent_height"), Is.False);
            Object.DestroyImmediate(group);
            parent.AddComponent<GridLayoutGroup>();
            Assert.That(Has("layout.parent_height"), Is.True);
        }

        [Test]
        public void ScrollChecksContentNotCollectionHostForLayoutConflicts()
        {
            var scroll = root.AddComponent<ScrollRect>();
            Configure(scroll);
            root.AddComponent<VerticalLayoutGroup>();
            Assert.That(Has("layout.child_positions"), Is.False);
            scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
            Assert.That(Has("layout.child_positions"), Is.True);
            var parent = scroll.content.parent.gameObject.AddComponent<VerticalLayoutGroup>();
            parent.childControlHeight = false;
            Assert.That(Has("layout.parent_scroll"), Is.True);
        }

        [Test]
        public void ShippedDemosHaveNoConfigurationIssues()
        {
            foreach (var path in new[]
            {
                CollectionViewDemoSeeder.PrefabPath,
                ExpandedCollectionDemoSeeder.PrefabPath
            }

            )
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var collection in prefab.GetComponentsInChildren<CollectionView>(true))
                    Assert.That(CollectionViewConfigurationValidator.Collect(collection), Is.Empty, path + "/" + collection.name);
            }
        }

        static void Configure(ScrollRect scroll)
        {
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scroll.transform, false);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = (RectTransform)content.transform;
        }
    }
}
