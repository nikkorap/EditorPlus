// UIPanZoomMouseOnly.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EditorPlus
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class UIPanZoomMouseOnly : MonoBehaviour,
        IScrollHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("Refs")]
        [SerializeField] private RectTransform viewport;  
        [SerializeField] private RectTransform content;   

        [Header("Zoom")]
        [SerializeField][Min(0.01f)] private float minScale = 0.3f;
        [SerializeField][Min(0.01f)] private float maxScale = 3f;
        [SerializeField] private float zoomSpeed = 0.1f;

        [Header("Pan")]
        [SerializeField] private PointerEventData.InputButton panButton = PointerEventData.InputButton.Middle;

        private Canvas _canvas;
        private bool _panning;
        private int _panPointerId = int.MinValue;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            AutoWireIfNull();
            EnsureViewportRaycastable();
        }

        private void OnValidate()
        {
            AutoWireIfNull();
            EnsureViewportRaycastable();
            if (minScale > maxScale) maxScale = minScale;
        }


        public void OnScroll(PointerEventData eventData)
        {
            if (viewport == null || content == null) return;

            float d = Mathf.Abs(eventData.scrollDelta.y) > 0.01f
                ? eventData.scrollDelta.y
                : eventData.scrollDelta.x;
            if (Mathf.Abs(d) < 0.01f) return;

            float factor = 1f + d * zoomSpeed;
            ZoomAtScreenPoint(eventData.position, factor);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (viewport == null || content == null) return;
            if (eventData.button != panButton) return;

            _panning = true;
            _panPointerId = eventData.pointerId;
            eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_panning || eventData.pointerId != _panPointerId) return;
            if (viewport == null || content == null) return;

            float scaleFactor = (_canvas != null && _canvas.scaleFactor > 0f) ? _canvas.scaleFactor : 1f;
            content.anchoredPosition += eventData.delta / scaleFactor;
            eventData.Use();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_panning || eventData.pointerId != _panPointerId) return;
            _panning = false;
            _panPointerId = int.MinValue;
            eventData.Use();
        }


        private void AutoWireIfNull()
        {
            if (viewport == null) viewport = transform as RectTransform;

            if (content == null && viewport != null)
            {
                var t = viewport.Find("Content");
                if (t != null) content = t as RectTransform;
                if (content == null)
                {
                    for (int i = 0; i < viewport.childCount; i++)
                    {
                        if (viewport.GetChild(i) is RectTransform rt) { content = rt; break; }
                    }
                }
            }
        }

        private void EnsureViewportRaycastable()
        {
            if (viewport == null) return;
            var img = viewport.GetComponent<Image>();
            if (img == null) img = viewport.gameObject.AddComponent<Image>();
            img.raycastTarget = true;
            if (img.sprite == null) img.color = new Color(0, 0, 0, 0); 
        }

        private void ZoomAtScreenPoint(Vector2 screenPoint, float factor)
        {
            float current = content.localScale.x;
            float target = Mathf.Clamp(current * factor, minScale, maxScale);
            float applied = (current != 0f) ? (target / current) : 1f;
            if (Mathf.Approximately(applied, 1f)) return;

            var cam = _canvas != null ? _canvas.worldCamera : null;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, screenPoint, cam, out var vpLocal);

            Vector2 before = (vpLocal - content.anchoredPosition) / current;

            content.localScale = new Vector3(target, target, 1f);

            Vector2 after = before * target;
            content.anchoredPosition = vpLocal - after;
        }
    }
}
