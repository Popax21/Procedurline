using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Provides a managed handle for textures
    /// </summary>
    public sealed class TextureHandle : TextureOwner {
        private static readonly FieldInfo VirtualTexture__Texture_QueuedLoadLock = typeof(VirtualTexture).GetField("_Texture_QueuedLoadLock", PatchUtils.BindAllInstance);

        private VirtualTexture vtex;
        private MTexture mtex;
        private bool ownsTex;

        private TextureData dataCache;
        private bool dataCacheValid = false;

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
        /// Invalidates cached texture data
        /// </summary>
        public void InvalidateCache() {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");
                dataCacheValid = false;
            }
        }

        /// <summary>
        /// Gets the texture data for the texture, downloading it if required.
        /// <b>NOTE: DATA MIGHT BE CACHED, DO NOT MODIFY THE RETURNED <see cref="TextureData" /> OBJECT!</b>
        /// </summary>
        public Task<TextureData> GetTextureData(CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            Task fetchTask;
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("TextureHandle");

                //Check for cached data
                if(dataCacheValid) return Task.FromResult(dataCache);

                //Check if a fetch task is running
                if(dataFetchTask == null) dataFetchTask = ((Func<Task>) (async () => {
                    await dataUploadSem.WaitAsync(dataCancelSrc.Token).ConfigureAwait(false);
                    try {
                        await ProcedurlineModule.TextureManager.DownloadData(this, dataCache, dataCancelSrc.Token).ConfigureAwait(false);
                        lock(LOCK) dataCacheValid = true;
                    } finally {
                        dataUploadSem.Release();
                    }
                }))();

                fetchTask = dataFetchTask;
            }

            return ((Func<Task<TextureData>>) (async () => {
                await fetchTask.OrCancelled(token);
                lock(LOCK) return dataCache;
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

                    //Set data cache
                    data.Copy(dataCache);
                    dataCacheValid = true;
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
        /// Gets the cached texture data, or null if no data is cached
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

        public int Width { get; }
        public int Height { get; }
        public VirtualTexture VirtualTexture => vtex;
        public Texture2D Texture => vtex?.Texture;
        public MTexture MTexture => mtex;
    }
}