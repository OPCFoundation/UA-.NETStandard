// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Reads a peer's published <c>EndpointDescription</c>s from an <see cref="ISharedKeyValueStore"/>, verifying
    /// record integrity, membership, endpoint identity and age (fail-closed).
    /// </summary>
    public sealed class SharedPeerEndpointDirectory : IPeerEndpointDirectory
    {
        /// <summary>
        /// Creates the directory.
        /// </summary>
        /// <param name="store">The shared store the endpoints are gossiped through.</param>
        /// <param name="context">The message context used to decode records.</param>
        /// <param name="protector">Verifies record integrity; forged/tampered records are dropped.</param>
        /// <param name="options">The load-direction options.</param>
        public SharedPeerEndpointDirectory(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector protector,
            LoadDirectionOptions options)
            : this(store, context, protector, options, TimeProvider.System)
        {
        }

        /// <summary>
        /// Creates the directory with an explicit clock for freshness checks.
        /// </summary>
        public SharedPeerEndpointDirectory(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector protector,
            LoadDirectionOptions options,
            TimeProvider timeProvider)
            : this(store, context, protector, options, timeProvider,
                (options ?? throw new ArgumentNullException(nameof(options))).PeerApplicationUris)
        {
        }

        /// <summary>
        /// Creates the directory with the resolved static member list.
        /// </summary>
        internal SharedPeerEndpointDirectory(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector protector,
            LoadDirectionOptions options,
            TimeProvider timeProvider,
            ArrayOf<string> peerApplicationUris)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = LoadDirectionRecordValidation.RequireProtection(m_store, protector);
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_peerApplicationUris = LoadDirectionRecordValidation.SnapshotMembers(peerApplicationUris);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string serverUri,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(serverUri) || !m_peerApplicationUris.Contains(serverUri))
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
                PeerEndpointCodec.TryDecode(
                    payload, m_context, out ArrayOf<EndpointDescription> endpoints, out long recordTicks) &&
                LoadDirectionRecordValidation.IsCurrent(
                    recordTicks, m_timeProvider.GetUtcNow().UtcDateTime.Ticks, m_options.StalenessWindow) &&
                LoadDirectionRecordValidation.MatchesMember(endpoints, serverUri))
            {
                return endpoints;
            }

            return [];
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly HashSet<string> m_peerApplicationUris;
    }
}
