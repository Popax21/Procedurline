using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Represents a data processor.
    /// Data processors process and modify data for a certain target using <see cref="ProcessData"/>.
    /// They also handle target scope registration by implementing <see cref="IDataScopeRegistrar{T}" />
    /// </summary>
    /// <seealso cref="IDataScopeRegistrar{T}" />
    /// <seealso cref="CompositeDataProcessor{T, I, D}" />
    public interface IDataProcessor<T, I, D> : IDataScopeRegistrar<T> {
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
    /// Same as <see cref="IDataProcessor{T, I, D}" />, but async
    /// </summary>
    /// <seealso cref="IDataProcessor{T, I, D}" />>
    public interface IAsyncDataProcessor<T, I, D> : IDataScopeRegistrar<T> {
        /// <summary>
        /// Processes and modifies data for a specified target asynchronously. For more details see <see cref="IDataProcessor{T, I, D}.ProcessData" />.
        /// </summary>
        Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default);
    }

    /// <summary>
    /// Implements an <seealso cref="IDataProcessor{T, I, D}" /> which simply invokes the delegates it's given
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
    /// Implements an <seealso cref="IDataProcessor{T, I, D}" /> which maintains an ordered collection of child data processors which are invoked in a specified order
    /// </summary>
    public class CompositeDataProcessor<T, I, D> : IDisposable, IDataProcessor<T, I, D> {
        public sealed class ProcessorHandle : IDisposable {
            internal readonly object LOCK = new object();
            private IDataProcessor<T, I, D> processor;

            internal ProcessorHandle(CompositeDataProcessor<T, I, D> composite, int order, IDataProcessor<T, I, D> processor) {
                Composite = composite;
                Order = order;
                this.processor = processor;

                //Insert into the processor list while keeping it sorted
                composite.rwLock.EnterReadLock();
                try {
                    LinkedListNode<ProcessorHandle> nextNode = composite.processors.First;
                    while(nextNode != null && nextNode.Value.Order < order) nextNode = nextNode.Next;

                    lock(composite.processors) {
                        if(nextNode != null) composite.processors.AddBefore(nextNode, this);
                        else composite.processors.AddLast(this);
                    }
                } finally {
                    composite.rwLock.ExitReadLock();
                }
            }

            public void Dispose() {
                lock(LOCK) processor = null;
            }

            public CompositeDataProcessor<T, I, D> Composite { get; }
            public int Order { get; }
            public IDataProcessor<T, I, D> Processor => processor;
        }

        private readonly ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();
        private readonly LinkedList<ProcessorHandle> processors = new LinkedList<ProcessorHandle>();

        public void Dispose() => rwLock?.Dispose();

        /// <summary>
        /// Adds a new processor to the composite processor. Duplicates are allowed.
        /// </summary>
        /// <returns>
        /// Returns a <see cref="ProcessorHandle" /> which can be used to remove the processor from the composite
        /// </returns>
        public virtual ProcessorHandle AddProcessor(int order, IDataProcessor<T, I, D> processor) => new ProcessorHandle(this, order, processor);

        public void RegisterScopes(T target, DataScopeKey key) {
            CleanList();

            rwLock.EnterReadLock();
            try {
                for(LinkedListNode<ProcessorHandle> node = processors.First; node != null; ) {
                    IDataProcessor<T, I, D> proc;
                    lock(node.Value.LOCK) proc = node.Value.Processor;
                    if(proc != null) proc.RegisterScopes(target, key);
                    lock(processors) node = node.Next;
                }
            } finally {
                rwLock.ExitReadLock();
            }
        }

        public bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
            CleanList();

            rwLock.EnterReadLock();
            try {
                bool modifiedData = false;
                for(LinkedListNode<ProcessorHandle> node = processors.First; node != null; ) {
                    IDataProcessor<T, I, D> proc;
                    lock(node.Value.LOCK) proc = node.Value.Processor;
                    if(proc != null) modifiedData |= proc.ProcessData(target, key, id, ref data);
                    lock(processors) node = node.Next;
                }
                return modifiedData;
            } finally {
                rwLock.ExitReadLock();
            }
        }

        private void CleanList() {
            //Remove all disposed handles
            rwLock.EnterWriteLock();
            try {
                for(LinkedListNode<ProcessorHandle> node = processors.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                    lock(node.Value.LOCK) {
                        if(node.Value.Processor == null) processors.Remove(node);
                    }
                }
            } finally {
                rwLock.ExitWriteLock();
            }
        }
    }
}