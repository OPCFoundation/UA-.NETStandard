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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    internal interface ITransportChannelBindingProvider
    {
        ValueTask<TransportChannelBinding> CreateTransportBindingAsync(CancellationToken ct);
    }

    internal sealed class TransportChannelBinding : ITransportChannel
    {
        internal TransportChannelBinding(
            ITransportChannel source,
            Func<bool> valid,
            Func<IServiceRequest, Func<bool>, CancellationToken, ValueTask<IServiceResponse>> send)
        {
            m_sourceContext = source.MessageContext;
            m_valid = () => valid() && ReferenceEquals(source.MessageContext, m_sourceContext);
            m_send = send;
            m_channelThumbprint = [.. source.ChannelThumbprint];
            m_clientCertificate = [.. source.ClientChannelCertificate];
            m_serverCertificate = [.. source.ServerChannelCertificate];
            EndpointDescription endpoint = source.EndpointDescription;
            EndpointDescription = new EndpointDescription
            {
                EndpointUrl = endpoint.EndpointUrl,
                Server = new ApplicationDescription
                {
                    ApplicationUri = endpoint.Server.ApplicationUri,
                    ApplicationName = endpoint.Server.ApplicationName,
                    ApplicationType = endpoint.Server.ApplicationType,
                    ProductUri = endpoint.Server.ProductUri,
                    GatewayServerUri = endpoint.Server.GatewayServerUri,
                    DiscoveryProfileUri = endpoint.Server.DiscoveryProfileUri,
                    DiscoveryUrls = endpoint.Server.DiscoveryUrls
                },
                ServerCertificate = endpoint.ServerCertificate,
                SecurityMode = endpoint.SecurityMode,
                SecurityPolicyUri = endpoint.SecurityPolicyUri,
                SecurityLevel = endpoint.SecurityLevel,
                TransportProfileUri = endpoint.TransportProfileUri,
                UserIdentityTokens = endpoint.UserIdentityTokens
            };
            EndpointConfiguration = source.EndpointConfiguration;
            MessageContext = CopyContext(m_sourceContext);
            OperationTimeout = source.OperationTimeout;
        }

        public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

        public EndpointDescription EndpointDescription { get; }

        public EndpointConfiguration EndpointConfiguration { get; }

        public byte[] ChannelThumbprint => [.. m_channelThumbprint];

        public byte[] ClientChannelCertificate => [.. m_clientCertificate];

        public byte[] ServerChannelCertificate => [.. m_serverCertificate];

        public IServiceMessageContext MessageContext { get; private set; }

        public int OperationTimeout { get; set; }

        internal bool IsCurrent
        {
            get
            {
                if (Volatile.Read(ref m_invalidated) != 0)
                {
                    return false;
                }
                if (!m_valid())
                {
                    Interlocked.Exchange(ref m_invalidated, 1);
                    return false;
                }
                return Volatile.Read(ref m_invalidated) == 0;
            }
        }

        public ValueTask<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfInvalid();
            return m_send(request, () => IsCurrent, ct);
        }

        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "A captured binding does not reconnect.");
        }

        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Dispose();
            return default;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref m_invalidated, 1);
        }

        internal bool HasSourceContext(IServiceMessageContext context)
        {
            return ReferenceEquals(m_sourceContext, context);
        }

        internal void AddValidation(
            Func<bool> valid,
            IServiceMessageContext? context = null)
        {
            ThrowIfInvalid();
            Func<bool> previous = m_valid;
            m_valid = () => previous() && valid();
            MessageContext = context ?? MessageContext;
        }

        internal void ThrowIfInvalid()
        {
            if (!IsCurrent)
            {
                throw InvalidBinding();
            }
        }

        internal static ServiceResultException InvalidBinding()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The captured session/channel binding is no longer current.");
        }

        internal static ServiceMessageContext CopyContext(
            IServiceMessageContext source,
            NamespaceTable? namespaces = null,
            StringTable? servers = null)
        {
            return new ServiceMessageContext(source.Telemetry, source.Factory)
            {
                NamespaceUris = namespaces ?? new NamespaceTable(source.NamespaceUris.ToArray()),
                ServerUris = servers ?? new StringTable(source.ServerUris.ToArray()),
                MaxStringLength = source.MaxStringLength,
                MaxByteStringLength = source.MaxByteStringLength,
                MaxArrayLength = source.MaxArrayLength,
                MaxMessageSize = source.MaxMessageSize,
                MaxEncodingNestingLevels = source.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = source.MaxDecoderRecoveries
            };
        }

        private readonly byte[] m_channelThumbprint;
        private readonly byte[] m_clientCertificate;
        private readonly byte[] m_serverCertificate;
        private readonly IServiceMessageContext m_sourceContext;
        private Func<bool> m_valid;
        private readonly Func<IServiceRequest, Func<bool>, CancellationToken, ValueTask<IServiceResponse>> m_send;
        private int m_invalidated;
    }
}
