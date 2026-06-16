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

namespace Opc.Ua.Core.Diagnostics.Bindings
{
    /// <summary>
    /// <see cref="IMessageSocket"/> decorator that forwards every call to
    /// an inner socket and, when the
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
    /// If null - one predicted branch + forward to the inner socket.
    /// </description></item>
    /// <item><description>
    /// If non-null - one <see cref="ReadOnlySpan{T}"/> view over the
    /// buffer and a single virtual call into the observer (which is
    /// expected to enqueue and return without I/O).
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Channel id is read from the wrapped <see cref="IMessageSink"/>
    /// (which is always a <see cref="UaSCUaBinaryChannel"/>) at the moment
    /// of the call so renewals and reattachments are handled
    /// transparently.
    /// </para>
    /// </remarks>
    public sealed class CapturingMessageSocket : IMessageSocket
    {
        private readonly IMessageSocket m_inner;
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILogger m_logger;
        private CapturingSinkWrapper m_sinkWrapper;

        /// <summary>
        /// Constructs a new capturing socket wrapper.
        /// </summary>
        /// <param name="inner">The wrapped socket; ownership transfers.</param>
        /// <param name="registry">The capture registry whose observer is
        /// consulted on the send / receive hot path.</param>
        /// <param name="originalSink">The sink supplied by the channel.
        /// Must be a <see cref="UaSCUaBinaryChannel"/> so the channel id
        /// can be projected to the observer.</param>
        /// <param name="loggerFactory">Optional logger factory.</param>
        /// <exception cref="ArgumentNullException">Any required argument is <c>null</c>.</exception>
        public CapturingMessageSocket(
            IMessageSocket inner,
            IChannelCaptureRegistry registry,
            IMessageSink originalSink,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(originalSink);

            m_inner = inner;
            m_registry = registry;
            m_logger = (loggerFactory ?? NullLoggerFactory.Instance)
                .CreateLogger<CapturingMessageSocket>();
            m_sinkWrapper = new CapturingSinkWrapper(originalSink, this);
            m_inner.ChangeSink(m_sinkWrapper);
        }

        /// <inheritdoc/>
        public int Handle => m_inner.Handle;

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint => m_inner.LocalEndpoint;

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint => m_inner.RemoteEndpoint;

        /// <inheritdoc/>
        public TransportChannelFeatures MessageSocketFeatures
            => m_inner.MessageSocketFeatures;

        /// <inheritdoc/>
        public Task ConnectAsync(Uri endpointUrl, CancellationToken ct = default)
        {
            return m_inner.ConnectAsync(endpointUrl, ct);
        }

        /// <inheritdoc/>
        public void Close()
        {
            m_inner.Close();
        }

        /// <inheritdoc/>
        public void ReadNextMessage()
        {
            m_inner.ReadNextMessage();
        }

        /// <inheritdoc/>
        public void ChangeSink(IMessageSink sink)
        {
            ArgumentNullException.ThrowIfNull(sink);
            m_sinkWrapper = new CapturingSinkWrapper(sink, this);
            m_inner.ChangeSink(m_sinkWrapper);
        }

        /// <inheritdoc/>
        public bool Send(IMessageSocketAsyncEventArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);

            IFrameCaptureSink? observer = m_registry.CurrentObserver;
            if (observer is not null)
            {
                TapOutbound(observer, args);
            }
            return m_inner.Send(args);
        }

        /// <inheritdoc/>
        public IMessageSocketAsyncEventArgs MessageSocketEventArgs()
        {
            return m_inner.MessageSocketEventArgs();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_inner.Dispose();
        }

        /// <summary>
        /// Called by the wrapped sink on the receive path. Reads the
        /// active observer (single volatile load) and forwards a span
        /// over the chunk bytes.
        /// </summary>
        internal void TapInbound(uint channelId, ArraySegment<byte> message)
        {
            IFrameCaptureSink? observer = m_registry.CurrentObserver;
            if (observer is null || message.Array is null || message.Count == 0)
            {
                return;
            }
            try
            {
                observer.OnFrameReceived(
                    channelId,
                    new ReadOnlySpan<byte>(message.Array, message.Offset, message.Count));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "CapturingMessageSocket: observer.OnFrameReceived threw on channel {ChannelId}.",
                    channelId);
            }
        }

        private void TapOutbound(IFrameCaptureSink observer, IMessageSocketAsyncEventArgs args)
        {
            uint channelId = m_sinkWrapper.ChannelId;
            try
            {
                BufferCollection? list = args.BufferList;
                if (list is { Count: > 0 })
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ArraySegment<byte> segment = list[i];
                        if (segment.Array is null || segment.Count == 0)
                        {
                            continue;
                        }
                        observer.OnFrameSent(
                            channelId,
                            new ReadOnlySpan<byte>(segment.Array, segment.Offset, segment.Count));
                    }
                    return;
                }
                byte[]? buffer = args.Buffer;
                if (buffer is null || args.Count == 0)
                {
                    return;
                }
                observer.OnFrameSent(
                    channelId,
                    new ReadOnlySpan<byte>(buffer, args.Offset, args.Count));
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "CapturingMessageSocket: observer.OnFrameSent threw on channel {ChannelId}.",
                    m_sinkWrapper.ChannelId);
            }
        }

        /// <summary>
        /// Sink wrapper that taps received chunks before forwarding to the
        /// real channel sink. Constructed once per <see cref="ChangeSink"/>
        /// call so the cast to <see cref="UaSCUaBinaryChannel"/> happens
        /// only once.
        /// </summary>
        private sealed class CapturingSinkWrapper : IMessageSink
        {
            private readonly IMessageSink m_inner;
            private readonly CapturingMessageSocket m_owner;
            private readonly UaSCUaBinaryChannel? m_channel;

            public CapturingSinkWrapper(IMessageSink inner, CapturingMessageSocket owner)
            {
                m_inner = inner;
                m_owner = owner;
                m_channel = inner as UaSCUaBinaryChannel;
            }

            public uint ChannelId => m_channel?.Id ?? 0u;

            public bool ChannelFull => m_inner.ChannelFull;

            public void OnMessageReceived(IMessageSocket source, ArraySegment<byte> message)
            {
                m_owner.TapInbound(ChannelId, message);
                m_inner.OnMessageReceived(source, message);
            }

            public void OnReceiveError(IMessageSocket source, ServiceResult result)
            {
                m_inner.OnReceiveError(source, result);
            }
        }
    }
}
