using System;
using System.Reflection;

using MonoMod.Utils;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class SpriteUtils {
        private static readonly Func<Sprite, Sprite> Sprite_CreateClone = typeof(Sprite).GetMethod("CreateClone", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate<Func<Sprite, Sprite>>();
        private static readonly Func<Sprite, Sprite, Sprite> Sprite_CloneInto = typeof(Sprite).GetMethod("CloneInto", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate<Func<Sprite, Sprite, Sprite>>();
        private static readonly Action<Sprite, MTexture> Sprite_SetFrame = typeof(Sprite).GetMethod("SetFrame", BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate<Action<Sprite, MTexture>>();

        private static readonly FieldInfo Sprite_currentAnimation = typeof(Sprite).GetField("currentAnimation", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Clones the sprite. This is a simple proxy of the vanilla <c>Sprite.CreateClone()</c> method.
        /// </summary>
        public static Sprite Clone(this Sprite sprite) => Sprite_CreateClone(sprite);

        /// <summary>
        /// Clones the sprite into the given target sprite. This is a simple proxy of the vanilla <c>Sprite.CloneInto()</c> method.
        /// </summary>
        public static Sprite CloneInto(this Sprite sprite, Sprite target) => Sprite_CloneInto(sprite, target);

        /// <summary>
        /// Clones the sprite into the given target sprite, but skips the checks which disallow usage of this method for custom sprites, and does not copy over sprite IDs. <b>This can break things</b>, and should only be used by the implementations of these custom sprites.
        /// </summary>
        public static Sprite CloneIntoUnsafe(this Sprite sprite, Sprite target) {
            try {
                UnsafeCloneSource = sprite;
                UnsafeCloneTarget = target;
                return sprite.CloneInto(target);
            } finally {
                UnsafeCloneSource = UnsafeCloneTarget = null;
            }
        }
        internal static Sprite UnsafeCloneSource, UnsafeCloneTarget;

        /// <summary>
        /// Sets the sprite's current animation frame. This is a simple proxy of the vanilla <c>Sprite.SetFrame()</c> method.
        /// </summary>
        public static void SetFrame(this Sprite sprite, MTexture frame) => Sprite_SetFrame(sprite, frame);

        /// <summary>
        /// Reloads the sprite's current animation. This can be used when you changed some animation data on the fly, and want it to take effect immediatly.
        /// </summary>
        public static void ReloadAnimation(this Sprite sprite, string animId = null) {
            //Get the currently playing animation
            string curAnim = sprite.CurrentAnimationID;
            if(!sprite.Animating || string.IsNullOrEmpty(curAnim)) curAnim = sprite.LastAnimationID;
            if(!string.IsNullOrEmpty(curAnim) && (animId == null || curAnim.Equals(animId, StringComparison.OrdinalIgnoreCase))) {
                sprite.Texture = null;

                //Get the new animation
                if(!(ProcedurlineModule.SpriteManager.GetProcessedAnimation(sprite, curAnim) is Sprite.Animation anim)) {
                    //The animation we were playing got deleted
                    Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Currently playing sprite animation '{curAnim}' [sprite '{ProcedurlineModule.SpriteManager?.GetSpriteID(sprite) ?? sprite.Path ?? "?????"}'] doesn't exit anymore after reload!");
                    sprite.Stop();
                } else {
                    if(sprite.Animating && !string.IsNullOrEmpty(sprite.CurrentAnimationID)) {
                        //Replace the internal animation reference
                        Sprite_currentAnimation.SetValue(sprite, anim);

                        //Clamp frame index if the animation length shrunk
                        if(sprite.CurrentAnimationFrame < anim.Frames.Length) {
                            sprite.Texture = anim.Frames[sprite.CurrentAnimationFrame];
                        }
                    } else if(sprite.CurrentAnimationFrame < anim.Frames.Length-1) {
                        //The animation's length increased, so continue playing the new remainder of the animation
                        int oldFrame = sprite.CurrentAnimationFrame;
                        sprite.Play(curAnim, true, false);
                        sprite.SetAnimationFrame(oldFrame);
                    } else if(anim.Goto != null) {
                        //We now have a Goto, utilize it
                        sprite.Play(anim.Goto.Choose(), true, false);
                    } else sprite.SetFrame(anim.Frames[anim.Frames.Length - 1]);
                }
            }
        }
    }
}