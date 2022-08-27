using System;
using System.Collections.Generic;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Holds a collection of <see cref="IDisposable" />s, which can then be all disposed at once. Usefull for keeping track of a dynamic collection of resources.
    /// </summary>
    public class DisposablePool : IDisposable {
        private readonly object LOCK = new object();
        private List<IDisposable> disposables = new List<IDisposable>();

        /// <summary>
        /// Disposes the disposable pool, disposing all the <see cref="IDisposable" />s in it.
        /// </summary>
        public virtual void Dispose() {
            lock(LOCK) {
                if(disposables == null) return;

                List<IDisposable> disps = disposables;
                disposables = null;
                for(int i = disps.Count-1; i >= 0; i--) disps[i].Dispose();
            }
        }

        /// <summary>
        /// Adds an <see cref="IDisposable" /> to the pool. If the pool's already diposed, immediately disposes the given diposable.
        /// </summary>
        /// <returns>
        /// The original diposable, or <c>null</c> if it was immediately disposed.
        /// </returns>
        public virtual IDisposable Add(IDisposable disp) {
            lock(LOCK) {
                if(disposables != null) {
                    disposables.Add(disp);
                    return disp;
                }
            }

            disp.Dispose();
            return null;
        }

        /// <summary>
        /// Adds an <see cref="IDisposable" /> to the pool. If the pool's already diposed, immediately disposes the given diposable.
        /// </summary>
        /// <returns>
        /// The original diposable, or <c>default</c> if it was immediately disposed.
        /// </returns>
        public virtual T Add<T>(T disp) where T : IDisposable {
            lock(LOCK) {
                if(disposables != null) {
                    disposables.Add(disp);
                    return disp;
                }
            }

            disp.Dispose();
            return default;
        }
    }

    /// <summary>
    /// A Monocle component which holds an <see cref="DisposablePool" />
    /// </summary>
    public sealed class DisposablePoolComponent : Component, IDisposable {
        /// <summary>
        /// Creates an <see cref="DisposablePoolComponent" />, adds it to a given entity, and returns its disposable pool
        /// </summary>
        public static DisposablePool AddTo(Entity entity) {
            DisposablePoolComponent comp = new DisposablePoolComponent();
            entity.Add(comp);
            return comp.Pool;
        }

        public readonly DisposablePool Pool = new DisposablePool();

        public DisposablePoolComponent() : base(false, false) {}

        public void Dispose() {
            //Delay disposal of the pool till the end of the frame, otherwise we might run into some realy nasty race conditions regarding component callback order
            Celeste.Scene.OnEndOfFrame += Pool.Dispose;
        }

        public override void Removed(Entity entity) {
            base.Removed(entity);
            this.Dispose();
        }

        public override void EntityRemoved(Scene scene) {
            base.EntityRemoved(scene);
            this.Dispose();
        }

        public override void SceneEnd(Scene scene) {
            base.SceneEnd(scene);
            this.Dispose();
        }
    }
}