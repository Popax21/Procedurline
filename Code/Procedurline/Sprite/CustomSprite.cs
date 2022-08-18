using System;
using System.Threading.Tasks;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a custom sprite, which is the base class of all procedurally defined sprites like <see cref="StaticSprite" /> or <see cref="MixerSprite" />.
    /// Custom sprites can hook into Procedurline's sprite managment and display routines to implement custom functionality, by providing e.g. custom cloning functions (<see cref="CreateCopy" />), notification functions when added to scenes (<see cref="RegisterSprite" />, <see cref="UnregisterSprite" />), or data scope registraction functions to add your own scopes for the purposes of invalidation (<see cref="RegisterScopes" />), which is also accomplished by implementing <see cref="IScopedInvalidatable" /> (<b>the usual disclaimers still apply!</b>).
    /// Note that if you are implementing sprite types which modify existing sprites, you should forward some of these calls to them if they are also <see cref="CustomSprite" />s.
    /// <b>Any updates of sprite data must occur on the main thread to prevent race conditions!</b>
    /// </summary>
    /// <seealso cref="CustomSpriteAnimation" />
    /// <seealso cref="StaticSprite" />
    /// <seealso cref="MixerSprite" />
    public abstract class CustomSprite : Sprite {
        public readonly string SpriteID;

        protected CustomSprite(string spriteId, Atlas atlas, string path) : base(atlas, path) => SpriteID = spriteId;

        /// <summary>
        /// Creates a copy of this sprite, which should act exactly like the original sprite, but still have its own current animation parameters / etc. This is used by Procedurline's <c>Sprite.CreateClone</c> hook to support cloning custom sprites.
        /// </summary>
        public abstract Monocle.Sprite CreateCopy();

        /// <summary>
        /// Called when a <see cref="ProcessedSprite" /> is created for this sprite. This means that it is currently in a scene, and will be actively used. Can be called multiple times with different <see cref="ProcessedSprite" />s.
        /// <b>Note that you must forward this event to any custom sprites your own implementation depends on.</b>
        /// </summary>
        public abstract void RegisterSprite(ProcessedSprite procSprite);

        /// <summary>
        /// Called when a <see cref="ProcessedSprite" /> is disposed for this sprite. This means that it is no longer in a scene, and will no longer be used used. Can be called multiple times with different <see cref="ProcessedSprite" />s.
        /// <b>Note that you must forward this event to any custom sprites your own implementation depends on.</b>
        /// </summary>
        public abstract void UnregisterSprite(ProcessedSprite procSprite);

        /// <summary>
        /// Register all appropiate <see cref="DataScope" />s which this sprite belongs to on the given key. Once it becomes invalidated, users should take this as an indication that all data obtained from the sprite('s animations) might have changed.
        /// <b>Note that you must forward this event to any custom sprites your own implementation depends on.</b>
        /// </summary>
        public virtual void RegisterScopes(DataScopeKey key) {}
    }

    /// <summary>
    /// Represents a custom sprite animation, which should only belong to <see cref="CustomSprite" /> instances.
    /// Custom animations allow one to add additional logic to Procedurline's sprite animation processing, like running their own processing before the animation is displayed (using the <see cref="UpdateData" /> function).
    /// <b>Any updates of animation data must occur on the main thread to prevent race conditions!</b>
    /// </summary>
    /// <seealso cref="CustomSprite" />
    public abstract class CustomSpriteAnimation : Sprite.Animation {
        public readonly string AnimationID;

        protected CustomSpriteAnimation(string animId) => AnimationID = animId;

        /// <summary>
        /// Updates the animation's data asynchronously. Note that this method should cache its processing, and notify potential users of invalidation using <see cref="CustomSprite.RegisterScopes" />
        /// </summary>
        public abstract Task UpdateData();
    }
}