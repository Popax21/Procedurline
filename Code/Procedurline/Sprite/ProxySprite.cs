using Monocle;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a sprite which simply proxies a given original sprite
    /// </summary>
    public interface IProxySprite {
        /// <summary>
        /// Returns the original sprite which this sprite proxies
        /// </summary>
        Sprite ProxiedSprite { get; }
    }

    /// <summary>
    /// Represents a sprite animation which simply proxies a given original animation
    /// </summary>
    public interface IProxySpriteAnimation {
        /// <summary>
        /// Returns the original animation which this animation proxies
        /// </summary>
        Sprite.Animation ProxiedAnimation { get; }
    }

    /// <summary>
    /// Implements a sprite which simply proxies a given original sprite. This class is used by default to clone custom sprites.
    /// </summary>
    public sealed class ProxySprite : CustomSprite, IProxySprite {
        private Sprite proxyTarget;

        public ProxySprite(string spriteId, Sprite proxyTarget) : base(spriteId, null, null) {
            proxyTarget.CloneIntoUnsafe(this);
            this.proxyTarget = proxyTarget;
        }

        public Sprite ProxiedSprite => proxyTarget;
    }
}