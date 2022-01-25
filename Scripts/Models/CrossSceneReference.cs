using System;

namespace plugin.asm.crossSceneReferences
{

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

}
