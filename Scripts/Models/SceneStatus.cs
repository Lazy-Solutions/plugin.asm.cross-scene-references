#if UNITY_EDITOR
#endif

namespace plugin.asm.crossSceneReferences
{
    public enum SceneStatus
    {
        /// <summary>Cross-scene reference utility has not done anything to this scene.</summary>
        Default,
        /// <summary>Cross-scene reference utility has restored references in this scene.</summary>
        Restored,
        /// <summary>Cross-scene reference utility has cleared references in this scene.</summary>
        Cleared
    }

}
