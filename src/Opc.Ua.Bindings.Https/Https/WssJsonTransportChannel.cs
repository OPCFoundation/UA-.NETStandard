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
using System.IO;
using System.Net.Security;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Client-side transport channel for the WSS <c>opcua+uajson</c>
    /// sub-protocol (OPC UA Part 6 §7.5.2). Each
    /// <see cref="SendRequestAsync"/> opens a fresh
    /// <see cref="ClientWebSocket"/>, sends one JSON-encoded request as a
    /// single text frame, receives the JSON-encoded response, and closes
    /// the WebSocket. This is the simplest correct implementation —
    /// reusing the WebSocket across requests can be added in a follow-up
    /// without changing the public shape.
    /// </summary>
    /// <remarks>
    /// The JSON sub-protocol does not use UA Secure Conversation, so this
    /// channel always operates with <see cref="MessageSecurityMode.None"/>
    /// and relies on TLS at the WebSocket layer for transport security.
    /// </remarks>
    public sealed class WssJsonTransportChannel : ITransportChannel, ISecureChannel
    {
        /// <summary>
        /// Pseudo-scheme used internally by <see cref="ClientChannelManager"/>
        /// to route the <c>UaWssJsonTransport</c> profile to this channel.
        /// Never appears in user-facing URLs.
        /// </summary>
        internal const string PseudoScheme = "opc.wss+json";

        /// <summary>
        /// Create a new WSS JSON transport channel.
        /// </summary>
        public WssJsonTransportChannel(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<WssJsonTransportChannel>();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_settings?.ServerCertificate?.Dispose();
                m_settings?.ClientCertificate?.Dispose();
                m_settings?.ClientCertificateChain?.Dispose();
            }
        }

        /// <inheritdoc/>
        public string UriScheme => PseudoScheme;

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription
            => m_settings?.Description ?? throw BadNotConnected();

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => m_settings?.Configuration ?? throw BadNotConnected();

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => m_messageContext ?? throw BadNotConnected();

        /// <inheritdoc/>
        public ChannelToken CurrentToken => new();

        /// <inheritdoc/>
        public byte[] ChannelThumbprint => [];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate => [];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate => [];

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public int OperationTimeout { get; set; }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            SaveSettings(url, settings);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            SaveSettings(connection.EndpointUrl, settings);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            // Each request opens its own WebSocket; nothing persistent to reconnect.
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct = default)
        {
            if (m_url == null || m_messageContext == null)
            {
                throw BadNotConnected();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (OperationTimeout > 0)
            {
                cts.CancelAfter(OperationTimeout);
            }

            var ws = new ClientWebSocket();
            System.Security.Cryptography.X509Certificates.X509Certificate2? clientCertificate = null;
            try
            {
                ws.Options.AddSubProtocol(Profiles.OpcUaWsSubProtocolUaJson);
                clientCertificate = ConfigureClientTls(ws);
                Uri wsUrl = NormalizeUrl(m_url);
                await ws.ConnectAsync(wsUrl, cts.Token).ConfigureAwait(false);

                if (!string.Equals(
                        ws.SubProtocol,
                        Profiles.OpcUaWsSubProtocolUaJson,
                        StringComparison.Ordinal))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotConnected,
                        "Server did not select the opcua+uajson WebSocket sub-protocol (got '{0}').",
                        ws.SubProtocol ?? "<none>");
                }

                byte[] payload;
                using (var memory = new MemoryStream())
                {
                    using (var encoder = new JsonEncoder(
                        memory,
                        m_messageContext,
                        JsonEncoderOptions.Compact))
                    {
                        encoder.EncodeMessage(request, request.TypeId);
                    }
                    payload = memory.ToArray();
                }

#if NET5_0_OR_GREATER
                await ws.SendAsync(
                    new ReadOnlyMemory<byte>(payload, 0, payload.Length),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cts.Token).ConfigureAwait(false);
#else
                await ws.SendAsync(
                    new ArraySegment<byte>(payload, 0, payload.Length),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cts.Token).ConfigureAwait(false);
#endif

                byte[] responseBytes = await ReceiveMessageAsync(
                    ws,
                    m_messageContext.MaxMessageSize,
                    cts.Token).ConfigureAwait(false);

                return JsonDecoder.DecodeMessage<IServiceResponse>(responseBytes, m_messageContext);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadRequestTimeout,
                    "WSS+JSON request timed out after {0} ms.",
                    OperationTimeout);
            }
            catch (Exception ex) when (ex is not ServiceResultException)
            {
                m_logger.WssJsonRequestFailed(ex);
                throw ServiceResultException.Create(
                    StatusCodes.BadUnknownResponse,
                    ex,
                    "Error sending WSS+JSON request: {0}",
                    ex.Message);
            }
            finally
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await CloseNormalAsync(ws, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ws.Dispose();
                }

                // The TLS client certificate must outlive the handshake and
                // the connection; release it only after the socket is gone.
                clientCertificate?.Dispose();
            }
        }

        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            m_url = url;
            m_settings = settings;
            OperationTimeout = settings.Configuration!.OperationTimeout;
            m_messageContext = new ServiceMessageContext(m_telemetry, settings.Factory!)
            {
                MaxArrayLength = settings.Configuration.MaxArrayLength,
                MaxByteStringLength = settings.Configuration.MaxByteStringLength,
                MaxMessageSize = settings.Configuration.MaxMessageSize,
                MaxStringLength = settings.Configuration.MaxStringLength,
                MaxEncodingNestingLevels = settings.Configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = settings.Configuration.MaxDecoderRecoveries,
                NamespaceUris = settings.NamespaceUris!,
                ServerUris = new StringTable()
            };
        }

        /// <summary>
        /// Receives one WebSocket message. The size is checked against
        /// <paramref name="maxMessageSize"/> (zero means unlimited) while the
        /// frames arrive, so a peer that never ends its message cannot grow
        /// the buffer beyond the limit before the decoder rejects it.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async Task<byte[]> ReceiveMessageAsync(
            WebSocket ws,
            int maxMessageSize,
            CancellationToken ct)
        {
            using var memory = new MemoryStream();
            byte[] receiveBuffer = new byte[8192];
            while (true)
            {
                WebSocketReceiveResult result = await ws
                    .ReceiveAsync(
                        new ArraySegment<byte>(receiveBuffer),
                        ct)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Server closed the WebSocket before sending a response.");
                }
                if (maxMessageSize > 0 && memory.Length + result.Count > maxMessageSize)
                {
                    // OPC 10000-6 §7.5.2: close with status 1009 (MessageTooBig).
                    // Only the Close frame is sent, briefly bounded, and the socket
                    // is then aborted instead of waiting for a handshake the peer
                    // may never answer.
                    await CloseMessageTooBigAsync(ws).ConfigureAwait(false);
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "MaxMessageSize {0} < {1}",
                        maxMessageSize,
                        memory.Length + result.Count);
                }
                if (result.Count > 0)
                {
                    memory.Write(receiveBuffer, 0, result.Count);
                }
                if (result.EndOfMessage)
                {
                    return memory.ToArray();
                }
            }
        }

        /// <summary>
        /// Sends a Close frame with status 1009 (MessageTooBig), waiting at most
        /// <see cref="kMessageTooBigCloseTimeout"/> milliseconds, then aborts
        /// the socket.
        /// </summary>
        private static async Task CloseMessageTooBigAsync(WebSocket ws)
        {
            try
            {
                using var cts = new CancellationTokenSource(kMessageTooBigCloseTimeout);
                await ws.CloseOutputAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    "Response exceeds MaxMessageSize.",
                    cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort: the socket is aborted either way.
            }
            ws.Abort();
        }

        /// <summary>
        /// Sends a normal Close frame without waiting for the peer's Close
        /// (one message per connection, OPC 10000-6 7.5.2), bounded by
        /// <see cref="kMessageTooBigCloseTimeout"/> and the caller's token,
        /// then aborts the socket. A peer that never answers the close
        /// handshake must not hang a request whose response already arrived.
        /// </summary>
        private static async Task CloseNormalAsync(WebSocket ws, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(kMessageTooBigCloseTimeout);
                await ws.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    string.Empty,
                    cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort: the socket is aborted either way.
            }
            ws.Abort();
        }

        private static Uri NormalizeUrl(Uri url)
        {
            if (string.Equals(url.Scheme, Utils.UriSchemeOpcWss, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(url) { Scheme = Utils.UriSchemeWss };
                if (url.IsDefaultPort)
                {
                    builder.Port = Utils.UaWebSocketsDefaultPort;
                }
                return builder.Uri;
            }
            return url;
        }

        /// <summary>
        /// Installs the TLS server validation callback and the optional TLS
        /// client certificate. Returns the caller-owned client certificate
        /// copy, which must stay alive until the WebSocket is disposed.
        /// </summary>
        private System.Security.Cryptography.X509Certificates.X509Certificate2? ConfigureClientTls(ClientWebSocket ws)
        {
#if NET5_0_OR_GREATER
            ICertificateValidatorEx? validator = m_settings?.CertificateValidator;
            if (validator != null)
            {
                ws.Options.RemoteCertificateValidationCallback =
                    (sender, cert, chain, errors) => ValidateRemoteCertificate(
                        validator,
                        cert as System.Security.Cryptography.X509Certificates.X509Certificate2,
                        chain,
                        errors);
            }

            Certificate? clientCert = m_settings?.ClientCertificate;
            if (clientCert != null)
            {
                System.Security.Cryptography.X509Certificates.X509Certificate2 x509 =
                    clientCert.AsX509Certificate2();
                ws.Options.ClientCertificates ??=
                    [];
                ws.Options.ClientCertificates.Add(x509);
                return x509;
            }
#endif
            return null;
        }

#if NET5_0_OR_GREATER
        private bool ValidateRemoteCertificate(
            ICertificateValidatorEx validator,
            System.Security.Cryptography.X509Certificates.X509Certificate2? cert,
            System.Security.Cryptography.X509Certificates.X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (cert == null)
            {
                return false;
            }
            try
            {
                if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateHostNameInvalid,
                        "The TLS certificate host name does not match the endpoint.");
                }
                using CertificateCollection validation = CertificateValidationHelpers
                    .BuildValidationCertificateCollection(cert, chain);
#pragma warning disable CA2025
                CertificateValidationResult result = validator
                    .ValidateAsync(validation, ct: default)
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore CA2025
                return result.IsValid;
            }
            catch (Exception ex)
            {
                m_logger.WssJsonFailedToValidateServerTlsCertificate(ex);
                return false;
            }
        }

#endif

        private static ServiceResultException BadNotConnected()
        {
            return ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "{0} not open.",
                nameof(WssJsonTransportChannel));
        }

        private const int kMessageTooBigCloseTimeout = 1000;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private Uri? m_url;
        private TransportChannelSettings? m_settings;
        private ServiceMessageContext? m_messageContext;
    }

    /// <summary>
    /// <see cref="ITransportChannelFactory"/> for the WSS <c>opcua+uajson</c>
    /// sub-protocol. Dispatched by <see cref="ClientChannelManager"/> via
    /// the internal pseudo-scheme <see cref="WssJsonTransportChannel.PseudoScheme"/>
    /// when an endpoint advertises <see cref="Profiles.UaWssJsonTransport"/>.
    /// </summary>
    public sealed class WssJsonTransportChannelFactory : ITransportChannelFactory
    {
        /// <inheritdoc/>
        public string UriScheme => WssJsonTransportChannel.PseudoScheme;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new WssJsonTransportChannel(telemetry);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="WssJsonTransportChannel"/>.
    /// </summary>
    internal static partial class WssJsonTransportChannelLog
    {
        [LoggerMessage(EventId = BindingsHttpsEventIds.WssJsonTransportChannel + 0, Level = LogLevel.Error,
            Message = "WSS+JSON request failed.")]
        public static partial void WssJsonRequestFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.WssJsonTransportChannel + 1, Level = LogLevel.Error,
            Message = "WssJsonTransportChannel: failed to validate server TLS certificate.")]
        public static partial void WssJsonFailedToValidateServerTlsCertificate(
            this ILogger logger,
            Exception exception);
    }
}
