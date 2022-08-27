namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A primitive implementation of an <see cref="IScopeRegistrar{T}" /> which simply forwards to the <see cref="IScopedObject.RegisterScopes" /> if the target implements that interface
    /// </summary>
    public sealed class ScopedObjectRegistrar<T> : IScopeRegistrar<T> {
        public readonly ScopedObjectRegistrar<T> Instance = new ScopedObjectRegistrar<T>();
        private ScopedObjectRegistrar() {}

        public void RegisterScopes(T target, DataScopeKey key) => (target as IScopedObject)?.RegisterScopes(key);
    }
}