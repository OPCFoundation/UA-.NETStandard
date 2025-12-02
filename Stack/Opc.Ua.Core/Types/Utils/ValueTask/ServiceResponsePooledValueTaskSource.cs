/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
