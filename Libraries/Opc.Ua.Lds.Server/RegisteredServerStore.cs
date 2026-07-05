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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// Thread-safe in-memory database of servers registered with the LDS plus
    /// network records advertised via LDS-ME.
    /// </summary>
    /// <remarks>
    /// Per OPC UA Part 12 §6.4.5.1, registered servers should re-register
    /// periodically; entries are pruned when they have not been refreshed
    /// within <see cref="RegistrationLifetime"/>. mDNS-observed records
    /// follow their own TTL via <see cref="MulticastRecordLifetime"/>.
    /// </remarks>
    public sealed class RegisteredServerStore : IRegisteredServerStore
    {
        private readonly SemaphoreSlim m_lock = new(1, 1);

        private readonly Dictionary<string, RegistrationEntry> m_byUri
            = new(StringComparer.Ordinal);

        private readonly List<ServerOnNetworkRecord> m_records = [];
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private uint m_nextRecordId = 1;
        private DateTime m_lastCounterResetTime;
        private ITimer m_pruneTimer;
        private bool m_disposed;

        /// <summary>
        /// Creates a new in-memory store.
        /// </summary>
        /// <param name="logger">Optional logger; defaults to a null logger.</param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/> used for the
        /// background prune timer and registration timestamps. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        public RegisteredServerStore(ILogger logger = null, TimeProvider timeProvider = null)
        {
            m_logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_lastCounterResetTime = m_timeProvider.GetUtcNow().UtcDateTime;
        }

        /// <summary>
        /// Lifetime for explicit RegisterServer registrations. After this period
        /// without a refresh, the registration is considered stale and removed.
        /// Default: 10 minutes (informational guidance from Part 12; servers in
        /// practice re-register every 30 seconds).
        /// </summary>
        public TimeSpan RegistrationLifetime { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// TTL for mDNS-observed network records. Defaults to 75 seconds, which
        /// matches the standard mDNS service-record TTL.
        /// </summary>
        public TimeSpan MulticastRecordLifetime { get; set; } = TimeSpan.FromSeconds(75);

        /// <summary>
        /// Most recent time the network record id counter was reset. Exposed via
        /// <see cref="ServerOnNetworkRecord"/> queries for client cursor logic.
        /// </summary>
        public DateTime LastCounterResetTime
        {
            get
            {
                m_lock.Wait();
                try
                {
                    return m_lastCounterResetTime;
                }
                finally
                {
                    m_lock.Release();
                }
            }
        }

        /// <summary>
        /// Test/diagnostic helper: snapshot of all current registrations.
        /// </summary>
        public IReadOnlyList<RegistrationEntry> Snapshot()
        {
            m_lock.Wait();
            try
            {
                return [.. m_byUri.Values.Select(Clone)];
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Test/diagnostic helper: snapshot of all current network records.
        /// </summary>
        public IReadOnlyList<ServerOnNetworkRecord> SnapshotNetworkRecords()
        {
            m_lock.Wait();
            try
            {
                return m_records.ConvertAll(Clone);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Starts the background pruning timer. Tests typically skip this and
        /// drive prune deterministically via <see cref="Prune(DateTime)"/>.
        /// </summary>
        public void StartPruneTimer(TimeSpan? interval = null)
        {
            TimeSpan tick = interval ?? TimeSpan.FromSeconds(30);
            m_pruneTimer?.Dispose();
            m_pruneTimer = m_timeProvider.CreateTimer(_ =>
            {
                try
                {
                    Prune(m_timeProvider.GetUtcNow().UtcDateTime);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "RegisteredServerStore prune failed.");
                }
            }, null, tick, tick);
        }

        /// <summary>
        /// Adds, updates, or removes a registration based on
        /// <paramref name="server"/>'s state. Returns the live registration
        /// (post-merge) or <c>null</c> if it was removed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is null.</exception>
        public async Task<RegistrationEntry> RegisterAsync(
            RegisteredServer server,
            MdnsDiscoveryConfiguration mdnsConfig,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            await m_lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string uri = server.ServerUri;

                // semaphore file: if path is set but file is missing, drop the registration
                bool semaphoreValid = string.IsNullOrEmpty(server.SemaphoreFilePath) ||
                    File.Exists(server.SemaphoreFilePath);

                if (!server.IsOnline || !semaphoreValid)
                {
                    if (m_byUri.Remove(uri))
                    {
                        m_logger.LogInformation(
                            "LDS: removed registration for {Uri} (IsOnline={Online}, SemaphoreOk={Sem}).",
                            uri,
                            server.IsOnline,
                            semaphoreValid);
                    }
                    RemoveNetworkRecordsForUriCore(uri);
                    return null;
                }

                if (!m_byUri.TryGetValue(uri, out RegistrationEntry entry))
                {
                    entry = new RegistrationEntry { ServerUri = uri };
                    m_byUri[uri] = entry;
                }

                entry.ProductUri = server.ProductUri;
                entry.ServerNames = server.ServerNames.IsNull
                    ? []
                    : server.ServerNames.ToList();
                entry.ServerType = server.ServerType;
                entry.GatewayServerUri = server.GatewayServerUri;
                entry.DiscoveryUrls = server.DiscoveryUrls.IsNull
                    ? []
                    : server.DiscoveryUrls.ToList();
                entry.SemaphoreFilePath = server.SemaphoreFilePath;
                entry.IsOnline = true;
                entry.LastSeenUtc = m_timeProvider.GetUtcNow().UtcDateTime;

                if (mdnsConfig != null)
                {
                    entry.MdnsServerName = mdnsConfig.MdnsServerName;
                    entry.ServerCapabilities = mdnsConfig.ServerCapabilities.IsNull
                        ? []
                        : mdnsConfig.ServerCapabilities.ToList();

                    UpdateNetworkRecordsCore(entry);
                }
                else if (entry.MdnsServerName != null && entry.ServerCapabilities.Count > 0)
                {
                    // refresh existing mDNS records' LastSeen
                    UpdateNetworkRecordsCore(entry);
                }

                return Clone(entry);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Returns translated <see cref="ApplicationDescription"/> entries for
        /// every active registration whose ServerUri passes <paramref name="serverUriFilter"/>.
        /// </summary>
        public IList<ApplicationDescription> Find(
            ICollection<string> serverUriFilter,
            ICollection<string> requestedLocaleIds)
        {
            m_lock.Wait();
            try
            {
                var result = new List<ApplicationDescription>(m_byUri.Count);
                foreach (RegistrationEntry e in m_byUri.Values)
                {
                    if (serverUriFilter is { Count: > 0 } &&
                        !serverUriFilter.Contains(e.ServerUri))
                    {
                        continue;
                    }

                    LocalizedText name = SelectName(e.ServerNames, requestedLocaleIds);

                    result.Add(new ApplicationDescription
                    {
                        ApplicationUri = e.ServerUri,
                        ProductUri = e.ProductUri,
                        ApplicationName = name,
                        ApplicationType = e.ServerType,
                        GatewayServerUri = e.GatewayServerUri,
                        DiscoveryUrls = [.. e.DiscoveryUrls]
                    });
                }
                return result;
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Returns paginated <see cref="ServerOnNetwork"/> records honoring
        /// <paramref name="startingRecordId"/>, <paramref name="maxRecordsToReturn"/>,
        /// and <paramref name="serverCapabilityFilter"/>.
        /// </summary>
        public (IList<ServerOnNetwork> records, DateTime lastCounterResetTime) ListOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            ICollection<string> serverCapabilityFilter)
        {
            m_lock.Wait();
            try
            {
                IEnumerable<ServerOnNetworkRecord> source = m_records
                    .Where(r => r.RecordId >= startingRecordId)
                    .OrderBy(r => r.RecordId);

                if (serverCapabilityFilter is { Count: > 0 })
                {
                    source = source.Where(r =>
                        serverCapabilityFilter.All(cap => r.ServerCapabilities.Contains(cap)));
                }

                if (maxRecordsToReturn > 0)
                {
                    source = source.Take((int)maxRecordsToReturn);
                }

                IList<ServerOnNetwork> dto = [.. source
                    .Select(r => new ServerOnNetwork
                    {
                        RecordId = r.RecordId,
                        ServerName = r.ServerName,
                        DiscoveryUrl = r.DiscoveryUrl,
                        ServerCapabilities = [.. r.ServerCapabilities]
                    })];

                return (dto, m_lastCounterResetTime);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Adds or refreshes mDNS-observed peer records. Called by
        /// <see cref="MulticastDiscovery"/> when service discoveries occur.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="discoveryUrl"/> is null or empty.</exception>
        public void UpsertMulticastRecord(
            string serverUri,
            string serverName,
            string discoveryUrl,
            IEnumerable<string> capabilities)
        {
            if (string.IsNullOrEmpty(discoveryUrl))
            {
                throw new ArgumentException("DiscoveryUrl must be provided.", nameof(discoveryUrl));
            }

            m_lock.Wait();
            try
            {
                ServerOnNetworkRecord existing = m_records.FirstOrDefault(r =>
                    r.ObservedViaMulticast &&
                    string.Equals(r.DiscoveryUrl, discoveryUrl, StringComparison.Ordinal));

                if (existing != null)
                {
                    existing.LastSeenUtc = m_timeProvider.GetUtcNow().UtcDateTime;
                    existing.ServerName = serverName;
                    existing.ServerCapabilities = capabilities?.ToList() ?? [];
                    return;
                }

                m_records.Add(new ServerOnNetworkRecord
                {
                    RecordId = m_nextRecordId++,
                    ServerUri = serverUri,
                    ServerName = serverName,
                    DiscoveryUrl = discoveryUrl,
                    ServerCapabilities = capabilities?.ToList() ?? [],
                    LastSeenUtc = m_timeProvider.GetUtcNow().UtcDateTime,
                    ObservedViaMulticast = true
                });
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Removes stale registrations and stale mDNS-observed records.
        /// </summary>
        public void Prune(DateTime nowUtc)
        {
            m_lock.Wait();
            try
            {
                var staleUris = m_byUri.Values
                    .Where(e =>
                        nowUtc - e.LastSeenUtc > RegistrationLifetime ||
                        (!string.IsNullOrEmpty(e.SemaphoreFilePath) && !File.Exists(e.SemaphoreFilePath)))
                    .Select(e => e.ServerUri)
                    .ToList();

                foreach (string uri in staleUris)
                {
                    m_byUri.Remove(uri);
                    RemoveNetworkRecordsForUriCore(uri);
                }

                m_records.RemoveAll(r =>
                    r.ObservedViaMulticast &&
                    nowUtc - r.LastSeenUtc > MulticastRecordLifetime);
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Test seam: directly insert a registration without protocol validation.
        /// Used by the LdsTestFixture to deterministically populate state.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
        /// <exception cref="ArgumentException">The <see cref="RegistrationEntry.ServerUri"/> of <paramref name="entry"/> is null or empty.</exception>
        internal void SeedRegistration(RegistrationEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            if (string.IsNullOrEmpty(entry.ServerUri))
            {
                throw new ArgumentException("ServerUri must be set.", nameof(entry));
            }

            m_lock.Wait();
            try
            {
                m_byUri[entry.ServerUri] = entry;
                if (!string.IsNullOrEmpty(entry.MdnsServerName) &&
                    entry.ServerCapabilities is { Count: > 0 })
                {
                    UpdateNetworkRecordsCore(entry);
                }
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Test seam: drop all state and reset counters.
        /// </summary>
        internal void Clear()
        {
            m_lock.Wait();
            try
            {
                m_byUri.Clear();
                m_records.Clear();
                m_nextRecordId = 1;
                m_lastCounterResetTime = m_timeProvider.GetUtcNow().UtcDateTime;
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_pruneTimer?.Dispose();
            m_lock.Dispose();
        }

        private void UpdateNetworkRecordsCore(RegistrationEntry entry)
        {
            // Replace any prior records for this ServerUri originating from
            // explicit registration (preserve mDNS-observed records).
            m_records.RemoveAll(r =>
                !r.ObservedViaMulticast &&
                string.Equals(r.ServerUri, entry.ServerUri, StringComparison.Ordinal));

            foreach (string url in entry.DiscoveryUrls)
            {
                LocalizedText firstName = entry.ServerNames.Count > 0
                    ? entry.ServerNames[0]
                    : LocalizedText.Null;

                m_records.Add(new ServerOnNetworkRecord
                {
                    RecordId = m_nextRecordId++,
                    ServerUri = entry.ServerUri,
                    ServerName = entry.MdnsServerName ?? firstName.Text,
                    DiscoveryUrl = url,
                    ServerCapabilities = [.. entry.ServerCapabilities],
                    LastSeenUtc = m_timeProvider.GetUtcNow().UtcDateTime,
                    ObservedViaMulticast = false
                });
            }
        }

        private void RemoveNetworkRecordsForUriCore(string serverUri)
        {
            m_records.RemoveAll(r =>
                !r.ObservedViaMulticast &&
                string.Equals(r.ServerUri, serverUri, StringComparison.Ordinal));
        }

        private static LocalizedText SelectName(
            IList<LocalizedText> names,
            ICollection<string> requestedLocaleIds)
        {
            if (names == null || names.Count == 0)
            {
                return new LocalizedText(string.Empty);
            }

            if (requestedLocaleIds is { Count: > 0 })
            {
                foreach (string locale in requestedLocaleIds)
                {
                    LocalizedText match = names.FirstOrDefault(n =>
                        string.Equals(n.Locale, locale, StringComparison.OrdinalIgnoreCase));
                    if (!match.IsNullOrEmpty)
                    {
                        return match;
                    }
                }
            }

            return names[0];
        }

        private static RegistrationEntry Clone(RegistrationEntry e)
        {
            return new()
            {
                ServerUri = e.ServerUri,
                ProductUri = e.ProductUri,
                ServerNames = [.. e.ServerNames],
                ServerType = e.ServerType,
                GatewayServerUri = e.GatewayServerUri,
                DiscoveryUrls = [.. e.DiscoveryUrls],
                SemaphoreFilePath = e.SemaphoreFilePath,
                IsOnline = e.IsOnline,
                LastSeenUtc = e.LastSeenUtc,
                ServerCapabilities = [.. e.ServerCapabilities],
                MdnsServerName = e.MdnsServerName
            };
        }

        private static ServerOnNetworkRecord Clone(ServerOnNetworkRecord r)
        {
            return new()
            {
                RecordId = r.RecordId,
                ServerUri = r.ServerUri,
                ServerName = r.ServerName,
                DiscoveryUrl = r.DiscoveryUrl,
                ServerCapabilities = [.. r.ServerCapabilities],
                LastSeenUtc = r.LastSeenUtc,
                ObservedViaMulticast = r.ObservedViaMulticast
            };
        }
    }
}
