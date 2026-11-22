// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    internal sealed class ClientChannel : ITransportChannel
    {
        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures => m_channel.SupportedFeatures;

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription => m_channel.EndpointDescription;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration => m_channel.EndpointConfiguration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => m_channel.MessageContext;

        /// <inheritdoc/>
        public byte[] ChannelThumbprint => m_channel?.ChannelThumbprint ?? [];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate => m_channel?.ClientChannelCertificate ?? [];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate => m_channel?.ServerChannelCertificate ?? [];

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => m_channel.OperationTimeout;
            set => m_channel.OperationTimeout = value;
        }

        internal ClientChannel(ClientChannelManager factory, ITransportChannel channel)
        {
            m_channel = channel;
            m_factory = factory;
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection,
            CancellationToken ct = default)
        {
            return m_channel.ReconnectAsync(connection, ct);
        }

        /// <inheritdoc/>
        public ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct = default)
        {
            return m_channel.SendRequestAsync(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            return m_channel.CloseAsync(ct);
        }

        public void Dispose()
        {
            m_factory.CloseChannel(m_channel);
        }

        private readonly ITransportChannel m_channel;
        private readonly ClientChannelManager m_factory;
    }
}
