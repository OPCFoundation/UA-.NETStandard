// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Direct and injectable native WebTransport endpoint factory. No fallback transport or Session is installed.
    /// </summary>
    public sealed class WebTransportDataChannelTransport
    {
        /// <summary>
        /// Borrows explicit native/credential/context providers and one application-scoped budget.
        /// Returned listeners and connections are caller-owned.
        /// </summary>
        public WebTransportDataChannelTransport(
            INativeQuicBackend backend,
            NativeHttp3Library http3Library,
            INativeQuicCredentialProvider credentialProvider,
            IDataChannelContextFactory contextFactory,
            DataChannelApplicationBudget applicationBudget,
            WebTransportDataChannelOptions options,
            IWebTransportConnectAuthorizer? connectAuthorizer = null,
            TimeProvider? timeProvider = null)
        {
            m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
            m_library = http3Library ?? throw new ArgumentNullException(nameof(http3Library));
            m_credentials = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            m_factory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            m_budget = applicationBudget ?? throw new ArgumentNullException(nameof(applicationBudget));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            m_authorizer = connectAuthorizer;
            m_clock = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Establishes one fresh h3/mTLS connection, exact WebTransport CONNECT and common HELLO.
        /// No application flow is opened implicitly.
        /// </summary>
        public async ValueTask<WebTransportDataChannelConnection> ConnectAsync(
            Uri endpointUrl,
            CancellationToken cancellationToken = default)
        {
            string address = WebTransportEndpoint.Address(endpointUrl, false);
            await m_library.ValidateAsync(cancellationToken).ConfigureAwait(false);
            NativeQuicConnection native = await NativeQuicConnection.ConnectAsync(
                m_backend, m_credentials, address, DataChannelProfiles.WebTransportAlpn, endpointUrl.DnsSafeHost,
                m_options.Native, m_clock, cancellationToken).ConfigureAwait(false);
            return await WebTransportDataChannelConnection.EstablishAsync(
                native, m_library, m_factory, m_budget, endpointUrl, m_options, m_authorizer, m_clock,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a real numeric UDP listener with explicit CONNECT authorization.
        /// Port zero returns the actual bound authority while preserving the exact path/query.
        /// </summary>
        public ValueTask<WebTransportDataChannelListener> ListenAsync(
            Uri endpointUrl,
            CancellationToken cancellationToken = default)
        {
            string address = WebTransportEndpoint.Address(endpointUrl, true);
            return ListenCoreAsync(endpointUrl, address, cancellationToken);
        }

        /// <summary>
        /// Binds an explicit local UDP address independently of the exact public CONNECT authority.
        /// This supports DNS authorities, wildcard local interfaces and explicit port forwarding without pooling.
        /// </summary>
        public ValueTask<WebTransportDataChannelListener> ListenAsync(
            Uri endpointUrl,
            IPEndPoint localEndpoint,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(localEndpoint);
            WebTransportEndpoint.Address(endpointUrl, true, false);
            return ListenCoreAsync(endpointUrl, localEndpoint.ToString(), cancellationToken);
        }

        /// <summary>
        /// Opens the selected local listener without changing the configured HTTP endpoint policy.
        /// </summary>
        private async ValueTask<WebTransportDataChannelListener> ListenCoreAsync(
            Uri endpointUrl,
            string address,
            CancellationToken cancellationToken)
        {
            if (m_authorizer == null)
            {
                throw new InvalidOperationException("A WebTransport listener requires explicit CONNECT authorization.");
            }
            await m_library.ValidateAsync(cancellationToken).ConfigureAwait(false);
            NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_backend, m_credentials, address, DataChannelProfiles.WebTransportAlpn, m_options.Native, m_clock,
                cancellationToken).ConfigureAwait(false);
            return new WebTransportDataChannelListener(listener, m_library, m_factory, m_budget,
                WebTransportEndpoint.WithBoundPort(endpointUrl, listener.Address),
                m_options, m_authorizer, m_clock);
        }

        private readonly INativeQuicBackend m_backend;
        private readonly NativeHttp3Library m_library;
        private readonly INativeQuicCredentialProvider m_credentials;
        private readonly IDataChannelContextFactory m_factory;
        private readonly DataChannelApplicationBudget m_budget;
        private readonly WebTransportDataChannelOptions m_options;
        private readonly IWebTransportConnectAuthorizer? m_authorizer;
        private readonly TimeProvider m_clock;
    }

    /// <summary>
    /// Adds the optional WebTransport factory without advertising an endpoint or replacing a context factory.
    /// </summary>
    public static class WebTransportDataChannelBuilderExtensions
    {
        /// <summary>
        /// Registers WebTransport. Register native backend, certificate provider, common context factory/budget,
        /// and explicit listener CONNECT authorizer separately. AddDataChannels supports standalone usage.
        /// </summary>
        public static IOpcUaBuilder AddWebTransportDataChannels(
            this IOpcUaBuilder builder,
            NativeHttp3Library http3Library,
            WebTransportDataChannelOptions options)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(http3Library);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            builder.Services.TryAddSingleton(http3Library);
            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<WebTransportDataChannelTransport>();
            return builder;
        }
    }
}
