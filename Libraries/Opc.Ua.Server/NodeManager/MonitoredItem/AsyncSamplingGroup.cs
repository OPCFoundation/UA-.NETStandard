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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An async-aware sampling group that uses
    /// <see cref="IReadAsyncNodeManager"/> to read node values asynchronously
    /// instead of the synchronous <see cref="INodeManager.Read"/> call.
    /// </summary>
    /// <remarks>
    /// The background sampling loop already runs inside a <see cref="System.Threading.Tasks.Task"/>,
    /// so <c>await</c> can be used freely without blocking a thread-pool thread.
    /// </remarks>
    public class AsyncSamplingGroup : SamplingGroup
    {
        /// <summary>
        /// Creates a new instance of <see cref="AsyncSamplingGroup"/>.
        /// </summary>
        public AsyncSamplingGroup(
            IServerInternal server,
            IAsyncNodeManager asyncNodeManager,
            List<SamplingRateGroup> samplingRates,
            OperationContext context,
            double samplingInterval,
            IUserIdentity? savedOwnerIdentity = null)
            : base(server, asyncNodeManager.SyncNodeManager, samplingRates, context, samplingInterval, savedOwnerIdentity)
        {
            m_asyncNodeManager = asyncNodeManager;
            m_logger = server.Telemetry.CreateLogger<AsyncSamplingGroup>();
        }

        /// <summary>
        /// Reads the values for a batch of monitored items asynchronously using
        /// <see cref="IReadAsyncNodeManager"/> and queues the results.
        /// </summary>
        protected override async Task DoSampleAsync(IList<ISampledDataChangeMonitoredItem> items)
        {
            try
            {
                if (items == null || items.Count == 0)
                {
                    return;
                }

                var itemsToRead = new List<ReadValueId>(items.Count);
                var values = new List<DataValue>(items.Count);
                var errors = new List<ServiceResult>(items.Count);

                for (int ii = 0; ii < items.Count; ii++)
                {
                    ReadValueId readValueId = items[ii].GetReadValueId();
                    readValueId.Processed = false;
                    itemsToRead.Add(readValueId);
                    values.Add(null!);
                    errors.Add(null!);
                }

                OperationContext context = BuildOperationContext(items);

                await m_asyncNodeManager.ReadAsync(
                    context,
                    0,
                    itemsToRead.ToArrayOf(),
                    values,
                    errors).ConfigureAwait(false);

                for (int ii = 0; ii < items.Count; ii++)
                {
                    if (values[ii] == null)
                    {
                        values[ii] = DataValue.FromStatusCode(
                            StatusCodes.BadInternalError,
                            DateTime.UtcNow);
                    }

                    items[ii].QueueValue(values[ii], errors[ii]);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Server: Unexpected error sampling values asynchronously.");
            }
        }

        private readonly IAsyncNodeManager m_asyncNodeManager;
        private readonly ILogger m_logger;
    }
}
