// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for dependency-injection registration of distributed address
    /// space building blocks.
    /// </summary>
    public sealed class DistributedAddressSpaceOptions
    {
        /// <summary>
        /// Gets or sets a factory that creates the record protector applied to
        /// every payload written to the shared store (authenticated encryption).
        /// </summary>
        /// <remarks>
        /// When this value is <c>null</c>, payloads are stored without
        /// encryption or integrity protection (a no-op protector). Production
        /// deployments backed by a network store MUST configure an
        /// <see cref="AesCbcHmacRecordProtector"/>; see
        /// <c>docs/HighAvailability.md</c>.
        /// </remarks>
        public Func<IServiceProvider, IRecordProtector>? RecordProtectorFactory { get; set; }

        /// <summary>
        /// Gets or sets a factory that creates the shared key/value store.
        /// </summary>
        /// <remarks>
        /// When this value is <c>null</c>, the fluent registration uses a
        /// singleton <see cref="InMemorySharedKeyValueStore"/>.
        /// </remarks>
        public Func<IServiceProvider, ISharedKeyValueStore>? KeyValueStoreFactory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether lease-based leader election
        /// should be used.
        /// </summary>
        /// <remarks>
        /// When this value is <c>false</c>, the fluent registration uses a
        /// static single-leader election so the local server is always the
        /// writer unless an election is already registered. When enabled, the shared lease
        /// replaces an earlier election registration as the server writer authority. It requires an
        /// independently enrolled <see cref="FreshnessProtectedKeyValueStore"/> and explicit lease membership.
        /// </remarks>
        public bool UseLeaderElection { get; set; }

        /// <summary>
        /// Gets or sets the shared-store key that holds the leader lease.
        /// </summary>
        public string LeaseKey { get; set; } = "addressspace/leader";

        /// <summary>
        /// Gets or sets the unique identifier for this server replica.
        /// </summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the same complete set of lease member ids on every replica.
        /// </summary>
        public ArrayOf<string> LeaseMemberIds { get; set; } = [];

        /// <summary>
        /// Gets or sets how long an acquired leader lease remains valid without
        /// renewal.
        /// </summary>
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets how often the leader-election background loop renews
        /// the lease.
        /// </summary>
        public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets the configured redundancy failover mode used for
        /// service-level subrange mapping.
        /// </summary>
        public RedundancySupport RedundancyMode { get; set; } = RedundancySupport.Warm;

        /// <summary>
        /// Gets or sets a function that returns the connected-client load used
        /// to decrement healthy service levels for load balancing. When unset,
        /// the provider uses the current number of hosted server sessions.
        /// </summary>
        public Func<uint>? ServiceLevelLoadMetric { get; set; }

        /// <summary>
        /// Gets or sets a function that returns the health-derived maximum
        /// service level for this replica. It can lower the level but cannot
        /// make an unverified replica ready to serve.
        /// </summary>
        public Func<byte>? HealthServiceLevel { get; set; }
    }
}
