using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
#endif

namespace plugin.asm.crossSceneReferences
{
    public class SceneReferenceData : Dictionary<ObjectReference, ReferenceData>
    {

        public SceneStatus status { get; set; }
        public bool hasErrors =>
            Values.Any(v => v.result != ObjectReference.FailReason.Succeeded);

    }

}
