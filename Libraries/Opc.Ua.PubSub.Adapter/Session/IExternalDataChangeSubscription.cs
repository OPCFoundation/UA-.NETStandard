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
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// A single client subscription on an external OPC UA server that holds
    /// many dynamically managed monitored items at a fixed publishing interval.
    /// Each monitored item delivers the latest <see cref="DataValue"/> of one
    /// node attribute via the <see cref="DataChanged"/> event. Disposing the
    /// subscription removes it from the server.
    /// </summary>
    public interface IExternalDataChangeSubscription : IAsyncDisposable
    {
        /// <summary>
        /// Raised on every data change reported by the server for any monitored
        /// item in this subscription. Handlers receive the originating client
        /// handle, node identifier, and the latest value.
        /// </summary>
        event EventHandler<ExternalDataChangeEventArgs>? DataChanged;

        /// <summary>
        /// Adds a monitored item to this subscription for the supplied node and
        /// attribute. The item is queued for creation on the server and becomes
        /// active after the next <see cref="ApplyChangesAsync"/> (or the next
        /// engine apply cycle). The publisher should prime the initial value by
        /// issuing a Read through <see cref="IExternalServerSession.ReadAsync"/>.
        /// </summary>
        /// <param name="nodeId">
        /// The node to monitor.
        /// </param>
        /// <param name="attributeId">
        /// The attribute to monitor, for example <see cref="Attributes.Value"/>.
        /// </param>
        /// <param name="samplingIntervalMs">
        /// The requested sampling interval in milliseconds. Use <c>-1</c> to
        /// defer to the subscription publishing interval.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The client handle assigned to the new monitored item; it identifies
        /// the item in <see cref="DataChanged"/> notifications.
        /// </returns>
        ValueTask<uint> AddMonitoredItemAsync(
            NodeId nodeId,
            uint attributeId,
            double samplingIntervalMs,
            CancellationToken ct = default);

        /// <summary>
        /// Flushes monitored items added since the last call to the server and
        /// completes once they have been created (or settled with an error).
        /// The underlying managed subscription engine also applies changes
        /// automatically; this method lets a publisher await item creation
        /// deterministically before priming initial values.
        /// </summary>
        /// <param name="ct">
        /// A token used to cancel the wait.
        /// </param>
        ValueTask ApplyChangesAsync(CancellationToken ct = default);
    }
}
