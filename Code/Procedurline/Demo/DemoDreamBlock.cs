using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline.Demo {
    public class DemoDreamBlock : CustomDreamBlock {
        private static readonly DreamBlockData dblockData = new DreamBlockData() {
            DeactivatedBackColor = Color.MidnightBlue, ActivatedBackColor = Color.DarkSlateBlue,
            DeactivatedLineColor = VanillaDeactivatedLineColor, ActivatedLineColor = VanillaActivatedLineColor,
            ParticleColors = VanillaParticleColors
        };

        public DemoDreamBlock(Vector2 position, float width, float height) : base(position, width, height, null, false, false, false, dblockData) {}

        public override void Awake(Scene scene) {
            base.Awake(scene);
            ActivateNoRoutine();
        }

        protected override int OnCollideSolid(Player player, Vector2 oldPos) {
            if(player.Right <= Left || Right <= player.Left) player.Speed.X *= -1;
            if(player.Top <= Top || Bottom <= player.Bottom) player.Speed.Y *= -1;
            player.Position = oldPos;
            player.Play(BounceSFX, null, 0f);
            return Player.StDreamDash;
        }

        protected override string BounceSFX => SFX.char_bad_booster_throw;
    }
}