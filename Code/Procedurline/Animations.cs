using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Celeste;
using Monocle;

using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {
    public class AnimationManager : GameComponent {
        private List<AnimationFilter> filters = new List<AnimationFilter>();
        private List<(MTexture, TextureData)> textureUploadList = new List<(MTexture, TextureData)>();

        private On.Monocle.Component.hook_Removed removedHook;
        private List<ILHook> ilHooks = new List<ILHook>();

        internal AnimationManager() : base(Engine.Instance) {
            //Add removed hook
            On.Monocle.Component.Removed += (removedHook = (On.Monocle.Component.orig_Removed orig, Component comp, Entity entity) => {
                orig(comp, entity);
                if(!(comp is Sprite sprite)) return;

                //Dispose filtered textures
                DynData<Sprite> dynSprite = new DynData<Sprite>(sprite);
                Dictionary<string, Sprite.Animation> anims = dynSprite.Get<Dictionary<string, Sprite.Animation>>("animations");
                HashSet<string> filteredAnims = dynSprite.Get<HashSet<string>>("filteredAnims");
                if(filteredAnims == null) return;
                foreach(Sprite.Animation anim in filteredAnims.Select(a => anims[a])) {
                    if(anim != null) foreach(MTexture frame in anim.Frames) frame.Texture.Dispose();
                }
            });

            //Add hooks for animation dict accesses
            foreach(MethodInfo m in typeof(Sprite).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                if(m.DeclaringType != typeof(Sprite)) continue;
                
                ilHooks.Add(new ILHook(m, ctx => {
                    //Replace all get accesses
                    {
                        ILCursor cursor = new ILCursor(ctx);
                        while(cursor.TryGotoNext(MoveType.Before, instr => instr.MatchCallvirt(typeof(Dictionary<string, Sprite.Animation>).GetMethod("get_Item")))) {
                            //Replace with delegate
                            cursor.Remove();
                            cursor.Emit(OpCodes.Ldarg_0);
                            cursor.EmitDelegate<Func<Dictionary<string, Sprite.Animation>, string, Sprite, Sprite.Animation>>((dict, animId, sprite) => {
                                //Check if the filtered version is in the sprite cache
                                DynData<Sprite> dynSprite = new DynData<Sprite>(sprite);
                                Dictionary<string, Sprite.Animation> cachedAnims = dynSprite.Get<Dictionary<string, Sprite.Animation>>("cachedAnimations");
                                if(cachedAnims?.TryGetValue(animId, out Sprite.Animation cachedAnim) ?? false) return cachedAnim ?? dict[animId];

                                //Filter animation
                                Sprite.Animation anim = dict[animId];
                                if(cachedAnims == null) dynSprite.Set("cachedAnimations", cachedAnims = new Dictionary<string, Sprite.Animation>(StringComparer.OrdinalIgnoreCase));
                                Sprite.Animation fAnim = cachedAnims[animId] = FilterAnimation(sprite, animId, anim);
                                return fAnim ?? anim;
                            });
                        }
                    }
                }));
            }
        }

        protected override void Dispose(bool disposing) {
            //Remove hooks
            if(removedHook != null) On.Monocle.Component.Removed -= removedHook;
            removedHook = null;

            foreach(ILHook h in ilHooks) h.Dispose();
            ilHooks.Clear();
        }

        public override void Update(GameTime gameTime) {
            //Upload textures
            if(textureUploadList.Count > 0) {
                List<(MTexture texture, TextureData data)> l = textureUploadList;
                textureUploadList = new List<(MTexture, TextureData)>();
                foreach(var tex in l) UploadTexture(tex.texture, tex.data);
            }
        }

        public void AddFilter(AnimationFilter filter, bool clearCache = true) {
            filters.Add(filter);
            if(clearCache) ClearFilterCache();
        }

        public void RemoveFilter(AnimationFilter filter, bool clearCache = true) {
            filters.Remove(filter);
            if(clearCache) ClearFilterCache();
        }
        
        public void ClearFilterCache(string animId = null) {
            //Reset sprites
            if(Engine.Scene == null) return;
            foreach(Entity e in Engine.Scene.Entities) {
                foreach(Sprite s in e.Components.Where(c => c is PlayerSprite)) {
                    //Clear cache
                    DynData<Sprite> dynSprite = new DynData<Sprite>(s);

                    Dictionary<string, Sprite.Animation> cachedAnims = dynSprite.Get<Dictionary<string, Sprite.Animation>>("cachedAnimations");
                    if(cachedAnims == null) continue;

                    if(animId == null) {
                        foreach(Sprite.Animation anim in cachedAnims.Values) {
                            if(anim != null) foreach(MTexture tex in anim.Frames) tex.Texture.Dispose();
                        }
                        cachedAnims.Clear();
                    } else {
                        if(cachedAnims.TryGetValue(animId, out Sprite.Animation anim) && anim != null) {
                            foreach(MTexture tex in anim.Frames) tex.Texture.Dispose();
                        }
                        cachedAnims.Remove(animId);
                    }

                    if(s.CurrentAnimationID != null && s.CurrentAnimationID.Length > 0 && (animId == null || s.CurrentAnimationID == animId)) {
                        //Reset animation
                        int f = s.CurrentAnimationFrame;
                        s.Play(s.CurrentAnimationID, true, false);
                        s.SetAnimationFrame(f);
                    }
                }
            }
        }

        private Sprite.Animation FilterAnimation(Sprite sprite, string animId, Sprite.Animation animation) {
            //Check if any filters even apply to this sprite
            if(!filters.Any(f => f.TargetSelector(sprite))) return null;

            //Create texture heap
            TextureHeap heap = new TextureHeap();

            //Filter each frame
            Rectangle[] fRects = new Rectangle[animation.Frames.Length];
            for(int i = 0; i < animation.Frames.Length; i++) {
                MTexture oTex = animation.Frames[i];

                //Get texture data
                TextureData texData = oTex.GetTextureData();
                
                //Apply filters
                foreach(AnimationFilter filter in filters) {
                    if(filter.TargetSelector(sprite)) texData = filter.Apply(sprite, animId, i, texData);
                }

                //Add texture to heap
                fRects[i] = heap.AddTexture(texData);
            }

            //Create heap texture
            TextureData hTexData = heap.CreateHeapTexture();
            MTexture hTex = new MTexture(VirtualContent.CreateTexture($"PCDLE@filterHeap<{sprite.GetHashCode()}:{animId}>", hTexData.Width, hTexData.Height, Color.White));
            UploadTexture(hTex, hTexData);

            //Create frame texture
            MTexture[] fTexs = new MTexture[animation.Frames.Length];
            for(int i = 0; i < animation.Frames.Length; i++) {
                MTexture oTex = animation.Frames[i];
                if(DebugFilterHeaps) fTexs[i] = new MTexture(hTex, oTex.AtlasPath, new Rectangle(0, 0, hTex.Width, hTex.Height), oTex.DrawOffset + new Vector2((float) -hTex.Width / 2, (float) oTex.Height / 2 - hTex.Height), hTex.Width, hTex.Height);
                else fTexs[i] = new MTexture(hTex, oTex.AtlasPath, fRects[i], oTex.DrawOffset, oTex.Width, oTex.Height);
            }

            return new Sprite.Animation() {
                Frames = fTexs,
                Delay = animation.Delay,
                Goto = animation.Goto
            };
        }

        private void UploadTexture(MTexture texture, TextureData data) {
            if(texture.Texture.Texture_Safe == null) {
                //Queue for later upload
                textureUploadList.Add((texture, data));
            } else texture.Texture.Texture_Safe.SetData<Color>(data.Pixels);
        }

        public bool DebugFilterHeaps { get; set; } = false;

        [Command("filterheaps", "Toggles if sprite filter heaps are drawn")]
        private static void DebugToggleFilterHeaps() {
            if(Module.AnimationManager == null) return;
            Module.AnimationManager.DebugFilterHeaps = !Module.AnimationManager.DebugFilterHeaps;
            Module.AnimationManager.ClearFilterCache();
        }
    }

    public abstract class AnimationFilter {
        public AnimationFilter(TargetSelector<Sprite> sel) => TargetSelector = sel;
        public TargetSelector<Sprite> TargetSelector { get; }
        public abstract TextureData Apply(Sprite sprite, string animationId, int frame, TextureData texture);
    }
}