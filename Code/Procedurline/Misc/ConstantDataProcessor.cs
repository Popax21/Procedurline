using System;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A trivial implementation of an <see cref="IDataProcessor{T, I, D}" /> which simply replaces all data with a constant value.
    /// Additionaly also disposes the old data if requested and it implements <see cref="IDisposable" />.
    /// </summary>
    /// <seealso cref="IDataProcessor{T, I, D}" />
    public sealed class ConstantDataProcessor<T, I, D> : IDataProcessor<T, I, D> {
        public readonly IScopeRegistrar<T> Registrar;
        public readonly D ConstData;
        public readonly bool DisposeOld;

        public ConstantDataProcessor(D constData, IScopeRegistrar<T> registrar = null, bool disposeOld = true) {
            Registrar = registrar;
            ConstData = constData;
            DisposeOld = disposeOld;
        }

        public void RegisterScopes(T target, DataScopeKey key) => Registrar?.RegisterScopes(target, key);

        public bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
            if(data?.Equals(ConstData) ?? (ConstData == null)) return false;
            if(DisposeOld && data is IDisposable disp) disp.Dispose();
            data = ConstData;
            return true;
        }
    }
}