using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a data processor.
    /// Data processors process and modify data for a certain target using <see cref="ProcessData"/>.
    /// They also handle target scope registration by implementing <see cref="IScopeRegistrar{T}" />
    /// </summary>
    /// <seealso cref="IScopeRegistrar{T}" />
    /// <seealso cref="CompositeDataProcessor{T, I, D}" />
    public interface IDataProcessor<T, I, D> : IScopeRegistrar<T> {
        /// <summary>
        /// Processes and modifies data for a specified target. Returns false if it it didn't modify the data.
        /// All data with the same key and ID MUST be processed the same way, or e.g. <see cref="DataProcessorCache{T, I, D}" /> will stop working.
        /// DO NOT change your behaviour based on target attributes not encapsulated by different scopes.
        /// If <paramref name="key" /> is <c>null</c>, then the above doesn't apply - your code is free to do whatever it wants to. Callers musn't cache the returned data if they don't provide a key.
        /// </summary>
        /// <returns>
        /// Returns <c>false</c> if data wasn't modified
        /// </returns>
        bool ProcessData(T target, DataScopeKey key, I id, ref D data);
    }

    /// <summary>
    /// Same as <see cref="IDataProcessor{T, I, D}" />, but asynchronous
    /// </summary>
    /// <seealso cref="IDataProcessor{T, I, D}" />>
    public interface IAsyncDataProcessor<T, I, D> : IScopeRegistrar<T> {
        /// <summary>
        /// Processes and modifies data for a specified target asynchronously. For more details see <see cref="IDataProcessor{T, I, D}.ProcessData" />.
        /// </summary>
        Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default);
    }

    /// <summary>
    /// Implements an <see cref="IDataProcessor{T, I, D}" /> which simply invokes the delegates it's given.
    /// </summary>
    public class DelegateDataProcessor<T, I, D> : IDataProcessor<T, I, D> {
        public delegate void RegisterScopesDelegate(T target, DataScopeKey key);
        public delegate bool ProcessDataDelegate(T target, DataScopeKey key, I id, ref D data);

        private RegisterScopesDelegate registerScopesDeleg;
        private ProcessDataDelegate processDataDeleg;

        public DelegateDataProcessor(RegisterScopesDelegate registerScopes = null, ProcessDataDelegate processData = null) {
            registerScopesDeleg = registerScopes;
            processDataDeleg = processData;
        }

        public void RegisterScopes(T target, DataScopeKey key) => registerScopesDeleg?.Invoke(target, key);
        public bool ProcessData(T target, DataScopeKey key, I id, ref D data) => processDataDeleg?.Invoke(target, key, id, ref data) ?? false;
    }

    /// <summary>
    /// Implements an <see cref="IAsyncDataProcessor{T, I, D}" /> wrapping a synchronous <see cref="IDataProcessor{T, I, D}" />
    /// </summary>
    public class AsyncProcessorWrapper<T, I, D> : IAsyncDataProcessor<T, I, D> {
        public readonly IDataProcessor<T, I, D> Processor;

        public AsyncProcessorWrapper(IDataProcessor<T, I, D> processor) => Processor = processor;

        public void RegisterScopes(T target, DataScopeKey key) => Processor.RegisterScopes(target, key);
        public Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default) => Task.FromResult(Processor.ProcessData(target, key, id, ref data.Data));
    }
}