using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomRefill : Refill {
        private static readonly Color ONCE_COLOR = Calc.HexToColor("#93bd40");
        private static readonly Color DOUBLE_COLOR = Calc.HexToColor("#e268d1");
        private static readonly Dictionary<Tuple<Color, bool>, Sprite> RECOLORED_SPRITES = new Dictionary<Tuple<Color, bool>, Sprite>();
        private static readonly FieldInfo SPRITE_FIELD = typeof(Refill).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo SHATTER_PARTICLE_FIELD = typeof(Refill).GetField("p_shatter", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo REGEN_PARTICLE_FIELD = typeof(Refill).GetField("p_regen", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo GLOW_PARTICLE_FIELD = typeof(Refill).GetField("p_glow", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo RESPAWN_TIMER_FIELD = typeof(Refill).GetField("respawnTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Func<Refill, Player, IEnumerator> REFILL_ROUTINE = (Func<Refill, Player, IEnumerator>) typeof(Refill).GetMethod("RefillRoutine", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(Func<Refill, Player, IEnumerator>));

        public CustomRefill(Vector2 position, Color color, bool doubleRefill, float respawnDelay = 2.5f) : base(position, doubleRefill, respawnDelay < 0) {
            Color = color;
            DoubleRefill = doubleRefill;
            RespawnDelay = respawnDelay;

            Color origCol = doubleRefill ? DOUBLE_COLOR : ONCE_COLOR;
            Matrix hueShift = ColorHelper.CalculateHueShiftMatrix(color.GetHue() - origCol.GetHue());
            float intensityShift = (float) (color.R+color.G+color.B) / (origCol.R+origCol.G+origCol.B);

            //Recolor sprite
            Sprite sprite = Components.Get<Sprite>();
            if(!RECOLORED_SPRITES.TryGetValue(new Tuple<Color, bool>(color, doubleRefill), out Sprite recSprite)) RECOLORED_SPRITES[new Tuple<Color, bool>(color, doubleRefill)] = recSprite = sprite.ShiftColor(hueShift, intensityShift);
            Remove(sprite);
            Add(sprite = recSprite.Clone());
            SPRITE_FIELD.SetValue(this, sprite);

            //Recolor particels
            SHATTER_PARTICLE_FIELD.SetValue(this, ShatterParticles = ((ParticleType) SHATTER_PARTICLE_FIELD.GetValue(this)).ShiftColor(hueShift, intensityShift));
            REGEN_PARTICLE_FIELD.SetValue(this, RegenerationParticles = ((ParticleType) REGEN_PARTICLE_FIELD.GetValue(this)).ShiftColor(hueShift, intensityShift));
            GLOW_PARTICLE_FIELD.SetValue(this, GlowParticles = ((ParticleType) GLOW_PARTICLE_FIELD.GetValue(this)).ShiftColor(hueShift, intensityShift));

            //Change player collide callaback
            Components.Get<PlayerCollider>().OnCollide = OnPlayerCollision;
        }

        private void OnPlayerCollision(Player player) {
            if(!Broken && OnTouch(player)) {
                Broken = true;
                Audio.Play(DoubleRefill ? "event:/new_content/game/10_farewell/pinkdiamond_touch" : "event:/game/general/diamond_touch", Position);
                Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
                Add(new Coroutine(REFILL_ROUTINE(this, player)));
                Collidable = false;
            }
        }

        protected void Respawn() {
            if(Broken) {
                RESPAWN_TIMER_FIELD.SetValue(this, RespawnDelay);
                Broken = false;
            }
        }

        protected abstract bool OnTouch(Player player);

        public bool Broken { get; private set; } = false;
        public Color Color { get; }
        public ParticleType ShatterParticles { get; }
        public ParticleType RegenerationParticles { get; }
        public ParticleType GlowParticles { get; }
        public bool DoubleRefill { get; }
        public float RespawnDelay { get; }
    }
}
