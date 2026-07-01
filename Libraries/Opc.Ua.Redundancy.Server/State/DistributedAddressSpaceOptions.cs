/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
        /// <c>Docs/HighAvailability.md</c>.
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
        /// writer.
        /// </remarks>
        public bool UseLeaderElection { get; set; }

        /// <summary>
        /// Gets or sets the shared-store key that holds the leader lease.
        /// </summary>
        public string LeaseKey { get; set; } = "addressspace/leader";

        /// <summary>
        /// Gets or sets the unique identifier for this server replica.
        /// </summary>
        public string NodeId { get; set; } = Environment.MachineName;

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
        /// to decrement healthy service levels for load balancing.
        /// </summary>
        public Func<uint>? ServiceLevelLoadMetric { get; set; }

        /// <summary>
        /// Gets or sets a function that returns the health-derived maximum
        /// service level for this replica.
        /// </summary>
        public Func<byte>? HealthServiceLevel { get; set; }
    }
}
