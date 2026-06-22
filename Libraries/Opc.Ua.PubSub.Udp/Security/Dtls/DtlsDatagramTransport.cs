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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// DTLS wrapper around the UDP datagram transport for Part 14 §7.3.2.4 unicast PubSub.
    /// </summary>
    public sealed class DtlsDatagramTransport : IPubSubTransport
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsDatagramTransport"/>.
        /// </summary>
        public DtlsDatagramTransport(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            PubSubTransportDirection direction,
            NetworkInterface? networkInterface,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            UdpTransportOptions udpOptions,
            IPubSubDiagnostics? diagnostics,
            IDtlsContextFactory contextFactory,
            DtlsProfile profile)
        {
            if (udpOptions is null)
            {
                throw new ArgumentNullException(nameof(udpOptions));
            }

            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            InnerTransport = new UdpDatagramTransport(
                connection,
                endpoint,
                direction,
                networkInterface,
                telemetry,
                timeProvider,
                udpOptions,
                diagnostics);
        }

        /// <inheritdoc/>
        public string TransportProfileUri => InnerTransport.TransportProfileUri;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction => InnerTransport.Direction;

        /// <inheritdoc/>
        public bool IsConnected => InnerTransport.IsConnected;

        /// <summary>
        /// Parsed DTLS endpoint.
        /// </summary>
        public UdpEndpoint Endpoint => InnerTransport.Endpoint;

        /// <summary>
        /// Resolved DTLS profile.
        /// </summary>
        public DtlsProfile Profile { get; }

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
        {
            add => InnerTransport.StateChanged += value;
            remove => InnerTransport.StateChanged -= value;
        }

        /// <inheritdoc/>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IDtlsContext context = await ContextFactory.CreateAsync(
                Connection,
                Endpoint,
                Profile,
                Telemetry,
                TimeProvider,
                cancellationToken).ConfigureAwait(false);
            m_context = context;
            await InnerTransport.OpenAsync(cancellationToken).ConfigureAwait(false);
            await context.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            m_context = null;
            await InnerTransport.CloseAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            IDtlsContext context = GetContext();
            ReadOnlyMemory<byte> record = await context.ProtectAsync(payload, cancellationToken).ConfigureAwait(false);
            await InnerTransport.SendAsync(record, topic, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IDtlsContext context = GetContext();
            await foreach (PubSubTransportFrame frame in InnerTransport.ReceiveAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                ReadOnlyMemory<byte> payload = await context.UnprotectAsync(frame.Payload, cancellationToken)
                    .ConfigureAwait(false);
                yield return new PubSubTransportFrame(payload, frame.Topic, frame.ReceivedAt);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_context = null;
            await InnerTransport.DisposeAsync().ConfigureAwait(false);
        }

        private IDtlsContext GetContext()
        {
            return m_context ?? throw new InvalidOperationException(
                "DTLS transport must be opened before protected datagrams can flow.");
        }

        private UdpDatagramTransport InnerTransport { get; }

        private IDtlsContextFactory ContextFactory { get; }

        private PubSubConnectionDataType Connection { get; }

        private ITelemetryContext Telemetry { get; }

        private TimeProvider TimeProvider { get; }

        private IDtlsContext? m_context;
    }
}
