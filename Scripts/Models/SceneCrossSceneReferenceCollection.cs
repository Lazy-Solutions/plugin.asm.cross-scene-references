using System;

namespace plugin.asm.crossSceneReferences
{

    /// <summary>A collection of <see cref="CrossSceneReference"/> for a scene.</summary>
    [Serializable]
    public class CrossSceneReferenceCollection
    {
        public string scene;
        public CrossSceneReference[] references;
    }

}
