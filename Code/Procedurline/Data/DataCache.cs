using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Caches data per scope configuration.
    /// It allows for queries for per-target data which can be shared between targets belonging to the same set of scopes (= having identical keys)
    /// The cache additionally keeps track of scope validity, and automatically disposed the scoped data if the key it belongs to gets invalidated.
    /// </summary>
    public abstract class DataCache<T, D> : IDisposable where D : class, IDisposable {
        public readonly object LOCK = new object();
        public readonly IScopeRegistrar<T> ScopeRegistrar;

        private ConcurrentDictionary<DataScopeKey, D> cache = new ConcurrentDictionary<DataScopeKey, D>();

        /// <param name="registrar">
        /// If <c>null</c> is provided, cast <c>this</c> to <see cref="IScopeRegistrar{T}" /> to allow child classes to implement it themselves
        /// </param>
        public DataCache(IScopeRegistrar<T> registrar) {
            ScopeRegistrar = registrar ?? ((IScopeRegistrar<T>) this);
        }

        public void Dispose() {
            lock(LOCK) {
                //Dispose shared data and cache keys
                if(cache != null) {
                    foreach(DataScopeKey key in cache.Keys) key.Dispose();
                    cache = null;
                }
            }
        }

        /// <summary>
        /// Checks if the cache has cached data for the given key
        /// </summary>
        public bool Contains(DataScopeKey key) {
            lock(key.LOCK) lock(LOCK) {
                return cache.ContainsKey(key);
            }
        }

        /// <summary>
        /// Retrieves the associated scoped data for the given target.
        /// This generates a temporary cache key for the target and tries to look up scoped data in the cache.
        /// If there is no scoped data cached for the target's key, new data is created and kept alive until its key gets invalidated, unless <paramref name="noCreateNew" /> is <c>true</c>.
        /// </summary>
        /// <returns>
        /// <c>null</c> if the target shouldn't have any associated scoped data.
        /// </returns>
        public D GetScopedData(T target, DataScopeKey tkey = null, bool noCreateNew = false) {
            DataScopeKey key = CreateKey(target);
            if(key == null) return null;

            try {
                lock(key.LOCK) {
                    //Register the target's scopes on the cache key
                    retry:;
    
                    if(!tkey?.IsValid ?? false) throw new ArgumentException("Target key can't be invalid!");
                    tkey?.RegisterScopes(key);

                    ScopeRegistrar.RegisterScopes(target, key);

                    lock(key.VALIDITY_LOCK) {
                        if(!key.IsValid) {
                            //This could happen if the key was registered on a scope which was then invalidated
                            key.Reset();
                            goto retry;
                        }

                        //Get or create the corresponding scoped data
                        lock(LOCK) {
                            if(cache == null) throw new ObjectDisposedException("DataCache");

                            if(noCreateNew) return cache.TryGetValue(key, out D data) ? data : null;

                            return cache.GetOrAdd(key, _ => {
                                D data = CreateScopedData(key);
                                key.TakeOwnership(data);
                                key.OnInvalidate += KeyInvalidated;
                                key = null; //Don't dispose the key
                                return data;
                            });
                        }
                    }
                }
            } finally {
                key?.Dispose();
            }
        }

        private void KeyInvalidated(IScopedInvalidatable inval) {
            DataScopeKey key = (DataScopeKey) inval;

            //Remove the key's scoped data from the cache
            lock(LOCK) cache.TryRemove(key, out _);

            key.Dispose();
        }

        /// <summary>
        /// Creates a new key for use with the scope registrar.
        /// Override this method if you want to use your own key class.
        /// </summary>
        /// <returns>
        /// <c>null</c> if the target shouldn't have any associated scoped data
        /// </returns>
        protected virtual DataScopeKey CreateKey(T target) => new DataScopeKey(false);

        /// <summary>
        /// Creates new scoped data for a specified cache key
        /// </summary>
        protected abstract D CreateScopedData(DataScopeKey key);

        public bool IsDisposed {
            get {
                lock(LOCK) return cache == null;
            }
        }
    }

    /// <summary>
    /// Implements a <see cref="DataCache{T, D}" /> which caches the processed data of a <see cref="IDataProcessor{T, I, D}" />.
    /// For it to work, it is <b>REQUIRED</b> that the given processor behaves correctly in terms of target scopes and ID invariance.
    /// </summary>
    public class DataProcessorCache<T, I, D> : DataCache<T, DataProcessorCache<T, I, D>.ScopedCache>, IDataProcessor<T, I, D> {
        public class ScopedCache : IDisposable, IDataProcessor<T, I, D> {
            public readonly object LOCK = new object();
            public readonly DataProcessorCache<T, I, D> Cache;
            public readonly DataScopeKey Key;
    
            private volatile Dictionary<I, Tuple<bool, D>> dataCache;

            public ScopedCache(DataProcessorCache<T, I, D> cache, DataScopeKey key) {
                Cache = cache;
                Key = key;
                dataCache = new Dictionary<I, Tuple<bool, D>>(cache.IDComparer);

                key.OnInvalidate += _ => OnInvalidate?.Invoke(this);
                key.OnInvalidateRegistrars += _ => OnInvalidateRegistrars?.Invoke(this);
            }

            /// <summary>
            /// WARNING: This is called with the cache key's <see
            /// cref="DataScope.LOCK"/> held - you must not attempt to acquire the cache's <see cref="LOCK"/>! 
            /// </summary>
            public virtual void Dispose() {
                dataCache = null;
            }

            public void RegisterScopes(T target, DataScopeKey key) {
                lock(LOCK) {
                    if(dataCache == null) throw new ObjectDisposedException("DataProcessorCache.ScopedCache");
                    Cache.RegisterScopes(target, key);
                }
            }

            public virtual bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
                lock(LOCK) {
                    Dictionary<I, Tuple<bool, D>> cache = dataCache;
                    if(cache == null) throw new ObjectDisposedException("DataProcessorCache.ScopedCache");

                    //Check the key
                    if(Key != key) throw new ArgumentException("Given scope key doesn't match cache key!");

                    //Check the cache
                    if(cache.TryGetValue(id, out Tuple<bool, D> cachedData)) {
                        if(cachedData.Item1) data = cachedData.Item2;
                        return cachedData.Item1;
                    }

                    //Run the processor
                    bool didModify = Cache.Processor.ProcessData(target, key, id, ref data);

                    //Store result in cache
                    if(didModify) dataCache.Add(id, new Tuple<bool, D>(true, data));
                    else dataCache.Add(id, new Tuple<bool, D>(false, default));

                    return didModify;
                }
            }

            public bool IsDisposed => dataCache == null;

            public int NumCached {
                get {
                    Dictionary<I, Tuple<bool, D>> cache = dataCache;
                    if(cache == null) return 0;
                    else return cache.Count;
                }
            }

            public event Action<ScopedCache> OnInvalidate;
            public event Action<ScopedCache> OnInvalidateRegistrars;
        }

        public readonly IDataProcessor<T, I, D> Processor;
        public readonly IEqualityComparer<I> IDComparer;

        public DataProcessorCache(IDataProcessor<T, I, D> processor, IEqualityComparer<I> idComparer = null) : base(null) {
            Processor = processor;
            IDComparer = idComparer ?? EqualityComparer<I>.Default;
        }

        public virtual void RegisterScopes(T target, DataScopeKey key) {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("DataProcessorCache");
                Processor.RegisterScopes(target, key);
            }
        }

        public virtual bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
            //Get the target's scoped cache
            retry:;
            ScopedCache scache = GetScopedData(target, key);
            if(scache == null) return false;

            lock(scache.LOCK) {
                if(scache.IsDisposed) goto retry; //Prevent race conditions

                //Let the scoped cache process the data
                return scache.ProcessData(target, scache.Key, id, ref data);
            }
        }

        protected override ScopedCache CreateScopedData(DataScopeKey key) => new ScopedCache(this, key);
    }

    /// <summary>
    /// Implements a <see cref="DataCache{T, D}" /> which caches the processed data of an <see cref="IAsyncDataProcessor{T, I, D}" />.
    /// For it to work, it is <b>REQUIRED</b> that the given processor behaves correctly in terms of target scope and ID invariance.
    /// </summary>
    public class AsyncDataProcessorCache<T, I, D> : DataCache<T, AsyncDataProcessorCache<T, I, D>.ScopedCache>, IAsyncDataProcessor<T, I, D> {
        public class ScopedCache : IDisposable, IAsyncDataProcessor<T, I, D> {
            public readonly object LOCK = new object();
            public readonly AsyncDataProcessorCache<T, I, D> Cache;
            public readonly DataScopeKey Key;
    
            private Dictionary<I, Task<Tuple<bool, D>>> dataCache;
            private CancellationTokenSource taskCancelSrc;

            protected internal ScopedCache(AsyncDataProcessorCache<T, I, D> cache, DataScopeKey key) {
                Cache = cache;
                Key = key;
                dataCache = new Dictionary<I, Task<Tuple<bool, D>>>(cache.IDComparer);
                taskCancelSrc = new CancellationTokenSource();

                key.OnInvalidate += _ => OnInvalidate?.Invoke(this);
                key.OnInvalidateRegistrars += _ => OnInvalidateRegistrars?.Invoke(this);
            }

            public virtual void Dispose() {
                lock(LOCK) {
                    if(dataCache != null) {
                        //Cancel tasks
                        taskCancelSrc?.Cancel();
                        taskCancelSrc?.Dispose();

                        dataCache = null;
                    }
                }
            }

            public void RegisterScopes(T target, DataScopeKey key) {
                lock(LOCK) {
                    if(dataCache == null) throw new ObjectDisposedException("DataProcessorCache.ScopedCache");
                    Cache.RegisterScopes(target, key);
                }
            }

            public virtual Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default) {
                lock(LOCK) {
                    if(dataCache == null) throw new ObjectDisposedException("DataProcessorCache.ScopedCache");

                    //Check the key
                    if(Key != key) throw new ArgumentException("Given scope key doesn't match cache key!");

                    //Check the cache
                    if(!dataCache.TryGetValue(id, out Task<Tuple<bool, D>> cacheTask)) {
                        //Start a new processor task
                        async Task<Tuple<bool, D>> WrapProcessorTask() {
                            AsyncRef<D> cref = new AsyncRef<D>(data);
                            bool didModify = await Cache.Processor.ProcessDataAsync(target, key, id, cref, taskCancelSrc.Token);

                            if(didModify) return new Tuple<bool, D>(true, cref.Data);
                            else return new Tuple<bool, D>(false, default);
                        }

                        dataCache.Add(id, cacheTask = WrapProcessorTask());
                    }

                    //Return wrapped task
                    async Task<bool> WrapCacheTask() {
                        Tuple<bool, D> cachedData = await cacheTask.OrCancelled(token);
                        if(cachedData.Item1) data.Data = cachedData.Item2;
                        return cachedData.Item1;
                    }
                    return WrapCacheTask();
                }
            }

            public bool IsDisposed => dataCache == null;

            public int NumCached {
                get {
                    lock(LOCK) {
                        if(dataCache == null) return 0;
                        else return dataCache.Count;
                    }
                }
            }

            public int NumPending {
                get {
                    lock(LOCK) {
                        if(dataCache == null) return 0;
                        else return dataCache.Count(kv => !kv.Value.IsCompleted);
                    }
                }
            }

            public event Action<ScopedCache> OnInvalidate;
            public event Action<ScopedCache> OnInvalidateRegistrars;
        }

        public readonly IAsyncDataProcessor<T, I, D> Processor;
        public readonly IEqualityComparer<I> IDComparer;

        public AsyncDataProcessorCache(IAsyncDataProcessor<T, I, D> processor, IEqualityComparer<I> idComparer = null) : base(null) {
            Processor = processor;
            IDComparer = idComparer ?? EqualityComparer<I>.Default;
        }

        public virtual void RegisterScopes(T target, DataScopeKey key) {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("DataProcessorCache");
                Processor.RegisterScopes(target, key);
            }
        }

        public virtual Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default) {
            //Get the target's scoped cache
            retry:;
            token.ThrowIfCancellationRequested();
            ScopedCache scache = GetScopedData(target, key);
            if(scache == null) return Task.FromResult(false);

            lock(scache.LOCK) {
                if(scache.IsDisposed) goto retry; //Prevent race conditions

                //Let the scoped cache process the data
                return scache.ProcessDataAsync(target, scache.Key, id, data);
            }
        }

        protected override ScopedCache CreateScopedData(DataScopeKey key) => new ScopedCache(this, key);
    }
}