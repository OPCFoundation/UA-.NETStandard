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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Retains all selected outcomes and the outer exchange's delivery charge through serialization.
    /// </summary>
    internal sealed class RetainedCallResponse : CallResponse, IResponseResourceRetention
    {
        /// <summary>
        /// Takes ownership of the result leases selected by every manager invocation in the Call.
        /// </summary>
        public RetainedCallResponse(IDisposable? outcomes)
        {
            m_outcomes = outcomes;
        }

        /// <inheritdoc/>
        public void AttachRequestResources(IDisposable resources)
        {
            if (Interlocked.CompareExchange(ref m_requestResources, resources, null) != null)
            {
                throw new InvalidOperationException("Delivery resources have already been attached.");
            }
        }

        /// <inheritdoc/>
        protected override void ReuseCore()
        {
            if (Interlocked.Exchange(ref m_released, 1) != 0)
            {
                return;
            }
            try
            {
                Interlocked.Exchange(ref m_outcomes, null)?.Dispose();
                ResetForReuse();
            }
            finally
            {
                Interlocked.Exchange(ref m_requestResources, null)?.Dispose();
            }
        }

        private IDisposable? m_outcomes;
        private IDisposable? m_requestResources;
        private int m_released;
    }

    /// <summary>
    /// Composes installed manager recognition with an existing host parking/control policy.
    /// </summary>
    internal sealed class RecoverableRequestPolicy(
        IAsyncServiceExecutionProvider provider,
        IRequestParkingPolicy? previous) : IRequestParkingPolicy, IRequestControlPolicy
    {
        /// <inheritdoc/>
        public bool CanPark(IServiceRequest request)
        {
            return provider.CanPark(request) || previous?.CanPark(request) == true;
        }

        /// <inheritdoc/>
        public bool IsControlRequest(IServiceRequest request)
        {
            return provider.IsControlRequest(request) ||
                (previous as IRequestControlPolicy)?.IsControlRequest(request) == true;
        }
    }
}
