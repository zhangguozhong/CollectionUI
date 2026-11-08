using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CollectionUI
{
    public sealed partial class CollectionView
    {
        // 只接受最后一个已显示非空分区的纯尾部追加。任何可能使旧尺寸或旧位置失效的条件
        // 都交回普通提交路径；业务不需要改用另一套追加 API。
        bool TryApplyTailAppend(CollectionChangeSet changes)
        {
            if (snapshot == null || Layout == null || snapshot.Sections.Count == 0 || !(layoutEngine is DefaultCollectionLayout) || sizesDirty || dirtyItems.Count != 0)
                return false;
            int sectionIndex = snapshot.Sections.Count - 1;
            var previousSection = snapshot.Sections[sectionIndex];
            if (previousSection.Items.Count == 0 || previousSection.Layout.Mode == LayoutMode.Skyline || !Layout.Sections.ContainsKey(previousSection.Id))
                return false;
            if (!changes.TryGetTailInsertions(previousSection, out var added))
                return false;
            using var applyScope = ApplyMarker.Auto();
            applying = true;
            try
            {
                EnsureInitialized();
                UpdateAncestorLayout();
                float width = AvailableWidth;
                if (width != layoutWidth)
                    return false;
                // 全局普通 ID 由已发布结果的索引验证，本页用小集合判重；不重新扫描旧数据。
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in added)
                {
                    if (!ids.Add(item.Id) || Layout.TryGetItem(item.Id, out _))
                        throw new ArgumentException("Item IDs must be globally unique: " + item.Id);
                    if (!prefabs.ContainsKey(item.ReuseIdentifier))
                        throw new ArgumentException("Unregistered template: " + item.ReuseIdentifier);
                    // 新身份已经确定。尺寸回调只读元数据，不能修改 ID 绕过本次增量校验。
                    item.Freeze();
                }

                // 公开模型保留 List API：仅复制目标分区的引用数组，不逐项复制元数据。
                // 其余分区属于组件私有快照，后续普通事务会复制后再修改，可安全共享。
                var section = new CollectionSection
                {
                    Id = previousSection.Id,
                    Header = previousSection.Header,
                    Footer = previousSection.Footer,
                    Layout = previousSection.Layout.Copy(),
                    Items = new List<CollectionItem>(previousSection.Items.Count + added.Count)
                };
                section.Items.AddRange(previousSection.Items);
                section.Items.AddRange(added);
                var candidate = new CollectionSnapshot
                {
                    Sections = new List<CollectionSection>(snapshot.Sections)
                };
                candidate.Sections[sectionIndex] = section;
                // 测量先写入本页暂存缓存；非法尺寸或业务异常时，旧快照、索引与缓存均不改变。
                var stagedSizes = new Dictionary<SizeKey, Vector2>();
                var used = new HashSet<SizeKey>();
                long start = Stopwatch.GetTimestamp();
                LayoutResult next;
                using (LayoutMarker.Auto())
                    next = SectionedLayout.AppendTail(Layout, section, sectionIndex, added, (item, available) => Measure(item, available, stagedSizes, used));
                LastLayoutMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                LayoutBuildCount++;
                var anchor = CaptureAnchor();
                ReconcileActive(next);
                long revision = presentationRevision + 1;
                int firstNewEntry = Layout.Entries.Count - (previousSection.Footer == null ? 0 : 1);
                // 已有条目与页脚只改变几何，不修改展示修订；新增条目才增加修订记录。
                for (int i = firstNewEntry; i < firstNewEntry + added.Count; i++)
                    presentationRevisions.Add(next.Entries[i].Key, revision);
                foreach (var pair in stagedSizes)
                    sizes.Add(pair.Key, pair.Value);
                snapshot = candidate;
                Layout = next;
                presentationRevision = revision;
                layoutWidth = width;
                CommitLayout(anchor);
                return true;
            }
            finally
            {
                applying = false;
            }
        }
    }
}
