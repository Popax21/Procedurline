using System;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents something belonging to a certain set of data scopes, which it's capable of registering on a given key. In contrast to <see cref="IScopeRegistrar{T}" />, which describes a third party registrar capable of regstering scopes for arbitrary targets, this interface allows for a single target to just register its own set of scopes it belongs to.
    /// </summary>
    /// <seealso cref="DataScope" />
    /// <seealso cref="DataScopeKey" />
    /// <seealso cref="IScopeRegistrar{T}" />
    public interface IScopedObject {
        /// <summary>
        /// Registers all scopes this object belongs to on the given key
        /// </summary>
        void RegisterScopes(DataScopeKey key);
    }

    /// <summary>
    /// Represents something capable of registering the appropiate scopes of a target on a given key. In contrast to <see cref="IScopedObject" />, which describes a target capable of registering just its own scopes on a given key, this interface allows for an external entity to do the scope assignment.
    /// </summary>
    /// <seealso cref="DataScope" />
    /// <seealso cref="DataScopeKey" />
    /// <seealso cref="IScopedObject" />
    public interface IScopeRegistrar<T> {
        /// <summary>
        /// Registers all scopes the target belongs to on the given key
        /// </summary>
        void RegisterScopes(T target, DataScopeKey key);
    }

    /// <summary>
    /// Represents something which has validity tied to or related to one or multiple data scopes, and as such can be invalidated. This can be scope keys, or entire data scopes themselves.
    /// </summary>
    /// <seealso cref="DataScope" />
    /// <seealso cref="DataScopeKey" />
    public interface IScopedInvalidatable {
        void Invalidate();
        void InvalidateRegistrars();

        event Action<IScopedInvalidatable> OnInvalidate;
        event Action<IScopedInvalidatable> OnInvalidateRegistrars;
    }
}