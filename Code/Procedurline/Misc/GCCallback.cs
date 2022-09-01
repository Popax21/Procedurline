using System;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A helper class which calls a callback every time a garbage collector generation occurs
    /// </summary>
    public sealed class GCCallback : IDisposable {
        private class DanglingObject {
            private readonly GCCallback callback;
            public DanglingObject(GCCallback cb) => callback = cb;

            ~DanglingObject() {
                if(callback.alive) {
                    callback.callback();
                    new DanglingObject(callback);
                }
            }
        }

        internal bool alive;
        internal Action callback;

        public GCCallback(Action cb) {
            alive = true;
            callback = cb;

            new DanglingObject(this);
        }

        public void Dispose() => alive = false;

        public bool Alive => alive;
    }
}