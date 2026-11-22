// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Publishes the local Server's direction signals to an <see cref="ISharedKeyValueStore"/> as integrity-protected,
    /// timestamped records keyed by the local ServerUri.
    /// </summary>
    public sealed class SharedPeerDirectionPublisher : IPeerDirectionPublisher
    {
        /// <summary>
        /// Creates the publisher.
        /// </summary>
        /// <param name="store">The shared store the signals are gossiped through.</param>
        /// <param name="context">The message context used to encode records.</param>
        /// <param name="protector">Protects record integrity.</param>
        /// <param name="options">The load-direction options (key prefixes).</param>
        /// <param name="timeProvider">The time source for record timestamps.</param>
        /// <param name="localServerUri">The local ServerUri used as the record key.</param>
        /// <exception cref="ArgumentException"><paramref name="localServerUri"/> is null or empty.</exception>
        public SharedPeerDirectionPublisher(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector protector,
            LoadDirectionOptions options,
            TimeProvider timeProvider,
            string localServerUri)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (string.IsNullOrEmpty(localServerUri))
            {
                throw new ArgumentException("The local ServerUri must be provided.", nameof(localServerUri));
            }
            m_localServerUri = localServerUri;
        }

        /// <inheritdoc/>
        public ValueTask PublishServiceLevelAsync(byte serviceLevel, CancellationToken cancellationToken = default)
        {
            return PublishAsync(m_options.ServiceLevelKeyPrefix, serviceLevel, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask PublishLoadWeightAsync(byte loadWeight, CancellationToken cancellationToken = default)
        {
            return PublishAsync(m_options.LoadKeyPrefix, loadWeight, cancellationToken);
        }

        private ValueTask PublishAsync(string keyPrefix, byte value, CancellationToken cancellationToken)
        {
            long ticks = m_timeProvider.GetUtcNow().UtcDateTime.Ticks;
            string key = keyPrefix + m_localServerUri;
            ByteString payload = m_protector.Protect(
                RecordProtectionContext.Create("peer-direction", key),
                PeerDirectionCodec.Encode(m_localServerUri, value, ticks, m_context));
            return m_store.SetAsync(key, payload, cancellationToken);
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly string m_localServerUri;
    }
}
