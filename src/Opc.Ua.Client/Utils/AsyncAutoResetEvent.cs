// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// Copyright Stephen Cleary Nito.AsyncEx
// Original idea by Stephen Toub:
// http://blogs.msdn.com/b/pfxteam/archive/2012/02/11/10266920.aspx

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// An async-compatible auto-reset event.
    /// </summary>
    public sealed class AsyncAutoResetEvent
    {
        /// <summary>
        /// Creates an async-compatible auto-reset event.
        /// </summary>
        /// <param name="set">Whether the auto-reset event is initially
        /// set or unset.</param>
        public AsyncAutoResetEvent(bool set)
        {
            m_set = set;
        }

        /// <summary>
        /// Creates an async-compatible auto-reset event that is
        /// initially unset.
        /// </summary>
        public AsyncAutoResetEvent()
          : this(false)
        {
        }

        /// <summary>
        /// Asynchronously waits for this event to be set. If the
        /// event is set, this method will auto-reset it and return
        /// immediately, even if the cancellation token is already
        /// signalled. If the wait is canceled, then it will not
        /// auto-reset this event.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token
        /// used to cancel this wait.</param>
        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            Task ret;
            lock (m_mutex)
            {
                if (m_set)
                {
                    m_set = false;
                    ret = Task.CompletedTask;
                }
                else
                {
                    ret = Enqueue(cancellationToken);
                }
            }
            return ret;
        }

        /// <summary>
        /// Sets the event, atomically completing a task returned
        /// by WaitAsync. If the event is already set, this method
        /// does nothing.
        /// </summary>
        public void Set()
        {
            lock (m_mutex)
            {
                if (m_queue.Count == 0)
                {
                    m_set = true;
                }
                else
                {
                    Dequeue();
                }
            }
        }

        private bool TryCancel(Task task, CancellationToken cancellationToken)
        {
            lock (m_mutex)
            {
                for (LinkedListNode<TaskCompletionSource<object?>>? i = m_queue.First;
                    i != null;
                    i = i.Next)
                {
                    TaskCompletionSource<object?> cur = i.Value;
                    if (cur.Task == task)
                    {
                        cur.TrySetCanceled(cancellationToken);
                        m_queue.Remove(i);
                        return true;
                    }
                }
                return false;
            }
        }

        private Task<object?> Enqueue(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.FromCanceled<object?>(token);
            }
            var tcs = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_queue.AddLast(tcs);
            if (!token.CanBeCanceled)
            {
                return tcs.Task;
            }
            CancellationTokenRegistration registration = token.Register(
                () => TryCancel(tcs.Task, token),
                useSynchronizationContext: false);
            tcs.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return tcs.Task;
        }

        private void Dequeue()
        {
            TaskCompletionSource<object?>? head = m_queue.First?.Value;
            if (head != null)
            {
                m_queue.RemoveFirst();
                head.TrySetResult(null);
            }
        }

        private readonly LinkedList<TaskCompletionSource<object?>> m_queue = new();
        private readonly Lock m_mutex = new();
        private bool m_set;
    }
}
