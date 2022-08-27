using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// Implements functionality shared between <seealso cref="CompositeDataProcessor{T, I, D}" /> and <seealso cref="CompositeAsyncDataProcessor{T, I, D}" />
    /// </summary>
    public abstract class BaseCompositeDataProcessor<C, P, T> : IScopeRegistrar<T> where C : BaseCompositeDataProcessor<C, P, T> where P : class, IScopeRegistrar<T> {
        public sealed class ProcessorHandle : IDisposable {
            internal readonly object LOCK = new object();

            public readonly C Composite;
            public readonly int Order;
            private P processor;

            internal ProcessorHandle(C composite, int order, P processor) {
                Composite = composite;
                Order = order;
                this.processor = processor;

                //Insert into the processor list while keeping it sorted
                composite.Lock();
                try {
                    LinkedListNode<ProcessorHandle> nextNode = composite.processors.First;
                    while(nextNode != null && nextNode.Value.Order < order) nextNode = nextNode.Next;

                    lock(composite.processors) {
                        if(nextNode != null) composite.processors.AddBefore(nextNode, this);
                        else composite.processors.AddLast(this);
                    }
                } finally {
                    composite.Unlock();
                }
            }

            public void Dispose() {
                lock(LOCK) processor = null;
            }

            public P Processor => processor;
        }

        private readonly object LOCK = new object();
        private int numUsers;

        protected readonly LinkedList<ProcessorHandle> processors = new LinkedList<ProcessorHandle>();

        /// <summary>
        /// Adds a new processor to the composite processor. Processors are invoked in increasing order of their <paramref name="order"/> parameter. Inserting the same processor multiple times is allowed.
        /// </summary>
        /// <returns>
        /// Returns a <see cref="ProcessorHandle" /> which can be used to remove the processor from the composite
        /// </returns>
        public virtual ProcessorHandle AddProcessor(int order, P processor) => new ProcessorHandle((C) this, order, processor);

        public void RegisterScopes(T target, DataScopeKey key) {
            Lock();
            try {
                for(LinkedListNode<ProcessorHandle> node = processors.First; node != null; ) {
                    P proc;
                    lock(node.Value.LOCK) proc = node.Value.Processor;
                    if(proc != null) proc.RegisterScopes(target, key);
                    lock(processors) node = node.Next;
                }
            } finally {
                Unlock();
            }
        }

        protected void Lock() {
            lock(LOCK) {
                if(numUsers++ == 0) {
                    //Clean the list, by removing processors which were removed
                    for(LinkedListNode<ProcessorHandle> node = processors.First, nnode = node?.Next; node != null; node = nnode, nnode = node?.Next) {
                        lock(node.Value.LOCK) {
                            if(node.Value.Processor == null) processors.Remove(node);
                        }
                    }
                }
            }
        }

        protected void Unlock() {
            lock(LOCK) numUsers--;
        }
    }

    /// <summary>
    /// Implements an <see cref="IDataProcessor{T, I, D}" /> which maintains an ordered collection of child data processors which are invoked in a specified order
    /// </summary>
    public class CompositeDataProcessor<T, I, D> : BaseCompositeDataProcessor<CompositeDataProcessor<T, I, D>, IDataProcessor<T, I, D>, T>, IDataProcessor<T, I, D> {
        public bool ProcessData(T target, DataScopeKey key, I id, ref D data) {
            Lock();
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
                Unlock();
            }
        }
    }

    /// <summary>
    /// Implements an <see cref="IAsyncDataProcessor{T, I, D}" /> which maintains an ordered collection of child data processors which are invoked in a specified order
    /// </summary>
    public class CompositeAsyncDataProcessor<T, I, D> : BaseCompositeDataProcessor<CompositeAsyncDataProcessor<T, I, D>, IAsyncDataProcessor<T, I, D>, T>, IAsyncDataProcessor<T, I, D> {
        public async Task<bool> ProcessDataAsync(T target, DataScopeKey key, I id, AsyncRef<D> data, CancellationToken token = default) {
            Lock();
            try {
                bool modifiedData = false;
                for(LinkedListNode<ProcessorHandle> node = processors.First; node != null; ) {
                    IAsyncDataProcessor<T, I, D> proc;
                    lock(node.Value.LOCK) proc = node.Value.Processor;
                    if(proc != null) modifiedData |= await proc.ProcessDataAsync(target, key, id, data, token);
                    lock(processors) node = node.Next;
                }
                return modifiedData;
            } finally {
                Unlock();
            }
        }
    }
}