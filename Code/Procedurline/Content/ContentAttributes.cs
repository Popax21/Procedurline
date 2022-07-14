using System;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Marks this method as a hook to apply to a content method.
    /// </summary>
    internal sealed class ContentHookAttribute : Attribute {
        public readonly string HookTargetName;
        public ContentHookAttribute(string targetName) => HookTargetName = targetName;
    }

    /// <summary>
    /// Marks this method as an IL hook to apply to a content method.
    /// </summary>
    internal sealed class ContentILHookAttribute : Attribute {
        public readonly string HookTargetName;
        public ContentILHookAttribute(string targetName) => HookTargetName = targetName;
    }

    /// <summary>
    /// Marks this content method for virtualization using <see cref="PatchUtils.Virtualize" />
    /// </summary>
    internal sealed class ContentVirtualizeAttribute : Attribute {
        public readonly bool CallBase;
        public ContentVirtualizeAttribute(bool callBase = true) => CallBase = callBase;
    }
}