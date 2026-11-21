// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Publishes the local Server's <c>EndpointDescription</c>s to an <see cref="ISharedKeyValueStore"/> as an
    /// integrity-protected record keyed by the local ServerUri.
    /// </summary>
    public sealed class SharedPeerEndpointPublisher : IPeerEndpointPublisher
    {
        /// <summary>
        /// Creates the publisher.
        /// </summary>
        /// <param name="store">The shared store the endpoints are gossiped through.</param>
        /// <param name="context">The message context used to encode records.</param>
        /// <param name="protector">Protects record integrity.</param>
        /// <param name="options">The load-direction options (endpoint key prefix).</param>
        /// <param name="localServerUri">The local ServerUri used as the record key.</param>
        /// <exception cref="ArgumentException"><paramref name="localServerUri"/> is null or empty.</exception>
        public SharedPeerEndpointPublisher(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector protector,
            LoadDirectionOptions options,
            string localServerUri)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(localServerUri))
            {
                throw new ArgumentException("The local ServerUri must be provided.", nameof(localServerUri));
            }
            m_localServerUri = localServerUri;
        }

        /// <inheritdoc/>
        public ValueTask PublishAsync(
            ArrayOf<EndpointDescription> endpoints,
            CancellationToken cancellationToken = default)
        {
            string key = m_options.EndpointKeyPrefix + m_localServerUri;
            ByteString payload = m_protector.Protect(
                RecordProtectionContext.Create("peer-endpoint", key),
                PeerEndpointCodec.Encode(endpoints, m_context));
            return m_store.SetAsync(key, payload, cancellationToken);
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly string m_localServerUri;
    }
}
