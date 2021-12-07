using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Celeste;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public class Module : EverestModule {
        public static Module Instance { get; private set; }
        public static string Name => Instance.Metadata.Name;
        public Module() { Instance = this; }

        private AnimationManager animationManager = null;
        private PlayerAnimationManager playerAnimationManager = null;

        public override void Load() {
            //Load content
            CustomBooster.Load();
        }

        public override void Unload() {
            //Unload content
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
            SettingsContentHandler.LoadWildcardContent("Graphics/Atlases/Gameplay/Procedurline/Player/Animations", typeof(Texture2D), (ModAsset asset, string name) => {
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
            });
        }

        public static AnimationManager AnimationManager => Instance.animationManager;
        public static PlayerAnimationManager PlayerAnimationManager => Instance.playerAnimationManager;
    }
}