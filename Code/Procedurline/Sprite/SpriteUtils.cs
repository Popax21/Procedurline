using System;
using System.Reflection;
using System.Collections.Generic;

using MonoMod.Utils;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class SpriteUtils {

        private static readonly PropertyInfo Sprite_CurrentAnimationID = typeof(Sprite).GetProperty(nameof(Sprite.CurrentAnimationID));
        private static readonly PropertyInfo Sprite_CurrentAnimationFrame = typeof(Sprite).GetProperty(nameof(Sprite.CurrentAnimationFrame));
        private static readonly PropertyInfo Sprite_LastAnimationID = typeof(Sprite).GetProperty(nameof(Sprite.LastAnimationID));

        private static readonly FieldInfo Sprite_animations = typeof(Sprite).GetField("animations", PatchUtils.BindAllInstance);
        private static readonly FieldInfo Sprite_currentAnimation = typeof(Sprite).GetField("currentAnimation", PatchUtils.BindAllInstance);
        private static readonly FieldInfo Sprite_animationTimer = typeof(Sprite).GetField("animationTimer", PatchUtils.BindAllInstance);

        private static readonly Func<Sprite, Sprite> Sprite_CreateClone = typeof(Sprite).GetMethod("CreateClone", PatchUtils.BindAllInstance).CreateDelegate<Func<Sprite, Sprite>>();
        private static readonly Func<Sprite, Sprite, Sprite> Sprite_CloneInto = typeof(Sprite).GetMethod("CloneInto", PatchUtils.BindAllInstance).CreateDelegate<Func<Sprite, Sprite, Sprite>>();
        private static readonly Action<Sprite, MTexture> Sprite_SetFrame = typeof(Sprite).GetMethod("SetFrame", PatchUtils.BindAllInstance).CreateDelegate<Action<Sprite, MTexture>>();

        /// <summary>
        /// Clones the sprite. This is a simple proxy of the vanilla <c>Sprite.CreateClone()</c> method.
        /// </summary>
        public static Sprite Clone(this Sprite sprite) => Sprite_CreateClone(sprite);

        /// <summary>
        /// Clones the sprite into the given target sprite. This is a simple proxy of the vanilla <c>Sprite.CloneInto()</c> method.
        /// </summary>
        public static Sprite CloneInto(this Sprite sprite, Sprite target) => Sprite_CloneInto(sprite, target);

        public static void SetAnimationDict(this Sprite sprite, Dictionary<string, Sprite.Animation> val) => Sprite_animations.SetValue(sprite, val);
        public static Sprite.Animation GetCurrentAnimation(this Sprite sprite) => (Sprite.Animation) Sprite_currentAnimation.GetValue(sprite);
        public static float GetAnimationTimer(this Sprite sprite) => (float) Sprite_animationTimer.GetValue(sprite);
        public static void SetAnimationTimer(this Sprite sprite, float val) => Sprite_animationTimer.SetValue(sprite, val);

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
                if(!sprite.Animations.TryGetValue(curAnim, out Sprite.Animation anim)) {
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
                    } else if(anim.Goto != null) {
                        //We now have a Goto, utilize it
                        sprite.Play(anim.Goto.Choose(), true, false);
                    } else {
                        Sprite_CurrentAnimationFrame.SetValue(sprite, anim.Frames.Length - 1);
                        sprite.SetFrame(anim.Frames[anim.Frames.Length - 1]);
                    }
                }
            }
        }

        /// <summary>
        /// Transfers this sprite's current animation ID and frame to the given target sprite. This can be used to seamlessly switch between two sprites with identical animations at runtime.
        /// </summary>
        public static void TransferAnimation(this Sprite sprite, Sprite target) {
            //Get the current animation ID
            string curAnim = sprite.CurrentAnimationID;
            if(!sprite.Animating || string.IsNullOrEmpty(curAnim)) curAnim = sprite.LastAnimationID;

            //Get the target's corresponding animation
            if(!target.Animations.TryGetValue(curAnim, out Sprite.Animation anim)) {
                Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Trying to transfer currently playing sprite animation '{curAnim}' [sprite '{ProcedurlineModule.SpriteManager?.GetSpriteID(sprite) ?? sprite.Path ?? "?????"}'] to target [sprite '{ProcedurlineModule.SpriteManager?.GetSpriteID(target) ?? target.Path ?? "?????"}'] which doesn't have it!");
                target.Stop();
                return;
            }

            if(sprite.Animating && !string.IsNullOrEmpty(sprite.CurrentAnimationID)) {
                //Transfer over animation ID, frame index and animation timer
                //Clamp the frame index to the new animation frame length
                target.Play(curAnim, true, false);
                target.SetAnimationFrame(Calc.Clamp(sprite.CurrentAnimationFrame, 0, anim.Frames.Length) - 1);
                Sprite_animationTimer.SetValue(target, Sprite_animationTimer.GetValue(sprite));
            } else if(!string.IsNullOrEmpty(curAnim)) {
                //Check if there now is a Goto
                if(anim.Goto != null) {
                    target.Play(anim.Goto.Choose(), true, false);
                } else {
                    target.Stop();
                    Sprite_CurrentAnimationFrame.SetValue(target, anim.Frames.Length - 1);
                    Sprite_LastAnimationID.SetValue(target, curAnim);
                    target.SetFrame(anim.Frames[anim.Frames.Length - 1]);
                }
            } else {
                //No animation is currently playing
                target.Stop();
                target.Texture = null;
            }
        }
    }
}