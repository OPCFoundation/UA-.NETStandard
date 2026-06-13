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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// An opt-in observer interface that receives the raw, wire-level
    /// chunk bytes a UA-SC channel sends or receives. Implementations are
    /// typically external diagnostic taps (packet capture / replay
    /// tooling) that store the bytes alongside the channel's
    /// <see cref="ChannelToken"/> material so the traffic can be
    /// reconstructed and decoded offline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The observer is invoked synchronously on the channel's send /
    /// receive path by a capturing <see cref="IUaSCByteTransport"/> decorator.
    /// Implementations MUST be fast and non-throwing - exceptions are
    /// swallowed by the decorator but observers should not rely on that
    /// behaviour. Heavy work (disk I/O, formatting) must be deferred to a
    /// background queue.
    /// </para>
    /// <para>
    /// The bytes passed to the observer are the exact wire bytes,
    /// including any asymmetric or symmetric encryption applied by the
    /// channel. Combined with the channel's <see cref="ChannelToken"/>
    /// snapshots (exposed via
    /// <see cref="ISecureChannel.OnTokenActivated"/> on the client side
    /// or <see cref="TcpListenerChannel"/>'s OnTokenActivated event on
    /// the server side) the bytes are sufficient to drive an offline
    /// reader through
    /// <see cref="UaSCUaBinaryChannel.ReadSymmetricMessage"/> /
    /// <see cref="UaSCUaBinaryChannel.ReadAsymmetricMessage"/>.
    /// </para>
    /// <para>
    /// The interface lives in <c>Opc.Ua.Bindings</c> alongside the rest
    /// of the transport-binding infrastructure; the channel itself does
    /// NOT take a direct dependency on it. A consumer wires the
    /// observer into the pipeline by registering a capturing transport
    /// binding (for example via
    /// <c>OPCFoundation.NetStandard.Opc.Ua.Bindings.Pcap</c>) - when no
    /// such binding is installed there is no runtime cost on the
    /// channel's send / receive path.
    /// </para>
    /// </remarks>
    public interface IFrameCaptureSink
    {
        /// <summary>
        /// Called when the channel is about to write a wire-level chunk.
        /// </summary>
        /// <param name="channelId">
        /// The OPC UA secure-channel id this chunk belongs to. May be 0
        /// for the very first chunk sent before the server has assigned
        /// an id.
        /// </param>
        /// <param name="chunk">
        /// The chunk bytes that will be handed to the socket. The buffer
        /// is only valid for the duration of the call - copy it if it
        /// must outlive the invocation.
        /// </param>
        void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk);

        /// <summary>
        /// Called when the channel has just received a wire-level chunk
        /// from the socket and before it is dispatched for processing.
        /// </summary>
        /// <param name="channelId">
        /// The OPC UA secure-channel id this chunk belongs to. May be 0
        /// for the very first chunk received before the channel id has
        /// been negotiated.
        /// </param>
        /// <param name="chunk">
        /// The chunk bytes received from the socket. The buffer is only
        /// valid for the duration of the call - copy it if it must
        /// outlive the invocation.
        /// </param>
        void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk);

        /// <summary>
        /// Called by the Pcap binding when a channel activates a new
        /// <see cref="ChannelToken"/> (initial OpenSecureChannel response,
        /// renewal, or final close). The supplied
        /// <see cref="ChannelToken"/> carries the derived signing and
        /// encryption key material that an offline decoder needs to
        /// recover the plaintext bytes recorded by
        /// <see cref="OnFrameSent"/> / <see cref="OnFrameReceived"/>.
        /// </summary>
        /// <param name="channelId">
        /// The OPC UA secure-channel id. May differ from
        /// <c>currentToken.ChannelId</c> only when the token is for the
        /// very next channel-id reassignment.
        /// </param>
        /// <param name="currentToken">
        /// The newly-activated token (never <c>null</c>).
        /// </param>
        /// <param name="previousToken">
        /// The previously-active token, or <c>null</c> if this is the
        /// initial activation. The observer should snapshot whatever
        /// fields it needs before returning; the channel may dispose
        /// either token shortly after this call returns.
        /// </param>
        void OnTokenActivated(
            uint channelId,
            ChannelToken currentToken,
            ChannelToken? previousToken);
    }
}
