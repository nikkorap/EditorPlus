// Plugin.cs

/*
TODO

height clamp broke terrain snapping FIXED
keybind to place multiple units     DONE
lines to airbases and waypoints     DONE
extend dropdowns                    DONE
toggle to place unit with hold position DONE
shrink MissionNameInput if more buttons are needed DONE

add snapping                        DONEish, not snapping cursor ghost
custom button icons

*/
using BepInEx;
using BepInEx.Logging;
using NuclearOption.MissionEditorScripts;
using NuclearOption.SavedMission.ObjectiveV2;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using System.IO;
using System;
using System.Collections.Generic;
using System.Reflection;
using NuclearOption.SavedMission.ObjectiveV2.Outcomes;
using UnityEngine.UI;
using System.Collections;
using NuclearOption.SavedMission;
using NuclearOption.MissionEditorScripts.Buttons;
using HarmonyLib;
using TMPro;
using RuntimeHandle;
using System.Globalization;
using NuclearOption.SavedMission.ObjectiveV2.Objectives;

namespace EditorPlus
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static new ManualLogSource Logger;
        private string _modPath;
        private AssetBundle _bundle;
        private GameObject _overlayRoot;
        private GraphView _view;
        private static MethodInfo _miShowEditOutcome;
        private Coroutine _sceneSetupCo;
        private static readonly Dictionary<Type, FieldInfo> _completeObjListField = [];
        private Button _overlayToggleButton;
        private Button _gridToggleButton;
        private Toggle _holdPosToggle;
        private bool holdpos = false;
        private static readonly Dictionary<Type, FieldInfo> s_AllItemsFieldCache = [];
        private ObjectiveEditorV2 editormenu;
        private ChangeTabButton objectivesBtn;
        private RectTransform _leftPanelRT;
        private Vector2 _leftPanelOriginalAnchored;
        private bool _leftPanelOffsetApplied;
        private const float LeftPanelShiftX = -530f;
        internal static Plugin Instance;
        private bool _nameInputShrunk;
        void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            _modPath = Path.GetDirectoryName(Info.Location);
            Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (_overlayRoot) Destroy(_overlayRoot);
            if (_bundle) _bundle.Unload(false);
        }

        private static bool OutcomeTypeSupportsOutputs(Outcome outc)
        {
            if (outc == null) return false;
            if (outc is StartObjectiveOutcome) return true;
            return outc.SavedOutcome.Type == OutcomeType.StopOrCompleteObjective;
        }

        private static bool IsInMissionEditor() => SceneSingleton<MissionEditor>.i != null && MissionManager.Objectives != null;

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (s.name != "GameWorld") return;

            if (_sceneSetupCo != null) StopCoroutine(_sceneSetupCo);
            _sceneSetupCo = StartCoroutine(SceneSetupWhenReady(s));
        }

        private IEnumerator SceneSetupWhenReady(Scene s)
        {
            while (s.isLoaded && s.name == "GameWorld" && !IsInMissionEditor())
                yield return null;

            if (!s.isLoaded || s.name != "GameWorld") yield break;

            EnsureOverlayLoaded();

            while (s.isLoaded && s.name == "GameWorld" && !TryEnsureTopbarToggleButton())
                yield return null;

            if (_overlayRoot && _overlayRoot.activeSelf)
                RebuildGraph();

            _sceneSetupCo = null;
        }

        private void OnSceneUnloaded(Scene s)
        {
            if (_sceneSetupCo != null) { StopCoroutine(_sceneSetupCo); _sceneSetupCo = null; }

            _overlayToggleButton = null;
            _gridToggleButton = null;
            _holdPosToggle = null;
            objectivesBtn = null;
            editormenu = null;
            _view = null;
            _nameInputShrunk = false;
            if (_overlayRoot)
            {
                Destroy(_overlayRoot);
                _overlayRoot = null;
            }
        }

        private static List<Objective> GetCompleteList(Outcome outc, bool createIfMissing = false)
        {

            Type t = outc.GetType();
            if (!_completeObjListField.TryGetValue(t, out var fi))
            {
                const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                fi = t.GetField("objectivesToStart", BF);
                _completeObjListField[t] = fi;
            }

            List<Objective> list = (List<Objective>)_completeObjListField[t]?.GetValue(outc);
            if (list == null && createIfMissing && _completeObjListField[t] != null)
            {
                list = [];
                _completeObjListField[t].SetValue(outc, list);
            }
            return list;
        }
        private bool TryEnsureTopbarToggleButton()
        {
            if (!IsInMissionEditor()) return false;

            if (_overlayToggleButton && _gridToggleButton && _holdPosToggle) return true;

            objectivesBtn = FindObjectsOfType<ChangeTabButton>(true)
                .FirstOrDefault(b => b && string.Equals(b.name, "ObjectivesButton", StringComparison.OrdinalIgnoreCase));
            if (!objectivesBtn) return false;

            Button template = objectivesBtn.GetComponent<Button>();
            if (!template) return false;

            Transform parent = template.transform?.parent;
            if (!parent) return false;
            ShrinkTopbarFirstSibling(parent, 0.7f);
            _overlayToggleButton ??= EnsureToolbarButton(
                parent, template, "EditorPlusToggleButton", "Graph",
                () =>
                {
                    if (!IsInMissionEditor() || !EnsureOverlayLoaded()) return;
                    bool show = !_overlayRoot.activeSelf;
                    if (!show && _view) _view.ClearUnitGhosts();
                    _overlayRoot.SetActive(show);
                    ApplyLeftPanelOffset(show);
                    if (show) RebuildGraph();
                });

            _gridToggleButton ??= EnsureToolbarButton(
                parent, template, "EditorPlusGridButton", "Grid",
                () =>
                {
                    if (!IsInMissionEditor() || !EnsureOverlayLoaded()) return;
                    _view?.ToggleBackgroundAndGrid();
                });

            Toggle autoSaveTemplate = parent.GetComponentsInChildren<Toggle>(true)
                .FirstOrDefault(t => t && string.Equals(t.name, "AutoSaveToggle", StringComparison.OrdinalIgnoreCase));
            _holdPosToggle ??= EnsureToolbarToggle(
                parent,
                autoSaveTemplate,
                "HoldPosToggle",
                "Hold Pos",
                Instance.holdpos,
                v =>
                {
                    Instance.holdpos = v;
                });

            return _overlayToggleButton && _gridToggleButton && _holdPosToggle;
        }
        private static Toggle EnsureToolbarToggle(
            Transform parent,
            Toggle template,
            string goName,
            string hoverText,
            bool initialValue,
            Action<bool> onValueChanged)
        {
            if (!template) return null;

            Transform existingTf = parent.Find(goName);
            GameObject go = existingTf ? existingTf.gameObject : Instantiate(template.gameObject, parent);
            go.name = goName;
            go.SetActive(true);

            Toggle t = go.GetComponent<Toggle>();
            if (!t) t = go.AddComponent<Toggle>();

            t.onValueChanged.RemoveAllListeners();
            t.SetIsOnWithoutNotify(initialValue);
            t.onValueChanged.AddListener(v => onValueChanged?.Invoke(v));
            t.interactable = true;

            ShowHoverText hover = go.GetComponentInChildren<ShowHoverText>(true);
            if (hover) hover.SetText(hoverText);
            TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = hoverText;

            go.transform.SetAsLastSibling();
            return t;
        }

        private void ShrinkTopbarFirstSibling(Transform parent, float factor = 0.7f)
        {
            if (_nameInputShrunk) return;
            if (parent?.Find("MissionNameInput") is not RectTransform rt) return;

            rt.offsetMax = new(rt.offsetMax.x * factor, rt.offsetMax.y);
            _nameInputShrunk = true;
        }

        private void ApplyLeftPanelOffset(bool on)
        {
            if (!_leftPanelRT)
            {
                _leftPanelRT = GameObject.Find("LeftPanel")?.GetComponent<RectTransform>();

                if (!_leftPanelRT)
                {
                    ObjectiveEditorV2 editor = editormenu ? editormenu : FindObjectOfType<ObjectiveEditorV2>(true);
                    if (editor)
                        _leftPanelRT = editor.GetComponentsInParent<RectTransform>(true)
                            .FirstOrDefault(rt => string.Equals(rt.name, "LeftPanel", StringComparison.OrdinalIgnoreCase));
                }
                if (_leftPanelRT && !_leftPanelOffsetApplied)
                    _leftPanelOriginalAnchored = _leftPanelRT.anchoredPosition;
            }

            if (!_leftPanelRT) return;

            if (on)
            {
                if (_leftPanelOffsetApplied) return;
                _leftPanelRT.anchoredPosition = _leftPanelOriginalAnchored + new Vector2(LeftPanelShiftX, 0f);
                _leftPanelOffsetApplied = true;
            }
            else
            {
                if (!_leftPanelOffsetApplied) return;
                _leftPanelRT.anchoredPosition = _leftPanelOriginalAnchored;
                _leftPanelOffsetApplied = false;
            }
        }

        private static Button EnsureToolbarButton(
            Transform parent,
            Button template,
            string goName,
            string hoverText,
            Action onClick)
        {
            Transform existingTf = parent.Find(goName);
            GameObject go = existingTf ? existingTf.gameObject : Instantiate(template.gameObject, parent);
            go.name = goName;

            Button btn = go.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => onClick?.Invoke());

            ShowHoverText label = go.GetComponentInChildren<ShowHoverText>(true);
            if (label) label.SetText(hoverText);

            go.transform.SetAsLastSibling();
            return btn;
        }

        private static IEnumerable<Func<Vector3>> EnumerateUnitWorldGetters(string uniqueId, bool isObjective)
        {
            MissionObjectives mo = MissionManager.Objectives;
            if (mo == null) yield break;

            if (isObjective)
            {
                Objective obj = mo.AllObjectives.FirstOrDefault(o => o.SavedObjective.UniqueName == uniqueId);
                if (obj == null) yield break;

                FieldInfo fi = FindFieldRecursive(obj.GetType(), "allItems");
                if (fi != null && fi.GetValue(obj) is IEnumerable items)
                {
                    foreach (object it in items)
                    {
                        if (it is SavedUnit su && su != null)
                        {
                            SavedUnit suLocal = su;
                            yield return () => suLocal.globalPosition.AsVector3() + Datum.originPosition;
                            continue;
                        }

                        if (it is SavedAirbase ab && ab != null)
                        {
                            SavedAirbase abLocal = ab;
                            yield return () => abLocal.Center.AsVector3() + Datum.originPosition;
                            continue;
                        }

                        if (it is Waypoint wp && wp != null)
                        {
                            Waypoint wpLocal = wp;
                            yield return () => wpLocal.GlobalPosition.Value.AsVector3() + Datum.originPosition;
                            continue;
                        }
                    }
                }

                yield break;
            }

            var ow = mo.AllOutcomes.FirstOrDefault(oc => oc.SavedOutcome.UniqueName == uniqueId);
            if (ow == null) yield break;

            foreach (var su in EnumerateFromObject<SavedUnit>(ow))
            {
                var local = su;
                yield return () => local.globalPosition.AsVector3() + Datum.originPosition;
            }

            var saved = GetPropOrFieldValue(ow, "SavedOutcome");
            foreach (var su in EnumerateFromObject<SavedUnit>(saved))
            {
                var local = su;
                yield return () => local.globalPosition.AsVector3() + Datum.originPosition;
            }

        }
        static readonly Dictionary<(Type, Type), MemberInfo[]> s_EnumerableMembersCache = [];

        static IEnumerable<T> EnumerateFromObject<T>(object src)
        {
            if (src == null) yield break;

            var t = src.GetType();
            if (!s_EnumerableMembersCache.TryGetValue((t, typeof(T)), out var members))
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var list = new List<MemberInfo>();

                foreach (var f in t.GetFields(F))
                    if (IsEnumerableOf<T>(f.FieldType)) list.Add(f);

                foreach (var p in t.GetProperties(F))
                    if (p.CanRead && p.GetIndexParameters().Length == 0 && IsEnumerableOf<T>(p.PropertyType)) list.Add(p);

                members = list.ToArray();
                s_EnumerableMembersCache[(t, typeof(T))] = members;
            }

            foreach (var m in members)
            {
                object v = m is FieldInfo f ? f.GetValue(src)
                           : m is PropertyInfo p ? p.GetValue(src, null)
                           : null;

                if (v == null) continue;

                if (v is IEnumerable<T> typed) { foreach (var e in typed) if (e != null) yield return e; }
                else if (v is IEnumerable any) { foreach (var e in any) if (e is T te) yield return te; }
            }

            static bool IsEnumerableOf<TElem>(Type ft)
            {
                if (typeof(IEnumerable<TElem>).IsAssignableFrom(ft)) return true;
                if (!typeof(IEnumerable).IsAssignableFrom(ft)) return false;
                if (ft.IsGenericType)
                {
                    var ga = ft.GetGenericArguments();
                    if (ga.Length == 1 && typeof(TElem).IsAssignableFrom(ga[0])) return true;
                }
                return false;
            }
        }

        private static FieldInfo FindFieldRecursive(Type t, string name)
        {
            if (t == null) return null;
            if (!s_AllItemsFieldCache.TryGetValue(t, out FieldInfo fi))
            {
                fi = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? FindFieldRecursive(t.BaseType, name);
                s_AllItemsFieldCache[t] = fi;
            }
            return fi;
        }

        private static object GetPropOrFieldValue(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return t.GetProperty(name, F)?.GetValue(obj, null) ?? t.GetField(name, F)?.GetValue(obj);
        }

        private bool EnsureOverlayLoaded()
        {
            if (_overlayRoot) return true;

            string path = Directory.EnumerateFiles(_modPath, "*.noep", SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (string.IsNullOrEmpty(path))
            {
                Logger.LogError($"No .noep file found in '{_modPath}'.");
                return false;
            }

            if (_bundle == null)
            {
                _bundle = AssetBundle.LoadFromFile(path);
                if (!_bundle) { Logger.LogError($"Failed to load bundle: {path}"); return false; }
            }

            var prefab = _bundle.LoadAsset<GameObject>("GraphOverlayPanel");
            if (!prefab) { Logger.LogError("Prefab 'GraphOverlayPanel' not found in bundle."); return false; }

            if (!TryFindHostCanvas(out var hostCanvas))
            {
                base.Logger.LogError("Could not find a suitable Canvas under 'SceneEssentials/Canvas'.");
                return false;
            }

            _overlayRoot = Instantiate(prefab, hostCanvas.transform, false);
            _overlayRoot.name = "MissionGraph_OverlayPanel";
            var rt = (RectTransform)_overlayRoot.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _overlayRoot.transform.SetAsFirstSibling();

            _view = _overlayRoot.GetComponent<GraphView>();
            if (!_view)
            {
                base.Logger.LogError("GraphView not found on overlay panel prefab.");
                return false;
            }

            _view.OnLink = (fromId, fromIsObj, toId, toIsObj) =>
            {
                try
                {
                    Logger.LogInfo($"[MissionGraph] LINK request: {fromId}({(fromIsObj ? "OBJ" : "OUT")}) > {toId}({(toIsObj ? "OBJ" : "OUT")})");

                    var mo = MissionManager.Objectives;
                    var o = mo.AllObjectives.FirstOrDefault(x => x.SavedObjective.UniqueName == (fromIsObj ? fromId : toId));
                    var oc = mo.AllOutcomes.FirstOrDefault(x => x.SavedOutcome.UniqueName == (fromIsObj ? toId : fromId));

                    if (o == null || oc == null)
                    {
                        Logger.LogError($"[MissionGraph] LINK resolve failed. UI/model out of sync. fromId={fromId} toId={toId} fromIsObj={fromIsObj} toIsObj={toIsObj}");
                        return;
                    }

                    if (fromIsObj && !toIsObj)
                    {
                        int before = o.Outcomes.Count;
                        if (o.Outcomes.Contains(oc)) return;
                        o.Outcomes.Add(oc);
                        int after = o.Outcomes.Count;
                        Logger.LogInfo($"Linked OUT '{oc.SavedOutcome.UniqueName}' to OBJ '{o.SavedObjective.UniqueName}' (count {before}→{after}).");
                        SceneSingleton<MissionEditor>.i.CheckAutoSave();
                    }
                    else if (!fromIsObj && toIsObj)
                    {
                        bool added = TryAddObjectiveReferenceToOutcome(oc, o);
                        if (added)
                        {
                            Logger.LogInfo($"[MissionGraph] Outcome '{oc.SavedOutcome.UniqueName}' now references OBJ '{o.SavedObjective.UniqueName}'.");
                            SceneSingleton<MissionEditor>.i.CheckAutoSave();
                            Logger.LogInfo("[MissionGraph] AutoSave requested (link).");
                        }
                        else
                        {
                            Logger.LogWarning($"[MissionGraph] Link no-op: outcome type unsupported or reference already present. out='{oc.SavedOutcome.UniqueName}' > obj='{o.SavedObjective.UniqueName}'.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[MissionGraph] LINK apply crashed: {ex}");
                }
            };

            _view.OnUnlink = (fromId, fromIsObj, toId, toIsObj) =>
            {
                try
                {
                    Logger.LogInfo($"[MissionGraph] UNLINK request: {fromId}({(fromIsObj ? "OBJ" : "OUT")}) > {toId}({(toIsObj ? "OBJ" : "OUT")})");

                    var mo = MissionManager.Objectives;
                    var o = mo.AllObjectives.FirstOrDefault(x => x.SavedObjective.UniqueName == (fromIsObj ? fromId : toId));
                    var oc = mo.AllOutcomes.FirstOrDefault(x => x.SavedOutcome.UniqueName == (fromIsObj ? toId : fromId));
                    if (o == null || oc == null)
                    {
                        Logger.LogError($"[MissionGraph] UNLINK resolve failed. UI/model out of sync. fromId={fromId} toId={toId} fromIsObj={fromIsObj} toIsObj={toIsObj}");
                        return;
                    }

                    bool changed = false;
                    if (fromIsObj && !toIsObj)
                    {
                        if (o.Outcomes.Contains(oc)) { o.Outcomes.Remove(oc); changed = true; }
                    }
                    else if (!fromIsObj && toIsObj)
                    {
                        changed = RemoveObjectiveReferenceFromOutcome(oc, o);
                    }

                    if (changed)
                    {
                        Logger.LogInfo($"[MissionGraph] Unlinked '{fromId}' × '{toId}'.");
                        SceneSingleton<MissionEditor>.i.CheckAutoSave();
                        Logger.LogInfo("[MissionGraph] AutoSave requested (unlink).");
                    }
                    else
                    {
                        Logger.LogWarning($"[MissionGraph] Unlink no-op: relationship not found. fromId={fromId} toId={toId}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[MissionGraph] UNLINK apply crashed: {ex}");
                }
            };
            _view.SetWorldCamera(Camera.main);
            _view.OnEditObjective = id => StartCoroutine(OpenAndEditObjective(id));
            _view.OnEditOutcome = id => StartCoroutine(OpenAndEditOutcome(id));
            _view.QueryUnitWorldPositions = (id, isObj) => EnumerateUnitWorldGetters(id, isObj);
            _overlayRoot.SetActive(false);
            return true;
        }

        private IEnumerator EnsureEditorMenu()
        {
            if (editormenu && editormenu.gameObject.activeInHierarchy) yield break;

            editormenu = null;
            editormenu = FindObjectOfType<ObjectiveEditorV2>();
            if (editormenu) yield break;

            objectivesBtn ??= FindObjectsOfType<ChangeTabButton>(true).FirstOrDefault(b => b && string.Equals(b.name, "ObjectivesButton", StringComparison.OrdinalIgnoreCase));
            if (!objectivesBtn) yield break;

            objectivesBtn.ToggleTab(true);

            const float timeout = 2f;
            float start = Time.realtimeSinceStartup;
            while (!(editormenu = FindObjectOfType<ObjectiveEditorV2>()) &&
                   Time.realtimeSinceStartup - start < timeout)
            {
                yield return null;
            }
        }

        private IEnumerator OpenAndEditObjective(string uniqueName)
        {
            yield return EnsureEditorMenu();
            if (!editormenu) yield break;

            editormenu.ShowObjectiveList();
            yield return null;
            var mo = MissionManager.Objectives;
            var obj = mo.AllObjectives.FirstOrDefault(o => o.SavedObjective.UniqueName == uniqueName);
            if (obj != null)
                editormenu.ShowEditObjective(obj);
        }

        private IEnumerator OpenAndEditOutcome(string uniqueName)
        {
            yield return EnsureEditorMenu();
            if (!editormenu) yield break;

            editormenu.ShowOutcomeList();
            yield return null;
            var mo = MissionManager.Objectives;
            int idx = mo.AllOutcomes.FindIndex(oc => oc.SavedOutcome.UniqueName == uniqueName);
            if (idx < 0) yield break;

            _miShowEditOutcome ??= typeof(ObjectiveEditorV2).GetMethod("ShowEditOutcome", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_miShowEditOutcome == null) yield break;
            _miShowEditOutcome?.Invoke(editormenu, [idx]);
        }

        private static bool TryAddObjectiveReferenceToOutcome(Outcome outc, Objective obj)
        {
            if (outc is StartObjectiveOutcome so)
            {
                so.objectivesToStart ??= new List<Objective>();
                if (so.objectivesToStart.Contains(obj)) return false;
                so.objectivesToStart.Add(obj);
                return true;
            }

            if (outc.SavedOutcome.Type == OutcomeType.StopOrCompleteObjective)
            {
                var list = GetCompleteList(outc, createIfMissing: true);
                if (list.Contains(obj)) return false;
                list.Add(obj);
                return true;
            }

            return false;
        }

        private static bool RemoveObjectiveReferenceFromOutcome(Outcome outc, Objective obj)
        {
            if (outc is StartObjectiveOutcome so)
                return so.objectivesToStart != null && so.objectivesToStart.Remove(obj);

            if (outc.SavedOutcome.Type == OutcomeType.StopOrCompleteObjective)
            {
                var list = GetCompleteList(outc);
                return list != null && list.Remove(obj);
            }

            return false;
        }

        private static bool TryFindHostCanvas(out Canvas host)
        {
            host = null;
            var container = GameObject.Find("SceneEssentials/Canvas");
            if (container)
            {
                var canvases = container.GetComponentsInChildren<Canvas>(true);
                Canvas pick = null;
                foreach (var c in canvases)
                {
                    if (!c.isActiveAndEnabled) continue;
                    if (c.name.Contains("Menu", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pick == null || c.sortingOrder >= pick.sortingOrder) pick = c;
                    }
                }
                if (pick == null)
                {
                    foreach (var c in canvases)
                        if (pick == null || c.sortingOrder >= pick.sortingOrder) pick = c;
                }

                if (pick != null) { host = pick; return true; }
            }
            host = FindObjectOfType<Canvas>();
            return host != null;
        }

        private void RebuildGraph()
        {

            Logger.LogDebug("[Graph] Rebuild start");

            var mo = MissionManager.Objectives;
            if (mo == null) return;

            var objectives = mo.AllObjectives.Select(o => new GraphView.ObjectiveDTO
            {
                Id = o.SavedObjective.UniqueName,
                UniqueName = o.SavedObjective.UniqueName,
                DisplayName = o.SavedObjective.DisplayName,
                TypeName = o.SavedObjective.Type.ToString(),
                Hidden = o.SavedObjective.Hidden,
                OutcomeCount = 0,
                Layer = 0,
                Row = 0,
                FactionName = (o as IHasFaction)?.FactionName
            }).ToArray();

            var outcomes = mo.AllOutcomes.Select(uc => new GraphView.OutcomeDTO
            {
                Id = uc.SavedOutcome.UniqueName,
                UniqueName = uc.SavedOutcome.UniqueName,
                TypeName = uc.SavedOutcome.Type.ToString(),
                UsedByCount = 0,
                Layer = 0,
                Row = 0
            }).ToArray();

            List<GraphView.LinkDTO> links = [];
            foreach (Objective o in mo.AllObjectives)
            {
                string oid = o.SavedObjective.UniqueName;
                foreach (var oc in o.Outcomes)
                    links.Add(new GraphView.LinkDTO { FromId = oid, FromIsObjective = true, ToId = oc.SavedOutcome.UniqueName, ToIsObjective = false });
            }
            foreach (Outcome oc in mo.AllOutcomes)
            {
                string uid = oc.SavedOutcome.UniqueName;
                foreach (Objective nextObj in ReflectObjectives(oc))
                    links.Add(new GraphView.LinkDTO { FromId = uid, FromIsObjective = false, ToId = nextObj.SavedObjective.UniqueName, ToIsObjective = true });
            }

            _view.CanOutcomeHaveOutputs = (outcomeId) =>
            {
                Outcome oc = mo.AllOutcomes.FirstOrDefault(x => x.SavedOutcome.UniqueName == outcomeId);
                return OutcomeTypeSupportsOutputs(oc);
            };

            _view.BuildGraph(new GraphView.GraphData
            {
                Objectives = objectives,
                Outcomes = outcomes,
                Links = [.. links]
            }, computeLayout: true, snapshotAsFull: true);

            Logger.LogDebug($"[Graph] Rebuild done: objectives={objectives.Length}, outcomes={outcomes.Length}, links={links.Count}");
        }

        private static IEnumerable<Objective> ReflectObjectives(Outcome outc)
        {
            if (outc is StartObjectiveOutcome so)
                return so.objectivesToStart ?? (IEnumerable<Objective>)[];

            if (outc.SavedOutcome.Type == OutcomeType.StopOrCompleteObjective)
                return GetCompleteList(outc) ?? (IEnumerable<Objective>)[];

            return [];
        }

        [HarmonyPatch(typeof(MissionEditor), nameof(MissionEditor.RegisterNewUnit), [typeof(Unit), typeof(string)])]
        internal static class MissionEditor_RegisterNewUnit_Patch
        {
            static void Postfix(ref SavedUnit __result)
            {
                if (__result is SavedVehicle v)
                {
                    v.holdPosition = Instance.holdpos;
                }
                if (__result is SavedShip s)
                {
                    s.holdPosition = Instance.holdpos;
                }
            }
        }

        [HarmonyPatch(typeof(EditorHandle), "ClampY", [typeof(Unit), typeof(GlobalPosition)])]
        internal static class EditorHandle_ClampY_Patch
        {
            static void Prefix(GlobalPosition position, out float __state) => __state = position.y;
            static void Postfix(ref GlobalPosition __result, float __state) => __result.y = Mathf.Max(__state, __result.y);

        }

        [HarmonyPatch(typeof(UnitMenu), "PlaceUnit", [])]
        static class UnitMenu_PlaceUnit_Patch
        {
            static readonly MethodInfo mi = AccessTools.Method(typeof(UnitMenu), "StartPlaceUnit", Type.EmptyTypes);

            static void Postfix(UnitMenu __instance)
            {
                if (Input.GetKey(KeyCode.LeftControl))
                    HarmonyCoroutineRunner.Instance.StartCoroutine(CallAtEndOfFrame(__instance));
            }

            static IEnumerator CallAtEndOfFrame(UnitMenu instance)
            {
                yield return new WaitForEndOfFrame();
                mi.Invoke(instance, null);
            }
        }

        public class HarmonyCoroutineRunner : MonoBehaviour
        {
            static HarmonyCoroutineRunner _instance;
            public static HarmonyCoroutineRunner Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        var go = new GameObject("HarmonyCoroutineRunner");
                        DontDestroyOnLoad(go);
                        _instance = go.AddComponent<HarmonyCoroutineRunner>();
                    }
                    return _instance;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Vector3DataField), "Setup", [typeof(string), typeof(IValueWrapper<Vector3>)])]
    static class Snap_Patch
    {
        static void Postfix(Vector3DataField __instance, string label)
        {
            bool isPos = string.Equals(label, "Position", StringComparison.OrdinalIgnoreCase);
            if (!(isPos || string.Equals(label, "Rotation", StringComparison.OrdinalIgnoreCase))) return;


            TMP_InputField anyInput = __instance.GetComponentInChildren<TMP_InputField>();
            if (!anyInput) return;

            RectTransform row = anyInput.transform.parent as RectTransform;
            if (!row) return;

            string name = isPos ? "SnapInput_Pos" : "SnapInput_Rot";
            _ = row.Find(name)?.GetComponent<TMP_InputField>() ?? CloneSnap(anyInput, row, name, isPos);

            HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
            if (hlg)
            {
                hlg.childControlWidth = true;
                hlg.childForceExpandWidth = true;
            }
        }

        static RuntimeTransformHandle _cachedHandle;
        static RuntimeTransformHandle GetHandle() =>
            _cachedHandle ? _cachedHandle : (_cachedHandle = UnityEngine.Object.FindObjectOfType<RuntimeTransformHandle>());


        static TMP_InputField CloneSnap(TMP_InputField template, Transform parent, string name, bool isPos)
        {
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = name;

            TMP_InputField snap = go.GetComponent<TMP_InputField>();
            if (!snap) return null;

            snap.onEndEdit.RemoveAllListeners();
            snap.contentType = TMP_InputField.ContentType.DecimalNumber;

            LayoutElement le = snap.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minWidth = 55f;

            RuntimeTransformHandle h = GetHandle();

            float value = h ? (isPos ? h.positionSnap.x : h.rotationSnap) : 0f;
            snap.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));

            snap.onEndEdit.AddListener(v =>
            {
                if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float val)) return;
                val = Mathf.Max(0f, val);

                RuntimeTransformHandle handle = GetHandle();
                if (!handle) return;

                if (isPos) handle.positionSnap = new Vector3(val, val, val);
                else handle.rotationSnap = val;
            });

            return snap;
        }
    }

    [HarmonyPatch(typeof(Dropdown), "Show")]
    static class Dropdown_Patch
    {
        static readonly FieldInfo F_List = AccessTools.Field(typeof(Dropdown), "m_Dropdown");

        static void Postfix(Dropdown __instance)
        {
            if (F_List?.GetValue(__instance) is not GameObject listGo) return;
            if (listGo.TryGetComponent(out RectTransform listRt))
            {
                int count = Mathf.Max(1, __instance.options?.Count ?? 0);
                listRt.sizeDelta = new(listRt.sizeDelta.x, count * 20 + 8);
            }
        }
    }
}