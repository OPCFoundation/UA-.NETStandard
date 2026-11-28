// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The pooled per-request storage of a Read: the result values that become the
    /// <see cref="ReadResponse.Results"/>, the operation results and the permission
    /// attribute cache of the validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node managers fill the results through <see cref="ValueList"/> and
    /// <see cref="ErrorList"/>, lists of exactly the request's length over the pooled
    /// arrays, so a node manager that sizes its work by <c>values.Count</c> sees the
    /// same count as with an exact array.
    /// </para>
    /// <para>
    /// The values are owned by the response that carries them (see
    /// <see cref="PooledReadResponse"/>) and are returned when the transport has
    /// encoded or dropped the response. The operation results and the attribute cache
    /// are only used while the request is dispatched; the dispatcher clears them when
    /// it is done with them (see <see cref="ReleaseDispatchState"/>).
    /// </para>
    /// </remarks>
    internal sealed class ReadResultBuffer : PooledNotificationBuffer<ReadResultBuffer>
    {
        private ReadResultBuffer(int capacity, Bucket? bucket)
            : base(bucket)
        {
            Values = new DataValue[capacity];
            Errors = new ServiceResult[capacity];
            ValueList = new FixedCountList<DataValue>(this, Values);
            ErrorList = new FixedCountList<ServiceResult>(this, Errors);
            AttributeCache = new Dictionary<NodeId, Variant[]>(1);
        }

        /// <summary>
        /// The result values in <c>[0, Count)</c>.
        /// </summary>
        public DataValue[] Values { get; }

        /// <summary>
        /// The operation results in <c>[0, Count)</c>.
        /// </summary>
        public ServiceResult[] Errors { get; }

        /// <summary>
        /// <see cref="Values"/> as a list of <c>Count</c> entries.
        /// </summary>
        public IList<DataValue> ValueList { get; }

        /// <summary>
        /// <see cref="Errors"/> as a list of <c>Count</c> entries.
        /// </summary>
        public IList<ServiceResult> ErrorList { get; }

        /// <summary>
        /// The permission attribute cache of the validation, empty when rented.
        /// </summary>
        public Dictionary<NodeId, Variant[]> AttributeCache { get; }

        /// <summary>
        /// The result values as the response array.
        /// </summary>
        public ArrayOf<DataValue> Results => new(new ReadOnlyMemory<DataValue>(Values, 0, Count));

        /// <summary>
        /// The number of buffers each bucket retains at most.
        /// </summary>
        internal const int MaxBuffersPerBucket = 1024;

        /// <summary>
        /// The array bytes each bucket retains at most.
        /// </summary>
        internal const long MaxRetainedBytesPerBucket = 16L * 1024 * 1024;

        /// <summary>
        /// The array bytes of one entry.
        /// </summary>
        internal static int EntryBytes => Unsafe.SizeOf<DataValue>() + IntPtr.Size;

        /// <summary>
        /// Rents a buffer for <paramref name="count"/> operations.
        /// </summary>
        /// <param name="count">The number of operations, at least one.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static ReadResultBuffer Rent(int count)
        {
            return s_pool.Rent(count);
        }

        /// <summary>
        /// Creates a buffer for <paramref name="count"/> operations that is cleared but not
        /// retained when it is returned.
        /// </summary>
        /// <param name="count">The number of operations, at least one.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static ReadResultBuffer CreateUnpooled(int count)
        {
            return s_pool.CreateUnpooled(count);
        }

        /// <summary>
        /// Clears the state only the dispatch uses, the operation results and the
        /// attribute cache, so it does not stay reachable while the response is sent.
        /// </summary>
        public void ReleaseDispatchState()
        {
            Array.Clear(Errors, 0, Count);
            AttributeCache.Clear();
        }

        /// <inheritdoc/>
        protected override void ClearEntries(int count)
        {
            // releases the values of the response.
            Array.Clear(Values, 0, count);
            Array.Clear(Errors, 0, count);
            AttributeCache.Clear();
        }

        private static readonly Pool s_pool = new(
            EntryBytes,
            MaxBuffersPerBucket,
            MaxRetainedBytesPerBucket,
            static (capacity, bucket) => new ReadResultBuffer(capacity, bucket));

        /// <summary>
        /// A fixed-size list over the first <c>Count</c> entries of a pooled array. Like an
        /// array, it can be written by index but not resized.
        /// </summary>
        /// <typeparam name="T">The entry type.</typeparam>
        private sealed class FixedCountList<T> : IList<T>, IReadOnlyList<T>
        {
            public FixedCountList(ReadResultBuffer owner, T[] items)
            {
                m_owner = owner;
                m_items = items;
            }

            public T this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)m_owner.Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    return m_items[index];
                }
                set
                {
                    if ((uint)index >= (uint)m_owner.Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    m_items[index] = value;
                }
            }

            public int Count => m_owner.Count;

            // an array is a fixed-size list and reports itself read-only in the same way.
            public bool IsReadOnly => true;

            public void Add(T item)
            {
                throw new NotSupportedException("The collection has a fixed size.");
            }

            public void Clear()
            {
                throw new NotSupportedException("The collection has a fixed size.");
            }

            public bool Contains(T item)
            {
                return IndexOf(item) >= 0;
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                Array.Copy(m_items, 0, array, arrayIndex, m_owner.Count);
            }

            public IEnumerator<T> GetEnumerator()
            {
                int count = m_owner.Count;
                for (int ii = 0; ii < count; ii++)
                {
                    yield return m_items[ii];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public int IndexOf(T item)
            {
                return Array.IndexOf(m_items, item, 0, m_owner.Count);
            }

            public void Insert(int index, T item)
            {
                throw new NotSupportedException("The collection has a fixed size.");
            }

            public bool Remove(T item)
            {
                throw new NotSupportedException("The collection has a fixed size.");
            }

            public void RemoveAt(int index)
            {
                throw new NotSupportedException("The collection has a fixed size.");
            }

            private readonly ReadResultBuffer m_owner;
            private readonly T[] m_items;
        }
    }

    /// <summary>
    /// A Read response whose <see cref="ReadResponse.Results"/> are a slice of a pooled
    /// <see cref="ReadResultBuffer"/>, returned when the transport has finished with the
    /// response.
    /// </summary>
    /// <remarks>
    /// The transports call <see cref="IPooledEncodeable.Reuse"/> on a response once it
    /// has been encoded for sending, or when it is dropped because sending failed (see
    /// <see cref="LeasedPublishResponse"/>). A response that is never reused, for example
    /// one a response mutator replaced, leaves its buffer to the collector. The response
    /// itself is not returned to the <see cref="ReadResponse"/> pool, because a pooled
    /// instance would be handed out as a plain <see cref="ReadResponse"/> later.
    /// </remarks>
    internal sealed class PooledReadResponse : ReadResponse
    {
        /// <summary>
        /// Creates a response that owns <paramref name="buffer"/> and carries its values.
        /// </summary>
        public PooledReadResponse(ReadResultBuffer buffer)
        {
            m_buffer = buffer;
            Results = buffer.Results;
        }

        /// <inheritdoc/>
        protected override void ReuseCore()
        {
            ReadResultBuffer? buffer = Interlocked.Exchange(ref m_buffer, null);
            if (buffer == null)
            {
                return;
            }

            // nobody may read the response after it was reused; if someone still does,
            // it sees no results rather than the values of a later request.
            Results = default;
            buffer.Return();
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = (PooledReadResponse)base.Clone();
            // the clone owns no buffer, so it must not share the pooled values.
            clone.m_buffer = null;
            clone.Results = Results.ToArray().ToArrayOf();
            return clone;
        }

        private ReadResultBuffer? m_buffer;
    }
}
