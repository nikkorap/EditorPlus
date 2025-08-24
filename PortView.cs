// PortView.cs
using UnityEngine;
using UnityEngine.EventSystems;

namespace EditorPlus
{
    public enum PortKind { Input, Output }

    [RequireComponent(typeof(RectTransform))]
    public sealed class PortView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler  
    {

        public void OnPointerEnter(PointerEventData e)
        {
            node?.NotifyHover(true);
            node?.NotifyPortHover(kind, true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (node == null) return;
            var nodeRT = node.GetComponent<RectTransform>();
            var cam = node.GetComponentInParent<Canvas>()?.worldCamera;
            bool stillOverNode = RectTransformUtility.RectangleContainsScreenPoint(nodeRT, e.position, cam);

            node.NotifyPortHover(kind, false);
            node.NotifyHover(stillOverNode);
        }

        [SerializeField] private NodeView node;
        [SerializeField] private PortKind kind = PortKind.Input;
        public PortKind Kind => kind;
        public void Init(NodeView n, PortKind k) { node = n; kind = k; }


        public void OnBeginDrag(PointerEventData e)
        {
            if (!gameObject.activeInHierarchy) return;
            if (e.button != PointerEventData.InputButton.Left || node == null) return;
            node.BeginLinkDrag(kind, e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (node == null) return;
            node.DragLinkTo(e.position);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (node == null) return;
            node.EndLinkDrag(e.position);
        }
    }
}
