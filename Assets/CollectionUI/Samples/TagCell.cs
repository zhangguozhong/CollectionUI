// 单行标签示例。绑定时使用与 MeasureItem 相同的字体、缩放及省略规则，确保显示宽度与布局尺寸一致。
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Samples
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UniversalWaterfall.Samples", "UniversalWaterfall.Samples", "TagCell")]
    public sealed class TagCell : CollectionCell
    {
        [SerializeField]
        Text label;
        public void Configure(Text text)
        {
            label = text;
            DemoContent.TagText.ApplyTo(label, false, TextAnchor.MiddleCenter);
        }

        protected override void OnBind(LayoutEntry entry) => UpdateContent(entry);
        /// <summary>外框或位置元数据变化时更新显示，不重建绑定世代，也不取消已有异步任务。</summary>
        protected override void OnLayoutUpdated(LayoutEntry entry) => UpdateContent(entry);
        void UpdateContent(LayoutEntry entry)
        {
            var model = (TagData)entry.Item.Data;
            var metrics = model.Measurements ?? DemoContent.ForScale(1);
            metrics.TagText.ApplyTo(label, false, TextAnchor.MiddleCenter);
            label.text = metrics.Tags.Measure(model.Text, entry.Rect.width).DisplayText;
        }

        protected override void OnUnbind()
        {
            if (label != null)
                label.text = string.Empty;
        }
    }
}
