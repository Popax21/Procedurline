using System;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements a trivial <see cref="IDataProcessor{T, I, D}" /> which filters calls to another data processor based on a given filter function
    /// </summary>
    public sealed class DataProcessorFilter<T, I, D> : IDataProcessor<T, I, D> {
        public readonly IDataProcessor<T, I, D> Processor;
        public readonly Func<T, bool> FilterFunc;

        public DataProcessorFilter(IDataProcessor<T, I, D> processor, Func<T, bool> filterFunc) {
            Processor = processor;
            FilterFunc = filterFunc;
        }

        public void RegisterScopes(T target, DataScopeKey key) {
            if(!FilterFunc(target)) return;
            Processor.RegisterScopes(target, key);
        }

        public bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
            if(!FilterFunc(target)) return false;
            return Processor.ProcessData(target, key, id, ref data);
        }
    }

    /// <summary>
    /// Implements a trivial <see cref="IAsyncDataProcessor{T, I, D}" /> which filters calls to another data processor based on a given filter function
    /// </summary>
    public sealed class AsyncDataProcessorFilter<T, I, D> : IAsyncDataProcessor<T, I, D> {
        public readonly IAsyncDataProcessor<T, I, D> Processor;
        public readonly Func<T, bool> FilterFunc;

        public AsyncDataProcessorFilter(IAsyncDataProcessor<T, I, D> processor, Func<T, bool> filterFunc) {
            Processor = processor;
            FilterFunc = filterFunc;
        }

        public void RegisterScopes(T target, DataScopeKey key) {
            if(!FilterFunc(target)) return;
            Processor.RegisterScopes(target, key);
        }

        public Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default) {
            if(!FilterFunc(target)) return Task.FromResult(false);
            return Processor.ProcessDataAsync(target, key, id, data, token);
        }
    }
}