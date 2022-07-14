using System;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Marks this method as a hook to apply to a content method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ContentHookAttribute : Attribute {
        public readonly string HookTargetName;
        public ContentHookAttribute(string targetName) => HookTargetName = targetName;
    }

    /// <summary>
    /// Marks this method as an IL hook to apply to a content method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ContentILHookAttribute : Attribute {
        public readonly string HookTargetName;
        public ContentILHookAttribute(string targetName) => HookTargetName = targetName;
    }

    /// <summary>
    /// Marks this content method for virtualization using <see cref="PatchUtils.Virtualize" />
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ContentVirtualizeAttribute : Attribute {
        public readonly bool CallBase;
        public ContentVirtualizeAttribute(bool callBase = true) => CallBase = callBase;
    }

    /// <summary>
    /// Marks this property as a proxy for a content field using <see cref="PatchUtils.MakeFieldProxy" />
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class ContentFieldProxyAttribute : Attribute {
        public readonly string FieldName;
        public ContentFieldProxyAttribute(string fieldName) => FieldName = fieldName;
    }
}