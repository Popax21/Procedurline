using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomRefill : Refill {
        public const float VanillaRespawnDelay = 2.5f;

        public static readonly Color OnceColor = Calc.HexToColor("#93bd40");
        public static readonly Color DoubleColor = Calc.HexToColor("#e268d1");
        private static readonly Dictionary<Tuple<Color, bool>, Sprite> SPRITE_CACHE = new Dictionary<Tuple<Color, bool>, Sprite>();

        private static readonly FieldInfo Refill_sprite = typeof(Refill).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);

        public readonly bool DoubleRefill, OneUse;

        public Sprite Sprite { get; private set; }
        private readonly Color spriteColor;

        public CustomRefill(Vector2 position, Color color, bool doubleRefill, bool oneUse) : base(position, doubleRefill, oneUse) {
            spriteColor = color;
            DoubleRefill = doubleRefill;
            OneUse = oneUse;
            RecolorGFX(color);
        }

        public override void Added(Scene scene) {
            //Process sprite
            Sprite sprite = Components.Get<Sprite>();
            Remove(sprite);
            Add(Sprite = ProcessSprite(sprite));
            Refill_sprite.SetValue(this, Sprite);

            base.Added(scene);
        }

        /// <summary>
        /// Processes the refill's sprite. By default this recolors it using the color passed to the constructor
        /// The default implementation caches sprites based on color and refill type, custom implementations should implement a cache themselves
        /// </summary>
        protected virtual Sprite ProcessSprite(Sprite origSprite) {
            if(!SPRITE_CACHE.TryGetValue(new Tuple<Color, bool>(spriteColor, DoubleRefill), out Sprite recSprite)) {
                Matrix colMat = ColorUtils.CalculateRecolorMatrix(DoubleRefill ? DoubleColor : OnceColor, spriteColor);
                SPRITE_CACHE[new Tuple<Color, bool>(spriteColor, DoubleRefill)] = recSprite = ProcedurlineModule.GlobalDisposablePool.Add(
                    new DerivedSprite($"custom{(DoubleRefill ? "Double" : string.Empty)}Refill-#{spriteColor.PackedValue:x8}", origSprite, new SpriteColorMatrixProcessor(colMat, 0.05f, 0.05f).WrapAsync<Sprite, string, SpriteAnimationData>())
                );
            }
            return recSprite.Clone();
        }

        /// <summary>
        /// Recolors the graphical effects (not the sprite!) of the refill
        /// </summary>
        protected virtual void RecolorGFX(Color col) {
            Matrix colMat = ColorUtils.CalculateRecolorMatrix(DoubleRefill ? DoubleColor : OnceColor, col);

            //Recolor particles
            ShatterParticleType = (DoubleRefill ? Refill.P_ShatterTwo : Refill.P_Shatter).ApplyMatrix(colMat, 0.05f, 0.05f);
            RegenerationParticleType = (DoubleRefill ? Refill.P_RegenTwo : Refill.P_Regen).ApplyMatrix(colMat, 0.05f, 0.05f);
            GlowParticleType = (DoubleRefill ? Refill.P_GlowTwo : Refill.P_Glow).ApplyMatrix(colMat, 0.05f, 0.05f);
        }

        /// <summary>
        /// Breaks the refill
        /// </summary>
        protected virtual void Break(Player player, float? respawnDelay = VanillaRespawnDelay) {
            Audio.Play(CollectSFX, Position);
            Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);

            Collidable = false;
            Add(new Coroutine(RefillRoutine(player)));

            if(respawnDelay.HasValue) RespawnTimer = respawnDelay.Value;
        }

        /// <summary>
        /// Called when the player touches your refill
        /// </summary>
        /// <returns>
        /// <c>true</c> if the default break behaviour should occur, or <c>false</c> otherwise.
        /// </returns>
        protected abstract bool OnTouch(Player player);

        public bool Broken => !Collidable;

        [ContentVirtualize(false)] protected virtual void OnPlayer(Player player) {
            if(!Broken && OnTouch(player)) Break(player);
        }
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual void Respawn() {}
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual IEnumerator RefillRoutine(Player player) => default;

        [ContentFieldProxy("p_shatter")] protected ParticleType ShatterParticleType { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }
        [ContentFieldProxy("p_regen")] protected ParticleType RegenerationParticleType { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }
        [ContentFieldProxy("p_glow")] protected ParticleType GlowParticleType { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }

        protected virtual string CollectSFX => DoubleRefill ? "event:/new_content/game/10_farewell/pinkdiamond_touch" : "event:/game/general/diamond_touch";
        [ContentPatchSFX("Respawn")] protected virtual string RespawnSFX => DoubleRefill ? "event:/new_content/game/10_farewell/pinkdiamond_return" : "event:/game/general/diamond_return";

        [ContentFieldProxy("respawnTimer")] protected float RespawnTimer { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }
    }
}
