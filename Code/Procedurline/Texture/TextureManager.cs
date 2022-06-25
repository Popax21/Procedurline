using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Manages texture creation / lifecycle / data upload / etc.
    /// </summary>
    public sealed class TextureManager : GameComponent {
        private struct TextureDataAction {
            public enum Action {
                DOWNLOAD, UPLOAD
            }

            public TaskCompletionSource<Texture2D> taskSrc;
            public CancellationToken taskToken;

            public TextureHandle texture;
            public TextureData data;
            public Action action;
        }

        private LinkedList<TextureDataAction> dataActionQueue = new LinkedList<TextureDataAction>();
        public readonly TextureScope GlobalScope;
        public readonly TextureScope UnownedScope;
        public readonly TextureScope StaticScope;

        public TextureHandle EmptyTexture { get; internal set; }
        public TextureHandle ErrorTexture { get; internal set; }
        internal readonly ConcurrentDictionary<VirtualTexture, TextureHandle> textureHandles = new ConcurrentDictionary<VirtualTexture, TextureHandle>();

        internal TextureManager(Game game) : base(game) {
            game.Components.Add(this);

            //Create texture scopes
            GlobalScope = new TextureScope("GLOBAL", null);
            UnownedScope = new TextureScope("UNOWNED", GlobalScope);
            StaticScope = new TextureScope("STATIC", GlobalScope);
        }

        protected override void Dispose(bool disposing) {
            //Dispose texture scopes
            UnownedScope?.Dispose();
            StaticScope?.Dispose();
            GlobalScope?.Dispose();
            textureHandles?.Clear();

            Game.Components.Remove(this);
            base.Dispose(disposing);
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            //Do pending texture data actions
            lock(dataActionQueue) {
                for(LinkedListNode<TextureDataAction> node = dataActionQueue.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                    TextureDataAction action = node.Value;

                    if(action.texture.IsDisposed) {
                        action.taskSrc.SetException(new ObjectDisposedException("TextureHandle"));
                    } else {
                        Texture2D tex = action.texture.Texture;
                        if(tex == null) continue;

                        //Execute the pending action
                        try {
                            action.taskToken.ThrowIfCancellationRequested();
                            switch(action.action) {
                                case TextureDataAction.Action.DOWNLOAD: action.data.DownloadData(tex); break;
                                case TextureDataAction.Action.UPLOAD: action.data.UploadData(tex); break;
                            }
                            action.taskSrc.SetResult(tex);
                        } catch(Exception e) {
                            action.taskSrc.SetException(e);
                        }
                    }
    
                    dataActionQueue.Remove(node);
                }
            }
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

        //Creates a texture from the specified texture data
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
        public Task<Texture2D> DownloadData(TextureHandle texh, TextureData data, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            Texture2D tex = texh.Texture;
            if(tex != null) {
                try {
                    data.DownloadData(tex);
                    return Task.FromResult(tex);
                } catch(Exception e) {
                    return Task.FromException<Texture2D>(e);
                }
            }

            //Enqueue the action
            TaskCompletionSource<Texture2D> taskSrc = new TaskCompletionSource<Texture2D>();
            lock(dataActionQueue) dataActionQueue.AddLast(new TextureDataAction() {
                taskSrc = taskSrc,
                taskToken = token,

                texture = texh,
                data = data,
                action = TextureDataAction.Action.DOWNLOAD
            });
            return taskSrc.Task;
        }

        /// <summary>
        /// Uploads the data from the given <see cref="TextureData"/> buffer into the specified <see cref="TextureHandle"/>'s texture.
        /// <b>NOTE: It's recommended to use <see cref="TextureHandle.SetTextureData"/> instead, as it caches the texture's data</b>
        /// </summary>
        public Task<Texture2D> UploadData(TextureHandle texh, TextureData data, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            Texture2D tex = texh.Texture;
            if(tex != null) {
                try {
                    data.UploadData(tex);
                    return Task.FromResult(tex);
                } catch(Exception e) {
                    return Task.FromException<Texture2D>(e);
                }
            }

            //Enqueue the action
            TaskCompletionSource<Texture2D> taskSrc = new TaskCompletionSource<Texture2D>();
            lock(dataActionQueue) dataActionQueue.AddLast(new TextureDataAction() {
                taskSrc = taskSrc,
                taskToken = token,

                texture = texh,
                data = data,
                action = TextureDataAction.Action.UPLOAD
            });
            return taskSrc.Task;
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
    }
}