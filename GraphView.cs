// GraphView.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EditorPlus
{
    public sealed class GraphView : MonoBehaviour
    {

        private readonly List<Connection> _connections = new();
        private readonly HashSet<NodeView> _hlNodes = new();
        private readonly HashSet<EdgeView> _hlEdges = new();
        private readonly HashSet<string> _focusObjIds = new();
        private readonly HashSet<string> _focusOutIds = new();

        private RectTransform _bgRT; private Image _bgImg;
        private RawImage _minorImg; private RectTransform _minorRT;
        private bool _bgGridVisible = true;
        private Vector2 _lastContentPos;
        private Vector2 _lastContentScale;
        private Vector2 _lastViewportSize;
        private bool _focusOn;
        private string _focusId;
        private bool _focusIsObj;
        private ObjectiveDTO[] _fullObjectives;
        private OutcomeDTO[] _fullOutcomes;
        private LinkDTO[] _fullLinks;
        private bool _building;
        public Func<string, bool> CanOutcomeHaveOutputs;
        private bool _isFocusBuild;
        public Action<string> OnEditObjective;
        public Action<string> OnEditOutcome;
        public Action<string, bool, string, bool> OnLink;
        public Action<string, bool, string, bool> OnUnlink;
        private Camera worldCamera;
        private string _focusAnchorId;
        private bool _focusAnchorIsObj;
        private readonly Dictionary<string, NodeView> _obj = new Dictionary<string, NodeView>();
        private readonly Dictionary<string, NodeView> _out = new Dictionary<string, NodeView>();
        private RectTransform viewport, content, edgeLayer;
        private EdgeView _hoverEdge;
        private NodeView _hoverNode;
        private NodeView _hoverPortNode;
        private PortKind _hoverPortKind;
        [SerializeField] private NodeView nodePrefab;
        [SerializeField] public Sprite edgeFadeSprite;
        [Header("Edge")]
        [SerializeField] public Color edgeTint = new(1f, 1f, 1f, 0.70f);
        [SerializeField] public float edgeThickness = 2f;

        [Header("Highlight Edge")]
        [SerializeField] public Color highlightEdgeTint = new(1f, 1f, 1f, 1f);
        [SerializeField] public float highlightEdgeThickness = 3f;

        [Header("Ghost Edge")]
        [SerializeField] public Color ghostEdgeTint = new(1f, 1f, 1f, 0.40f);
        [SerializeField] public float ghostEdgeThickness = 2f;
        private readonly List<(RectTransform anchor, EdgeView edge, System.Func<Vector3> worldGetter, RectTransform begin)> _unitGhosts = new();
        private NodeView _unitPinnedCenter;
        private bool _unitPinnedOn; public System.Func<string, bool, IEnumerable<System.Func<Vector3>>> QueryUnitWorldPositions;

        [SerializeField] public Color unitGhostTint = new(1f, 1f, 1f, 0.35f);
        [SerializeField] public float unitGhostThickness = 2f;
        private Camera _uiCam;
        private Camera UiCam => _uiCam ??= GetComponentInParent<Canvas>()?.worldCamera;
        private NodeView _dragFromNode;
        private PortKind _dragFromKind;
        private RectTransform _ghostEnd;
        private EdgeView _ghostEdge;
        private float _gridPhaseX = 0f, _gridPhaseY = 0f;
        private float _gridCellPx = 64f * 4;

        public struct ObjectiveDTO
        {
            public string Id, UniqueName, DisplayName, TypeName;
            public bool Hidden;
            public int OutcomeCount;
            public int Layer;
            public int Row;
            public string FactionName;
        }

        public struct OutcomeDTO
        {
            public string Id, UniqueName, TypeName;
            public int UsedByCount;
            public int Layer;
            public int Row;
        }

        public struct LinkDTO
        {
            public string FromId;
            public bool FromIsObjective;
            public string ToId;
            public bool ToIsObjective;
        }

        public struct GraphData
        {
            public ObjectiveDTO[] Objectives;
            public OutcomeDTO[] Outcomes;
            public LinkDTO[] Links;
        }

        public void SetWorldCamera(Camera cam) { worldCamera = cam; }
        public Camera GetWorldCamera() => worldCamera;
        public void ClearHighlights()
        {
            foreach (var n in _hlNodes) if (n) n.SetHighlighted(false);
            foreach (var e in _hlEdges) if (e) e.ApplyAppearance(false);
            _hlNodes.Clear();
            _hlEdges.Clear();
        }
        public void HighlightNeighborhood(NodeView center, bool on)
        {
            ClearHighlights();
            if (!on || center == null) { _hoverNode = null; return; }

            center.SetHighlighted(true);
            _hlNodes.Add(center);
            foreach (var c in _connections)
                if (c.From == center || c.To == center)
                {
                    if (c.Edge) { c.Edge.ApplyAppearance(true); _hlEdges.Add(c.Edge); }
                    var other = (c.From == center) ? c.To : c.From;
                    if (other) { other.SetHighlighted(true); _hlNodes.Add(other); }
                }
            _hoverNode = center;
        }

        public void ToggleFocusRebuild(NodeView center)
        {
            if (!center) return;
            bool isObj = center.Kind == NodeKind.Objective;

            if (!_focusOn)
            {
                FocusRebuildAround(center);
                return;
            }

            if (_focusIsObj == isObj && _focusId == center.Id)
            {
                RestoreFullGraph(center);
                return;
            }

            ExpandFocus(center);
        }

        public void FocusRebuildAround(NodeView center)
        {
            if (!center) return;
            _focusObjIds.Clear(); _focusOutIds.Clear();
            bool isObj = (center.Kind == NodeKind.Objective);
            AddNeighborhoodFromFull(center.Id, isObj, _focusObjIds, _focusOutIds);

            PinnedRebuild(center, () => BuildDataFromFocusSets());

            _focusOn = true;
            _focusAnchorId = center.Id;
            _focusAnchorIsObj = isObj;
            _focusId = _focusAnchorId;
            _focusIsObj = _focusAnchorIsObj;
        }

        private void ExpandFocus(NodeView center)
        {
            if (!center || !_focusOn) return;

            bool isObj = (center.Kind == NodeKind.Objective);
            AddNeighborhoodFromFull(center.Id, isObj, _focusObjIds, _focusOutIds);
            NodeView anchor = null;
            if (!string.IsNullOrEmpty(_focusAnchorId))
            {
                anchor = _focusAnchorIsObj
                    ? (_obj.TryGetValue(_focusAnchorId, out var n) ? n : null)
                    : (_out.TryGetValue(_focusAnchorId, out var m) ? m : null);
            }
            if (!anchor) anchor = center;

            PinnedRebuild(anchor, () => BuildDataFromFocusSets());

            _focusOn = true;
        }

        public void RestoreFullGraph(NodeView center)
        {
            if (_fullObjectives == null || _fullOutcomes == null || _fullLinks == null) return;

            PinnedRebuild(center, () => new GraphData
            {
                Objectives = _fullObjectives,
                Outcomes = _fullOutcomes,
                Links = _fullLinks
            });

            _focusOn = false; _focusId = null; _focusObjIds.Clear(); _focusOutIds.Clear();

            _gridPhaseX = 0f; _gridPhaseY = 0f; UpdateGridUV();
        }

        private void EnsureGridLayer()
        {
            if (!viewport) return;

            if (_bgRT == null)
            {
                var t = viewport.Find("Background") as RectTransform;
                if (t)
                {
                    _bgRT = t;
                    _bgImg = t.GetComponent<Image>();
                    if (_bgImg) _bgImg.raycastTarget = false;
                    _bgRT.gameObject.SetActive(_bgGridVisible);
                }
            }

            if (_minorRT == null)
            {
                var t = (viewport.Find("Grid") as RectTransform);
                if (t)
                {
                    _minorRT = t;
                    _minorImg = t.GetComponent<RawImage>();
                    if (_minorImg) _minorImg.raycastTarget = false;
                    _minorRT.gameObject.SetActive(_bgGridVisible);
                }
            }
            if (_minorImg)
            {
                _minorImg.raycastTarget = false;
                _minorImg.uvRect = new Rect(0, 0, 1, 1);
            }

            if (_bgRT) _bgRT.SetAsFirstSibling();
            if (_minorRT) _minorRT.SetSiblingIndex(1);
            if (content) content.SetSiblingIndex(2);
        }

        public void ToggleBackgroundAndGrid()
        {
            SetBackgroundAndGridVisible(!_bgGridVisible);
        }

        public void SetBackgroundAndGridVisible(bool on)
        {
            _bgGridVisible = on;
            EnsureGridLayer();
            if (_bgRT) _bgRT.gameObject.SetActive(on);
            if (_minorRT) _minorRT.gameObject.SetActive(on);
            if (_minorImg) _minorImg.enabled = on;
        }

        private Vector2 ViewportPixelOfContent(Vector2 contentPoint)
        {
            var world = content.TransformPoint(new Vector3(contentPoint.x, contentPoint.y, 0f));
            var vLocal = viewport.InverseTransformPoint(world);
            var r = viewport.rect;
            float px = vLocal.x + r.width * viewport.pivot.x;
            float py = vLocal.y + r.height * viewport.pivot.y;
            return new Vector2(px, py);
        }

        private void UpdateGridUV()
        {
            if (!viewport || !content || _minorImg == null) return;

            var vpSize = viewport.rect.size;
            if (vpSize.x <= 0f || vpSize.y <= 0f) return;

            var s = content.localScale;
            float sx = Mathf.Abs(s.x) > 1e-6f ? s.x : 1f;
            float sy = Mathf.Abs(s.y) > 1e-6f ? s.y : 1f;
            float cell = Mathf.Max(1e-3f, _gridCellPx);

            float uPerPx = 1f / (cell * sx);
            float vPerPx = 1f / (cell * sy);

            float tilesX = vpSize.x * uPerPx;
            float tilesY = vpSize.y * vPerPx;

            Vector2 originPx = ViewportPixelOfContent(Vector2.zero);

            float uOff = Mathf.Round(-originPx.x + _gridPhaseX / uPerPx) * uPerPx;
            float vOff = Mathf.Round(-originPx.y + _gridPhaseY / vPerPx) * vPerPx;

            _minorImg.enabled = true;
            _minorImg.uvRect = new Rect(uOff, vOff, tilesX, tilesY);
        }

        private void LateUpdate()
        {
            if (!viewport || !content) return;

            var pos = content.anchoredPosition;
            var scale = content.localScale;
            var size = viewport.rect.size;

            if (_lastContentPos != pos || _lastContentScale != (Vector2)scale || _lastViewportSize != size)
            {
                _lastContentPos = pos;
                _lastContentScale = scale;
                _lastViewportSize = size;
                UpdateGridUV();
            }
            UpdateUnitAnchorsToScreen();
        }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Delete))
                return;

            if (!viewport) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, Input.mousePosition, UiCam))
                return;

            if (_hoverEdge)
            {
                OnEdgeRequestDelete(_hoverEdge);
                return;
            }

            if (_hoverPortNode)
            {
                if (_hoverPortKind == PortKind.Input) UnlinkIncoming(_hoverPortNode);
                else UnlinkOutgoing(_hoverPortNode);
                ClearHighlights();
                return;
            }

            if (_hoverNode)
            {
                UnlinkIncoming(_hoverNode);
                UnlinkOutgoing(_hoverNode);
                ClearHighlights();
            }
        }

        private EdgeView CreateEdge(string name, RectTransform begin, RectTransform end, in EdgeView.Appearance app)
        { 
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(EdgeView));
            var rt = (RectTransform)go.transform;
            rt.SetParent(edgeLayer, false);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;

            var e = go.GetComponent<EdgeView>();
            e.Init(content, begin, end);
            e.ApplyAppearance(app, highlighted: false);
            edgeLayer.SetAsFirstSibling();
            return e;
        }

        private EdgeView.Appearance AppForNormal(NodeView from, NodeView to)
        {
            Color normal = edgeTint;
            if ((from.Kind == NodeKind.Objective && from.TryGetFactionColor(out Color fc)) ||
                (to.Kind == NodeKind.Objective && to.TryGetFactionColor(out fc)))
            {
                normal = new Color(fc.r, fc.g, fc.b, edgeTint.a);
            }

            return new EdgeView.Appearance
            {
                normalTint = normal * 2f,
                highlightTint = highlightEdgeTint,
                normalThickness = edgeThickness,
                highlightThickness = highlightEdgeThickness,
                minHitThickness = EdgeView.Appearance.Default.minHitThickness,
                sprite = edgeFadeSprite
            };
        }

        public Connection MakeConnection(NodeView from, NodeView to)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return null;
            if (from.Kind == to.Kind) return null;
            foreach (var c in _connections) if (c.From == from && c.To == to) return null;

            var app = AppForNormal(from, to);
            var edge = CreateEdge($"Edge_{from.name}_to_{to.name}", from.RightPort, to.LeftPort, app);

            var conn = new Connection(from, to, edge);
            from.AddOutgoing(conn);           
            RegisterConnection(conn);         
            return conn;
        }

        private EdgeView.Appearance AppForGhost() => new EdgeView.Appearance
        {
            normalTint = ghostEdgeTint,
            highlightTint = ghostEdgeTint,
            normalThickness = ghostEdgeThickness,
            highlightThickness = ghostEdgeThickness,
            minHitThickness = EdgeView.Appearance.Default.minHitThickness,
            sprite = edgeFadeSprite
        };

        private EdgeView.Appearance AppForUnitGhost() => new EdgeView.Appearance
        {
            normalTint = unitGhostTint,
            highlightTint = unitGhostTint,
            normalThickness = unitGhostThickness,
            highlightThickness = unitGhostThickness,
            minHitThickness = EdgeView.Appearance.Default.minHitThickness,
            sprite = edgeFadeSprite
        };


        public void BuildGraph(GraphData data, bool computeLayout = true, bool snapshotAsFull = true)
        {
            if (computeLayout)
                data = ComputeTreeLayout(data);

            if (snapshotAsFull)
            {
                _fullObjectives = data.Objectives ?? Array.Empty<ObjectiveDTO>();
                _fullOutcomes = data.Outcomes ?? Array.Empty<OutcomeDTO>();
                _fullLinks = data.Links ?? Array.Empty<LinkDTO>();
            }
            Build(data.Objectives, data.Outcomes, data.Links);
        }

        private static NodeKey NK(bool isObj, string id) => new NodeKey { IsObjective = isObj, Id = id };
        private struct NodeKey : IEquatable<NodeKey>
        {
            public bool IsObjective;
            public string Id;
            public bool Equals(NodeKey other) => IsObjective == other.IsObjective && Id == other.Id;
            public override bool Equals(object o) => o is NodeKey k && Equals(k);
            public override int GetHashCode() => (IsObjective ? 1 : 0) * 397 ^ (Id?.GetHashCode() ?? 0);
        }

        private GraphData ComputeTreeLayout(GraphData src)
        {
            var objById = (src.Objectives ?? Array.Empty<ObjectiveDTO>())
                .ToDictionary(o => o.Id, StringComparer.Ordinal);
            var outById = (src.Outcomes ?? Array.Empty<OutcomeDTO>())
                .ToDictionary(u => u.Id, StringComparer.Ordinal);

            var adj = new Dictionary<NodeKey, List<NodeKey>>();
            var indeg = new Dictionary<NodeKey, int>();
            void Ensure(NodeKey k) { if (!adj.ContainsKey(k)) adj[k] = new List<NodeKey>(); if (!indeg.ContainsKey(k)) indeg[k] = 0; }

            var usedBy = new Dictionary<string, int>(StringComparer.Ordinal);
            var outcomeCount = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var l in src.Links ?? Array.Empty<LinkDTO>())
            {
                var u = NK(l.FromIsObjective, l.FromId);
                var v = NK(l.ToIsObjective, l.ToId);
                Ensure(u); Ensure(v);
                adj[u].Add(v); indeg[v]++;

                if (l.FromIsObjective && !l.ToIsObjective)
                    outcomeCount[l.FromId] = outcomeCount.TryGetValue(l.FromId, out var cc) ? cc + 1 : 1;
                if (!l.FromIsObjective && l.ToIsObjective)
                    usedBy[l.FromId] = usedBy.TryGetValue(l.FromId, out var uu) ? uu + 1 : 1;
            }

            var layer = new Dictionary<NodeKey, int>();
            var q = new Queue<NodeKey>(indeg.Where(p => p.Value == 0).Select(p => p.Key));
            foreach (var k in q) layer[k] = 0;
            while (q.Count > 0)
            {
                var u = q.Dequeue();
                if (!adj.TryGetValue(u, out var outs)) continue;
                foreach (var v in outs)
                {
                    int next = layer[u] + 1;
                    if (!layer.TryGetValue(v, out var lv) || next > lv) layer[v] = next;
                    if (--indeg[v] == 0) q.Enqueue(v);
                }
            }

            var pred = new Dictionary<NodeKey, List<NodeKey>>();
            foreach (var (u, outs) in adj)
            {
                foreach (var v in outs)
                {
                    if (!pred.TryGetValue(v, out var list)) pred[v] = list = new List<NodeKey>();
                    list.Add(u);
                }
                if (!pred.ContainsKey(u)) pred[u] = new List<NodeKey>();
            }
            foreach (var n in adj.Keys)
                if (!layer.ContainsKey(n))
                    layer[n] = pred.TryGetValue(n, out var ps) && ps.Count > 0 ? ps.Select(p => layer.TryGetValue(p, out var L) ? L + 1 : 0).Max() : 0;

            var primaryParent = new Dictionary<NodeKey, NodeKey>();
            var treeChildren = new Dictionary<NodeKey, List<NodeKey>>();
            void AddChild(NodeKey p, NodeKey c) { if (!treeChildren.TryGetValue(p, out var kids)) treeChildren[p] = kids = new List<NodeKey>(); kids.Add(c); }

            foreach (var u in adj.Keys.OrderBy(k => layer[k]).ThenBy(k => k.Id, StringComparer.Ordinal))
            {
                if (!adj.TryGetValue(u, out var outs)) continue;
                foreach (var v in outs.Where(v => layer[v] > layer[u]).OrderBy(v => layer[v]).ThenBy(v => v.Id, StringComparer.Ordinal))
                    if (!primaryParent.ContainsKey(v)) { primaryParent[v] = u; AddChild(u, v); }
            }

            int Degree(NodeKey n) => adj.TryGetValue(n, out var l) ? l.Count : 0;
            foreach (var kv in treeChildren.ToList())
                kv.Value.Sort((a, b) => { int cmp = layer[a].CompareTo(layer[b]); if (cmp != 0) return cmp; cmp = Degree(b).CompareTo(Degree(a)); if (cmp != 0) return cmp; return string.Compare(a.Id, b.Id, StringComparison.Ordinal); });

            var roots = adj.Keys.Where(k => !primaryParent.ContainsKey(k)).OrderBy(k => layer[k]).ThenBy(k => k.Id, StringComparer.Ordinal).ToList();

            var subtreeSpan = new Dictionary<NodeKey, int>();
            var centerY = new Dictionary<NodeKey, float>();
            int Span(NodeKey n)
            {
                if (!treeChildren.TryGetValue(n, out var kids) || kids.Count == 0) return subtreeSpan[n] = 1;
                int s = 0; foreach (var c in kids) s += Span(c); return subtreeSpan[n] = Math.Max(s, 1);
            }
            float Place(NodeKey n, float startY)
            {
                if (!treeChildren.TryGetValue(n, out var kids) || kids.Count == 0) { centerY[n] = startY + (subtreeSpan[n] - 1) * 0.5f; return centerY[n]; }
                float cursor = startY; var childC = new List<float>(kids.Count);
                foreach (var c in kids) { float cy = Place(c, cursor); childC.Add(cy); cursor += subtreeSpan[c]; }
                float myC = childC.Average(); centerY[n] = myC; return myC;
            }
            float forest = 0f;
            foreach (var r in roots) { Span(r); Place(r, forest); forest += subtreeSpan[r]; }
            foreach (var n in adj.Keys) if (!centerY.ContainsKey(n)) { subtreeSpan[n] = 1; centerY[n] = forest; forest += 1; }

            var row = centerY.ToDictionary(kv => kv.Key, kv => Mathf.RoundToInt(kv.Value));

            var allKeys = new List<NodeKey>();
            foreach (var id in objById.Keys) allKeys.Add(NK(true, id));
            foreach (var id in outById.Keys) allKeys.Add(NK(false, id));

            var connected = new HashSet<NodeKey>(adj.Keys);
            foreach (var kv in adj) foreach (var k in kv.Value) connected.Add(k);

            var unconnected = allKeys.Where(k => !connected.Contains(k))
                                     .OrderBy(k => k.Id, StringComparer.Ordinal)
                                     .ToList();

            var fixedRow = new Dictionary<NodeKey, int>();
            for (int i = 0; i < unconnected.Count; i++) fixedRow[unconnected[i]] = i;

            int rowOffset = unconnected.Count;

            int GetLayer(NodeKey k)
            {
                if (fixedRow.ContainsKey(k)) return 0;                      
                return layer.TryGetValue(k, out var L) ? L : 0;             
            }
            int GetRow(NodeKey k)
            {
                if (fixedRow.TryGetValue(k, out var rFixed)) return rFixed; 
                var r = row.TryGetValue(k, out var R) ? R : 0;
                return r + rowOffset;                                       
            }

            ObjectiveDTO MapObj(ObjectiveDTO o) => new ObjectiveDTO
            {
                Id = o.Id,
                UniqueName = o.UniqueName,
                DisplayName = o.DisplayName,
                TypeName = o.TypeName,
                Hidden = o.Hidden,
                OutcomeCount = outcomeCount.TryGetValue(o.Id, out var oc) ? oc : o.OutcomeCount,
                Layer = GetLayer(NK(true, o.Id)),   
                Row = GetRow(NK(true, o.Id)),       
                FactionName = o.FactionName
            };
            OutcomeDTO MapOut(OutcomeDTO u) => new OutcomeDTO
            {
                Id = u.Id,
                UniqueName = u.UniqueName,
                TypeName = u.TypeName,
                UsedByCount = usedBy.TryGetValue(u.Id, out var ub) ? ub : u.UsedByCount,
                Layer = GetLayer(NK(false, u.Id)),  
                Row = GetRow(NK(false, u.Id))       
            };

            return new GraphData
            {
                Objectives = objById.Values.Select(MapObj).ToArray(),
                Outcomes = outById.Values.Select(MapOut).ToArray(),
                Links = src.Links ?? Array.Empty<LinkDTO>()
            };
        }


        private void EnsureEdgesContainer()
        {
            if (!content) return;
            if (!edgeLayer)
            {
                var go = new GameObject("Edges", typeof(RectTransform));
                edgeLayer = go.GetComponent<RectTransform>();
                edgeLayer.SetParent(content, false);
                edgeLayer.anchorMin = Vector2.zero;
                edgeLayer.anchorMax = Vector2.one;
                edgeLayer.offsetMin = Vector2.zero;
                edgeLayer.offsetMax = Vector2.zero;
            }
            edgeLayer.SetSiblingIndex(0);
        }

        public void ToggleUnitConnections(NodeView center)
        {
            if (center == null) return;
            if (_unitPinnedOn && _unitPinnedCenter == center)
            {
                _unitPinnedOn = false;
                _unitPinnedCenter = null;
                ClearUnitGhosts();
                return;
            }
            _unitPinnedOn = true;
            _unitPinnedCenter = center;
            BuildUnitConnections(center);
        }

        public void ClearUnitGhosts()
        {
            for (int i = 0; i < _unitGhosts.Count; i++)
            {
                if (_unitGhosts[i].edge) Destroy(_unitGhosts[i].edge.gameObject);
                if (_unitGhosts[i].anchor) Destroy(_unitGhosts[i].anchor.gameObject);
            }
            _unitGhosts.Clear();
        }

        private void OnDisable()
        {
            ClearUnitGhosts();
        }

        private void OnDestroy()
        {
            ClearUnitGhosts();
        }
        private void BuildUnitConnections(NodeView center)
        {
            ClearUnitGhosts();
            if (center == null || !_unitPinnedOn || _unitPinnedCenter != center) return;
            if (QueryUnitWorldPositions == null || content == null || edgeLayer == null) return;

            var isObj = (center.Kind == NodeKind.Objective);
            var worldGetters = QueryUnitWorldPositions(center.Id, isObj);
            if (worldGetters == null) return;
            foreach (var getWorld in worldGetters)
            {
                var anchor = new GameObject("UnitAnchor", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                anchor.SetParent(content, false);
                anchor.sizeDelta = Vector2.one;
                anchor.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                anchor.GetComponent<Image>().raycastTarget = false;

                var edge = CreateEdge("Edge_UnitGhost", center.RT, anchor, AppForUnitGhost());
                _unitGhosts.Add((anchor, edge, getWorld, center.RT));

            }

            UpdateUnitAnchorsToScreen();
        }

        private void UpdateUnitAnchorsToScreen()
        {
            if (_unitGhosts.Count == 0 || !content) return;

            var worldCam = worldCamera ?? Camera.main;

            if (worldCam == null) return;
            var vpRectInContent = GetViewportRectInContent();
            foreach (var item in _unitGhosts)
            {
                if (!item.anchor) continue;
                Vector3 world;
                try { world = item.worldGetter != null ? item.worldGetter() : default; }
                catch { item.anchor.gameObject.SetActive(false); continue; }
                var screen = worldCam.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                {
                    var vp = worldCam.WorldToViewportPoint(world);
                    vp.x = 1f - vp.x; vp.y = 1f - vp.y;
                    screen = new Vector3(vp.x * Screen.width, vp.y * Screen.height, 0.0001f);
                }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, UiCam, out var local);

                var fromLocal = ToContentLocalCenter(item.begin);
                var clamped = ClampToRectAlongSegment(fromLocal, local, vpRectInContent);
                item.anchor.anchoredPosition = clamped;

            }
        }

        private Rect GetViewportRectInContent()
        {
            if (!viewport || !content) return new Rect(0, 0, 0, 0);
            var wc = new Vector3[4];
            viewport.GetWorldCorners(wc);
            var a = content.InverseTransformPoint(wc[0]);
            var b = content.InverseTransformPoint(wc[2]);
            var min = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
            var max = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Vector2 ToContentLocalCenter(RectTransform rt)
        {
            var t = rt.TransformPoint(rt.rect.center);
            var p = ((RectTransform)rt.parent).InverseTransformPoint(t);
            return new Vector2(p.x, p.y);
        }

        private static Vector2 ClampToRectAlongSegment(Vector2 from, Vector2 to, Rect rect)
        {
            if (rect.Contains(to)) return to;
            var d = to - from;
            const float EPS = 1e-5f;
            float bestT = float.PositiveInfinity;
            Vector2 best = to;

            void TryHit(float t, float x, float y)
            {
                if (t < 0f || t > 1f) return;
                var p = from + d * t;
                if (p.x < rect.xMin - EPS || p.x > rect.xMax + EPS ||
                    p.y < rect.yMin - EPS || p.y > rect.yMax + EPS) return;
                if (t < bestT) { bestT = t; best = p; }
            }

            if (Mathf.Abs(d.x) > EPS)
            {
                TryHit((rect.xMin - from.x) / d.x, rect.xMin, 0f);
                TryHit((rect.xMax - from.x) / d.x, rect.xMax, 0f);
            }
            if (Mathf.Abs(d.y) > EPS)
            {
                TryHit((rect.yMin - from.y) / d.y, 0f, rect.yMin);
                TryHit((rect.yMax - from.y) / d.y, 0f, rect.yMax);
            }
            best.x = Mathf.Clamp(best.x, rect.xMin + 1f, rect.xMax - 1f);
            best.y = Mathf.Clamp(best.y, rect.yMin + 1f, rect.yMax - 1f);
            return best;
        }

        private void PinnedRebuild(NodeView center, Func<GraphData> make)
        {
            if (!center || !content || !viewport) return;

            float s = content.localScale.x;
            var vpSize = viewport.rect.size;

            float cell = Mathf.Max(1e-3f, _gridCellPx);
            float sxBefore = Mathf.Approximately(s, 0f) ? 1f : s;
            float tilesX_before = vpSize.x / (cell * sxBefore);
            float tilesY_before = vpSize.y / (cell * sxBefore);

            Vector2 originPx_before = ViewportPixelOfContent(Vector2.zero);
            float baseX_before = -(originPx_before.x / vpSize.x) * tilesX_before;
            float baseY_before = -(originPx_before.y / vpSize.y) * tilesY_before;

            Vector2 anchorLocal_before = ToContentLocalCenter(center.RT);
            Vector2 anchorPx_before = ViewportPixelOfContent(anchorLocal_before);

            float u_anchor_before = (anchorPx_before.x / vpSize.x) * tilesX_before + (baseX_before + _gridPhaseX);
            float v_anchor_before = (anchorPx_before.y / vpSize.y) * tilesY_before + (baseY_before + _gridPhaseY);

            Vector2 lockVp = content.anchoredPosition + ToContentLocalCenter(center.RT) * s;

            ClearUnitGhosts();
            var data = make();
            _isFocusBuild = true;
            try
            {
                BuildGraph(data, computeLayout: true, snapshotAsFull: false);
            }
            finally { _isFocusBuild = false; }

            NodeView rebuilt = null;
            if (center.Kind == NodeKind.Objective) _obj.TryGetValue(center.Id, out rebuilt);
            else _out.TryGetValue(center.Id, out rebuilt);

            if (rebuilt && content)
            {
                float s2 = content.localScale.x;
                Vector2 post = ToContentLocalCenter(rebuilt.RT);
                content.anchoredPosition = lockVp - post * s2;

                float tilesX_after = vpSize.x / (cell * (Mathf.Approximately(s2, 0f) ? 1f : s2));
                float tilesY_after = vpSize.y / (cell * (Mathf.Approximately(s2, 0f) ? 1f : s2));

                Vector2 originPx_after = ViewportPixelOfContent(Vector2.zero);
                float baseX_after = -(originPx_after.x / vpSize.x) * tilesX_after;
                float baseY_after = -(originPx_after.y / vpSize.y) * tilesY_after;

                Vector2 anchorLocal_after = ToContentLocalCenter(rebuilt.RT);
                Vector2 anchorPx_after = ViewportPixelOfContent(anchorLocal_after);

                _gridPhaseX = u_anchor_before - ((anchorPx_after.x / vpSize.x) * tilesX_after + baseX_after);
                _gridPhaseY = v_anchor_before - ((anchorPx_after.y / vpSize.y) * tilesY_after + baseY_after);

                UpdateGridUV();
            }
        }

        private void AddNeighborhoodFromFull(string id, bool isObj, HashSet<string> keepObj, HashSet<string> keepOut)
        {
            if (isObj) keepObj.Add(id); else keepOut.Add(id);
            if (_fullLinks == null) return;

            foreach (var l in _fullLinks)
            {
                if (!((l.FromIsObjective == isObj && l.FromId == id) ||
                      (l.ToIsObjective == isObj && l.ToId == id)))
                    continue;

                if (l.FromIsObjective) keepObj.Add(l.FromId); else keepOut.Add(l.FromId);
                if (l.ToIsObjective) keepObj.Add(l.ToId); else keepOut.Add(l.ToId);
            }
        }

        private void CenterViewportOn(NodeView n)
        {
            if (!viewport || !content || !n) return;
            float s = Mathf.Approximately(content.localScale.x, 0f) ? 1f : content.localScale.x;
            Vector2 local = ToContentLocalCenter(n.RT); 
            content.anchoredPosition = -local * s;      
            UpdateGridUV();
        }


        private GraphData BuildDataFromFocusSets()
        {
            var obj = (_fullObjectives ?? Array.Empty<ObjectiveDTO>()).Where(o => _focusObjIds.Contains(o.Id)).ToArray();
            var outc = (_fullOutcomes ?? Array.Empty<OutcomeDTO>()).Where(o => _focusOutIds.Contains(o.Id)).ToArray();
            var links = (_fullLinks ?? Array.Empty<LinkDTO>()).Where(l =>
                (l.FromIsObjective ? _focusObjIds.Contains(l.FromId) : _focusOutIds.Contains(l.FromId)) &&
                (l.ToIsObjective ? _focusObjIds.Contains(l.ToId) : _focusOutIds.Contains(l.ToId))
            ).ToArray();
            return new GraphData { Objectives = obj, Outcomes = outc, Links = links };
        }



        public void BeginLinkDrag(NodeView fromNode, PortKind fromKind, PointerEventData e)
        {
            if (fromNode == null) return;
            _dragFromNode = fromNode;
            _dragFromKind = fromKind;

            _ghostEnd = new GameObject("GhostEnd", typeof(RectTransform)).GetComponent<RectTransform>();
            _ghostEnd.SetParent(content, false);
            _ghostEnd.sizeDelta = Vector2.one;

            var begin = (fromKind == PortKind.Output) ? fromNode.RightPort : fromNode.LeftPort;
            _ghostEdge = CreateEdge("Edge_Ghost", begin, _ghostEnd, AppForGhost());

            HighlightNeighborhood(fromNode, true);
            DragLinkTo(e.position);
        }

        public void DragLinkTo(Vector2 screenPos)
        {
            if (_ghostEnd == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPos, UiCam, out var local);
            _ghostEnd.anchoredPosition = local;

            if (_dragFromNode != null)
            {
                HighlightNeighborhood(_dragFromNode, true);

                var targetPort = RaycastPortAt(screenPos);
                var to = targetPort ? targetPort.GetComponentInParent<NodeView>() : null;
                if (to != null && to != _dragFromNode && to.Kind != _dragFromNode.Kind
                    && targetPort.Kind != _dragFromKind)
                {
                    to.SetHighlighted(true);
                    _hlNodes.Add(to);
                }
            }
        }


        public void EndLinkDrag(Vector2 screenPos)
        {
            void CleanupGhost()
            {
                if (_ghostEdge) Destroy(_ghostEdge.gameObject);
                if (_ghostEnd) Destroy(_ghostEnd.gameObject);
                _ghostEdge = null; _ghostEnd = null;
                ClearHighlights();
            }

            if (_dragFromNode == null) { CleanupGhost(); return; }

            var targetPort = RaycastPortAt(screenPos);
            var fromNode = _dragFromNode;
            var fromKind = _dragFromKind;
            CleanupGhost();
            _dragFromNode = null;
            if (fromNode == null || targetPort == null) return;
            var toNode = targetPort.GetComponentInParent<NodeView>();
            if (toNode == null || toNode == fromNode) return;

            var toKind = targetPort.Kind;
            if (fromKind == PortKind.Output && toKind == PortKind.Input)
            {
                if (fromNode.Kind == toNode.Kind) return;
                ClearHighlights();
                TryCreateLink(fromNode, toNode);
            }
            else if (fromKind == PortKind.Input && toKind == PortKind.Output)
            {
                if (fromNode.Kind == toNode.Kind) return;
                ClearHighlights();
                TryCreateLink(toNode, fromNode);
            }
        }

        private void TryCreateLink(NodeView from, NodeView to)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return;
            if (from.Kind == to.Kind) return;

            foreach (var c in _connections)
                if (c.From == from && c.To == to)
                    return;

            MakeConnection(from,to);

            OnLink?.Invoke(from.Id, from.Kind == NodeKind.Objective, to.Id, to.Kind == NodeKind.Objective);

        }

        private PortView RaycastPortAt(Vector2 screenPos)
        {
            if (EventSystem.current == null) return null;
            var results = new List<RaycastResult>();
            var ped = new PointerEventData(EventSystem.current) { position = screenPos };
            EventSystem.current.RaycastAll(ped, results);

            for (int i = 0; i < results.Count; i++)
            {
                var pv = results[i].gameObject.GetComponentInParent<PortView>();
                if (pv != null) return pv;
            }
            return null;
        }

        internal void RegisterConnection(Connection c)
        {
            if (c == null || _connections.Contains(c)) return;
            _connections.Add(c);

            if (c.Edge != null)
            {
                c.Edge.OnRequestDelete = OnEdgeRequestDelete;
                c.Edge.OnHoverChanged = OnEdgeHoverChanged;
            }
        }

        void OnEdgeHoverChanged(EdgeView edge, bool on)
        {
            var c = _connections.Find(cc => cc.Edge == edge);
            if (c == null) return;

            if (on)
                HighlightConnection(c);
            else
                ClearHighlights();
            _hoverEdge = on ? edge : null;
        }
        public void OnPortHoverChanged(NodeView node, PortKind kind, bool on)
        {
            if (on)
            {
                _hoverPortNode = node;
                _hoverPortKind = kind;
            }
            else
            {
                if (_hoverPortNode == node) _hoverPortNode = null;
            }
        }

        void HighlightConnection(Connection c)
        {
            ClearHighlights();

            if (c.Edge) { c.Edge.ApplyAppearance(true); _hlEdges.Add(c.Edge); }
            if (c.From) { c.From.SetHighlighted(true); _hlNodes.Add(c.From); }
            if (c.To) { c.To.SetHighlighted(true); _hlNodes.Add(c.To); }
        }

        private void OnEdgeRequestDelete(EdgeView edge)
        {
            var c = _connections.Find(cc => cc.Edge == edge);
            if (c == null) return;
            Plugin.Logger.LogDebug($"[Graph] UI delete edge {c.From?.Id} > {c.To?.Id}");
            RemoveConnection(c, notifyModel: true);
        }

        private void RemoveConnection(Connection c, bool notifyModel)
        {
            if (c.Edge) { c.Edge.OnRequestDelete = null; c.Edge.OnHoverChanged = null; Destroy(c.Edge.gameObject); }
            _connections.Remove(c);
            c.From?.RemoveConnectionTo(c.To);
            if (notifyModel && OnUnlink != null && c.From != null && c.To != null)
                OnUnlink.Invoke(c.From.Id, c.From.Kind == NodeKind.Objective, c.To.Id, c.To.Kind == NodeKind.Objective);
            ClearHighlights();
        }

        internal void Unlink(NodeView from, NodeView to)
        {
            var c = _connections.Find(cc => cc.From == from && cc.To == to);
            if (c != null) RemoveConnection(c, notifyModel: true);
        }

        private void RemoveAllConnections(Predicate<Connection> match, bool notifyModel)
        {
            var toRemove = _connections.Where(c => match(c)).ToList();
            foreach (var c in toRemove)
                RemoveConnection(c, notifyModel);
        }

        internal void UnlinkOutgoing(NodeView n) => RemoveAllConnections(c => c.From == n, true);
        internal void UnlinkIncoming(NodeView n) => RemoveAllConnections(c => c.To == n, true);

        public void Clear()
        {
            if (edgeLayer)
            {
                for (int i = edgeLayer.childCount - 1; i >= 0; i--)
                    Destroy(edgeLayer.GetChild(i).gameObject);
            }

            if (content)
            {
                for (int i = content.childCount - 1; i >= 0; i--)
                {
                    var ch = content.GetChild(i) as RectTransform;
                    if (!ch) continue;
                    if ((edgeLayer && ch == edgeLayer)) continue;
                    Destroy(ch.gameObject);
                }
            }

            foreach (var n in _obj.Values) if (n) n.DisconnectAll();
            foreach (var n in _out.Values) if (n) n.DisconnectAll();
            _obj.Clear();
            _out.Clear();
            _connections.Clear();
            ClearHighlights();
        }

        private void Awake()
        {
            AutoWire();
            EnsureGridLayer();
            var ci = content.GetComponent<Image>();
            if (ci) ci.raycastTarget = false;
            EnsureEdgesContainer();
            UpdateGridUV();
        }

        private void AutoWire()
        {
            if (!viewport)
            {
                var t = transform.Find("Viewport");
                if (t) viewport = t as RectTransform;
            }
            if (!content && viewport)
            {
                var t = viewport.Find("Content");
                if (t) content = t as RectTransform;
                if (!content) { Plugin.Logger.LogError("[GraphView] Missing 'Content' under Viewport"); return; }
            }
            if (!edgeLayer && content)
            {
                var t = content.Find("Edges");
                if (t) edgeLayer = t as RectTransform;
            }

        }

        public void Build(ObjectiveDTO[] objectives, OutcomeDTO[] outcomes, LinkDTO[] links)
        {
            if (_building) return;
            _building = true;
            try
            {
                AutoWire();
                EnsureEdgesContainer();
                if (!content || !viewport || !nodePrefab)
                {
                    Plugin.Logger.LogError($"[GraphView] Missing refs: content={(content != null)}, viewport={(viewport != null)}, nodePrefab={(nodePrefab != null)}");
                    return;
                }
                Clear();

                var nodes = new List<(NodeView v, int layer, int row)>();
                int maxLayer = 0, maxRow = 0;

                foreach (var o in objectives)
                {
                    var n = Instantiate(nodePrefab, content);
                    n.name = $"OBJ_{o.UniqueName}";
                    n.InitGraph(edgeLayer, content, this);
                    n.BindObjective(o.Id, o.UniqueName, o.DisplayName, o.TypeName, o.Hidden, o.OutcomeCount);
                    n.ApplyFaction(o.FactionName);
                    nodes.Add((n, o.Layer, o.Row));
                    maxLayer = Mathf.Max(maxLayer, o.Layer);
                    maxRow = Mathf.Max(maxRow, o.Row);
                    _obj[o.Id] = n;
                }

                foreach (var oc in outcomes)
                {
                    var n = Instantiate(nodePrefab, content);
                    n.name = $"OUT_{oc.UniqueName}";
                    n.InitGraph(edgeLayer, content, this);
                    n.BindOutcome(oc.Id, oc.UniqueName, oc.TypeName, oc.UsedByCount);
                    bool supports = CanOutcomeHaveOutputs?.Invoke(n.Id) ?? false;
                    n.SetOutputPortVisible(supports);
                    nodes.Add((n, oc.Layer, oc.Row));
                    maxLayer = Mathf.Max(maxLayer, oc.Layer);
                    maxRow = Mathf.Max(maxRow, oc.Row);
                    _out[oc.Id] = n;
                }

                var colWidths = new float[maxLayer + 1];
                var rowHeights = new float[maxRow + 1];
                foreach (var e in nodes)
                {
                    var sz = e.v.GetLayoutSize();
                    if (sz.x > colWidths[e.layer]) colWidths[e.layer] = sz.x;
                    if (sz.y > rowHeights[e.row]) rowHeights[e.row] = sz.y;
                }

                var colX = new float[colWidths.Length];
                var rowY = new float[rowHeights.Length];
                float accX = 0f;
                float accY = 0f;
                for (int c = 0; c < colWidths.Length; c++) { colX[c] = accX; accX += colWidths[c]; }
                for (int r = 0; r < rowHeights.Length; r++) { rowY[r] = accY; accY += rowHeights[r]; }

                foreach (var (v, layer, row) in nodes)
                {
                    v.RT.anchorMin = v.RT.anchorMax = new Vector2(0, 1);
                    v.RT.pivot = new Vector2(0, 1);
                    v.RT.anchoredPosition = new Vector2(colX[layer], rowY[row]);
                }

                foreach (var l in links)
                {
                    NodeView from = l.FromIsObjective
                        ? (_obj.TryGetValue(l.FromId, out var oFrom) ? oFrom : null)
                        : (_out.TryGetValue(l.FromId, out var uFrom) ? uFrom : null);
                    NodeView to = l.ToIsObjective
                        ? (_obj.TryGetValue(l.ToId, out var oTo) ? oTo : null)
                        : (_out.TryGetValue(l.ToId, out var uTo) ? uTo : null);
                    if (from != null && to != null && !ReferenceEquals(from, to))
                        MakeConnection(from, to);
                }

                const float slack = 16f;
                float neededW = Mathf.Max(viewport.rect.width, accX + slack);
                float neededH = Mathf.Max(viewport.rect.height, accY + slack);
                content.sizeDelta = new Vector2(neededW, neededH);

                EnsureGridLayer();
                UpdateGridUV();
                if (!_isFocusBuild && nodes.Count > 0)
                {
                    CenterViewportOn(nodes[0].v); 
                }
            }
            finally
            {
                _building = false;
            }
        }
    }
}
