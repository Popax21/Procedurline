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
    /// An interface which represents animations which also hold player animation metadata. When implemented by <see cref="CustomSpriteAnimation" />, Procedurline will utilize the data they provide as the metadata when if their animation is part of a <see cref="PlayerSprite" />.
    /// This interface is implemented by <see cref="PlayerSpriteAnimation" /> and <see cref="PlayerSpriteAnimationData" /> by default.
    /// </summary>
    /// <see cref="PlayerSpriteAnimation" />
    /// <see cref="PlayerSpriteAnimationData" />
    public interface IPlayerSpriteAnimation {
        /// <summary>
        /// Returns the player sprite animation metadata for a particular animation frame
        /// </summary>
        PlayerSpriteAnimationFrameData GetPlayerAnimationMetadata(int frameIdx);
    }

    /// <summary>
    /// A subclass of <see cref="Sprite.Animation" /> which also holds <see cref="PlayerSpriteAnimationFrameData" />.
    /// Procedurline replaces all animation references in <see cref="PlayerSprite" />s with instances of <see cref="PlayerSpriteAnimation" />, which allow sprite animation processor to change this metadata as well.
    /// </summary>
    /// <see cref="IPlayerSpriteAnimation" />
    /// <see cref="PlayerSpriteAnimationData" />
    public sealed class PlayerSpriteAnimation : Sprite.Animation, IPlayerSpriteAnimation {
        public PlayerSpriteAnimationFrameData[] PlayerFrameData;

        public PlayerSpriteAnimationFrameData GetPlayerAnimationMetadata(int frameIdx) => PlayerFrameData[frameIdx];
    }

    /// <summary>
    /// A subclass of <see cref="SpriteAnimationData" /> which also holds <see cref="PlayerSpriteAnimationFrameData" />.
    /// </summary>
    /// <see cref="IPlayerSpriteAnimation" />
    /// <see cref="PlayerSpriteAnimation" />
    public sealed class PlayerSpriteAnimationData : SpriteAnimationData, IPlayerSpriteAnimation {
        public PlayerSpriteAnimationFrameData[] PlayerFrameData;

        public PlayerSpriteAnimationFrameData GetPlayerAnimationMetadata(int frameIdx) => PlayerFrameData[frameIdx];
    }
}