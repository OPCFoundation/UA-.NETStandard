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

using Opc.Ua;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Services
{
    /// <summary>
    /// Monitored item service set
    /// <see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13"/>
    /// </summary>
    internal interface IMonitoredItemServiceSet
    {
        /// <summary>
        /// Set monitoring
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="monitoringMode"></param>
        /// <param name="monitoredItemIds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(RequestHeader? requestHeader,
            uint subscriptionId, MonitoringMode monitoringMode, ArrayOf<uint> monitoredItemIds,
            CancellationToken ct = default);

        /// <summary>
        /// Create monitored items services
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="timestampsToReturn"></param>
        /// <param name="itemsToCreate"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate, CancellationToken ct = default);

        /// <summary>
        /// Modify monitored item service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="timestampsToReturn"></param>
        /// <param name="itemsToModify"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(RequestHeader? requestHeader,
            uint subscriptionId, TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify, CancellationToken ct = default);

        /// <summary>
        /// Delete monitored items service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="monitoredItemIds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(RequestHeader? requestHeader,
            uint subscriptionId, ArrayOf<uint> monitoredItemIds, CancellationToken ct = default);
    }
}
