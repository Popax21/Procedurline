using System;
using System.Threading;
using System.Threading.Tasks;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements an sprite animation processor which proxies to another processor modifying the animation's <see cref="SpriteAnimationData" />
    /// </summary>
    public sealed class SpriteAnimationDataProcessor : IAsyncDataProcessor<Sprite, string, Sprite.Animation> {
        public readonly IAsyncDataProcessor<Sprite, string, SpriteAnimationData> DataProcessor;
        public readonly Func<Sprite, DataScopeKey, string, TextureScope> ScopeFunc;

        public SpriteAnimationDataProcessor(IAsyncDataProcessor<Sprite, string, SpriteAnimationData> processor, Func<Sprite, DataScopeKey, string, TextureScope> scopeFunc) {
            DataProcessor = processor;
            ScopeFunc = scopeFunc;
        }

        public void RegisterScopes(Sprite target, DataScopeKey key) => DataProcessor.RegisterScopes(target, key);

        public async Task<bool> ProcessDataAsync(Sprite sprite, DataScopeKey key, string animId, AsyncRef<Sprite.Animation> animRef, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();

            //Get sprite animation data
            using(SpriteAnimationData animData = (animRef.Data != null) ? await ProcedurlineModule.SpriteManager.GetAnimationData(animRef, token) : null) {
                //Run processor
                AsyncRef<SpriteAnimationData> procAnimData = new AsyncRef<SpriteAnimationData>(animData);
                if(!await DataProcessor.ProcessDataAsync(sprite, key, animId, procAnimData, token)) {
                    //Optimize by returning the original animation
                    return false;
                }

                //Create new animation
                animRef.Data = await ProcedurlineModule.SpriteManager.CreateAnimation(animId, ScopeFunc(sprite, key, animId), procAnimData, token);

                return true;
            }
        }
    }
}