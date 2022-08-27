using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements functionality shared between <seealso cref="DataProcessorMultiplexer{T, I, D}" /> and <seealso cref="AsyncDataProcessorMultiplexer{T, I, D}" />
    /// </summary>
    public abstract class BaseDataProcessorMultiplexer<P, T> : IDisposable, IScopeRegistrar<T> where P : IScopeRegistrar<T> {
        public readonly object LOCK = new object();

        public bool IsDisposed { get { lock(LOCK) return isDisposed; } }
        private bool isDisposed = false;
        private P[] processors;
        private int curIdx;

        public readonly DataScope MuxScope;
        public readonly DataScope[] IndexScopes;
        private readonly object indexStoreKey = new object();

        protected BaseDataProcessorMultiplexer(string name, int count) : this(name, new P[count]) {}
        protected BaseDataProcessorMultiplexer(string name, params P[] processors) {
            if(processors.Length <= 0) throw new ArgumentException("Can't create a BaseDataProcessorMultiplexer with an empty processor array!");
            this.processors = processors;

            //Create data scopes
            MuxScope = new DataScope(name);
            IndexScopes = new DataScope[processors.Length];
            for(int i = 0; i < processors.Length; i++) IndexScopes[i] = new DataScope(null, new DictionaryEntry(indexStoreKey, i));
        }

        public void Dispose() {
            lock(LOCK) {
                if(isDisposed) return;
                isDisposed = true;

                //Dispose data scopes
                MuxScope.Dispose();
                for(int i = 0; i < IndexScopes.Length; i++) IndexScopes[i]?.Dispose();
            }
        }

        public void RegisterScopes(T target, DataScopeKey key) {
            lock(LOCK) {
                if(isDisposed) throw new ObjectDisposedException("BaseDataProcessorMultiplexer");

                //Register mux scopes
                MuxScope.RegisterKey(key);
                IndexScopes[curIdx].RegisterKey(key);

                //Forward to processor
                this[curIdx]?.RegisterScopes(target, key);
            }
        }

        /// <summary>
        /// Gets the <see cref="DataScopeKey" />'s registered processor index, or <c>-1</c> if no index is registered
        /// </summary>
        public int GetScopeKeyProcessorIndex(DataScopeKey key) {
            if(!key.DataStore.TryGetValue(indexStoreKey, out object idxObj)) return -1;
            return (int) idxObj;
        }

        /// <summary>
        /// Gets the number of processors addresible by the multiplexer
        /// </summary>
        public int Count => processors.Length;

        /// <summary>
        /// Accesses the current processor index of the multiplexer. Assignment will cause a registrar invalidation of the <see cref="MuxScope" />.
        /// </summary>
        public virtual int CurrentIndex {
            get {
                lock(LOCK) {
                    if(isDisposed) throw new ObjectDisposedException("BaseDataProcessorMultiplexer");
                    return curIdx;
                }
            }
            set {
                lock(LOCK) {
                    if(isDisposed) throw new ObjectDisposedException("BaseDataProcessorMultiplexer");
                    if(value < 0 || processors.Length <= value) throw new IndexOutOfRangeException("Index outside of processor array bounds!");

                    if(value == curIdx) return;
                    curIdx = value;
                    MuxScope.InvalidateRegistrars();
                }
            }
        }

        /// <summary>
        /// Accesses the processor with the specified index. Invalidates the corresponding entry in the <see cref="IndexScopes" /> array on assignment.
        /// </summary>
        public virtual P this[int idx] {
            get {
                lock(LOCK) {
                    if(isDisposed) throw new ObjectDisposedException("BaseDataProcessorMultiplexer");
                    return processors[idx];
                }
            }
            set {
                lock(LOCK) {
                    if(isDisposed) throw new ObjectDisposedException("BaseDataProcessorMultiplexer");
                    if(idx < 0 || processors.Length <= idx) throw new IndexOutOfRangeException("Index outside of processor array bounds!");

                    processors[idx] = value;
                    IndexScopes[idx].Invalidate();
                }
            }
        }
    }

    /// <summary>
    /// Implements an <see cref="IDataProcessor{T, I, D}" /> which multiplexs multiple other processors, and allows one to switch between them at runtime
    /// </summary>
    public class DataProcessorMultiplexer<T, I, D> : BaseDataProcessorMultiplexer<IDataProcessor<T, I, D>, T>, IDataProcessor<T, I, D> {
        public DataProcessorMultiplexer(string name, int count) : base(name, count) {}
        public DataProcessorMultiplexer(string name, params IDataProcessor<T, I, D>[] processors) : base(name, processors) {}

        public bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("DataProcessorMultiplexer");

                //Get processor index
                int procIdx = (key != null) ? GetScopeKeyProcessorIndex(key) : CurrentIndex;
                if(procIdx < 0) return false;

                //Forward to processor
                return this[procIdx]?.ProcessData(target, key, id, ref data) ?? false;
            }
        }
    }

    /// <summary>
    /// Implements an <see cref="IAsyncDataProcessor{T, I, D}" /> which multiplexs multiple other processors, and allows one to switch between them at runtime
    /// </summary>
    public class AsyncDataProcessorMultiplexer<T, I, D> : BaseDataProcessorMultiplexer<IAsyncDataProcessor<T, I, D>, T>, IAsyncDataProcessor<T, I, D> {
        public AsyncDataProcessorMultiplexer(string name, int count) : base(name, count) {}
        public AsyncDataProcessorMultiplexer(string name, params IAsyncDataProcessor<T, I, D>[] processors) : base(name, processors) {}

        public Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default) {
            lock(LOCK) {
                if(IsDisposed) throw new ObjectDisposedException("AsyncDataProcessorMultiplexer");

                //Get processor index
                int procIdx = (key != null) ? GetScopeKeyProcessorIndex(key) : CurrentIndex;
                if(procIdx < 0) return Task.FromResult(false);

                //Forward to processor
                return this[procIdx]?.ProcessDataAsync(target, key, id, data, token) ?? Task.FromResult(false);
            }
        }
    }
}