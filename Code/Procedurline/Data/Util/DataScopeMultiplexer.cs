using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements an <see cref="IScopeRegistrar{T}" /> which allows one to multiplex between multiple ways of data processing
    /// </summary>
    /// <see cref="DataProcessorMultiplexer{T, I, D}" />
    /// <see cref="AsyncDataProcessorMultiplexer{T, I, D}" />
    /// <see cref="SpriteMultiplexer" />
    public abstract class DataScopeMultiplexer<T> : IDisposable, IScopeRegistrar<T> {
        public readonly object LOCK = new object();

        public bool IsDisposed { get { lock(LOCK) return isDisposed; } }
        private bool isDisposed = false;
        private int curIdx;

        public readonly int Count;
        public readonly DataScope MuxScope;
        public readonly DataScope[] IndexScopes;

        protected DataScopeMultiplexer(string name, int count) {
            //Create data scopes
            Count = count;
            MuxScope = new DataScope(name) { Transparent = true };
            IndexScopes = new DataScope[count];
            for(int i = 0; i < count; i++) IndexScopes[i] = new DataScope((name != null) ? $"{name}[{i}]" : null);
        }

        public void Dispose() {
            lock(LOCK) {
                if(isDisposed) return;
                isDisposed = true;

                //Dispose data scopes
                MuxScope.Dispose();
                for(int i = 0; i < IndexScopes.Length; i++) IndexScopes[i].Dispose();
            }
        }

        public virtual void RegisterScopes(T target, DataScopeKey key) {
            lock(LOCK) {
                if(isDisposed) throw new ObjectDisposedException("DataScopeMultiplexer");

                //Register mux scopes
                MuxScope.RegisterKey(key);
                IndexScopes[curIdx].RegisterKey(key);
            }
        }

        /// <summary>
        /// Accesses the current index of the multiplexer. Assignment will cause a registrar invalidation of the <see cref="MuxScope" />.
        /// </summary>
        public virtual int MuxIndex {
            get {
                lock(LOCK) {
                    if(isDisposed) throw new ObjectDisposedException("DataScopeMultiplexer");
                    return curIdx;
                }
            }
            set {
                lock(LOCK) {
                    if(isDisposed) throw new ObjectDisposedException("DataScopeMultiplexer");
                    if(value < 0 || Count <= value) throw new IndexOutOfRangeException("Index outside of multiplexer bounds!");

                    if(value == curIdx) return;
                    curIdx = value;
                    MuxScope.InvalidateRegistrars();
                }
            }
        }
    }

    /// <summary>
    /// Implements an <see cref="IDataProcessor{T, I, D}" /> which multiplexs multiple other processors, and allows one to switch between them at runtime
    /// </summary>
    /// <see cref="DataScopeMultiplexer{T}" />
    /// <see cref="AsyncDataProcessorMultiplexer{T, I, D}" />
    public class DataProcessorMultiplexer<T, I, D> : DataScopeMultiplexer<T>, IDataProcessor<T, I, D> {
        private IDataProcessor<T, I, D>[] processors;

        public DataProcessorMultiplexer(string name, int count) : this(name, new IDataProcessor<T, I, D>[count]) {}
        public DataProcessorMultiplexer(string name, params IDataProcessor<T, I, D>[] processors) : base(name, processors.Length) {
            this.processors = processors;
            for(int i = 0; i < processors.Length; i++) IndexScopes[i].Transparent = (processors[i] == null);
        }

        public override void RegisterScopes(T target, DataScopeKey key) {
            lock(LOCK) {
                base.RegisterScopes(target, key);

                //Forward to processor
                this[MuxIndex]?.RegisterScopes(target, key);
            }
        }

        public virtual bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
            lock(LOCK) {
                //Forward to processor
                return this[MuxIndex]?.ProcessData(target, key, id, ref data) ?? false;
            }
        }

        /// <summary>
        /// Accesses the processor with the specified index. Invalidates the corresponding index scope on assignment.
        /// </summary>
        public virtual IDataProcessor<T, I, D> this[int idx] {
            get {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("DataProcessorMultiplexer");
                    return processors[idx];
                }
            }
            set {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("DataProcessorMultiplexer");
                    processors[idx] = value;
                    IndexScopes[idx].Transparent = (value == null);
                    IndexScopes[idx].Invalidate();
                }
            }
        }
    }

    /// <summary>
    /// Implements an <see cref="IAsyncDataProcessor{T, I, D}" /> which multiplexs multiple other processors, and allows one to switch between them at runtime
    /// </summary>
    /// <see cref="DataScopeMultiplexer{T}" />
    /// <see cref="DataProcessorMultiplexer{T, I, D}" />
    public class AsyncDataProcessorMultiplexer<T, I, D> : DataScopeMultiplexer<T>, IAsyncDataProcessor<T, I, D> {
        private IAsyncDataProcessor<T, I, D>[] processors;

        public AsyncDataProcessorMultiplexer(string name, int count) : this(name, new IAsyncDataProcessor<T, I, D>[count]) {}
        public AsyncDataProcessorMultiplexer(string name, params IAsyncDataProcessor<T, I, D>[] processors) : base(name, processors.Length) {
            this.processors = processors;
            for(int i = 0; i < processors.Length; i++) IndexScopes[i].Transparent = (processors[i] == null);
        }

        public override void RegisterScopes(T target, DataScopeKey key) {
            lock(LOCK) {
                base.RegisterScopes(target, key);

                //Forward to processor
                this[MuxIndex]?.RegisterScopes(target, key);
            }
        }

        public Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default) {
            lock(LOCK) {
                //Forward to processor
                return this[MuxIndex]?.ProcessDataAsync(target, key, id, data, token) ?? Task.FromResult(false);
            }
        }

        /// <summary>
        /// Accesses the processor with the specified index. Invalidates the corresponding index scope on assignment.
        /// </summary>
        public virtual IAsyncDataProcessor<T, I, D> this[int idx] {
            get {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("AsyncDataProcessorMultiplexer");
                    return processors[idx];
                }
            }
            set {
                lock(LOCK) {
                    if(IsDisposed) throw new ObjectDisposedException("AsyncDataProcessorMultiplexer");
                    processors[idx] = value;
                    IndexScopes[idx].Transparent = (value == null);
                    IndexScopes[idx].Invalidate();
                }
            }
        }
    }
}