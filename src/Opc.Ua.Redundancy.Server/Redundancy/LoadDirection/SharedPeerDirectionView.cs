// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Reads the per-peer health <c>ServiceLevel</c> and load-weight signals from an
    /// <see cref="ISharedKeyValueStore"/>, verifying record integrity (fail-closed) and aging out stale entries.
    /// </summary>
    public sealed class SharedPeerDirectionView : IPeerDirectionView
    {
        /// <summary>
        /// Creates the view.
        /// </summary>
        /// <param name="store">The shared store the signals are gossiped through.</param>
        /// <param name="context">The message context used to decode records.</param>
        /// <param name="protector">Verifies record integrity; forged/tampered records are dropped.</param>
        /// <param name="options">The load-direction options (members, key prefixes and age window).</param>
        /// <param name="timeProvider">The time source for staleness checks.</param>
        public SharedPeerDirectionView(
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
        /// Creates the view with the resolved static member list.
        /// </summary>
        internal SharedPeerDirectionView(
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
        public async ValueTask<ArrayOf<PeerDirectionRecord>> GetPeersAsync(
            CancellationToken cancellationToken = default)
        {
            var signals = new List<(string ServerUri, byte ServiceLevel, long HealthTicks,
                byte LoadWeight, long LoadTicks, bool LoadFound)>(m_peerApplicationUris.Count);
            foreach (string serverUri in m_peerApplicationUris)
            {
                (bool healthFound, byte serviceLevel, long healthTicks) = await ReadSignalAsync(
                    m_options.ServiceLevelKeyPrefix, serverUri, cancellationToken).ConfigureAwait(false);
                if (!healthFound)
                {
                    continue;
                }

                (bool loadFound, byte loadWeight, long loadTicks) = await ReadSignalAsync(
                    m_options.LoadKeyPrefix, serverUri, cancellationToken).ConfigureAwait(false);
                signals.Add((serverUri, serviceLevel, healthTicks, loadWeight, loadTicks, loadFound));
            }

            long nowTicks = m_timeProvider.GetUtcNow().UtcDateTime.Ticks;
            TimeSpan maximumAge = m_options.StalenessWindow;
            var records = new List<PeerDirectionRecord>(signals.Count);
            foreach ((string serverUri, byte serviceLevel, long healthTicks,
                byte loadWeight, long loadTicks, bool loadFound) in signals)
            {
                if (!LoadDirectionRecordValidation.IsCurrent(healthTicks, nowTicks, maximumAge))
                {
                    continue;
                }

                bool loadKnown = loadFound && LoadDirectionRecordValidation.IsCurrent(loadTicks, nowTicks, maximumAge);
                records.Add(new PeerDirectionRecord
                {
                    ServerUri = serverUri,
                    ServiceLevel = serviceLevel,
                    LoadWeight = loadKnown ? loadWeight : (byte)0,
                    LoadKnown = loadKnown
                });
            }

            return new ArrayOf<PeerDirectionRecord>([.. records]);
        }

        private async ValueTask<(bool Found, byte Value, long Ticks)> ReadSignalAsync(
            string keyPrefix,
            string serverUri,
            CancellationToken cancellationToken)
        {
            string key = keyPrefix + serverUri;
            (bool found, ByteString record) = await m_store.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
            if (found &&
                m_protector.TryUnprotect(RecordProtectionContext.Create("peer-direction", key), record,
                    out ByteString payload) &&
                PeerDirectionCodec.TryDecode(
                    payload, m_context, out string recordServerUri, out byte value, out long ticks) &&
                string.Equals(recordServerUri, serverUri, StringComparison.Ordinal))
            {
                return (true, value, ticks);
            }

            return default;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly HashSet<string> m_peerApplicationUris;
    }
}
