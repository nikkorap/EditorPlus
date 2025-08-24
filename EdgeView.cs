// EdgeView.cs
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EditorPlus
{
    [RequireComponent(typeof(RectTransform)), RequireComponent(typeof(Image))]
    public sealed class EdgeView : MonoBehaviour, IPointerClickHandler
    {
        [Serializable]
        public struct Appearance
        {
            public Color normalTint;
            public Color highlightTint;
            public float normalThickness;
            public float highlightThickness;
            public float minHitThickness;
            public Sprite sprite;

            public static Appearance Default => new()
            {
                normalTint = new Color(1f, 1f, 1f, 0.70f),
                highlightTint = new Color(1f, 1f, 1f, 1f),
                normalThickness = 2f,
                highlightThickness = 3f,
                minHitThickness = 14f,
                sprite = null
            };
        }
        [SerializeField] private float minScreenThicknessPx = 1f;
        public Action<EdgeView> OnRequestDelete;
        public Action<EdgeView, bool> OnHoverChanged;

        private RectTransform _content, _rt, _hitPadRT;
        private RectTransform _begin, _end;
        private Image _img;

        private Appearance _appearance = Appearance.Default;
        private bool _highlighted;

        private float CurrentThickness => _highlighted ? _appearance.highlightThickness : _appearance.normalThickness;
        private Color CurrentTint => _highlighted ? _appearance.highlightTint : _appearance.normalTint;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);

            _img = GetComponent<Image>();
            if (_img) _img.raycastTarget = false;

            var hit = new GameObject("HitPad", typeof(RectTransform), typeof(Image));
            hit.transform.SetParent(transform, false);
            _hitPadRT = (RectTransform)hit.transform;

            var hitImg = hit.GetComponent<Image>();
            hitImg.color = new Color(1, 1, 1, 0f);
            hitImg.raycastTarget = true;

            var relay = hit.AddComponent<HitPadRelay>();
            relay.owner = this;
        }

        public void Init(RectTransform content, RectTransform begin, RectTransform end)
        {
            _content = content;
            _begin = begin;
            _end = end;
            RefreshVisual();
        }

        public void ApplyAppearance(in Appearance appearance, bool highlighted)
        {
            _appearance = appearance;
            _highlighted = highlighted;
            RefreshVisual();
        }

        public void ApplyAppearance(in Appearance appearance)
        {
            _appearance = appearance;
            RefreshVisual();
        }

        public void ApplyAppearance(bool highlighted)
        {
            _highlighted = highlighted;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (!_img) _img = GetComponent<Image>();

            if (_img)
            {
                _img.color = CurrentTint;
                _img.sprite = _appearance.sprite;
                _img.type = Image.Type.Simple;
                _img.preserveAspect = false;
                _img.raycastTarget = false;
            }

            var s = _rt.sizeDelta;
            s.y = CurrentThickness;
            _rt.sizeDelta = s;
        }


        public void OnPointerEnter(PointerEventData e) => OnHoverChanged?.Invoke(this, true);
        public void OnPointerExit(PointerEventData e) => OnHoverChanged?.Invoke(this, false);
        public void OnPointerClick(PointerEventData e) {}
        

        void LateUpdate()
        {
            if (!_begin || !_end || !_content) return;

            var a3 = _content.InverseTransformPoint(_begin.TransformPoint(_begin.rect.center));
            var b3 = _content.InverseTransformPoint(_end.TransformPoint(_end.rect.center));
            var A = new Vector2(a3.x, a3.y);
            var B = new Vector2(b3.x, b3.y);

            var d = B - A;
            var len = d.magnitude;
            if (len < 0.01f) { _rt.sizeDelta = Vector2.zero; return; }

            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float s = _content ? _content.localScale.x : 1f;

            float visThickness = Mathf.Max(CurrentThickness, minScreenThicknessPx / Mathf.Max(1e-4f, s));

            Vector2 centerContent = A + d * 0.5f;
            Vector2 vpCenter = _content.anchoredPosition + centerContent * s;
            vpCenter = new Vector2(Mathf.Round(vpCenter.x), Mathf.Round(vpCenter.y));

            _rt.sizeDelta = new Vector2(len, visThickness);
            _rt.anchoredPosition = (vpCenter - _content.anchoredPosition) / s;
            _rt.localRotation = Quaternion.Euler(0, 0, angle);

            if (_hitPadRT)
            {
                float hitVis = Mathf.Max(_appearance.minHitThickness / Mathf.Max(1e-4f, s), visThickness);
                _hitPadRT.sizeDelta = new Vector2(len, hitVis);
                _hitPadRT.anchoredPosition = Vector2.zero;
                _hitPadRT.localRotation = Quaternion.identity;
            }

        }

        private sealed class HitPadRelay :
            MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
        {
            public EdgeView owner;
            public void OnPointerEnter(PointerEventData e) => owner?.OnPointerEnter(e);
            public void OnPointerExit(PointerEventData e) => owner?.OnPointerExit(e);
            public void OnPointerDown(PointerEventData e) {}
            public void OnPointerClick(PointerEventData e) => owner?.OnPointerClick(e);
        }
    }
}
