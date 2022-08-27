using System;
using System.Diagnostics;
using System.Collections.Generic;
using MonoMod.Utils;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Handles cached texture data eviction. Procedurline caches texture data in RAM to improve performance, but will evict it if memory gets low, or a certain maximum size gets exceeded. This class handles the implementation of this mechanism.
    /// Currently, there are four ways an eviction sweep can be triggered, in which the evictor will potentially evict texture data:
    /// - once every level load / scene change
    /// - every time a texture download / upload operation is performed
    /// - every time a GC generation is triggered
    /// </summary>
    public sealed class TextureCacheEvictor : IDisposable {
        private readonly object LOCK = new object();

        private GCCallback gcCallback;
        private Process curProc;

        private LinkedList<TextureHandle> cachedTextures = new LinkedList<TextureHandle>();
        private long totalCacheSize;

        public TextureCacheEvictor() {
            lock(LOCK) {
                //Create a GC callback
                gcCallback = new GCCallback(GCCallback);

                //Obtain a handle for the current process
                curProc = Process.GetCurrentProcess();

                //Register hooks
                Everest.Events.Level.OnLoadLevel += LevelLoadHandler;
                On.Monocle.Engine.OnSceneTransition += SceneTransitionHook;
            }
        }

        public void Dispose() {
            lock(LOCK) {
                if(curProc == null) return;

                //Dispose the GC callback
                gcCallback?.Dispose();
                gcCallback = null;

                //Unregister hooks
                Everest.Events.Level.OnLoadLevel -= LevelLoadHandler;
                On.Monocle.Engine.OnSceneTransition -= SceneTransitionHook;

                //Dispose the process handle
                curProc.Dispose();
                curProc = null;
            }
        }

        /// <summary>
        /// Does an eviction sweep. If the current texture cache is too big, this causes the least recently used textures to be evicted.
        /// </summary>
        /// <returns>
        /// The size of evicted texture data, in bytes.
        /// </returns>
        public long DoSweep() => EvictInternal(false);

        /// <summary>
        /// Evicts all cached texture data. This is primarly usefull for debugging purposes.
        /// </summary>
        /// <returns>
        /// The size of evicted texture data, in bytes.
        /// </returns>
        public long EvictAll() => EvictInternal(true);

        private long EvictInternal(bool evictAll) {
            lock(LOCK) {
                if(curProc == null) throw new ObjectDisposedException("TextureCacheEvictor");

                long maxCacheSize = MaxCacheSize;
                long totalEvictedSize = 0;
                LinkedListNode<TextureHandle> curEvictionCandidate = cachedTextures.Last;
                while(curEvictionCandidate != null && (evictAll || totalCacheSize > maxCacheSize)) {
                    LinkedListNode<TextureHandle> nextCand = curEvictionCandidate.Previous;

                    //Try to evict the current candidate
                    TextureHandle tex = curEvictionCandidate.Value;
                    if(RemoveTexture(tex)) {
                        //The eviction was successfull
                        totalEvictedSize += tex.Width*tex.Height*4;
                    }

                    curEvictionCandidate = nextCand;
                }

                if(totalEvictedSize > 0) Logger.Log(LogLevel.Info, ProcedurlineModule.Name, $"Evicted {totalEvictedSize} bytes out of texture data cache [cache size {(totalCacheSize + totalEvictedSize) / 1024}kB -> {totalCacheSize / 1024}kB / max {maxCacheSize / 1024}kB]");
                return totalEvictedSize;
            }
        }

        internal bool CacheTexture(TextureHandle tex, bool force) {
            lock(LOCK) {
                if(curProc == null) throw new ObjectDisposedException("TextureCacheEvictor");
                if(tex.cacheNode != null) return true;

                //Check if the texture's cached size is below half of the maxmimum cache size
                long texSize = tex.Width*tex.Height*4;
                if(!force && texSize > MaxCacheSize/2) return false;

                //Add to texture list
                tex.cacheNode = cachedTextures.AddFirst(tex);
                totalCacheSize += texSize;

                //Do an eviction sweep
                DoSweep();
                return tex.cacheNode != null;
            }
        }

        internal bool RemoveTexture(TextureHandle tex) {
            lock(LOCK) {
                if(curProc == null) throw new ObjectDisposedException("TextureCacheEvictor");
                if(tex.cacheNode == null) return false;

                //Evict cached data
                if(!tex.EvictCachedData()) return false;

                //Remove from texture list
                cachedTextures.Remove(tex.cacheNode);
                tex.cacheNode = null;
                totalCacheSize -= tex.Width*tex.Height*4;

                return true;
            }
        }

        internal void MarkTexture(TextureHandle tex) {
            lock(LOCK) {
                if(curProc == null) throw new ObjectDisposedException("TextureCacheEvictor");
                if(tex.cacheNode == null) return;

                //Move to head of list
                cachedTextures.Remove(tex.cacheNode);
                cachedTextures.AddFirst(tex.cacheNode);

                //Do an eviction sweep
                DoSweep();
            }
        }

        private void GCCallback() => DoSweep();
        private void LevelLoadHandler(Level level, Player.IntroTypes intro, bool fromLoader) => DoSweep();
        private void SceneTransitionHook(On.Monocle.Engine.orig_OnSceneTransition orig, Monocle.Engine engine, Monocle.Scene from, Monocle.Scene to) {
            orig(engine, from, to);
            DoSweep();
        }

        public long TotalCacheSize => totalCacheSize;
        public long MaxCacheSize => Math.Min(ProcedurlineModule.Settings.MaxTextureCacheSize, Math.Max((MaxMemoryUsage - CurrentMemoryUsage) - ProcedurlineModule.Settings.MinTextureCacheMargin, 0));

        public long CurrentMemoryUsage => curProc.WorkingSet64;
        public long MaxMemoryUsage {
            get {
                long maxMemUsage = long.MaxValue;
                
                //We can do better on Windows
                if(PlatformHelper.Is(MonoMod.Utils.Platform.Windows)) maxMemUsage = (long) curProc.MaxWorkingSet;
    
                //32 bit processes limit our memory
                if(!Environment.Is64BitProcess) {
                    //On Windows, we're limited to the 4GB instead of 2GB on 64 bit machines
                    if(PlatformHelper.Is(MonoMod.Utils.Platform.Windows) && Environment.Is64BitOperatingSystem) {
                        maxMemUsage = Math.Min(maxMemUsage, 4*1024*1024*1024L);
                    } else {
                        maxMemUsage = Math.Min(maxMemUsage, 2*1024*1024*1024L);
                    }
                }

                return maxMemUsage;
            }
        }
    }
}