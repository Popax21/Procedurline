using System;
using System.Threading;
using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements a <see cref="CustomSprite" /> which contains some common helper code which can be used to deal with sprite data scope invalidation.
    /// It provides animations a mechanism to check their validity, and be notified when they become invalidated.
    /// </summary>
    /// <seealso cref="CustomSprite" />
    /// <seealso cref="DerivedSprite" />
    /// <seealso cref="MixerSprite" />
    public abstract class InvalidatableSprite : CustomSprite, IDisposable {
        public new abstract class Animation : CustomSpriteAnimation {
            public readonly InvalidatableSprite Sprite;
            private bool isValid = false;
            private ulong curValidityToken = 0;

            protected Animation(InvalidatableSprite sprite, string animId) : base(animId) => Sprite = sprite;

            /// <summary>
            /// Checks the animation's validity. If it's invalid, you have to redo any processing you did, and discard the old data.
            /// The obtained validity token can be passed to <see cref="MarkValid" /> once processing has completed.
            /// </summary>
            protected bool CheckValidity(out ulong valToken) {
                lock(Sprite.LOCK) {
                    if(Sprite.isDisposed) throw new ObjectDisposedException("InvalidatableSprite");

                    Sprite.EnsureKeyValidity();
                    valToken = !isValid ? curValidityToken : default;
                    return isValid;
                }
            }

            /// <summary>
            /// A wrapper around <see cref="CheckValidity(out ulong)" /> which also obtains the cancellation token.
            /// </summary>
            protected bool CheckValidity(out ulong valToken, out CancellationToken token) {
                lock(Sprite.LOCK) {
                    if(CheckValidity(out valToken)) {
                        token = default;
                        return true;
                    } else {
                        token = Sprite.CancellationToken;
                        return false;
                    }
                }
            }

            /// <summary>
            /// Checks if the given validity token is still valid
            /// </summary>
            protected bool IsTokenValid(ulong valToken) {
                lock(Sprite.LOCK) {
                    Sprite.EnsureKeyValidity();
                    return !isValid && valToken == curValidityToken;
                }
            }

            /// <summary>
            /// Marks the animation as valid. Invoke this method once your processing has completed, using the token you obtained by calling <see cref="CheckValidity(out ulong)" />.
            /// If the token has change in the mean time, this means that during processing another invalidation has occured, and as such the animation can't truly be considered valid.
            /// </summary>
            /// <returns>
            /// <c>true</c> if the animation has succesfully been marked as valid, <c>false</c> otherwise.
            /// </returns>
            protected bool MarkValid(ulong valToken) {
                lock(Sprite.LOCK) {
                    if(!IsTokenValid(valToken)) return false;
                    isValid = true;
                    return true;
                }
            }

            /// <summary>
            /// Clones the sprite's <see cref="DataScopeKey" /> if the given validity token is still valid, otherwise returns <c>null</c>. This function can be used when one needs to pass the scope key to e.g. a <see cref="IDataProcessor{T, I, D}" />.
            /// </summary>
            protected DataScopeKey CloneScopeKey(ulong valToken) {
                lock(Sprite.LOCK) {
                    if(!IsTokenValid(valToken)) return null;
                    return Sprite.ScopeKey.Clone();
                }
            }

            /// <summary>
            /// Called when the animation becomes invalidated. Use this to cancel any currently ongoing processing. 
            /// When called, <see cref="InvalidatableSprite.LOCK" /> is held.
            /// </summary>
            protected internal virtual void Invalidate() {
                isValid = false;
                curValidityToken = unchecked(curValidityToken + 1);
            }
        }

        protected readonly object LOCK = new object();

        private bool isDisposed;
        public bool IsDisposed { get { lock(LOCK) return isDisposed; } }

        public readonly DataScopeKey ScopeKey;
        private bool scopeKeyValid;

        private CancellationTokenSource tokenSrc;

        protected InvalidatableSprite(string spriteId, Atlas atlas, string path) : base(spriteId, atlas, path) {
            ScopeKey = new DataScopeKey();
            ScopeKey.OnInvalidate += OnInvalidate;
        }

        public virtual void Dispose() {
            lock(LOCK) {
                if(isDisposed) return;
                isDisposed = true;

                //Cancel tasks
                tokenSrc?.Cancel();
                tokenSrc = null;

                //Dispose the scope key
                ScopeKey?.Dispose();
            }
        }

        /// <summary>
        /// Checks the scope key for validity. If it is invalid, re-registers the key's scopes.
        /// </summary>
        public void EnsureKeyValidity() {
            lock(LOCK) {
                //Check if the scope key is valid
                lock(ScopeKey.VALIDITY_LOCK) {
                    if(scopeKeyValid && ScopeKey.IsValid) return;
                }

                //If we thought the scope key was valid, manually invoke the invalidation callback
                if(scopeKeyValid) OnInvalidate(ScopeKey);

                //Re-register the scope key
                retryKey:;
                ScopeKey.Reset();
                RegisterScopes(ScopeKey);
                lock(ScopeKey.VALIDITY_LOCK) {
                    if(!ScopeKey.IsValid) {
                        //Could happen because of race conditions
                        goto retryKey;
                    }

                    //Mark the scope key as valid
                    //As soon as we leave this lock statemement, the key could become invalid at any time
                    //But if it does, then OnInvalidate will be called, immediatly marking it as invalid again
                    scopeKeyValid = true;
                }
            }
        }

        public override void RegisterScopes(DataScopeKey key) {
            if(isDisposed) throw new ObjectDisposedException("InvalidatableSprite");
            base.RegisterScopes(key);
        }

        private void OnInvalidate(IScopedInvalidatable inval) {
            lock(LOCK) {
                //Mark the key as invalid
                scopeKeyValid = false;

                //Invalidate animations
                foreach(Sprite.Animation anim in Animations.Values) (anim as Animation)?.Invalidate();

                //Cancel tasks
                tokenSrc?.Cancel();
                tokenSrc = null;
            }
        }

        /// <summary>
        /// Returns a token which can be utilized to cancel asynchronous tasks once the sprite has been invalidated.
        /// </summary>
        protected CancellationToken CancellationToken {
            get {
                lock(LOCK) {
                    if(isDisposed) throw new ObjectDisposedException("InvalidatableSprite");
                    tokenSrc ??= new CancellationTokenSource();
                    return tokenSrc.Token;
                }
            }
        }
    }
}