using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomRefill : Refill {
        public const float VanillaRespawnDelay = 2.5f;

        public static readonly Color OnceColor = Calc.HexToColor("#93bd40");
        public static readonly Color DoubleColor = Calc.HexToColor("#e268d1");
        private static readonly Dictionary<Tuple<Color, bool>, Sprite> SPRITE_CACHE = new Dictionary<Tuple<Color, bool>, Sprite>();

        private static readonly FieldInfo Refill_sprite = typeof(Refill).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Refill_p_shatter = typeof(Refill).GetField("p_shatter", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Refill_p_regen = typeof(Refill).GetField("p_regen", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Refill_p_glow = typeof(Refill).GetField("p_glow", BindingFlags.NonPublic | BindingFlags.Instance);

        public readonly Color Color;
        public readonly bool DoubleRefill, OneUse;
        public readonly ParticleType ShatterParticleType;
        public readonly ParticleType RegenerationParticleType;
        public readonly ParticleType GlowParticleType;

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
            Refill_p_shatter.SetValue(this, ShatterParticleType = ((ParticleType) Refill_p_shatter.GetValue(this)).ApplyMatrix(colMat, 0.05f, 0.05f));
            Refill_p_regen.SetValue(this, RegenerationParticleType = ((ParticleType) Refill_p_regen.GetValue(this)).ApplyMatrix(colMat, 0.05f, 0.05f));
            Refill_p_glow.SetValue(this, GlowParticleType = ((ParticleType) Refill_p_glow.GetValue(this)).ApplyMatrix(colMat, 0.05f, 0.05f));
        }

        /// <summary>
        /// Processes the refill's sprite. By default this recolors it using the color passed to the constructor
        /// The default implementation caches sprites based on color and refill type, custom implementations should implement a cache themselves
        /// </summary>
        protected virtual Sprite ProcessSprite(Sprite origSprite) {
            if(!SPRITE_CACHE.TryGetValue(new Tuple<Color, bool>(Color, DoubleRefill), out Sprite recSprite)) {
                Matrix colMat = ColorUtils.CalculateRecolorMatrix(DoubleRefill ? DoubleColor : OnceColor, Color);
                SPRITE_CACHE[new Tuple<Color, bool>(Color, DoubleRefill)] = recSprite = new StaticSprite($"custom{(DoubleRefill ? "Double" : string.Empty)}Refill-#{Color.PackedValue:x8}", origSprite, new SpriteColorMatrixProcessor(colMat, 0.05f, 0.05f));
            }
            return recSprite.Clone();
        }

        /// <summary>
        /// Breaks the refill.
        /// </summary>
        protected virtual void Break(Player player, float? respawnDelay = VanillaRespawnDelay) {
            Audio.Play(CollectSFX, Position);
            Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);

            Collidable = false;
            Add(new Coroutine(RefillRoutine(player)));

            if(respawnDelay.HasValue) RespawnTimer = respawnDelay.Value;
        }

        /// <summary>
        /// Called when the player touches your refill.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the default break behaviour should occur, or <c>false</c> otherwise.
        /// </returns>
        protected abstract bool OnTouch(Player player);

        public bool Broken => !Collidable;

        [ContentVirtualize(false)] protected virtual void OnPlayer(Player player) {
            if(!Broken && OnTouch(player)) Break(player);
        }
        [ContentVirtualize] protected virtual void Respawn() {}
        [ContentVirtualize] protected virtual IEnumerator RefillRoutine(Player player) => default;

        protected virtual string CollectSFX => DoubleRefill ? "event:/new_content/game/10_farewell/pinkdiamond_touch" : "event:/game/general/diamond_touch";
        [ContentPatchSFX("Respawn")] protected virtual string RespawnSFX => DoubleRefill ? "event:/new_content/game/10_farewell/pinkdiamond_return" : "event:/game/general/diamond_return";

        [ContentFieldProxy("respawnTimer")] protected float RespawnTimer { get; set; }
    }
}
