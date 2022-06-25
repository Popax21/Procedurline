using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Holds the player's hair colors.
    /// </summary>
    public struct PlayerHairColorData {
        public Color UsedColor, NormalColor, TwoDashesColor;
        public Color BadelineUsedColor, BadelineNormalColor, BadelineTwoDashesColor;

        /// <summary>
        /// Applies the given color matrix to all hair colors.
        /// </summary>
        public void ApplyMatrix(Matrix colMat) {
            UsedColor = UsedColor.ApplyMatrix(colMat);
            NormalColor = NormalColor.ApplyMatrix(colMat);
            TwoDashesColor = TwoDashesColor.ApplyMatrix(colMat);
            BadelineUsedColor = BadelineUsedColor.ApplyMatrix(colMat);
            BadelineNormalColor = BadelineNormalColor.ApplyMatrix(colMat);
            BadelineTwoDashesColor = BadelineTwoDashesColor.ApplyMatrix(colMat);
        }

        /// <summary>
        /// Returns a <see cref="PlayerHairColorData" /> instance with all alpha components removed.
        /// </summary>
        public PlayerHairColorData RemoveAlpha() => new PlayerHairColorData() {
            UsedColor = UsedColor.RemoveAlpha(),
            NormalColor = NormalColor.RemoveAlpha(),
            TwoDashesColor = TwoDashesColor.RemoveAlpha(),
            BadelineUsedColor = BadelineUsedColor.RemoveAlpha(),
            BadelineNormalColor = BadelineNormalColor.RemoveAlpha(),
            BadelineTwoDashesColor = BadelineTwoDashesColor.RemoveAlpha()
        };
    }

    /// <summary>
    /// Holds the player's hair settings.
    /// </summary>
    public struct PlayerHairSettingsData {
        public int NodeCount;
        public Vector2 StepPerSegment;
        public float StepInFacingPerSegment;
        public float StepApproach;
        public float StepYSinePerSegment;
    }

    /// <summary>
    /// Holds a player hair node's data.
    /// </summary>
    public struct PlayerHairNodeData {
        public Color Color;
        public Vector2 Scale;
        public MTexture Texture;
    }
}