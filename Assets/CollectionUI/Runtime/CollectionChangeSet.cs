using System;
using System.Collections.Generic;

namespace CollectionUI
{
    /// <summary>按添加顺序执行的增量事务。位置索引针对该步骤的当前状态；移动目标索引针对源元素移除后的列表。
    /// 添加操作时即复制元数据；Data 保留业务对象引用。更新必须保留原稳定 ID。</summary>
    public sealed class CollectionChangeSet
    {
        readonly List<Action<Transaction>> operations = new List<Action<Transaction>>();
        /// <summary>当前操作数；空事务不触发布局。</summary>
        public int Count => operations.Count;

        /// <summary>将条目插入指定分区的位置（允许等于当前数量）。</summary>
        public CollectionChangeSet InsertItem(string sectionId, int index, CollectionItem item)
        {
            var copy = CopyItem(item);
            operations.Add(transaction => transaction.InsertItem(sectionId, index, copy.Copy()));
            return this;
        }

        /// <summary>删除全局稳定 ID 对应的条目；未知 ID 导致整个事务失败。</summary>
        public CollectionChangeSet RemoveItem(string itemId)
        {
            operations.Add(transaction => transaction.RemoveItem(itemId));
            return this;
        }

        /// <summary>移动条目，支持跨分区；目标索引为移除源条目后的索引。</summary>
        public CollectionChangeSet MoveItem(string itemId, string destinationSectionId, int index)
        {
            operations.Add(transaction => transaction.MoveItem(itemId, destinationSectionId, index));
            return this;
        }

        /// <summary>替换条目元数据并强制重测和重绑；无需递增 Version，replacement.Id 必须与 itemId 一致。</summary>
        public CollectionChangeSet UpdateItem(string itemId, CollectionItem replacement)
        {
            var copy = CopyItem(replacement);
            if (copy.Id != itemId)
                throw new ArgumentException("Updates must preserve stable item IDs.");
            operations.Add(transaction => transaction.UpdateItem(itemId, copy.Copy()));
            return this;
        }

        /// <summary>插入完整分区，包括分组头尾、布局配置及普通条目。</summary>
        public CollectionChangeSet InsertSection(int index, CollectionSection section)
        {
            var copy = CopySection(section);
            operations.Add(transaction => transaction.InsertSection(index, CopySection(copy)));
            return this;
        }

        /// <summary>删除分区及其中全部元素。</summary>
        public CollectionChangeSet RemoveSection(string sectionId)
        {
            operations.Add(transaction => transaction.RemoveSection(sectionId));
            return this;
        }

        /// <summary>移动分区；目标索引针对移除该分区后的列表。</summary>
        public CollectionChangeSet MoveSection(string sectionId, int index)
        {
            operations.Add(transaction => transaction.MoveSection(sectionId, index));
            return this;
        }

        /// <summary>替换整个分区并强制重测和重绑其中所有元素；无需递增 Version，replacement.Id 必须保持不变。</summary>
        public CollectionChangeSet UpdateSection(string sectionId, CollectionSection replacement)
        {
            var copy = CopySection(replacement);
            if (copy.Id != sectionId)
                throw new ArgumentException("Updates must preserve stable section IDs.");
            operations.Add(transaction => transaction.UpdateSection(sectionId, CopySection(copy)));
            return this;
        }

        internal CollectionSnapshot Apply(CollectionSnapshot source, out HashSet<string> forced)
        {
            var transaction = new Transaction(source.CopyForTransaction());
            forced = transaction.Forced;
            foreach (var operation in operations)
                operation(transaction);
            return transaction.Data;
        }

        // 索引仅属于本次候选快照，失败时整体丢弃，不会污染已发布状态。
        // 更新只查索引；插入、删除、移动仍有 List 移位及受影响后缀索引的线性成本。
        sealed class Transaction
        {
            struct ItemLocation
            {
                public CollectionSection Section;
                public int Index;
            }

            readonly Dictionary<string, int> sections = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, ItemLocation> items = new Dictionary<string, ItemLocation>(StringComparer.Ordinal);
            public readonly CollectionSnapshot Data;
            public readonly HashSet<string> Forced = new HashSet<string>(StringComparer.Ordinal);
            public Transaction(CollectionSnapshot data)
            {
                Data = data;
                ValidateIdentities(data);
                ReindexSections(0);
                foreach (var section in data.Sections)
                    ReindexItems(section, 0);
            }

            int SectionIndex(string id)
            {
                if (id != null && sections.TryGetValue(id, out int index))
                    return index;
                throw new ArgumentException("Unknown section ID: " + id);
            }

            ItemLocation Locate(string id)
            {
                if (id != null && items.TryGetValue(id, out var location))
                    return location;
                throw new ArgumentException("Unknown item ID: " + id);
            }

            void ReindexSections(int start)
            {
                for (int i = start; i < Data.Sections.Count; i++)
                    sections[Data.Sections[i].Id] = i;
            }

            void ReindexItems(CollectionSection section, int start)
            {
                for (int i = start; i < section.Items.Count; i++)
                    items[section.Items[i].Id] = new ItemLocation
                    {
                        Section = section,
                        Index = i
                    };
            }

            void RequireUnusedItem(string id)
            {
                if (items.ContainsKey(id))
                    throw new ArgumentException("Item IDs must be globally unique: " + id);
            }

            void ForceItem(CollectionSection section, CollectionItem item, ElementKind kind)
            {
                // 内部候选快照不再额外复制，因此新元数据在事务中补齐测量键。
                item.LayoutKey = LayoutEntry.MakeKey(kind, section.Id, item.Id);
                Forced.Add(item.LayoutKey);
            }

            void RegisterSection(CollectionSection section)
            {
                foreach (var item in section.Items)
                    RequireUnusedItem(item.Id);
                ReindexItems(section, 0);
                if (section.Header != null)
                    ForceItem(section, section.Header, ElementKind.Header);
                if (section.Footer != null)
                    ForceItem(section, section.Footer, ElementKind.Footer);
                foreach (var item in section.Items)
                    ForceItem(section, item, ElementKind.Item);
            }

            void UnregisterItems(CollectionSection section)
            {
                foreach (var item in section.Items)
                    items.Remove(item.Id);
            }

            public void InsertItem(string sectionId, int index, CollectionItem item)
            {
                var section = Data.Sections[SectionIndex(sectionId)];
                RequireUnusedItem(item.Id);
                section.Items.Insert(index, item);
                ReindexItems(section, index);
                ForceItem(section, item, ElementKind.Item);
            }

            public void RemoveItem(string itemId)
            {
                var location = Locate(itemId);
                location.Section.Items.RemoveAt(location.Index);
                items.Remove(itemId);
                ReindexItems(location.Section, location.Index);
            }

            public void MoveItem(string itemId, string destinationSectionId, int index)
            {
                var location = Locate(itemId);
                var destination = Data.Sections[SectionIndex(destinationSectionId)];
                var item = location.Section.Items[location.Index];
                location.Section.Items.RemoveAt(location.Index);
                destination.Items.Insert(index, item);
                if (location.Section == destination)
                    ReindexItems(destination, Math.Min(location.Index, index));
                else
                {
                    ReindexItems(location.Section, location.Index);
                    ReindexItems(destination, index);
                }
            }

            public void UpdateItem(string itemId, CollectionItem item)
            {
                var location = Locate(itemId);
                location.Section.Items[location.Index] = item;
                ForceItem(location.Section, item, ElementKind.Item);
            }

            public void InsertSection(int index, CollectionSection section)
            {
                if (sections.ContainsKey(section.Id))
                    throw new ArgumentException("Section IDs must be nonempty and unique.");
                RegisterSection(section);
                Data.Sections.Insert(index, section);
                ReindexSections(index);
            }

            public void RemoveSection(string sectionId)
            {
                int index = SectionIndex(sectionId);
                UnregisterItems(Data.Sections[index]);
                Data.Sections.RemoveAt(index);
                sections.Remove(sectionId);
                ReindexSections(index);
            }

            public void MoveSection(string sectionId, int index)
            {
                int from = SectionIndex(sectionId);
                var section = Data.Sections[from];
                Data.Sections.RemoveAt(from);
                Data.Sections.Insert(index, section);
                ReindexSections(Math.Min(from, index));
            }

            public void UpdateSection(string sectionId, CollectionSection replacement)
            {
                int index = SectionIndex(sectionId);
                UnregisterItems(Data.Sections[index]);
                RegisterSection(replacement);
                Data.Sections[index] = replacement;
            }
        }

        // 即使使用自定义布局，也保证稳定标识与模板标识合法；公开快照完整验证一次；事务使用增量索引逐步拒绝临时重复 ID。
        internal static void ValidateIdentities(CollectionSnapshot data)
        {
            var sections = new HashSet<string>(StringComparer.Ordinal);
            var items = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in data.Sections)
            {
                if (string.IsNullOrEmpty(section.Id) || !sections.Add(section.Id))
                    throw new ArgumentException("Section IDs must be nonempty and unique.");
                if (section.Header != null)
                    ValidateItem(section.Header);
                if (section.Footer != null)
                    ValidateItem(section.Footer);
                foreach (var item in section.Items)
                {
                    ValidateItem(item);
                    if (!items.Add(item.Id))
                        throw new ArgumentException("Item IDs must be globally unique: " + item.Id);
                }
            }
        }

        static void ValidateItem(CollectionItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.ReuseIdentifier))
                throw new ArgumentException("Item ID and reuse identifier must be nonempty.");
        }

        static CollectionItem CopyItem(CollectionItem item)
        {
            ValidateItem(item);
            return item.Copy();
        }

        static CollectionSection CopySection(CollectionSection section)
        {
            var data = new CollectionSnapshot();
            data.Sections.Add(section);
            var copy = data.Copy();
            ValidateIdentities(copy);
            return copy.Sections[0];
        }
    }
}
