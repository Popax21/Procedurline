using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline.Demo {
    public class DemoBooster : CustomBooster {
        private static Sprite SPRITE;
        private static readonly IAsyncDataProcessor<Sprite, string, SpriteAnimationData> GrayscaleDataProcessor = new SpriteColorMatrixProcessor(ColorUtils.GrayscaleColorMatrix, 0.05f, 0.05f).WrapAsync<Sprite, string, SpriteAnimationData>();

        private DisposablePool dispPool;
        private SpriteAnimationCache grayscaleCache;
        private AsyncDataProcessorMultiplexer<Sprite, string, Sprite.Animation> grayscaleMux;

        public DemoBooster(Vector2 pos) : base(pos, default, false) {
            dispPool = DisposablePoolComponent.AddTo(this);

            //Create dynamic processing stack
            //NOTE: In real world use cases, you would only do this once in a sort of global controller, not in every entity
            grayscaleMux = dispPool.Add(new AsyncDataProcessorMultiplexer<Sprite, string, Sprite.Animation>("pldemo-grayscale-mux",
                null,
                grayscaleCache = dispPool.Add(new SpriteAnimationCache(dispPool.Add(new TextureScope("pldemo-grayscale", ProcedurlineModule.TextureManager.StaticScope)),
                    new SpriteAnimationDataProcessor(GrayscaleDataProcessor, (_, k, _) => grayscaleCache.GetScopedData(k).TextureScope)
                ))
            ));

            dispPool.Add(ProcedurlineModule.SpriteManager.DynamicAnimationMixer.AddProcessor(100000,
                new AsyncDataProcessorFilter<Sprite, string, Sprite.Animation>(grayscaleMux, sprite => {
                    return sprite.Entity is Player;
                })
            ));

            dispPool.Add(ProcedurlineModule.PlayerManager.HairColorProcessor.AddProcessor(100000,
                new DelegateDataProcessor<Player, VoidBox, PlayerHairColorData>(processData: (Player player, DataScopeKey _, VoidBox _, ref PlayerHairColorData color) => {
                    if(grayscaleMux.MuxIndex == 0) return false;
                    color.ApplyMatrix(ColorUtils.GrayscaleColorMatrix);
                    return true;
                })
            ));
        }

        protected override Sprite ProcessSprite(Sprite origSprite) {
            if(SPRITE == null) SPRITE = new DerivedSprite("pldemo-booster", origSprite, GrayscaleDataProcessor, grayscaleCache.TextureScope);
            return SPRITE.Clone();
        }

        protected override BoostType? OnPlayerEnter(Player player) {
            Logger.Log("PLdemo", "Player entered booster!");
            return BoostType.GREEN_BOOST;
        }

        protected override void PlayerBoosted(Player player, Vector2 dir) {
            Logger.Log("PLdemo", "Player boosted while in booster!");

            //Toggle grayscale by changing dynamic processor mux index
            grayscaleMux.MuxIndex = (grayscaleMux.MuxIndex + 1) % 2;

            base.PlayerBoosted(player, dir);
        }

        protected override string BoostSFX => SFX.char_mad_death_golden;
    }
}