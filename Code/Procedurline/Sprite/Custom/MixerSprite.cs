using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements a <see cref="CustomSprite" /> which applies an <see cref="IDataProcessor{T, I, D}" /> to mix arbitrary animations together as one animation.
    /// </summary>
    public sealed class MixerSprite : InvalidatableSprite {
        private new sealed class Animation : InvalidatableSprite.Animation, IProxySpriteAnimation {
            public new readonly MixerSprite Sprite;

            private Task mixerTask;
            private ulong mixerTaskValToken;
            private Sprite.Animation mixedAnim;

            public Animation(MixerSprite sprite, string animId) : base(sprite, animId) => Sprite = sprite;

            public override Task ProcessData() {
                lock(LOCK) {
                    //Check the animation's validity
                    if(CheckValidity(out ulong valToken, out CancellationToken token)) return mixerTask;

                    //Check if there already is a valid mixer task
                    if(mixerTask != null && IsTokenValid(mixerTaskValToken)) return mixerTask;

                    //Start new mixer task
                    mixerTaskValToken = valToken;
                    return mixerTask = InvokeMixer(valToken, token);
                }
            }

            private async Task InvokeMixer(ulong valToken, CancellationToken token) {
                //Run the mixer
                AsyncRef<Sprite.Animation> animRef = new AsyncRef<Sprite.Animation>();
                using(DataScopeKey scopeKey = CloneScopeKey(valToken)) {
                    if(scopeKey == null) return;
                    await Sprite.Mixer.ProcessDataAsync(Sprite, scopeKey, AnimationID, animRef, token);
                }

                Sprite.Animation anim = animRef.Data;
                if(anim == null) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"MixerSprite '{Sprite.SpriteID}' mixer did not return an animation for animation '{AnimationID}'!");

                //If it's a custom animation, wait for it to finish processing
                if(anim is CustomSpriteAnimation customAnim) await customAnim.ProcessData();

                //Replace the animation
                lock(LOCK) {
                    lock(Sprite.LOCK) {
                        //Mark the animation as valid
                        if(!MarkValid(valToken)) return;

                        //Replace the animation
                        ReplaceData(anim);
                        mixedAnim = anim;
                    }
                }
            }

            public Sprite.Animation ProxiedAnimation {
                get {
                    lock(LOCK) return mixedAnim;
                }
            }
        }

        public readonly IAsyncDataProcessor<Sprite, string, Sprite.Animation> Mixer;

        public MixerSprite(string spriteId, Sprite template, IAsyncDataProcessor<Sprite, string, Sprite.Animation> mixer) : this(spriteId, template.Animations.Keys.ToArray(), mixer) {
            SpriteAnimationState.TransferState(template, this);
        }

        public MixerSprite(string spriteId, string[] animIds, IAsyncDataProcessor<Sprite, string, Sprite.Animation> mixer) : base(spriteId, null, string.Empty) {
            Mixer = mixer;
            foreach(string animId in animIds) Animations.Add(animId, new Animation(this, animId));
        }

        public override void RegisterScopes(DataScopeKey key) {
            base.RegisterScopes(key);

            //Register mixers scopes
            Mixer.RegisterScopes(this, key);
        }
    }
}