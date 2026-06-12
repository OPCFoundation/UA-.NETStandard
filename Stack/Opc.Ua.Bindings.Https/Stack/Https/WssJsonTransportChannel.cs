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
        public void Dispose() => Dispose(true);

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
        public ValueTask CloseAsync(CancellationToken ct = default) => default;

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
            try
            {
                ws.Options.AddSubProtocol(Profiles.OpcUaWsSubProtocolUaJson);
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
                        encoder.Close();
                    }
                    payload = memory.ToArray();
                }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
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

                byte[] responseBytes;
                using (var memory = new MemoryStream())
                {
                    byte[] receiveBuffer = new byte[8192];
                    while (true)
                    {
                        WebSocketReceiveResult result = await ws
                            .ReceiveAsync(
                                new ArraySegment<byte>(receiveBuffer),
                                cts.Token)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConnectionClosed,
                                "Server closed the WebSocket before sending a response.");
                        }
                        if (result.Count > 0)
                        {
                            memory.Write(receiveBuffer, 0, result.Count);
                        }
                        if (result.EndOfMessage)
                        {
                            break;
                        }
                    }
                    responseBytes = memory.ToArray();
                }

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
                m_logger.LogError(ex, "WSS+JSON request failed.");
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
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            string.Empty,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Best-effort.
                }
                ws.Dispose();
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

        private static ServiceResultException BadNotConnected()
        {
            return ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "{0} not open.",
                nameof(WssJsonTransportChannel));
        }

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
}
