using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CollectionUI
{
    public sealed partial class CollectionView : IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        Coroutine sectionNavigation;
        PointerEventData activeDrag, rebasedDrag;
        bool elasticDragStartPending;
        Vector2 dragStartOffset, dragStartPointer;
        /// <summary>按稳定分区 ID 滚动到页眉或第一个普通条目；隐藏/空分区和 Expanded 不操作。
        /// 动画使用不受 timeScale 影响的时间；拖动、重新布局、下一次跳转或禁用立即取消。</summary>
        public void ScrollToSection(string sectionId, bool animated = false, float duration = .25f)
        {
            if (refreshing || applying)
                throw new InvalidOperationException("Scroll outside binding and measurement callbacks.");
            CancelSectionNavigation();
            if (Layout == null || IsExpandedPresentation || !isActiveAndEnabled || sectionId == null)
                return;
            LayoutEntry firstItem = null, header = null;
            foreach (var entry in Layout.Entries)
            {
                if (entry.SectionId != sectionId)
                    continue;
                if (entry.Kind == ElementKind.Header && (header == null || entry.Rect.yMin < header.Rect.yMin))
                    header = entry;
                if (entry.Kind == ElementKind.Item && (firstItem == null || entry.ItemIndex < firstItem.ItemIndex))
                    firstItem = entry;
            }

            var target = header ?? firstItem;
            if (target == null)
                return;
            if (animated && (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0))
                throw new ArgumentOutOfRangeException(nameof(duration));
            GetComponent<CollectionLoader>()?.ReleaseScrollHold();
            scroll.StopMovement();
            float y = Mathf.Clamp(target.Rect.yMin, 0, Mathf.Max(0, Layout.ContentHeight - scroll.viewport.rect.height));
            if (!animated || duration == 0)
            {
                SetOffset(y);
                RefreshVisible();
            }
            else
                sectionNavigation = StartCoroutine(AnimateSectionNavigation(y, duration));
        }

        IEnumerator AnimateSectionNavigation(float target, float duration)
        {
            float start = content.anchoredPosition.y, elapsed = 0;
            while (elapsed < duration)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetOffset(Mathf.LerpUnclamped(start, target, t * t * (3 - 2 * t)));
                RefreshVisible();
            }

            sectionNavigation = null;
        }

        internal void CancelSectionNavigation()
        {
            if (sectionNavigation == null)
                return;
            StopCoroutine(sectionNavigation);
            sectionNavigation = null;
        }

        /// <summary>记录当前指针，仅在布局改变滚动锚点时重设 ScrollRect 的拖动基准。</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null)
            {
                CancelSectionNavigation();
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left || activeDrag != null)
                return;
            CancelSectionNavigation();
            activeDrag = eventData;
            elasticDragStartPending = false;
            if (initialized && scroll != null)
            {
                dragStartOffset = content.anchoredPosition;
                dragStartPointer = eventData.position;
                float bottom = Mathf.Max(0, content.rect.height - scroll.viewport.rect.height);
                elasticDragStartPending = dragStartOffset.y < 0 || dragStartOffset.y > bottom;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || activeDrag == null || activeDrag.pointerId != eventData.pointerId)
                return;
            activeDrag = eventData;
            CancelSectionNavigation();
            if (!elasticDragStartPending)
                return;
            elasticDragStartPending = false;
            if (scroll.movementType != UnityEngine.UI.ScrollRect.MovementType.Elastic)
                return;
            // 用户抓住仍在 loading/回弹的列表时，原生 BeginDrag 也会从已压缩坐标开始。
            // 第一次 Drag 内重建原始基准并立即重放当前位移；不依赖同节点事件组件的排列顺序。
            content.anchoredPosition = dragStartOffset;
            RebaseScrollAfterLayout(scroll.velocity, dragStartPointer);
            scroll.OnDrag(eventData);
            RefreshVisible();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData != null && activeDrag != null && activeDrag.pointerId == eventData.pointerId)
            {
                activeDrag = null;
                elasticDragStartPending = false;
            }
        }

        void RebaseScrollAfterLayout(Vector2 velocity, Vector2? atPointer = null)
        {
            elasticDragStartPending = false;
            if (activeDrag != null && scroll.isActiveAndEnabled)
            {
                Vector2 pointerPosition = atPointer ?? activeDrag.position;
                PointerEventData start = atPointer.HasValue ? RebasedPointer(pointerPosition) : activeDrag;
                if (scroll.movementType == UnityEngine.UI.ScrollRect.MovementType.Elastic)
                {
                    float height = scroll.viewport.rect.height;
                    float bottom = Mathf.Max(0, content.rect.height - height);
                    float y = content.anchoredPosition.y;
                    float displayedStretch = y < 0 ? y : y > bottom ? y - bottom : 0;
                    if (displayedStretch != 0 && height > 0)
                    {
                        // 对 UGUI 的 RubberDelta 反解原始越界量。若直接从压缩后的坐标开始，
                        // 下一次 Drag 会再次压缩，造成列表回跳。只校正指针起点，不临时移动内容。
                        float distance = Mathf.Min(Mathf.Abs(displayedStretch), height * .9999f);
                        float rawStretch = Mathf.Sign(displayedStretch) * distance * height / (.55f * (height - distance));
                        float correction = rawStretch - displayedStretch;
                        var camera = activeDrag.pressEventCamera;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(scroll.viewport, pointerPosition, camera, out var cursor);
                        var correctedStart = RectTransformUtility.WorldToScreenPoint(camera, scroll.viewport.TransformPoint(cursor - new Vector2(0, correction)));
                        start = RebasedPointer(correctedStart);
                    }
                }

                scroll.OnBeginDrag(start);
            }

            // 同步前帧坐标，布局平移不会被算成手指速度；保留原惯性速度。
            scroll.Rebuild(UnityEngine.UI.CanvasUpdate.PostLayout);
            scroll.velocity = velocity;
        }

        PointerEventData RebasedPointer(Vector2 position)
        {
            if (rebasedDrag == null)
                rebasedDrag = new PointerEventData(EventSystem.current);
            rebasedDrag.button = activeDrag.button;
            rebasedDrag.pointerId = activeDrag.pointerId;
            rebasedDrag.pointerPressRaycast = activeDrag.pointerPressRaycast;
            rebasedDrag.position = position;
            return rebasedDrag;
        }

        partial void OnPresentationDisabled()
        {
            activeDrag = null;
            elasticDragStartPending = false;
            CancelSectionNavigation();
        }

        partial void OnPresentationDestroyed()
        {
            activeDrag = null;
            elasticDragStartPending = false;
            CancelSectionNavigation();
        }

        partial void OnLayoutGeometryCommitted() => CancelSectionNavigation();
        // 叠层 Cell 的业务生命周期同样受集合回调重入约束保护。
        internal void WithPresentationCallback(Action action)
        {
            bool previous = refreshing;
            refreshing = true;
            try
            {
                action();
            }
            finally
            {
                refreshing = previous;
            }
        }
    }
}
