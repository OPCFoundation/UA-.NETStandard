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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An async-aware sampling group manager that creates <see cref="AsyncSamplingGroup"/>
    /// instances instead of plain <see cref="SamplingGroup"/> instances, enabling the
    /// use of <see cref="IReadAsyncNodeManager"/> during the sampling loop.
    /// </summary>
    public class AsyncSamplingGroupManager : SamplingGroupManager
    {
        /// <summary>
        /// Creates a new instance of <see cref="AsyncSamplingGroupManager"/>.
        /// </summary>
        /// <param name="server">The server instance.</param>
        /// <param name="asyncNodeManager">The async node manager used for reading values.</param>
        /// <param name="maxQueueSize">Maximum monitored item queue size.</param>
        /// <param name="maxDurableQueueSize">Maximum durable monitored item queue size.</param>
        /// <param name="samplingRates">The available sampling rates.</param>
        public AsyncSamplingGroupManager(
            IServerInternal server,
            IAsyncNodeManager asyncNodeManager,
            uint maxQueueSize,
            uint maxDurableQueueSize,
            IEnumerable<SamplingRateGroup> samplingRates)
            : base(server, asyncNodeManager.SyncNodeManager, maxQueueSize, maxDurableQueueSize, samplingRates)
        {
            m_asyncNodeManager = asyncNodeManager;
        }

        /// <inheritdoc/>
        protected override SamplingGroup CreateSamplingGroup(
            IServerInternal server,
            INodeManager nodeManager,
            List<SamplingRateGroup> samplingRates,
            OperationContext context,
            double samplingInterval,
            IUserIdentity? savedOwnerIdentity = null)
        {
            return new AsyncSamplingGroup(
                server,
                m_asyncNodeManager,
                samplingRates,
                context,
                samplingInterval,
                savedOwnerIdentity);
        }

        private readonly IAsyncNodeManager m_asyncNodeManager;
    }
}
