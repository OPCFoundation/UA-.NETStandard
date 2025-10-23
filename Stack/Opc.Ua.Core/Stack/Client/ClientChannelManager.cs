/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#nullable enable

using System;
using Opc.Ua.Bindings;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Client side transport channel factory. Manages creation
    /// of transport channels for clients.
    /// </summary>
    public sealed class ClientChannelManager : ITransportChannelManager
    {
        /// <summary>
        /// Callback to register channel diagnostics
        /// </summary>
        public event Action<ITransportChannel, TransportChannelDiagnostic>? OnDiagnostics;

        /// <summary>
        /// Create client channel factory.
        /// </summary>
        /// <param name="configuration">The application configuration to use</param>
        /// <param name="channelFactory">An optional factory to create channel types
        /// from. Uses the default channel bindings if none is provided</param>
        public ClientChannelManager(
            ApplicationConfiguration configuration,
            ITransportChannelBindings? channelFactory = null)
        {
            m_configuration = configuration;
            m_channelFactory = channelFactory;
        }

        /// <inheritdoc/>
        public async ValueTask<ITransportChannel> CreateChannelAsync(
            ConfiguredEndpoint endpoint,
            IServiceMessageContext context,
            X509Certificate2? clientCertificate,
            X509Certificate2Collection? clientCertificateChain = null,
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            // initialize the channel which will be created with the server.
            ITransportChannel channel;
            if (connection != null)
            {
                channel = await CreateUaBinaryChannelAsync(
                    m_configuration,
                    connection,
                    endpoint.Description,
                    endpoint.Configuration,
                    clientCertificate,
                    clientCertificateChain,
                    context,
                    m_channelFactory,
                    ct).ConfigureAwait(false);
            }
            else
            {
                channel = await CreateUaBinaryChannelAsync(
                    m_configuration,
                    endpoint.Description,
                    endpoint.Configuration,
                    clientCertificate,
                    clientCertificateChain,
                    context,
                    m_channelFactory,
                    ct).ConfigureAwait(false);
            }
            if (channel is ISecureChannel secureChannel)
            {
                secureChannel.OnTokenActivated += OnChannelTokenActivated;
            }
            return channel;
        }

        /// <summary>
        /// Called when channel is disposed
        /// </summary>
        /// <param name="channel"></param>
        internal void CloseChannel(ITransportChannel channel)
        {
            if (channel is ISecureChannel secureChannel)
            {
                secureChannel.OnTokenActivated -= OnChannelTokenActivated;
            }
            channel.Dispose();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2? clientCertificate,
            X509Certificate2Collection? clientCertificateChain,
            IServiceMessageContext messageContext,
            ITransportChannelBindings? transportChannelBindings = null,
            CancellationToken ct = default)
        {
            // initialize the channel which will be created with the server.
            string uriScheme = new Uri(description.EndpointUrl).Scheme;
            transportChannelBindings ??= TransportBindings.Channels;
            ITransportChannel channel =
                transportChannelBindings.Create(uriScheme, messageContext.Telemetry)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.",
                    uriScheme);

            if (channel is not ISecureChannel secureChannel)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The transport channel does not support opening.");
            }

            // create a UA channel.
            var settings = new TransportChannelSettings
            {
                Description = description,
                Configuration = endpointConfiguration,
                ClientCertificate = clientCertificate,
                ClientCertificateChain = clientCertificateChain
            };

            if (description.ServerCertificate != null && description.ServerCertificate.Length > 0)
            {
                settings.ServerCertificate = Utils.ParseCertificateBlob(
                    description.ServerCertificate,
                    messageContext.Telemetry);
            }

            if (configuration != null)
            {
                settings.CertificateValidator = configuration.CertificateValidator
                    .GetChannelValidator();
            }

            settings.NamespaceUris = messageContext.NamespaceUris;
            settings.Factory = messageContext.Factory;

            await secureChannel.OpenAsync(connection, settings, ct).ConfigureAwait(false);

            return channel;
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="transportChannelBindings">Optional bindings to use</param>
        /// <param name="ct">The cancellation token</param>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2? clientCertificate,
            X509Certificate2Collection? clientCertificateChain,
            IServiceMessageContext messageContext,
            ITransportChannelBindings? transportChannelBindings = null,
            CancellationToken ct = default)
        {
            string uriScheme = description.TransportProfileUri switch
            {
                Profiles.UaTcpTransport => Utils.UriSchemeOpcTcp,
                Profiles.HttpsBinaryTransport => Utils.UriSchemeOpcHttps,
                Profiles.UaWssTransport => Utils.UriSchemeOpcWss,
                _ => new Uri(description.EndpointUrl).Scheme
            };

            // initialize the channel which will be created with the server.
            transportChannelBindings ??= TransportBindings.Channels;
            ITransportChannel channel =
                transportChannelBindings.Create(uriScheme, messageContext.Telemetry)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.",
                    uriScheme);

            if (channel is not ISecureChannel secureChannel)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The transport channel does not support opening.");
            }

            // create a UA-TCP channel.
            var settings = new TransportChannelSettings
            {
                Description = description,
                Configuration = endpointConfiguration,
                ClientCertificate = clientCertificate,
                ClientCertificateChain = clientCertificateChain
            };

            if (description.ServerCertificate != null && description.ServerCertificate.Length > 0)
            {
                settings.ServerCertificate = Utils.ParseCertificateBlob(
                    description.ServerCertificate,
                    messageContext.Telemetry);
            }

            if (configuration != null)
            {
                settings.CertificateValidator = configuration.CertificateValidator
                    .GetChannelValidator();
            }

            settings.NamespaceUris = messageContext.NamespaceUris;
            settings.Factory = messageContext.Factory;

            await secureChannel.OpenAsync(
                new Uri(description.EndpointUrl),
                settings,
                ct).ConfigureAwait(false);
            return channel;
        }

        /// <summary>
        /// Called when the token is changing
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="token"></param>
        /// <param name="previousToken"></param>
        internal void OnChannelTokenActivated(
            ITransportChannel channel,
            ChannelToken? token,
            ChannelToken? previousToken)
        {
            if (token == null || OnDiagnostics == null)
            {
                // Closed
                return;
            }

            if (previousToken == null)
            {
                // Created
            }

            // Get effective ip address and port
            IMessageSocket? socket = (channel as IMessageSocketChannel)?.Socket;
            IPAddress? remoteIpAddress = GetIPAddress(socket?.RemoteEndpoint);
            int remotePort = GetPort(socket?.RemoteEndpoint);
            IPAddress? localIpAddress = GetIPAddress(socket?.LocalEndpoint);
            int localPort = GetPort(socket?.LocalEndpoint);

            OnDiagnostics.Invoke(channel, new TransportChannelDiagnostic
            {
                Endpoint = channel.EndpointDescription,
                TimeStamp = DateTimeOffset.UtcNow,
                RemoteIpAddress = remoteIpAddress,
                RemotePort = remotePort == -1 ? null : remotePort,
                LocalIpAddress = localIpAddress,
                LocalPort = localPort == -1 ? null : localPort,
                ChannelId = token.ChannelId,
                TokenId = token.TokenId,
                CreatedAt = token.CreatedAt,
                Lifetime = TimeSpan.FromMilliseconds(token.Lifetime),
                Client = ToChannelKey(token.ClientInitializationVector,
                    token.ClientEncryptingKey, token.ClientSigningKey),
                Server = ToChannelKey(token.ServerInitializationVector,
                    token.ServerEncryptingKey, token.ServerSigningKey)
            });

            static ChannelKey? ToChannelKey(byte[]? iv, byte[]? key, byte[]? sk)
            {
                if (iv == null ||
                    key == null ||
                    sk == null ||
                    iv.Length == 0 ||
                    key.Length == 0 ||
                    sk.Length == 0)
                {
                    return null;
                }
                return new ChannelKey(iv, key, sk.Length);
            }
        }

        /// <summary>
        /// Get ip address from endpoint if the endpoint is an
        /// IPEndPoint.  Otherwise return null.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="preferv4"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        internal static IPAddress? GetIPAddress(EndPoint? endpoint, bool preferv4 = false)
        {
            if (endpoint is not IPEndPoint ipe)
            {
                return null;
            }
            IPAddress address = ipe.Address;
            if (preferv4 &&
                address.AddressFamily == AddressFamily.InterNetworkV6 &&
                address.IsIPv4MappedToIPv6)
            {
                return address.MapToIPv4();
            }
            return address;
        }

        /// <summary>
        /// Get port from endpoint if the endpoint is an
        /// IPEndPoint.  Otherwise return -1.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        internal static int GetPort(EndPoint? endpoint)
        {
            if (endpoint is IPEndPoint ipe)
            {
                return ipe.Port;
            }
            return -1;
        }

        /// <summary>
        /// Client channel wrapper
        /// </summary>
        private sealed class ClientChannel : ITransportChannel
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
            public int OperationTimeout
            {
                get => m_channel.OperationTimeout;
                set => m_channel.OperationTimeout = value;
            }

            public ClientChannel(ClientChannelManager factory, ITransportChannel channel)
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
            public Task<IServiceResponse> SendRequestAsync(
                IServiceRequest request,
                CancellationToken ct = default)
            {
                return m_channel.SendRequestAsync(request, ct);
            }

            /// <inheritdoc/>
            public Task CloseAsync(CancellationToken ct = default)
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

        private readonly ApplicationConfiguration m_configuration;
        private readonly ITransportChannelBindings? m_channelFactory;
    }
}
