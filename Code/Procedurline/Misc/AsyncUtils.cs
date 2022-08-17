using System;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// A simple class allowing to pass values by reference asynchronously
    /// </summary>
    public sealed class AsyncRef<T> {
        public T Data;
        public AsyncRef(T data = default) => Data = data;
        public static implicit operator T(AsyncRef<T> aref) => aref.Data;
    }

    public static class AsyncUtils {
        /// <summary>
        /// Registers a continuation handler, or invokes it immediately if the task already completed
        /// </summary>
        public static Task ContinueWithOrInvoke(this Task task, Action<Task> contHandler, CancellationToken token = default) {
            int didInvoke = 0;

            Task cont = task.ContinueWith(t => {
                if(Interlocked.Exchange(ref didInvoke, 1) == 0) contHandler(t);
            }, token);

            if(task.IsCompleted) {
                if(Interlocked.Exchange(ref didInvoke, 1) == 0) contHandler(task);
            }

            return cont;
        }

        /// <summary>
        /// Registers a continuation handler, or invokes it immediately if the task already completed
        /// </summary>
        public static Task ContinueWithOrInvoke<T>(this Task<T> task, Action<Task<T>> contHandler, CancellationToken token = default) {
            int didInvoke = 0;

            Task cont = task.ContinueWith(t => {
                if(Interlocked.Exchange(ref didInvoke, 1) == 0) contHandler(t);
            }, token);

            if(task.IsCompleted) {
                if(Interlocked.Exchange(ref didInvoke, 1) == 0) contHandler(task);
            }

            return cont;
        }

        /// <summary>
        /// Returns a new task, which will complete either when the original task does or the given token is cancelled
        /// </summary>
        public static Task OrCancelled(this Task task, CancellationToken token) {
            if(token == default) return task;

            TaskCompletionSource<int> taskSrc = new TaskCompletionSource<int>();

            task.ContinueWith(t => {
                if(t.Status == TaskStatus.RanToCompletion) taskSrc.TrySetResult(1234);
                else if(t.Status == TaskStatus.Canceled) taskSrc.TrySetCanceled();
                else taskSrc.TrySetException(t.Exception);
            });
            if(task.IsCompleted) return task;

            CancellationTokenRegistration reg = token.Register(() => taskSrc.TrySetCanceled(token));
            taskSrc.Task.ContinueWith(_ => reg.Dispose());

            return taskSrc.Task;
        }
    
        /// <summary>
        /// Returns a new task, which will complete either when the original task does or the given token is cancelled
        /// </summary>
        public static Task<T> OrCancelled<T>(this Task<T> task, CancellationToken token) {
            if(token == default) return task;

            TaskCompletionSource<T> taskSrc = new TaskCompletionSource<T>();

            task.ContinueWith(t => {
                if(t.Status == TaskStatus.RanToCompletion) taskSrc.TrySetResult(t.Result);
                else if(t.Status == TaskStatus.Canceled) taskSrc.TrySetCanceled();
                else taskSrc.TrySetException(t.Exception);
            }, token);
            if(task.IsCompleted) return task;

            CancellationTokenRegistration reg = token.Register(() => taskSrc.TrySetCanceled(token));
            taskSrc.Task.ContinueWith(_ => reg.Dispose());

            return taskSrc.Task;
        }

        /// <summary>
        /// Returns an <see cref="IAsyncDataProcessor{T, I, D}" /> wrapping this <see cref="IDataProcessor{T, I, D}" />
        /// </summary>
        public static IAsyncDataProcessor<T, I, D> WrapAsync<T, I, D>(this IDataProcessor<T, I, D> proc) => new AsyncProcessorWrapper<T, I, D>(proc);
    }
}