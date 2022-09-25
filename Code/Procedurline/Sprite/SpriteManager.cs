using System;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Cil;
using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Manages sprites and their properties (like animations).
    /// Allows for users to add data processors which can transparently modify sprite animations before rendering.
    /// </summary>
    public sealed class SpriteManager : GameComponent {
        private sealed class DataProcessorWrapper : IAsyncDataProcessor<Sprite, string, Sprite.Animation> {
            public readonly SpriteManager Manager;
            public readonly SpriteAnimationDataProcessor Processor;

            public DataProcessorWrapper(SpriteManager manager) {
                Manager = manager;
                Processor = new SpriteAnimationDataProcessor(manager.DynamicAnimationProcessor, (s, k, _) => Manager.DynamicAnimationCache.GetTextureScope(s, k));
            }

            public void RegisterScopes(Sprite target, DataScopeKey key) => Processor.RegisterScopes(target, key);

            public async Task<bool> ProcessDataAsync(Sprite target, DataScopeKey key, string animId, AsyncRef<Sprite.Animation> animRef, CancellationToken token = default) {
                SpriteScopeKey skey = (SpriteScopeKey) key;
                try {
                    //Proxy to actual processor
                    Stopwatch timer = ProcedurlineModule.Settings.LogProcessingTimes ? Stopwatch.StartNew() : null;

                    bool didModify = await (ProcedurlineModule.Settings.UseThreadPool ?
                        Task.Run(() => Processor.ProcessDataAsync(target, skey, animId, animRef, token)) :
                        Processor.ProcessDataAsync(target, skey, animId, animRef, token)
                    );

                    if(timer != null) Logger.Log(ProcedurlineModule.Name, $"Finished processing sprite '{skey.SpriteID}' animation '{animId}' (took {timer.ElapsedMilliseconds}ms)");

                    return didModify;
                } catch(Exception e) {
                    token.ThrowIfCancellationRequested();
                    if(!skey.IsValid) return false;
                    Logger.Log(LogLevel.Error, ProcedurlineModule.Name, $"Error while processing sprite '{skey.SpriteID}' animation '{animId}': {e}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Contains the dynamic animation mixer processor. It is the main entry point for dynamic animation processing, and also invokes the <see cref="DynamicAnimationProcessor" /> at order 0.
        /// By adding processor to it you can "mix" the animations displayed for any sprite instance by swapping the <see cref="Sprite.Animation" /> instances with arbitrary other ones.
        /// <b>NOTE: If you mix in <see cref="CustomSpriteAnimation" /> instances, you HAVE TO register the <see cref="CustomSprite" />'s scopes by calling <see cref="CustomSprite.RegisterScopes" />. While Procedurline ensures that custom sprite processing has finished before utilizing any animation data, it DOES NOT forward custom sprite invalidation, which you HAVE TO do manually.</b>
        /// </summary>
        public readonly CompositeAsyncDataProcessor<Sprite, string, Sprite.Animation> DynamicAnimationMixer;
        public readonly CompositeAsyncDataProcessor<Sprite, string, SpriteAnimationData> DynamicAnimationProcessor;
        public readonly SpriteAnimationCache DynamicAnimationCache;
        private readonly DisposablePool hookPool;

        private readonly ConditionalWeakTable<Sprite, string> spriteIds = new ConditionalWeakTable<Sprite, string>();
        private readonly ConcurrentDictionary<Sprite, SpriteHandler> spriteHandlers = new ConcurrentDictionary<Sprite, SpriteHandler>();
        private bool debugSpriteHandlers = false;

        internal SpriteManager(Game game) : base(game) {
            game.Components.Add(this);

            //Setup dynamic animation processing and caching
            DynamicAnimationMixer = new CompositeAsyncDataProcessor<Sprite, string, Sprite.Animation>();
            DynamicAnimationProcessor = new CompositeAsyncDataProcessor<Sprite, string, SpriteAnimationData>();
            DynamicAnimationCache = new SpriteAnimationCache(new TextureScope("DYNANIMCACHE", ProcedurlineModule.TextureManager.GlobalScope), new DataProcessorWrapper(this));

            DynamicAnimationMixer.AddProcessor(0, DynamicAnimationCache);
            DynamicAnimationMixer.AddProcessor(int.MinValue, new DelegateDataProcessor<Sprite, string, Sprite.Animation>(registerScopes: RegisterSpriteScopes).WrapAsync());
            DynamicAnimationProcessor.AddProcessor(int.MinValue, new DelegateDataProcessor<Sprite, string, SpriteAnimationData>(registerScopes: (_, k) => ProcedurlineModule.DynamicScope.RegisterKey(k)).WrapAsync());

            //Install hooks
            using(new DetourContext(ProcedurlineModule.HOOK_PRIO)) {
                On.Monocle.Component.Added += ComponentAddedHook;
                On.Monocle.Component.Removed += ComponentRemovedHook;
                On.Monocle.Component.EntityAdded += ComponentEntityAddedHook;
                On.Monocle.Component.EntityRemoved += ComponentEntityRemovedHook;
                On.Monocle.Component.SceneEnd += ComponentSceneEndHook;

                On.Monocle.Image.Render += ImageRenderHook;
                On.Monocle.Sprite.CreateClone += SpriteCreateCloneHook;
                On.Monocle.Sprite.CloneInto += SpriteCloneIntoHook;
                On.Monocle.SpriteBank.Create += SpriteBankCreateHook;
                On.Monocle.SpriteBank.CreateOn += SpriteBankCreateOnHook;

                On.Monocle.Scene.Render += SceneRenderHook;
                On.Celeste.Level.Render += LevelRenderHook;

                foreach(MethodInfo m in typeof(Sprite).GetMethods(PatchUtils.BindAllInstance)) {
                    if(m.DeclaringType != typeof(Sprite)) continue;

                    bool didModify = false;
                    ILHook hook = new ILHook(m, ctx => {
                        //Replace all animation dict get accesses
                        ILCursor cursor = new ILCursor(ctx);
                        while(cursor.TryGotoNext(MoveType.Before, instr => instr.MatchCallOrCallvirt(typeof(Dictionary<string, Sprite.Animation>).GetMethod("get_Item")))) {
                            //Replace with hook
                            cursor.Remove();
                            cursor.Emit(OpCodes.Ldarg_0);
                            cursor.EmitDelegate<Func<Dictionary<string, Sprite.Animation>, string, Sprite, Sprite.Animation>>(SpriteDictGetHook);

                            didModify = true;
                        }
                    });
                    if(!didModify) {
                        hook.Dispose();
                        continue;
                    }

                    //Add hook to list
                    hookPool.Add(hook);
                }
            }
        }

        protected override void Dispose(bool disposing) {
            //Dispose sprite handlers
            lock(spriteHandlers) {
                foreach(SpriteHandler handler in spriteHandlers.Values) handler.Dispose();
                spriteHandlers.Clear();
            }

            //Cleanup animation cache and processing
            On.Monocle.Component.Added -= ComponentAddedHook;
            On.Monocle.Component.Removed -= ComponentRemovedHook;
            On.Monocle.Component.EntityAdded -= ComponentEntityAddedHook;
            On.Monocle.Component.EntityRemoved -= ComponentEntityRemovedHook;
            On.Monocle.Component.SceneEnd -= ComponentSceneEndHook;

            On.Monocle.Image.Render += ImageRenderHook;
            On.Monocle.Sprite.CreateClone -= SpriteCreateCloneHook;
            On.Monocle.Sprite.CloneInto -= SpriteCloneIntoHook;
            On.Monocle.SpriteBank.Create -= SpriteBankCreateHook;
            On.Monocle.SpriteBank.CreateOn -= SpriteBankCreateOnHook;

            On.Monocle.Scene.Render -= SceneRenderHook;
            On.Celeste.Level.Render -= LevelRenderHook;

            hookPool.Dispose();

            DynamicAnimationCache?.Dispose();
            DynamicAnimationCache.TextureScope?.Dispose();

            Game.Components.Remove(this);
            base.Dispose(disposing);
        }

        /// <summary>
        /// Registers the sprite's default scopes on the key
        /// </summary>
        public void RegisterSpriteScopes(Sprite sprite, DataScopeKey key) => RegisterSpriteScopes(sprite, key, false);

        /// <summary>
        /// Registers the sprite's default scopes on the key. Optionally does not register any custom sprite scopes.
        /// </summary>
        public void RegisterSpriteScopes(Sprite sprite, DataScopeKey key, bool noCustom) {
            ProcedurlineModule.GlobalScope.RegisterKey(key);
            ProcedurlineModule.SpriteScope.RegisterKey(key);
            if(sprite is PlayerSprite || sprite.Entity is Player) ProcedurlineModule.PlayerScope.RegisterKey(key);

            //If the sprite is a custom one, register its scopes on the key as well
            if(!noCustom && sprite is CustomSprite customSprite) customSprite.RegisterScopes(key);
        }

        /// <summary>
        /// Gets the <see cref="SpriteAnimationData" /> for the specified animation.
        /// The resulting animation data object will contain newly created <see cref="TextureData" /> objects, so it's required to call <see cref="SpriteAnimationData.Dispose" /> once you finished using it.
        /// </summary>
        public async Task<SpriteAnimationData> GetAnimationData(Sprite.Animation anim, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            if(anim is CustomSpriteAnimation customAnim) {
                //Wait for the animation to finish updating
                try {
                    await ProcessCustomAnimation(customAnim).OrCancelled(token);
                } catch(Exception e) {
                    //Custom animation processing exceptions aren't our responsibility
                    Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Encountered an error processing sprite animation data for CustomSpriteAnimation '{customAnim.AnimationID}': {e}");
                }
            }

            //Fetch the sprite animation values
            float animDelay;
            Chooser<string> animGoto;
            MTexture[] animFrames;
            anim.GetAnimationData(out animDelay, out animGoto, out animFrames);

            //Create animation data
            SpriteAnimationData animData;
            if(anim is PlayerSpriteAnimation playerAnim) {
                animData = new PlayerSpriteAnimationData() {
                    Goto = animGoto,
                    Delay = animDelay,
                    Frames = new SpriteAnimationData.AnimationFrame[animFrames.Length],
                    PlayerFrameData = playerAnim.PlayerFrameData
                };
            } else {
                animData = new SpriteAnimationData() {
                    Goto = animGoto,
                    Delay = animDelay,
                    Frames = new SpriteAnimationData.AnimationFrame[animFrames.Length]
                };
            }

            Task<TextureHandle.CachePinHandle>[] frameTasks = new Task<TextureHandle.CachePinHandle>[animFrames.Length];
            try {
                for(int i = 0; i < frameTasks.Length; i++) {
                    frameTasks[i] = ProcedurlineModule.TextureManager.GetHandle(animFrames[i].Texture).GetTextureData(token);
                }

                //Wait for frames to finish downloading
                await Task.WhenAll(frameTasks).ConfigureAwait(false);

                //Extract result frame data
                for(int i = 0; i < frameTasks.Length; i++) {
                    Rectangle clipRect = animFrames[i].ClipRect;
                    TextureData texData = new TextureData(clipRect.Width, clipRect.Height);
                    frameTasks[i].Result.CopyDataAndDispose(texData, srcRect: clipRect);

                    animData.Frames[i] = new SpriteAnimationData.AnimationFrame() {
                        TextureData = texData,
                        AtlasPath = animFrames[i].AtlasPath,
                        DrawOffset = animFrames[i].DrawOffset,
                        Width = animFrames[i].Width,
                        Height = animFrames[i].Height,
                        Scale = (animFrames[i] is ScaledMTexture scaledMTex) ? scaledMTex.Scale : Vector2.One,
                        ScaleFix = animFrames[i].ScaleFix
                    };
                }

                return animData;
            } catch(Exception) {
                for(int i = 0; i < frameTasks.Length; i++) {
                    _ = frameTasks[i].ContinueWithOrInvoke(t => {
                        if(t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
                    });
                }

                for(int i = 0; i < animData.Frames.Length; i++) {
                    animData.Frames[i].TextureData?.Dispose();
                }
                throw;
            }
        }

        /// <summary>
        /// Create a new animation from the given <see cref="SpriteAnimationData" />
        /// </summary>
        public async Task<Sprite.Animation> CreateAnimation(string name, TextureScope scope, SpriteAnimationData animData, CancellationToken token = default, AsyncRef<TextureHandle> texHandle = null) {
            token.ThrowIfCancellationRequested();

            TextureHandle heapTex = null;
            try {
                //Create texture heap from animation frames
                TextureHeap heap = new TextureHeap();
                Rectangle[] frameRects = new Rectangle[animData.Frames.Length];
                for(int i = 0; i < frameRects.Length; i++) {
                    frameRects[i] = heap.AddTexture(animData.Frames[i].TextureData);
                }

                //Create heap texture
                using(TextureData heapTexData = heap.CreateAtlasTexture()) {
                    heapTex = await ProcedurlineModule.TextureManager.CreateTexture(name, scope, heapTexData, token).ConfigureAwait(false);
                }

                //Create animation
                Sprite.Animation anim;
                
                if(animData is PlayerSpriteAnimationData playerAnimData) {
                    anim = new PlayerSpriteAnimation() {
                        Goto = animData.Goto,
                        Delay = animData.Delay,
                        Frames = new MTexture[frameRects.Length],
                        PlayerFrameData = playerAnimData.PlayerFrameData
                    };
                } else {
                    anim = new Sprite.Animation() {
                        Goto = animData.Goto,
                        Delay = animData.Delay,
                        Frames = new MTexture[frameRects.Length]
                    };
                }

                for(int i = 0; i < frameRects.Length; i++) {
                    anim.Frames[i] = new ScaledMTexture(heapTex.MTexture, animData.Frames[i].AtlasPath, frameRects[i], animData.Frames[i].DrawOffset, animData.Frames[i].Width, animData.Frames[i].Height) {
                        Scale = animData.Frames[i].Scale,
                        ScaleFix = animData.Frames[i].ScaleFix
                    };
                }

                if(texHandle != null) texHandle.Data = heapTex;
                return anim;
            } catch(Exception) {
                heapTex?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Processes the given custom animation data, ensuring it's running on the right thread and the engine is blocked if required. This should be used by functions which could potentially start custom sprite animation processing....
        /// </summary>
        public Task ProcessCustomAnimation(CustomSpriteAnimation anim) {
            Task processTask;
            if(ProcedurlineModule.Settings.UseThreadPool) {
                processTask = Task.Run(() => anim.ProcessData());
            } else {
                processTask = anim.ProcessData();
            }

            if(!ProcedurlineModule.Settings.AsynchronousStaticProcessing) {
                ProcedurlineModule.GlobalManager.BlockEngineOnTask(processTask);
            }

            return processTask;
        }

        /// <summary>
        /// Returns the sprite's unique identifier.
        /// </summary>
        /// <returns>
        /// <c>null</c> if the sprite doesn't have an unique ID
        /// </returns>
        public string GetSpriteID(Sprite sprite) {
            //If the sprite is a custom sprite, return its ID
            if(sprite is CustomSprite customSprite) return customSprite.SpriteID;

            //Check sprite ID table
            if(spriteIds.TryGetValue(sprite, out string spriteId)) return spriteId;

            //Fallback to path
            return sprite.Path;
        }

        /// <summary>
        /// Creates a <see cref="SpriteHandler" /> for the sprite. You are resposible for properly disposing it using <see cref="SpriteHandler.Dispose" /> once the sprite's not used anymore.
        /// This method should be used for sprites which Procedurline wouldn't pick up as active by itself.
        /// </summary>
        /// <returns>
        /// <c>null</c> if the sprite can't have / already has an associated sprite handler
        /// </returns>
        public SpriteHandler CreateSpriteHandler(Sprite sprite) {
            string spriteId = GetSpriteID(sprite);
            if(spriteId == null) return null;

            lock(spriteHandlers) {
                if(spriteHandlers.ContainsKey(sprite)) return null;
                SpriteHandler handler = new SpriteHandler(sprite, spriteId, false);
                spriteHandlers.TryAdd(sprite, handler);
                return handler;
            }
        }

        /// <summary>
        /// Returns the sprite's <see cref="SpriteHandler" /> wrapper, which is responsible for all sprite modifications/processing.
        /// </summary>
        /// <returns>
        /// <c>null</c> if the sprite doesn't have an associated sprite handler
        /// </returns>
        public SpriteHandler GetSpriteHandler(Sprite sprite) {
            lock(spriteHandlers) {
                return spriteHandlers.TryGetValue(sprite, out SpriteHandler handler) ? handler : null;
            }
        }

        private void AddSpriteRef(Sprite sprite) {
            lock(spriteHandlers) {
                if(!spriteHandlers.TryGetValue(sprite, out SpriteHandler handler)) {
                    //Get the sprite's ID
                    string spriteId = GetSpriteID(sprite);
                    if(spriteId == null) return;

                    //Create a sprite handler
                    spriteHandlers.TryAdd(sprite, handler = new SpriteHandler(sprite, spriteId, true));
                }
                if(handler.OwnedByManager) handler.numManagerRefs++;
            }
        }

        private void RemoveSpriteRef(Sprite sprite) {
            lock(spriteHandlers) {
                if(!spriteHandlers.TryGetValue(sprite, out SpriteHandler handler)) return;
                if(handler.OwnedByManager && --handler.numManagerRefs <= 0) {
                    handler.Dispose();
                    spriteHandlers.TryRemove(sprite, out _);
                }
            }
        }

        private void ComponentAddedHook(On.Monocle.Component.orig_Added orig, Component comp, Entity entity) {
            orig(comp, entity);

            if(comp is Sprite sprite) {
                if(entity.Scene == null) return;
                AddSpriteRef(sprite);
            }
        }

        private void ComponentRemovedHook(On.Monocle.Component.orig_Removed orig, Component comp, Entity entity) {
            if(comp is Sprite sprite) {
                if(entity.Scene == null) return;
                RemoveSpriteRef(sprite);
            }

            orig(comp, entity);
        }

        private void ComponentEntityAddedHook(On.Monocle.Component.orig_EntityAdded orig, Component comp, Scene scene) {
            orig(comp, scene);
            if(comp is Sprite sprite) AddSpriteRef(sprite);
        }

        private void ComponentEntityRemovedHook(On.Monocle.Component.orig_EntityRemoved orig, Component comp, Scene scene) {
            if(comp is Sprite sprite) RemoveSpriteRef(sprite);
            orig(comp, scene);
        }

        private void ComponentSceneEndHook(On.Monocle.Component.orig_SceneEnd orig, Component comp, Scene scene) {
            if(comp is Sprite sprite) RemoveSpriteRef(sprite);
            orig(comp, scene);
        }

        private void ImageRenderHook(On.Monocle.Image.orig_Render orig, Image img) {
            if(img is Sprite sprite && GetSpriteHandler(sprite) is SpriteHandler handler) {
                //If there is a queued cache reset, execute it
                lock(handler.LOCK) {
                    if(handler.queueReload) {
                        sprite.ReloadAnimation();
                        handler.queueReload = false;
                    }
                }
            }

            orig(img);
        }

        private Sprite SpriteCreateCloneHook(On.Monocle.Sprite.orig_CreateClone orig, Sprite sprite) {
            if(sprite is CustomSprite customSprite) return customSprite.CreateCopy();
            return orig(sprite);
        }

        private Sprite SpriteCloneIntoHook(On.Monocle.Sprite.orig_CloneInto orig, Sprite sprite, Sprite target) {
            //Check for custom sprites, unless this is an unsafe clone
            if(SpriteUtils.UnsafeCloneSource != sprite || SpriteUtils.UnsafeCloneTarget != target) {
                if(sprite is CustomSprite) throw new ArgumentException($"Can't use Sprite.CloneInto on a custom sprite of type '{sprite.GetType()}'!");
                if(target is CustomSprite) throw new ArgumentException($"Can't use Sprite.CloneInto targeting a custom sprite of type '{target.GetType()}'!");
            } else SpriteUtils.UnsafeCloneSource = SpriteUtils.UnsafeCloneTarget = null;

            Sprite clone = orig(sprite, target);
            spriteIds.Remove(clone);
            if(GetSpriteID(sprite) is string spriteId) spriteIds.Add(clone, spriteId);
            GetSpriteHandler(clone)?.ResetCache();
            return clone;
        }

        private Sprite SpriteBankCreateHook(On.Monocle.SpriteBank.orig_Create orig, SpriteBank bank, string id) {
            Sprite sprt = orig(bank, id);
            spriteIds.Remove(sprt);
            spriteIds.Add(sprt, id);
            return sprt;
        }

        private Sprite SpriteBankCreateOnHook(On.Monocle.SpriteBank.orig_CreateOn orig, SpriteBank bank, Sprite sprite, string id) {
            //Check for custom sprites
            if(sprite is CustomSprite) throw new ArgumentException($"Can't use SpriteBank.CreateOn on a custom sprite of type '{sprite.GetType()}'!");

            Sprite sprt = orig(bank, sprite, id);
            spriteIds.Remove(sprt);
            spriteIds.Add(sprt, id);
            GetSpriteHandler(sprt)?.ResetCache();
            return sprt;
        }

        private Sprite.Animation SpriteDictGetHook(Dictionary<string, Sprite.Animation> dict, string id, Sprite sprite) {
            //Forward to the sprite handler
            SpriteHandler handler = GetSpriteHandler(sprite);
            if(handler == null) return dict[id];
            return handler.GetProcessedAnimation(id) ?? throw new KeyNotFoundException($"Animation '{id}' not found!");
        }

        private void DrawSpriteHandlerDebug(Scene scene, Matrix mat) {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone, null, Celeste.ScreenMatrix);

            //Do layout and draw passes
            Dictionary<SpriteHandler, Rectangle> rects = null;
            bool isLayout = true;
            for(int i = 0; i < 10; i++) {
                if(i == 9) isLayout = false; //Force the last pass to be the draw pass

                bool didChange = false;
                Dictionary<SpriteHandler, Rectangle> nrects = new Dictionary<SpriteHandler, Rectangle>();
                foreach(Entity entity in scene.Entities) {
                    foreach(Component comp in entity.Components) {
                        if(!(comp is Sprite sprite)) continue;
                        SpriteHandler handler = GetSpriteHandler(sprite);
                        if(handler == null) continue;
                        didChange |= handler.DrawDebug(scene, mat, rects, nrects, isLayout);
                    }
                }
                rects = nrects;

                if(!isLayout) break;
                if(!didChange) isLayout = false; //If nothing changed, skip remaining layout passes
            }

            Draw.SpriteBatch.End();
        }

        private void SceneRenderHook(On.Monocle.Scene.orig_Render orig, Scene scene) {
            orig(scene);
            if(debugSpriteHandlers) DrawSpriteHandlerDebug(scene, Matrix.Identity);
        }

        private void LevelRenderHook(On.Celeste.Level.orig_Render orig, Level level) {
            orig(level);
            if(debugSpriteHandlers) DrawSpriteHandlerDebug(level, level.Camera.Matrix * Matrix.CreateScale(6));
        }

        [Command("pl_dbgsprites", "Enable/Disable debug rendering of Procedurline sprite handlers")]
        private static void DBGSPRITES() {
            ProcedurlineModule.SpriteManager.debugSpriteHandlers = !ProcedurlineModule.SpriteManager.debugSpriteHandlers;
        }
    }
}