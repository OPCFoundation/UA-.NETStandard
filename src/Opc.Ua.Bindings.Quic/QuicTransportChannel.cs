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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A transport channel that carries the OPC UA Secure Conversation
    /// over QUIC, with UA Binary encoding.
    /// </summary>
    /// <remarks>
    /// The conversation itself is unchanged: the control stream carries
    /// HEL, ACK, ERR, OPN, MSG and CLO byte for byte as they appear over
    /// opc.tcp. Only the framing below the MessageChunk differs, and only
    /// data channels use the streams beside it.
    /// </remarks>
    public sealed class QuicTransportChannel : UaSCUaBinaryTransportChannel
    {
        /// <summary>
        /// Creates a QUIC transport channel.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        public QuicTransportChannel(ITelemetryContext telemetry)
            : this(telemetry, DefaultBufferManagerFactory.Instance, (QuicClientOptions?)null)
        {
        }

        /// <summary>
        /// Creates a QUIC transport channel.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="bufferManagerFactory">Factory used to create
        /// channel buffer managers.</param>
        /// <param name="options">How the connection is established, or
        /// null for the defaults.</param>
        public QuicTransportChannel(
            ITelemetryContext telemetry,
            IBufferManagerFactory bufferManagerFactory,
            QuicClientOptions? options)
            : this(
                telemetry,
                bufferManagerFactory,
                new QuicByteTransportFactory(telemetry, options))
        {
        }

        private QuicTransportChannel(
            ITelemetryContext telemetry,
            IBufferManagerFactory bufferManagerFactory,
            QuicByteTransportFactory transportFactory)
            : base(
                transportFactory,
                telemetry,
                timeProvider: null,
                bufferManagerFactory: bufferManagerFactory)
        {
            m_transportFactory = transportFactory;
        }

        /// <inheritdoc/>
        protected override void OnSettingsSaved(
            TransportChannelSettings settings,
            ChannelQuotas quotas)
        {
            _ = quotas;
            m_transportFactory.SetEndpointDescription(settings.Description);
            m_transportFactory.SetClientCertificate(settings.ClientCertificate);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_transportFactory.DisposeClientCertificate();
            }

            base.Dispose(disposing);
        }

        private readonly QuicByteTransportFactory m_transportFactory;
    }

    /// <summary>
    /// Creates <see cref="QuicTransportChannel"/> instances for the
    /// <c>opc.quic</c> url scheme.
    /// </summary>
    public sealed class QuicTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// Creates a factory using the default buffer-manager factory.
        /// </summary>
        public QuicTransportChannelFactory()
            : this(DefaultBufferManagerFactory.Instance, null)
        {
        }

        /// <summary>
        /// Creates a factory.
        /// </summary>
        /// <param name="bufferManagerFactory">Factory used to create
        /// channel buffer managers.</param>
        /// <param name="options">How connections are established.</param>
        public QuicTransportChannelFactory(
            IBufferManagerFactory bufferManagerFactory,
            QuicClientOptions? options)
        {
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new ArgumentNullException(nameof(bufferManagerFactory));
            m_options = options;
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcQuic;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new QuicTransportChannel(telemetry, m_bufferManagerFactory, m_options);
        }

        private readonly IBufferManagerFactory m_bufferManagerFactory;
        private readonly QuicClientOptions? m_options;
    }
}
