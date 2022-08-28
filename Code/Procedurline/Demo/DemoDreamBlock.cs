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
            if((player.Position.X < Left || Right < player.Position.X) && Top <= player.Position.Y && player.Position.Y <= Bottom) player.Speed.X *= -1;
            if((player.Position.Y < Top || Bottom < player.Position.Y) && Left <= player.Position.X && player.Position.X <= Right) player.Speed.Y *= -1;
            player.Position = oldPos;
            return Player.StDreamDash;
        }

        protected override string BounceSFX => SFX.char_bad_booster_throw;
    }
}