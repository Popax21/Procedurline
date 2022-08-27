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
        /// <b>NOTE: Cache pin handles are NOT threading safe!</b> They have to be only used by one thread at a time, or external locking is required.
        /// </summary>
        public sealed class CachePinHandle : IDisposable {
            public readonly TextureHandle Texture;
            public bool Alive { get; private set; }

            internal CachePinHandle(TextureHandle tex, bool incrCount = true) {
                Texture = tex;
                Alive = true;

                //Increment the pin count if requested to do so
                if(incrCount) Texture.IncrementCachePinCount();
            }

            public void Dispose() {
                lock(Texture.LOCK) {
                    if(!Alive) return;
                    Alive = false;

                    //Decrement the pin count, and do an eviction sweep if it reached zero
                    if(--Texture.cachePinCount <= 0) ProcedurlineModule.TextureManager.CacheEvictor.DoSweep();
                }
            }

            /// <summary>
            /// Clones the texture's data, and then disposes the <see cref="CachePinHandle" />
            /// </summary>
            public TextureData CloneDataAndDispose() {
                if(!Alive) throw new ObjectDisposedException("TextureHandle.CachePinHandle");
                TextureData texData = Texture.CachedData.Clone();
                Dispose();
                return texData;
            }

            /// <summary>
            /// Copies the texture's data into another buffer, and then disposes the <see cref="CachePinHandle" />
            /// </summary>
            public void CopyDataAndDispose(TextureData dst, Rectangle? srcRect = null, Rectangle? dstRect = null) {
                if(!Alive) throw new ObjectDisposedException("TextureHandle.CachePinHandle");
                Texture.CachedData.Copy(dst, srcRect, dstRect);
                Dispose();
            }
        }

        private static readonly FieldInfo VirtualTexture__Texture_QueuedLoadLock = typeof(VirtualTexture).GetField("_Texture_QueuedLoadLock", PatchUtils.BindAllInstance);

        public readonly int Width;
        public readonly int Height;

        private VirtualTexture vtex;
        private MTexture mtex;
        private bool ownsTex;

        internal int cachePinCount;
        internal LinkedListNode<TextureHandle> cacheNode;
        internal TextureData dataCache;

        private SemaphoreSlim dataUploadSem;
        private CancellationTokenSource dataCancelSrc;
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

                        //Add to textures dictionary
                        lock(ProcedurlineModule.TextureManager.textureHandles) ProcedurlineModule.TextureManager.textureHandles[vtex] = this;
                    }
                });
                ownsTex = true;

                //Create the data cache
                dataCache = new TextureData(width, height);
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
                ownsTex = false;

                //Create the data cache
                dataCache = new TextureData(Width, Height);
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
                base.Dispose();

                //Remove from cached list
                ProcedurlineModule.TextureManager.CacheEvictor.RemoveTexture(this);

                if(vtex != null) {
                    //Remove from textures dictionary
                    lock(ProcedurlineModule.TextureManager.textureHandles) ProcedurlineModule.TextureManager.textureHandles.TryRemove(vtex, out _);

                    //Dispose the texture
                    if(ownsTex) MainThreadHelper.Do(vtex.Dispose);
                    vtex = null;
                }

                //Dispose the data cache
                dataCache?.Dispose();
                dataCache = null;

                dataCancelSrc?.Cancel();
                dataUploadSem?.Dispose();
                dataUploadSem = null;

                dataCancelSrc?.Dispose();
                dataCancelSrc = null;
            }
        }

        /// <summary>
        /// Creates a <see cref="CachePinHandle" /> for this texture. This will force it to be cached, and not get evicted as long as a pin handle is still alive
        /// </summary>
        public CachePinHandle PinCache() => new CachePinHandle(this);

        private void IncrementCachePinCount() {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
                if(cachePinCount++ <= 0) {
                    //Force-cache the texture
                    if(!ProcedurlineModule.TextureManager.CacheEvictor.CacheTexture(this, true)) Celeste.CriticalFailureHandler(new InvalidOperationException("Couldn't force-cache texture!"));
                    dataCache ??= new TextureData(Width, Height);
                }
            }
        }

        /// <summary>
        /// Invalidates cached texture data
        /// </summary>
        public void InvalidateCache() {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
                ProcedurlineModule.TextureManager.CacheEvictor.RemoveTexture(this);
            }
        }

        internal bool EvictCachedData() {
            //FIXME This could deadlock!
            lock(LOCK) {
                //Check if we're pinned in the cache
                if(cachePinCount > 0) return false;

                //Dispose cached data
                dataCache?.Dispose();
                dataCache = null;

                return true;
            }
        }

        /// <summary>
        /// Gets the texture data for the texture, downloading it if required.
        /// <b>NOTE: DATA MIGHT BE CACHED, DO NOT MODIFY THE RETURNED <see cref="TextureData" /> OBJECT!</b>
        /// </summary>
        public Task<CachePinHandle> GetTextureData(CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            Task fetchTask;
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");

                //Check for already cached data
                if(dataCache != null) return Task.FromResult<CachePinHandle>(PinCache());

                //Increment cache pin count
                IncrementCachePinCount();

                //Check if a fetch task is already running
                if(dataFetchTask == null) dataFetchTask = ((Func<Task>) (async () => {
                    await dataUploadSem.WaitAsync(dataCancelSrc.Token).ConfigureAwait(false);
                    try {
                        await ProcedurlineModule.TextureManager.DownloadData(this, dataCache, dataCancelSrc.Token).ConfigureAwait(false);
                        lock(LOCK) {
                            dataFetchTask = null;
                        }
                    } finally {
                        dataUploadSem.Release();
                    }
                }))();

                fetchTask = dataFetchTask;
            }

            return ((Func<Task<CachePinHandle>>) (async () => {
                await fetchTask.OrCancelled(token).ConfigureAwait(false);
                return new CachePinHandle(this, false);
            }))();
        }

        /// <summary>
        /// Sets and uploads the texture data for the texture
        /// </summary>
        public async Task SetTextureData(TextureData data, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
            }

            await dataUploadSem.WaitAsync(dataCancelSrc.Token).ConfigureAwait(false);
            try {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("TextureHandle");

                    //Try to cache data
                    if(ProcedurlineModule.TextureManager.CacheEvictor.CacheTexture(this, false)) {
                        dataCache ??= new TextureData(Width, Height);
                        data.Copy(dataCache);
                    }
                }

                //Upload texture data
                await ProcedurlineModule.TextureManager.UploadData(this, data, token).ConfigureAwait(false);
            } finally {
                dataUploadSem.Release();
            }
        }

        /// <summary>
        /// Triggers Everest to load the texture.
        /// </summary>
        public void TriggerTextureLoad() {
            if(VirtualTexture__Texture_QueuedLoadLock == null) {
                //We're in the future! Hopefully Everest's FTL / lazy texture loading code is a bit less messy now...
                //TODO Correctly deal with the utopia
                return;
            }

            MainThreadHelper.Do(() => {
                //Check if the texture has already loaded
                if((!Texture?.IsDisposed) ?? false) return;

                //Check if a load has already been triggered
                if(VirtualTexture__Texture_QueuedLoadLock.GetValue(VirtualTexture) != null) return;

                //Reload the texture to trigger a load, don't set _Texture_Requesting to do so asynchronously
                VirtualTexture.Reload();
            });
        }

        /// <summary>
        /// Gets the cached texture data, or null if no data is cached. Note that the returned texture data can become disposed at any time if <see cref="TextureOwner.LOCK" /> isn't held.
        /// </summary>
        public TextureData CachedData {
            get {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
                    return dataCache;
                }
            }
        }

        public override int NumTextures => 1;

        public VirtualTexture VirtualTexture => vtex;
        public Texture2D Texture => vtex?.Texture;
        public MTexture MTexture => mtex;
    }
}