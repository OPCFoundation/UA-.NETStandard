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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Async transport binding for one PubSubConnection. Owns the
    /// underlying socket / broker session and exposes a uniform
    /// send / receive surface that PubSub encoders and decoders can
    /// drive without depending on a specific transport library.
    /// </summary>
    /// <remarks>
    /// Implements the transport-layer abstraction described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3">
    /// Part 14 §7.3 PubSub transport mappings</see>. The transport
    /// owns the I/O lifecycle: callers may not concurrently send from
    /// multiple producers without external coordination, but multiple
    /// receivers may consume from <see cref="ReceiveAsync"/> via the
    /// returned <see cref="IAsyncEnumerable{T}"/> at the transport's
    /// discretion. Implementations must be safe to call
    /// <see cref="CloseAsync"/> concurrently with an in-flight
    /// <see cref="SendAsync"/>.
    /// </remarks>
    public interface IPubSubTransport : IAsyncDisposable
    {
        /// <summary>
        /// Identifier of the transport profile this instance binds
        /// (e.g. <see cref="Profiles.PubSubUdpUadpTransport"/>).
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Direction the connection is configured to service.
        /// </summary>
        PubSubTransportDirection Direction { get; }

        /// <summary>
        /// Whether the transport is currently in the connected state.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Raised whenever the transport state changes (connect,
        /// disconnect, recoverable error).
        /// </summary>
        event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Opens the transport (socket bind / broker connect /
        /// subscription).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask OpenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the transport. Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask CloseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Emits a single frame on the transport.
        /// </summary>
        /// <param name="payload">
        /// Frame bytes — typically the output of an
        /// <see cref="Encoding.INetworkMessageEncoder.EncodeAsync"/>
        /// invocation.
        /// </param>
        /// <param name="topic">
        /// MQTT topic to publish the frame on. UDP transports ignore
        /// this parameter.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives frames from the transport. The async sequence
        /// completes only when the transport is disposed or the
        /// caller cancels <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async sequence of inbound frames.</returns>
        IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            CancellationToken cancellationToken = default);
    }
}
