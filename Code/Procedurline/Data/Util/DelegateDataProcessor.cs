using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements an <see cref="IDataProcessor{T, I, D}" /> which simply invokes the delegates it's given.
    /// </summary>
    public class DelegateDataProcessor<T, I, D> : IDataProcessor<T, I, D> {
        public delegate void RegisterScopesDelegate(T target, DataScopeKey key);
        public delegate bool ProcessDataDelegate(T target, DataScopeKey key, I id, ref D data);

        private readonly RegisterScopesDelegate registerScopesDeleg;
        private readonly ProcessDataDelegate processDataDeleg;

        public DelegateDataProcessor(RegisterScopesDelegate registerScopes = null, ProcessDataDelegate processData = null) {
            registerScopesDeleg = registerScopes;
            processDataDeleg = processData;
        }

        public void RegisterScopes(T target, DataScopeKey key) => registerScopesDeleg?.Invoke(target, key);
        public bool ProcessData(T target, DataScopeKey key, I id, ref D data) => processDataDeleg?.Invoke(target, key, id, ref data) ?? false;
    }

    /// <summary>
    /// Implements an <see cref="IAsyncDataProcessor{T, I, D}" /> which simply invokes the delegates it's given.
    /// </summary>
    public class DelegateAsyncDataProcessor<T, I, D> : IAsyncDataProcessor<T, I, D> {
        public delegate void RegisterScopesDelegate(T target, DataScopeKey key);
        public delegate Task<bool> ProcessDataAsyncDelegate(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token);

        private readonly RegisterScopesDelegate registerScopesDeleg;
        private readonly ProcessDataAsyncDelegate processDataAsyncDeleg;

        public DelegateAsyncDataProcessor(RegisterScopesDelegate registerScopes = null, ProcessDataAsyncDelegate processDataAsync = null) {
            registerScopesDeleg = registerScopes;
            processDataAsyncDeleg = processDataAsync;
        }

        public void RegisterScopes(T target, DataScopeKey key) => registerScopesDeleg?.Invoke(target, key);
        public Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token) => processDataAsyncDeleg?.Invoke(target, key, id, data, token) ?? Task.FromResult(false);
    }
}