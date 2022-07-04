using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A simple implementation of a sprite animation processor which applies a color matrix to all pixels.
    /// </summary>
    public sealed class SpriteColorMatrixProcessor : IDataProcessor<Sprite, string, SpriteAnimationData>, IDataProcessor<Sprite, int, SpriteAnimationData.AnimationFrame> {
        public readonly Matrix ColorMatrix;
        public readonly float SaturationThreshold, ValueThreshold;

        public SpriteColorMatrixProcessor(Matrix colMat, float satThresh, float valThresh) {
            ColorMatrix = colMat;
            SaturationThreshold = satThresh;
            ValueThreshold = valThresh;
        }

        public void RegisterScopes(Sprite target, DataScopeKey key) {}

        public bool ProcessData(Sprite target, DataScopeKey key, string id, ref SpriteAnimationData data) {
            return data?.ApplyFrameProcessor(this, target, key) ?? false;
        }

        public bool ProcessData(Sprite target, DataScopeKey key, int id, ref SpriteAnimationData.AnimationFrame data) {
            if(data.TextureData == null) return false;
            data.TextureData.ApplyColorMatrix(ColorMatrix, SaturationThreshold, ValueThreshold);
            return true;
        }
    }
}