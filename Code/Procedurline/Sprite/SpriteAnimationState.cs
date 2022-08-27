using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents the current animation state of a sprite. It can be created from or applied to a given <see cref="Sprite" />
    /// </summary>
    public struct SpriteAnimationState {
        /// <summary>
        /// Transfers this sprite's current animation ID and frame to the given target sprite. This can be used to seamlessly switch between two sprites with identical animations at runtime.
        /// </summary>
        public static void TransferAnimation(Sprite from, Sprite to) => new SpriteAnimationState(from).Apply(to);

        public Vector2 Position;
        public Vector2? Justify;
        public string CurrentAnimationID, LastAnimationID;
        public int CurrentAnimationFrame;
        public float AnimationTimer;

        /// <summary>
        /// Creates a new <see cref="SpriteAnimationState" /> by capturing the current state of a given sprite.
        /// </summary>
        public SpriteAnimationState(Sprite sprite) {
            Position = sprite.Position;
            Justify = sprite.Justify;
            CurrentAnimationID = sprite.CurrentAnimationID;
            LastAnimationID = sprite.LastAnimationID;
            CurrentAnimationFrame = sprite.CurrentAnimationFrame;
            AnimationTimer = sprite.GetAnimationTimer();
        }

        /// <summary>
        /// Applies this <see cref="SpriteAnimationState" /> to a given target sprite. Returns <c>false</c> if the animation state could not be applied.
        /// </summary>
        public bool Apply(Sprite target) {
            //Transfer over basic parameters
            target.Position = Position;
            target.Justify = Justify;
            target.SetLastAnimationID(LastAnimationID);

            //Get the current animation ID
            string curAnim = CurrentAnimationID;
            if(string.IsNullOrEmpty(curAnim)) curAnim = LastAnimationID;

            //Get the target's corresponding animation
            if(!(target.GetProcessedAnimation(curAnim) is Sprite.Animation anim)) {
                Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"Trying to apply sprite animation '{curAnim}' to target [sprite '{ProcedurlineModule.SpriteManager?.GetSpriteID(target) ?? target.Path ?? "?????"}'] which doesn't have it!");
                target.Stop();
                return false;
            }

            if(!string.IsNullOrEmpty(CurrentAnimationID)) {
                //Transfer over animation ID, frame index and animation timer
                //Clamp the frame index to the new animation frame length
                target.Play(curAnim, true, false);
                target.SetAnimationFrame(Calc.Clamp(CurrentAnimationFrame, 0, anim.Frames.Length) - 1);
                target.SetAnimationTimer(AnimationTimer);
            } else if(!string.IsNullOrEmpty(curAnim)) {
                //Check if there now is a Goto
                if(anim.Goto != null) {
                    target.Play(anim.Goto.Choose(), true, false);
                } else {
                    target.Stop();
                    target.SetAnimationFrame(anim.Frames.Length - 1);
                    target.SetFrame(anim.Frames[anim.Frames.Length - 1]);
                }
            } else {
                //No animation is currently playing
                target.Stop();
                target.Texture = null;
            }
            return true;
        }
    }
}