using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline.Demo {
    public class DemoBooster : CustomBooster {
        private static Sprite SPRITE;

        public DemoBooster(Vector2 pos) : base(pos, default, false) {}

        protected override Sprite ProcessSprite(Sprite origSprite) {
            if(SPRITE == null) SPRITE = new DerivedSprite("pldemo-booster", origSprite, new SpriteColorMatrixProcessor(ColorUtils.GrayscaleColorMatrix, 0.05f, 0.05f).WrapAsync<Sprite, string, SpriteAnimationData>());
            return SPRITE.Clone();
        }

        protected override BoostType? OnPlayerEnter(Player player) {
            Logger.Log("PLdemo", "Player entered booster!");
            return BoostType.GREEN_BOOST;
        }

        protected override void PlayerBoosted(Player player, Vector2 dir) {
            Logger.Log("PLdemo", "Player boosted while in booster!");
            base.PlayerBoosted(player, dir);
        }

        protected override string BoostSFX => SFX.char_mad_death_golden;
    }
}