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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Factory for <see cref="WebSocketClientByteTransport"/>; used by the
    /// client-side WSS transport channel.
    /// </summary>
    internal sealed class WebSocketClientByteTransportFactory : IUaSCByteTransportFactory
    {
        public WebSocketClientByteTransportFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
        }

        /// <inheritdoc/>
        public string Implementation => "UA-WSS";

        /// <summary>
        /// Optional OPC UA certificate validator invoked from the
        /// <c>RemoteCertificateValidationCallback</c> wired into every
        /// <see cref="System.Net.WebSockets.ClientWebSocket"/> the factory
        /// produces; set by <see cref="WssTransportChannel"/> after channel
        /// settings are bound.
        /// </summary>
        internal ICertificateValidatorEx? CertificateValidator { get; set; }

        /// <summary>
        /// Optional client TLS certificate added to
        /// <c>ClientWebSocketOptions.ClientCertificates</c> for servers
        /// that require mutual TLS authentication; set by
        /// <see cref="WssTransportChannel"/> after channel settings are bound.
        /// </summary>
        internal Opc.Ua.Security.Certificates.Certificate? ClientTlsCertificate { get; set; }

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            return new WebSocketClientByteTransport(
                bufferManager,
                receiveBufferSize,
                telemetry ?? m_telemetry)
            {
                CertificateValidator = CertificateValidator,
                ClientTlsCertificate = ClientTlsCertificate
            };
        }

        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Creates a transport channel with WebSocket Secure transport, UA-SC
    /// security and UA Binary encoding (the <c>opcua+uacp</c> WebSocket
    /// sub-protocol defined by OPC UA Part 6 §7.5.2).
    /// </summary>
    public class WssTransportChannel : UaSCUaBinaryTransportChannel
    {
        /// <summary>
        /// Create a new WSS transport channel.
        /// </summary>
        public WssTransportChannel(ITelemetryContext telemetry)
            : this(new WebSocketClientByteTransportFactory(telemetry), telemetry)
        {
        }

        private WssTransportChannel(
            WebSocketClientByteTransportFactory factory,
            ITelemetryContext telemetry)
            : base(factory, telemetry)
        {
            m_factory = factory;
        }

        /// <inheritdoc/>
        protected override void OnSettingsSaved(
            TransportChannelSettings settings,
            ChannelQuotas quotas)
        {
            // Push the OPC UA certificate validator + (optional) client TLS
            // certificate down to the factory so every ClientWebSocket
            // produced for this channel uses them at the TLS layer.
            m_factory.CertificateValidator = quotas?.CertificateValidator;
            m_factory.ClientTlsCertificate = settings?.ClientCertificate;
        }

        private readonly WebSocketClientByteTransportFactory m_factory;
    }

    /// <summary>
    /// <see cref="ITransportChannelFactory"/> for the standard <c>wss://</c>
    /// URL scheme.
    /// </summary>
    public class WssTransportChannelFactory : ITransportChannelFactory
    {
        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeWss;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new WssTransportChannel(telemetry);
        }
    }

    /// <summary>
    /// <see cref="ITransportChannelFactory"/> for the OPC UA <c>opc.wss://</c>
    /// URL scheme alias.
    /// </summary>
    public class OpcWssTransportChannelFactory : ITransportChannelFactory
    {
        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcWss;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new WssTransportChannel(telemetry);
        }
    }
}
