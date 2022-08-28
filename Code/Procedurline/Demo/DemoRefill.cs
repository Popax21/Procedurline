using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline.Demo {
    public class DemoRefill : CustomRefill {
        private static Color[] colors = new Color[] {
            Color.Red, Color.Green, Color.Blue, Color.Yellow, Color.Fuchsia
        };
        private static Sprite[] recoloredSprites;

        private DisposablePool dispPool;
        private SpriteMultiplexer spriteMux;

        public DemoRefill(Vector2 pos) : base(pos, colors[0], false, false) {
            dispPool = DisposablePoolComponent.AddTo(this);
        }

        protected override Sprite ProcessSprite(Sprite origSprite) {
            if(recoloredSprites == null) {
                //Recolor sprites
                TextureScope texScope = ProcedurlineModule.GlobalDisposablePool.Add(new TextureScope("pldemo-refill", ProcedurlineModule.TextureManager.StaticScope));
                recoloredSprites = new Sprite[colors.Length];
                for(int i = 0; i < colors.Length; i++) {
                    recoloredSprites[i] = ProcedurlineModule.GlobalDisposablePool.Add(new DerivedSprite($"pldemo-refill-#{colors[i].PackedValue:x8}", origSprite,
                        new SpriteColorMatrixProcessor(ColorUtils.CalculateRecolorMatrix(OnceColor, colors[i]), 0.05f, 0.05f).WrapAsync<Sprite, string, SpriteAnimationData>(),
                    texScope));
                }
            }

            //Create sprite mux
            dispPool.Add(spriteMux = new SpriteMultiplexer("pldemo-refill-mux", recoloredSprites));

            //Create sprite
            return dispPool.Add(new MixerSprite("pldemo-refill", origSprite, spriteMux.WrapAsync()));
        }

        protected override bool OnTouch(Player player) {
            Logger.Log("PLdemo", "Player touched refill!");

            //Randomize color
            int colorIdx = Calc.Random.Range(0, colors.Length);
            spriteMux.MuxIndex = colorIdx;
            RecolorGFX(colors[colorIdx]);

            return true;
        }

        protected override string CollectSFX => SFX.char_mad_death;
    }
}