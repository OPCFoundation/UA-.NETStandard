// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// Store for LDS explicit registrations and LDS-ME observed network records.
    /// </summary>
    public interface IRegisteredServerStore : IDisposable
    {
        /// <summary>
        /// Lifetime for explicit RegisterServer registrations.
        /// </summary>
        TimeSpan RegistrationLifetime { get; set; }

        /// <summary>
        /// TTL for mDNS-observed network records.
        /// </summary>
        TimeSpan MulticastRecordLifetime { get; set; }

        /// <summary>
        /// Most recent time the network record id counter was reset.
        /// </summary>
        DateTime LastCounterResetTime { get; }

        /// <summary>
        /// Snapshot of all current registrations.
        /// </summary>
        IReadOnlyList<RegistrationEntry> Snapshot();

        /// <summary>
        /// Snapshot of all current network records.
        /// </summary>
        IReadOnlyList<ServerOnNetworkRecord> SnapshotNetworkRecords();

        /// <summary>
        /// Starts background pruning.
        /// </summary>
        void StartPruneTimer(TimeSpan? interval = null);

        /// <summary>
        /// Adds, updates, or removes a server registration.
        /// </summary>
        Task<RegistrationEntry?> RegisterAsync(
            RegisteredServer server,
            MdnsDiscoveryConfiguration? mdnsConfig,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns translated application descriptions for active registrations.
        /// </summary>
        IList<ApplicationDescription> Find(
            ICollection<string> serverUriFilter,
            ICollection<string> requestedLocaleIds);

        /// <summary>
        /// Returns paginated network records.
        /// </summary>
        (IList<ServerOnNetwork> records, DateTime lastCounterResetTime) ListOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            ICollection<string> serverCapabilityFilter);

        /// <summary>
        /// Adds or refreshes an mDNS-observed peer record.
        /// </summary>
        void UpsertMulticastRecord(
            string? serverUri,
            string serverName,
            string discoveryUrl,
            IEnumerable<string> capabilities);

        /// <summary>
        /// Removes stale registrations and mDNS-observed records.
        /// </summary>
        void Prune(DateTime nowUtc);
    }
}
