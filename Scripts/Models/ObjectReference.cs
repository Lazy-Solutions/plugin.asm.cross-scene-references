using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using System.Collections;

using scene = UnityEngine.SceneManagement.Scene;
using Object = UnityEngine.Object;
using Component = UnityEngine.Component;
using AdvancedSceneManager.Utility;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace plugin.asm.crossSceneReferences
{

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
            if (unityEventIndex.HasValue)
                this.unityEventIndex = unityEventIndex.Value;
            if (arrayIndex.HasValue)
                this.arrayIndex = arrayIndex.Value;
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

        public bool SetValue(object value, out Object target, out FailReason reasonForFailure, bool setValueIfNull = false)
        {

            target = null;

            if (!GetTarget(out var obj, out reasonForFailure))
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

        public bool GetTarget(out Object component, out FailReason reasonForFailure)
        {

            component = null;
            reasonForFailure = FailReason.Unknown;

            if (!GetScene(scene, out var _, ref reasonForFailure))
                return false;

            if (!GetObject(out var obj, ref reasonForFailure))
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

        bool GetObject(out GameObject obj, ref FailReason fail)
        {

            if (GuidReferenceUtility.TryFindPersistent(objectID, out obj))
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

            field = obj.GetType().FindField(this.field);
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

            var persistentCallsField = typeof(UnityEvent)._GetFields().FirstOrDefault(f => f.Name == "m_PersistentCalls");
            FieldInfo CallsField(object o) => o.GetType()._GetFields().FirstOrDefault(f => f.Name == "m_Calls");
            FieldInfo TargetField(object o) => o.GetType()._GetFields().FirstOrDefault(f => f.Name == "m_Target");

            if (persistentCallsField is null)
            {
                Debug.LogError("Cross-scene utility: Could not find field for setting UnityEvent listener.");
                return;
            }

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
        public FailReason Evaluate()
        {

            if (!GetTarget(out var obj, out FailReason reasonForFailure))
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

}
