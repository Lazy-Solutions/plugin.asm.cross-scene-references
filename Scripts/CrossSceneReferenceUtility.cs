#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE0062 // Make local function 'static'

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using AdvancedSceneManager.Models;

using scene = UnityEngine.SceneManagement.Scene;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using Component = UnityEngine.Component;
using UnityEngine.UIElements;
using Lazy.Utility;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Utility;

#if UNITY_EDITOR
using AdvancedSceneManager.Editor.Utility;
using UnityEditor.Callbacks;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace plugin.asm.crossSceneReferences
{

    /// <summary>An utility for saving and restoring cross-scene references.</summary>
    public static class CrossSceneReferenceUtility
    {

        /// <summary>Enables message when a cross-scene reference could not be resolved.</summary>
        public static bool unableToResolveCrossSceneReferencesWarning
        {
            get => PlayerPrefs.GetInt("AdvancedSceneManager.Warnings.unableToResolveCrossSceneReferences") == 1;
            set => PlayerPrefs.SetInt("AdvancedSceneManager.Warnings.unableToResolveCrossSceneReferences", value ? 1 : 0);
        }

        #region SceneOperation

        static IEnumerator RestoreCrossSceneReferences()
        {

            var e = SceneUtility.GetAllOpenUnityScenes().GetEnumerator();
            var i = 0;
            while (e.MoveNext())
            {
                var e1 = RestoreCrossSceneReferencesWithWarnings_IEnumerator(e.Current, respectSettingsSuppressingWarnings: true);
                while (e1.MoveNext())
                {
                    i += 1;
                    if (i > 20)
                    {
                        i = 0;
                        yield return null;
                    }
                }
            }

        }

        #endregion
        #region Assets

        const string Key = "CrossSceneReferences";

        /// <summary>Loads cross-scene references for a scene.</summary>
        public static SceneCrossSceneReferenceCollection Load(string scenePath) =>
            SceneDataUtility.Get<SceneCrossSceneReferenceCollection>(scenePath, Key);

        public static SceneCrossSceneReferenceCollection[] Enumerate() =>
            SceneDataUtility.Enumerate<SceneCrossSceneReferenceCollection>(Key).Where(c => c.references?.Any() ?? false).ToArray();

#if UNITY_EDITOR

        /// <summary>Save the cross-scene references for a scene. This removes all previously added references for this scene.</summary>
        public static void Save(scene scene, params CrossSceneReference[] references) =>
            Save(new SceneCrossSceneReferenceCollection() { references = references, scene = scene.path }, scene.path);

        /// <summary>Saves a <see cref="CrossSceneReference"/>.</summary>
        public static void Save(SceneCrossSceneReferenceCollection reference, string scenePath)
        {
            SceneDataUtility.Set(scenePath, Key, reference);
            ClearReferenceStatusesForScene(scenePath);
            OnSaved?.Invoke();
        }

        /// <summary>Removes all cross-scene references for this scene.</summary>
        public static void Remove(scene scene) =>
            Remove(scene.path);

        /// <summary>Removes all cross-scene references for this scene.</summary>
        public static void Remove(string scene) =>
            SceneDataUtility.Unset(scene, Key);

        /// <summary>Removes all cross-scene references for this scene.</summary>
        public static void Remove(CrossSceneReference reference)
        {

            if (reference == null)
                return;

            var collection = SceneDataUtility.Get<SceneCrossSceneReferenceCollection>(reference.variable.scene, Key);
            var list = collection.references;
            var i = Array.FindIndex(list, r => r.variable.ToString() == reference.variable.ToString());
            if (i == -1)
                return;

            ArrayUtility.RemoveAt(ref list, i);
            collection.references = list;
            SceneDataUtility.Set(reference.variable.scene, Key, collection);

            RemoveReferenceStatus(reference.variable);

        }

#endif

        #region Convert from old version to SceneDataUtility

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void Convert()
        {

            var collections =
                Resources.LoadAll<TextAsset>("AdvancedSceneManager/Cross-scene references").
                Select(a => JsonUtility.FromJson<SceneCrossSceneReferenceCollection>(a.text)).
                OfType<SceneCrossSceneReferenceCollection>().
                ToArray();

            foreach (var collection in collections)
                SceneDataUtility.Set(collection.scene, "CrossSceneReferences", collection);

            var path = "Assets/Settings/Resources/AdvancedSceneManager/Cross-scene references";
            if (Directory.Exists(path))
                Directory.Delete(path, true);

        }
#endif

        /// <summary>A collection of <see cref="CrossSceneReference"/> for a scene.</summary>
        [Serializable]
        public class SceneCrossSceneReferenceCollection
        {
            public string scene;
            public CrossSceneReference[] references;
        }

        #endregion

        #endregion
        #region Editor

#if UNITY_EDITOR

        static CrossSceneReferenceUtility() =>
            SettingsTab.instance.Add(header: SettingsTab.instance.DefaultHeaders.Appearance_Hierarchy, callback: ShowHierarchyIconToggle);

        internal static bool showIndicator
        {
            get => EditorPrefs.GetBool("AdvancedSceneManager.ShowCrossSceneIndicator", true);
            set => EditorPrefs.SetBool("AdvancedSceneManager.ShowCrossSceneIndicator", value);
        }

        static VisualElement ShowHierarchyIconToggle()
        {
            var toggle = new Toggle("Display unresolved cross-scene reference icon:") { tooltip = "Enable or disable icon in hierarchy that indicates if a scene has unresolved cross-scene references (saved in EditorPrefs)" };
            toggle.SetValueWithoutNotify(showIndicator);
            _ = toggle.RegisterValueChangedCallback(e => { showIndicator = e.newValue; EditorApplication.RepaintHierarchyWindow(); });
            return toggle;
        }

        static GUIStyle hierarchyIconStyle;
        static bool OnSceneGUI(Rect position, scene scene, ref float width)
        {

            if (!Profile.current)
                return false;

            if (!showIndicator)
                return false;

            var references = GetInvalidReferences(scene).ToArray();
            if (!references.Any())
                return false;

            if (hierarchyIconStyle == null)
                hierarchyIconStyle = new GUIStyle() { padding = new RectOffset(2, 2, 2, 2) };

            _ = GUI.Button(position, new GUIContent(EditorGUIUtility.IconContent("orangeLight").image,
                      "This scene contains cross-scene references that could not be resolved. New cross-scene referenses in this scene will not be saved."), hierarchyIconStyle);

            return true;

        }

        static bool OnGameObjectGUI(Rect position, GameObject obj, ref float width)
        {

            if (!Profile.current)
                return false;

            if (!showIndicator)
                return false;

            var references = GetInvalidReferences(obj).ToArray();
            if (!references.Any())
                return false;

            var icon = EditorGUIUtility.IconContent("orangeLight").image;
            var tooltip = "The game object has cross-scene references that were unable to be resolved:" + Environment.NewLine +
                string.Join(Environment.NewLine,
                    references.Select(GetDisplayString));

            _ = GUI.Button(position, new GUIContent(icon, tooltip), hierarchyIconStyle);

            return true;

            string GetDisplayString(KeyValuePair<ObjectReference, ReferenceData> reference)
            {
                var index = reference.Key.Index;
                var str = index.HasValue ? " (" + index.Value + ")" : "";
                return reference.Value.component + "." + reference.Value.member + str + ": " + reference.Value.result.ToString();
            }

        }

#endif

        #endregion
        #region Models

        /// <summary>A reference to an object in a scene.</summary>
        [Serializable]
        public class ObjectReference : IEqualityComparer<ObjectReference>
        {

            public ObjectReference()
            { }

            public ObjectReference(scene scene, string objectID, FieldInfo field = null)
            {
                this.scene = scene.path;
                this.objectID = objectID;
                this.field = field?.Name;
            }

            /// <summary>Adds data about a component.</summary>
            public ObjectReference With(Component component)
            {
                componentType = component.GetType().AssemblyQualifiedName;
                componentTypeIndex = component.gameObject.GetComponents(component.GetType()).ToList().IndexOf(component);
                return this;
            }

            /// <summary>Adds data about an unity event.</summary>
            public ObjectReference With(int? unityEventIndex = null, int? arrayIndex = null)
            {
                if (unityEventIndex.HasValue) this.unityEventIndex = unityEventIndex.Value;
                if (arrayIndex.HasValue) this.arrayIndex = arrayIndex.Value;
                return this;
            }

            public string scene;
            public string objectID;

            public string componentType;
            public int componentTypeIndex;
            public string field;

            public int arrayIndex = -1;
            public int unityEventIndex = -1;

            public int? Index
            {
                get
                {
                    if (arrayIndex != -1)
                        return arrayIndex;
                    else if (unityEventIndex != -1)
                        return unityEventIndex;
                    return null;
                }
            }

            #region Get target, set value

            public enum FailReason
            {
                Succeeded, Unknown, SceneIsNotOpen, InvalidObjectPath, ComponentNotFound, InvalidField, TypeMismatch
            }

            public bool SetValue(object value, out Object target, out FailReason reasonForFailure, bool forceHierarchyScan, bool setValueIfNull = false)
            {

                target = null;

                if (!GetTarget(out var obj, out reasonForFailure, forceHierarchyScan))
                    return false;

                if (!GetField(obj, out var field, ref reasonForFailure))
                    return false;

                target = obj;
                if ((setValueIfNull && value == null) || field.FieldType.IsAssignableFrom(value.GetType()) || unityEventIndex != -1 || arrayIndex != -1)
                {

                    reasonForFailure = FailReason.Succeeded;

                    if (unityEventIndex != -1)
                        SetPersistentListener((UnityEvent)field.GetValue(obj), value as Object, ref reasonForFailure);
                    else if (arrayIndex != -1)
                        SetArrayElement((IList)field.GetValue(obj), value as Object, ref reasonForFailure);
                    else
                        SetField(field, target, value, ref reasonForFailure);

                    return reasonForFailure == FailReason.Succeeded;

                }
                else
                    return false;

            }

            #region Get

            public bool GetTarget(out Object component, out FailReason reasonForFailure, bool forceHierarchyScan)
            {

                component = null;
                reasonForFailure = FailReason.Unknown;

                if (!GetScene(this.scene, out var _, ref reasonForFailure))
                    return false;

                if (!GetObject(out var obj, ref reasonForFailure, forceHierarchyScan))
                    return false;

                reasonForFailure = FailReason.Succeeded;
                if (!string.IsNullOrEmpty(componentType))
                    return GetComponent(obj, componentType, componentTypeIndex, out component, ref reasonForFailure);
                else
                {
                    component = obj;
                    return true;
                }

            }

            bool GetScene(string scenePath, out scene scene, ref FailReason fail)
            {
                scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
                if (!scene.isLoaded)
                {
                    fail = FailReason.SceneIsNotOpen;
                    return false;
                }
                return true;
            }

            bool GetObject(out GameObject obj, ref FailReason fail, bool forceHierarchyScan)
            {

                if (GuidReferenceUtility.TryFindPersistent(objectID, out obj, forceHierarchyScan))
                    return true;

                fail = FailReason.InvalidObjectPath;
                return false;

            }

            bool GetComponent(GameObject obj, string name, int index, out Object component, ref FailReason fail)
            {

                if (name == null)
                {
                    component = null;
                    fail = FailReason.ComponentNotFound;
                    return false;
                }
                var type = Type.GetType(name, throwOnError: false);
                if (type == null)
                {
                    component = null;
                    fail = FailReason.ComponentNotFound;
                    return false;
                }

                component = obj.GetComponents(type).ElementAtOrDefault(index);
                if (!component)
                {
                    fail = FailReason.ComponentNotFound;
                    return false;
                }

                return true;

            }

            public bool GetField(object obj, out FieldInfo field, ref FailReason fail)
            {

                field = null;

                if (obj == null)
                {
                    fail = FailReason.InvalidField;
                    return false;
                }

                field = FindField(obj.GetType(), this.field);
                if (field == null)
                    fail = FailReason.InvalidField;

                return field != null;

            }

            #endregion
            #region Set

            void SetField(FieldInfo field, object target, object value, ref FailReason reasonForFailure)
            {
                if (EnsureCorrectType(value, field.FieldType, ref reasonForFailure))
                    field.SetValue(target, value);
            }

            void SetPersistentListener(UnityEvent ev, Object value, ref FailReason reasonForFailure)
            {

                var persistentCallsField = GetFields(typeof(UnityEvent)).FirstOrDefault(f => f.Name == "m_PersistentCalls");
                FieldInfo CallsField(object o) => GetFields(o.GetType()).FirstOrDefault(f => f.Name == "m_Calls");
                FieldInfo TargetField(object o) => GetFields(o.GetType()).FirstOrDefault(f => f.Name == "m_Target");

                var persistentCallGroup = persistentCallsField.GetValue(ev);
                var calls = CallsField(persistentCallGroup).GetValue(persistentCallGroup);
                var call = (calls as IList)[unityEventIndex];

                var field = TargetField(call);
                if (EnsureCorrectType(value, field.FieldType, ref reasonForFailure))
                    TargetField(call).SetValue(call, value);

            }

            void SetArrayElement(IList list, Object value, ref FailReason reasonForFailure)
            {

                var type = list.GetType().GetInterfaces().FirstOrDefault(t => t.IsGenericType).GenericTypeArguments[0];

                if (EnsureCorrectType(value, type, ref reasonForFailure))
                {
                    if (list.Count > arrayIndex)
                        list[arrayIndex] = value;
                }

            }

            bool EnsureCorrectType(object value, Type target, ref FailReason reasonForFailure)
            {

                var t = value?.GetType();

                if (t == null)
                    return true;
                if (target.IsAssignableFrom(t))
                    return true;

                reasonForFailure = FailReason.TypeMismatch;
                return false;

            }

            #endregion

            #endregion

            /// <summary>Evaluates path and returns <see cref="FailReason.Succeeded"/> if target path is okay, otherwise <see cref="FailReason"/> will indicate why not.</summary>
            public FailReason Evaluate(bool forceHierarchyScan = false)
            {

                if (!GetTarget(out var obj, out FailReason reasonForFailure, forceHierarchyScan))
                    return reasonForFailure;
                if (!GetField(obj, out _, ref reasonForFailure))
                    return reasonForFailure;

                return FailReason.Succeeded;

            }

            /// <summary>Returns true if the reference is still valid.</summary>
            public bool IsValid(bool returnTrueWhenSceneIsUnloaded = false)
            {

                var result = Evaluate();
                if (returnTrueWhenSceneIsUnloaded && result == FailReason.SceneIsNotOpen)
                    return true;
                else
                    return result == FailReason.Succeeded;

            }

            public override string ToString() =>
                Path.GetFileNameWithoutExtension(scene) + "/" + string.Join("/", objectID) +
                (Type.GetType(componentType) != null ? "+" + GetName() : "") +
                (Index.HasValue ? $"({Index.Value})" : "");

            string GetName() =>
#if UNITY_EDITOR
                ObjectNames.NicifyVariableName(Type.GetType(componentType).Name);
#else
                Type.GetType(componentType).Name;
#endif

            public override bool Equals(object obj) =>
                obj is ObjectReference re &&
                this.AsTuple() == re.AsTuple();

            public override int GetHashCode() =>
                AsTuple().GetHashCode();

            public (string scene, string objectID, string componentType, int componentTypeIndex, string field, int unityEventIndex, int arrayIndex) AsTuple() =>
                (scene, objectID, componentType, componentTypeIndex, field, unityEventIndex, arrayIndex);

            public bool Equals(ObjectReference x, ObjectReference y) =>
                x?.Equals(y) ?? false;

            public int GetHashCode(ObjectReference obj) =>
                obj?.GetHashCode() ?? -1;

        }

        /// <summary>A reference to a variable that references another object in some other scene.</summary>
        [Serializable]
        public class CrossSceneReference
        {

            public ObjectReference variable;
            public ObjectReference value;

            public CrossSceneReference()
            { }

            public CrossSceneReference(ObjectReference variable, ObjectReference value)
            {
                this.variable = variable;
                this.value = value;
            }

        }

        #endregion

#if UNITY_EDITOR

        #region Reference status

        enum SceneStatus
        {
            /// <summary>Cross-scene reference utility has not done anything to this scene.</summary>
            Default,
            /// <summary>Cross-scene reference utility has restored references in this scene.</summary>
            Restored,
            /// <summary>Cross-scene reference utility has cleared references in this scene.</summary>
            Cleared
        }

        class SceneReferenceData : Dictionary<ObjectReference, ReferenceData>
        {

            public SceneStatus status { get; set; }
            public bool hasErrors =>
                Values.Any(v => v.result != ObjectReference.FailReason.Succeeded);

        }

        struct ReferenceData
        {

            public ObjectReference.FailReason result;
            public GameObject source;
            public GameObject gameObject;
            public string component;
            public string member;

            public ReferenceData(ObjectReference.FailReason result) : this()
            {
                this.result = result;
            }

            public ReferenceData(ObjectReference.FailReason result, GameObject gameObject, string component, string member, GameObject source = null)
            {
                this.source = source ? source : gameObject;
                this.result = result;
                this.gameObject = gameObject;
                this.component = component;
                this.member = member;
            }

        }

        static readonly Dictionary<scene, SceneReferenceData> referenceStatuses = new Dictionary<scene, SceneReferenceData>();

        static bool CanSceneBeSaved(scene scene)
        {
            if (!referenceStatuses.ContainsKey(scene))
                referenceStatuses.Add(scene, new SceneReferenceData());
            return referenceStatuses[scene].status == SceneStatus.Restored && !referenceStatuses[scene].hasErrors;
        }

        static SceneStatus GetSceneStatus(scene scene) =>
            referenceStatuses.TryGetValue(scene, out var dict)
            ? dict.status
            : SceneStatus.Default;

        static void SetSceneStatus(scene scene, SceneStatus state)
        {
            if (!referenceStatuses.ContainsKey(scene))
                referenceStatuses.Add(scene, new SceneReferenceData());
            referenceStatuses[scene].status = state;
        }

        static void RemoveReferenceStatus(ObjectReference reference)
        {
            foreach (var scene in referenceStatuses)
                scene.Value.Remove(reference);
        }

        static KeyValuePair<ObjectReference, ReferenceData>[] GetInvalidReferences(scene scene)
        {

            if (!scene.IsValid() || !scene.isLoaded || !referenceStatuses.ContainsKey(scene))
                return Array.Empty<KeyValuePair<ObjectReference, ReferenceData>>();

            return referenceStatuses[scene].Where(kvp => kvp.Value.result != ObjectReference.FailReason.Succeeded).ToArray();

        }

        static KeyValuePair<ObjectReference, ReferenceData>[] GetInvalidReferences(GameObject obj)
        {

            if (obj == null || !referenceStatuses.ContainsKey(obj.scene))
                return Array.Empty<KeyValuePair<ObjectReference, ReferenceData>>();

            return referenceStatuses[obj.scene].Where(kvp => kvp.Value.source == obj && kvp.Value.result != ObjectReference.FailReason.Succeeded).ToArray();

        }

        static void ClearReferenceStatusesForScene(string scene) =>
            ClearReferenceStatusesForScene(referenceStatuses.Keys.FirstOrDefault(s => s.path == scene));

        static void ClearReferenceStatusesForScene(scene scene)
        {

            if (!referenceStatuses.ContainsKey(scene))
                return;

            foreach (var reference in referenceStatuses[scene].Keys.ToArray())
                referenceStatuses[scene][reference] = new ReferenceData(ObjectReference.FailReason.Succeeded);

        }

        #endregion

        static void RestoreScenes()
        {
            foreach (var scene in SceneUtility.GetAllOpenUnityScenes().ToArray())
                RestoreCrossSceneReferencesWithWarnings(scene, respectSettingsSuppressingWarnings: true);
        }

        internal static event Action OnSaved;

        #region Triggers / unity callbacks

        [RuntimeInitializeOnLoadMethod]
        [InitializeOnLoadMethod]
        [DidReloadScripts]
        static void OnLoad() =>
            CoroutineUtility.Run(() => Initialize(restoreScenes: true), when: () => !EditorApplication.isCompiling);

        static void Deinitialize(bool clearScenes = false)
        {

            if (clearScenes)
                foreach (var scene in SceneUtility.GetAllOpenUnityScenes())
                    ClearScene(scene);

            EditorSceneManager.preventCrossSceneReferences = true;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            BuildEventsUtility.preBuild -= BuildEventsUtility_preBuild;

            AssemblyReloadEvents.beforeAssemblyReload -= AssemblyReloadEvents_beforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= AssemblyReloadEvents_afterAssemblyReload;

            EditorSceneManager.sceneSaving -= EditorSceneManager_sceneSaving;
            EditorSceneManager.sceneSaved -= EditorSceneManager_sceneSaved;
            EditorSceneManager.sceneOpened -= EditorSceneManager_sceneOpening;
            EditorSceneManager.sceneClosed -= EditorSceneManager_sceneClosed;
            SceneOperation.RestoreCrossSceneReferencesCallback -= RestoreCrossSceneReferences;
            CrossSceneReferenceUtilityProxy.clearScene -= ClearScene;

            HierarchyGUIUtility.RemoveSceneGUI(OnSceneGUI);
            HierarchyGUIUtility.RemoveGameObjectGUI(OnGameObjectGUI);
            isInitialized = false;

        }

        static bool isInitialized;
        static void Initialize(bool restoreScenes = false)
        {

            if (isInitialized)
                return;
            isInitialized = true;

            Deinitialize(clearScenes: restoreScenes);

            EditorSceneManager.preventCrossSceneReferences = false;

            AssemblyReloadEvents.beforeAssemblyReload += AssemblyReloadEvents_beforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += AssemblyReloadEvents_afterAssemblyReload;

            EditorSceneManager.sceneSaving += EditorSceneManager_sceneSaving;
            EditorSceneManager.sceneSaved += EditorSceneManager_sceneSaved;
            EditorSceneManager.sceneOpened += EditorSceneManager_sceneOpening;
            EditorSceneManager.sceneClosed += EditorSceneManager_sceneClosed;
            BuildEventsUtility.preBuild += BuildEventsUtility_preBuild;
            SceneOperation.RestoreCrossSceneReferencesCallback += RestoreCrossSceneReferences;
            CrossSceneReferenceUtilityProxy.clearScene += ClearScene;

            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            HierarchyGUIUtility.AddSceneGUI(OnSceneGUI);
            HierarchyGUIUtility.AddGameObjectGUI(OnGameObjectGUI);

            if (restoreScenes)
                RestoreScenes();

        }

        private static void BuildEventsUtility_preBuild(UnityEditor.Build.Reporting.BuildReport _)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                foreach (var scene in SceneUtility.GetAllOpenUnityScenes().ToArray())
                    ClearScene(scene);
        }

        private static void AssemblyReloadEvents_beforeAssemblyReload()
        {
            //if (!EditorApplication.isPlayingOrWillChangePlaymode)
            //    foreach (var scene in UnitySceneUtility.GetAllOpenUnityScenes())
            //    {
            //        var references = FindCrossSceneReferences(scene);
            //        Save(scene, references);
            //    }
        }

        static void AssemblyReloadEvents_afterAssemblyReload() =>
            CoroutineUtility.Run(RestoreScenes, when: () => SceneUtility.hasAnyScenes);

        [PostProcessBuild]
        static void PostProcessBuild(BuildTarget _, string _1)
        {

            if (!Profile.current)
                return;

            CoroutineUtility.Run(RestoreScenes, when: () => !(EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer));

        }

        static void OnPlayModeChanged(PlayModeStateChange mode)
        {

            EditorSceneManager.preventCrossSceneReferences = false;

            foreach (var scene in SceneUtility.GetAllOpenUnityScenes().ToArray())
                ClearScene(scene);

            if (mode == PlayModeStateChange.EnteredPlayMode || mode == PlayModeStateChange.EnteredEditMode)
                RestoreScenes();

        }

        private static void EditorSceneManager_sceneClosed(scene _) =>
            RestoreScenes();

        private static void EditorSceneManager_sceneOpening(scene _, OpenSceneMode _1) =>
            RestoreScenes();

        static readonly List<string> scenesToIgnore = new List<string>();

        /// <summary>Ignores the specified scene.</summary>
        public static void Ignore(string scenePath, bool ignore)
        {
            if (ignore && !scenesToIgnore.Contains(scenePath))
                scenesToIgnore.Add(scenePath);
            else if (!ignore)
                scenesToIgnore.Remove(scenePath);
        }

        static bool isAdding;
        private static void EditorSceneManager_sceneSaving(scene scene, string path)
        {

            EditorSceneManager.preventCrossSceneReferences = false;
            if (isAdding || BuildPipeline.isBuildingPlayer || scenesToIgnore.Contains(path))
                return;

            if (!CanSceneBeSaved(scene))
            {
                if (GetSceneStatus(scene) == SceneStatus.Restored)
                    Debug.LogError($"Cannot save cross-scene references in scene '{path}' since it had errors when last saved, please resolve these to save new cross-scene references.");
                return;
            }

            isAdding = true;

            var l = new List<CrossSceneReference>();
            var newReferences = FindCrossSceneReferences(scene).ToArray();
            var referencesToCarryOver = Enumerate().FirstOrDefault(r => r.scene == path)?.references?.Where(r => r.variable.IsValid(returnTrueWhenSceneIsUnloaded: true)).ToArray() ?? Array.Empty<CrossSceneReference>();

            l.AddRange(referencesToCarryOver);
            l.AddRange(newReferences);

            var l1 = l.GroupBy(r => r.variable).
                Select(g => (oldRef: g.ElementAtOrDefault(0), newRef: g.ElementAtOrDefault(1))).
                Where(g =>
                {

                    //This is a bit confusing, but oldRef is newRef when no actual oldRef exist,
                    //we should probably improve this to be more readable
                    if (newReferences.Contains(g.oldRef))
                        g = (oldRef: null, newRef: g.oldRef);

                    //This is a new reference, or has been updated
                    if (g.newRef != null)
                        return true;

                    //This reference has not been updated to a new cross-scene target,
                    //but we still don't know if it has been set to null or to same scene,
                    //lets check if it is still valid (beyond unloaded target scene)
                    var shouldCarryOver = (g.oldRef?.value?.IsValid(returnTrueWhenSceneIsUnloaded: true) ?? false);
                    return shouldCarryOver;

                }).
                Select(g => g.newRef ?? g.oldRef).ToArray();

            Save(scene, l1.ToArray());

            foreach (var s in SceneUtility.GetAllOpenUnityScenes().ToArray())
                ClearScene(s);

            isAdding = false;

        }

        private static void EditorSceneManager_sceneSaved(scene scene) =>
            RestoreScenes();

        #endregion
        #region Find

        /// <summary>Finds all cross-scene references in the scenes.</summary>
        public static CrossSceneReference[] FindCrossSceneReferences(params scene[] scenes)
        {

            var l = new List<CrossSceneReference>();

            var components = FindComponents(scenes).
                Where(s => s.obj && s.scene.IsValid()).
                Select(c => (c.scene, c.obj, fields: GetFields(c.obj.GetType()).Where(IsSerialized).ToArray())).
                ToArray();

            foreach (var (scene, obj, fields) in components)
                foreach (var field in fields.ToArray())
                {

                    var o = field.GetValue(obj);

                    if (o != null)
                    {

                        if (typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                        {
                            for (int i = 0; i < ((UnityEventBase)o).GetPersistentEventCount(); i++)
                            {
                                if (GetCrossSceneReference(o, scene, out var reference, unityEventIndex: i))
                                {
                                    var source = GetSourceCrossSceneReference(scene, obj, field, unityEventIndex: i);
                                    l.Add(new CrossSceneReference(source, reference));
                                }
                            }
                        }
                        else if (typeof(IList).IsAssignableFrom(field.FieldType))
                        {
                            for (int i = 0; i < ((IList)o).Count; i++)
                            {
                                if (GetCrossSceneReference(o, scene, out var reference, arrayIndex: i))
                                {
                                    var source = GetSourceCrossSceneReference(scene, obj, field, arrayIndex: i);
                                    l.Add(new CrossSceneReference(source, reference));
                                }
                            }
                        }
                        else if (GetCrossSceneReference(o, scene, out var reference))
                            l.Add(new CrossSceneReference(GetSourceCrossSceneReference(scene, obj, field), reference));

                    }

                }

            return l.ToArray();

        }

        static bool IsSerialized(FieldInfo field) =>
             (field?.IsPublic ?? false) || field?.GetCustomAttribute<SerializeField>() != null;

        static IEnumerable<(scene scene, Component obj)> FindComponents(params scene[] scenes)
        {
            foreach (var scene in scenes)
                if (scene.isLoaded)
                    foreach (var rootObj in scene.GetRootGameObjects())
                        foreach (var obj in rootObj.GetComponentsInChildren<Component>(includeInactive: true))
                            yield return (scene, obj);
        }

        static bool GetCrossSceneReference(object obj, scene sourceScene, out ObjectReference reference, int unityEventIndex = -1, int arrayIndex = -1)
        {

            reference = null;

            if (obj is GameObject go && go && IsCrossScene(sourceScene.path, go.scene.path))
                reference = new ObjectReference(go.scene, GuidReferenceUtility.AddPersistent(go));

            else if (obj is Component c && c && c.gameObject && IsCrossScene(sourceScene.path, c.gameObject.scene.path))
                reference = new ObjectReference(c.gameObject.scene, GuidReferenceUtility.AddPersistent(c.gameObject)).With(c);

            else if (obj is UnityEvent ev)
                return GetCrossSceneReference(ev.GetPersistentTarget(unityEventIndex), sourceScene, out reference);

            else if (obj is IList list)
                return GetCrossSceneReference(list[arrayIndex], sourceScene, out reference);

            return reference != null;

        }

        static bool IsCrossScene(string srcScene, string scenePath)
        {
            var isPrefab = string.IsNullOrWhiteSpace(scenePath);
            var isDifferentScene = scenePath != srcScene;
            return isDifferentScene && !isPrefab;
        }

        static ObjectReference GetSourceCrossSceneReference(scene scene, Component obj, FieldInfo field, int? unityEventIndex = null, int? arrayIndex = null) =>
            new ObjectReference(scene, GuidReferenceUtility.AddPersistent(obj.gameObject), field).With(obj).With(unityEventIndex, arrayIndex);

        #endregion

#endif

        static IEnumerable<FieldInfo> GetFields(Type type)
        {

            foreach (var field in type.GetFields(BindingFlags.GetField | BindingFlags.SetField | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                yield return field;

            if (type.BaseType != null)
                foreach (var field in GetFields(type.BaseType))
                    yield return field;

        }

        static FieldInfo FindField(Type type, string name)
        {
            var e = GetFields(type).GetEnumerator();
            while (e.MoveNext())
                if (e.Current.Name == name)
                    return e.Current;
            return null;
        }

        /// <summary>Clears all added cross-scene references in scene, to prevent warning when saving.</summary>
        public static void ClearScene(scene scene)
        {

            if (Load(scene.path) is SceneCrossSceneReferenceCollection references)
                foreach (var variable in references.references.OfType<CrossSceneReference>().ToArray())
                    variable.variable.SetValue(null, out _, out _, forceHierarchyScan: true, setValueIfNull: true);

#if UNITY_EDITOR
            SetSceneStatus(scene, SceneStatus.Cleared);
#endif

        }

        /// <summary>Restores cross-scene references in the scene.</summary>
        public static void Restore(scene scene) =>
            RestoreWithInfo(scene).ToArray();

        /// <summary>Restores cross-scene references in the scene.</summary>
        public static IEnumerable<((ObjectReference reference, Object obj, ObjectReference.FailReason result) variable, (ObjectReference reference, Object obj, ObjectReference.FailReason result) value)> RestoreWithInfo(scene scene, bool forceHierarchyScan = false)
        {

            if (Load(scene.path) is SceneCrossSceneReferenceCollection references)
                foreach (var variable in references.references)
                {

                    ObjectReference.FailReason variableFailReason;
                    Object target;

                    if (variable.value.GetTarget(out var value, out var valueFailReason, forceHierarchyScan))
                        variable.variable.SetValue(value, out target, out variableFailReason, forceHierarchyScan);
                    else
                        variable.variable.GetTarget(out target, out variableFailReason, forceHierarchyScan);

                    yield return ((variable.variable, target, variableFailReason), (variable.value, value, valueFailReason));

                }

        }

        /// <summary>Restores cross-scene references and logs any failures to the console.</summary>
        public static void RestoreCrossSceneReferencesWithWarnings(scene scene, bool respectSettingsSuppressingWarnings = false)
        {
            var e = RestoreCrossSceneReferencesWithWarnings_IEnumerator(scene, respectSettingsSuppressingWarnings);
            while (e.MoveNext())
            { }
        }

        /// <summary>Restores cross-scene references and logs any failures to the console.</summary>
        public static IEnumerator RestoreCrossSceneReferencesWithWarnings_IEnumerator(scene scene, bool respectSettingsSuppressingWarnings = false, bool forceHierarchyScan = false)
        {

            if (!scene.isLoaded)
                yield break;

            var e = RestoreWithInfo(scene, forceHierarchyScan).GetEnumerator();
            var i = 0;
            while (e.MoveNext())
            {

                SetReferenceStatus(result: ObjectReference.FailReason.Succeeded);

                if (e.Current.variable.result != ObjectReference.FailReason.Succeeded)
                {
                    Log($"Could not resolve variable for cross-scene reference: {e.Current.variable.result}");
                    SetReferenceStatus(e.Current.variable.result);
                }
                if (e.Current.value.result != ObjectReference.FailReason.Succeeded)
                {
                    Log($"Could not resolve value for cross-scene reference{(e.Current.variable.obj ? ", " + e.Current.variable.obj.name : " ")}: {e.Current.value.result}", e.Current.variable.obj);
                    SetReferenceStatus(e.Current.value.result);
                }

                void Log(string message, Object target = null)
                {

                    if (!respectSettingsSuppressingWarnings || unableToResolveCrossSceneReferencesWarning)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning(message, target);
#else
                        Debug.LogError(message, target);
#endif
                    }
                }

                void SetReferenceStatus(ObjectReference.FailReason result)
                {
#if UNITY_EDITOR

                    if (!referenceStatuses.ContainsKey(scene))
                        referenceStatuses.Add(scene, new SceneReferenceData());

                    if (e.Current.variable.obj is Component c)
                        referenceStatuses[scene].Set(e.Current.variable.reference, new ReferenceData(result, c.gameObject, c.GetType().Name, e.Current.variable.reference.field));

#endif
                }

                i += 1;

                if (i > 20)
                {
                    i = 0;
                    yield return null;
                }

            }

#if UNITY_EDITOR
            SetSceneStatus(scene, SceneStatus.Restored);
#endif

        }

    }

}
