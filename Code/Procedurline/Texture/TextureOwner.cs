using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents something owning one or multiple textures
    /// </summary>
    public abstract class TextureOwner : IDisposable {
        public readonly object LOCK = new object();
        private bool isDisposed = false;
        private LinkedListNode<TextureOwner> scopeNode = null;

        public TextureOwner(string name, TextureScope scope) {
            scope ??= ProcedurlineModule.TextureManager?.GlobalScope;

            Name = name;
            Scope = scope;

            if(scope != null) {
                //Add to scope
                lock(LOCK) {
                    lock(scope.LOCK) scopeNode = scope.owners.AddLast(this);
                }
            }
        }

        public virtual void Dispose() {
            lock(LOCK) {
                //Remove from scope
                if(scopeNode != null) {
                    lock(Scope.LOCK) Scope.owners.Remove(scopeNode);
                    scopeNode = null;
                }

                isDisposed = true;
            }
        }

        public override string ToString() => $"{GetType().Name} '{Path}' [{NumTextures} textures]";

        public virtual string Name { get; }
        public string Path => (Scope == null) ? $"/{Name}" : $"{Scope.Path}/{Name}";
        public TextureScope Scope { get; }
        public bool IsDisposed => isDisposed;

        /// <summary>
        /// Returns the number of actual textures owned by this owner
        /// </summary>
        public abstract int NumTextures { get; }
    }

    /// <summary>
    /// Manages texture lifecycle. If a scope is disposed, all textures and sub-scopes owned by it are also disposed
    /// </summary>
    public class TextureScope : TextureOwner, IEnumerable<TextureOwner> {
        internal LinkedList<TextureOwner> owners = new LinkedList<TextureOwner>();

        public TextureScope(string name, TextureScope scope) : base(name, scope) {}

        public override void Dispose() {
            lock(LOCK) {
                if(owners != null) {
                    Clear();
                    owners = null;
                }

                base.Dispose();
            }
        }

        /// <summary>
        /// Clears and disposes all textures and sub-scopes owned by this scope
        /// </summary>
        public virtual void Clear() {
            //Dispose owners in scope
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureScope");
                while(owners.First != null) owners.First.Value.Dispose();
            }
        }

        public IEnumerator<TextureOwner> GetEnumerator() => owners.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override int NumTextures {
            get {
                lock(LOCK) return owners.Sum(o => o.NumTextures);
            }
        }
    }
}