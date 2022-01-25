using System.Collections;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace plugin.asm.crossSceneReferences
{

    public static class SceneOperation
    {

        static readonly Callback sceneOperationCallback = Callback.Before(Phase.OpenCallbacks).Do(RestoreCrossSceneReferences);

        [RuntimeInitializeOnLoadMethod]
        public static void OnLoad()
        {
            Debug.LogError("start");
            AdvancedSceneManager.Core.SceneOperation.AddCallback(sceneOperationCallback);
        }

        static IEnumerator RestoreCrossSceneReferences()
        {
            Debug.LogError("restore");
            foreach (var scene in SceneUtility.GetAllOpenUnityScenes())
                yield return CrossSceneReferenceUtility.RestoreCrossSceneReferencesWithWarnings_IEnumerator(scene, respectSettingsSuppressingWarnings: true);
        }

    }

}
