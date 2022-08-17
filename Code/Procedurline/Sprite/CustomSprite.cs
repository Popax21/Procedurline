using System.Threading.Tasks;

using Monocle;

namespace Celeste.Mod.Procedurline {
    //FIXME add docs
    public abstract class CustomSprite : Sprite {
        public readonly string SpriteID;

        protected CustomSprite(string spriteId, Atlas atlas, string path) : base(atlas, path) => SpriteID = spriteId;

        public abstract Monocle.Sprite CreateCopy();

        public abstract void RegisterSprite(ProcessedSprite procSprite);
        public abstract void UnregisterSprite(ProcessedSprite procSprite);

        public virtual void RegisterScopes(DataScopeKey key) {}
    }

    public abstract class CustomSpriteAnimation : Sprite.Animation {
        public readonly object LOCK = new object();
        public readonly string AnimationID;

        protected CustomSpriteAnimation(string animId) => AnimationID = animId;

        public abstract Task UpdateAnimation();
    }
}