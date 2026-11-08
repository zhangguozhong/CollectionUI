// 商品示例 Cell。按预先计算的通用元素矩形放置图片、标题和价格，业务组合不进入 Runtime。
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Samples
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UniversalWaterfall.Samples", "UniversalWaterfall.Samples", "ProductCell")]
    public sealed class ProductCell : CollectionCell
    {
        [SerializeField]
        Text title, price, imageLabel;
        [SerializeField]
        Image artwork;
        public void Configure(Text title, Text price, Image artwork, Text imageLabel)
        {
            this.title = title;
            this.price = price;
            this.artwork = artwork;
            this.imageLabel = imageLabel;
            DemoContent.TitleText.ApplyTo(title, true);
            DemoContent.PriceText.ApplyTo(price, true);
        }

        protected override void OnBind(LayoutEntry entry) => UpdateContent(entry);
        /// <summary>外框或位置元数据变化时更新显示，不重建绑定世代，也不取消已有异步任务。</summary>
        protected override void OnLayoutUpdated(LayoutEntry entry) => UpdateContent(entry);
        void UpdateContent(LayoutEntry entry)
        {
            var model = (ProductData)entry.Item.Data;
            var metrics = model.Measurements ?? DemoContent.ForScale(1);
            var layout = metrics.MeasureProduct(model.Title, model.Price, model.ImageAspectRatio, entry.Rect.width);
            Apply((RectTransform)artwork.transform, layout.ElementRects[0]);
            Apply((RectTransform)title.transform, layout.ElementRects[1]);
            Apply((RectTransform)price.transform, layout.ElementRects[2]);
            metrics.TitleText.ApplyTo(title, true);
            metrics.PriceText.ApplyTo(price, true);
            title.text = model.Title;
            price.text = model.Price;
            artwork.color = model.Color;
            imageLabel.text = model.ImageLabel;
        }

        static void Apply(RectTransform target, Rect frame)
        {
            target.anchorMin = target.anchorMax = new Vector2(0, 1);
            target.pivot = new Vector2(0, 1);
            target.anchoredPosition = new Vector2(frame.x, -frame.y);
            target.sizeDelta = frame.size;
        }

        protected override void OnUnbind()
        {
            if (title != null)
                title.text = string.Empty;
            if (price != null)
                price.text = string.Empty;
            if (imageLabel != null)
                imageLabel.text = string.Empty;
        }
    }
}
