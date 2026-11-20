/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
        /// <param name="options">The load-direction options (key prefixes, staleness window).</param>
        /// <param name="timeProvider">The time source for staleness checks.</param>
        public SharedPeerDirectionView(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector protector,
            LoadDirectionOptions options,
            TimeProvider timeProvider)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<PeerDirectionRecord>> GetPeersAsync(
            CancellationToken cancellationToken = default)
        {
            long nowTicks = m_timeProvider.GetUtcNow().UtcDateTime.Ticks;
            long stalenessTicks = m_options.StalenessWindow.Ticks;

            Dictionary<string, (byte Value, long Ticks)> health = await ReadSignalsAsync(
                m_options.ServiceLevelKeyPrefix, cancellationToken).ConfigureAwait(false);
            Dictionary<string, (byte Value, long Ticks)> load = await ReadSignalsAsync(
                m_options.LoadKeyPrefix, cancellationToken).ConfigureAwait(false);

            var records = new List<PeerDirectionRecord>(health.Count);
            foreach (KeyValuePair<string, (byte Value, long Ticks)> entry in health)
            {
                if (IsStale(nowTicks, entry.Value.Ticks, stalenessTicks))
                {
                    continue;
                }

                bool loadKnown = load.TryGetValue(entry.Key, out (byte Value, long Ticks) loadEntry) &&
                    !IsStale(nowTicks, loadEntry.Ticks, stalenessTicks);

                records.Add(new PeerDirectionRecord
                {
                    ServerUri = entry.Key,
                    ServiceLevel = entry.Value.Value,
                    LoadWeight = loadKnown ? loadEntry.Value : (byte)0,
                    LoadKnown = loadKnown
                });
            }

            return new ArrayOf<PeerDirectionRecord>([.. records]);
        }

        private async ValueTask<Dictionary<string, (byte Value, long Ticks)>> ReadSignalsAsync(
            string keyPrefix,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, (byte Value, long Ticks)>(StringComparer.Ordinal);
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(keyPrefix, cancellationToken)
                .ConfigureAwait(false))
            {
                if (!m_protector.TryUnprotect(
                        RecordProtectionContext.Create("peer-direction", entry.Key),
                        entry.Value,
                        out ByteString payload) ||
                    !PeerDirectionCodec.TryDecode(
                        payload, m_context, out string serverUri, out byte value, out long ticks))
                {
                    continue;
                }

                // Keep the newest record when a peer appears more than once.
                if (!result.TryGetValue(serverUri, out (byte Value, long Ticks) existing) ||
                    ticks > existing.Ticks)
                {
                    result[serverUri] = (value, ticks);
                }
            }

            return result;
        }

        private static bool IsStale(long nowTicks, long recordTicks, long stalenessTicks)
        {
            return nowTicks - recordTicks > stalenessTicks;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly TimeProvider m_timeProvider;
    }
}
