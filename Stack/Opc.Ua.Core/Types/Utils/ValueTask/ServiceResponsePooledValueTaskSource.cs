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
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Opc.Ua
{
    /// <summary>
    /// A pooled value task source for IServiceResponse.
    /// </summary>
    /// <remarks>
    /// The pool is shared by every server in the process, so a completion that arrives
    /// after the source was returned to the pool would answer whichever request rented it
    /// next. Each rental therefore carries a lease and only the first completion with the
    /// current lease is accepted.
    /// </remarks>
    internal sealed class ServiceResponsePooledValueTaskSource : IValueTaskSource<IServiceResponse>, IValueTaskSource
    {
        private static readonly ObjectPool<ServiceResponsePooledValueTaskSource> s_pool = CreatePool(1024);

        private readonly ObjectPool<ServiceResponsePooledValueTaskSource> m_pool;
        private readonly ManualResetValueTaskSource<IServiceResponse> m_source;
        private int m_resultRetrieved;
        private int m_lease;
        private int m_pendingLease;

        /// <summary>
        /// Private constructor to enforce pooling.
        /// </summary>
        private ServiceResponsePooledValueTaskSource(ObjectPool<ServiceResponsePooledValueTaskSource> pool)
        {
            m_pool = pool;
            m_source = new ManualResetValueTaskSource<IServiceResponse>();
        }

        /// <summary>
        /// Creates or gets a pooled instance.
        /// </summary>
        public static ServiceResponsePooledValueTaskSource Create()
        {
            return Create(s_pool);
        }

        /// <summary>
        /// Creates or gets an instance from the specified pool.
        /// </summary>
        internal static ServiceResponsePooledValueTaskSource Create(
            ObjectPool<ServiceResponsePooledValueTaskSource> pool)
        {
            ServiceResponsePooledValueTaskSource source = pool.Get();
            int lease = source.m_lease + 1;
            if (lease == 0)
            {
                lease = 1;
            }
            source.m_lease = lease;
            source.m_resultRetrieved = 0;
            Volatile.Write(ref source.m_pendingLease, lease);
            return source;
        }

        /// <summary>
        /// Creates an empty pool for sources.
        /// </summary>
        internal static ObjectPool<ServiceResponsePooledValueTaskSource> CreatePool(int maxSize)
        {
            ObjectPool<ServiceResponsePooledValueTaskSource> pool = null;
            pool = new ObjectPool<ServiceResponsePooledValueTaskSource>(
                () => new ServiceResponsePooledValueTaskSource(pool), maxSize);
            return pool;
        }

        /// <summary>
        /// Returns the object to the pool.
        /// </summary>
        private void ReturnToPool()
        {
            if (Interlocked.CompareExchange(ref m_resultRetrieved, 1, 0) == 0)
            {
                m_source.Reset();
                m_pool.Return(this);
            }
        }

        /// <summary>
        /// The lease of the current rental, captured by the owner when the
        /// source is created and passed back to <see cref="TrySetResult"/>.
        /// </summary>
        public int Lease => m_lease;

        /// <summary>
        /// The value task to await.
        /// </summary>
        public ValueTask<IServiceResponse> Task => new(this, Version);

        /// <summary>
        /// The value task to await.
        /// </summary>
        public ValueTask SourceTask => new(this, Version);

        /// <inheritdoc/>
        public short Version => m_source.Version;

        /// <summary>
        /// Completes the rental identified by <paramref name="lease"/>.
        /// </summary>
        /// <returns>
        /// <c>false</c> if that rental was already completed or the source was
        /// recycled to another owner; the result is then discarded.
        /// </returns>
        public bool TrySetResult(int lease, IServiceResponse result)
        {
            if (Interlocked.CompareExchange(ref m_pendingLease, 0, lease) != lease)
            {
                return false;
            }
            m_source.SetResult(result);
            return true;
        }

        /// <inheritdoc/>
        public IServiceResponse GetResult(short token)
        {
            try
            {
                return m_source.GetResult(token);
            }
            finally
            {
                ReturnToPool();
            }
        }

        /// <inheritdoc/>
        void IValueTaskSource.GetResult(short token)
        {
            try
            {
                ((IValueTaskSource)m_source).GetResult(token);
            }
            finally
            {
                ReturnToPool();
            }
        }

        /// <inheritdoc/>
        public ValueTaskSourceStatus GetStatus(short token)
        {
            return m_source.GetStatus(token);
        }

        /// <inheritdoc/>
        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            m_source.OnCompleted(continuation, state, token, flags);
        }
    }
}
