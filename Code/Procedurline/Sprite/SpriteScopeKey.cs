using System;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A <see cref="DataScopeKey" /> which also keeps track of sprite IDs.
    /// </summary>
    public class SpriteScopeKey : DataScopeKey {
        public readonly string SpriteID;
        public SpriteScopeKey(string spriteId) => SpriteID = spriteId;

        public override bool Equals(DataScopeKey other) => other is SpriteScopeKey spriteKey && base.Equals(other) && SpriteID.Equals(spriteKey.SpriteID, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => unchecked(base.GetHashCode() * 31 + SpriteID.ToLower().GetHashCode());
        public override string ToString() => $"SpriteScopeKey [{SpriteID}][{GetScopeListString(", ")}]";
    }
}