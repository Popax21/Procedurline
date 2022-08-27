using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline.Demo {
    public class DemoRefill : CustomRefill {
        private static Sprite SPRITE;

        public DemoRefill(Vector2 pos) : base(pos, default, false, false) {}

        protected override Sprite ProcessSprite(Sprite origSprite) {
            if(SPRITE == null) SPRITE = new DerivedSprite("pldemo-refill", origSprite, new SpriteColorMatrixProcessor(ColorUtils.GrayscaleColorMatrix, 0.05f, 0.05f).WrapAsync<Sprite, string, SpriteAnimationData>());
            return SPRITE.Clone();
        }

        protected override bool OnTouch(Player player) {
            Logger.Log("PLdemo", "Player touched refill!");
            return true;
        }

        protected override string CollectSFX => SFX.char_mad_death_golden;
    }
}