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

#if !NET8_0_OR_GREATER

using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for <see cref="PrioritizedChannelHelper"/>.
    /// Polyfill for <c>UnboundedPrioritizedChannelOptions</c>
    /// which is only available in .NET 9+.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class UnboundedPrioritizedChannelOptions<T>
    {
        /// <summary>
        /// Whether only a single reader will access the channel.
        /// </summary>
        public bool SingleReader { get; set; }

        /// <summary>
        /// The comparer used to order items by priority.
        /// </summary>
        public IComparer<T>? Comparer { get; set; }
    }

    /// <summary>
    /// Polyfill for <c>Channel.CreateUnboundedPrioritized</c> on
    /// runtimes before .NET 9. Uses an unbounded channel with a
    /// priority queue that re-orders items on read.
    /// </summary>
    internal static class PrioritizedChannelHelper
    {
        /// <summary>
        /// Create an unbounded channel that reads items in priority
        /// order according to the provided comparer.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static Channel<T> CreateUnboundedPrioritized<T>(
            UnboundedPrioritizedChannelOptions<T> options)
        {
            return new PrioritizedChannel<T>(
                options.Comparer ?? Comparer<T>.Default);
        }
    }

    /// <summary>
    /// A channel implementation that stores items in a priority
    /// queue, yielding them in priority order on read. This is a
    /// polyfill for the .NET 9 prioritized channel API.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class PrioritizedChannel<T> : Channel<T>
    {
        public PrioritizedChannel(IComparer<T> comparer)
        {
            m_comparer = comparer;
            m_heap = [];
            m_semaphore = new SemaphoreSlim(0);
            Writer = new PrioritizedWriter(this);
            Reader = new PrioritizedReader(this);
        }

        private void HeapPush(T item)
        {
            m_heap.Add(item);
            int i = m_heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (m_comparer.Compare(m_heap[i], m_heap[parent]) >= 0)
                {
                    break;
                }
                (m_heap[i], m_heap[parent]) = (m_heap[parent], m_heap[i]);
                i = parent;
            }
        }

        private T HeapPop()
        {
            T result = m_heap[0];
            int last = m_heap.Count - 1;
            m_heap[0] = m_heap[last];
            m_heap.RemoveAt(last);
            last--;
            int i = 0;
            while (true)
            {
                int left = (2 * i) + 1;
                int right = (2 * i) + 2;
                int smallest = i;
                if (left <= last &&
                    m_comparer.Compare(m_heap[left], m_heap[smallest]) < 0)
                {
                    smallest = left;
                }
                if (right <= last &&
                    m_comparer.Compare(m_heap[right], m_heap[smallest]) < 0)
                {
                    smallest = right;
                }
                if (smallest == i)
                {
                    break;
                }
                (m_heap[i], m_heap[smallest]) = (m_heap[smallest], m_heap[i]);
                i = smallest;
            }
            return result;
        }

        private sealed class PrioritizedWriter : ChannelWriter<T>
        {
            private readonly PrioritizedChannel<T> m_channel;

            public PrioritizedWriter(PrioritizedChannel<T> channel)
            {
                m_channel = channel;
            }

            public override bool TryWrite(T item)
            {
                lock (m_channel.m_lock)
                {
                    if (m_channel.m_completed)
                    {
                        return false;
                    }
                    m_channel.HeapPush(item);
                }
                m_channel.m_semaphore.Release();
                return true;
            }

            public override ValueTask<bool> WaitToWriteAsync(
                CancellationToken ct = default)
            {
                return m_channel.m_completed
                    ? new ValueTask<bool>(false)
                    : new ValueTask<bool>(true);
            }

            public override bool TryComplete(System.Exception? error = null)
            {
                lock (m_channel.m_lock)
                {
                    if (m_channel.m_completed)
                    {
                        return false;
                    }
                    m_channel.m_completed = true;
                }
                m_channel.m_semaphore.Release();
                return true;
            }
        }

        private sealed class PrioritizedReader : ChannelReader<T>
        {
            private readonly PrioritizedChannel<T> m_channel;

            public PrioritizedReader(PrioritizedChannel<T> channel)
            {
                m_channel = channel;
            }

            public override bool TryRead(out T item)
            {
                lock (m_channel.m_lock)
                {
                    if (m_channel.m_heap.Count > 0 &&
                        m_channel.m_semaphore.Wait(0))
                    {
                        item = m_channel.HeapPop();
                        return true;
                    }
                }
                item = default!;
                return false;
            }

            public override async ValueTask<bool> WaitToReadAsync(
                CancellationToken ct = default)
            {
                while (true)
                {
                    lock (m_channel.m_lock)
                    {
                        if (m_channel.m_heap.Count > 0)
                        {
                            return true;
                        }
                        if (m_channel.m_completed)
                        {
                            return false;
                        }
                    }
                    await m_channel.m_semaphore.WaitAsync(ct)
                        .ConfigureAwait(false);
                    // Consumed the semaphore signal — check again.
                    // If an item was added, we'll find it in the heap.
                    // If completed, we return false.
                    lock (m_channel.m_lock)
                    {
                        if (m_channel.m_heap.Count > 0)
                        {
                            return true;
                        }
                        if (m_channel.m_completed)
                        {
                            return false;
                        }
                    }
                }
            }
        }

        private readonly IComparer<T> m_comparer;
        private readonly List<T> m_heap;
        private readonly SemaphoreSlim m_semaphore;
        private readonly object m_lock = new();
        private bool m_completed;
    }
}

#endif
