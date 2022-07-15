using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Monocle;

namespace Celeste.Mod.Procedurline {
    public abstract class CustomBooster : Booster {
        public enum BoostType {
            GREEN_BOOST, RED_BOOST, CUSTOM
        }

        public const float VanillaCantUseDelay = 0.45f;

        public static readonly Color GreenColor = Calc.HexToColor("#0e4a36");
        public static readonly Color RedColor = Calc.HexToColor("#9c1105");
        private static readonly Dictionary<Color, Sprite> SPRITE_CACHE = new Dictionary<Color, Sprite>();

        private static readonly FieldInfo Booster_sprite = typeof(Booster).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Booster_particleType = typeof(Booster).GetField("particleType", BindingFlags.NonPublic | BindingFlags.Instance);

        public readonly bool IsRed;
        public readonly Color Color;
        public readonly Sprite Sprite;
        public readonly ParticleType AppearParticleType;
        public readonly ParticleType BurstParticleType;

        public CustomBooster(Vector2 pos, Color color, bool isRed) : base(pos, isRed) {
            IsRed = isRed;
            Color = color;

            Matrix colMat = ColorUtils.CalculateRecolorMatrix(isRed ? RedColor : GreenColor, color);

            //Process sprite
            Sprite sprite = Components.Get<Sprite>();
            Remove(sprite);
            Add(Sprite = ProcessSprite(sprite));
            Booster_sprite.SetValue(this, Sprite);

            //Recolor particles
            AppearParticleType = P_RedAppear.ApplyMatrix(colMat);
            Booster_particleType.SetValue(this, BurstParticleType = P_BurstRed.ApplyMatrix(colMat, 0.05f, 0.05f));
        }

        /// <summary>
        /// Processes the booster's sprite. By default this recolors it using the color passed to the constructor
        /// The default implementation caches sprites based on color, custom implementations should implement a cache themselves
        /// </summary>
        protected virtual Sprite ProcessSprite(Sprite origSprite) {
            if(!SPRITE_CACHE.TryGetValue(Color, out Sprite recSprite)) {
                Matrix colMat = ColorUtils.CalculateRecolorMatrix(RedColor, Color);
                SPRITE_CACHE[Color] = recSprite = new StaticSprite($"customBooster-#{Color.PackedValue:x8}", origSprite, new SpriteColorMatrixProcessor(colMat, 0.05f, 0.05f).WrapAsync<Sprite, string, SpriteAnimationData>());
            }
            return recSprite.Clone();
        }

        /// <summary>
        /// Make the player enter the booster.
        /// </summary>
        protected virtual void MakePlayerEnter(Player player, BoostType boostType, float cantUseDelay = VanillaCantUseDelay) {
            CantUseTimer = cantUseDelay;

            if(boostType != BoostType.CUSTOM) {
                if(boostType == BoostType.GREEN_BOOST) player.Boost(this);
                else player.RedBoost(this);
            }

            Audio.Play(EnterSFX, Position);
            Components.Get<Wiggler>().Start();
            Sprite.Play("inside");
            Sprite.FlipX = (player.Facing == Facings.Left);
        }

        /// <summary>
        /// Make the player exit the booster.
        /// </summary>
        protected virtual void MakePlayerExit(Player player, int newState = Player.StNormal) {
            if(player.CurrentBooster != this || player.StateMachine.State != Player.StBoost || player.StateMachine.State != Player.StDash || player.StateMachine.State != Player.StRedDash) return;
            player.StateMachine.State = newState;
        }

        /// <summary>
        /// Called when the player enters your booster.
        /// </summary>
        /// <returns>
        /// The type of boost it should give them, or <c>null</c> if no default behaviour should take place.
        /// </returns>
        protected abstract BoostType? OnPlayerEnter(Player player);
        [ContentVirtualize] protected virtual new void Appear() {}
        [ContentVirtualize(false)] protected virtual void AppearParticles() {
            //Modified vanilla code which uses custom particle types
            ParticleSystem particlesBG = SceneAs<Level>()?.ParticlesBG;
            if(particlesBG == null) return;
            for(int i = 0; i < 360; i += 30) particlesBG.Emit(AppearParticleType, 1, Center, Vector2.One * 2f, i * Calc.DegToRad);
        }
        [ContentVirtualize] protected virtual new void Respawn() {}
        [ContentVirtualize] protected virtual IEnumerator BoostRoutine(Player player, Vector2 dir) => default;

        [ContentVirtualize(false)] protected virtual void OnPlayer(Player player) {
            //Modified vanilla code
            if(RespawnTimer <= 0f && CantUseTimer <= 0f && !BoostingPlayer) {
                BoostType? boostType = OnPlayerEnter(player);
                if(boostType == null) return;
                MakePlayerEnter(player, boostType.Value);
            }
        }
        [ContentVirtualize] protected virtual new void OnPlayerDashed(Vector2 dir) {}
        [ContentVirtualize] protected virtual new void PlayerBoosted(Player player, Vector2 dir) {}
        [ContentVirtualize] protected virtual new void PlayerReleased() {}
        [ContentVirtualize] protected virtual new void PlayerDied() {}

        [ContentPatchSFX("Appear")] [ContentPatchSFX("Respawn")] protected virtual string AppearSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_reappear" : "event:/game/04_cliffside/greenbooster_reappear";
        protected virtual string EnterSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_enter" : "event:/game/04_cliffside/greenbooster_enter";
        [ContentPatchSFX("PlayerBoosted")] protected virtual string BoostSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_dash" : "event:/game/04_cliffside/greenbooster_dash";
        [ContentPatchSFX("PlayerBoosted")] protected virtual string MoveSFX => "event:/game/05_mirror_temple/redbooster_move";
        [ContentPatchSFX("PlayerReleased")] protected virtual string ReleasedSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_end" : "event:/game/04_cliffside/greenbooster_end";

        [ContentFieldProxy("respawnTimer")] protected float RespawnTimer { get; set; }
        [ContentFieldProxy("cannotUseTimer")] protected float CantUseTimer { get; set; }
    }
}