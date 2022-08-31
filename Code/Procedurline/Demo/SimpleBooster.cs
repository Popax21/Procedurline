using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Procedurline.Demo {
    public class SimpleBooster : CustomBooster {
        private class MyProcessor : IDisposable, IDataProcessor<Sprite, string, SpriteAnimationData> {
            //Procedurline is multithreaded, so we have to ensure we have a proper locking scheme
            public readonly object LOCK = new object();
            private DataScope dataScope, activeScope;
            private bool isActive;

            public MyProcessor() {
                //We can make this scope transparent, as we'll always register it - it does not affect anything
                dataScope = new DataScope("pldemo-my-processor") { Transparent = true };

                //The active scope however has to be non-transparent - the sprite is treated differently when it has the scope, compared to when it doesn't have it
                activeScope = new DataScope("pldemo-my-processor-active");
            }

            public void Dispose() {
                dataScope.Dispose();
                activeScope.Dispose();
            }

            public void RegisterScopes(Sprite target, DataScopeKey key) {
                //Here, we should register any scopes our target might belong to
                //This allows the caching system to properly track and cache our processed sprite data
                lock(LOCK) {
                    dataScope.RegisterKey(key);

                    //We want to have a seperate scope for when our processor is active, so if we are active, register it
                    //As the active scope is non-transparent, Procedurline knows that our booster has different animation data when it has this scope, compared to when it hasn't
                    if(isActive) activeScope.RegisterKey(key);
                }
            }

            public bool ProcessData(Sprite target, DataScopeKey key, string id, ref SpriteAnimationData data) {
                //Check if the processor should be active
                lock(LOCK) {
                    if(!isActive) return false;
                }

                //Process the sprite
                //Care should be taken that data isn't null, which is the case when the animation didn't exist
                //Procedurline still processes the animation to allow processors to "inject" a new animation
                if(data == null) return false;

                for(int i = 0; i < data.Frames.Length; i++) {
                    ref SpriteAnimationData.AnimationFrame frame = ref data.Frames[i];

                    //Downscale the frame
                    TextureData downscaledFrame = new TextureData(frame.TextureData.Width / 2, frame.TextureData.Height / 2);
                    for(int x = 0; x < downscaledFrame.Width; x++) {
                        for(int y = 0; y < downscaledFrame.Height ; y++) {
                            downscaledFrame[x,y] = new Color((
                                frame.TextureData[2*x + 0, 2*y + 0].ToVector4() +
                                frame.TextureData[2*x + 1, 2*y + 0].ToVector4() +
                                frame.TextureData[2*x + 0, 2*y + 1].ToVector4() +
                                frame.TextureData[2*x + 1, 2*y + 1].ToVector4()
                            ) / 4);
                        }
                    }

                    //Replace the frame texture data
                    //We have to dispose the old one, as we implicitly took ownership of it by replacing it with our own
                    frame.TextureData.Dispose();
                    frame.TextureData = downscaledFrame;

                    //We have to upscale our now downscaled frame to ensure it remains the same size
                    //Even though MTextures have a Width / Height property we can control, it's not used for rendering
                    frame.Scale *= 2;
                }
                return true;
            }

            public bool IsActive {
                get {
                    lock(LOCK) return isActive;
                }
                set {
                    lock(LOCK) {
                        isActive = value;

                        //Invalidate our scope's registrars
                        //This causes the cached data to stay valid, but our sprite to still re-evalute the scopes it belongs to
                        dataScope.InvalidateRegistrars();
                    }
                }
            }
        }

        private DisposablePool dispPool;
        private MyProcessor myProc;

        public SimpleBooster(Vector2 pos) : base(pos, GreenColor, false) {
            //Create a disposable pool for this entity
            //It will dispose everything it contains once the entity is removed, or the scene ends
            dispPool = DisposablePoolComponent.AddTo(this);
        }

        //We want to apply our own processor, so overwrite the method
        protected override Sprite ProcessSprite(Sprite origSprite) {
            //Create our custom processor, and add it to our disposable pool
            myProc = dispPool.Add(new MyProcessor());

            //Create a new derived sprite using our custom processor
            //Normally, you would only create one instance you store in a global variable, and then clone for every sprite
            //But as we're gonna change the way the sprite's processed, we have to have a new sprite instance per entity
            //This also means any cached data is only valid for this sprite
            //So usually, one would use a SpriteMultiplexer to achieve this effect, but the intend of this example is to show how do it from scratch
            //DerivedSprite expects an asynchronous data processor, so we have to wrap it first
            return dispPool.Add(new DerivedSprite("pldemo-my-booster", origSprite, myProc.WrapAsync()));
        }

        protected override BoostType? OnPlayerEnter(Player player) {
            //Toggle our processor
            myProc.IsActive = !myProc.IsActive;

            //Act like a green booster
            return BoostType.GREEN_BOOST;
        }
    }
}