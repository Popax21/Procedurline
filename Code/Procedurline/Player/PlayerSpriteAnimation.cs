using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Holds additional per-frame player metadata.
    /// </summary>
    public struct PlayerSpriteAnimationFrameData {
        public bool IsRunning, IsDreamDashing;
        public int CarryYOffset;

        public bool HasHair;
        public Vector2 HairOffset;
        public int HairFrame;

        public PlayerSpriteAnimationFrameData(string animId, PlayerAnimMetadata metadata) : this() {
            IsRunning = animId == "flip" || animId.StartsWith("run");
            IsDreamDashing = animId.StartsWith("dreamDash");;
            CarryYOffset = metadata.CarryYOffset;

            HasHair = metadata.HasHair;
            HairOffset = metadata.HairOffset;
            HairFrame = metadata.Frame;
        }
    }

    /// <summary>
    /// A subclass of <see cref="Sprite.Animation" /> which also holds <see cref="PlayerSpriteAnimationFrameData" />.
    /// Procedurline replaces all animation references in <see cref="PlayerSprite" />s with instances of <see cref="PlayerSpriteAnimation" />, which allow sprite animation processor to change this metadata as well.
    /// </summary>
    public sealed class PlayerSpriteAnimation : Sprite.Animation {
        public PlayerSpriteAnimationFrameData[] PlayerFrameData;
    }

    /// <summary>
    /// A subclass of <see cref="SpriteAnimationData" /> which also holds <see cref="PlayerSpriteAnimationFrameData" />.
    /// </summary>
    public sealed class PlayerSpriteAnimationData : SpriteAnimationData {
        public PlayerSpriteAnimationFrameData[] PlayerFrameData;
    }
}