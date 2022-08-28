using System;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a dynamic sprite animation. Procedurline assumes all Monocle Sprite.Animation instances are static by default, however, when an animation extends this class, it will assume that the data can change at any time.
    /// To ensure thread safety, updates to the normal animation data variables are only done on the main thread, and a seperate set of thread-safe variables are provided, protected by <see cref="LOCK" />.
    /// Note that dynamic sprite animations by themselves <b>do not allow one to dynamically change data after it has finished processing</b>. If this is needed, you need to implement a proper scope invalidation mechanism using <see cref="CustomSprite.RegisterScopes" />.
    /// </summary>
    public class DynamicSpriteAnimation : Sprite.Animation {
        public readonly object LOCK = new object();
        public float TDelay;
        public Chooser<string> TGoto;
        public MTexture[] TFrames;
        private readonly MTexture[] DummyFrames;

        private bool syncingFromThreaded, syncingToThreaded;

        protected DynamicSpriteAnimation() {
            Delay = TDelay = 0f;
            Goto = TGoto = null;
            Frames = TFrames = DummyFrames = new MTexture[] { ProcedurlineModule.TextureManager.EmptyTexture.MTexture };
        }

        /// <summary>
        /// Queues a copy of this animation's thread-safe data to the animation's normal data. Cancels syncs queued by <see cref="QueueSyncToThreaded" />.
        /// </summary>
        public virtual void QueueSyncFromThreaded() {
            lock(LOCK) {
                //Check if a sync is already queued
                if(syncingFromThreaded) return;
                syncingFromThreaded = true;
                syncingToThreaded = false;
            }

            //Queue a sync on the main thread
            MainThreadHelper.Do(() => {
                lock(LOCK) {
                    if(!syncingFromThreaded) return;
                    Delay = TDelay;
                    Goto = TGoto;
                    Frames = TFrames;
                    syncingFromThreaded = false;
                }
                NotifyNonThreadedDataChanged();
            });
        }

        /// <summary>
        /// Queues a copy of this animation's normal data to the animation's thread-safe data. Cancels syncs queued by <see cref="QueueSyncFromThreaded" />.
        /// </summary>
        public virtual void QueueSyncToThreaded() {
            lock(LOCK) {
                //Check if a sync is already queued
                if(syncingToThreaded) return;
                syncingToThreaded = true;
                syncingFromThreaded = false;
            }

            //Queue a sync on the main thread
            MainThreadHelper.Do(() => {
                lock(LOCK) {
                    if(!syncingToThreaded) return;
                    TDelay = Delay;
                    TGoto = Goto;
                    TFrames = Frames;
                    syncingToThreaded = false;
                }
                NotifyThreadedDataChanged();
            });
        }

        /// <summary>
        /// Helper method which replaces this animation's thread-safe animation data, and then queues a sync
        /// </summary>
        public virtual void ReplaceData(Sprite.Animation anim) {
            lock(LOCK) {
                if(anim == null) {
                    TDelay = anim?.Delay ?? 0f;
                    TGoto = anim?.Goto ?? null;
                    TFrames = anim?.Frames ?? DummyFrames;
                } else {
                    anim.GetAnimationData(out TDelay, out TGoto, out TFrames);
                }

                QueueSyncFromThreaded();
            }
            NotifyThreadedDataChanged();
        }

        protected void NotifyNonThreadedDataChanged() => OnNonThreadedDataChange?.Invoke(this);
        protected void NotifyThreadedDataChanged() => OnThreadedDataChange?.Invoke(this);

        protected event Action<DynamicSpriteAnimation> OnNonThreadedDataChange;
        protected event Action<DynamicSpriteAnimation> OnThreadedDataChange;
    }
}