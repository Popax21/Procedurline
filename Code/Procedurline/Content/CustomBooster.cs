using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomBooster : Booster {
        public enum BoostType {
            GREEN_BOOST, RED_BOOST
        }

        public static readonly Color GreenColor = Calc.HexToColor("#0e4a36");
        public static readonly Color RedColor = Calc.HexToColor("#9c1105");

        private static readonly Dictionary<Color, Sprite> RECOLORED_SPRITES = new Dictionary<Color, Sprite>();
        private static readonly FieldInfo Booster_sprite = typeof(Booster).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Booster_particleType = typeof(Booster).GetField("particleType", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Booster_respawnTimer = typeof(Booster).GetField("respawnTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Booster_cannotUseTimer = typeof(Booster).GetField("cannotUseTimer", BindingFlags.NonPublic | BindingFlags.Instance);

        [ContentHook("AppearParticles")]
        private static void AppearParticlesHook(On.Celeste.Booster.orig_AppearParticles orig, Booster booster) {
            if(booster is CustomBooster cBooster) {
                //Modified vanilla code which uses custom particle types
                ParticleSystem particlesBG = cBooster.SceneAs<Level>()?.ParticlesBG;
                if(particlesBG == null) return;
                for(int i = 0; i < 360; i += 30) particlesBG.Emit(cBooster.AppearParticles, 1, cBooster.Center, Vector2.One * 2f, i * Calc.DegToRad);
            } else orig(booster);
        }

        public readonly Color Color;
        public readonly Sprite Sprite;
        public readonly ParticleType AppearParticles;
        public readonly ParticleType BurstParticles;

        public CustomBooster(Vector2 pos, Color color) : base(pos, true) {
            Color = color;

            Matrix colMat = ColorUtils.CalculateRecolorMatrix(RedColor, color);

            //Process sprite
            Sprite sprite = Components.Get<Sprite>();
            Remove(sprite);
            Add(Sprite = ProcessSprite(sprite));
            Booster_sprite.SetValue(this, Sprite);

            //Recolor particles
            AppearParticles = P_RedAppear.ApplyMatrix(colMat);
            Booster_particleType.SetValue(this, BurstParticles = P_BurstRed.ApplyMatrix(colMat, 0.05f, 0.05f));

            //Change player collide callaback
            Components.Get<PlayerCollider>().OnCollide = OnPlayerCollision;
        }

        private void OnPlayerCollision(Player player) {
            //Modified version of the vanilla code
            if((float) Booster_respawnTimer.GetValue(this) <= 0f && (float) Booster_cannotUseTimer.GetValue(this) <= 0f && !BoostingPlayer) {
                BoostType? boostType = OnPlayerEnter(player);
                if(boostType == null) return;
                Booster_cannotUseTimer.SetValue(this, 0.45f);

                if(boostType == BoostType.GREEN_BOOST) player.Boost(this);
                else player.RedBoost(this);
                Audio.Play((boostType == BoostType.RED_BOOST) ? "event:/game/05_mirror_temple/redbooster_enter" : "event:/game/04_cliffside/greenbooster_enter", Position);

                Components.Get<Wiggler>().Start();
                Sprite sprite = Components.Get<Sprite>();
                sprite.Play("inside");
                sprite.FlipX = (player.Facing == Facings.Left);
            }
        }

        /// <summary>
        /// Processes the booster's sprite. By default this recolors it using the color passed to the constructor
        /// The default implementation caches sprites based on color, custom implementations should implement a cache themselves
        /// </summary>
        protected virtual Sprite ProcessSprite(Sprite origSprite) {
            if(!RECOLORED_SPRITES.TryGetValue(Color, out Sprite recSprite)) {
                Matrix colMat = ColorUtils.CalculateRecolorMatrix(RedColor, Color);
                RECOLORED_SPRITES[Color] = recSprite = new StaticSprite($"customBooster-#{Color.PackedValue:x8}", Sprite, new SpriteColorMatrixProcessor(colMat, 0.05f, 0.05f));
            }
            return recSprite.Clone();
        }

        /// <summary>
        /// Called when the player enters your booster. Return the type of boost it should give them, or <c>null</c> if no default behaviour should take place.
        /// </summary>
        protected abstract BoostType? OnPlayerEnter(Player player);

        [ContentVirtualize] protected virtual new void Appear() {}
        [ContentVirtualize] protected virtual new void PlayerBoosted(Player player, Vector2 dir) {}
        [ContentVirtualize] protected virtual new void PlayerReleased() {}
        [ContentVirtualize] protected virtual new void PlayerDied() {}
    }
}