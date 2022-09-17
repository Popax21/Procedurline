using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Contains and wraps sprite animation data in a more easily accesible way
    /// </summary>
    public class SpriteAnimationData : IDisposable {
        /// <summary>
        /// Contains the data required to describe a single frame of a sprite animation
        /// </summary>
        public struct AnimationFrame {
            public TextureData TextureData;
            public string AtlasPath;
            public Vector2 DrawOffset;
            public int Width, Height;
            public Vector2 Scale;
            public float ScaleFix;
        }

        public Chooser<string> Goto;
        public float Delay;
        public AnimationFrame[] Frames;

        public void Dispose() {
            for(int i = 0; i < Frames.Length; i++) Frames[i].TextureData?.Dispose();
        }

        /// <summary>
        /// Clones the sprite animation data
        /// </summary>
        public SpriteAnimationData Clone() {
            SpriteAnimationData clone = new SpriteAnimationData() {
                Goto = Goto,
                Delay = Delay,
                Frames = new AnimationFrame[Frames.Length]
            };

            for(int i = 0; i < Frames.Length; i++) {
                clone.Frames[i] = new AnimationFrame() {
                    TextureData = Frames[i].TextureData.Clone(),
                    AtlasPath = Frames[i].AtlasPath,
                    DrawOffset = Frames[i].DrawOffset,
                    Width = Frames[i].Width,
                    Height = Frames[i].Height,
                    Scale = Frames[i].Scale
                };
            }

            return clone;
        }

        /// <summary>
        /// Applies a data processor to all frames of the animation
        /// </summary>
        public bool ApplyFrameProcessor(IDataProcessor<Sprite, int, AnimationFrame> processor, Sprite target, DataScopeKey scopeKey) {
            bool didModify = false;
            for(int i = 0; i < Frames.Length; i++) didModify |= processor.ProcessData(target, scopeKey, i, ref Frames[i]);
            return didModify;
        }

        /// <summary>
        /// Applies a data processor to all frames of the animation
        /// </summary>
        public async Task<bool> ApplyFrameProcessor(IAsyncDataProcessor<Sprite, int, AnimationFrame> processor, Sprite target, DataScopeKey scopeKey, CancellationToken token = default) {
            //Start tasks
            AsyncRef<AnimationFrame>[] frames = new AsyncRef<AnimationFrame>[Frames.Length];
            Task<bool>[] tasks = new Task<bool>[Frames.Length];
            for(int i = 0; i < Frames.Length; i++) {
                frames[i] = new AsyncRef<AnimationFrame>(Frames[i]);
                tasks[i] = processor.ProcessDataAsync(target, scopeKey, i, frames[i], token);
            }

            //Wait for tasks
            await Task.WhenAll(tasks);            

            //Return result
            bool didModify = false;
            for(int i = 0; i < Frames.Length; i++) {
                if(tasks[i].Result) {
                    didModify = true;
                    Frames[i] = frames[i].Data;
                }
            }
            return didModify;
        }
    }
}