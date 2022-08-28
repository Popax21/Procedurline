using System;

using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements an <see cref="AsyncDataProcessorCache{T, I, D}" /> which caches sprite animations.
    /// An extra subclass is required because sprites require the use of <see cref="SpriteScopeKey" /> instead of the default <see cref="DataScopeKey" />.
    /// It also takes care of creating texture scopes per scoped cache wich can be used to store the processed frame textures.
    /// </summary>
    public class SpriteAnimationCache : AsyncDataProcessorCache<Sprite, string, Sprite.Animation> {
        public new class ScopedCache : AsyncDataProcessorCache<Sprite, string, Sprite.Animation>.ScopedCache {
            public readonly new SpriteAnimationCache Cache;
            public readonly new SpriteScopeKey Key;
            public readonly TextureScope TextureScope;

            protected internal ScopedCache(SpriteAnimationCache cache, SpriteScopeKey key) : base(cache, key) {
                Cache = cache;
                Key = key;
                TextureScope = new TextureScope($"{key.SpriteID}:{key.GetScopeListString("-")}", cache.TextureScope);
            }

            public override void Dispose() {
                base.Dispose();
                TextureScope?.Dispose();
            }
        }

        public readonly TextureScope TextureScope;

        public SpriteAnimationCache(TextureScope scope, IAsyncDataProcessor<Sprite, string, Sprite.Animation> processor) : base(processor, StringComparer.OrdinalIgnoreCase) {
            TextureScope = scope;
        }


        /// <inheritdoc cref="DataCache{T,D}.GetScopedData(T)" />
        public new ScopedCache GetScopedData(Sprite target) => (ScopedCache) base.GetScopedData(target);

        /// <inheritdoc cref="DataCache{T,D}.GetScopedData(DataScopeKey)" />
        public new ScopedCache GetScopedData(DataScopeKey key) => (ScopedCache) base.GetScopedData(key);

        protected override DataScopeKey CreateKey(Sprite target) {
            string id = ProcedurlineModule.SpriteManager.GetSpriteID(target);
            if(id == null) return null;
            return new SpriteScopeKey(id);
        }

        protected override AsyncDataProcessorCache<Sprite, string, Sprite.Animation>.ScopedCache CreateScopedData(DataScopeKey key) => new ScopedCache(this,(SpriteScopeKey) key);
    }
}