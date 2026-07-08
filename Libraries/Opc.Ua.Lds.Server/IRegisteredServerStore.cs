/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
        Task<RegistrationEntry> RegisterAsync(
            RegisteredServer server,
            MdnsDiscoveryConfiguration mdnsConfig,
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
            string serverUri,
            string serverName,
            string discoveryUrl,
            IEnumerable<string> capabilities);

        /// <summary>
        /// Removes stale registrations and mDNS-observed records.
        /// </summary>
        void Prune(DateTime nowUtc);
    }
}
