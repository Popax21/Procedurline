using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a data scope. Data scopes are collection of objects who share one or more properties.
    /// Objects are represented through "scope keys". Scope keys represent a specific configuration of scopes which are "registered" on the key.
    /// When a scope is invalidated, all registered scope keys get invalidated as well.
    /// </summary>
    /// <seealso cref="DataScopeKey" />
    /// <seealso cref="IDataScopeRegistrar{T}" />
    public class DataScope : IDisposable, IReadOnlyCollection<DataScopeKey> {
        internal readonly object LOCK = new object(); //Locking strategy: once a scope lock is held, you CAN NOT lock any other locks, neither scope nor key locks
        internal LinkedList<DataScopeKey> registeredKeys = new LinkedList<DataScopeKey>();

        public DataScope(string name) => Name = name;

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
        /// This function should be called anytime that targets belonging to this scope could be assigned a different set of scopes if they were to get re-registered (usually through an <see cref="IDataScopeRegistrar{T}" />).
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

        public override string ToString() => $"{GetType().Name} [{Name}]";

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

        public event Action<DataScope> OnInvalidate;
        public event Action<DataScope> OnInvalidateRegistrars;
    }

    /// <summary>
    /// Represents a data scope key. For details, see <c cref="DataScope">DataScope</c>
    /// </summary>
    /// <seealso cref="DataScope" />
    public class DataScopeKey : IDisposable, IEquatable<DataScopeKey>, IReadOnlyCollection<DataScope> {
        private const int HASH_MAGIC = unchecked((int) 0xcafec0de);
        private static long NEXT_ID = 0;

        public readonly ulong ID = unchecked((ulong) Interlocked.Increment(ref NEXT_ID));

        /// <summary>
        /// When locking multiple keys, lock the one with the lower ID first
        /// </summary>
        public readonly object LOCK = new object();

        /// <summary>
        /// When holding this lock, IsValid cannot change.
        /// You must also be holding <see cref="LOCK" /> before locking/using this lock, or undefined behaviour will occur.
        /// <b>DO NOT CALL ANY NON-TRIVIAL METHODS WHILE HOLDING THIS LOCK</b> 
        /// </summary>
        public readonly object VALIDITY_LOCK = new object();

        //Protected using LOCK, individual entries are additionally secured using their scope's lock
        internal ConcurrentDictionary<DataScope, LinkedListNode<DataScopeKey>> scopes = new ConcurrentDictionary<DataScope, LinkedListNode<DataScopeKey>>();
        internal bool isInvalidating;
        private bool isValid;
        private int hashCode;

        private List<IDisposable> ownedObjects = new List<IDisposable>();
        private bool ownsSelf = false;

        public DataScopeKey() => Reset();

        /// <summary>
        /// Disposes the scope key. This effectively performs a call to <see cref="Invalidate" /> before destryoing the key.
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        public virtual void Dispose() {
            lock(LOCK) {
                if(scopes != null) Reset();
                scopes = null;
            }
        }

        /// <summary>
        /// Copy the scope key
        /// </summary>
        public virtual void Copy(DataScopeKey dst) {
            void CopyInternal() {
                try {
                    if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                    if(dst.scopes == null) throw new ObjectDisposedException("DataScopeKey");

                    dst.Reset();
                    foreach(DataScope scope in scopes.Keys) {
                        lock(scope.LOCK) {
                            if(isInvalidating) break;
                            dst.RegisterScope(scope);
                        }
                    }

                    if(!IsValid) dst.Invalidate();
                } catch(Exception) {
                    if(!dst.IsDisposed) dst.Reset();
                    throw;
                }
            }

            if(ID < dst.ID) {
                lock(LOCK) lock(dst.LOCK) CopyInternal();
            } else {
                lock(dst.LOCK) lock(LOCK) CopyInternal();
            }
        }

        /// <summary>
        /// Called when this key is registered on a new scope
        /// Both the scope and key lock are held, <b>so you CAN NOT call functions like <see cref="Invalidate" /> or <see cref="Reset" /></b>
        /// </summary>
        /// <returns>
        /// <c>false</c> if the key couldn't be registered
        /// </returns>
        protected internal virtual bool RegisterScope(DataScope scope) {
            lock(scope.LOCK) {
                //Add key to the scope's key list
                if(scope.registeredKeys == null) return false;
                if(scopes.ContainsKey(scope)) return false;

                scopes.TryAdd(scope, scope.registeredKeys.AddLast(this));

                //Update hash code
                //Yes, using XORs for hashing is usually not a good idea, but we have to ensure that no matter the order of scopes registration, the same hash code is returned
                hashCode = unchecked(hashCode ^ scope.GetHashCode());
            }
            return true;
        }

        /// <summary>
        /// Invalidates the scope key. Invalidated scope keys can't be registered on new scopes or take ownership of objects until they're reset using Reset().
        /// Also disposes all owned objects which the key took ownership of using <see cref="TakeOwnership" />.
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        public virtual void Invalidate() {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(!isValid) return;

                //Cleanup
                RemoveFromScopes();
                isInvalidating = false;
                isValid = false;

                //Invoke event
                OnInvalidate?.Invoke(this);

                //Dispose owned objects
                foreach(IDisposable obj in ownedObjects) obj.Dispose();
                ownedObjects.Clear();

                if(ownsSelf) Dispose();
            }
        }

        /// <summary>
        /// Invalidates the scope key's registrars, which does nothing other than invoking <see cref="OnInvalidateRegistrars" />.
        /// This event should be used to notify the component responsible for registring the key's scopes (usually through an <see cref="IDataScopeRegistrar{T}" />) that their target could potentially be assigned different scopes after the invalidation.
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        public virtual void InvalidateRegistrars() {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(!IsValid) return;

                //Invoke event
                OnInvalidateRegistrars?.Invoke(this);
            }
        }

        /// <summary>
        /// Resets the scope key. This removes it from all scopes it's registered on and resets its validity, so that it can be used again in the future.
        /// Also disposes all owned objects which the key took ownership of using <see cref="TakeOwnership" />, except itself, if the key owns itself.
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        public virtual void Reset() {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");

                //Cleanup
                RemoveFromScopes();
                scopes.Clear();
                isInvalidating = false;
                isValid = true;
                hashCode = HASH_MAGIC;

                //Dispose owned objects
                foreach(IDisposable obj in ownedObjects) obj.Dispose();
                ownedObjects.Clear();
                ownsSelf = false;
            }
        }

        private void RemoveFromScopes() {
            foreach(DataScope scope in scopes.Keys) {
                lock(scope.LOCK) {
                    //We can now safely access the value
                    LinkedListNode<DataScopeKey> node = scopes[scope];
                    if(node == null) continue;

                    if(scope.registeredKeys != null) scope.registeredKeys.Remove(node);
                    scopes[scope] = null;
                }
            }
        }

        /// <returns>
        /// Returns <c>true</c> if the key is registered on the specified scope
        /// </returns>
        public bool IsRegistered(DataScope scope) {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                return scopes.ContainsKey(scope);
            }
        }

        /// <returns>
        /// Takes ownership of the given disposable objects. Owned objects are disposed when the key is invalidated or reset using <see cref="Invalidate" /> or <see cref="Reset" />.
        /// If the key is already invalidated, the object is immediately disposed.
        /// If the object given is the key itself, then the key will automatically dispose itself when it's invalidated.
        /// </returns>
        public virtual void TakeOwnership(IDisposable obj) {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(!IsValid) obj.Dispose();
                if(obj is DataScopeKey kobj && kobj == this) ownsSelf = true;
                else ownedObjects.Add(obj);
            }
        }

        /// <returns>
        /// Gets the owned object of the specified type, or <c>null</c> if there is no owned object of that type.
        /// </returns>
        public virtual T GetOwnedObject<T>() where T : class, IDisposable {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(!IsValid) return null;
                return ownedObjects.FirstOrDefault(o => o is T) as T;
            }
        }

        public override bool Equals(object obj) => obj is DataScopeKey other && Equals(other);
        public virtual bool Equals(DataScopeKey other) {
            if(ID > other.ID) return other.Equals(this);

            lock(LOCK) lock(VALIDITY_LOCK) {
                lock(other.LOCK) lock(other.VALIDITY_LOCK) {
                    if(!IsValid || !other.IsValid) return false;
                    if(hashCode != other.hashCode) return false;
                    if(scopes.Count != other.scopes.Count) return false;

                    foreach(DataScope scope in scopes.Keys) {
                        if(!other.scopes.ContainsKey(scope)) return false;
                    }
                    return true;
                }
            }
        }

        public override int GetHashCode() {
            lock(LOCK) return unchecked(hashCode * 31 + scopes.Count);
        }

        /// <summary>
        /// Returns a string list of the names of all scopes the key belongs to, seperated by <paramref name="delim" /> 
        /// </summary>
        public string GetScopeListString(string delim) {
            lock(LOCK) {
                if(scopes == null) return "<DISPOSED>";

                StringBuilder builder = new StringBuilder();
                foreach(DataScope scope in scopes.Keys) {
                    if(builder.Length > 0) builder.Append(delim);
                    builder.Append(scope.Name);
                }
                return builder.ToString();
            }
        }
        public override string ToString() => $"{GetType().Name} [{GetScopeListString("; ")}]";

        public IEnumerator<DataScope> GetEnumerator() => scopes.Keys.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsDisposed { get { lock(LOCK) return scopes == null; } }
        public bool IsValid {
            get {
                lock(LOCK) {
                    lock(VALIDITY_LOCK) return scopes != null && !isInvalidating && isValid;
                }
            }
        }

        public int Count {
            get {
                lock(LOCK) {
                    if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                    return scopes.Count;
                }
            }
        }

        /// <summary>
        /// Invoked by <seealso cref="Invalidate" /> when the key is invalidated before disposing owned objects, with <see cref="DataScopeKey.LOCK" /> held.
        /// </summary>
        public event Action<DataScopeKey> OnInvalidate;

        /// <summary>
        /// Invoked by <seealso cref="InvalidateRegistrars" /> when the key's registrars are invalidated, with <see cref="DataScopeKey.LOCK" /> held.
        /// </summary>
        public event Action<DataScopeKey> OnInvalidateRegistrars;

        public static bool operator ==(DataScopeKey a, DataScopeKey b) => a.Equals(b);
        public static bool operator !=(DataScopeKey a, DataScopeKey b) => !a.Equals(b);
    }

    /// <summary>
    /// Represents something capable of registering appropiate scopes of a target on a given key
    /// </summary>
    /// <seealso cref="DataScope" />
    /// <seealso cref="DataScopeKey" />
    public interface IDataScopeRegistrar<T> {
        /// <summary>
        /// Register all scopes the target belongs to on the key
        /// </summary>
        void RegisterScopes(T target, DataScopeKey key);
    }
}