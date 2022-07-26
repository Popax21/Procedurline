using System;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Marks this method as a content initializer to call when loading the mod.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ContentInitAttribute : Attribute {}

    /// <summary>
    /// Marks this method as a content uninitializer to call when unloading the mod.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ContentUninitAttribute : Attribute {}

    /// <summary>
    /// Marks this method as a hook to apply to a content method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ContentHookAttribute : Attribute {
        public readonly string TargetTypeName, TargetMethodName;

        public ContentHookAttribute(string methodName) : this(null, methodName) {}
        public ContentHookAttribute(string typeName, string methodName) {
            TargetTypeName = typeName;
            TargetMethodName = methodName;
        }
    }

    /// <summary>
    /// Marks this method as an IL hook to apply to a content method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ContentILHookAttribute : Attribute {
        public readonly string TargetTypeName, TargetMethodName;
        public readonly bool HookStateMachine;

        public ContentILHookAttribute(string methodName, bool hookStateMachine = false) : this(null, methodName, hookStateMachine) {}
        public ContentILHookAttribute(string typeName, string methodName, bool hookStateMachine = false) {
            TargetTypeName = typeName;
            TargetMethodName = methodName;
            HookStateMachine = hookStateMachine;
        }
    }

    /// <summary>
    /// Marks this content method for virtualization using <see cref="PatchUtils.Virtualize" />
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ContentVirtualizeAttribute : Attribute {
        public readonly bool CallBase;
        public ContentVirtualizeAttribute(bool callBase = true) => CallBase = callBase;
    }

    /// <summary>
    /// Marks this property as a proxy for a content field using <see cref="PatchUtils.PatchFieldProxy" />
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    internal sealed class ContentFieldProxyAttribute : Attribute {
        public readonly string TargetFieldName;
        public ContentFieldProxyAttribute(string fieldName) => TargetFieldName = fieldName;
    }

    /// <summary>
    /// Marks this property as a custom SFX to replace the vanilla one in a given content method using <see cref="PatchUtils.PatchSFX" />. Can be applied multiple times.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    internal sealed class ContentPatchSFXAttribute : Attribute {
        public readonly string TargetMethodName;
        public ContentPatchSFXAttribute(string methodName) => TargetMethodName = methodName;
    }
}