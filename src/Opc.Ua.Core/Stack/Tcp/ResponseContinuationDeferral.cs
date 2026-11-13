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
using System.Collections.Generic;
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
    /// The receive loop defers the completions instead and runs them where its thread
    /// would have run the queued items: after the loop registered its continuation on a
    /// receive (or another wait) that went pending. The loop then no longer depends on
    /// the thread, so a caller's continuation that blocks it cannot stall the receive
    /// loop, as with a pool thread today. A completion that would exceed
    /// <see cref="kMaxPending"/> deferred ones is not deferred.
    /// </para>
    /// </remarks>
    internal static class ResponseContinuationDeferral
    {
        /// <summary>
        /// The most completions one receive loop defers; later ones complete at once.
        /// </summary>
        private const int kMaxPending = 16;

        /// <summary>
        /// Opens a scope in which completed waits are deferred.
        /// </summary>
        public static void Enter()
        {
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
        /// Defers the completion of a wait, when the calling thread is in a scope.
        /// </summary>
        /// <returns><see langword="false"/> if the caller must complete the wait.</returns>
        public static bool TryDefer(IDeferredWaitCompletion wait)
        {
            if (t_depth <= 0)
            {
                return false;
            }
            List<IDeferredWaitCompletion> pending = t_pending ??= [];
            if (pending.Count >= kMaxPending)
            {
                return false;
            }
            pending.Add(wait);
            return true;
        }

        /// <summary>
        /// Completes the deferred waits of this thread.
        /// </summary>
        /// <param name="inline">Whether their continuations run on this thread; only
        /// when the thread holds no lock and nothing waits for it to return.</param>
        public static void CompletePending(bool inline)
        {
            List<IDeferredWaitCompletion>? pending = t_pending;
            if (pending == null || pending.Count == 0)
            {
                return;
            }
            int depth = t_depth;
            t_depth = 0;
            try
            {
                // in the order the responses arrived
                for (int ii = 0; ii < pending.Count; ii++)
                {
                    IDeferredWaitCompletion wait = pending[ii];
                    pending[ii] = null!;
                    wait.CompleteDeferredWait(inline);
                }
            }
            finally
            {
                pending.Clear();
                t_depth = depth;
            }
        }

        /// <summary>
        /// Awaits <paramref name="task"/> and, if it goes pending, completes the deferred
        /// waits on this thread once the continuation is registered.
        /// </summary>
        public static CompleteOnSuspend AwaitAndComplete(ValueTask task)
        {
            return new CompleteOnSuspend(task);
        }

        /// <summary>
        /// Awaits <paramref name="task"/> and, if it goes pending, completes the deferred
        /// waits on this thread once the continuation is registered.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        public static CompleteOnSuspend<T> AwaitAndComplete<T>(ValueTask<T> task)
        {
            return new CompleteOnSuspend<T>(task);
        }

        /// <summary>
        /// Awaits a value task; when the awaiting method suspends, its continuation is
        /// registered first, then the deferred waits complete inline.
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
                CompletePending(inline: true);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(continuation);
                CompletePending(inline: true);
            }
        }

        /// <summary>
        /// Awaits a value task; when the awaiting method suspends, its continuation is
        /// registered first, then the deferred waits complete inline.
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
                CompletePending(inline: true);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(continuation);
                CompletePending(inline: true);
            }
        }

        [ThreadStatic]
        private static List<IDeferredWaitCompletion>? t_pending;

        [ThreadStatic]
        private static int t_depth;
    }
}
