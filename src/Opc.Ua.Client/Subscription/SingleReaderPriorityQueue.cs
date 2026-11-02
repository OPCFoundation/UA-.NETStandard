/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Opc.Ua.Client
{
    /// <summary>
    /// An unbounded priority queue with many writers and a single reader
    /// whose wait does not allocate.
    /// </summary>
    /// <remarks>
    /// Replaces an unbounded prioritized channel. The channel allocates a
    /// waiter object and a cancellation callback node every time its reader
    /// blocks and stores both in long-lived objects; with many
    /// subscriptions these old-to-young references cost card-table
    /// scanning in every client GC. Here the reader reuses one value task
    /// source and registers its cancellation token once, not per wait.
    /// The reader must await each wait before starting the next one.
    /// </remarks>
    /// <typeparam name="T">The queued item type.</typeparam>
    internal sealed class SingleReaderPriorityQueue<T> : IValueTaskSource<bool>, IDisposable
    {
        /// <summary>
        /// Create the queue. The smallest item by <paramref name="comparison"/>
        /// is read first.
        /// </summary>
        public SingleReaderPriorityQueue(Comparison<T> comparison)
        {
            m_comparison = comparison;
            m_heap = new T[kInitialCapacity];
            m_waiter.RunContinuationsAsynchronously = true;
        }

        /// <summary>
        /// Number of queued items.
        /// </summary>
        public int Count
        {
            get
            {
                lock (m_lock)
                {
                    return m_count;
                }
            }
        }

        /// <summary>
        /// Queue an item and wake the reader. Returns false once the queue
        /// was completed.
        /// </summary>
        public bool TryWrite(T item)
        {
            lock (m_lock)
            {
                if (m_completed)
                {
                    return false;
                }
                Push(item);
                if (m_waiting)
                {
                    m_waiting = false;
                    m_waiter.SetResult(true);
                }
                return true;
            }
        }

        /// <summary>
        /// Mark the queue complete. Queued items can still be read; the
        /// wait returns false once they are drained.
        /// </summary>
        public bool TryComplete()
        {
            lock (m_lock)
            {
                if (m_completed)
                {
                    return false;
                }
                m_completed = true;
                if (m_waiting)
                {
                    m_waiting = false;
                    m_waiter.SetResult(m_count > 0);
                }
                return true;
            }
        }

        /// <summary>
        /// Take the smallest queued item.
        /// </summary>
        public bool TryRead(out T item)
        {
            lock (m_lock)
            {
                if (m_count == 0)
                {
                    item = default!;
                    return false;
                }
                item = Pop();
                return true;
            }
        }

        /// <summary>
        /// Wait until an item can be read. Returns false when the queue is
        /// complete and empty, and throws <see cref="OperationCanceledException"/>
        /// when <paramref name="ct"/> is cancelled.
        /// </summary>
        public ValueTask<bool> WaitToReadAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return new ValueTask<bool>(Task.FromCanceled<bool>(ct));
            }
            if (ct != m_cancellationToken)
            {
                // Only the reader touches the registration. Disposing it
                // waits for a running callback, so a stale token cannot
                // cancel a later wait.
                m_registration.Dispose();
                m_cancellationToken = ct;
                m_registration = ct.CanBeCanceled ?
                    ct.Register(static s => ((SingleReaderPriorityQueue<T>)s!).OnCancel(), this) :
                    default;
            }
            lock (m_lock)
            {
                if (m_count > 0)
                {
                    return new ValueTask<bool>(true);
                }
                if (m_completed)
                {
                    return new ValueTask<bool>(false);
                }
                m_waiter.Reset();
                m_waiting = true;
                // The callback may have run before the registration existed.
                if (ct.IsCancellationRequested)
                {
                    m_waiting = false;
                    m_waiter.SetException(new OperationCanceledException(ct));
                }
                return new ValueTask<bool>(this, m_waiter.Version);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_registration.Dispose();
            m_registration = default;
            m_cancellationToken = default;
        }

        /// <inheritdoc/>
        bool IValueTaskSource<bool>.GetResult(short token)
        {
            return m_waiter.GetResult(token);
        }

        /// <inheritdoc/>
        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
        {
            return m_waiter.GetStatus(token);
        }

        /// <inheritdoc/>
        void IValueTaskSource<bool>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            m_waiter.OnCompleted(continuation, state, token, flags);
        }

        private void OnCancel()
        {
            lock (m_lock)
            {
                if (m_waiting)
                {
                    m_waiting = false;
                    m_waiter.SetException(new OperationCanceledException(m_cancellationToken));
                }
            }
        }

        private void Push(T item)
        {
            if (m_count == m_heap.Length)
            {
                Array.Resize(ref m_heap, m_heap.Length * 2);
            }
            int index = m_count++;
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (m_comparison(item, m_heap[parent]) >= 0)
                {
                    break;
                }
                m_heap[index] = m_heap[parent];
                index = parent;
            }
            m_heap[index] = item;
        }

        private T Pop()
        {
            T first = m_heap[0];
            int last = --m_count;
            T item = m_heap[last];
            m_heap[last] = default!;
            if (last > 0)
            {
                int index = 0;
                while (true)
                {
                    int child = (index << 1) + 1;
                    if (child >= last)
                    {
                        break;
                    }
                    if (child + 1 < last &&
                        m_comparison(m_heap[child + 1], m_heap[child]) < 0)
                    {
                        child++;
                    }
                    if (m_comparison(item, m_heap[child]) <= 0)
                    {
                        break;
                    }
                    m_heap[index] = m_heap[child];
                    index = child;
                }
                m_heap[index] = item;
            }
            return first;
        }

        private const int kInitialCapacity = 4;
        private readonly Lock m_lock = new();
        private readonly Comparison<T> m_comparison;
        private T[] m_heap;
        private int m_count;
        private bool m_completed;
        private bool m_waiting;
        private ManualResetValueTaskSourceCore<bool> m_waiter;
        private CancellationToken m_cancellationToken;
        private CancellationTokenRegistration m_registration;
    }
}
