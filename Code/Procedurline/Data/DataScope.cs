using System;
using System.Collections;
using System.Collections.Generic;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a data scope. Data scopes are collection of objects who share one or more properties.
    /// Objects are represented through "scope keys". Scope keys represent a specific configuration of scopes which are "registered" on the key.
    /// When a scope is invalidated, all registered scope keys get invalidated as well.
    /// </summary>
    /// <seealso cref="DataScopeKey" />
    /// <seealso cref="IScopeRegistrar{T}" />
    public class DataScope : IDisposable, IScopedInvalidatable, IReadOnlyCollection<DataScopeKey> {
        internal readonly object LOCK = new object(); //Locking strategy: once a scope lock is held, you CAN NOT lock any other locks, neither scope nor key locks
        internal LinkedList<DataScopeKey> registeredKeys = new LinkedList<DataScopeKey>();

        /// <summary>
        /// Holds the entry which will be added to <see cref="DataScopeKey" />'s <see cref="DataScopeKey.DataStore" /> once the scope is registered on them. This mechanism can be used to efficiently store auxiliary data together with a given scope.
        /// </summary>
        public readonly DictionaryEntry? DataStoreEntry;

        /// <summary>
        /// If <paramref name="name" /> is <c>null</c>, the scope is a so called "anonymous scope". These scopes aren't explicitly listed in debug views.
        /// </summary>
        public DataScope(string name, DictionaryEntry? storeEntry = null) {
            Name = name;
            DataStoreEntry = storeEntry;
        }

        public virtual void Dispose() {
            LinkedList<DataScopeKey> invalKeys = null;
            lock(LOCK) {
                if(registeredKeys != null) invalKeys = InvalidateLocked();
                registeredKeys = null;
            }
            if(invalKeys != null) InvalidateUnlocked(invalKeys);
        }

        /// <summary>
        /// Register a key on the scope. This results in the key's validity becoming tied to the scope's validity.
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        /// <returns>
        /// Returns <c>false</c> if the key was already registered/couldn't be registered
        /// </returns>
        public virtual bool RegisterKey(DataScopeKey key) {
            lock(key.LOCK) {
                if(key.IsDisposed) throw new ObjectDisposedException("DataScopeKey");
                if(!key.IsValid) throw new ArgumentException("Scope key is invalidated!");
                return key.RegisterScope(this);
            }
        }

        /// <summary>
        /// Invalidates the scope. All registered keys will get invalidated
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        /// <seealso cref="DataScopeKey.Invalidate" />
        public virtual void Invalidate() {
            LinkedList<DataScopeKey> invalKeys;
            lock(LOCK) {
                if(registeredKeys == null) throw new ObjectDisposedException("DataScope");
                invalKeys = InvalidateLocked();
            }
            InvalidateUnlocked(invalKeys);
            OnInvalidate?.Invoke(this);
        }

        /// <summary>
        /// Invalidates the registrars of all registered keys.
        /// This function should be called anytime that targets belonging to this scope could be assigned a different set of scopes if they were to get re-registered (usually through an <see cref="IScopeRegistrar{T}" />).
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        /// <seealso cref="DataScopeKey.InvalidateRegistrars" />
        public virtual void InvalidateRegistrars() {
            LinkedList<DataScopeKey> invalKeys;
            lock(LOCK) {
                if(registeredKeys == null) throw new ObjectDisposedException("DataScope");
                invalKeys = new LinkedList<DataScopeKey>(registeredKeys);
            }
            foreach(DataScopeKey key in invalKeys) key.InvalidateRegistrars();
            OnInvalidateRegistrars?.Invoke(this);
        }

        private LinkedList<DataScopeKey> InvalidateLocked() {
            LinkedList<DataScopeKey> invalKeys = new LinkedList<DataScopeKey>(registeredKeys);
            foreach(DataScopeKey key in invalKeys) {
                if(key.scopes.TryGetValue(this, out LinkedListNode<DataScopeKey> node) && node != null) {
                    //The key still has a valid reference to us

                    //Mark the key as "invalidating"
                    //The key will still be valid during the duration of the operation it is currently executing
                    lock(key.VALIDITY_LOCK) key.isInvalidating = true;
                    key.scopes[this] = null;
                }
            }
            registeredKeys.Clear();
            return invalKeys;
        }

        private void InvalidateUnlocked(LinkedList<DataScopeKey> invalKeys) {
            //Finish key invalidation
            foreach(DataScopeKey key in invalKeys) {
                lock(key.LOCK) {
                    if(!key.IsDisposed && key.isInvalidating) key.Invalidate();
                }
            }
        }

        public IEnumerator<DataScopeKey> GetEnumerator() => registeredKeys.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{GetType()} [{Name ?? "<anonymous>"}]";

        public virtual string Name { get; }
        public bool IsDisposed { get { lock(LOCK) return registeredKeys != null; } }

        public int Count {
            get {
                lock(LOCK) {
                    if(registeredKeys == null) throw new ObjectDisposedException("DataScope");
                    return registeredKeys.Count;
                }
            }
        }

        public event Action<IScopedInvalidatable> OnInvalidate;
        public event Action<IScopedInvalidatable> OnInvalidateRegistrars;
    }
}