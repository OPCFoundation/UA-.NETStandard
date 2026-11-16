/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A wait for a channel operation whose completion the receive loop deferred.
    /// </summary>
    internal interface IDeferredWaitCompletion
    {
        /// <summary>
        /// Ends the deferred wait.
        /// </summary>
        /// <param name="inline">Whether the continuation of the wait may run on the
        /// calling thread.</param>
        void CompleteDeferredWait(bool inline);
    }

    /// <summary>
    /// Lets a receive loop run the continuations of the requests its responses completed
    /// on its own thread once it has nothing more to receive, instead of queueing a pool
    /// work item for each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request waits for its response on a value task source that runs its
    /// continuation asynchronously: the receive loop holds the channel gate when it
    /// completes the request, and must not run the caller's code. The source queues the
    /// continuation with <c>preferLocal</c>, so the receive loop's thread runs it once
    /// the next receive goes pending and the thread returns to the pool, or a worker
    /// the enqueue woke takes it. When the loop's thread runs it, the woken worker found
    /// no work, and since .NET 11 such a worker spins much longer before it blocks again
    /// (UnfairSemaphoreSpinLimit 70 to 1024 backoff units).
    /// </para>
    /// <para>
    /// The receive loop defers the completion instead and runs it where its thread
    /// would have run the queued item: after the loop registered its continuation on a
    /// receive (or another wait) that went pending. The loop then no longer depends on
    /// the thread, so a caller's continuation that blocks it cannot stall the receive
    /// loop, as with a pool thread today.
    /// </para>
    /// <para>
    /// A thread holds at most one deferred completion. A later response completes the
    /// one held before it on the pool, as without deferral: a continuation run inline
    /// may block until another of its requests completes, and that one must not wait
    /// for the blocked thread. The held completion also goes to the pool once the loop
    /// dispatched <see cref="kMaxDeferredChunks"/> more chunks without its receive going
    /// pending, so a busy channel does not hold a response back.
    /// </para>
    /// </remarks>
    internal static class ResponseContinuationDeferral
    {
        /// <summary>
        /// How many more chunks a receive loop dispatches while it holds a completion
        /// before it completes it on the pool.
        /// </summary>
        private const int kMaxDeferredChunks = 4;

        /// <summary>
        /// Opens a scope in which a completed wait is deferred.
        /// </summary>
        /// <remarks>
        /// Called by the receive loop before it dispatches a chunk, while it holds no lock.
        /// </remarks>
        public static void Enter()
        {
            if (t_pending != null && ++t_deferredChunks > kMaxDeferredChunks)
            {
                CompletePending(inline: false);
            }
            t_depth++;
        }

        /// <summary>
        /// Closes the scope opened by <see cref="Enter"/>.
        /// </summary>
        public static void Exit()
        {
            t_depth--;
        }

        /// <summary>
        /// Defers the completion of a wait, when the calling thread is in a scope. A
        /// completion deferred before completes on the pool.
        /// </summary>
        /// <remarks>
        /// The caller may hold locks: a completion on the pool only queues the
        /// continuation.
        /// </remarks>
        /// <returns><see langword="false"/> if the caller must complete the wait.</returns>
        public static bool TryDefer(IDeferredWaitCompletion wait)
        {
            if (t_depth <= 0)
            {
                return false;
            }
            IDeferredWaitCompletion? previous = t_pending;
            t_pending = wait;
            t_deferredChunks = 0;
            previous?.CompleteDeferredWait(inline: false);
            return true;
        }

        /// <summary>
        /// Completes the wait deferred on this thread, if any.
        /// </summary>
        /// <param name="inline">Whether its continuation runs on this thread; only when
        /// the thread holds no lock and nothing waits for it to return.</param>
        public static void CompletePending(bool inline)
        {
            IDeferredWaitCompletion? pending = t_pending;
            if (pending == null)
            {
                return;
            }
            t_pending = null;
            int depth = t_depth;
            t_depth = 0;
            if (inline)
            {
                t_inline++;
            }
            try
            {
                pending.CompleteDeferredWait(inline);
            }
            finally
            {
                t_depth = depth;
                if (inline)
                {
                    t_inline--;
                }
            }
        }

        /// <summary>
        /// Completes the deferred wait inline once the awaiting receive loop suspended,
        /// unless the loop ran inside the dispatch of another loop on this thread, which
        /// may hold locks: that loop completes it when it suspends.
        /// </summary>
        /// <remarks>
        /// A loop can also resume inside a continuation this thread runs inline, when that
        /// continuation completes the loop's receive synchronously. The wait it deferred
        /// then completes on the pool: run inline as well, its continuation would be
        /// nested in the first and could wait for it forever.
        /// </remarks>
        private static void CompleteOnSuspended()
        {
            if (t_depth == 0)
            {
                CompletePending(inline: t_inline == 0);
            }
        }

        /// <summary>
        /// Awaits <paramref name="task"/> and, if it goes pending, completes the deferred
        /// wait on this thread once the continuation is registered.
        /// </summary>
        public static CompleteOnSuspend AwaitAndComplete(ValueTask task)
        {
            return new CompleteOnSuspend(task);
        }

        /// <summary>
        /// Awaits <paramref name="task"/> and, if it goes pending, completes the deferred
        /// wait on this thread once the continuation is registered.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        public static CompleteOnSuspend<T> AwaitAndComplete<T>(ValueTask<T> task)
        {
            return new CompleteOnSuspend<T>(task);
        }

        /// <summary>
        /// Awaits a value task; when the awaiting method suspends, its continuation is
        /// registered first, then the deferred wait completes inline.
        /// </summary>
        internal readonly struct CompleteOnSuspend : ICriticalNotifyCompletion
        {
            private readonly ValueTask m_task;

            public CompleteOnSuspend(ValueTask task)
            {
                m_task = task;
            }

            public CompleteOnSuspend GetAwaiter()
            {
                return this;
            }

            public bool IsCompleted => m_task.IsCompleted;

            public void GetResult()
            {
                m_task.GetAwaiter().GetResult();
            }

            public void OnCompleted(Action continuation)
            {
                m_task.ConfigureAwait(false).GetAwaiter().OnCompleted(continuation);
                CompleteOnSuspended();
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(continuation);
                CompleteOnSuspended();
            }
        }

        /// <summary>
        /// Awaits a value task; when the awaiting method suspends, its continuation is
        /// registered first, then the deferred wait completes inline.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        internal readonly struct CompleteOnSuspend<T> : ICriticalNotifyCompletion
        {
            private readonly ValueTask<T> m_task;

            public CompleteOnSuspend(ValueTask<T> task)
            {
                m_task = task;
            }

            public CompleteOnSuspend<T> GetAwaiter()
            {
                return this;
            }

            public bool IsCompleted => m_task.IsCompleted;

            public T GetResult()
            {
                return m_task.GetAwaiter().GetResult();
            }

            public void OnCompleted(Action continuation)
            {
                m_task.ConfigureAwait(false).GetAwaiter().OnCompleted(continuation);
                CompleteOnSuspended();
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(continuation);
                CompleteOnSuspended();
            }
        }

        [ThreadStatic]
        private static IDeferredWaitCompletion? t_pending;

        [ThreadStatic]
        private static int t_deferredChunks;

        [ThreadStatic]
        private static int t_depth;

        [ThreadStatic]
        private static int t_inline;
    }
}
