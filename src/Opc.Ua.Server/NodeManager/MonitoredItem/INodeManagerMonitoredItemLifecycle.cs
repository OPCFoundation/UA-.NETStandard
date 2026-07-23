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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides optional monitored-item detach and reattach operations for built-in node managers.
    /// </summary>
    internal interface INodeManagerMonitoredItemLifecycle
    {
        /// <summary>
        /// Returns a stable snapshot of monitored items owned by the node manager.
        /// </summary>
        ValueTask<IReadOnlyList<IMonitoredItem>> GetMonitoredItemsSnapshotAsync(
            IReadOnlyCollection<NodeId>? nodeIds = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates whether an existing monitored item can attach to this manager.
        /// </summary>
        ValueTask<ServiceResult> ValidateMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Detaches an existing monitored item without disposing it.
        /// </summary>
        ValueTask<ServiceResult> DetachMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attaches an existing monitored item to a compatible node in this manager.
        /// </summary>
        ValueTask<ServiceResult> AttachMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores an existing monitored item to this manager during rollback.
        /// </summary>
        ValueTask<ServiceResult> RestoreMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);
    }
}
