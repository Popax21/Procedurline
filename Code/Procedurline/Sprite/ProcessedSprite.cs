using System;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a processed sprite. Processed sprite objects are created and bound to each Monocle sprite by the <see cref="SpriteManager" />, which also allows access to them.
    /// Processed sprites are responsible for integrating the backend sprite animation processing logic with the "frontend" <see cref="Sprite" /> / <see cref="CustomSprite" /> objects, by handling animation hooks, animation invalidation, async processing, etc.
    /// </summary>
    public sealed class ProcessedSprite : IDisposable {
        private static readonly FieldInfo Sprite_currentAnimation = typeof(Sprite).GetField("currentAnimation", BindingFlags.NonPublic | BindingFlags.Instance);

        public readonly object LOCK = new object();
        public readonly Sprite Sprite;
        public readonly string SpriteID;
        public readonly DataScopeKey ScopeKey;
        public readonly bool OwnedByManager;
        internal int numReferences;
        private bool hasValidKey;
        private bool customSpriteRegistered;

        private bool didError = false;
        private Dictionary<string, Sprite.Animation> errorAnimations = new Dictionary<string, Sprite.Animation>(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource cancelSrc;
        private Dictionary<string, Task<Sprite.Animation>> procTasks = new Dictionary<string, Task<Sprite.Animation>>(StringComparer.OrdinalIgnoreCase);

        internal ProcessedSprite(Sprite sprite, string spriteId, bool ownedByManager) {
            Sprite = sprite;
            SpriteID = spriteId;
            ScopeKey = new DataScopeKey();
            OwnedByManager = ownedByManager;

            ScopeKey.OnInvalidate += OnScopeInvalidate;
            ScopeKey.OnInvalidateRegistrars += OnScopeInvalidate;

            //If the sprite is a custom sprite, register it
            if(sprite is CustomSprite customSprite) {
                customSprite.RegisterSprite(this);
                customSpriteRegistered = true;
            }

            //Reload current animation
            ReloadAnimation();
        }

        public void Dispose() {
            lock(LOCK) {
                //Cancel tasks
                cancelSrc?.Cancel();
                cancelSrc?.Dispose();
                cancelSrc = null;

                ResetCache();
                procTasks = null;

                //Dispose the scope key
                ScopeKey?.Dispose();

                //If the sprite is a static sprite, unregister its handlers
                if(Sprite is CustomSprite customSprite && customSpriteRegistered) {
                    customSprite.UnregisterSprite(this);
                    customSpriteRegistered = false;
                }
            }
        }

        /// <summary>
        /// Gets the specific processed animation for the sprite.
        /// If the processed animation isn't cached, this starts an asynchronous processing task, and the original animation is returned.
        /// </summary>
        public Sprite.Animation GetAnimation(string animId) {
            lock(ScopeKey.LOCK)
            lock(LOCK) {
                if(procTasks == null) throw new ObjectDisposedException("ProcessedSprite");

                //Get the original animation
                if(!Sprite.Animations.TryGetValue(animId, out Sprite.Animation origAnim)) origAnim = null;

                //If we don't have a valid scope key, obtain a new one
                while(!hasValidKey || !ScopeKey.IsValid) {
                    ScopeKey.Reset();
                    ProcedurlineModule.SpriteManager.AnimationMixer.RegisterScopes(Sprite, ScopeKey);
                    hasValidKey = true;
                }
                //We might race and not have an valid key outside of the loop, but in that case, our callbacks will reset our cache soon anyway

                //Check for already running task
                if(!procTasks.TryGetValue(animId, out Task<Sprite.Animation> procTask)) {
                    //Start new processor task
                    if(cancelSrc == null) cancelSrc = new CancellationTokenSource();
                    procTasks.Add(animId, procTask = GetProcessorTask(animId, origAnim, cancelSrc.Token));
                    procTask.ContinueWithOrInvoke(t => {
                        if(t.Status == TaskStatus.RanToCompletion) ReloadAnimation(animId);
                        else if(t.Exception != null) {
                            Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Error processing sprite '{SpriteID}' animation '{animId}': {t.Exception}");
                        }
                    }, cancelSrc.Token);
                }

                if(procTask.IsCompleted) {
                    if(procTask.Status != TaskStatus.RanToCompletion) {
                        didError = true;

                        //Try to get a cached error animation
                        if(errorAnimations.TryGetValue(animId, out Sprite.Animation errorAnim)) return errorAnim;

                        //Create a new error animation
                        errorAnim = new Sprite.Animation();
                        errorAnim.Goto = origAnim?.Goto;
                        errorAnim.Delay = origAnim?.Delay ?? 0;
                        errorAnim.Frames = new MTexture[origAnim?.Frames?.Length ?? 1];
                        for(int i = 0; i < errorAnim.Frames.Length; i++) {
                            errorAnim.Frames[i] = new MTexture(
                                ProcedurlineModule.TextureManager.ErrorTexture.MTexture,
                                origAnim?.Frames[i]?.AtlasPath,
                                new Rectangle(0, 0, ProcedurlineModule.TextureManager.ErrorTexture.Width, ProcedurlineModule.TextureManager.ErrorTexture.Height),
                                origAnim?.Frames[i]?.DrawOffset ?? Vector2.Zero,
                                origAnim?.Frames[i]?.Width ?? 32, origAnim?.Frames[i]?.Height ?? 32
                            );
                            errorAnim.Frames[i].ScaleFix = Math.Min(
                                (float) errorAnim.Frames[i].Width / errorAnim.Frames[i].ClipRect.Width,
                                (float) errorAnim.Frames[i].Height / errorAnim.Frames[i].ClipRect.Height
                            );
                        }

                        errorAnimations.Add(animId, errorAnim);
                        return errorAnim;
                    } else return procTask.Result;
                }

                //Block the engine if async dynamic processing is disabled
                if(!ProcedurlineModule.Settings.AsynchronousDynamicProcessing) {
                    ProcedurlineModule.GlobalManager.BlockEngineOnTask(procTask);
                }

                //Return the original animation while the processor task is still running
                return origAnim;
            }
        }

        private async Task<Sprite.Animation> GetProcessorTask(string animId, Sprite.Animation origAnim, CancellationToken token) {
            AsyncRef<Sprite.Animation> animRef = new AsyncRef<Sprite.Animation>(origAnim);
            await ProcedurlineModule.SpriteManager.AnimationMixer.ProcessDataAsync(Sprite, ScopeKey, animId, animRef, token);
            return animRef.Data;
        }

        /// <summary>
        /// Reloads the sprite's current animation, if its ID is the one specified.
        /// If the animation ID is <c>null</c>, then reload no matter the current animation.
        /// </summary>
        public void ReloadAnimation(string animId = null) {
            MainThreadHelper.Do(() => {
                lock(LOCK) {
                    if(procTasks == null) return;

                    string curAnim = Sprite.CurrentAnimationID;
                    if(!Sprite.Animating || string.IsNullOrEmpty(curAnim)) curAnim = Sprite.LastAnimationID;
                    if(!string.IsNullOrEmpty(curAnim) && (animId == null || curAnim == animId)) {
                        Sprite.Texture = null;

                        //Reload the animation
                        Sprite.Animation anim = GetAnimation(curAnim);
                        if(anim == null) {
                            //The animation we were playing got erased
                            Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Currently playing sprite animation '{curAnim}' [sprite '{Sprite.Path}'] got erased after reload!");
                            Sprite.Stop();
                        } else {
                            if(Sprite.Animating && !string.IsNullOrEmpty(Sprite.CurrentAnimationID)) {
                                Sprite_currentAnimation.SetValue(Sprite, anim);
                                if(Sprite.CurrentAnimationFrame < anim.Frames.Length) {
                                    Sprite.Texture = anim.Frames[Sprite.CurrentAnimationFrame];
                                }
                            } else if(Sprite.CurrentAnimationFrame < anim.Frames.Length-1) {
                                //The animation's length increased
                                int oldFrame = Sprite.CurrentAnimationFrame;
                                Sprite.Play(curAnim, true, false);
                                Sprite.SetAnimationFrame(oldFrame);
                            } else {
                                Sprite.Texture = anim.Frames[anim.Frames.Length - 1];
                            }
                        }

                        OnAnimationReload?.Invoke(this, animId);
                    }
                }
            });
        }

        /// <summary>
        /// Resets the sprite's own cache of processor tasks. If given, only resets one animation's task (note that it will still continue to process because of a design limitation).
        /// </summary>
        public void ResetCache(string animId = null) {
            lock(LOCK) {
                hasValidKey = false;
                ScopeKey.Reset();

                if(animId == null) {
                    //Cancel all tasks
                    cancelSrc?.Cancel();
                    cancelSrc?.Dispose();
                    cancelSrc = null;
                    procTasks.Clear();
                } else {
                    //Remove the task
                    procTasks.Remove(animId, out _);
                }

                ReloadAnimation(animId);
            }
        }

        private void OnScopeInvalidate(IScopedInvalidatable key) => ResetCache();

        internal bool DrawDebug(Scene scene, Matrix mat, Dictionary<ProcessedSprite, Rectangle> rects, Dictionary<ProcessedSprite, Rectangle> nrects, bool layoutPass) {
            //Build string to be drawn
            StringBuilder str = new StringBuilder();

            str.AppendLine(SpriteID);

            if(OwnedByManager) {
                str.AppendLine($"MANAGED {numReferences} refs");
            } else {
                str.AppendLine($"UNMANAGED");
            }

            if(!string.IsNullOrEmpty(Sprite.CurrentAnimationID)) {
                str.AppendLine($"ANIM '{Sprite.CurrentAnimationID}' :{Sprite.CurrentAnimationFrame:d02}");
            } else if(!string.IsNullOrEmpty(Sprite.LastAnimationID)) {
                str.AppendLine($"ANIM '{Sprite.LastAnimationID}' END");
            } else {
                str.AppendLine($"ANIM <none>");
            }

            lock(LOCK) {
                str.AppendLine($"CACHE {procTasks.Count} ({procTasks.Count(kv => !kv.Value.IsCompleted)} pend)");
                if(didError) str.AppendLine("!!!ERROR!!!");
                str.Append(ScopeKey.GetScopeListString("\n"));
            }

            if(layoutPass) {
                //Determine the best rectangle to use
                float GetRectangleScore(Rectangle r) {
                    float score = 0;

                    Rectangle vr = Celeste.Viewport.Bounds;
                    if(!vr.Contains(r)) score -= r.Width * r.Height -
                        (Math.Max(r.Left, vr.Left) - Math.Min(r.Right, vr.Right)) *
                        (Math.Max(r.Top, vr.Top) - Math.Min(r.Bottom, vr.Bottom))
                    ;

                    foreach((ProcessedSprite ps, Rectangle pr) in (rects ?? nrects)) {
                        if(ps == this) continue;
                        if(pr.Intersects(r)) score -=
                            (Math.Max(r.Left, pr.Left) - Math.Min(r.Right, pr.Right)) *
                            (Math.Max(r.Top, pr.Top) - Math.Min(r.Bottom, pr.Bottom))
                        ;
                    }
                    return score;
                }

                Rectangle curRect = default;
                float curScore = float.NegativeInfinity;
                void TryRectangle(Rectangle nr, float pref) {
                    float score = pref + GetRectangleScore(nr);
                    if(curScore < score) {
                        curRect = nr;
                        curScore = score;
                    }
                }

                if(rects?.TryGetValue(this, out Rectangle orect) ?? false) {
                    curRect = orect;
                    curScore = GetRectangleScore(orect);
                } else orect = default;
    
                Vector2 drawPos = Vector2.Transform(Sprite.RenderPosition, mat), drawSize = Draw.DefaultFont.MeasureString(str);
                int drawX = (int) drawPos.X, drawY = (int) drawPos.Y, drawW = (int) drawSize.X, drawH = (int) drawSize.Y;
                TryRectangle(new Rectangle(drawX         +  6, drawY         +  6, drawW + 12, drawH + 12), 30);
                TryRectangle(new Rectangle(drawX - drawW - 18, drawY         +  6, drawW + 12, drawH + 12), 20);
                TryRectangle(new Rectangle(drawX         +  6, drawY - drawH - 18, drawW + 12, drawH + 12), 10);
                TryRectangle(new Rectangle(drawX - drawW - 18, drawY - drawH - 18, drawW + 12, drawH + 12),  0);

                nrects[this] = curRect;
                return curRect != orect;
            } else {
                //Draw string
                Rectangle rect = rects[this];
                Draw.Rect(rect, Color.Black * 0.7f);
                Draw.SpriteBatch.DrawString(Draw.DefaultFont, str, new Vector2(rect.X + 6, rect.Y + 6), Color.White);
                return false;
            }
        }

        public bool IsDisposed {
            get {
                lock(LOCK) return procTasks != null;
            }
        }

        public event Action<ProcessedSprite, string> OnAnimationReload;
    }
}