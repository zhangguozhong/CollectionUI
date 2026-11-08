// 演示业务数据与尺寸提供器。标签/商品是示例场景，具体模型和组合规则留在 Samples；Runtime 只处理通用数据与尺寸。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CollectionUI.Samples
{
    public enum DemoScenario
    {
        FreeRectangles,
        Columns,
        Tags,
        Products
    }

    public sealed class TagData
    {
        public string Text;
        public DemoMeasurements Measurements;
    }

    public sealed class ProductData
    {
        public string Title, Price, ImageLabel;
        public DemoMeasurements Measurements;
        public float ImageAspectRatio;
        public Color Color;
    }

    public sealed class DemoMeasurements
    {
        public readonly UguiTextMeasurer TagText, TitleText, PriceText;
        public readonly SingleLineMeasurer Tags;
        readonly Dictionary<ProductKey, LayoutMetrics> productMetrics = new Dictionary<ProductKey, LayoutMetrics>();
        struct ProductKey : IEquatable<ProductKey>
        {
            public string Title, Price;
            public float Ratio, Width;
            public bool Equals(ProductKey other) => Title == other.Title && Price == other.Price && Ratio == other.Ratio && Width == other.Width;
            public override bool Equals(object obj) => obj is ProductKey key && Equals(key);
            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Title.GetHashCode() * 397 ^ Price.GetHashCode()) * 397 ^ Ratio.GetHashCode()) * 397 ^ Width.GetHashCode();
                }
            }
        }

        // 业务组合保留在示例中；Runtime 只认识通用元素和矩形。
        /// <summary>示例业务将图片比例、标题和价格组合成三个通用元素，并缓存完整的子矩形结果供 Cell 绑定使用。</summary>
        public LayoutMetrics MeasureProduct(string title, string price, float ratio, float width)
        {
            var key = new ProductKey
            {
                Title = title ?? string.Empty,
                Price = price ?? string.Empty,
                Ratio = ratio,
                Width = width
            };
            if (productMetrics.TryGetValue(key, out var result))
                return result;
            var elements = new[]
            {
                ContentElement.Aspect(ratio),
                ContentElement.Text(TitleText, key.Title),
                ContentElement.Text(PriceText, key.Price)
            };
            result = VerticalContentMeasurer.Measure(elements, width, new Insets(16, 16, 16, 16), 12);
            if (productMetrics.Count >= 512)
                productMetrics.Clear();
            productMetrics.Add(key, result);
            return result;
        }

        public DemoMeasurements(Font font, float pixelsPerUnit)
        {
            TagText = new UguiTextMeasurer(font, 28, pixelsPerUnit: pixelsPerUnit);
            TitleText = new UguiTextMeasurer(font, 28, pixelsPerUnit: pixelsPerUnit);
            PriceText = new UguiTextMeasurer(font, 32, FontStyle.Bold, pixelsPerUnit: pixelsPerUnit);
            Tags = new SingleLineMeasurer(TagText);
        }
    }

    /// <summary>按 Canvas 缩放隔离的不可变样式；数据携带匹配的样式，支持不同 Canvas 同时使用。</summary>
    public static class DemoContent
    {
        static readonly Font Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static readonly Dictionary<float, DemoMeasurements> measurements = new Dictionary<float, DemoMeasurements>();
        /// <summary>测量配置不可变并按 Canvas 缩放隔离，避免不同 Canvas 的样式互相覆盖；缓存数量设上限。</summary>
        public static DemoMeasurements ForScale(float pixelsPerUnit)
        {
            if (measurements.TryGetValue(pixelsPerUnit, out var result))
                return result;
            result = new DemoMeasurements(Font, pixelsPerUnit);
            if (measurements.Count >= 8)
                measurements.Clear();
            measurements.Add(pixelsPerUnit, result);
            return result;
        }

        public static UguiTextMeasurer TagText => ForScale(1).TagText;
        public static UguiTextMeasurer TitleText => ForScale(1).TitleText;
        public static UguiTextMeasurer PriceText => ForScale(1).PriceText;

        /// <summary>交给 CollectionView.MeasureItem 的示例回调，只计算尺寸，不创建 UI 对象。</summary>
        public static Vector2 Measure(CollectionItem item, float width)
        {
            if (item.Data is TagData tag)
                return (tag.Measurements ?? ForScale(1)).Tags.Measure(tag.Text, width).Size;
            if (item.Data is ProductData product)
                return (product.Measurements ?? ForScale(1)).MeasureProduct(product.Title, product.Price, product.ImageAspectRatio, width).Size;
            throw new ArgumentException("The sample measurer expects TagData or ProductData.");
        }

        /// <summary>建立五组稳定 ID 的数据，分组头尾共享模板；不同业务模型选择自己的尺寸计算规则。</summary>
        public static CollectionSnapshot Create(int count, DemoScenario scenario, DemoMeasurements metrics = null)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (scenario == DemoScenario.FreeRectangles || scenario == DemoScenario.Columns)
                return CollectionViewDemo.CreateData(count, scenario == DemoScenario.Columns ? LayoutMode.Columns : LayoutMode.Skyline);
            metrics = metrics ?? ForScale(1);
            var result = new CollectionSnapshot();
            for (int s = 0; s < 5; s++)
            {
                var section = new CollectionSection
                {
                    Id = "section-" + s
                };
                section.Layout = new SectionLayoutSettings
                {
                    Mode = scenario == DemoScenario.Tags ? LayoutMode.Flow : LayoutMode.Columns,
                    ColumnCount = 2,
                    Insets = new Insets(16, 16, 16, 16),
                    HorizontalSpacing = 16,
                    VerticalSpacing = 20,
                    SectionSpacing = 32
                };
                section.Header = new CollectionItem
                {
                    Id = "header",
                    ReuseIdentifier = "header",
                    Size = ItemSize.Fixed(0, 88),
                    Data = new DemoCard
                    {
                        Title = "SECTION " + (s + 1) + (scenario == DemoScenario.Tags ? " / TAGS - ORDERED FLOW" : " / PRODUCTS - MEASURED HEIGHT")
                    }
                };
                section.Footer = new CollectionItem
                {
                    Id = "footer",
                    ReuseIdentifier = "header",
                    Size = ItemSize.Fixed(0, 56),
                    Data = new DemoCard
                    {
                        Title = "End of section " + (s + 1)
                    }
                };
                for (int i = count * s / 5; i < count * (s + 1) / 5; i++)
                {
                    object model = scenario == DemoScenario.Tags ? (object)new TagData
                    {
                        Measurements = metrics,
                        Text = TagNames[i % TagNames.Length]
                    }

                    : new ProductData
                    {
                        Measurements = metrics,
                        Title = ProductTitles[i % ProductTitles.Length],
                        Price = Prices[i % Prices.Length],
                        ImageLabel = ImageLabels[i % ImageLabels.Length],
                        ImageAspectRatio = ImageRatios[i % ImageRatios.Length],
                        Color = Color.HSVToRGB(i * .073f % 1, .22f, .55f)
                    };
                    section.Items.Add(new CollectionItem { Id = "item-" + i, ReuseIdentifier = scenario == DemoScenario.Tags ? "tag" : "product", Size = ItemSize.Measured(), Data = model });
                }

                result.Sections.Add(section);
            }

            return result;
        }

        static readonly string[] TagNames =
        {
            "New",
            "Outdoor",
            "Home & living",
            "Travel essentials",
            "Music",
            "Sale",
            "Books",
            "Running shoes",
            "Accessories",
            "Photography",
            "Handmade",
            "Kitchen",
            "Gifts for family and friends",
            "Spring collection",
            "A deliberately very long tag: single-line text is measured, capped to the available row width, and truncated at a complete text element with an ellipsis instead of spilling beyond its card or silently changing the font size",
            "Office",
            "Weekend",
            "Electronics",
            "Garden",
            "Kids"
        };
        static readonly string[] ProductTitles =
        {
            "Everyday canvas tote",
            "Lightweight travel backpack with adjustable shoulder straps and multiple organizer pockets",
            "Ceramic mug",
            "Soft cotton throw blanket for the living room, woven texture, machine washable and available in several colors",
            "Wireless over-ear headphones with comfortable cushions",
            "Minimal desk lamp with a flexible neck and warm light for reading and working",
            "Insulated water bottle",
            "Compact weekend organizer for your charging cables, camera accessories and other daily essentials"
        };
        static readonly string[] Prices =
        {
            "$19.90",
            "$129.00",
            "$38.50",
            "$65.00",
            "$89.90"
        };
        static readonly string[] ImageLabels =
        {
            "EVERYDAY",
            "TRAVEL",
            "HOME",
            "COMFORT",
            "AUDIO",
            "WORK",
            "OUTDOOR",
            "WEEKEND"
        };
        static readonly float[] ImageRatios =
        {
            1.25f,
            .85f,
            1.4f,
            1,
            1.1f,
            .75f,
            1.6f,
            1.2f
        };
    }
}
