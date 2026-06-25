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
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// Raw Layer-2 frame channel bound to one network interface. Owns
    /// the underlying privileged socket / capture handle and exposes a
    /// uniform send / receive surface over complete Ethernet frames so
    /// the <see cref="EthernetDatagramTransport"/> can build and parse
    /// frames without depending on a specific platform backend.
    /// </summary>
    /// <remarks>
    /// Backends (Linux <c>AF_PACKET</c>, macOS BPF, SharpPcap, in-memory
    /// loopback) implement this abstraction; the transport owns the
    /// framing. Implementations must be safe to call
    /// <see cref="CloseAsync"/> concurrently with an in-flight
    /// <see cref="SendFrameAsync"/>. A frame yielded by
    /// <see cref="ReceiveFramesAsync"/> is owned by the channel and only
    /// valid until the next iteration; consumers must copy or parse it
    /// before requesting the next frame.
    /// </remarks>
    public interface IEthernetFrameChannel : IAsyncDisposable
    {
        /// <summary>
        /// The MAC address of the bound network interface, used as the
        /// source MAC for frames the transport builds.
        /// </summary>
        PhysicalAddress InterfaceAddress { get; }

        /// <summary>
        /// Whether the channel is currently open.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Opens the channel (socket bind / capture start / membership
        /// join).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask OpenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the channel. Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask CloseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a single complete Ethernet frame (without FCS).
        /// </summary>
        /// <param name="frame">The frame bytes to emit.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives complete Ethernet frames. The async sequence
        /// completes only when the channel is closed / disposed or the
        /// caller cancels <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async sequence of inbound frames.</returns>
        IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveFramesAsync(
            CancellationToken cancellationToken = default);
    }
}
