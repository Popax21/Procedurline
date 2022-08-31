using System;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements a <see cref="IDataProcessor{T, I, D}" /> which can be used to multiplex between different sprites when used as an animation mixer
    /// </summary>
    public class SpriteMultiplexer : DataScopeMultiplexer<Sprite>, IDataProcessor<Sprite, string, Sprite.Animation> {
        private Sprite[] sprites;

        public SpriteMultiplexer(string name, int count) : base(name, count) {}
        public SpriteMultiplexer(string name, params Sprite[] sprites) : base(name, sprites.Length) => this.sprites = sprites;

        public override void RegisterScopes(Sprite target, DataScopeKey key) {
            lock(LOCK) {
                base.RegisterScopes(target, key);

                //If the sprite is a CustomSprite, register its scopes
                if(this[MuxIndex] is CustomSprite customSprite) customSprite.RegisterScopes(key);
            }
        }

        public virtual bool ProcessData(Sprite target, DataScopeKey key, string id, ref Sprite.Animation anim) {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("SpriteMultiplexer");

                //Replace with the sprite's animation
                Sprite.Animation muxAnim = this[MuxIndex]?.GetOriginalAnimation(id);
                if(anim == muxAnim) return false;
                anim = muxAnim;
                return true;
            }
        }

        /// <summary>
        /// Accesses the sprite with the specified index. Invalidates the corresponding index scope on assignment.
        /// </summary>
        public virtual Sprite this[int idx] {
            get {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("SpriteMultiplexer");
                    return sprites[idx];
                }
            }
            set {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("SpriteMultiplexer");
                    sprites[idx] = value;
                    IndexScopes[idx].Invalidate();
                }
            }
        }
    }
}