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
    internal sealed class ServiceResponsePooledValueTaskSource : IValueTaskSource<IServiceResponse>, IValueTaskSource
    {
        private static readonly ObjectPool<ServiceResponsePooledValueTaskSource> s_pool =
            new(() => new ServiceResponsePooledValueTaskSource(), 1024);

        private readonly ManualResetValueTaskSource<IServiceResponse> m_source;
        private int m_resultRetrieved;

        /// <summary>
        /// Private constructor to enforce pooling.
        /// </summary>
        private ServiceResponsePooledValueTaskSource()
        {
            m_source = new ManualResetValueTaskSource<IServiceResponse>();
        }

        /// <summary>
        /// Creates or gets a pooled instance.
        /// </summary>
        public static ServiceResponsePooledValueTaskSource Create()
        {
            ServiceResponsePooledValueTaskSource source = s_pool.Get();
            source.m_resultRetrieved = 0;
            return source;
        }

        /// <summary>
        /// Returns the object to the pool.
        /// </summary>
        private void ReturnToPool()
        {
            if (Interlocked.CompareExchange(ref m_resultRetrieved, 1, 0) == 0)
            {
                m_source.Reset();
                s_pool.Return(this);
            }
        }

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
        /// Set the result of the task.
        /// </summary>
        public void SetResult(IServiceResponse result)
        {
            m_source.SetResult(result);
        }

        /// <summary>
        /// Set an exception for the task.
        /// </summary>
        public void SetException(Exception error)
        {
            m_source.SetException(error);
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
