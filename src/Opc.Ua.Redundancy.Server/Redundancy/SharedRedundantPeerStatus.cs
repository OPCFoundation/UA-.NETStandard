// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Exchanges authenticated, short-lived observations of replica status.
    /// </summary>
    internal sealed class SharedRedundantPeerStatus
    {
        /// <summary>
        /// Uses a shared store and record protector scoped to the same redundant server set.
        /// </summary>
        public SharedRedundantPeerStatus(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            TimeProvider timeProvider,
            string keyPrefix)
        {
            m_store = store;
            m_protector = protector;
            m_timeProvider = timeProvider;
            m_keyPrefix = keyPrefix;
        }

        /// <summary>
        /// Records the latest local observation for the supplied member identifier.
        /// </summary>
        public ValueTask PublishAsync(string serverId, byte serviceLevel, ServerState state, CancellationToken ct)
        {
            byte[] data = new byte[kLength];
            data[0] = serviceLevel;
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(1), (int)state);
            BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(5), m_timeProvider.GetUtcNow().UtcDateTime.Ticks);
            string key = m_keyPrefix + serverId;
            return m_store.SetAsync(
                key, m_protector.Protect(RecordProtectionContext.Create(kRecordType, key), ByteString.From(data)), ct);
        }

        /// <summary>
        /// Reads an observation only if it matches the member and is still current.
        /// </summary>
        public async ValueTask<(byte ServiceLevel, ServerState State)> ReadAsync(string serverId, CancellationToken ct)
        {
            string key = m_keyPrefix + serverId;
            (bool found, ByteString value) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
            if (!found ||
                !m_protector.TryUnprotect(
                    RecordProtectionContext.Create(kRecordType, key), value, out ByteString data) ||
                data.Length != kLength)
            {
                return (ServiceLevels.NoData, ServerState.Unknown);
            }

            var state = (ServerState)BinaryPrimitives.ReadInt32BigEndian(data.Span[1..]);
            long observed = BinaryPrimitives.ReadInt64BigEndian(data.Span[5..]);
            long now = m_timeProvider.GetUtcNow().UtcDateTime.Ticks;
            if (state < ServerState.Running || state >= ServerState.Unknown ||
                observed <= 0 || observed > now || now - observed > s_freshnessWindow.Ticks)
            {
                return (ServiceLevels.NoData, ServerState.Unknown);
            }
            return (data.Span[0], state);
        }

        private const int kLength = 1 + sizeof(int) + sizeof(long);
        private const string kRecordType = "redundant-peer-status";
        private static readonly TimeSpan s_freshnessWindow = TimeSpan.FromSeconds(10);
        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly TimeProvider m_timeProvider;
        private readonly string m_keyPrefix;
    }
}
