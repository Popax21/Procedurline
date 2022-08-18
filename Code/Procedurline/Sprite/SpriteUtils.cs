using System;
using System.Reflection;

using MonoMod.Utils;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class SpriteUtils {
        private static readonly Func<Sprite, Sprite> Sprite_CreateClone = typeof(Sprite).GetMethod("CreateClone", BindingFlags.NonPublic | BindingFlags.Instance).CreateFastDelegate().CastDelegate<Func<Sprite, Sprite>>();
        private static readonly Func<Sprite, Sprite, Sprite> Sprite_CloneInto = typeof(Sprite).GetMethod("CloneInto", BindingFlags.NonPublic | BindingFlags.Instance).CreateFastDelegate().CastDelegate<Func<Sprite, Sprite, Sprite>>();

        /// <summary>
        /// Clones the sprite. This is a simple proxy of the vanilla <c>Sprite.CreateClone()</c> method.
        /// </summary>
        public static Sprite Clone(this Sprite sprite) => Sprite_CreateClone(sprite);

        /// <summary>
        /// Clones the sprite into the given target sprite. This is a simple proxy of the vanilla <c>Sprite.CloneInto()</c> method.
        /// </summary>
        public static Sprite CloneInto(this Sprite sprite, Sprite target) => Sprite_CloneInto(sprite, target);
    }
}