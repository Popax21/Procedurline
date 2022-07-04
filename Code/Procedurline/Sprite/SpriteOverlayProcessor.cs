using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A simple implementation of a sprite animation processor which overlays a given texture or animation over all frames
    /// <b>WARNING:</b> When overlaying another animation, all of its render parameters are ignored, and instead its raw pixel data is copied!
    /// </summary>
    public sealed class SpriteOverlayProcessor : IAsyncDataProcessor<Sprite, string, SpriteAnimationData>, IAsyncDataProcessor<Sprite, int, SpriteAnimationData.AnimationFrame> {
        public readonly Point Offset;
        public readonly Task<TextureData> TextureData;
        public readonly Task<SpriteAnimationData> AnimationData;
        public readonly int FrameOffset;

        public SpriteOverlayProcessor(Point offset, TextureHandle tex) : this(offset, tex.GetTextureData()) {}
        public SpriteOverlayProcessor(Point offset, TextureData texData) : this(offset, Task.FromResult(texData)) {}
        public SpriteOverlayProcessor(Point offset, Task<TextureData> texData) {
            Offset = offset;
            TextureData = texData;
        }

        public SpriteOverlayProcessor(Point offset, Sprite.Animation anim, int frameOff = 0) : this(offset, ProcedurlineModule.SpriteManager.GetAnimationData(anim), frameOff) {}
        public SpriteOverlayProcessor(Point offset, SpriteAnimationData animData, int frameOff = 0) : this(offset, Task.FromResult(animData), frameOff) {}
        public SpriteOverlayProcessor(Point offset, Task<SpriteAnimationData> animData, int frameOff = 0) {
            Offset = offset;
            AnimationData = animData;
            FrameOffset = frameOff;
        }

        public void RegisterScopes(Sprite target, DataScopeKey key) {}

        public Task<bool> ProcessDataAsync(Sprite target, DataScopeKey key, string id, AsyncRef<SpriteAnimationData> data, CancellationToken token = default) {
            if(data.Data == null) return Task.FromResult(false);
            return data.Data.ApplyFrameProcessor(this, target, key);
        }

        public async Task<bool> ProcessDataAsync(Sprite target, DataScopeKey key, int id, AsyncRef<SpriteAnimationData.AnimationFrame> data, CancellationToken token = default) {
            if(data.Data.TextureData == null) return false;

            //Get the texture data to overlay
            TextureData overlayData = null;
            if(TextureData != null) {
                overlayData = await TextureData;
            } else if(AnimationData != null) {
                SpriteAnimationData animData = await AnimationData;
                int relFrame = id + FrameOffset;
                if(relFrame < 0 || animData.Frames.Length <= relFrame) return false;
                overlayData = animData.Frames[relFrame].TextureData;
            } else return false;

            //Overlay data onto the frame
            int copyW = Math.Min(overlayData.Width, data.Data.Width - Offset.X), copyH = Math.Min(overlayData.Height, data.Data.Height - Offset.Y);
            overlayData.Copy(data.Data.TextureData, new Rectangle(0, 0, copyW, copyH), new Rectangle(Offset.X, Offset.Y, copyW, copyH));

            return true;
        }
    }
}