#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using Lazy.Utility;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

using scene = UnityEngine.SceneManagement.Scene;

namespace plugin.asm.crossSceneReferences
{

    public static class Editor
    {

        [InitializeOnLoadMethod]
        static void OnLoad() =>
            SettingsTab.instance.Add(header: SettingsTab.instance.DefaultHeaders.Appearance_Hierarchy, callback: ShowHierarchyIconToggle);

        [RuntimeInitializeOnLoadMethod]
        [InitializeOnLoadMethod]
        [DidReloadScripts]
        static void OnLoad2() =>
            CoroutineUtility.Run(() => Initialize(restoreScenes: true), when: () => !EditorApplication.isCompiling);

        #region Hierarchy indicator

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

            var references = CrossSceneReferenceUtility.GetInvalidReferences(scene).ToArray();
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

            var references = CrossSceneReferenceUtility.GetInvalidReferences(obj).ToArray();
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

        #endregion
        #region Triggers / unity callbacks

        static void Deinitialize(bool clearScenes = false)
        {

            if (clearScenes)
                foreach (var scene in SceneUtility.GetAllOpenUnityScenes())
                    CrossSceneReferenceUtility.ClearScene(scene);

            EditorSceneManager.preventCrossSceneReferences = true;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            BuildEventsUtility.preBuild -= BuildEventsUtility_preBuild;

            AssemblyReloadEvents.beforeAssemblyReload -= AssemblyReloadEvents_beforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= AssemblyReloadEvents_afterAssemblyReload;

            EditorSceneManager.sceneSaving -= EditorSceneManager_sceneSaving;
            EditorSceneManager.sceneSaved -= EditorSceneManager_sceneSaved;
            EditorSceneManager.sceneOpened -= EditorSceneManager_sceneOpening;
            EditorSceneManager.sceneClosed -= EditorSceneManager_sceneClosed;

            CrossSceneReferenceUtilityProxy.clearScene -= CrossSceneReferenceUtility.ClearScene;

            HierarchyGUIUtility.RemoveSceneGUI(OnSceneGUI);
            HierarchyGUIUtility.RemoveGameObjectGUI(OnGameObjectGUI);
            isInitialized = false;

        }

        static bool isInitialized;
        static void Initialize(bool restoreScenes = false)
        {

            if (Application.isPlaying)
                return;

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

            CrossSceneReferenceUtilityProxy.clearScene += CrossSceneReferenceUtility.ClearScene;

            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            HierarchyGUIUtility.AddSceneGUI(OnSceneGUI);
            HierarchyGUIUtility.AddGameObjectGUI(OnGameObjectGUI);

            if (restoreScenes)
                CrossSceneReferenceUtility.RestoreScenes();

        }

        private static void BuildEventsUtility_preBuild(UnityEditor.Build.Reporting.BuildReport _)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                foreach (var scene in SceneUtility.GetAllOpenUnityScenes().ToArray())
                    CrossSceneReferenceUtility.ClearScene(scene);
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
            CoroutineUtility.Run(CrossSceneReferenceUtility.RestoreScenes, when: () => SceneUtility.hasAnyScenes);

        [PostProcessBuild]
        static void PostProcessBuild(BuildTarget _, string _1)
        {

            if (!Profile.current)
                return;

            CoroutineUtility.Run(CrossSceneReferenceUtility.RestoreScenes, when: () => !(EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer));

        }

        static void OnPlayModeChanged(PlayModeStateChange mode)
        {

            EditorSceneManager.preventCrossSceneReferences = false;

            foreach (var scene in SceneUtility.GetAllOpenUnityScenes().ToArray())
                CrossSceneReferenceUtility.ClearScene(scene);

            if (mode == PlayModeStateChange.EnteredPlayMode || mode == PlayModeStateChange.EnteredEditMode)
                CrossSceneReferenceUtility.RestoreScenes();

        }

        static void EditorSceneManager_sceneClosed(scene _) =>
            CrossSceneReferenceUtility.RestoreScenes();

        static void EditorSceneManager_sceneOpening(scene _, OpenSceneMode _1) =>
            CrossSceneReferenceUtility.RestoreScenes();

        static readonly List<string> scenesToIgnore = new List<string>();

        /// <summary>Ignores the specified scene.</summary>
        public static void Ignore(string scenePath, bool ignore)
        {
            if (ignore && !scenesToIgnore.Contains(scenePath))
                scenesToIgnore.Add(scenePath);
            else if (!ignore)
                _ = scenesToIgnore.Remove(scenePath);
        }

        static bool isAdding;
        static void EditorSceneManager_sceneSaving(scene scene, string path)
        {

            EditorSceneManager.preventCrossSceneReferences = false;
            if (isAdding || BuildPipeline.isBuildingPlayer || scenesToIgnore.Contains(path))
                return;

            if (!CrossSceneReferenceUtility.CanSceneBeSaved(scene))
            {
                if (CrossSceneReferenceUtility.GetSceneStatus(scene) == SceneStatus.Restored)
                    Debug.LogError($"Cannot save cross-scene references in scene '{path}' since it had errors when last saved, please resolve these to save new cross-scene references.");
                return;
            }

            isAdding = true;

            var l = new List<CrossSceneReference>();
            var newReferences = CrossSceneReferenceUtility.FindCrossSceneReferences(scene).ToArray();
            var referencesToCarryOver = CrossSceneReferenceUtility.Enumerate().FirstOrDefault(r => r.scene == path)?.references?.Where(r => r.variable.IsValid(returnTrueWhenSceneIsUnloaded: true)).ToArray() ?? Array.Empty<CrossSceneReference>();

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

            CrossSceneReferenceUtility.Save(scene, l1.ToArray());

            foreach (var s in SceneUtility.GetAllOpenUnityScenes().ToArray())
                CrossSceneReferenceUtility.ClearScene(s);

            isAdding = false;

        }

        static void EditorSceneManager_sceneSaved(scene scene) =>
           CrossSceneReferenceUtility.RestoreScenes();

        #endregion

    }

}

#endif
