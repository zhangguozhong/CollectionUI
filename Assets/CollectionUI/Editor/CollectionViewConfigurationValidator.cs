using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Editor
{
    /// <summary>Inspector 配置提示；仅供编辑器展示，不会修改组件或初始化运行时对象池。</summary>
    public sealed class CollectionViewConfigurationIssue
    {
        public string Code { get; }
        public MessageType Severity { get; }
        public string Message { get; }

        public CollectionViewConfigurationIssue(string code, MessageType severity, string message)
        {
            Code = code;
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>只读检查序列化配置；不访问会触发运行时初始化的 CollectionView.ScrollRect 属性。</summary>
    public static class CollectionViewConfigurationValidator
    {
        public static List<CollectionViewConfigurationIssue> Collect(CollectionView view)
        {
            var result = new List<CollectionViewConfigurationIssue>();
            if (view == null)
                return result;
            using var serialized = new SerializedObject(view);
            var templates = serialized.FindProperty("templates");
            var ids = new HashSet<string>(System.StringComparer.Ordinal);
            if (templates.arraySize == 0)
                Add(result, "template.empty_list", MessageType.Warning, "尚未配置 Cell 模板。请在 Templates 中添加 ID 和 Prefab；若通过代码 RegisterTemplate 注册，可忽略此提示。");
            for (int i = 0; i < templates.arraySize; i++)
            {
                var entry = templates.GetArrayElementAtIndex(i);
                string id = entry.FindPropertyRelative("Id").stringValue;
                if (string.IsNullOrEmpty(id))
                    Add(result, "template.empty_id", MessageType.Error, "Templates 第 " + (i + 1) + " 项的 ID 为空。请填写与数据 ReuseIdentifier 一致的标识。");
                else if (!ids.Add(id))
                    Add(result, "template.duplicate_id", MessageType.Error, "模板 ID「" + id + "」重复。请删除重复配置，或为不同模板指定不同 ID。");
                if (entry.FindPropertyRelative("Prefab").objectReferenceValue == null)
                    Add(result, "template.missing_prefab", MessageType.Error, "Templates 第 " + (i + 1) + " 项缺少 Prefab。请指定带 CollectionCell 的预制体。");
            }

            int mode = serialized.FindProperty("displayMode").intValue;
            if (mode < 0 || mode > (int)CollectionDisplayMode.Scroll)
                Add(result, "mode.invalid", MessageType.Error, "Display Mode 值无效，请重新选择 Auto、Expanded 或 Scroll。");
            var scroll = view.GetComponent<ScrollRect>();
            bool scrolling = mode == (int)CollectionDisplayMode.Scroll || (mode == (int)CollectionDisplayMode.Auto && scroll != null);
            RectTransform content = (RectTransform)view.transform;
            bool ownsHeight = serialized.FindProperty("autoHeight").boolValue;
            if (scrolling)
            {
                if (scroll == null)
                {
                    Add(result, "scroll.missing_component", MessageType.Error, "Scroll 模式需要在本节点添加 ScrollRect，并配置 Viewport 和 Content。");
                    return result;
                }

                if (scroll.viewport == null)
                    Add(result, "scroll.missing_viewport", MessageType.Error, "ScrollRect 缺少 Viewport。请明确指定可视区域 RectTransform。");
                if (scroll.content == null)
                    Add(result, "scroll.missing_content", MessageType.Error, "ScrollRect 缺少 Content。请创建内容容器并赋值。");
                if (scroll.viewport != null && scroll.content != null && scroll.content.parent != scroll.viewport)
                    Add(result, "scroll.content_hierarchy", MessageType.Error, "Content 必须是 Viewport 的直接子节点，且不能与 Viewport 为同一节点，以保证可视区域与布局坐标一致。");
                content = scroll.content;
                ownsHeight = true;
            }

            if (content == null)
                return result;
            var layout = content.GetComponent<LayoutGroup>();
            if (layout != null && layout.enabled)
                Add(result, "layout.child_positions", MessageType.Warning, "内容容器上的 LayoutGroup 会与 CollectionView 同时控制 Cell 的位置和尺寸。请移除或禁用此 LayoutGroup；Cell 内部仍可使用布局组件。");
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null && fitter.enabled && (fitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained || (ownsHeight && fitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained)))
                Add(result, "layout.content_fitter", MessageType.Warning, "ContentSizeFitter 正在控制集合依赖的宽度，或与集合争用高度。请关闭冲突轴；普通模式由外部控制高度时可关闭 Auto Height。");
            var parentLayout = content.parent == null ? null : content.parent.GetComponent<LayoutGroup>();
            if (parentLayout != null && parentLayout.enabled && !IgnoredByParent(content))
            {
                if (scrolling)
                    Add(result, "layout.parent_scroll", MessageType.Warning, "Content 的父节点存在 LayoutGroup，会改写滚动位置或内容尺寸。请让 ScrollRect 和 CollectionView 独立控制 Content，禁用该布局或将 Content 排除出父布局。");
                else if (ownsHeight && ControlsHeight(parentLayout))
                    Add(result, "layout.parent_height", MessageType.Warning, "父 LayoutGroup 与 Auto Height 同时控制集合高度。请关闭 Auto Height 交由父布局管理，或关闭父布局的 Child Control Height；也可使用 LayoutElement.ignoreLayout 排除该节点。");
            }

            return result;
        }

        static bool ControlsHeight(LayoutGroup layout)
        {
            if (layout is HorizontalOrVerticalLayoutGroup linear)
                return linear.childControlHeight;
            // Grid 固定写入两个轴；未知自定义 LayoutGroup 保守提示用户确认尺寸所有权。
            return true;
        }

        static bool IgnoredByParent(RectTransform content)
        {
            // 与当前 UGUI LayoutGroup 的规则一致：存在 ignorer 且全部返回 true 才排除。
            // LayoutElement 即使 disabled，其 ignoreLayout getter 仍参与 UGUI 的检查。
            var components = content.GetComponents<Component>();
            bool any = false;
            foreach (var component in components)
            {
                if (!(component is ILayoutIgnorer ignorer))
                    continue;
                any = true;
                if (!ignorer.ignoreLayout)
                    return false;
            }

            return any;
        }

        static void Add(List<CollectionViewConfigurationIssue> result, string code, MessageType severity, string message)
        {
            result.Add(new CollectionViewConfigurationIssue(code, severity, message));
        }
    }
}
