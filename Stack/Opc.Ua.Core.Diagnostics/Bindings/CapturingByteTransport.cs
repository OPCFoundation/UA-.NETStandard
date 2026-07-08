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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Bindings
{
    /// <summary>
    /// <see cref="IUaSCByteTransport"/> decorator that forwards every call
    /// to an inner transport and, when the
    /// <see cref="IChannelCaptureRegistry"/> has an active observer, taps
    /// a copy of the bytes that move across the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hot path on send / receive:
    /// <list type="number">
    /// <item><description>
    /// One <c>volatile</c> read of
    /// <see cref="IChannelCaptureRegistry.CurrentObserver"/>.
    /// </description></item>
    /// <item><description>
    /// If null - one predicted branch + forward to the inner transport.
    /// </description></item>
    /// <item><description>
    /// If non-null - one <see cref="ReadOnlySpan{T}"/> view over the
    /// buffer and a single virtual call into the observer (which is
    /// expected to enqueue and return without I/O).
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The transport does not have a direct handle on the owning UASC
    /// channel so the channel id is reported as <c>0</c> on every frame.
    /// Offline decoders correlate frames to channels via the
    /// <see cref="IFrameCaptureSink.OnTokenActivated"/> notifications
    /// raised by <c>PcapTransportChannelBinding</c> on the channel's
    /// token-activated event (which DO carry the authoritative channel
    /// id via <see cref="ChannelToken.ChannelId"/>).
    /// </para>
    /// </remarks>
    public sealed class CapturingByteTransport : IUaSCByteTransport, IDisposable
    {
        private readonly IUaSCByteTransport m_inner;
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILogger m_logger;

        /// <summary>
        /// Constructs a new capturing transport wrapper.
        /// </summary>
        /// <param name="inner">The wrapped transport; ownership transfers.</param>
        /// <param name="registry">The capture registry whose observer is
        /// consulted on the send / receive hot path.</param>
        /// <param name="loggerFactory">Optional logger factory.</param>
        /// <exception cref="ArgumentNullException">Any required argument is <c>null</c>.</exception>
        public CapturingByteTransport(
            IUaSCByteTransport inner,
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(registry);

            m_inner = inner;
            m_registry = registry;
            m_logger = (loggerFactory ?? NullLoggerFactory.Instance)
                .CreateLogger<CapturingByteTransport>();
        }

        /// <inheritdoc/>
        public string Implementation => m_inner.Implementation + "+pcap";

        /// <inheritdoc/>
        public TransportChannelFeatures Features => m_inner.Features;

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint => m_inner.LocalEndpoint;

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint => m_inner.RemoteEndpoint;

        /// <inheritdoc/>
        public ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            return m_inner.ConnectAsync(url, ct);
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            IFrameCaptureSink? observer = m_registry.CurrentObserver;
            if (observer is not null && chunk.Length != 0)
            {
                TapOutbound(observer, chunk.Span);
            }
            return m_inner.SendChunkAsync(chunk, ct);
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            IFrameCaptureSink? observer = m_registry.CurrentObserver;
            if (observer is not null && buffers.Count != 0)
            {
                for (int i = 0; i < buffers.Count; i++)
                {
                    ArraySegment<byte> segment = buffers[i];
                    if (segment.Array is null || segment.Count == 0)
                    {
                        continue;
                    }
                    TapOutbound(observer, new ReadOnlySpan<byte>(
                        segment.Array, segment.Offset, segment.Count));
                }
            }
            return m_inner.SendChunkAsync(buffers, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            ArraySegment<byte> chunk = await m_inner
                .ReceiveChunkAsync(ct)
                .ConfigureAwait(false);
            IFrameCaptureSink? observer = m_registry.CurrentObserver;
            if (observer is not null && chunk.Array is not null && chunk.Count != 0)
            {
                TapInbound(observer, chunk);
            }
            return chunk;
        }

        /// <inheritdoc/>
        public void Close()
        {
            m_inner.Close();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            (m_inner as IDisposable)?.Dispose();
        }

        private void TapInbound(IFrameCaptureSink observer, ArraySegment<byte> chunk)
        {
            try
            {
                observer.OnFrameReceived(
                    channelId: 0,
                    new ReadOnlySpan<byte>(chunk.Array!, chunk.Offset, chunk.Count));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "CapturingByteTransport: observer.OnFrameReceived threw.");
            }
        }

        private void TapOutbound(IFrameCaptureSink observer, ReadOnlySpan<byte> chunk)
        {
            try
            {
                observer.OnFrameSent(channelId: 0, chunk);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "CapturingByteTransport: observer.OnFrameSent threw.");
            }
        }
    }
}
