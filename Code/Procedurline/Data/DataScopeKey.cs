using System;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a data scope key. For details, see <c cref="DataScope">DataScope</c>
    /// </summary>
    /// <seealso cref="DataScope" />
    public class DataScopeKey : IDisposable, IScopedObject, IScopedInvalidatable, IEquatable<DataScopeKey>, IReadOnlyCollection<DataScope> {
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

        /// <summary>
        /// When <c>true</c>, calls to <see cref="InvalidateRegistrars" /> behave the same as calls to <see cref="Invalidate" />
        /// </summary>
        public readonly bool InvalidateOnRegistrarInvalidation;

        //Protected using LOCK, individual entries are additionally secured using their scope's lock
        internal ConcurrentDictionary<DataScope, LinkedListNode<DataScopeKey>> scopes = new ConcurrentDictionary<DataScope, LinkedListNode<DataScopeKey>>();
        private HashSet<DataScope> nonTransparentScopes = new HashSet<DataScope>();
        private int hashCode;

        internal bool isInvalidating;
        private bool isValid;

        /// <summary>
        /// Contains the dictionary used to store scope data entries. These entries can be used to store auxiliary data along with data keys, which can then be efficiently queried by e.g. data processors.
        /// </summary>
        public readonly ConcurrentDictionary<object, object> DataStore = new ConcurrentDictionary<object, object>();

        private List<IDisposable> ownedObjects = new List<IDisposable>();
        private bool ownsSelf = false;

        public DataScopeKey() => Reset();
        public DataScopeKey(bool invalOnRegistrarInval) : this() => InvalidateOnRegistrarInvalidation = invalOnRegistrarInval;

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
        /// Copy the scope key's set of registered scopes onto a different key, resetting it in the process. If the key is invalid, the destination key will also be once the operations finishes.
        /// </summary>
        public virtual void Copy(DataScopeKey dst) {
            void CopyInternal() {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(dst.scopes == null) throw new ObjectDisposedException("DataScopeKey");

                dst.Reset();
                foreach(DataScope scope in scopes.Keys) {
                    lock(scope.LOCK) dst.RegisterScope(scope);
                }

                if(!IsValid) dst.Invalidate();
            }

            if(ID < dst.ID) {
                lock(LOCK) lock(dst.LOCK) CopyInternal();
            } else {
                lock(dst.LOCK) lock(LOCK) CopyInternal();
            }
        }

        /// <summary>
        /// Clones the scope key, returning an almost identical copy of it. The cloned key will not own any objects the original key took ownership of using <see cref="TakeOwnership" />, and have <see cref="InvalidateOnRegistrarInvalidation" /> set to <c>false</c>.
        /// </summary>
        public virtual DataScopeKey Clone() {
            DataScopeKey key = new DataScopeKey();
            try {
                Copy(key);
                return key;
            } catch(Exception) {
                key.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Register the scope key's set of registered scopes on a different key. If the key is invalid, the destination key will also be once the operations finishes.
        /// </summary>
        public void RegisterScopes(DataScopeKey dst) {
            void RegisterInternal() {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(dst.scopes == null) throw new ObjectDisposedException("DataScopeKey");

                foreach(DataScope scope in scopes.Keys) {
                    lock(scope.LOCK) dst.RegisterScope(scope);
                }

                if(!IsValid) dst.Invalidate();
            }

            if(ID < dst.ID) {
                lock(LOCK) lock(dst.LOCK) RegisterInternal();
            } else {
                lock(dst.LOCK) lock(LOCK) RegisterInternal();
            }
        }

        /// <summary>
        /// Called when this key is registered on a new scope.
        /// Both the scope lock (<see cref="DataScope.LOCK" />) and key lock (<see cref="LOCK" />) are held, <b>so you CAN NOT call functions like <see cref="Invalidate" /> or <see cref="Reset" /></b>
        /// </summary>
        /// <returns>
        /// <c>false</c> if the key couldn't be registered
        /// </returns>
        protected internal virtual bool RegisterScope(DataScope scope) {
            lock(scope.LOCK) {
                //Add key to the scope's key list
                if(scope.registeredKeys == null) return false;
                if(scopes.ContainsKey(scope)) return false;

                if(!scopes.TryAdd(scope, scope.registeredKeys.AddLast(this))) {
                    Environment.FailFast($"Could not add DataScope {scope} to key {this} scopes dict even though 'ContainsKey(scope) == false'!");
                }

                if(!scope.transparent) {
                    nonTransparentScopes.Add(scope);

                    //Update hash code
                    //Yes, using XORs for hashing is usually not a good idea, but we have to ensure that no matter the order of scopes registration, the same hash code is returned
                    hashCode = unchecked(hashCode ^ scope.GetHashCode());
                }

                //If the scope has a data store entry, add it to the key's store
                if(scope.DataStoreEntry is DictionaryEntry entry) {
                    if(!DataStore.TryAdd(entry.Key, entry.Value)) {
                        Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"DataScopeKey auxiliary data store key conflict for key '{entry.Key}' [type {entry.Key.GetType()}]!");
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Invalidates the scope key, invoking <see cref="OnInvalidate" />. Invalidated scope keys can't be registered on new scopes or take ownership of objects until they're reset using <see cref="Reset" />.
        /// Also disposes all owned objects which the key took ownership of using <see cref="TakeOwnership" />, including itself, if the key owns itself.
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
        /// Invalidates the scope key's registrars, which does nothing other than invoking <see cref="OnInvalidateRegistrars" />, unless <see cref="InvalidateOnRegistrarInvalidation" /> is <c>true</c>, in which case this simply calls <see cref="Invalidate" />.
        /// This event should be used to notify the component responsible for registring the key's scopes (usually through an <see cref="IScopeRegistrar{T}" />) that their target could potentially be assigned different scopes after the invalidation.
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        public virtual void InvalidateRegistrars() {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(InvalidateOnRegistrarInvalidation) Invalidate();
                if(!IsValid) return;

                //Invoke event
                OnInvalidateRegistrars?.Invoke(this);
            }
        }

        /// <summary>
        /// Resets the scope key. This removes it from all scopes it's registered on and resets its validity, so that it can be used again in the future.
        /// Also disposes all owned objects which the key took ownership of using <see cref="TakeOwnership" />, except itself, if the key owns itself (but the key would no longer do so in the future).
        /// <b>NOTE: Make sure you DO NOT hold any scope locks when calling this function, otherwise deadlocks could occur!</b>
        /// </summary>
        public virtual void Reset() {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");

                //Cleanup
                RemoveFromScopes();
                scopes.Clear();
                nonTransparentScopes.Clear();

                isInvalidating = false;
                isValid = true;
                hashCode = HASH_MAGIC;

                DataStore.Clear();

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
        /// Takes ownership of the given disposable object. Owned objects are disposed when the key is invalidated or reset using <see cref="Invalidate" /> or <see cref="Reset" />.
        /// If the key is already invalidated, the object is immediately disposed.
        /// If the object given is the key itself, then the key will automatically dispose itself when it's invalidated.
        /// </returns>
        public virtual void TakeOwnership(IDisposable obj) {
            lock(LOCK) {
                if(scopes == null) throw new ObjectDisposedException("DataScopeKey");
                if(!IsValid) {
                    obj.Dispose();
                    return;
                }

                if(object.ReferenceEquals(obj, this)) ownsSelf = true;
                else ownedObjects.Add(obj);
            }
        }

        public override bool Equals(object obj) => obj is DataScopeKey other && Equals(other);
        public virtual bool Equals(DataScopeKey other) {
            if(other == null) return false;
            if(ID > other.ID) return other.Equals(this);

            lock(LOCK) lock(VALIDITY_LOCK) {
                lock(other.LOCK) lock(other.VALIDITY_LOCK) {
                    if(IsValid != other.IsValid) return false;
                    if(hashCode != other.hashCode) return false;
                    if(nonTransparentScopes.Count != other.nonTransparentScopes.Count) return false;

                    foreach(DataScope scope in nonTransparentScopes) {
                        if(!other.nonTransparentScopes.Contains(scope)) return false;
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

                int numAnonm = 0;
                foreach(DataScope scope in nonTransparentScopes) {
                    if(scope.Name != null) {
                        if(builder.Length > 0) builder.Append(delim);
                        builder.Append(scope.Name);
                    } else numAnonm++;
                }

                if(numAnonm > 0) {
                    if(builder.Length > 0) builder.Append(delim);
                    builder.Append($"<#anon={numAnonm}>");
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
        public event Action<IScopedInvalidatable> OnInvalidate;

        /// <summary>
        /// Invoked by <seealso cref="InvalidateRegistrars" /> when the key's registrars are invalidated, with <see cref="DataScopeKey.LOCK" /> held.
        /// </summary>
        public event Action<IScopedInvalidatable> OnInvalidateRegistrars;
    }
}