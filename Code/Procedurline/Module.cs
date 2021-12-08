using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Celeste;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public class ProcedurlineModule : EverestModule {
        public static ProcedurlineModule Instance { get; private set; }
        public static string Name => Instance.Metadata.Name;
        public ProcedurlineModule() { Instance = this; }

        private On.Monocle.Scene.hook_BeforeUpdate updateHook;
        private LinkedList<Tuple<VirtualTexture, TextureData>> textureUploadList = new LinkedList<Tuple<VirtualTexture, TextureData>>();

        private AnimationManager animationManager = null;
        private PlayerAnimationManager playerAnimationManager = null;

        public override void Load() {
            //Add hooks
            On.Monocle.Scene.BeforeUpdate += updateHook = (orig, scene) => {
                UploadTextures();
                orig(scene);
            };

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

        public static void UploadTexture(VirtualTexture texture, TextureData data) {
            Texture2D tex = texture.Texture_Safe;
            if(tex == null) {
                //Queue for later upload
                Instance.textureUploadList.AddLast(new Tuple<VirtualTexture, TextureData>(texture, data));
            } else tex.SetData<Color>(data.Pixels);
        }

        private void UploadTextures() {
            if(textureUploadList.Count > 0) {
                LinkedList<Tuple<VirtualTexture, TextureData>> l = textureUploadList;
                textureUploadList = new LinkedList<Tuple<VirtualTexture, TextureData>>();
                foreach(var tex in l) UploadTexture(tex.Item1, tex.Item2);
            }
        }

        public static AnimationManager AnimationManager => Instance.animationManager;
        public static PlayerAnimationManager PlayerAnimationManager => Instance.playerAnimationManager;
    }
}
