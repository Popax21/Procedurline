using System;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents statically processed sprites.
    /// Most of the actual logic is in <see cref="StaticSpriteAnimation" />, but this class is used to keep common data for all animations and provides the static sprite constructor.
    /// </summary>
    public sealed class StaticSprite : CustomSprite, IScopedInvalidatable, IDisposable {
        private static readonly FieldInfo Sprite_currentAnimation = typeof(Sprite).GetField("currentAnimation", BindingFlags.NonPublic | BindingFlags.Instance);

        public readonly bool IsCopy;
        public readonly TextureScope TextureScope;
        public readonly Sprite OriginalSprite;

        /// <summary>
        /// External sources invoking this processor <b>MUST</b> register the $GLOBAL and $STATIC scopes themselves.
        /// </summary> 
        public readonly IAsyncDataProcessor<Sprite, string, SpriteAnimationData> Processor;

        public StaticSprite(string spriteId, Sprite origSprite, IAsyncDataProcessor<Sprite, string, SpriteAnimationData> processor, TextureScope texScope = null) : base(spriteId, null, string.Empty) {
            IsCopy = false;
            OriginalSprite = origSprite;
            TextureScope = new TextureScope(spriteId, texScope ?? ProcedurlineModule.TextureManager.StaticScope);
            Processor = processor;

            origSprite.CloneInto(this);

            //Replace animations
            foreach(string animId in Animations.Keys.ToArray()) {
                Sprite.Animation anim = Animations[animId];
                Animations[animId] = new StaticSpriteAnimation(this, animId, Animations[animId]);
            }

            if(!string.IsNullOrEmpty(CurrentAnimationID)) {
                Sprite_currentAnimation.SetValue(this, Animations[CurrentAnimationID]);
            }
        }

        private StaticSprite(StaticSprite origSprite) : base(origSprite.SpriteID, null, string.Empty) {
            IsCopy = true;
            OriginalSprite = origSprite.OriginalSprite;
            TextureScope = origSprite.TextureScope;
            Processor = origSprite.Processor;

            origSprite.CloneInto(this);

            //Replace animations
            foreach(string animId in Animations.Keys.ToArray()) {
                Sprite.Animation anim = Animations[animId];
                if(anim is StaticSpriteAnimation staticAnim) {
                    Animations[animId] = new StaticSpriteAnimation(this, staticAnim);
                } else Animations[animId] = anim;
            }

            if(!string.IsNullOrEmpty(CurrentAnimationID)) {
                Sprite_currentAnimation.SetValue(this, Animations[CurrentAnimationID]);
            }
        }

        public void Dispose() {
            //Dispose animations
            foreach(Sprite.Animation anim in Animations.Values) (anim as IDisposable)?.Dispose();
        }

        public override Monocle.Sprite CreateCopy() => new StaticSprite(this);

        public override void RegisterSprite(ProcessedSprite procSprite) {
            //Chain to original sprite
            if(OriginalSprite is CustomSprite csprite) csprite.RegisterSprite(procSprite);
        }

        public override void UnregisterSprite(ProcessedSprite procSprite) {
            //Chain to original sprite
            if(OriginalSprite is CustomSprite csprite) csprite.UnregisterSprite(procSprite);
        }

        public override void RegisterScopes(DataScopeKey key) {
            //Register scopes
            ProcedurlineModule.GlobalScope.RegisterKey(key);
            ProcedurlineModule.StaticScope.RegisterKey(key);
            Processor.RegisterScopes(this, key);

            //Chain to original sprite
            if(OriginalSprite is CustomSprite csprite) csprite.RegisterScopes(key);
        }

        public void Invalidate() {
            foreach(Sprite.Animation anim in Animations.Values) (anim as IScopedInvalidatable)?.Invalidate();
            OnInvalidate?.Invoke(this);
        }

        public void InvalidateRegistrars() {
            foreach(Sprite.Animation anim in Animations.Values) (anim as IScopedInvalidatable)?.InvalidateRegistrars();
            OnInvalidateRegistrars?.Invoke(this);
        }

        public event Action<IScopedInvalidatable> OnInvalidate;
        public event Action<IScopedInvalidatable> OnInvalidateRegistrars;
    }

    /// <summary>
    /// Represents statically processed sprite animations.
    /// Statically processed sprite animations are used by e.g. custom boosters, but because of Procedurline's design still have to run asynchroniously.
    /// So this class is used to keep track of the processing task and potential scope invalidations.
    /// </summary>
    public sealed class StaticSpriteAnimation : CustomSpriteAnimation, IScopedInvalidatable, IDisposable {
        private static readonly Action<Sprite, MTexture> Sprite_SetFrame = (Action<Sprite, MTexture>) typeof(Sprite).GetMethod("SetFrame", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(Action<Sprite, MTexture>));

        public bool IsDisposed { get; private set; }
        public readonly StaticSprite Sprite;
        public readonly Sprite.Animation OriginalAnimation;

        private readonly DataScopeKey scopeKey;
        private Task processorTask;
        private CancellationTokenSource procTaskCancelSrc;
        private TextureHandle texHandle;

        private Sprite.Animation dummyAnim, errorAnim;

        internal StaticSpriteAnimation(StaticSprite sprite, string animId, Sprite.Animation origAnim) : base(animId) {
            Sprite = sprite;
            OriginalAnimation = origAnim;
            scopeKey = new DataScopeKey();
            scopeKey.OnInvalidate += _ => OnInvalidate?.Invoke(this);
            scopeKey.OnInvalidateRegistrars += k => k.Invalidate();

            //Setup dummy animation
            dummyAnim = new Sprite.Animation() {
                Delay = 0f,
                Goto = null,
                Frames = new MTexture[] { ProcedurlineModule.TextureManager.EmptyTexture.MTexture }
            };

            Delay = dummyAnim.Delay;
            Goto = dummyAnim.Goto;
            Frames = dummyAnim.Frames;

            //Add invalidation handlers
            if(origAnim is IScopedInvalidatable invalAnim) {
                invalAnim.OnInvalidate += OrigInvalidated;
                invalAnim.OnInvalidateRegistrars += OrigInvalidated;
            }
        }

        internal StaticSpriteAnimation(StaticSprite sprite, StaticSpriteAnimation origAnim) : base(origAnim.AnimationID) {
            Sprite = sprite;
            OriginalAnimation = origAnim;

            //Copy state of original animation
            Delay = origAnim.Delay;
            Goto = origAnim.Goto;
            Frames = origAnim.Frames;
        }

        public void Dispose() {
            lock(LOCK) {
                if(IsDisposed) return;
                IsDisposed = true;

                scopeKey.Dispose();
                procTaskCancelSrc.Cancel();
                procTaskCancelSrc.Dispose();
                texHandle?.Dispose();
                texHandle = null;

                //Remove invalidation handlers
                if(OriginalAnimation is IScopedInvalidatable invalAnim) {
                    invalAnim.OnInvalidate -= OrigInvalidated;
                    invalAnim.OnInvalidateRegistrars -= OrigInvalidated;
                }
            }
        }

        public override Task UpdateAnimation() {
            if(Sprite.IsCopy && OriginalAnimation is CustomSpriteAnimation customAnim) {
                //Forward to original animation
                return customAnim.UpdateAnimation().ContinueWithOrInvoke(_ => {
                    lock(LOCK) ReplaceAnimation(customAnim);
                });
            }

            lock(LOCK) {
                lock(scopeKey.LOCK) {
                    lock(scopeKey.VALIDITY_LOCK) {
                        if(scopeKey.IsValid && processorTask != null) return processorTask;
                    }

                    //Cancel old processor task
                    procTaskCancelSrc?.Cancel();
                    procTaskCancelSrc?.Dispose();
                    procTaskCancelSrc = null;

                    procTaskCancelSrc = new CancellationTokenSource();
                    CancellationToken token = procTaskCancelSrc.Token;
 
                    //Replace with dummy animation
                    ReplaceAnimation(dummyAnim, null, token);

                    retryKey:;
                    //Reset scope key and register the sprite's scopes
                    scopeKey.Reset();
                    Sprite.RegisterScopes(scopeKey);
                    lock(scopeKey.VALIDITY_LOCK) {
                        if(!scopeKey.IsValid) {
                            //Could happen because of race conditions
                            goto retryKey;
                        }

                        //Start new processor task
                        if(ProcedurlineModule.Settings.UseThreadPool) {
                            processorTask = Task.Run(() => ProcessAnimation(procTaskCancelSrc.Token));
                        } else {
                            processorTask = ProcessAnimation(procTaskCancelSrc.Token);
                        }

                        if(!ProcedurlineModule.Settings.AsynchronousStaticProcessing) {
                            ProcedurlineModule.GlobalManager.BlockEngineOnTask(processorTask);
                        }

                        return processorTask;
                    }
                }
            }
        }

        private async Task ProcessAnimation(CancellationToken token) {
            AsyncRef<TextureHandle> texHandleRef = new AsyncRef<TextureHandle>();
            try {
                Stopwatch timer = new Stopwatch();
                timer.Start();

                //Get original animation's data
                using(SpriteAnimationData animData = await ProcedurlineModule.SpriteManager.GetAnimationData(OriginalAnimation)) {
                    //Run processor
                    AsyncRef<SpriteAnimationData> procAnimData = new AsyncRef<SpriteAnimationData>(animData);
                    Sprite.Animation procAnim = OriginalAnimation;
                    if(await Sprite.Processor.ProcessDataAsync(Sprite, scopeKey, AnimationID, procAnimData)) {
                        //Create new animation
                        procAnim = await ProcedurlineModule.SpriteManager.CreateAnimation(AnimationID, Sprite.TextureScope, procAnimData, token, texHandleRef);
                    }

                    if(ProcedurlineModule.Settings.LogProcessingTimes) {
                        Logger.Log(ProcedurlineModule.Name, $"Done processing static sprite animation '{Sprite.SpriteID}' animation '{AnimationID}' (took {timer.ElapsedMilliseconds}ms)");
                    }

                    //Replace animation
                    await ReplaceAnimation(procAnim, texHandleRef.Data, token);
                }
            } catch(Exception e) {
                token.ThrowIfCancellationRequested();
                if(!scopeKey.IsValid) return;
                Logger.Log(LogLevel.Error, ProcedurlineModule.Name, $"Error while processing static sprite '{Sprite.SpriteID}' animation '{AnimationID}': {e}");
                texHandleRef?.Data?.Dispose();

                //Create error animation
                lock(LOCK) {
                    if(errorAnim == null) {
                        errorAnim = new Sprite.Animation();
                        errorAnim.Goto = OriginalAnimation.Goto;
                        errorAnim.Delay = OriginalAnimation.Delay;
                        errorAnim.Frames = new MTexture[OriginalAnimation.Frames.Length];
                        for(int i = 0; i < errorAnim.Frames.Length; i++) {
                            errorAnim.Frames[i] = new MTexture(
                                ProcedurlineModule.TextureManager.ErrorTexture.MTexture,
                                OriginalAnimation.Frames[i].AtlasPath,
                                new Rectangle(0, 0, ProcedurlineModule.TextureManager.ErrorTexture.Width, ProcedurlineModule.TextureManager.ErrorTexture.Height),
                                OriginalAnimation.Frames[i].DrawOffset,
                                OriginalAnimation.Frames[i].Width, OriginalAnimation.Frames[i].Height
                            );
                            errorAnim.Frames[i].ScaleFix = Math.Min(
                                (float) errorAnim.Frames[i].Width / errorAnim.Frames[i].ClipRect.Width,
                                (float) errorAnim.Frames[i].Height / errorAnim.Frames[i].ClipRect.Height
                            );
                        }
                    }
                }

                //Replace with error animation
                await ReplaceAnimation(errorAnim, null, token);

                throw;
            }
        }

        private Task ReplaceAnimation(Sprite.Animation anim, TextureHandle tex, CancellationToken token) {
            TaskCompletionSource<object> complSrc = new TaskCompletionSource<object>();
            MainThreadHelper.Do(() => {
                try {
                    lock(LOCK) {
                        if(token.IsCancellationRequested) return;

                        ReplaceAnimation(anim);

                        //Set texture handle
                        texHandle?.Dispose();
                        texHandle = tex;

                        complSrc.SetResult(null);
                    }
                } catch(Exception e) {
                    complSrc.SetException(e);
                }
            });
            return complSrc.Task;
        }

        private void ReplaceAnimation(Sprite.Animation anim) {
            Delay = anim.Delay;
            Goto = anim.Goto;
            Frames = anim.Frames;

            //Reload playing frame
            if(Sprite.Animating && !string.IsNullOrEmpty(Sprite.CurrentAnimationID)) {
                if(Sprite.CurrentAnimationID.Equals(AnimationID, StringComparison.OrdinalIgnoreCase)) {
                    if(Sprite.CurrentAnimationFrame < Frames.Length) {
                        Sprite_SetFrame(Sprite, Frames[Sprite.CurrentAnimationFrame]);
                    } else {
                        Sprite.Texture = null;
                    }
                }
            } else if(!string.IsNullOrEmpty(Sprite.LastAnimationID)) {
                if(Sprite.LastAnimationID.Equals(AnimationID, StringComparison.OrdinalIgnoreCase)) {
                    if(Sprite.CurrentAnimationFrame < Frames.Length-1) {
                        //The animation's length increased
                        int oldFrame = Sprite.CurrentAnimationFrame;
                        Sprite.Play(AnimationID, true, false);
                        Sprite.SetAnimationFrame(oldFrame);
                    } else if(Goto != null) {
                        //We now have a Goto
                        Sprite.Play(Goto.Choose(), true, false);
                    } else Sprite_SetFrame(Sprite, Frames[Frames.Length - 1]);
                }
            }
        }

        public void Invalidate() {
            lock(LOCK) scopeKey.Invalidate();
        }
        public void InvalidateRegistrars() => Invalidate();

        private void OrigInvalidated(IScopedInvalidatable obj) => Invalidate();

        public event Action<IScopedInvalidatable> OnInvalidate;
        public event Action<IScopedInvalidatable> OnInvalidateRegistrars;
    }
}