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

        private long totalCacheSize, effCacheSize;
        private LinkedList<TextureHandle> cachedTextures = new LinkedList<TextureHandle>(), pseudoCachedTextures = new LinkedList<TextureHandle>();

        private volatile bool inEvictSweep;
        private LinkedList<TextureHandle> evictionBacklog = new LinkedList<TextureHandle>();

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

                inEvictSweep = true;
                try {
                    //If there are any pseudo-cached textures, try to evict them
                    if(pseudoCachedTextures.Count > 0) {
                        for(LinkedListNode<TextureHandle> node = pseudoCachedTextures.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                            RemoveTexture(node.Value, false);
                        }
                    }

                    //Evict if cache is over maxmimum size
                    long maxCacheSize = MaxCacheSize;
                    long totalEvictedSize = 0;
                    LinkedListNode<TextureHandle> curEvictionCandidate = cachedTextures.Last;
                    while(curEvictionCandidate != null && (evictAll || effCacheSize > maxCacheSize)) {
                        LinkedListNode<TextureHandle> nextCand = curEvictionCandidate.Previous;

                        //Try to evict the current candidate
                        TextureHandle tex = curEvictionCandidate.Value;
                        if(RemoveTexture(tex, false)) {
                            //The eviction was successfull
                            totalEvictedSize += tex.Width*tex.Height*4;
                        }

                        curEvictionCandidate = nextCand;
                    }

                    if(totalEvictedSize > 0) Logger.Log(LogLevel.Info, ProcedurlineModule.Name, $"Evicted {totalEvictedSize} bytes out of texture data cache [effective cache size {(effCacheSize + totalEvictedSize) / 1024}kB -> {effCacheSize / 1024}kB / max {maxCacheSize / 1024}kB]");
                    return totalEvictedSize;
                } finally {
                    //Clear backlog
                    inEvictSweep = false;

                    lock(evictionBacklog) {
                        foreach(TextureHandle tex in evictionBacklog) RemoveTexture(tex, false);
                        evictionBacklog.Clear();
                    }
                }
            }
        }

        internal bool CacheTexture(TextureHandle tex, bool force) {
            //The caller already holds tex.LOCK
            //Because of this, we only try to lock the evictor's LOCK when we know we aren't in the cache list
            //As such, we can't deadlock with EvictInternal, as it only tries to lock the texture lock when we ARE in the cache list
            if(tex.cacheNode != null) return true;

            lock(LOCK) {
                if(curProc == null) throw new ObjectDisposedException("TextureCacheEvictor");

                //Check if the texture's cached size is below the maxmimum cache size
                long texSize = tex.Width*tex.Height*4;  
                if(texSize > MaxCacheSize) {
                    tex.isPseudoCached = true;
                    if(!force) return false;
                } else tex.isPseudoCached = false;

                //Add to texture list
                tex.cacheNode = (tex.isPseudoCached ? pseudoCachedTextures : cachedTextures).AddFirst(tex);
                totalCacheSize += texSize;
                if(!tex.isPseudoCached) effCacheSize += texSize;

                //Do an eviction sweep
                DoSweep();
                return tex.cacheNode != null;
            }
        }

        internal bool RemoveTexture(TextureHandle tex, bool fromTex=true) {
            if(fromTex && inEvictSweep) {
                //The texture asked that it be removed by itself, while there currently is an eviction sweep
                //As such, we already hold tex.LOCK, and could deadlock with EvictInternal when it tries to remove the texture as well
                //So simply add this texture to a list of to-be-removed textures, and abort early
                if(tex.cacheNode == null) return false;
                lock(evictionBacklog) evictionBacklog.AddLast(tex);
                return false;
            }

            lock(LOCK) {
                if(curProc == null) throw new ObjectDisposedException("TextureCacheEvictor");

                lock(tex.LOCK) {
                    if(tex.cacheNode == null) return false;

                    //Evict cached data, abort if it's pinned
                    if(!tex.EvictCachedData()) return false;

                    //Remove from texture list
                    long texSize = tex.Width*tex.Height*4;
                    (tex.isPseudoCached ? pseudoCachedTextures : cachedTextures).Remove(tex.cacheNode);
                    tex.cacheNode = null;
                    totalCacheSize -= texSize;
                    if(!tex.isPseudoCached) effCacheSize -= texSize;

                    return true;
                }
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

        private void GCCallback() => ProcedurlineModule.GlobalManager.MainThreadTaskFactory.StartNew(DoSweep, GlobalManager.ForceQueue);
        private void LevelLoadHandler(Level level, Player.IntroTypes intro, bool fromLoader) => DoSweep();
        private void SceneTransitionHook(On.Monocle.Engine.orig_OnSceneTransition orig, Monocle.Engine engine, Monocle.Scene from, Monocle.Scene to) {
            orig(engine, from, to);
            DoSweep();
        }

        public int NumCachedTextures { get { lock(LOCK) return cachedTextures.Count; } }
        public long TotalCacheSize { get { lock(LOCK) return totalCacheSize; } }
        public long MaxCacheSize => Math.Min(ProcedurlineModule.Settings.MaxTextureCacheSize, Math.Max((MaxMemoryUsage - CurrentMemoryUsage) - ProcedurlineModule.Settings.MinTextureCacheMargin, 0));

        public long CurrentMemoryUsage => curProc.WorkingSet64;
        public long MaxMemoryUsage {
            get {
                long maxMemUsage = (long) (Everest.SystemMemoryMB*1024*1024);

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