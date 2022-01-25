using UnityEngine;

#if UNITY_EDITOR
#endif

namespace plugin.asm.crossSceneReferences
{
    public struct ReferenceData
    {

        public ObjectReference.FailReason result;
        public GameObject source;
        public GameObject gameObject;
        public string component;
        public string member;

        public ReferenceData(ObjectReference.FailReason result) : this() =>
            this.result = result;

        public ReferenceData(ObjectReference.FailReason result, GameObject gameObject, string component, string member, GameObject source = null)
        {
            this.source = source ? source : gameObject;
            this.result = result;
            this.gameObject = gameObject;
            this.component = component;
            this.member = member;
        }

    }

}
