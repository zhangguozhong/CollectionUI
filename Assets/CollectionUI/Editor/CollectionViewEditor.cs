using UnityEditor;

namespace CollectionUI.Editor
{
    /// <summary>保留 Unity 默认属性绘制、Tooltip、Undo 和多选编辑，只追加只读配置诊断。</summary>
    [CustomEditor(typeof(CollectionView))]
    [CanEditMultipleObjects]
    public sealed class CollectionViewEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            foreach (var selected in targets)
            {
                var view = (CollectionView)selected;
                var issues = CollectionViewConfigurationValidator.Collect(view);
                if (issues.Count == 0)
                    continue;
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(targets.Length > 1 ? "配置检查 — " + view.name : "配置检查", EditorStyles.boldLabel);
                foreach (var issue in issues)
                    EditorGUILayout.HelpBox(issue.Message, issue.Severity);
            }
        }
    }
}
