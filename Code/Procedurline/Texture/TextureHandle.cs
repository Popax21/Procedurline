using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Provides a managed handle for textures
    /// </summary>
    public sealed class TextureHandle : TextureOwner {
        /// <summary>
        /// Represents a handle which keeps a <see cref="TextureHandle" /> forcefully pinned in the texture cache. As long as one of these handles is alive, the texture will be cached and can not be evicted from the cache.
        /// This mechanism is utilized by <see cref="GetTextureData" /> to allow invokers to copy their texture data before potentially disposing the texture data again.
        /// </summary>
        public sealed class CachePinHandle : IDisposable {
            public readonly TextureHandle Texture;
            public bool Alive { get; internal set; }
            private LinkedListNode<CachePinHandle> listNode;
            internal bool forceDisposed = false;

            internal CachePinHandle(TextureHandle tex, bool incrCount = true) {
                Texture = tex;
                Alive = true;

                //Increment the pin count
                lock(tex.LOCK) {
                    if(tex.IsDisposed) throw new ObjectDisposedException("TextureHandle");

                    listNode = tex.cachePinHandles.AddLast(this);

                    //Force-cache the texture if this is the first handle
                    if(tex.cachePinHandles.Count <= 1) {
                        if(!ProcedurlineModule.TextureManager.CacheEvictor.CacheTexture(tex, true)) Celeste.CriticalFailureHandler(new InvalidOperationException("Couldn't force-cache texture!"));
                        tex.dataCache ??= new TextureData(tex.Width, tex.Height);
                    }
                }
            }

            public void Dispose() {
                bool doEvictorSweep = false;
                lock(Texture.LOCK) {
                    if(!Alive) return;
                    Alive = false;
                    
                    Texture.cachePinHandles.Remove(listNode);
                    listNode = null;

                    //Do an eviction sweep if this was the the last handle
                    if(Texture.cachePinHandles.Count <= 0) doEvictorSweep = true;
                }

                //We have to make this call while not holding the texture lock, otherwise we might deadlock with an already ongoing eviction
                if(doEvictorSweep) ProcedurlineModule.TextureManager.CacheEvictor.DoSweep();
            }

            /// <summary>
            /// Clones the texture's data, and then disposes the <see cref="CachePinHandle" />
            /// </summary>
            public TextureData CloneDataAndDispose() {
                TextureData texData;

                lock(Texture.LOCK) {
                    if(forceDisposed) throw new InvalidOperationException("Cached texture data has been manually invalidated!");
                    if(!Alive) throw new ObjectDisposedException("TextureHandle.CachePinHandle");
                    if(!Texture.dataCacheValid) throw new InvalidOperationException("Cached texture data is invalid!");

                    texData = Texture.CachedData.Clone();
                }

                Dispose();
                return texData;
            }

            /// <summary>
            /// Copies the texture's data into another buffer, and then disposes the <see cref="CachePinHandle" />
            /// </summary>
            public void CopyDataAndDispose(TextureData dst, Rectangle? srcRect = null, Rectangle? dstRect = null) {
                lock(Texture.LOCK) {
                    if(forceDisposed) throw new InvalidOperationException("Cached texture data has been manually invalidated!");
                    if(!Alive) throw new ObjectDisposedException("TextureHandle.CachePinHandle");
                    if(!Texture.dataCacheValid) throw new InvalidOperationException("Cached texture data is invalid!");

                    Texture.CachedData.Copy(dst, srcRect, dstRect);
                }

                Dispose();
            }
        }

        private static readonly FieldInfo VirtualTexture__Texture_QueuedLoadLock = typeof(VirtualTexture).GetField("_Texture_QueuedLoadLock", PatchUtils.BindAllInstance);

        public readonly int Width;
        public readonly int Height;

        private VirtualTexture vtex;
        private MTexture mtex;
        public readonly bool OwnsTexture;

        internal LinkedListNode<TextureHandle> cacheNode;
        internal bool isPseudoCached;
        private LinkedList<CachePinHandle> cachePinHandles = new LinkedList<CachePinHandle>();

        private bool dataCacheValid = false;
        private TextureData dataCache;

        private SemaphoreSlim dataUploadSem;
        private CancellationTokenSource dataCancelSrc, dataFetchCancelSrc;
        private Task dataFetchTask = null;

        public TextureHandle(string name, TextureScope scope, int width, int height, Color col) : base(name, scope) {
            try {
                Width = width;
                Height = height;

                //Create the texture on the main thread
                MainThreadHelper.Do(() => {
                    lock(LOCK) {
                        if(IsDisposed) return;
                        vtex = VirtualContent.CreateTexture($"/PLHANDLE{Path}", width, height, col);
                        mtex = new MTexture(vtex);
                        ShortCircuitTextureLoad();

                        //Add to textures dictionary
                        lock(ProcedurlineModule.TextureManager.textureHandles) ProcedurlineModule.TextureManager.textureHandles[vtex] = this;
                    }
                });
                OwnsTexture = true;

                //Create the data cache
                dataUploadSem = new SemaphoreSlim(1, 1);
                dataCancelSrc = new CancellationTokenSource();
            } catch(Exception) {
                Dispose();
                throw;
            }
        }

        internal TextureHandle(string name, TextureScope scope, VirtualTexture tex) : base(name, scope) {
            try {
                Width = tex.Width;
                Height = tex.Height;

                vtex = tex;
                mtex = new MTexture(vtex);
                OwnsTexture = false;

                //Create the data cache
                dataUploadSem = new SemaphoreSlim(1, 1);
                dataCancelSrc = new CancellationTokenSource();

                //Add to textures dictionary
                lock(ProcedurlineModule.TextureManager.textureHandles) ProcedurlineModule.TextureManager.textureHandles[vtex] = this;
            } catch(Exception) {
                Dispose();
                throw;
            }
        }

        public override void Dispose() {
            lock(LOCK) {
                if(IsDisposed) return;
                base.Dispose();

                //Remove from cached list
                ProcedurlineModule.TextureManager.CacheEvictor.RemoveTexture(this);

                if(vtex != null) {
                    //Remove from textures dictionary
                    lock(ProcedurlineModule.TextureManager.textureHandles) ProcedurlineModule.TextureManager.textureHandles.TryRemove(vtex, out _);

                    //Dispose the texture
                    if(OwnsTexture) MainThreadHelper.Do(vtex.Dispose);
                    vtex = null;
                }

                //Dispose the data cache
                dataCache?.Dispose();
                dataCache = null;

                foreach(CachePinHandle pinHandle in cachePinHandles) pinHandle.Dispose();
                cachePinHandles.Clear();

                dataCancelSrc?.Cancel();
                dataFetchCancelSrc?.Cancel();

                dataUploadSem?.Dispose();
                dataUploadSem = null;

                dataCancelSrc?.Dispose();
                dataCancelSrc = null;

                dataFetchCancelSrc?.Dispose();
                dataFetchCancelSrc = null;
            }
        }

        /// <summary>
        /// Creates a <see cref="CachePinHandle" /> for this texture. This will force it to be cached, and not get evicted as long as a pin handle is still alive
        /// </summary>
        public CachePinHandle PinCache() => new CachePinHandle(this);

        /// <summary>
        /// Invalidates cached texture data
        /// </summary>
        public void InvalidateCache() {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");

                //Force-dispose all cache pin handles
                foreach(CachePinHandle pinHandle in cachePinHandles) {
                    pinHandle.forceDisposed = true;
                    pinHandle.Dispose();
                }
                cachePinHandles.Clear();

                //Remove from cache
                ProcedurlineModule.TextureManager.CacheEvictor.RemoveTexture(this);

                //Force the data cache to be treated like it's invalid
                dataCacheValid = false;
            }
        }

        internal bool EvictCachedData() {
            //Check if we're pinned in the cache
            if(cachePinHandles.Count > 0) return false;

            //Dispose cached data, unless there's currently a pending download
            //In which case, cancel the download
            if (dataFetchCancelSrc != null) {
                dataFetchCancelSrc.Cancel();
                dataFetchTask = null;
            } else {
                dataCache?.Dispose();
            }

            dataCache = null;
            dataCacheValid = false;
            dataFetchCancelSrc = null;

            return true;
        }

        /// <summary>
        /// Gets the texture data for the texture, downloading it if required.
        /// <b>NOTE: DATA MIGHT BE CACHED, DO NOT MODIFY THE RETURNED <see cref="TextureData" /> OBJECT!</b>
        /// </summary>
        public async Task<CachePinHandle> GetTextureData(CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            CachePinHandle pinHandle = null;
            try {
                Task fetchTask;
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("TextureHandle");

                    //Check for already cached data
                    if(dataCache != null && dataCacheValid) return PinCache();

                    //Create a cache pin handle
                    pinHandle = PinCache();

                    //Check if a fetch task is already running
                    if(dataFetchTask == null) {
                        TextureData cacheData = dataCache;
                        dataFetchTask = ((Func<Task>) (async () => {
                            await dataUploadSem.WaitAsync(dataCancelSrc.Token).ConfigureAwait(false);

                            //Mark the download has having started
                            CancellationTokenSource tokenSrc;
                            lock(LOCK) {
                                if(cacheData != dataCache) {
                                    //The invalidation happened before we set the download flag, so the data object has already been disposed of
                                    dataUploadSem.Release();
                                    return;
                                }

                                tokenSrc = dataFetchCancelSrc = new CancellationTokenSource();
                            }

                            try {
                                //Download into the cached data object
                                await ProcedurlineModule.TextureManager.CacheEvictor.RunWithOOMHandler(async () => {
                                    await ProcedurlineModule.TextureManager.DownloadData(this, dataCache, tokenSrc.Token).ConfigureAwait(false);
                                    lock(LOCK) {
                                        if(dataCache == cacheData) dataCacheValid = true;
                                    }
                                });
                            } finally {
                                //Mark the download as complete
                                lock(LOCK) {
                                    if(dataCache == cacheData) {
                                        //The cached data hasn't been invalidated since the task was started
                                        dataFetchTask = null;
                                        dataFetchCancelSrc.Dispose();
                                        dataFetchCancelSrc = null;
                                        dataUploadSem.Release();
                                    } else {
                                        //The cache has been invalidated during the download
                                        //In this case, we don't even hold the semaphore anymore
                                        cacheData.Dispose();
                                    }
                                }
                            }
                        }))();
                    }

                    fetchTask = dataFetchTask;
                }

                //Wait for the fetch task to complete
                await fetchTask.OrCancelled(token).ConfigureAwait(false);

                //Transfer ownership of the cache pin handle to the caller
                return pinHandle;
            } catch(Exception) {
                //Something went wrong
                pinHandle?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Sets and uploads the texture data for the texture
        /// </summary>
        public async Task SetTextureData(TextureData data, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            CancellationToken dataToken;
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
                dataToken = dataCancelSrc.Token;
            }

            using(CancellationTokenSource tokenSrc = new CancellationTokenSource())
            using(dataToken.Register(tokenSrc.Cancel))
            using(token.Register(tokenSrc.Cancel)) {
                await dataUploadSem.WaitAsync(dataCancelSrc.Token).ConfigureAwait(false);
            }
        
            try {
                await ProcedurlineModule.TextureManager.CacheEvictor.RunWithOOMHandler(async () => {
                    lock(LOCK) {
                        if(IsDisposed) throw new ObjectDisposedException("TextureHandle");

                        //Try to cache data
                        if(ProcedurlineModule.TextureManager.CacheEvictor.CacheTexture(this, false)) {
                            try {
                                dataCache ??= new TextureData(Width, Height);
                                data.Copy(dataCache);
                                dataCacheValid = true;
                            } catch(Exception e) {
                                Logger.Log(LogLevel.Error, ProcedurlineModule.Name, $"Error while caching uploaded texture data for '{Path}': {e}");

                                //Something went wrong
                                ProcedurlineModule.TextureManager.CacheEvictor.RemoveTexture(this);
                                dataCacheValid = false;
                                throw;
                            }
                        }
                    }

                    //Upload texture data
                    await ProcedurlineModule.TextureManager.UploadData(this, data, token).ConfigureAwait(false);
                });
            } finally {
                dataUploadSem.Release();
            }
        }

        /// <summary>
        /// Makes Everest start to load the texture, if it hasn't already been doing so before.
        /// </summary>
        public void TriggerTextureLoad() {
            MainThreadHelper.Do(() => {
                if(IsDisposed) return;
                if(IsLoading || HasLoaded) return;

                //Don't set _Texture_Requesting to load asynchronously
                VirtualTexture.Reload();
            });
        }

        /// <summary>
        /// Short circuits Everest's texture loading, and instead immediatly creates a blank texture if possible
        /// </summary>
        public void ShortCircuitTextureLoad() {
            MainThreadHelper.Do(() => {
                if(IsDisposed) return;
                if(IsLoading || HasLoaded) return;
                VirtualTexture.Texture = new Texture2D(Celeste.Instance.GraphicsDevice, Width, Height, false, SurfaceFormat.Color);
            });
        }

        /// <summary>
        /// Checks if the texture is currently being loaded by Everest
        /// </summary>
        public bool IsLoading {
            get {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("TextureHandle");

                    if(VirtualTexture__Texture_QueuedLoadLock == null) {
                        //We're in the future! Hopefully Everest's FTL / lazy texture loading code is a bit less messy now...
                        //TODO Correctly deal with the utopia
                        return false;
                    }

                    return VirtualTexture__Texture_QueuedLoadLock.GetValue(vtex) != null;
                }
            }
        }

        /// <summary>
        /// Checks if the texture has finished being loaded by Everest
        /// </summary>
        public bool HasLoaded {
            get {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
                    return !(Texture?.IsDisposed ?? true); 
                }
            }
        }

        /// <summary>
        /// Gets the cached texture data, or null if no data is cached. Note that the returned texture data can become disposed at any time if <see cref="TextureOwner.LOCK" /> isn't held.
        /// </summary>
        public TextureData CachedData {
            get {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
                    return dataCacheValid ? dataCache : null;
                }
            }
        }

        public override int NumTextures => 1;

        public VirtualTexture VirtualTexture => vtex;
        public Texture2D Texture => vtex?.Texture;
        public MTexture MTexture => mtex;
    }
}