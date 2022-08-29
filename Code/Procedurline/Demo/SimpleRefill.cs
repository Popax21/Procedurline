using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline.Demo {
    public class SimpleRefill : CustomRefill {
        public SimpleRefill(Vector2 position) : base(position, Color.BlueViolet, false, false) {}

        protected override bool OnTouch(Player player) {
            if(player.Dashes > 0) return false;

            //Randomize the number of dashes
            player.Dashes = Calc.Random.Next(1, 3);
            return true;
        }
    }
}