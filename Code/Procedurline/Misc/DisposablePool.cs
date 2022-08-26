using System;
using System.Collections.Generic;

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
}