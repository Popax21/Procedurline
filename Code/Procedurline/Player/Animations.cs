using System;
using System.Collections.Generic;
using Monocle;
using MonoMod.Utils;

namespace Celeste.Mod.Procedurline {
    public class PlayerAnimationManager : IDisposable {
        private Dictionary<string, PlayerAnimMetadata> metadata = new Dictionary<string, PlayerAnimMetadata>(), globMetadata;
        private Dictionary<string, Sprite.Animation> wildcardAnimations = new Dictionary<string, Sprite.Animation>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<PlayerSpriteMode, Dictionary<string, Sprite.Animation>> animations = new Dictionary<PlayerSpriteMode, Dictionary<string, Sprite.Animation>>();

        private On.Monocle.Component.hook_Added addedHook;
        private On.Celeste.PlayerSprite.hook_ClearFramesMetadata clearMetadataHook;

        internal PlayerAnimationManager() {
            //Get global frame metadata
            globMetadata = new DynData<PlayerSprite>().Get<Dictionary<string, PlayerAnimMetadata>>("FrameMetadata");

            //Add added hook
            On.Monocle.Component.Added += (addedHook = (On.Monocle.Component.orig_Added orig, Component comp, Entity entity) => {
                orig(comp, entity);
                if(!(comp is PlayerSprite sprite)) return;

                //Add player animations
                Dictionary<string, Sprite.Animation> anims = new DynData<PlayerSprite>(sprite).Get<Dictionary<string, Sprite.Animation>>("animations");
                foreach(var anim in wildcardAnimations) anims.TryAdd(anim.Key, anim.Value);
                if(animations.TryGetValue(sprite.Mode, out var modeAnims)) {
                    foreach(var anim in modeAnims) anims.TryAdd(anim.Key, anim.Value);
                }
            });

            //Add "clear metadata" hook
            On.Celeste.PlayerSprite.ClearFramesMetadata += (clearMetadataHook = (On.Celeste.PlayerSprite.orig_ClearFramesMetadata orig) => {
                orig();

                //Reinsert custom metadata
                globMetadata.AddRange(metadata);
            });
        }

        public void Dispose() {
            //Remove hooks
            if(addedHook != null) On.Monocle.Component.Added -= addedHook;
            addedHook = null;

            if(clearMetadataHook != null) On.Celeste.PlayerSprite.ClearFramesMetadata -= clearMetadataHook;
            clearMetadataHook = null;
        }

        public void AddAnimation(PlayerSpriteMode? mode, string id, string path, float delay, Chooser<string> into) {
            AddAnimation(mode, id, new Sprite.Animation() {
                Frames = GFX.Game.GetAtlasSubtextures(path).ToArray(),
                Delay = delay, 
                Goto = into
            });
        }

        public void AddAnimation(PlayerSpriteMode? mode, string id, Sprite.Animation anim) {
            //Iterate over all frames
            foreach(MTexture t in anim.Frames) {
                //Load metadata
                PlayerAnimMetadata meta;
                if(!t.Metadata.TryGetMeta<PlayerAnimMetadata>(out meta)) {
                    Logger.Log(LogLevel.Warn, Module.Name, $"No metadata for player animation '{id}' frame '{t.AtlasPath}'");
                    continue;
                }
                metadata[t.AtlasPath] = meta;
                globMetadata[t.AtlasPath] = meta;
            }

            if(!mode.HasValue) {
                //Add to wildcard dict
                if(wildcardAnimations.ContainsKey(id)) Logger.Log(Module.Name, $"Replacing duplicate wildcard player animation '{id}'");
                wildcardAnimations[id] = anim;
            } else {
                //Add animation
                if(!animations.TryGetValue(mode.Value, out var modeAnims)) animations.Add(mode.Value, modeAnims = new Dictionary<string, Sprite.Animation>(StringComparer.OrdinalIgnoreCase));
                if(modeAnims.ContainsKey(id)) Logger.Log(LogLevel.Warn, Module.Name, $"Replacing duplicate player animation '{id}' for sprite mode '{mode}'");
                modeAnims[id] = anim;
            }

            //Add to existing sprites
            if(Engine.Scene != null) {
                foreach(Entity e in Engine.Scene.Entities) {
                    foreach(Component c in e.Components) {
                        if(!(c is PlayerSprite sprite)) continue;
                        if(mode.HasValue && sprite.Mode != mode.Value) continue;
                        new DynData<PlayerSprite>(sprite).Get<Dictionary<string, Sprite.Animation>>("animations").TryAdd(id, anim);
                    }
                }
            }

            Logger.Log(Module.Name, $"Added player animation '{id}' for sprite mode '{mode?.ToString() ?? "ANY"}'");
        }
    }
}