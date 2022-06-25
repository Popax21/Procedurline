using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomRefill : Refill {
        public static readonly Color OnceColor = Calc.HexToColor("#93bd40");
        public static readonly Color DoubleColor = Calc.HexToColor("#e268d1");

        private static readonly Dictionary<Tuple<Color, bool>, Sprite> RECOLORED_SPRITES = new Dictionary<Tuple<Color, bool>, Sprite>();
        private static readonly FieldInfo Refill_sprite = typeof(Refill).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo Refill_p_shatter = typeof(Refill).GetField("p_shatter", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Refill_p_regen = typeof(Refill).GetField("p_regen", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Refill_p_glow = typeof(Refill).GetField("p_glow", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo Refill_respawnTimer = typeof(Refill).GetField("respawnTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Func<Refill, Player, IEnumerator> Refill_RefillRoutine = (Func<Refill, Player, IEnumerator>) typeof(Refill).GetMethod("RefillRoutine", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(Func<Refill, Player, IEnumerator>));

        public readonly Color Color;
        public readonly bool DoubleRefill, OneUse;
        public readonly ParticleType ShatterParticles;
        public readonly ParticleType RegenerationParticles;
        public readonly ParticleType GlowParticles;

        public CustomRefill(Vector2 position, Color color, bool doubleRefill, bool oneUse) : base(position, doubleRefill, oneUse) {
            Color = color;
            DoubleRefill = doubleRefill;
            OneUse = oneUse;

            Matrix colMat = ColorUtils.CalculateRecolorMatrix(doubleRefill ? DoubleColor : OnceColor, color);

            //Process sprite
            Sprite sprite = Components.Get<Sprite>();
            Remove(sprite);
            Add(sprite = ProcessSprite(sprite));
            Refill_sprite.SetValue(this, sprite);

            //Recolor particels
            Refill_p_shatter.SetValue(this, ShatterParticles = ((ParticleType) Refill_p_shatter.GetValue(this)).ApplyMatrix(colMat, 0.05f, 0.05f));
            Refill_p_regen.SetValue(this, RegenerationParticles = ((ParticleType) Refill_p_regen.GetValue(this)).ApplyMatrix(colMat, 0.05f, 0.05f));
            Refill_p_glow.SetValue(this, GlowParticles = ((ParticleType) Refill_p_glow.GetValue(this)).ApplyMatrix(colMat, 0.05f, 0.05f));

            //Change player collide callaback
            Components.Get<PlayerCollider>().OnCollide = OnPlayerCollision;
        }

        private void OnPlayerCollision(Player player) {
            if(!Broken && OnTouch(player)) {
                Broken = true;
                Audio.Play(DoubleRefill ? "event:/new_content/game/10_farewell/pinkdiamond_touch" : "event:/game/general/diamond_touch", Position);
                Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
                Add(new Coroutine(Refill_RefillRoutine(this, player)));
                Collidable = false;
            }
        }

        /// <summary>
        /// Processes the refill's sprite. By default this recolors it using the color passed to the constructor
        /// The default implementation caches sprites based on color and refill type, custom implementations should implement a cache themselves
        /// </summary>
        protected virtual Sprite ProcessSprite(Sprite origSprite) {
            if(!RECOLORED_SPRITES.TryGetValue(new Tuple<Color, bool>(Color, DoubleRefill), out Sprite recSprite)) {
                Matrix colMat = ColorUtils.CalculateRecolorMatrix(DoubleRefill ? DoubleColor : OnceColor, Color);
                RECOLORED_SPRITES[new Tuple<Color, bool>(Color, DoubleRefill)] = recSprite = new StaticSprite($"custom{(DoubleRefill ? "Double" : string.Empty)}Refill-#{Color.PackedValue:x8}", origSprite, new SpriteColorMatrixProcessor(colMat, 0.05f, 0.05f));
            }
            return recSprite.Clone();
        }

        /// <summary>
        /// Respawns the refill, if it's broken.
        /// </summary>
        protected void Respawn(float time = 0f) {
            if(Broken) {
                Refill_respawnTimer.SetValue(this, time);
                Broken = false;
            }
        }

        /// <summary>
        /// Called when the player touches your refill. Return <c>true</c> if it should break, or <c>false</c> otherwise.
        /// </summary>
        protected abstract bool OnTouch(Player player);

        public bool Broken { get; private set; } = false;
    }
}
