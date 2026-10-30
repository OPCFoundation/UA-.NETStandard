// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Owns a native UDP listener. Disposing it stops admission, not already returned data contexts.
    /// </summary>
    public sealed class WebTransportDataChannelListener : IAsyncDisposable
    {
        /// <summary>
        /// Adopts an already listening native endpoint and its immutable configured policy.
        /// </summary>
        internal WebTransportDataChannelListener(
            NativeQuicListener listener,
            NativeHttp3Library library,
            IDataChannelContextFactory factory,
            DataChannelApplicationBudget budget,
            Uri endpointUrl,
            WebTransportDataChannelOptions options,
            IWebTransportConnectAuthorizer authorizer,
            TimeProvider clock)
        {
            m_listener = listener;
            m_library = library;
            m_factory = factory;
            m_budget = budget;
            EndpointUrl = endpointUrl;
            m_options = options;
            m_authorizer = authorizer;
            m_clock = clock;
        }

        /// <summary>
        /// Gets the actual bound endpoint including the exact CONNECT resource.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Gets whether the real native listener is admitting connections.
        /// </summary>
        public bool IsListening => m_listener.IsListening;

        /// <summary>
        /// Gets actual listener close and outstanding accept drainage.
        /// </summary>
        public Task Completion => m_listener.Completion;

        /// <summary>
        /// Returns one owned connection only after authenticated H3/CONNECT/HELLO establishment.
        /// A failed CONNECT is never retried on the same TLS connection.
        /// </summary>
        public async ValueTask<WebTransportDataChannelConnection> AcceptAsync(
            CancellationToken cancellationToken = default)
        {
            NativeQuicConnection native = await m_listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return await WebTransportDataChannelConnection.EstablishAsync(
                native, m_library, m_factory, m_budget, EndpointUrl, m_options, m_authorizer, m_clock,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_listener.DisposeAsync();
        }

        private readonly NativeQuicListener m_listener;
        private readonly NativeHttp3Library m_library;
        private readonly IDataChannelContextFactory m_factory;
        private readonly DataChannelApplicationBudget m_budget;
        private readonly WebTransportDataChannelOptions m_options;
        private readonly IWebTransportConnectAuthorizer m_authorizer;
        private readonly TimeProvider m_clock;
    }
}
