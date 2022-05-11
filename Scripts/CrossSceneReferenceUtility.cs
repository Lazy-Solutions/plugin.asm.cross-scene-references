using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using Component = UnityEngine.Component;
using AdvancedSceneManager.Utility;

using scene = UnityEngine.SceneManagement.Scene;
using UnityEngine.Events;
using System.Reflection;
using UnityEngine.Scripting;

#if UNITY_EDITOR
using UnityEditor;
#endif

[assembly: Preserve]
[assembly: AlwaysLinkAssembly]
namespace plugin.asm.crossSceneReferences
{

    /// <summary>An utility for saving and restoring cross-scene references.</summary>
    public static partial class CrossSceneReferenceUtility
    {

        /// <summary>Enables message when a cross-scene reference could not be resolved.</summary>
        public static bool unableToResolveCrossSceneReferencesWarning
        {
            get => PlayerPrefs.GetInt("AdvancedSceneManager.Warnings.unableToResolveCrossSceneReferences") == 1;
            set => PlayerPrefs.SetInt("AdvancedSceneManager.Warnings.unableToResolveCrossSceneReferences", value ? 1 : 0);
        }

#pragma warning disable CS0067
        internal static event Action OnSaved;
#pragma warning restore CS0067

        #region Reference status

        static readonly Dictionary<scene, SceneReferenceData> referenceStatuses = new Dictionary<scene, SceneReferenceData>();

        /// <summary>Gets if the cross-scene references can be saved.</summary>
        /// <remarks>This would be if status: <see cref="SceneStatus.Restored"/> and no resolve errors.</remarks>
        public static bool CanSceneBeSaved(scene scene)
        {
            if (!referenceStatuses.ContainsKey(scene))
                referenceStatuses.Add(scene, new SceneReferenceData());
            return referenceStatuses[scene].status == SceneStatus.Restored && !referenceStatuses[scene].hasErrors;
        }

        /// <summary>Gets the status of this scene, when relating to cross-scene reference restore.</summary>
        public static SceneStatus GetSceneStatus(scene scene) =>
               referenceStatuses.TryGetValue(scene, out var dict)
               ? dict.status
               : SceneStatus.Default;

        /// <summary>Gets any invalid references on this scene.</summary>
        public static KeyValuePair<ObjectReference, ReferenceData>[] GetInvalidReferences(scene scene)
        {

            if (!scene.IsValid() || !scene.isLoaded || !referenceStatuses.ContainsKey(scene))
                return Array.Empty<KeyValuePair<ObjectReference, ReferenceData>>();

            return referenceStatuses[scene].Where(kvp => kvp.Value.result != ObjectReference.FailReason.Succeeded).ToArray();

        }

        /// <summary>Gets any invalid references on this game object.</summary>
        public static KeyValuePair<ObjectReference, ReferenceData>[] GetInvalidReferences(GameObject obj)
        {

            if (obj == null || !referenceStatuses.ContainsKey(obj.scene))
                return Array.Empty<KeyValuePair<ObjectReference, ReferenceData>>();

            return referenceStatuses[obj.scene].Where(kvp => kvp.Value.source == obj && kvp.Value.result != ObjectReference.FailReason.Succeeded).ToArray();

        }

        static void SetSceneStatus(scene scene, SceneStatus state)
        {
            if (!referenceStatuses.ContainsKey(scene))
                referenceStatuses.Add(scene, new SceneReferenceData());
            referenceStatuses[scene].status = state;
        }

        static void RemoveReferenceStatus(ObjectReference reference)
        {
            foreach (var scene in referenceStatuses)
                _ = scene.Value.Remove(reference);
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
        #region Assets

        const string Key = "CrossSceneReferences";

        /// <summary>Loads cross-scene references for a scene.</summary>
        public static CrossSceneReferenceCollection Load(string scenePath) =>
            SceneDataUtility.Get<CrossSceneReferenceCollection>(scenePath, Key);

        /// <summary>Loads cross-scene references for all scenes.</summary>
        public static CrossSceneReferenceCollection[] Enumerate() =>
            SceneDataUtility.Enumerate<CrossSceneReferenceCollection>(Key).Where(c => c.references?.Any() ?? false).ToArray();

#if UNITY_EDITOR

        /// <summary>Save the cross-scene references for a scene. This removes all previously added references for this scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Save(scene scene, params CrossSceneReference[] references) =>
            Save(new CrossSceneReferenceCollection() { references = references, scene = scene.path }, scene.path);

        /// <summary>Saves a <see cref="CrossSceneReference"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Save(CrossSceneReferenceCollection reference, string scenePath)
        {
            SceneDataUtility.Set(scenePath, Key, reference);
            ClearReferenceStatusesForScene(scenePath);
            OnSaved?.Invoke();
        }

        /// <summary>Removes all cross-scene references for this scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Remove(scene scene) =>
            Remove(scene.path);

        /// <summary>Removes all cross-scene references for this scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Remove(string scene) =>
            SceneDataUtility.Unset(scene, Key);

        /// <summary>Removes all cross-scene references for this scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void Remove(CrossSceneReference reference)
        {

            if (reference == null)
                return;

            var collection = SceneDataUtility.Get<CrossSceneReferenceCollection>(reference.variable.scene, Key);
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

        #endregion
        #region Restore

        /// <summary>Restores cross-scene references in all scenes.</summary>
        public static void RestoreScenes()
        {
            foreach (var scene in SceneUtility.GetAllOpenUnityScenes().ToArray())
                RestoreCrossSceneReferencesWithWarnings(scene, respectSettingsSuppressingWarnings: true);
        }

        /// <summary>Restores cross-scene references in the scene.</summary>
        public static void Restore(scene scene) =>
            RestoreWithInfo(scene).ToArray();

        /// <summary>Restores cross-scene references in the scene.</summary>
        public static IEnumerable<((ObjectReference reference, Object obj, ObjectReference.FailReason result) variable, (ObjectReference reference, Object obj, ObjectReference.FailReason result) value)> RestoreWithInfo(scene scene)
        {

            if (Load(scene.path) is CrossSceneReferenceCollection references)
                foreach (var variable in references.references)
                {

                    ObjectReference.FailReason variableFailReason;
                    Object target;

                    if (variable.value.GetTarget(out var value, out var valueFailReason))
                        _ = variable.variable.SetValue(value, out target, out variableFailReason);
                    else
                        _ = variable.variable.GetTarget(out target, out variableFailReason);

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
        public static IEnumerator RestoreCrossSceneReferencesWithWarnings_IEnumerator(scene scene, bool respectSettingsSuppressingWarnings = false)
        {

            if (!scene.isLoaded)
                yield break;

            var e = RestoreWithInfo(scene).GetEnumerator();
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

        #endregion
        #region Find

        /// <summary>Finds all cross-scene references in the scenes.</summary>
        public static IEnumerable<CrossSceneReference> FindCrossSceneReferences(params scene[] scenes)
        {

            var components = FindComponents(scenes).
                Where(s => s.obj && s.scene.IsValid()).
                Select(c => (c.scene, c.obj, fields: c.obj.GetType()._GetFields().Where(IsSerialized).ToArray())).
                ToArray();

            foreach (var (scene, obj, fields) in components)
            {

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
                                    yield return new CrossSceneReference(source, reference);
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
                                    yield return new CrossSceneReference(source, reference);
                                }
                            }
                        }
                        else if (GetCrossSceneReference(o, scene, out var reference))
                            yield return new CrossSceneReference(GetSourceCrossSceneReference(scene, obj, field), reference);

                    }

                }
            }
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
                reference = new ObjectReference(go.scene, GuidReferenceUtility.GetOrAddPersistent(go));

            else if (obj is Component c && c && c.gameObject && IsCrossScene(sourceScene.path, c.gameObject.scene.path))
                reference = new ObjectReference(c.gameObject.scene, GuidReferenceUtility.GetOrAddPersistent(c.gameObject)).With(c);

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
            new ObjectReference(scene, GuidReferenceUtility.GetOrAddPersistent(obj.gameObject), field).With(obj).With(unityEventIndex, arrayIndex);

        #endregion

#if UNITY_EDITOR

        /// <summary>Clears all added cross-scene references in scene, to prevent warning when saving.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static void ClearScene(scene scene)
        {

            if (Load(scene.path) is CrossSceneReferenceCollection references)
                foreach (var variable in references.references.OfType<CrossSceneReference>().ToArray())
                    variable.variable.SetValue(null, out _, out _, setValueIfNull: true);

            SetSceneStatus(scene, SceneStatus.Cleared);

        }

#endif

    }

}
