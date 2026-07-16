/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Reverse Connect Client Host.
    /// </summary>
    public class ReverseConnectHost
    {
        /// <summary>
        /// Create reverse connect host using a process-local
        /// <see cref="DefaultTransportBindingRegistry"/> pre-seeded
        /// with the raw-socket TCP factories.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use</param>
        public ReverseConnectHost(ITelemetryContext telemetry)
            : this(telemetry, transportBindings: null)
        {
        }

        /// <summary>
        /// Create reverse connect host using the supplied
        /// <paramref name="transportBindings"/> registry. The DI
        /// integration in <c>ReverseConnectManager</c> wires the host's
        /// <see cref="ITransportBindingRegistry"/> through this ctor so
        /// transports registered via <c>AddOpcTcpTransport()</c> /
        /// <c>AddHttpsTransport()</c> etc. are visible to the
        /// reverse-connect listener.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <param name="transportBindings">
        /// Optional transport binding registry. When <c>null</c> the
        /// host constructs a <see cref="DefaultTransportBindingRegistry"/>
        /// pre-seeded with the raw-socket TCP factories on first use.
        /// </param>
        public ReverseConnectHost(
            ITelemetryContext telemetry,
            ITransportBindingRegistry? transportBindings)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ReverseConnectHost>();
            m_transportBindings = transportBindings;
        }

        /// <summary>
        /// Creates a new reverse listener host for a client.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void CreateListener(
            Uri url,
            ConnectionWaitingHandlerAsync onConnectionWaiting,
            EventHandler<ConnectionStatusEventArgs> onConnectionStatusChanged)
        {
            CreateListener(url, onConnectionWaiting, onConnectionStatusChanged, serverCertificates: null, certificateValidator: null);
        }

        /// <summary>
        /// Creates a new reverse listener host for a client. The
        /// optional <paramref name="serverCertificates"/> /
        /// <paramref name="certificateValidator"/> are forwarded to the
        /// underlying <see cref="ITransportListener.OpenAsync"/> via
        /// <see cref="TransportListenerSettings"/> and are required by
        /// listener bindings that terminate TLS - in particular the WSS
        /// reverse-connect listener provided by
        /// <c>Opc.Ua.Bindings.Https</c>. For plain <c>opc.tcp</c>
        /// (raw-socket or Kestrel) the parameters can stay <c>null</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void CreateListener(
            Uri url,
            ConnectionWaitingHandlerAsync onConnectionWaiting,
            EventHandler<ConnectionStatusEventArgs> onConnectionStatusChanged,
            ICertificateRegistry? serverCertificates,
            ICertificateValidatorEx? certificateValidator)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            ITransportBindingRegistry registry =
                m_transportBindings ??= DefaultTransportBindingRegistry.WithDefaultTcp();
            ITransportListener? listener = registry.CreateListener(
                url.Scheme,
                m_telemetry);

            m_listener =
                listener
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.",
                    url.Scheme);
            Url = url;
            m_onConnectionWaiting = onConnectionWaiting;
            m_onConnectionStatusChanged = onConnectionStatusChanged;
            m_serverCertificates = serverCertificates;
            m_certificateValidator = certificateValidator;
        }

        /// <summary>
        /// The Url which is used by the transport listener.
        /// </summary>
        public Uri? Url { get; private set; }

        /// <summary>
        /// Opens a reverse listener host.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// CreateListener has not been called before OpenAsync.
        /// </exception>
        public async ValueTask OpenAsync(CancellationToken ct = default)
        {
            if (m_listener == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "CreateListener must be called before OpenAsync.");
            }

            // create the UA listener.
            try
            {
                var settings = new TransportListenerSettings
                {
                    Descriptions = null,
                    Configuration = null,
                    CertificateValidator = m_certificateValidator,
                    NamespaceUris = null,
                    Factory = null,
                    ServerCertificates = m_serverCertificates,
                    ReverseConnectListener = true,
                    MaxChannelCount = 0
                };

                m_logger.ReverseConnectHostLogMessage0(Url);

                await m_listener.OpenAsync(Url!, settings, null!, ct).ConfigureAwait(false);

                m_listener.ConnectionWaiting += m_onConnectionWaiting;
                m_listener.ConnectionStatusChanged += m_onConnectionStatusChanged;
            }
            catch (Exception e)
            {
                m_logger.ReverseConnectHostLogMessage1(e, Url);
                throw;
            }
        }

        /// <summary>
        /// Close the reverse connect listener.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask CloseAsync(CancellationToken ct = default)
        {
            if (m_listener == null)
            {
                return;
            }
            m_listener.ConnectionWaiting -= m_onConnectionWaiting;
            m_listener.ConnectionStatusChanged -= m_onConnectionStatusChanged;
            try
            {
                await m_listener.CloseAsync(ct).ConfigureAwait(false);
            }
            catch (Exception closeError)
            {
                try
                {
                    await m_listener.DisposeAsync().ConfigureAwait(false);
                    m_listener = null;
                }
                catch (Exception disposeError)
                {
                    throw new AggregateException(closeError, disposeError);
                }
                throw;
            }
        }

        private ITransportListener? m_listener;
        private ConnectionWaitingHandlerAsync? m_onConnectionWaiting;
        private EventHandler<ConnectionStatusEventArgs>? m_onConnectionStatusChanged;
        private ICertificateRegistry? m_serverCertificates;
        private ICertificateValidatorEx? m_certificateValidator;
        private ITransportBindingRegistry? m_transportBindings;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for ReverseConnectHost.
    /// </summary>
    internal static partial class ReverseConnectHostLog
    {
        [LoggerMessage(EventId = CoreEventIds.ReverseConnectHost + 0, Level = LogLevel.Information,
            Message = "Open reverse connect listener for {Url}.")]
        public static partial void ReverseConnectHostLogMessage0(this ILogger logger, global::System.Uri? url);

        [LoggerMessage(EventId = CoreEventIds.ReverseConnectHost + 1, Level = LogLevel.Error,
            Message = "Could not open listener for {Url}.")]
        public static partial void ReverseConnectHostLogMessage1(
            this ILogger logger,
            global::System.Exception? exception,
            global::System.Uri? url);
    }

}
