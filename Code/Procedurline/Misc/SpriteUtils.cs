using System;
using System.Reflection;

using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class SpriteUtils {
        private static readonly Func<Sprite, Sprite> Sprite_CreateClone = (Func<Sprite, Sprite>) typeof(Sprite).GetMethod("CreateClone", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(Func<Sprite, Sprite>));
        private static readonly Func<Sprite, Sprite, Sprite> Sprite_CloneInto = (Func<Sprite, Sprite, Sprite>) typeof(Sprite).GetMethod("CloneInto", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(Func<Sprite, Sprite, Sprite>));

        /// <summary>
        /// Clones the sprite
        /// </summary>
        public static Sprite Clone(this Sprite sprite) => Sprite_CreateClone(sprite);

        /// <summary>
        /// Clones the sprite into the given target sprite
        /// </summary>
        public static Sprite CloneInto(this Sprite sprite, Sprite target) => Sprite_CloneInto(sprite, target);
    }
}