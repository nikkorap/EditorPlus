// NodeView.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace EditorPlus
{
    public enum NodeKind { Objective, Outcome }

    public sealed class NodeView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {

        [Header("UI refs")]
        [SerializeField] private Text title;
        [SerializeField] private Text subtitle;
        [SerializeField] private Image bg;

        [Header("Ports")]
        [SerializeField] private RectTransform leftPort;
        [SerializeField] private RectTransform rightPort;

        [Header("Layout")]
        [SerializeField] private Vector2 layoutPadding = new Vector2(12f, 8f);
        [SerializeField] private Image border;
        [SerializeField] private Image factionCorner;

        [SerializeField] private Color factionA = new(0.31f, 0.27f, 0.90f, 1f);
        [SerializeField] private Color factionB = new(0.96f, 0.62f, 0.04f, 1f);
        [SerializeField] private Color objectiveFill = new(0.23f, 0.27f, 0.36f, 0.95f);
        [SerializeField] private Color outcomeFill = new(0.18f, 0.29f, 0.22f, 0.95f);

        [SerializeField] private Color highlightBorder = Color.white;
        [SerializeField] private Color hiddenBorder = new(0.42f, 0.45f, 0.50f, 1f);
        [SerializeField] private Color normalBorder = new(1f, 1f, 1f, 0.0f);
        [SerializeField] private Color unattachedBorder = new(0.6f, 0.6f, 0.6f, 0.6f);

        private GraphView _graph;
        private Vector2 _pressScreenPos;
        private bool _beganDrag;
        private int _pressPointerId = -999;
        private bool _isHidden;
        private bool _isUnattached;
        private string _factionName;
        public Vector2 LayoutPadding => layoutPadding;
        public void OnPointerEnter(PointerEventData e)
        {
            if (_graph != null && !_dragging)
                _graph.HighlightNeighborhood(this, true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (_graph == null || _dragging) return;

            var rt = (RectTransform)transform;
            var cam = GetComponentInParent<Canvas>()?.worldCamera;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, e.position, cam))
                _graph.ClearHighlights();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (OverPort(e.position)) return;

            if (e.button == PointerEventData.InputButton.Right)
            {
                _graph?.ToggleFocusRebuild(this);
                return;
            }

            if (e.button != PointerEventData.InputButton.Left) return;

            var threshold = EventSystem.current ? EventSystem.current.pixelDragThreshold : 5;
            if (_beganDrag) return;
            if ((e.position - _pressScreenPos).sqrMagnitude > threshold * threshold) return;

            if (e.clickCount > 1)
            {
                if (Kind == NodeKind.Objective) _graph?.OnEditObjective?.Invoke(Id);
                else _graph?.OnEditOutcome?.Invoke(Id);
            }
            else
            {
                _graph?.ToggleUnitConnections(this);
            }
        }

        public bool TryGetFactionColor(out Color c)
        {
            c = default;
            if (Kind != NodeKind.Objective || string.IsNullOrEmpty(_factionName)) return false;

            if (string.Equals(_factionName, "Boscali", StringComparison.OrdinalIgnoreCase)) { c = factionA; return true; }
            if (string.Equals(_factionName, "Primeva", StringComparison.OrdinalIgnoreCase)) { c = factionB; return true; }
            return false;
        }
        public void ApplyFaction(string factionName)
        {
            _factionName = string.IsNullOrWhiteSpace(factionName) ? null : factionName.Trim();

            if (!factionCorner) return;
            bool show = Kind == NodeKind.Objective && !string.IsNullOrEmpty(_factionName);
            factionCorner.enabled = show;
            if (!show) return;

            var key = string.IsNullOrWhiteSpace(_factionName) ? "" : _factionName.Trim().ToLowerInvariant();
            if (key.Contains("b")) factionCorner.color = factionA;
            else if (key.Contains("p")) factionCorner.color = factionB;
            else factionCorner.enabled = false;
        }

        public Vector2 GetLayoutSize()
        {
            var rt = RT;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            var s = rt.rect.size;
            return s + new Vector2(layoutPadding.x * 2f, layoutPadding.y * 2f);
        }

        public void SetOutputPortVisible(bool visible)
        {
            if (rightPort) rightPort.gameObject.SetActive(visible);
        }

        public void SetHighlighted(bool on)
        {
            if (!border) return;
            border.enabled = on || _isHidden || _isUnattached;
            border.color = on ? highlightBorder
                              : _isHidden ? hiddenBorder
                              : _isUnattached ? unattachedBorder
                              : normalBorder;
        }

        public void NotifyHover(bool on) => _graph?.HighlightNeighborhood(this, on);
        public void NotifyPortHover(PortKind portKind, bool on) => _graph?.OnPortHoverChanged(this, portKind, on);
        public void BeginLinkDrag(PortKind fromKind, PointerEventData e) => _graph?.BeginLinkDrag(this, fromKind, e);
        public void DragLinkTo(Vector2 screenPos) => _graph?.DragLinkTo(screenPos);
        public void EndLinkDrag(Vector2 screenPos) => _graph?.EndLinkDrag(screenPos);
        private void EnsurePort(RectTransform rt, PortKind kind)
        {
            if (!rt) return;

            var img = rt.GetComponent<Image>();
            img.raycastTarget = true;
            rt.SetAsLastSibling();
            var pv = rt.GetComponent<PortView>();
            pv.Init(this, kind);
        }

        public void InitGraph(RectTransform edgeLayer, RectTransform content, GraphView graph)
        {
            _edgeLayer = edgeLayer;
            _content = content;
            _graph = graph;

            _canvas = GetComponentInParent<Canvas>();
            _cam = _canvas ? _canvas.worldCamera : null;
            EnsurePort(leftPort, PortKind.Input);
            EnsurePort(rightPort, PortKind.Output);
        }

        internal void AddOutgoing(Connection c) => _outgoing.Add(c);

        public void RemoveConnectionTo(NodeView target)
        {
            for (int i = _outgoing.Count - 1; i >= 0; i--)
            {
                var c = _outgoing[i];
                if (c.To != target) continue;
                _outgoing.RemoveAt(i);
            }
        }

        public void DisconnectAll()
        {
            _outgoing.Clear();
        }

        public RectTransform LeftPort => leftPort;
        public string Id { get; private set; }
        public NodeKind Kind { get; private set; }
        public RectTransform RT => (RectTransform)transform;
        public RectTransform RightPort => rightPort;

        private RectTransform _edgeLayer;
        private RectTransform _content;
        private Canvas _canvas;
        private Camera _cam;

        private bool _dragging;
        private Vector2 _dragStartLocalInContent;
        private Vector2 _dragStartAnchored;

        private readonly List<Connection> _outgoing = new();

        public void BindObjective(string id, string uniqueName, string displayName,
                                  string typeName, bool hidden, int outcomeCount)
        {
            Id = id; Kind = NodeKind.Objective;
            if (title) title.text = typeName;

            string sub = string.IsNullOrEmpty(displayName) ? uniqueName : $"{displayName} [{uniqueName}]";
            if (subtitle) subtitle.text = sub;

            _isHidden = hidden;
            _isUnattached = false;

            if (bg) bg.color = objectiveFill;       
            if (border) { border.enabled = hidden; border.color = hidden ? hiddenBorder : normalBorder; }

            leftPort?.SetAsLastSibling();
            rightPort?.SetAsLastSibling();
        }

        public void BindOutcome(string id, string uniqueName, string typeName, int usedByCount)
        {
            Id = id; Kind = NodeKind.Outcome;
            if (title) title.text = typeName;
            if (subtitle) subtitle.text = uniqueName;

            _isHidden = false;
            _isUnattached = (usedByCount == 0);

            if (bg) bg.color = outcomeFill;          
            if (border) { border.enabled = _isUnattached; border.color = _isUnattached ? unattachedBorder : normalBorder; }
            _factionName = null;
            if (factionCorner) factionCorner.enabled = false;
        }

        public void OnPointerDown(PointerEventData e)
        {
            RT.SetAsLastSibling();
            _pressScreenPos = e.position;
            _beganDrag = false;
            _pressPointerId = e.pointerId;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_content == null) return;

            if (OverPort(e.pressPosition)) return;

            _dragging = true;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, e.position, _cam, out _dragStartLocalInContent);
            _dragStartAnchored = RT.anchoredPosition;
            if (e.pointerId == _pressPointerId)
                _beganDrag = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging || _content == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, e.position, _cam, out var nowLocal);
            Vector2 delta = nowLocal - _dragStartLocalInContent;

            var newPos = _dragStartAnchored + delta;
            RT.anchoredPosition = newPos;
        }

        public void OnEndDrag(PointerEventData e)
        {
            _dragging = false;
            if (e.pointerId == _pressPointerId)
                _beganDrag = false;
        }

        private bool OverPort(Vector2 screenPos)
        {
            if (!leftPort && !rightPort) return false;
            if (leftPort && RectTransformUtility.RectangleContainsScreenPoint(leftPort, screenPos, _cam)) return true;
            if (rightPort && RectTransformUtility.RectangleContainsScreenPoint(rightPort, screenPos, _cam)) return true;
            return false;
        }
    }

    public sealed class Connection
    {
        public NodeView From;
        public NodeView To;
        public EdgeView Edge;
        public Connection(NodeView from, NodeView to, EdgeView edge)
        {
            From = from;
            To = to;
            Edge = edge;
        }
    }
}