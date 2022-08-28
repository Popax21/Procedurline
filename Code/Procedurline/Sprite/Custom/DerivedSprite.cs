using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements a <see cref="CustomSprite" />s which apply an <see cref="IAsyncDataProcessor{T, I, D}" /> to another sprite's data. This functionality can be used by custom gameplay elements to e.g. recolor vanilla sprites for use as their own sprite.
    /// </summary>
    public sealed class DerivedSprite : InvalidatableSprite {
        private new sealed class Animation : InvalidatableSprite.Animation, IDisposable {
            public new readonly DerivedSprite Sprite;
            public readonly Sprite.Animation OriginalAnimation;

            private Task procTask;
            private ulong procTaskValToken;
            private TextureHandle texHandle;
            private Sprite.Animation errorAnim;

            internal Animation(DerivedSprite sprite, string animId, Sprite.Animation origAnim) : base(sprite, animId) {
                Sprite = sprite;
                OriginalAnimation = origAnim;
            }

            public void Dispose() {
                lock(LOCK) {
                    //Dispose the texture handle
                    texHandle?.Dispose();
                    texHandle = null;
                }
            }

            public override Task ProcessData() {
                lock(LOCK) {
                    //Check the animation's validity
                    if(CheckValidity(out ulong valToken, out CancellationToken token)) return procTask;

                    //Check if there already is a valid processor task
                    if(procTask != null && IsTokenValid(procTaskValToken)) return procTask;

                    //Start new processor task
                    procTaskValToken = valToken;
                    return procTask = InvokeProcessor(valToken, token);
                }
            }

            private async Task InvokeProcessor(ulong valToken, CancellationToken token) {
                AsyncRef<TextureHandle> texHandleRef = new AsyncRef<TextureHandle>();
                try {
                    //Run processor
                    Stopwatch timer = ProcedurlineModule.Settings.LogProcessingTimes ? Stopwatch.StartNew() : null;

                    //Get original animation's data
                    AsyncRef<SpriteAnimationData> procAnimData = new AsyncRef<SpriteAnimationData>();
                    using(procAnimData.Data = await ProcedurlineModule.SpriteManager.GetAnimationData(OriginalAnimation)) {
                        bool didModify;
                        using(DataScopeKey scopeKey = CloneScopeKey(valToken)) {
                            if(scopeKey == null) return;
                            didModify = await Sprite.Processor.ProcessDataAsync(Sprite, scopeKey, AnimationID, procAnimData, token);
                        }

                        //Create the animation
                        Sprite.Animation procAnim = OriginalAnimation;
                        if(didModify) {
                            procAnim = await ProcedurlineModule.SpriteManager.CreateAnimation(AnimationID, Sprite.TextureScope, procAnimData, token, texHandleRef);
                        }

                        if(timer != null) {
                            Logger.Log(ProcedurlineModule.Name, $"Finished processing DerivedSprite animation '{Sprite.SpriteID}' animation '{AnimationID}' (took {timer.ElapsedMilliseconds}ms)");
                        }

                        //Replace animation
                        ReplaceAnimation(procAnim, texHandleRef.Data, valToken, true);
                    }
                } catch(Exception e) {
                    texHandleRef?.Data?.Dispose();

                    token.ThrowIfCancellationRequested();
                    if(!IsTokenValid(valToken)) return;

                    Logger.Log(LogLevel.Error, ProcedurlineModule.Name, $"Error while processing DerivedSprite '{Sprite.SpriteID}' animation '{AnimationID}': {e}");

                    //Create error animation, if we don't have one already
                    lock(LOCK) errorAnim ??= CreateErrorAnimation();

                    //Replace with error animation
                    ReplaceAnimation(errorAnim, null, valToken, true);

                    throw;
                }
            }

            private void ReplaceAnimation(Sprite.Animation anim, TextureHandle tex, ulong valToken, bool markValid) {
                lock(LOCK) {
                    lock(Sprite.LOCK) {
                        //Try to mark the animation is valid, or check if the token still is valid
                        if(markValid ? !MarkValid(valToken) : !IsTokenValid(valToken)) return;

                        //Replace animation data
                        ReplaceData(anim);

                        //Set texture handle
                        texHandle?.Dispose();
                        texHandle = tex;
                    }
                }
            }

            private Sprite.Animation CreateErrorAnimation() {
                Sprite.Animation anim = new Sprite.Animation();
                anim.Goto = OriginalAnimation.Goto;
                anim.Delay = OriginalAnimation.Delay;
                anim.Frames = new MTexture[OriginalAnimation.Frames.Length];
                for(int i = 0; i < anim.Frames.Length; i++) {
                    anim.Frames[i] = new MTexture(
                        ProcedurlineModule.TextureManager.ErrorTexture.MTexture,
                        OriginalAnimation.Frames[i].AtlasPath,
                        new Rectangle(0, 0, ProcedurlineModule.TextureManager.ErrorTexture.Width, ProcedurlineModule.TextureManager.ErrorTexture.Height),
                        OriginalAnimation.Frames[i].DrawOffset,
                        OriginalAnimation.Frames[i].Width, OriginalAnimation.Frames[i].Height
                    );
                    anim.Frames[i].ScaleFix = Math.Min(
                        (float) anim.Frames[i].Width / anim.Frames[i].ClipRect.Width,
                        (float) anim.Frames[i].Height / anim.Frames[i].ClipRect.Height
                    );
                }
                return anim;
            }
        }

        public readonly Sprite OriginalSprite;
        public readonly TextureScope TextureScope;

        /// <summary>
        /// NOTE: External sources invoking this processor <b>MUST</b> use <see cref="RegisterScopes" /> to register their scope keys instead of this processor's <see cref="IScopeRegistrar{T}" /> implementation.
        /// </summary>
        public readonly IAsyncDataProcessor<Sprite, string, SpriteAnimationData> Processor;

        public DerivedSprite(string spriteId, Sprite origSprite, IAsyncDataProcessor<Sprite, string, SpriteAnimationData> processor, TextureScope texScope = null) : base(spriteId, null, string.Empty) {
            OriginalSprite = origSprite;
            TextureScope = new TextureScope(spriteId, texScope ?? ProcedurlineModule.TextureManager.StaticScope);
            Processor = processor;
            InitWrapped(origSprite, (animId, anim) => new Animation(this, animId, anim));
        }

        public override void Dispose() {
            lock(LOCK) {
                base.Dispose();

                //Dispose animations
                foreach(Sprite.Animation anim in Animations.Values) (anim as IDisposable)?.Dispose();

                //Dispose the texture scope
                TextureScope?.Dispose();
            }
        }

        public override void RegisterScopes(DataScopeKey key) {
            base.RegisterScopes(key);

            //Register processor scopes
            Processor.RegisterScopes(this, key);

            //Chain to original sprite
            if(OriginalSprite is CustomSprite csprite) csprite.RegisterScopes(key);
        }
    }
}