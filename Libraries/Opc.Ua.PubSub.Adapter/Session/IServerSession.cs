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
    /// A mockable abstraction over a managed client session connected to an
    /// external OPC UA server. PubSub adapter components (publisher source,
    /// subscriber writer, action handler) consume this interface to Read,
    /// Write, Call and Subscribe against the server. Connection resilience
    /// (reconnect and keep-alive) is owned by the underlying managed session.
    /// Disposing the instance closes the session.
    /// </summary>
    public interface IServerSession : IAsyncDisposable
    {
        /// <summary>
        /// Indicates whether the underlying managed session is currently
        /// connected to the server.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connects the managed session to the external server. The call is
        /// idempotent: invoking it again while already connected is a no-op.
        /// </summary>
        /// <param name="ct">
        /// A token used to cancel the connect.
        /// </param>
        ValueTask ConnectAsync(CancellationToken ct = default);

        /// <summary>
        /// Reads the supplied node/attribute combinations from the server.
        /// Connects on first use if necessary.
        /// </summary>
        /// <param name="nodesToRead">
        /// The nodes and attributes to read.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The data values, one per entry of <paramref name="nodesToRead"/>.
        /// </returns>
        ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken ct = default);

        /// <summary>
        /// Writes the supplied values to the server. Connects on first use if
        /// necessary.
        /// </summary>
        /// <param name="nodesToWrite">
        /// The node/attribute values to write.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The per-write status codes, one per entry of
        /// <paramref name="nodesToWrite"/>.
        /// </returns>
        ValueTask<ArrayOf<StatusCode>> WriteAsync(
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken ct = default);

        /// <summary>
        /// Calls a method on the server. Connects on first use if necessary.
        /// </summary>
        /// <param name="objectId">
        /// The object that provides the method.
        /// </param>
        /// <param name="methodId">
        /// The method to call.
        /// </param>
        /// <param name="inputArguments">
        /// The input arguments for the call.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The method status and output arguments.
        /// </returns>
        ValueTask<RemoteCallResult> CallAsync(
            NodeId objectId,
            NodeId methodId,
            ArrayOf<Variant> inputArguments,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a client subscription holding dynamically managed monitored
        /// items at the supplied publishing interval. Connects on first use if
        /// necessary.
        /// </summary>
        /// <param name="publishingIntervalMs">
        /// The requested publishing interval in milliseconds.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// A subscription that data-change adapters can add monitored items to.
        /// </returns>
        ValueTask<IDataChangeSubscription> CreateDataChangeSubscriptionAsync(
            double publishingIntervalMs,
            CancellationToken ct = default);

        /// <summary>
        /// Resolves a configured node identifier to a concrete server
        /// <see cref="NodeId"/>. When <paramref name="nodeId"/> carries a
        /// relative browse path (see <see cref="NodeBrowsePath"/>) it is translated
        /// through the server's TranslateBrowsePathsToNodeIds service and the
        /// result is cached for subsequent use; otherwise the value is returned
        /// unchanged. Connects on first use if necessary.
        /// </summary>
        /// <param name="nodeId">
        /// The configured node identifier, which may be a concrete node id or a
        /// browse-path sentinel produced by <see cref="NodeBrowsePath.ToNodeId"/>.
        /// </param>
        /// <param name="ct">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The resolved concrete <see cref="NodeId"/>.
        /// </returns>
        ValueTask<NodeId> ResolveNodeIdAsync(
            NodeId nodeId,
            CancellationToken ct = default);
    }
}
