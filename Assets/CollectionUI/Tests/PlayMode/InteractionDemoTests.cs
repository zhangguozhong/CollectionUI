#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using CollectionUI.Samples;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CollectionUI.Tests
{
    /// <summary>以演示的真实按钮验证业务完成请求及差量操作，不直接修改控制器私有状态。</summary>
    public class InteractionDemoTests
    {
        GameObject root;
        [UnityTest]
        public IEnumerator ButtonsKeepLoadingUntilCompletionAndApplyChanges()
        {
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/InteractionDemo.prefab"));
            root.GetComponent<CanvasScaler>().enabled = false;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            root.GetComponentInChildren<DemoSafeArea>().enabled = false;
            ((RectTransform)root.transform).sizeDelta = new Vector2(720, 1280);
            yield return null;
            var demo = root.GetComponent<CollectionInteractionDemo>();
            void Click(string name) => root.GetComponentsInChildren<Button>().Single(b => b.name == name).onClick.Invoke();
            int Count() => demo.Collection.GetSnapshotCopy().Sections.Sum(s => s.Items.Count);
            Assert.That(Count(), Is.EqualTo(30));
            Click("LoadMore");
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(demo.Interaction.IsLoadingMore, Is.True);
            Click("Complete");
            Assert.That(Count(), Is.EqualTo(40));
            Assert.That(demo.Interaction.ActiveRequestId, Is.Zero);
            Click("Insert");
            Assert.That(Count(), Is.EqualTo(41));
            Click("Move");
            Assert.That(Count(), Is.EqualTo(41));
            Click("Remove");
            Assert.That(Count(), Is.EqualTo(40));
            Click("NoMore");
            Click("LoadMore");
            Assert.That(demo.Interaction.ActiveRequestId, Is.Zero);
            Click("Refresh");
            Assert.That(demo.Interaction.IsRefreshing, Is.True);
            Click("Complete");
            Assert.That(Count(), Is.EqualTo(30));
            Assert.That(demo.Interaction.HasMore, Is.True);
            foreach (var button in root.GetComponentsInChildren<Button>())
            {
                var r = (RectTransform)button.transform;
                Assert.That(r.rect.width, Is.GreaterThanOrEqualTo(96));
                Assert.That(r.rect.height, Is.GreaterThanOrEqualTo(96));
            }
        }

        [UnityTest]
        public IEnumerator RefreshCompletionInRealDemoKeepsThePullForSmoothReturn()
        {
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CollectionUI/Samples/Generated/InteractionDemo.prefab"));
            root.GetComponent<CanvasScaler>().enabled = false;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            root.GetComponentInChildren<DemoSafeArea>().enabled = false;
            ((RectTransform)root.transform).sizeDelta = new Vector2(720, 1280);
            yield return null;
            var demo = root.GetComponent<CollectionInteractionDemo>();
            demo.Interaction.BeginRefresh();
            yield return new WaitForSecondsRealtime(1);
            var content = demo.Collection.ScrollRect.content;
            float before = content.anchoredPosition.y;
            Assert.That(before, Is.LessThan(-90));
            demo.CompletePending();
            Assert.That(content.anchoredPosition.y, Is.EqualTo(before).Within(.1f), "完成按钮不能立即定位而打断回弹。");
            Assert.That(demo.Interaction.ActiveRequestId, Is.Zero);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(content.anchoredPosition.y, Is.EqualTo(0).Within(.5f));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (root != null)
                Object.Destroy(root);
            yield return null;
        }
    }
}
#endif

