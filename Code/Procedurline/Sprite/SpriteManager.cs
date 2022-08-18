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
        private sealed class AnimationCacheProcessor : IAsyncDataProcessor<Sprite, string, Sprite.Animation> {
            public readonly SpriteManager Manager;

            public AnimationCacheProcessor(SpriteManager manager) => Manager = manager;

            public void RegisterScopes(Sprite target, DataScopeKey key) {
                Manager.AnimationProcessor.RegisterScopes(target, key);
            }

            public Task<bool> ProcessDataAsync(Sprite sprite, DataScopeKey key, string animId, AsyncRef<Sprite.Animation> animRef, CancellationToken token = default) {
                SpriteAnimationCache.ScopedCache scache = (SpriteAnimationCache.ScopedCache) Manager.AnimationCache.GetScopedData(key);
                if(ProcedurlineModule.Settings.UseThreadPool) return Task.Run(() => ProcessAnimation(sprite, scache, animId, animRef, token));
                else return ProcessAnimation(sprite, scache, animId, animRef, token);
            }

            private async Task<bool> ProcessAnimation(Sprite sprite, SpriteAnimationCache.ScopedCache scache, string animId, AsyncRef<Sprite.Animation> animRef, CancellationToken token = default) {
                token.ThrowIfCancellationRequested();

                try {
                    Stopwatch timer = new Stopwatch();
                    timer.Start();

                    //Get sprite animation data
                    using(SpriteAnimationData animData = (animRef.Data != null) ? await Manager.GetAnimationData(animRef, token) : null) {
                        //Run processor
                        AsyncRef<SpriteAnimationData> procAnimData = new AsyncRef<SpriteAnimationData>(animData);
                        if(!await Manager.AnimationProcessor.ProcessDataAsync(sprite, scache.Key, animId, procAnimData, token)) {
                            //Optimize by returning the original animation
                            return false;
                        }

                        //Create new animation
                        animRef.Data = await Manager.CreateAnimation(animId, scache.TextureScope, procAnimData, token);

                        if(ProcedurlineModule.Settings.LogProcessingTimes) {
                            Logger.Log(ProcedurlineModule.Name, $"Done processing sprite '{scache.Key.SpriteID}' animation '{animId}' (took {timer.ElapsedMilliseconds}ms)");
                        }

                        return true;
                    }
                } catch(Exception e) {
                    token.ThrowIfCancellationRequested();
                    if(!scache.Key.IsValid) return false;
                    Logger.Log(LogLevel.Error, ProcedurlineModule.Name, $"Error while processing sprite '{scache.Key.SpriteID}' animation '{animId}': {e}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Contains the animation mixer processor. It is the main entry point for dynamic animation processing, and also invokes the <see cref="AnimationProcessor" /> at order 0.
        /// By adding processor to it you can "mix" the animations displayed for any sprite instance by swapping the <see cref="Sprite.Animation" /> instances with arbitrary other ones.
        /// <b>NOTE: If you mix in <see cref="CustomSpriteAnimation" /> instances, you HAVE TO register the <see cref="CustomSprite" />'s scopes by calling <see cref="CustomSprite.RegisterScopes" />. While Procedurline ensures that custom sprite processing has finished before utilizing any animation data, it DOES NOT forward custom sprite invalidation, which you HAVE TO do manually.</b>
        /// </summary>
        public readonly CompositeAsyncDataProcessor<Sprite, string, Sprite.Animation> AnimationMixer;
        public readonly CompositeAsyncDataProcessor<Sprite, string, SpriteAnimationData> AnimationProcessor;
        public readonly SpriteAnimationCache AnimationCache;
        private readonly List<ILHook> animationHooks = new List<ILHook>();

        private readonly ConditionalWeakTable<Sprite, string> spriteIds = new ConditionalWeakTable<Sprite, string>();
        private readonly ConcurrentDictionary<Sprite, ProcessedSprite> processedSprites = new ConcurrentDictionary<Sprite, ProcessedSprite>();
        private bool debugProcessedSprites = false;

        internal SpriteManager(Game game) : base(game) {
            game.Components.Add(this);

            //Setup animation processing and caching
            AnimationMixer = new CompositeAsyncDataProcessor<Sprite, string, Sprite.Animation>();
            AnimationProcessor = new CompositeAsyncDataProcessor<Sprite, string, SpriteAnimationData>();
            AnimationCache = new SpriteAnimationCache(new TextureScope("ANIMCACHE", ProcedurlineModule.TextureManager.GlobalScope), new AnimationCacheProcessor(this));
            AnimationMixer.AddProcessor(0, AnimationCache);

            //Register default scope registrar
            AnimationMixer.AddProcessor(int.MinValue, new DelegateDataProcessor<Sprite, string, Sprite.Animation>(registerScopes: RegisterDefaultScopes).WrapAsync());

            //Install hooks
            using(new DetourContext(ProcedurlineModule.HOOK_PRIO)) {
                On.Monocle.Component.Added += ComponentAddedHook;
                On.Monocle.Component.Removed += ComponentRemovedHook;
                On.Monocle.Component.EntityAdded += ComponentEntityAddedHook;
                On.Monocle.Component.EntityRemoved += ComponentEntityRemovedHook;
                On.Monocle.Component.SceneEnd += ComponentSceneEndHook;

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
                    animationHooks.Add(hook);
                }
            }
        }

        protected override void Dispose(bool disposing) {
            //Dispose processed sprites
            lock(processedSprites) {
                foreach(ProcessedSprite psprite in processedSprites.Values) psprite.Dispose();
                processedSprites.Clear();
            }

            //Cleanup animation cache and processing
            On.Monocle.Component.Added -= ComponentAddedHook;
            On.Monocle.Component.Removed -= ComponentRemovedHook;
            On.Monocle.Component.EntityAdded -= ComponentEntityAddedHook;
            On.Monocle.Component.EntityRemoved -= ComponentEntityRemovedHook;
            On.Monocle.Component.SceneEnd -= ComponentSceneEndHook;

            On.Monocle.Sprite.CreateClone -= SpriteCreateCloneHook;
            On.Monocle.Sprite.CloneInto -= SpriteCloneIntoHook;
            On.Monocle.SpriteBank.Create -= SpriteBankCreateHook;
            On.Monocle.SpriteBank.CreateOn -= SpriteBankCreateOnHook;

            On.Monocle.Scene.Render -= SceneRenderHook;
            On.Celeste.Level.Render -= LevelRenderHook;

            foreach(ILHook h in animationHooks) h.Dispose();
            animationHooks.Clear();

            AnimationCache?.Dispose();
            AnimationCache.TextureScope?.Dispose();

            Game.Components.Remove(this);
            base.Dispose(disposing);
        }

        /// <summary>
        /// Gets the <see cref="SpriteAnimationData" /> for the specified animation.
        /// The resulting animation data object will contain newly created <see cref="TextureData" /> objects, so it's required to call <see cref="SpriteAnimationData.Dispose" /> once you finished using it.
        /// </summary>
        public async Task<SpriteAnimationData> GetAnimationData(Sprite.Animation anim, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            //Fetch the sprite animation values
            float animDelay;
            Chooser<string> animGoto;
            MTexture[] animFrames;

            if(anim is CustomSpriteAnimation customAnim) {
                //Wait for the animation to finish updating
                try {
                    await customAnim.UpdateData().OrCancelled(token);
                } catch(Exception e) {
                    //Static animation processing exceptions aren't our responsibility
                    Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Trying to get sprite animation data for failed processed static sprite animation: {e}");
                }

                //We have to get the animation's data on the main thread
                TaskCompletionSource<Tuple<float, Chooser<string>, MTexture[]>> tcs = new TaskCompletionSource<Tuple<float, Chooser<string>, MTexture[]>>();
                MainThreadHelper.Do(() => tcs.SetResult(new Tuple<float, Chooser<string>, MTexture[]>(customAnim.Delay, customAnim.Goto, customAnim.Frames)));

                Tuple<float, Chooser<string>, MTexture[]> data = await tcs.Task;
                animDelay = data.Item1;
                animGoto = data.Item2;
                animFrames = data.Item3;
            } else {
                animDelay = anim.Delay;
                animGoto = anim.Goto;
                animFrames = anim.Frames;
            }

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

            try {
                Task<TextureData>[] frameTasks = new Task<TextureData>[animFrames.Length];
                for(int i = 0; i < frameTasks.Length; i++) {
                    frameTasks[i] = ProcedurlineModule.TextureManager.GetHandle(animFrames[i].Texture).GetTextureData(token);
                }

                //Wait for frames to finish downloading
                await Task.WhenAll(frameTasks).ConfigureAwait(false);

                //Extract result frame data
                for(int i = 0; i < frameTasks.Length; i++) {
                    Rectangle clipRect = animFrames[i].ClipRect;
                    TextureData texData = new TextureData(clipRect.Width, clipRect.Height);
                    frameTasks[i].Result.Copy(texData, srcRect: clipRect);

                    animData.Frames[i] = new SpriteAnimationData.AnimationFrame() {
                        TextureData = texData,
                        AtlasPath = animFrames[i].AtlasPath,
                        DrawOffset = animFrames[i].DrawOffset,
                        Width = animFrames[i].Width,
                        Height = animFrames[i].Height
                    };
                }

                return animData;
            } catch(Exception) {
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
                    anim.Frames[i] = new MTexture(heapTex.MTexture, animData.Frames[i].AtlasPath, frameRects[i], animData.Frames[i].DrawOffset, animData.Frames[i].Width, animData.Frames[i].Height);
                }

                if(texHandle != null) texHandle.Data = heapTex;
                return anim;
            } catch(Exception) {
                heapTex?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Processes the given animation data. Note that you should almost never be calling this yourself, instead use <see cref="GetProcessedAnimation" />
        /// </summary>
        public async Task<Sprite.Animation> ProcessAnimation(Sprite sprite, string animId, Sprite.Animation anim, CancellationToken token = default) {
            AsyncRef<Sprite.Animation> animRef = new AsyncRef<Sprite.Animation>(anim);
            if(!await AnimationMixer.ProcessDataAsync(sprite, null, animId, animRef, token)) return anim;
            return animRef.Data;
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
        /// Creates a <see cref="ProcessedSprite" /> for the sprite. You are resposible for properly disposing it using <see cref="ProcessedSprite.Dispose" /> once the sprite's not used anymore.
        /// This method should be used for sprites which Procedurline wouldn't pick up as active by itself.
        /// </summary>
        /// <returns>
        /// <c>null</c> if the sprite can't have / already has an associated processed sprite
        /// </returns>
        public ProcessedSprite CreateProcessedSprite(Sprite sprite) {
            string spriteId = GetSpriteID(sprite);
            if(spriteId == null) return null;

            lock(processedSprites) {
                if(processedSprites.ContainsKey(sprite)) return null;
                ProcessedSprite psprite = new ProcessedSprite(sprite, spriteId, false);
                processedSprites.TryAdd(sprite, psprite);
                return psprite;
            }
        }

        /// <summary>
        /// Returns the sprite's <see cref="ProcessedSprite" /> wrapper, which is responsible for all sprite modifications/processing.
        /// </summary>
        /// <returns>
        /// <c>null</c> if the sprite doesn't have an associated processed sprite
        /// </returns>
        public ProcessedSprite GetProcessedSprite(Sprite sprite) {
            lock(processedSprites) {
                return processedSprites.TryGetValue(sprite, out ProcessedSprite psprite) ? psprite : null;
            }
        }

        /// <summary>
        /// Gets the dynamically processed version of a sprite's animation, or the original animation if it hasn't been processed yet. This simply proxies to <see cref="ProcessedSprite.GetAnimation" />
        /// </summary>
        public Sprite.Animation GetProcessedAnimation(Sprite sprite, string animId) => GetProcessedSprite(sprite)?.GetAnimation(animId) ?? (sprite.Animations.TryGetValue(animId, out Sprite.Animation anim) ? anim : null);

        private void RegisterDefaultScopes(Sprite sprite, DataScopeKey key) {
            ProcedurlineModule.GlobalScope.RegisterKey(key);
            if(sprite is PlayerSprite || sprite.Entity is Player) ProcedurlineModule.PlayerScope.RegisterKey(key);

            //If the sprite is a custom one, register its scopes on the key as well
            if(sprite is CustomSprite customSprite) customSprite.RegisterScopes(key);
        }

        private void AddSpriteRef(Sprite sprite) {
            lock(processedSprites) {
                if(!processedSprites.TryGetValue(sprite, out ProcessedSprite psprite)) {
                    //Get the sprite's ID
                    string spriteId = GetSpriteID(sprite);
                    if(spriteId == null) return;

                    //Create a processed sprite
                    processedSprites.TryAdd(sprite, psprite = new ProcessedSprite(sprite, spriteId, true));
                }
                if(psprite.OwnedByManager) psprite.numReferences++;
            }
        }

        private void RemoveSpriteRef(Sprite sprite) {
            lock(processedSprites) {
                if(!processedSprites.TryGetValue(sprite, out ProcessedSprite psprite)) return;
                if(psprite.OwnedByManager && --psprite.numReferences <= 0) {
                    psprite.Dispose();
                    processedSprites.TryRemove(sprite, out _);
                }
            }
        }

        private void ComponentAddedHook(On.Monocle.Component.orig_Added orig, Component comp, Entity entity) {
            orig(comp, entity);
            if(!(comp is Sprite sprite)) return;

            if(entity.Scene == null) return;
            AddSpriteRef(sprite);
        }

        private void ComponentRemovedHook(On.Monocle.Component.orig_Removed orig, Component comp, Entity entity) {
            orig(comp, entity);
            if(!(comp is Sprite sprite)) return;

            if(entity.Scene == null) return;
            RemoveSpriteRef(sprite);
        }

        private void ComponentEntityAddedHook(On.Monocle.Component.orig_EntityAdded orig, Component comp, Scene scene) {
            orig(comp, scene);
            if(!(comp is Sprite sprite)) return;
            AddSpriteRef(sprite);
        }

        private void ComponentEntityRemovedHook(On.Monocle.Component.orig_EntityRemoved orig, Component comp, Scene scene) {
            orig(comp, scene);
            if(!(comp is Sprite sprite)) return;
            RemoveSpriteRef(sprite);
        }

        private void ComponentSceneEndHook(On.Monocle.Component.orig_SceneEnd orig, Component comp, Scene scene) {
            orig(comp, scene);
            if(!(comp is Sprite sprite)) return;
            RemoveSpriteRef(sprite);
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
            GetProcessedSprite(clone)?.ResetCache();
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
            GetProcessedSprite(sprt)?.ResetCache();
            return sprt;
        }

        private Sprite.Animation SpriteDictGetHook(Dictionary<string, Sprite.Animation> dict, string id, Sprite sprite) {
            //Forward to ProcessedSprite
            ProcessedSprite psprite = GetProcessedSprite(sprite);
            if(psprite == null) return dict[id];
            return psprite.GetAnimation(id) ?? throw new KeyNotFoundException($"Animation '{id}' not found!");
        }

        private void DrawProcessedSpriteDebug(Scene scene, Matrix mat) {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone, null, Celeste.ScreenMatrix);

            //Do layout and draw passes
            Dictionary<ProcessedSprite, Rectangle> rects = null;
            bool isLayout = true;
            for(int i = 0; i < 10; i++) {
                if(i == 9) isLayout = false; //Force the last pass to be the draw pass

                bool didChange = false;
                Dictionary<ProcessedSprite, Rectangle> nrects = new Dictionary<ProcessedSprite, Rectangle>();
                foreach(Entity entity in scene.Entities) {
                    foreach(Component comp in entity.Components) {
                        if(!(comp is Sprite sprite)) continue;
                        ProcessedSprite psprite = GetProcessedSprite(sprite);
                        if(psprite == null) continue;
                        didChange |= psprite.DrawDebug(scene, mat, rects, nrects, isLayout);
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
            if(debugProcessedSprites) DrawProcessedSpriteDebug(scene, Matrix.Identity);
        }

        private void LevelRenderHook(On.Celeste.Level.orig_Render orig, Level level) {
            orig(level);
            if(debugProcessedSprites) DrawProcessedSpriteDebug(level, level.Camera.Matrix * Matrix.CreateScale(6));
        }

        [Command("pl_dbgprcsprts", "Enable/Disable debug rendering of Procedurline processed sprites")]
        private static void DBGPRCSPRTS() {
            ProcedurlineModule.SpriteManager.debugProcessedSprites = !ProcedurlineModule.SpriteManager.debugProcessedSprites;
        }
    }
}