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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Capturing decorator for an <see cref="IPubSubTransport"/>. It wraps a
    /// real transport and, when a capture session has installed an observer
    /// on the shared <see cref="IPubSubCaptureRegistry"/>, taps the raw
    /// payload bytes of every sent / received frame. All other behaviour is
    /// delegated unchanged to the inner transport.
    /// </summary>
    /// <remarks>
    /// This keeps capture out of the UDP / MQTT transports entirely: the
    /// decorator is only inserted when the diagnostics package decorates the
    /// transport factory (see <c>AddPubSubPcap</c>), mirroring the UA-SC
    /// capturing message-socket decorator. When no observer is registered the
    /// tap is a single volatile read.
    /// </remarks>
    public sealed class CapturingPubSubTransport : IPubSubTransport
    {
        /// <summary>
        /// Initializes a new <see cref="CapturingPubSubTransport"/>.
        /// </summary>
        /// <param name="inner">The wrapped transport.</param>
        /// <param name="registry">The shared capture registry.</param>
        /// <param name="timeProvider">Clock for outbound capture timestamps.</param>
        /// <param name="logger">Optional logger.</param>
        public CapturingPubSubTransport(
            IPubSubTransport inner,
            IPubSubCaptureRegistry registry,
            TimeProvider? timeProvider = null,
            ILogger<CapturingPubSubTransport>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(registry);
            m_inner = inner;
            m_registry = registry;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = logger;
            m_inner.StateChanged += OnInnerStateChanged;
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_inner.TransportProfileUri;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction => m_inner.Direction;

        /// <inheritdoc/>
        public bool IsConnected => m_inner.IsConnected;

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            return m_inner.OpenAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            return m_inner.CloseAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            await m_inner.SendAsync(payload, topic, cancellationToken).ConfigureAwait(false);
            Capture(
                PubSubCaptureDirection.Outbound,
                new DateTimeUtc(m_timeProvider.GetUtcNow().UtcDateTime),
                topic,
                payload.Span);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (PubSubTransportFrame frame in m_inner.ReceiveAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                Capture(
                    PubSubCaptureDirection.Inbound,
                    frame.ReceivedAt,
                    frame.Topic,
                    frame.Payload.Span);
                yield return frame;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_inner.StateChanged -= OnInnerStateChanged;
            await m_inner.DisposeAsync().ConfigureAwait(false);
        }

        private void OnInnerStateChanged(object? sender, PubSubTransportStateChangedEventArgs e)
        {
            StateChanged?.Invoke(this, e);
        }

        private void Capture(
            PubSubCaptureDirection direction,
            DateTimeUtc timestamp,
            string? topic,
            ReadOnlySpan<byte> payload)
        {
            IPubSubCaptureObserver? observer = m_registry.CurrentObserver;
            if (observer is null)
            {
                return;
            }
            try
            {
                var context = new PubSubCaptureContext(
                    direction,
                    m_inner.TransportProfileUri,
                    timestamp,
                    endpoint: null,
                    topic: topic);
                observer.OnFrameCaptured(in context, payload);
            }
            catch (Exception ex)
            {
                m_logger?.PubSubCaptureObserverThrew(ex);
            }
        }

        private readonly IPubSubTransport m_inner;
        private readonly IPubSubCaptureRegistry m_registry;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<CapturingPubSubTransport>? m_logger;
    }

    /// <summary>
    /// Source-generated log messages for CapturingPubSubTransport.
    /// </summary>
    internal static partial class CapturingPubSubTransportLog
    {
        [LoggerMessage(EventId = PubSubDiagnosticsEventIds.CapturingPubSubTransport + 0, Level = LogLevel.Debug,
            Message = "PubSub capture observer threw; ignoring.")]
        public static partial void PubSubCaptureObserverThrew(this ILogger logger, Exception exception);
    }

}
