using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Celeste;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public class Procedurline : EverestModule {
        public static Procedurline Instance { get; private set; }
        public static string Name => Instance.Metadata.Name;
        public Procedurline() { Instance = this; }

        private On.Monocle.Scene.hook_BeforeUpdate updateHook;
        private LinkedList<(MTexture, TextureData)> textureUploadList = new LinkedList<(MTexture, TextureData)>();

        private AnimationManager animationManager = null;
        private PlayerAnimationManager playerAnimationManager = null;

        public override void Load() {
            //Add hooks
            On.Monocle.Scene.BeforeUpdate += updateHook = (orig, scene) => UploadTextures();

            //Load content
            HairOverride.Load();
            CustomBooster.Load();
        }

        public override void Unload() {
            //Remove hooks
            if(updateHook != null) On.Monocle.Scene.BeforeUpdate -= updateHook;
            updateHook = null;

            //Unload content
            HairOverride.Unload();
            CustomBooster.Unload();
            
            //Destroy the animation managers
            if(animationManager != null) animationManager.Dispose();
            animationManager = null;

            if(playerAnimationManager != null) playerAnimationManager.Dispose();
            playerAnimationManager = null;
        }

        public override void LoadContent(bool firstLoad) {
            //Create the animation managers
            animationManager = new AnimationManager();
            playerAnimationManager = new PlayerAnimationManager();

            //Load new player animations
            HashSet<string> loadedAnimations = new HashSet<string>();
            ModAsset animDirAsset = Everest.Content.Get("Graphics/Atlases/Gameplay/Procedurline/Player/Animations", true);
            if(animDirAsset != null) foreach(ModAsset asset in animDirAsset.Children) {
                if(asset.Type != typeof(Texture2D)) continue;
                string name = asset.PathVirtual.Substring(asset.PathVirtual.LastIndexOf('/')+1);

                //Trim away number suffix from name
                while(Char.IsNumber(name, name.Length-1)) name = name.Substring(0, name.Length-1);
                
                //Trim away atlas prefix and number suffix from path
                string path = asset.PathVirtual;
                while(Char.IsNumber(path, path.Length-1)) path = path.Substring(0, path.Length-1);
                path = path.Substring(GFX.Game.DataPath.Length + (GFX.Game.DataPath[GFX.Game.DataPath.Length-1] == '/' ? 0 : 1));

                //If we already loaded an animation with this path, skip it
                if(loadedAnimations.Contains(path)) return;
                loadedAnimations.Add(path);

                //Add animation
                playerAnimationManager.AddAnimation(default, name, path, 0.15f, new Monocle.Chooser<string>(name));
            }
        }

        public static void UploadTexture(MTexture texture, TextureData data) {
            if(texture.Texture.Texture_Safe == null) {
                //Queue for later upload
                Instance.textureUploadList.AddLast((texture, data));
            } else texture.Texture.Texture_Safe.SetData<Color>(data.Pixels);
        }

        private void UploadTextures() {
            if(textureUploadList.Count > 0) {
                LinkedList<(MTexture texture, TextureData data)> l = textureUploadList;
                textureUploadList = new LinkedList<(MTexture, TextureData)>();
                foreach(var tex in l) UploadTexture(tex.texture, tex.data);
            }
        }

        public static AnimationManager AnimationManager => Instance.animationManager;
        public static PlayerAnimationManager PlayerAnimationManager => Instance.playerAnimationManager;
    }
}