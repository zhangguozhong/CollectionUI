using System;
using System.Collections.Generic;
using UnityEngine;

namespace CollectionUI
{
    /// <summary>可选分组吸顶。独立叠层复用业务页眉模板，最多保留四个实例，不改变内容布局或 ScrollRect 配置。</summary>
    [RequireComponent(typeof(CollectionView))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class CollectionStickyHeaders : MonoBehaviour
    {
        [SerializeField, Tooltip("启用分组吸顶：滚过页眉后固定到视口顶部，下一分区将它推出。Expanded 模式不生成叠层。")]
        bool stickyEnabled = true;
        public bool StickyEnabled
        {
            get => stickyEnabled;
            set
            {
                stickyEnabled = value;
                if (!value)
                    ClearPresentation();
            }
        }

        sealed class Section
        {
            internal string Id;
            internal float Start;
            internal LayoutEntry Header;
        }

        struct PooledHeader
        {
            internal string Template;
            internal CollectionCell Cell;
        }

        const int MaxInstances = 4;
        readonly List<Section> sections = new List<Section>();
        readonly List<PooledHeader> pool = new List<PooledHeader>();
        CollectionView view;
        LayoutResult indexedLayout;
        RectTransform overlay;
        CollectionCell current;
        LayoutEntry bound;
        long boundRevision;
        string boundKey;
        string currentTemplate;
        void Awake() => view = GetComponent<CollectionView>();
        void LateUpdate()
        {
            if (!stickyEnabled || view == null || !view.isActiveAndEnabled || view.Layout == null || view.IsExpandedPresentation)
            {
                ClearPresentation();
                return;
            }

            var layout = view.Layout;
            if (!ReferenceEquals(indexedLayout, layout))
                IndexSections(layout);
            float top = view.PresentationContent.anchoredPosition.y;
            int lo = 0, hi = sections.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sections[mid].Start <= top)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            int index = lo - 1;
            var header = index >= 0 ? sections[index].Header : null;
            if (header == null || top < header.Rect.yMin)
            {
                ReturnCurrent();
                return;
            }

            float displacement = index + 1 < sections.Count ? Mathf.Min(0, sections[index + 1].Start - top - header.Rect.height) : 0;
            Show(header, displacement);
        }

        void IndexSections(LayoutResult layout)
        {
            sections.Clear();
            if (layout.Sections.Count != 0)
            {
                // 内置结果已有分区边界与起始元素；分页追加后只访问分区，不扫描所有旧条目。
                foreach (var pair in layout.Sections)
                {
                    var geometry = pair.Value;
                    var first = geometry.Count > 0 ? layout.Entries[geometry.FirstEntry] : null;
                    sections.Add(new Section { Id = pair.Key, Start = geometry.Start, Header = first != null && first.Kind == ElementKind.Header ? first : null });
                }
            }
            else
            {
                // 自定义布局没有分区元数据，仍从全部条目推导，不能假设页眉位于 Entries[0]。
                var byId = new Dictionary<string, Section>(StringComparer.Ordinal);
                foreach (var entry in layout.Entries)
                {
                    if (!byId.TryGetValue(entry.SectionId, out var section))
                    {
                        section = new Section
                        {
                            Id = entry.SectionId,
                            Start = entry.Rect.yMin
                        };
                        byId.Add(section.Id, section);
                        sections.Add(section);
                    }
                    else
                        section.Start = Mathf.Min(section.Start, entry.Rect.yMin);
                    if (entry.Kind == ElementKind.Header && (section.Header == null || entry.Rect.yMin < section.Header.Rect.yMin))
                        section.Header = entry;
                }
            }

            sections.Sort((a, b) => a.Start.CompareTo(b.Start));
            indexedLayout = layout;
        }

        void EnsureOverlay()
        {
            if (overlay != null)
                return;
            overlay = (RectTransform)new GameObject("StickyHeaders", typeof(RectTransform)).transform;
            overlay.SetParent(view.ScrollRect.viewport, false);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            // 与 Content 同属 Viewport，继承同一裁剪及安全区。模板保持原射线策略；叠层自身不接收射线。
            overlay.SetAsLastSibling();
        }

        void Show(LayoutEntry entry, float displacement)
        {
            if (current != null && currentTemplate != entry.Item.ReuseIdentifier)
                ReturnCurrent();
            EnsureOverlay();
            if (current == null)
            {
                currentTemplate = entry.Item.ReuseIdentifier;
                for (int i = pool.Count - 1; i >= 0; i--)
                    if (pool[i].Template == currentTemplate)
                    {
                        current = pool[i].Cell;
                        pool.RemoveAt(i);
                        break;
                    }

                if (current == null)
                {
                    if (!view.TryGetTemplate(currentTemplate, out var prefab))
                        return;
                    if (pool.Count >= MaxInstances)
                    {
                        Destroy(pool[0].Cell.gameObject);
                        pool.RemoveAt(0);
                    }

                    current = Instantiate(prefab, overlay);
                    current.gameObject.SetActive(false);
                }
            }

            var rt = (RectTransform)current.transform;
            bool changedEntry = !ReferenceEquals(bound, entry);
            if (changedEntry)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.sizeDelta = entry.Rect.size;
            }

            var position = new Vector2(entry.Rect.xMin, -displacement);
            if (rt.anchoredPosition != position)
                rt.anchoredPosition = position;
            // 同一条目复用身份与绑定修订，逐帧推挤不重新绑定。
            string key = changedEntry ? entry.Key : boundKey;
            long revision = view.GetPresentationBindingRevision(key);
            bool needsBind = bound == null || boundKey != key || boundRevision != revision;
            // 委托只在绑定/布局变化时创建，逐帧推挤没有分配或重新绑定。
            var presented = current;
            if (needsBind || changedEntry)
                UpdateBinding(entry, needsBind);
            // 业务绑定可同步禁用或重置吸顶；不要把已经释放的实例再次记为已绑定。
            if (current == null || current != presented || !stickyEnabled || !isActiveAndEnabled)
                return;
            bound = entry;
            boundRevision = revision;
            boundKey = key;
            if (current != null && !current.gameObject.activeSelf && stickyEnabled && isActiveAndEnabled)
                current.gameObject.SetActive(true);
        }

        void UpdateBinding(LayoutEntry entry, bool needsBind)
        {
            var cell = current;
            view.WithPresentationCallback(() =>
            {
                if (needsBind)
                    cell.Bind(entry);
                else
                    cell.UpdateLayout(entry);
            });
        }

        void ReturnCurrent()
        {
            if (current == null)
                return;
            var released = current;
            current = null;
            bound = null;
            if (view != null)
                view.WithPresentationCallback(released.Release);
            else
                released.Release();
            if (released == null)
                return;
            released.gameObject.SetActive(false);
            pool.Add(new PooledHeader { Template = currentTemplate, Cell = released });
            currentTemplate = null;
        }

        void ClearPresentation()
        {
            ReturnCurrent();
            pool.Clear();
            if (overlay != null)
            {
                overlay.gameObject.SetActive(false);
                Destroy(overlay.gameObject);
                overlay = null;
            }

            indexedLayout = null;
            sections.Clear();
        }

        void OnDisable() => ClearPresentation();
        void OnDestroy() => ClearPresentation();
    }
}
