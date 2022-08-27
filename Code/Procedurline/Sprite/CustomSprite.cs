using System;
using System.Linq;
using System.Threading.Tasks;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a custom sprite, which is the base class of all procedurally defined sprites like <see cref="DerivedSprite" /> or <see cref="MixerSprite" />.
    /// Custom sprites have the ability to hook into some of Procedurline's sprite processing mechanisms, by being able to provide custom copy and scope registration functions.
    /// <b>All updates to sprite data must happen on the main thread!</b>
    /// </summary>
    /// <seealso cref="CustomSpriteAnimation" />
    /// <seealso cref="DerivedSprite" />
    /// <seealso cref="MixerSprite" />
    public abstract class CustomSprite : Sprite, IScopedTarget {
        public readonly string SpriteID;

        protected CustomSprite(string spriteId, Atlas atlas, string path) : base(atlas, path) => SpriteID = spriteId;

        /// <summary>
        /// Inits this <see cref="CustomSprite" />'s animations by duplicating them from an original sprite and passing them through a wrapper function.
        /// </summary>
        protected void InitWrapped(Sprite origSprite, Func<string, Sprite.Animation, Sprite.Animation> wrapper) {
            origSprite.CloneIntoUnsafe(this);
            foreach(string animId in Animations.Keys.ToArray()) Animations[animId] = wrapper(animId, Animations[animId]);
            this.ReloadAnimation();
        }

        /// <summary>
        /// Creates a copy of this sprite, which should act exactly like the original sprite, but still have its own current animation parameters / etc. This is used by Procedurline's <c>Sprite.CreateClone</c> hook to support cloning custom sprites.
        /// </summary>
        public virtual Monocle.Sprite CreateCopy() => new ProxySprite(SpriteID, this);

        /// <summary>
        /// Called when a <see cref="SpriteHandler" /> is created for this sprite. This means that it is currently in a scene, and will be actively used. Can be called multiple times with different <see cref="SpriteHandler" />s.
        /// </summary>
        public virtual void RegisterHandler(SpriteHandler handler) {}

        /// <summary>
        /// Called when a <see cref="SpriteHandler" /> is disposed for this sprite. This means that it is no longer in a scene, and will no longer be used. Can be called multiple times with different <see cref="SpriteHandler" />s.
        /// </summary>
        public virtual void UnregisterHandler(SpriteHandler handler) {}

        /// <summary>
        /// Register all appropiate <see cref="DataScope" />s which this sprite belongs to on the given key. Once it becomes invalidated, users should take this as an indication that all data obtained from the sprite('s animations) might have changed.
        /// <b>Note that you must forward this event to any custom sprites your own implementation depends on.</b>
        /// </summary>
        public virtual void RegisterScopes(DataScopeKey key) => ProcedurlineModule.SpriteManager.RegisterSpriteScopes(this, key, true);
    }

    /// <summary>
    /// Represents a custom sprite animation. Custom sprite animations have the capability to hook into Procedurline's sprite processing system, by being able to process data dynamically before it's used.
    /// Note that custom sprite animations by themselves <b>do not allow one to dynamically change data after it has finished processing</b>. If this is needed, you need to implement a proper scope invalidation mechanism using <see cref="CustomSprite.RegisterScopes" />.
    /// <b>All updates to animation data must happen on the main thread!</b>
    /// </summary>
    /// <seealso cref="CustomSprite" />
    public abstract class CustomSpriteAnimation : Sprite.Animation {
        public readonly string AnimationID;
        private MTexture[] DummyFrames;

        protected CustomSpriteAnimation(string animId) {
            AnimationID = animId;

            Delay = 0f;
            Goto = null;
            Frames = DummyFrames = new MTexture[] { ProcedurlineModule.TextureManager.EmptyTexture.MTexture };
        }

        /// <summary>
        /// Called when a <see cref="SpriteHandler" /> starts using this animation. Can be called multiple times with different <see cref="SpriteHandler" />s.
        /// </summary>
        public virtual void RegisterHandler(SpriteHandler handler) => OnReload += handler.AnimationReloaded;

        /// <summary>
        /// Called when a <see cref="SpriteHandler" /> stops using this animation. Can be called multiple times with different <see cref="SpriteHandler" />s.
        /// </summary>
        public virtual void UnregisterHandler(SpriteHandler handler) => OnReload -= handler.AnimationReloaded;

        /// <summary>
        /// Called before this animation's data is used, either by <see cref="SpriteManager.GetAnimationData" /> or by <see cref="SpriteHandler" />s.
        /// <see cref="SpriteHandler" />s will still play the current animation data while this task is running if asynchronous static processing is enabled in the settings, if you want to instead always block the game until your processing is complete call <see cref="GlobalManager.BlockEngineOnTask" />.
        /// Once you have new animation data, you can either use the <see cref="ReplaceData" /> helper method to swap it with your current animation data, or you can replace it yourself <b>on the main thread</b> and then call <see cref="Reload" />.
        /// If your animation data can become invalidated some time after this task completes, you <b>HAVE</b> to utilize the <see cref="CustomSprite.RegisterScopes" /> mechanism to notify potential users of the invalidation. <b>DO NOT</b> call <see cref="Reload" /> to specifically accomplish this.
        /// <b>NOTE:</b> if you want manually kickstart processing of a custom sprite's animation, because Procedurline won't do so yourself, you <b>HAVE TO</b> call <see cref="SpriteManager.ProcessCustomAnimation" /> instead of the method directly. <b>Only call this method directly if you are yourself inside a custom sprite animation processing task!</b> 
        /// </summary>
        public abstract Task ProcessData();

        /// <summary>
        /// Reloads the animation. This causes all sprites using it to reload the new animation data (assuming they have an attached <see cref="SpriteHandler" />), and invokes <see cref="OnReload" />.
        /// <b>DO NOT USE THIS MECHANISM TO HANDLE INVALIDATION!</b> This will not work properly, as <b>dynamic sprite processing does not have any reload handlers</b>. Use <see cref="CustomSprite.RegisterScopes" /> if your processed data can change.
        /// </summary>
        public virtual void Reload() => MainThreadHelper.Do(() => OnReload?.Invoke(this));

        /// <summary>
        /// Helper method which replaces this animation's data, and then reloads the animation.
        /// </summary>
        protected void ReplaceData(Sprite.Animation anim) {
            MainThreadHelper.Do(() => {
                Delay = anim?.Delay ?? 0f;
                Goto = anim?.Goto ?? null;
                Frames = anim?.Frames ?? DummyFrames;
                Reload();
            });
        }

        public event Action<CustomSpriteAnimation> OnReload;
    }
}