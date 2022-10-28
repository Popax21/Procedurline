using System;
using System.Reflection;
using System.Collections.Generic;

using MonoMod.Utils;
using Monocle;

namespace Celeste.Mod.Procedurline {
    public static class SpriteUtils {
        private static readonly PropertyInfo Sprite_Animating = typeof(Sprite).GetProperty(nameof(Sprite.Animating));
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

        public static void SetAnimating(this Sprite sprite, bool val) => Sprite_Animating.SetValue(sprite, val);
        public static void SetCurrentAnimationID(this Sprite sprite, string val) => Sprite_CurrentAnimationID.SetValue(sprite, val);
        public static void SetLastAnimationID(this Sprite sprite, string val) => Sprite_LastAnimationID.SetValue(sprite, val);
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
        /// Gets the specified original animation of the sprite, or null if it does not exist. This is a wrapper around <see cref="SpriteHandler.GetOriginalAnimation" />
        /// </summary>
        public static Sprite.Animation GetOriginalAnimation(this Sprite sprite, string animId) {
            if(ProcedurlineModule.SpriteManager.GetSpriteHandler(sprite) is SpriteHandler handler) return handler.GetOriginalAnimation(animId);
            if(sprite.Animations.TryGetValue(animId, out Sprite.Animation anim)) return anim;
            return null;
        }

        /// <summary>
        /// Gets the specified processed animation of the sprite, or null if it does not exist. This is a wrapper around <see cref="SpriteHandler.GetProcessedAnimation" />
        /// </summary>
        public static Sprite.Animation GetProcessedAnimation(this Sprite sprite, string animId) {
            if(ProcedurlineModule.SpriteManager.GetSpriteHandler(sprite) is SpriteHandler handler) return handler.GetProcessedAnimation(animId);
            if(sprite.Animations.TryGetValue(animId, out Sprite.Animation anim)) return anim;
            return null;
        }

        /// <summary>
        /// Sets the sprite's current animation frame. This is a simple proxy of the vanilla <c>Sprite.SetFrame()</c> method.
        /// </summary>
        public static void SetFrame(this Sprite sprite, MTexture frame) => Sprite_SetFrame(sprite, frame);

        /// <summary>
        /// Reloads the sprite's current animation. This can be used when you changed some animation data on the fly, and want it to take effect immediatly.
        /// </summary>
        public static void ReloadAnimation(this Sprite sprite, string animId = null) {
            static MTexture GetFrameSafe(Sprite.Animation anim, int idx) {
                if(idx < 0 || anim.Frames.Length <= idx) return null;
                return anim.Frames[idx];
            }

            //Get the currently playing animation
            string curAnim = sprite.CurrentAnimationID;
            if(string.IsNullOrEmpty(curAnim)) curAnim = sprite.LastAnimationID;
            if(!string.IsNullOrEmpty(curAnim) && (animId == null || curAnim.Equals(animId, StringComparison.OrdinalIgnoreCase))) {
                sprite.Texture = null;

                //Get the new animation
                if(!(sprite.GetProcessedAnimation(curAnim) is Sprite.Animation anim)) {
                    //The animation we were playing got deleted
                    Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Currently playing sprite animation '{curAnim}' [sprite '{ProcedurlineModule.SpriteManager?.GetSpriteID(sprite) ?? sprite.Path ?? "?????"}'] doesn't exit anymore after reload!");
                    sprite.Stop();
                } else {
                    if(!string.IsNullOrEmpty(sprite.CurrentAnimationID)) {
                        //Replace the internal animation reference
                        Sprite_Animating.SetValue(sprite, anim.Delay > 0);
                        Sprite_currentAnimation.SetValue(sprite, anim);

                        //Replace the current frame, clamp frame index if the animation length shrunk
                        if(sprite.CurrentAnimationFrame < anim.Frames.Length) {
                            sprite.SetFrame(GetFrameSafe(anim, sprite.CurrentAnimationFrame));
                        } else {
                            sprite.SetFrame(GetFrameSafe(anim, anim.Frames.Length-1));
                        }
                    } else if(anim.Goto != null) {
                        //We now have a Goto, utilize it
                        sprite.Play(anim.Goto.Choose(), true, false);
                    } else if(sprite.CurrentAnimationFrame < anim.Frames.Length) {
                        //Animation length increased, play rest of animation
                        int frame = sprite.CurrentAnimationFrame;
                        sprite.Play(curAnim, true, false);
                        Sprite_CurrentAnimationFrame.SetValue(sprite, frame);
                        sprite.SetFrame(GetFrameSafe(anim, frame));
                    } else {
                        //Show first/last frame of animation
                        int frame = Math.Sign(Celeste.TimeRate * Celeste.TimeRateB) < 0 ? 0 : (anim.Frames.Length - 1);
                        Sprite_CurrentAnimationFrame.SetValue(sprite, frame);
                        sprite.SetFrame(GetFrameSafe(anim, frame));
                    }
                }
            }
        }

        /// <summary>
        /// Obtains the animation's data in a safe manner. For dynamic sprite animations, this obtains the animation data in a thread safe way.
        /// </summary>
        public static void GetAnimationData(this Sprite.Animation anim, out float animDelay, out Chooser<string> animGoto, out MTexture[] animFrames) {
            if(anim is DynamicSpriteAnimation threadedAnim) {
                lock(threadedAnim.LOCK) {
                    animDelay = threadedAnim.TDelay;
                    animGoto = threadedAnim.TGoto;
                    animFrames = threadedAnim.TFrames;
                }
            } else {
                animDelay = anim.Delay;
                animGoto = anim.Goto;
                animFrames = anim.Frames;
            }
        }
    }
}