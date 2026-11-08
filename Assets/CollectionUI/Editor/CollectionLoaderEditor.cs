using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Editor
{
    /// <summary>请求交互配置只读检查，不自动创建业务 loading 或改写 ScrollRect 的运动方式。</summary>
    [CustomEditor(typeof(CollectionLoader))]
    [CanEditMultipleObjects]
    public sealed class CollectionLoaderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            foreach (var selected in targets)
            {
                var component = (CollectionLoader)selected;
                using var data = new SerializedObject(component);
                var scroll = component.GetComponent<ScrollRect>();
                if (scroll == null || scroll.viewport == null || scroll.content == null)
                {
                    EditorGUILayout.HelpBox("请先配置本节点 ScrollRect 的 Viewport 和 Content，再配置业务 loading 节点。", MessageType.Error);
                    continue;
                }

                if (scroll.movementType != ScrollRect.MovementType.Elastic)
                    EditorGUILayout.HelpBox("下拉和上拉手势需要 ScrollRect 的 Movement Type = Elastic。组件不会自动更改此配置。", MessageType.Error);
                if (component.GetComponent<CollectionView>().DisplayMode == CollectionDisplayMode.Expanded)
                    EditorGUILayout.HelpBox("滚动交互不能使用 Expanded 模式，请选择 Auto 或 Scroll。", MessageType.Error);
                var top = data.FindProperty("refreshLoadingPrefab").objectReferenceValue as RectTransform;
                var bottom = data.FindProperty("loadMoreLoadingPrefab").objectReferenceValue as RectTransform;
                Check(top, data.FindProperty("refreshEnabled").boolValue, scroll, "顶部刷新");
                Check(bottom, data.FindProperty("loadMoreEnabled").boolValue, scroll, "底部加载");
                EditorGUILayout.HelpBox("业务提交数据后需显式调用完成 API。预加载不显示 loading，只在手势上拉且接近底部时触发；与加载更多互斥，业务确认没有更多数据时停止分页。", MessageType.Info);
            }
        }

        static void Check(RectTransform view, bool required, ScrollRect scroll, string label)
        {
            if (view == null)
            {
                if (required)
                    EditorGUILayout.HelpBox("请传入" + label + "的业务 loading Prefab 资源。", MessageType.Error);
            }
            else if (!PrefabUtility.IsPartOfPrefabAsset(view) || view.parent != null)
                EditorGUILayout.HelpBox(label + " loading 须引用 Prefab 资源的根节点，组件会自动实例化到 Viewport。", MessageType.Error);
        }
    }
}
