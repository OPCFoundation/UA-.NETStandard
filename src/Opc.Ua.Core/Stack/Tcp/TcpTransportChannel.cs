// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel with UA-TCP transport, UA-SC security
    /// and UA Binary encoding.
    /// </summary>
    public class TcpTransportChannel : UaSCUaBinaryTransportChannel
    {
        /// <summary>
        /// Create a Tcp transport channel.
        /// </summary>
        public TcpTransportChannel(ITelemetryContext telemetry)
            : this(telemetry, DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a TCP transport channel with a buffer-manager factory.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use.</param>
        /// <param name="bufferManagerFactory">Factory used to create channel buffer managers.</param>
        public TcpTransportChannel(
            ITelemetryContext telemetry,
            IBufferManagerFactory bufferManagerFactory)
            : base(
                new TcpByteTransportFactory(telemetry),
                telemetry,
                timeProvider: null,
                bufferManagerFactory: bufferManagerFactory)
        {
        }
    }

    /// <summary>
    /// Creates a new <see cref="TcpTransportChannel"/> with <see cref="ITransportChannel"/> interface.
    /// </summary>
    public class TcpTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// Creates a factory using the default buffer-manager factory.
        /// </summary>
        public TcpTransportChannelFactory()
            : this(DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a factory using the specified buffer-manager factory.
        /// </summary>
        /// <param name="bufferManagerFactory">Factory used to create channel buffer managers.</param>
        public TcpTransportChannelFactory(IBufferManagerFactory bufferManagerFactory)
        {
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new System.ArgumentNullException(nameof(bufferManagerFactory));
        }

        /// <summary>
        /// The protocol supported by the channel.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <summary>
        /// Creates a new instance of a TCP transport channel.
        /// </summary>
        /// <returns>The transport channel.</returns>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new TcpTransportChannel(telemetry, m_bufferManagerFactory);
        }

        private readonly IBufferManagerFactory m_bufferManagerFactory;
    }
}
