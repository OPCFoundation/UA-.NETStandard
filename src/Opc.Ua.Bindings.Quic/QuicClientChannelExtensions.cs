/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Client side helpers for the experimental QUIC data-channel binding.
    /// </summary>
    /// <remarks>
    /// The Server reaches its data channel transport through
    /// <see cref="QuicStandardServerExtensions.UseQuicDataChannelTransport"/>.
    /// A Client needs the same reach for the opposite reason: once
    /// <c>OpenDataChannel</c> returns, the Client has to attach its own
    /// <see cref="DataChannelManager"/> to the QUIC stream the Server named
    /// in <c>revisedTransportChannelId</c>, or opened one itself for a
    /// Client-initiated direction. Without a seam here an application would
    /// have to unwrap the transport by reflection, which is how this
    /// binding was previously unreachable from outside the assembly.
    /// </remarks>
    public static class QuicClientChannelExtensions
    {
        /// <summary>
        /// The QUIC connection behind a client transport channel, or null
        /// when the channel is not connected or is not a QUIC channel.
        /// </summary>
        /// <param name="channel">The client channel.</param>
        public static QuicMultiplexedTransport? GetQuicTransport(
            this UaSCUaBinaryTransportChannel channel)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            // The peer binding of Part 6 errata §7.6.1 wraps the connection,
            // so the concrete transport is one layer down on a secured
            // channel and directly present on an unsecured one.
            return channel.Transport switch
            {
                QuicMultiplexedTransport direct => direct,
                QuicPeerBindingTransport bound => bound.Inner,
                _ => null
            };
        }

        /// <summary>
        /// Creates the data channel transport for a connected client
        /// channel, so frames can be carried on per-channel QUIC streams.
        /// </summary>
        /// <param name="channel">The connected client channel.</param>
        /// <param name="bufferManager">The pool frame buffers come from.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public static QuicDataChannelTransport CreateDataChannelTransport(
            this UaSCUaBinaryTransportChannel channel,
            BufferManager bufferManager,
            ITelemetryContext telemetry)
        {
            QuicMultiplexedTransport transport = channel.GetQuicTransport()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadNotConnected,
                    "The channel is not a connected opc.quic channel.");

            return new QuicDataChannelTransport(transport, bufferManager, telemetry);
        }

        /// <summary>
        /// Attaches a data channel to the QUIC stream carrying it.
        /// </summary>
        /// <remarks>
        /// For a Client-initiated direction the Client opened the stream and
        /// sent its id as <c>transportChannelId</c>; for `SourceToSink` the
        /// Server opened one and returned its id as
        /// <c>revisedTransportChannelId</c>. Either way the id names the
        /// stream this channel's frames travel on (§7.4).
        /// </remarks>
        /// <param name="transport">The data channel transport.</param>
        /// <param name="channelId">The data channel.</param>
        /// <param name="transportChannelId">The QUIC stream id.</param>
        /// <param name="direction">The channel direction.</param>
        /// <param name="ct">Cancellation token.</param>
        public static ValueTask AttachChannelAsync(
            this QuicDataChannelTransport transport,
            uint channelId,
            ulong transportChannelId,
            DataChannelDirection direction,
            CancellationToken ct = default)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            return transport.BindChannelAsync(
                channelId,
                transportChannelId,
                direction,
                isOpcUaServer: false,
                ct);
        }
    }
}
