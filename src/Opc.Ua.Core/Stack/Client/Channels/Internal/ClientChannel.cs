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
