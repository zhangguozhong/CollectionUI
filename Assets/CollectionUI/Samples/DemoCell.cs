// 基础卡片及分组头尾示例。业务数据只提供标题，背景色和尺寸文字用于观察排版；解绑时清空旧显示。
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Samples
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UniversalWaterfall.Samples", "UniversalWaterfall.Samples", "DemoCell")]
    public sealed class DemoCell : CollectionCell
    {
        [SerializeField]
        Text label;
        [SerializeField]
        Image background;
        public void Configure(Text text, Image image)
        {
            label = text;
            background = image;
        }

        protected override void OnBind(LayoutEntry entry)
        {
            if (label == null)
                return;
            var card = entry.Item.Data as DemoCard;
            label.text = card == null ? entry.Item.Id : card.Title;
            if (entry.Kind == ElementKind.Item)
            {
                label.text += "\n" + entry.Rect.width.ToString("0") + " x " + entry.Rect.height.ToString("0");
                background.color = Color.HSVToRGB((entry.ItemIndex * 0.067f + entry.SectionIndex * 0.16f) % 1f, 0.36f, 0.39f);
            }
            else
                background.color = new Color(0.13f, 0.20f, 0.28f);
        }

        /// <summary>条目移动或外框改变时更新示例标签；不取消业务绑定或异步任务。</summary>
        protected override void OnLayoutUpdated(LayoutEntry entry)
        {
            OnBind(entry);
        }

        protected override void OnUnbind()
        {
            if (label != null)
                label.text = string.Empty;
        }
    }

    public sealed class DemoCard
    {
        public string Title;
    }
}
