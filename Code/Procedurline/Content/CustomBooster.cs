using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomBooster : Booster {
        public enum BoostType {
            GREEN_BOOST, RED_BOOST
        }

        private static readonly Color ORIG_COLOR = Calc.HexToColor("9c1105");
        private static readonly Dictionary<Color, Sprite> RECOLORED_SPRITES = new Dictionary<Color, Sprite>();
        private static readonly FieldInfo SPRITE_FIELD = typeof(Booster).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PARTICLE_TYPE_FIELD = typeof(Booster).GetField("particleType", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RESPAWN_TIMER_FIELD = typeof(Booster).GetField("respawnTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CANNOT_USE_TIMER = typeof(Booster).GetField("cannotUseTimer", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static void Load() => On.Celeste.Booster.AppearParticles += AppearParticlesHook;
        internal static void Unload() => On.Celeste.Booster.AppearParticles -= AppearParticlesHook;
        private static void AppearParticlesHook(On.Celeste.Booster.orig_AppearParticles orig, Booster booster) {
            if(booster is CustomBooster cBooster) {
                ParticleSystem particlesBG = cBooster.SceneAs<Level>()?.ParticlesBG;
                if(particlesBG == null) return;
                for(int i = 0; i < 360; i += 30) particlesBG.Emit(cBooster.AppearParticles, 1, cBooster.Center, Vector2.One * 2f, i * Calc.DegToRad);
            } else orig(booster);
        }
        
        public CustomBooster(Vector2 pos, Color color) : base(pos, true) {
            Matrix hueShift = ColorHelper.CalculateHueShiftMatrix(color.GetHue() - ORIG_COLOR.GetHue());
            float intensityShift = (float) (color.R+color.G+color.B) / (ORIG_COLOR.R+ORIG_COLOR.G+ORIG_COLOR.B);

            //Recolor the sprite
            Sprite sprite = Components.Get<Sprite>();
            if(!RECOLORED_SPRITES.TryGetValue(color, out Sprite recSprite)) RECOLORED_SPRITES[color] = recSprite = sprite.ShiftColor(hueShift, intensityShift);
            Remove(sprite);
            Add(sprite = recSprite.Clone());
            SPRITE_FIELD.SetValue(this, sprite);

            //Recolor particles
            AppearParticles = P_RedAppear.ShiftColor(hueShift, intensityShift);
            PARTICLE_TYPE_FIELD.SetValue(this, BurstParticles = P_BurstRed.ShiftColor(hueShift, intensityShift));

            //Change player collide callaback
            Components.Get<PlayerCollider>().OnCollide = OnPlayerCollision;
        }

        private void OnPlayerCollision(Player player) {
            if ((float) RESPAWN_TIMER_FIELD.GetValue(this) <= 0f && (float) CANNOT_USE_TIMER.GetValue(this) <= 0f && !BoostingPlayer) {
                BoostType? boostType = OnBoost(player);
                if(boostType == null) return;
                CANNOT_USE_TIMER.SetValue(this, 0.45f);

                if(boostType == BoostType.GREEN_BOOST) player.Boost(this);
                else player.RedBoost(this);
                Audio.Play((boostType == BoostType.RED_BOOST) ? "event:/game/05_mirror_temple/redbooster_enter" : "event:/game/04_cliffside/greenbooster_enter", Position);

                Components.Get<Wiggler>().Start();
                Sprite sprite = Components.Get<Sprite>();
                sprite.Play("inside");
                sprite.FlipX = (player.Facing == Facings.Left);
            }
        }
        
        protected abstract BoostType? OnBoost(Player player);

        public ParticleType AppearParticles { get; }
        public ParticleType BurstParticles { get; }
    }
}