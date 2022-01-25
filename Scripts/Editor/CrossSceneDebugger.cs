#if UNITY_EDITOR

using System.IO;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using scene = UnityEngine.SceneManagement.Scene;

namespace plugin.asm.crossSceneReferences
{

    public class CrossSceneDebugger : EditorWindow
    {

        [SerializeField] private SerializableStringBoolDict expanded = new SerializableStringBoolDict();
        CrossSceneReferenceCollection[] references;

        [MenuItem("Tools/Advanced Scene Manager/Window/Cross-scene reference debugger", priority = 52)]
        public static void Open()
        {
            var window = GetWindow<CrossSceneDebugger>();
            window.titleContent = new GUIContent("Cross-scene references");
            window.minSize = new Vector2(730, 300);
        }

        [InitializeOnLoadMethod]
        static void OnLoad() =>
            InitializeSettings();

        void OnEnable()
        {

            Editor_OnSaved();
            CrossSceneReferenceUtility.OnSaved += Editor_OnSaved;

            //Load variables from editor prefs
            var json = EditorPrefs.GetString("AdvancedSceneManager.CrossSceneDebugger", JsonUtility.ToJson(this));
            JsonUtility.FromJsonOverwrite(json, this);

        }

        void OnFocus() =>
            Editor_OnSaved();

        void OnDisable()
        {

            CrossSceneReferenceUtility.OnSaved -= Editor_OnSaved;

            //Save variables to editor prefs
            var json = JsonUtility.ToJson(this);
            EditorPrefs.SetString("AdvancedSceneManager.CrossSceneDebugger", json);

        }

        void Editor_OnSaved()
        {
            references = CrossSceneReferenceUtility.Enumerate();
            Repaint();
        }

        #region Settings

        static void InitializeSettings()
        {
            SettingsTab.instance.Add(ShowUnresolvedReferencesIcon, SettingsTab.instance.DefaultHeaders.Appearance_Hierarchy);
            SettingsTab.instance.Add(LogUnresolvedReferences, SettingsTab.instance.DefaultHeaders.Log);
        }

        static VisualElement ShowUnresolvedReferencesIcon()
        {
            var toggle = new Toggle("Unable to resolve cross-scene reference:") { tooltip = "Log a warning, or error if build, when a cross-scene reference could not be resolved (PlayerPrefs)." };
            toggle.SetValueWithoutNotify(CrossSceneReferenceUtility.unableToResolveCrossSceneReferencesWarning);
            _ = toggle.RegisterValueChangedCallback(e => CrossSceneReferenceUtility.unableToResolveCrossSceneReferencesWarning = e.newValue);
            return toggle;
        }

        static VisualElement LogUnresolvedReferences()
        {
            var toggle = new Toggle("Log when a reference is unable to be resolved:");
            toggle.SetValueWithoutNotify(CrossSceneReferenceUtility.unableToResolveCrossSceneReferencesWarning);
            _ = toggle.RegisterValueChangedCallback(e => CrossSceneReferenceUtility.unableToResolveCrossSceneReferencesWarning = e.newValue);
            return toggle;
        }

        #endregion
        #region OnGUI

        Vector2 scrollPos;
        void OnGUI()
        {

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            _ = EditorGUILayout.BeginVertical(new GUIStyle() { margin = new RectOffset(64, 64, 42, 42) });
            if (references != null)
                foreach (var scene in references)
                {
                    if (DrawHeader(scene.scene, Path.GetFileNameWithoutExtension(scene.scene)))
                        foreach (var item in scene.references)
                            Draw(item);
                }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();

        }

        bool DrawHeader(string key, string header) =>
            expanded.Set(key, EditorGUILayout.Foldout(expanded.GetValue(key), header, toggleOnLabelClick: true));

        void Draw(CrossSceneReference reference)
        {

            EditorGUILayout.Space();

            GUILayout.BeginVertical(new GUIStyle(GUI.skin.window), GUILayout.Height(82), GUILayout.MaxWidth(10));

            DrawSubHeader(reference.variable);

            var r = GUILayoutUtility.GetLastRect();
            r = new Rect(r.xMax - 22, r.y - 18, 22, 22);
            if (GUI.Button(r, new GUIContent("x", "Remove")))
            {
                CrossSceneReferenceUtility.Remove(reference);
                Editor_OnSaved();
            }

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            Draw(reference.variable, "Variable:");

            _ = EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("", GUILayout.Width(150 + 150 + 64 + 3));
            EditorGUILayout.LabelField("↓");
            EditorGUILayout.EndHorizontal();

            Draw(reference.value, "Target:");

            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            GUILayout.EndVertical();
            EditorGUILayout.Space();

        }

        void DrawSubHeader(ObjectReference reference)
        {

            int? index = null;
            if (reference.arrayIndex != -1)
                index = reference.arrayIndex;
            else if (reference.unityEventIndex != -1)
                index = reference.unityEventIndex;

            var indexString = index.HasValue ? " (index: " + index + ")" : "";

            _ = EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(12, false);
            GUILayout.Label(reference.field + indexString, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            EditorGUILayout.EndHorizontal();


        }

        void Draw(ObjectReference reference, string label)
        {

            _ = EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(12, false);

            EditorGUILayout.LabelField(label, GUILayout.Width(64));

            GUI.enabled = false;
            _ = EditorGUILayout.ObjectField(AssetDatabase.LoadAssetAtPath<SceneAsset>(reference.scene), typeof(SceneAsset), allowSceneObjects: false, GUILayout.Width(150));
            GUI.enabled = true;

            _ = Draw(UnityEngine.SceneManagement.SceneManager.GetSceneByPath(reference.scene), reference);

            EditorGUILayout.EndHorizontal();

        }

        bool Draw(scene scene, ObjectReference reference)
        {
            if (scene.isLoaded)
            {
                var succeeded = reference.GetTarget(out var c, out var fail, forceHierarchyScan: true);
                if (succeeded)
                {
                    GUI.enabled = false;
                    _ = EditorGUILayout.ObjectField(c, typeof(SceneAsset), allowSceneObjects: false, GUILayout.Width(150));
                    GUI.enabled = true;
                    return true;
                }
                else
                {
                    EditorGUILayout.LabelField("--" + fail.ToString() + "--");
                    return false;
                }
            }
            else
            {
                GUILayout.Label("--Scene not loaded--", GUILayout.ExpandWidth(false));
                if (GUILayout.Button(new GUIContent("+", "Open scene additively to get more info"), GUILayout.ExpandWidth(false)))
                    _ = EditorSceneManager.OpenScene(reference.scene, OpenSceneMode.Additive);
                return false;
            }
        }

        #endregion

    }

}
#endif
