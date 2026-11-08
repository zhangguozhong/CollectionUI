using System;
using System.Collections.Generic;
using UnityEngine;

namespace CollectionUI
{
    /// <summary>独立于已实例化视图计算纯几何布局，内容坐标的 Y 轴向下为正。</summary>
    public interface ICollectionLayout
    {
        /// <summary>根据快照和视口宽度生成几何信息；测量回调处理动态尺寸，历史结果及脏分区集合供增量复用。</summary>
        LayoutResult Build(CollectionSnapshot snapshot, float viewportWidth, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, ISet<string> dirtySections = null);
    }

    /// <summary>内置布局入口，支持分区、等宽列、天际线及保持输入顺序的流式排列。</summary>
    public sealed class DefaultCollectionLayout : ICollectionLayout
    {
        /// <summary>使用内置分区算法构建布局，参数及复用规则与 SectionedLayout.Build 一致。</summary>
        public LayoutResult Build(CollectionSnapshot snapshot, float viewportWidth, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, ISet<string> dirtySections = null) => SectionedLayout.Build(snapshot, viewportWidth, measure, previous, dirtySections);
        // 私有候选数据已经由视图复制或事务生成；避免内置布局再复制一遍元数据。
        internal LayoutResult BuildOwned(CollectionSnapshot snapshot, float viewportWidth, Func<CollectionItem, float, Vector2> measure = null, LayoutResult previous = null, ISet<string> dirtySections = null) => SectionedLayout.BuildOwned(snapshot, viewportWidth, measure, previous, dirtySections);
    }
}
