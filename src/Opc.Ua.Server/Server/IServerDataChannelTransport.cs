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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The transport a Server binds an accepted OpenDataChannel to.
    /// </summary>
    /// <remarks>
    /// A data channel rides a transport that already exists, so the Service
    /// Set needs a way to reach it from the SecureChannel a request arrived
    /// on. Inline framing and the outer-protocol transports answer this
    /// differently: inline multiplexes on the ChannelId in the frame header,
    /// while <c>opc.quic</c> binds each channel to its own stream.
    /// </remarks>
    public interface IServerDataChannelTransport
    {
        /// <summary>
        /// Resolves the data channel engine for a SecureChannel, if this
        /// transport carries it.
        /// </summary>
        /// <param name="secureChannelContext">The SecureChannel the request
        /// arrived on.</param>
        /// <param name="capabilities">What the Server advertises.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="manager">The engine that carries the channels.</param>
        /// <param name="maxFrameSize">The largest payload a frame may carry
        /// on this transport.</param>
        /// <param name="isReliable">False when frames can be genuinely lost
        /// in flight.</param>
        bool TryGetManager(
            SecureChannelContext secureChannelContext,
            DataChannelServerCapabilities capabilities,
            ITelemetryContext telemetry,
            out DataChannelManager manager,
            out uint maxFrameSize,
            out bool isReliable);

        /// <summary>
        /// Opens the transport stream for a direction this peer initiates, and
        /// returns the identifier to carry in revisedTransportChannelId.
        /// </summary>
        /// <param name="secureChannelContext">The SecureChannel.</param>
        /// <param name="channelId">The data channel.</param>
        /// <param name="direction">The negotiated direction.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<ulong> AllocateServerStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            DataChannelDirection direction,
            CancellationToken ct);

        /// <summary>
        /// Binds a data channel to the transport stream the Client named.
        /// </summary>
        /// <remarks>
        /// The identifier is taken straight from the requester, so an
        /// implementation validates it before it binds anything and refuses
        /// rather than echoing a value it has not checked.
        /// </remarks>
        /// <param name="secureChannelContext">The SecureChannel.</param>
        /// <param name="channelId">The data channel.</param>
        /// <param name="streamId">The identifier from transportChannelId.</param>
        /// <param name="direction">The negotiated direction.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask BindClientStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            CancellationToken ct);

        /// <summary>
        /// Tears down every data channel on a SecureChannel.
        /// </summary>
        /// <param name="secureChannelContext">The SecureChannel.</param>
        /// <param name="reason">Why the channels cannot continue.</param>
        void AbortSecureChannel(SecureChannelContext secureChannelContext, StatusCode reason);
    }
}
