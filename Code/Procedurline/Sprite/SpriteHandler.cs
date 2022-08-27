using System;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a sprite handler. Sprite handler objects are created and bound to each Monocle sprite by the <see cref="SpriteManager" />, which also allows access to them, or alternatively can be created manually using <see cref="SpriteManager.CreateSpriteHandler" />.
    /// Sprite handlers are responsible for integrating the backend sprite animation processing logic with the "frontend" <see cref="Sprite" /> / <see cref="CustomSprite" /> objects, by handling animation hooks, animation invalidation, async processing, etc.
    /// </summary>
    public sealed class SpriteHandler : IDisposable {
        public readonly object LOCK = new object();
        public readonly Sprite Sprite;
        public readonly string SpriteID;
        public readonly DataScopeKey ScopeKey;
        private bool hasValidKey;
        private bool customSpriteRegistered;

        public readonly bool OwnedByManager;
        internal int numManagerRefs;

        internal bool queueReload;

        private bool didError = false;
        private Dictionary<string, Sprite.Animation> errorAnimations = new Dictionary<string, Sprite.Animation>(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource cancelSrc;
        private Dictionary<string, Task<Sprite.Animation>> procTasks = new Dictionary<string, Task<Sprite.Animation>>(StringComparer.OrdinalIgnoreCase);

        internal SpriteHandler(Sprite sprite, string spriteId, bool ownedByManager) {
            Sprite = sprite;
            SpriteID = spriteId;
            OwnedByManager = ownedByManager;

            ScopeKey = new DataScopeKey(true);
            ScopeKey.OnInvalidate += ScopeInvalidated;

            //If the sprite is a custom sprite, register it
            if(sprite is CustomSprite customSprite) {
                customSprite.RegisterHandler(this);
                customSpriteRegistered = true;
            }

            queueReload = true;
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

                //If the sprite is a custom sprite, unregister it
                if(Sprite is CustomSprite customSprite && customSpriteRegistered) {
                    customSprite.UnregisterHandler(this);
                    customSpriteRegistered = false;
                }
            }
        }

        /// <summary>
        /// Gets the specified original animation for the sprite
        /// </summary>
        /// <returns>
        /// The animation, or <c>null</c> if no animation exists
        /// </returns>
        public Sprite.Animation GetOriginalAnimation(string animId) => Sprite.Animations.TryGetValue(animId, out Sprite.Animation anim) ? anim : null;

        /// <summary>
        /// Gets the specified processed animation for the sprite.
        /// If the processed animation isn't cached, this starts an asynchronous processing task, and the original animation is returned.
        /// </summary>
        /// <returns>
        /// The animation, or <c>null</c> if no animation exists
        /// </returns>
        public Sprite.Animation GetProcessedAnimation(string animId) {
            lock(ScopeKey.LOCK)
            lock(LOCK) {
                if(procTasks == null) throw new ObjectDisposedException("ProcessedSprite");

                //Get the original animation
                if(!Sprite.Animations.TryGetValue(animId, out Sprite.Animation origAnim)) origAnim = null;

                //If we don't have a valid scope key, obtain a new one
                while(!hasValidKey || !ScopeKey.IsValid) {
                    ScopeKey.Reset();
                    ProcedurlineModule.SpriteManager.DynamicAnimationMixer.RegisterScopes(Sprite, ScopeKey);
                    hasValidKey = true;
                }
                //We might race and not have an valid key outside of the loop, but in that case, our callbacks will reset our cache soon anyway

                //Check for already running task
                if(!procTasks.TryGetValue(animId, out Task<Sprite.Animation> procTask)) {
                    //Start new processor task
                    if(cancelSrc == null) cancelSrc = new CancellationTokenSource();
                    procTasks.Add(animId, procTask = ProcessAnimation(animId, origAnim, cancelSrc.Token));
                    procTask.ContinueWithOrInvoke(t => {
                        if(t.Status == TaskStatus.RanToCompletion) {
                            MainThreadHelper.Do(() => Sprite.ReloadAnimation());  
                        } else if(t.Exception != null) {
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

        private async Task<Sprite.Animation> ProcessAnimation(string animId, Sprite.Animation origAnim, CancellationToken token) {
            AsyncRef<Sprite.Animation> animRef = new AsyncRef<Sprite.Animation>(origAnim);
            await ProcedurlineModule.SpriteManager.DynamicAnimationMixer.ProcessDataAsync(Sprite, ScopeKey, animId, animRef, token);
            return animRef.Data;
        }

        /// <summary>
        /// Resets the sprite's cache of processor tasks.
        /// </summary>
        public void ResetCache() {
            lock(LOCK) {
                hasValidKey = false;
                ScopeKey.Reset();

                //Cancel all tasks
                cancelSrc?.Cancel();
                cancelSrc?.Dispose();
                cancelSrc = null;
                procTasks.Clear();

                queueReload = true;
            }
        }

        private void ScopeInvalidated(IScopedInvalidatable key) => ResetCache();
        internal void AnimationReloaded(CustomSpriteAnimation anim) => Sprite.ReloadAnimation(anim.AnimationID);

        internal bool DrawDebug(Scene scene, Matrix mat, Dictionary<SpriteHandler, Rectangle> rects, Dictionary<SpriteHandler, Rectangle> nrects, bool layoutPass) {
            //Build string to be drawn
            StringBuilder str = new StringBuilder();

            str.AppendLine(SpriteID);

            if(OwnedByManager) {
                str.AppendLine($"MANAGED {numManagerRefs} refs");
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
                str.Append(hasValidKey ? ScopeKey.GetScopeListString("\n") : "<<<INVAL KEY>>>");
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

                    foreach((SpriteHandler ps, Rectangle pr) in (rects ?? nrects)) {
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
    }
}