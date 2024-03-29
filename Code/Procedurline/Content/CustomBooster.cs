using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        public readonly bool IsRed;

        public Sprite Sprite { get; private set; }
        private readonly Color spriteColor;

        public CustomBooster(Vector2 pos, Color color, bool isRed) : base(pos, isRed) {
            spriteColor = color;
            IsRed = isRed;
            RecolorGFX(color);
        }

        public override void Added(Scene scene) {
            //Process sprite
            Sprite sprite = Components.Get<Sprite>();
            Remove(sprite);
            Add(Sprite = ProcessSprite(sprite));
            Booster_sprite.SetValue(this, Sprite);

            base.Added(scene);
        }

        /// <summary>
        /// Processes the booster's sprite. By default this recolors it using the color passed to the constructor
        /// The default implementation caches sprites based on color, custom implementations should implement a cache themselves
        /// </summary>
        protected virtual Sprite ProcessSprite(Sprite origSprite) {
            if(!SPRITE_CACHE.TryGetValue(spriteColor, out Sprite recSprite)) {
                Matrix colMat = ColorUtils.CalculateRecolorMatrix(RedColor, spriteColor);
                SPRITE_CACHE[spriteColor] = recSprite = ProcedurlineModule.GlobalDisposablePool.Add(
                    new DerivedSprite($"customBooster-#{spriteColor.PackedValue:x8}", origSprite, new SpriteColorMatrixProcessor(colMat, 0.05f, 0.05f).WrapAsync<Sprite, string, SpriteAnimationData>())
                );
            }
            return recSprite.Clone();
        }

        /// <summary>
        /// Recolors the graphical effects (not the sprite!) of the booster
        /// </summary>
        protected virtual void RecolorGFX(Color col) {
            Matrix colMat = ColorUtils.CalculateRecolorMatrix(IsRed ? RedColor : GreenColor, col);

            //Recolor particles
            AppearParticleType = (IsRed ? P_RedAppear : P_Appear).ApplyMatrix(colMat, 0.05f, 0.05f);
            BurstParticleType = (IsRed ? P_BurstRed : P_Burst).ApplyMatrix(colMat, 0.05f, 0.05f);
        }

        /// <summary>
        /// Make the player enter the booster.
        /// </summary>
        protected virtual void MakePlayerEnter(Player player, BoostType boostType, float? cantUseDelay = VanillaCantUseDelay) {
            if(cantUseDelay.HasValue) CantUseTimer = cantUseDelay.Value;

            switch(boostType) {
                case BoostType.GREEN_BOOST: player.Boost(this); break;
                case BoostType.RED_BOOST: player.RedBoost(this); break;
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

        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void Appear() {}
        [ContentVirtualize(false)] protected virtual void AppearParticles() {
            //Modified vanilla code which uses custom particle types
            ParticleSystem particlesBG = SceneAs<Level>()?.ParticlesBG;
            if(particlesBG == null) return;
            for(int i = 0; i < 360; i += 30) particlesBG.Emit(AppearParticleType, 1, Center, Vector2.One * 2f, i * Calc.DegToRad);
        }
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void Respawn() {}
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual IEnumerator BoostRoutine(Player player, Vector2 dir) => default;

        [ContentVirtualize(false)] protected virtual void OnPlayer(Player player) {
            //Modified vanilla code
            if(RespawnTimer <= 0f && CantUseTimer <= 0f && !BoostingPlayer) {
                BoostType? boostType = OnPlayerEnter(player);
                if(boostType == null) return;
                MakePlayerEnter(player, boostType.Value);
            }
        }
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void OnPlayerDashed(Vector2 dir) {}
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void PlayerBoosted(Player player, Vector2 dir) {}
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void PlayerReleased() {}
        [ContentVirtualize] [MethodImpl(MethodImplOptions.NoInlining)] protected virtual new void PlayerDied() {}

        protected ParticleType AppearParticleType { get; private set; }
        [ContentFieldProxy("particleType")] protected ParticleType BurstParticleType { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }

        [ContentPatchSFX("Appear")] [ContentPatchSFX("Respawn")] protected virtual string AppearSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_reappear" : "event:/game/04_cliffside/greenbooster_reappear";
        protected virtual string EnterSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_enter" : "event:/game/04_cliffside/greenbooster_enter";
        [ContentPatchSFX("PlayerBoosted")] protected virtual string BoostSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_dash" : "event:/game/04_cliffside/greenbooster_dash";
        [ContentPatchSFX("PlayerBoosted")] protected virtual string MoveSFX => "event:/game/05_mirror_temple/redbooster_move";
        [ContentPatchSFX("PlayerReleased")] protected virtual string ReleasedSFX => IsRed ? "event:/game/05_mirror_temple/redbooster_end" : "event:/game/04_cliffside/greenbooster_end";

        [ContentFieldProxy("respawnTimer")] protected float RespawnTimer { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }
        [ContentFieldProxy("cannotUseTimer")] protected float CantUseTimer { [MethodImpl(MethodImplOptions.NoInlining)] get; [MethodImpl(MethodImplOptions.NoInlining)] set; }
    }
}