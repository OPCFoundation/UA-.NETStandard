// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Reads a peer's published <c>EndpointDescription</c>s from an <see cref="ISharedKeyValueStore"/>, verifying
    /// record integrity (fail-closed).
    /// </summary>
    public sealed class SharedPeerEndpointDirectory : IPeerEndpointDirectory
    {
        /// <summary>
        /// Creates the directory.
        /// </summary>
        /// <param name="store">The shared store the endpoints are gossiped through.</param>
        /// <param name="context">The message context used to decode records.</param>
        /// <param name="protector">Verifies record integrity; forged/tampered records are dropped.</param>
        /// <param name="options">The load-direction options (endpoint key prefix).</param>
        public SharedPeerEndpointDirectory(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector protector,
            LoadDirectionOptions options)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string serverUri,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(serverUri))
            {
                return [];
            }

            string key = m_options.EndpointKeyPrefix + serverUri;
            (bool found, ByteString value) = await m_store
                .TryGetAsync(key, cancellationToken)
                .ConfigureAwait(false);

            if (found &&
                m_protector.TryUnprotect(
                    RecordProtectionContext.Create("peer-endpoint", key), value, out ByteString payload) &&
                PeerEndpointCodec.TryDecode(payload, m_context, out ArrayOf<EndpointDescription> endpoints))
            {
                return endpoints;
            }

            return [];
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
    }
}
