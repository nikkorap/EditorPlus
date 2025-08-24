// Plugin.cs
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
        private static readonly Dictionary<Type, FieldInfo> _completeObjListField = new();
        private Button _overlayToggleButton;
        private Button _gridToggleButton;
        private static readonly Dictionary<Type, FieldInfo> s_AllItemsFieldCache = new();
        private static readonly Dictionary<Type, MemberInfo[]> s_UnitsMembersCache = new();
        private ObjectiveEditorV2 editormenu;
        private ChangeTabButton objectivesBtn;
        private RectTransform _leftPanelRT;
        private Vector2 _leftPanelOriginalAnchored;
        private bool _leftPanelOffsetApplied;
        private const float LeftPanelShiftX = -530f;

        void Awake()
        {
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
            objectivesBtn = null;     
            editormenu = null;
            _view = null;

            if (_overlayRoot)
            {
                Destroy(_overlayRoot);
                _overlayRoot = null;
            }
        }

        private static List<Objective> GetCompleteList(Outcome outc, bool createIfMissing = false)
        {

            var t = outc.GetType();
            if (!_completeObjListField.TryGetValue(t, out var fi))
            {
                const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                fi = t.GetField("objectivesToStart", BF);
                _completeObjListField[t] = fi;
            }

            var list = (List<Objective>)_completeObjListField[t]?.GetValue(outc);
            if (list == null && createIfMissing && _completeObjListField[t] != null)
            {
                list = new List<Objective>();
                _completeObjListField[t].SetValue(outc, list);
            }
            return list;
        }
        private bool TryEnsureTopbarToggleButton()
        {
            if (!IsInMissionEditor()) return false;

            if (_overlayToggleButton && _gridToggleButton) return true;

            objectivesBtn = FindObjectsOfType<ChangeTabButton>(true).FirstOrDefault(b => b && string.Equals(b.name, "ObjectivesButton", StringComparison.OrdinalIgnoreCase));
            if (!objectivesBtn) return false;

            var template = objectivesBtn.GetComponent<Button>();
            if (!template) return false;

            var parent = template.transform?.parent;
            if (!parent) return false;

            _overlayToggleButton ??= EnsureToolbarButton(
                parent, template, "EditorPlusToggleButton", "Graph",
                () =>
                {
                    if (!IsInMissionEditor() || !EnsureOverlayLoaded()) return;
                    var show = !_overlayRoot.activeSelf;
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

            return _overlayToggleButton && _gridToggleButton;
        }
        private void ApplyLeftPanelOffset(bool on)
        {
            if (!_leftPanelRT)
            {
                _leftPanelRT = GameObject.Find("LeftPanel")?.GetComponent<RectTransform>();

                if (!_leftPanelRT)
                {
                    var editor = editormenu ? editormenu : FindObjectOfType<ObjectiveEditorV2>(true);
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
            var existingTf = parent.Find(goName);
            var go = existingTf ? existingTf.gameObject : Instantiate(template.gameObject, parent);
            go.name = goName;

            var btn = go.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var label = go.GetComponentInChildren<ShowHoverText>(true);
            if (label) label.SetText(hoverText);

            go.transform.SetAsLastSibling();
            return btn;
        }

        private static IEnumerable<Func<Vector3>> EnumerateUnitWorldGetters(string uniqueId, bool isObjective)
        {
            var mo = MissionManager.Objectives;
            if (mo == null) yield break;

            if (isObjective)
            {
                var obj = mo.AllObjectives.FirstOrDefault(o => o.SavedObjective.UniqueName == uniqueId);
                if (obj == null) yield break;

                var fi = FindFieldRecursive(obj.GetType(), "allItems");
                if (fi != null && fi.GetValue(obj) is IEnumerable items)
                {
                    foreach (var it in items)
                    {
                        if (it is SavedUnit su && su != null)
                        {
                            var suLocal = su;
                            yield return () => suLocal.globalPosition.AsVector3() + Datum.originPosition;
                        }
                    }
                }
                yield break;
            }

            var ow = mo.AllOutcomes.FirstOrDefault(oc => oc.SavedOutcome.UniqueName == uniqueId);
            if (ow == null) yield break;

            foreach (var su in EnumerateSavedUnitsFromObject(ow))
            {
                var suLocal = su;
                yield return () => suLocal != null
                    ? suLocal.globalPosition.AsVector3() + Datum.originPosition
                    : new Vector3(float.NaN, float.NaN, float.NaN);
            }

            var saved = GetPropOrFieldValue(ow, "SavedOutcome");
            if (saved != null)
            {
                foreach (var su in EnumerateSavedUnitsFromObject(saved))
                {
                    var suLocal = su;
                    yield return () => suLocal != null
                        ? suLocal.globalPosition.AsVector3() + Datum.originPosition
                        : new Vector3(float.NaN, float.NaN, float.NaN);
                }
            }
        }

        private static FieldInfo FindFieldRecursive(Type t, string name)
        {
            if (t == null) return null;
            if (!s_AllItemsFieldCache.TryGetValue(t, out var fi))
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
            var t = obj.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return t.GetProperty(name, F)?.GetValue(obj, null) ?? t.GetField(name, F)?.GetValue(obj);
        }

        private static IEnumerable<SavedUnit> EnumerateSavedUnitsFromObject(object src)
        {
            if (src == null) yield break;

            var t = src.GetType();
            if (!s_UnitsMembersCache.TryGetValue(t, out var members))
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var list = new List<MemberInfo>();
                list.AddRange(t.GetFields(F).Where(IsSavedUnitEnumerableField));
                list.AddRange(t.GetProperties(F).Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsSavedUnitEnumerableProperty(p)));
                members = list.ToArray();
                s_UnitsMembersCache[t] = members;
            }

            foreach (var m in members)
            {
                object value = m is FieldInfo f ? f.GetValue(src)
                                : m is PropertyInfo p ? p.GetValue(src, null)
                                : null;
                if (value == null) continue;

                if (value is IEnumerable<SavedUnit> typed)
                {
                    foreach (var su in typed) if (su != null) yield return su;
                    continue;
                }
                if (value is IEnumerable any)
                    foreach (var it in any) if (it is SavedUnit su && su != null) yield return su;
            }

            static bool IsSavedUnitEnumerableField(FieldInfo f) => IsSavedUnitEnumerableType(f.FieldType);
            static bool IsSavedUnitEnumerableProperty(PropertyInfo p) => IsSavedUnitEnumerableType(p.PropertyType);
            static bool IsSavedUnitEnumerableType(Type ft)
            {
                if (typeof(IEnumerable<SavedUnit>).IsAssignableFrom(ft)) return true;
                if (!typeof(IEnumerable).IsAssignableFrom(ft)) return false;
                if (ft.IsGenericType)
                {
                    var ga = ft.GetGenericArguments();
                    if (ga.Length == 1 && typeof(SavedUnit).IsAssignableFrom(ga[0])) return true;
                }
                return false;
            }
        }

        private bool EnsureOverlayLoaded()
        {
            if (_overlayRoot) return true;

            string path = Directory.EnumerateFiles(_modPath, "*.noep", SearchOption.TopDirectoryOnly)
                                   .FirstOrDefault();

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

            _miShowEditOutcome ??= typeof(ObjectiveEditorV2) .GetMethod("ShowEditOutcome", BindingFlags.Instance | BindingFlags.NonPublic);
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

            var links = new List<GraphView.LinkDTO>();
            foreach (var o in mo.AllObjectives)
            {
                var oid = o.SavedObjective.UniqueName;
                foreach (var oc in o.Outcomes)
                    links.Add(new GraphView.LinkDTO { FromId = oid, FromIsObjective = true, ToId = oc.SavedOutcome.UniqueName, ToIsObjective = false });
            }
            foreach (var oc in mo.AllOutcomes)
            {
                var uid = oc.SavedOutcome.UniqueName;
                foreach (var nextObj in ReflectObjectives(oc))
                    links.Add(new GraphView.LinkDTO { FromId = uid, FromIsObjective = false, ToId = nextObj.SavedObjective.UniqueName, ToIsObjective = true });
            }

            _view.CanOutcomeHaveOutputs = (outcomeId) =>
            {
                var oc = mo.AllOutcomes.FirstOrDefault(x => x.SavedOutcome.UniqueName == outcomeId);
                return OutcomeTypeSupportsOutputs(oc);
            };

            _view.BuildGraph(new GraphView.GraphData
            {
                Objectives = objectives,
                Outcomes = outcomes,
                Links = links.ToArray()
            }, computeLayout: true, snapshotAsFull: true);

            Logger.LogDebug($"[Graph] Rebuild done: objectives={objectives.Length}, outcomes={outcomes.Length}, links={links.Count}");

        }

        [HarmonyPatch]
        internal static class EditorHandle_ClampY_Patch
        {
            static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(EditorHandle), "ClampY", [typeof(Unit), typeof(GlobalPosition)]);
            static bool Prefix(Unit unit, GlobalPosition position, ref GlobalPosition __result)
            {
                __result = position;
                return false;
            }
        }

        private static IEnumerable<Objective> ReflectObjectives(Outcome outc)
        {
            if (outc is StartObjectiveOutcome so)
                return (IEnumerable<Objective>)(so.objectivesToStart ?? (IEnumerable<Objective>)Array.Empty<Objective>());

            if (outc.SavedOutcome.Type == OutcomeType.StopOrCompleteObjective)
                return (IEnumerable<Objective>)(GetCompleteList(outc) ?? (IEnumerable<Objective>)Array.Empty<Objective>());

            return Array.Empty<Objective>();
        }
    }
}