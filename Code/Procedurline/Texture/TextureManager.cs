using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Manages texture creation / lifecycle / data download and upload / etc.
    /// </summary>
    public sealed class TextureManager : GameComponent {
        public readonly TextureScope GlobalScope;
        public readonly TextureScope UnownedScope;
        public readonly TextureScope DerivedScope;

        public TextureHandle EmptyTexture { get; internal set; }
        public TextureHandle ErrorTexture { get; internal set; }
        internal readonly ConcurrentDictionary<VirtualTexture, TextureHandle> textureHandles = new ConcurrentDictionary<VirtualTexture, TextureHandle>();

        public readonly TextureCacheEvictor CacheEvictor;

        internal TextureManager(Game game) : base(game) {
            game.Components.Add(this);

            //Create the cache evictor
            CacheEvictor = new TextureCacheEvictor();

            //Create texture scopes
            GlobalScope = new TextureScope("GLOBAL", null);
            UnownedScope = new TextureScope("UNOWNED", GlobalScope);
            DerivedScope = new TextureScope("DERIVED", GlobalScope);

            //Install hooks
            using(ProcedurlineModule.HOOK_CONTEXT.Use()) {
                ScaledMTexture.AddHooks();
            }
        }

        protected override void Dispose(bool disposing) {
            //Remove hooks
            ScaledMTexture.RemoveHooks();

            //Dispose texture scopes
            UnownedScope?.Dispose();
            DerivedScope?.Dispose();
            GlobalScope?.Dispose();
            textureHandles?.Clear();

            //Dispose the cache evictor
            CacheEvictor.Dispose();

            Game.Components.Remove(this);
            base.Dispose(disposing);
        }

        /// <summary>
        /// Gets the <see cref="TextureHandle" /> for a <see cref="VirtualTexture" />
        /// </summary>
        public TextureHandle GetHandle(VirtualTexture tex) {
            lock(textureHandles) {
                if(textureHandles.TryGetValue(tex, out TextureHandle handle)) return handle;
                return new TextureHandle(tex.Name, UnownedScope, tex);
            }
        }

        /// <summary>
        /// Creates a texture from the specified texture data
        /// </summary>
        public async Task<TextureHandle> CreateTexture(string name, TextureScope scope, TextureData data, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            TextureHandle tex = new TextureHandle(name, scope, data.Width, data.Height, Color.Transparent);
            await tex.SetTextureData(data, token).ConfigureAwait(false);
            return tex;
        }

        /// <summary>
        /// Downloads the data from the given <see cref="TextureHandle"/>'s texture into the specified <see cref="TextureData"/> buffer.
        /// <b>NOTE: It's recommended to use <see cref="TextureHandle.GetTextureData"/> instead, as it caches the texture's data</b>
        /// </summary>
        public async Task DownloadData(TextureHandle texh, TextureData data, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            //Trigger a texture load
            texh.TriggerTextureLoad();

            //Wait for an opportunity to execute the download
            while(!(await ProcedurlineModule.GlobalManager.MainThreadTaskFactory.StartNew(() => {
                Texture2D tex = texh.Texture;
                if(tex == null || texh.IsLoading) return false;
                data.DownloadData(tex);
                return true;
            }, GlobalManager.ForceQueue)));
        }

        /// <summary>
        /// Uploads the data from the given <see cref="TextureData"/> buffer into the specified <see cref="TextureHandle"/>'s texture.
        /// <b>NOTE: It's recommended to use <see cref="TextureHandle.SetTextureData"/> instead, as it caches the texture's data</b>
        /// </summary>
        public async Task UploadData(TextureHandle texh, TextureData data, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            //Short circuit texture loading if possible
            texh.ShortCircuitTextureLoad();

            //Wait for an opportunity to execute the upload
            while(!(await ProcedurlineModule.GlobalManager.MainThreadTaskFactory.StartNew(() => {
                Texture2D tex = texh.Texture;
                if(tex == null || texh.IsLoading) return false;
                data.UploadData(tex);
                return true;
            }, GlobalManager.ForceQueue)));
        }

        [Command("pl_texscopes", "Displays all Procedurline texture scopes")]
        private static void TEXSCOPES() {
            DumpScope(ProcedurlineModule.TextureManager.GlobalScope, 0);
        }

        private static void DumpScope(TextureScope scope, int lvl) {
            Celeste.Commands.Log($"{new string(' ', 4*lvl)}- {scope.Name} ({scope.NumTextures} textures)");
            foreach(TextureOwner o in scope) {
                if(o is TextureScope s) DumpScope(s, lvl+1);
            }
        }

        [Command("pl_dumptexs", "Displays all textures in the specified Procedurline texture scope")]
        private static void DUMPTEXS(string path) {
            path ??= "/GLOBAL";
            if(path.StartsWith("/")) path = path.Substring(1);
            if(path.StartsWith("GLOBAL")) path = path.Substring(6);
            if(path.StartsWith("/")) path = path.Substring(1);
            path += "/";

            TextureScope scope = ProcedurlineModule.TextureManager.GlobalScope;
            while(!string.IsNullOrEmpty(path)) {
                TextureScope nscope = scope?.FirstOrDefault(s => path.StartsWith(s.Name + "/", StringComparison.OrdinalIgnoreCase)) as TextureScope;
                if(nscope != null) {
                    scope = nscope;
                    path = path.Substring(scope.Name.Length+1);
                } else if(path[0] == '/') {
                    path = path.Substring(1);
                } else {
                    scope = null;
                    break;
                }
            }

            if(scope != null) {
                Celeste.Commands.Log($"{scope}");
                foreach(TextureOwner owner in scope) Celeste.Commands.Log($"  - {owner}");
            } else Celeste.Commands.Log("Couldn't find texture scope!");
        }

        [Command("pl_texcache", "Displays information about the Procedurline texture cache")]
        private static void TEXCACHE() {
            string FormatBytes(long bytes) => $"{bytes}B / {bytes / 1024}kB / {bytes / (1024*1024)}mB";
            Celeste.Commands.Log($"Cached Textures: {ProcedurlineModule.TextureManager.CacheEvictor.NumCachedTextures}");
            Celeste.Commands.Log($"Total size: {FormatBytes(ProcedurlineModule.TextureManager.CacheEvictor.TotalCacheSize)}");
            Celeste.Commands.Log($"Maximum size: {FormatBytes(ProcedurlineModule.TextureManager.CacheEvictor.MaxCacheSize)}");
            Celeste.Commands.Log($"Current memory usage: {FormatBytes(ProcedurlineModule.TextureManager.CacheEvictor.CurrentMemoryUsage)}");
            Celeste.Commands.Log($"Maximum memory usage: {FormatBytes(ProcedurlineModule.TextureManager.CacheEvictor.MaxMemoryUsage)}");
        }

        [Command("pl_evicttex", "Evicts all non-pinned data from the Procedurline texture cache")]
        private static void EVICTTEX() {
            long totalEvictedSize = ProcedurlineModule.TextureManager.CacheEvictor.EvictAll();
            Celeste.Commands.Log($"Evicted {totalEvictedSize} bytes [{totalEvictedSize / 1024}kB]");
        }
    }
}