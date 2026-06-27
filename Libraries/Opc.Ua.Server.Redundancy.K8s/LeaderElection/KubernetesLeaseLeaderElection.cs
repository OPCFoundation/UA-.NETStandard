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
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.Redundancy;

namespace Opc.Ua.Server.Redundancy.K8s
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: Kubernetes Lease-backed <see cref="ILeaderElection"/> implementation.
    /// </summary>
    public sealed class KubernetesLeaseLeaderElection : ILeaderElection
    {
        /// <summary>
        /// Creates a Kubernetes Lease election using the in-cluster API client.
        /// </summary>
        /// <param name="options">The leader election options.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <param name="logger">Optional logger.</param>
        public KubernetesLeaseLeaderElection(
            KubernetesLeaderElectionOptions options,
            TimeProvider? timeProvider = null,
            ILogger? logger = null)
            : this(CreateApiClient(options), options, timeProvider, logger)
        {
        }

        internal KubernetesLeaseLeaderElection(
            IKubernetesApiClient apiClient,
            KubernetesLeaderElectionOptions options,
            TimeProvider? timeProvider = null,
            ILogger? logger = null)
        {
            m_apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_namespace = KubernetesApiClientFactory.ResolveNamespace(m_options.Kubernetes, m_apiClient);
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = logger;
        }

        /// <inheritdoc/>
        public bool IsLeader
        {
            get
            {
                lock (m_lock)
                {
                    return m_isLeader;
                }
            }
        }

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public async ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
        {
            if (!m_apiClient.IsInCluster)
            {
                SetLeader(false);
                return false;
            }

            DateTimeOffset now = m_timeProvider.GetUtcNow();
            KubernetesLease? current = await m_apiClient
                .GetLeaseAsync(m_namespace, m_options.LeaseName, ct)
                .ConfigureAwait(false);

            try
            {
                if (current == null)
                {
                    KubernetesLease created = NewLease(now);
                    await m_apiClient.CreateLeaseAsync(m_namespace, created, ct).ConfigureAwait(false);
                    SetLeader(true);
                    return true;
                }

                if (!CanAcquireOrRenew(current, now))
                {
                    SetLeader(false);
                    return false;
                }

                string? previousHolder = current.Spec.HolderIdentity;
                current.Spec.HolderIdentity = m_options.Kubernetes.NodeId;
                current.Spec.LeaseDurationSeconds = ToLeaseSeconds(m_options.LeaseDuration);
                current.Spec.RenewTime = FormatTime(now);
                if (!string.Equals(previousHolder, m_options.Kubernetes.NodeId, StringComparison.Ordinal))
                {
                    current.Spec.AcquireTime = FormatTime(now);
                    current.Spec.LeaseTransitions = (current.Spec.LeaseTransitions ?? 0) + 1;
                }

                await m_apiClient
                    .ReplaceLeaseAsync(m_namespace, m_options.LeaseName, current, ct)
                    .ConfigureAwait(false);
                SetLeader(true);
                return true;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                SetLeader(false);
                return false;
            }
        }

        /// <inheritdoc/>
        public void Start()
        {
            lock (m_lock)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;
                m_loop = Task.Run(() => RenewLoopAsync(m_cts.Token));
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }

            m_cts.Cancel();
            if (m_loop != null)
            {
                try
                {
                    await m_loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }

            await ReleaseIfOwnedAsync().ConfigureAwait(false);
            m_cts.Dispose();
        }

        internal static bool IsReadyServiceLevel(byte serviceLevel, byte readyMinimumServiceLevel)
        {
            return serviceLevel >= readyMinimumServiceLevel;
        }

        private static IKubernetesApiClient CreateApiClient(KubernetesLeaderElectionOptions options)
        {
            return KubernetesApiClientFactory.Create(
                (options ?? throw new ArgumentNullException(nameof(options))).Kubernetes);
        }

        private KubernetesLease NewLease(DateTimeOffset now)
        {
            string time = FormatTime(now);
            return new KubernetesLease
            {
                Metadata = new KubernetesObjectMetadata
                {
                    Name = m_options.LeaseName,
                    Namespace = m_namespace
                },
                Spec = new KubernetesLeaseSpec
                {
                    HolderIdentity = m_options.Kubernetes.NodeId,
                    LeaseDurationSeconds = ToLeaseSeconds(m_options.LeaseDuration),
                    AcquireTime = time,
                    RenewTime = time,
                    LeaseTransitions = 0
                }
            };
        }

        private bool CanAcquireOrRenew(KubernetesLease lease, DateTimeOffset now)
        {
            string? holder = lease.Spec.HolderIdentity;
            if (string.IsNullOrEmpty(holder) ||
                string.Equals(holder, m_options.Kubernetes.NodeId, StringComparison.Ordinal))
            {
                return true;
            }

            DateTimeOffset renewTime = ParseTime(lease.Spec.RenewTime) ?? DateTimeOffset.MinValue;
            int leaseSeconds = lease.Spec.LeaseDurationSeconds > 0
                ? lease.Spec.LeaseDurationSeconds
                : ToLeaseSeconds(m_options.LeaseDuration);
            return renewTime.AddSeconds(leaseSeconds) <= now;
        }

        private async Task RenewLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await TryAcquireOrRenewAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_logger?.LogError(ex, "Kubernetes Lease election renew failed for {NodeId}.",
                            m_options.Kubernetes.NodeId);
                    }

                    await Task.Delay(m_options.RenewInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private async Task ReleaseIfOwnedAsync()
        {
            if (!m_apiClient.IsInCluster)
            {
                return;
            }

            try
            {
                KubernetesLease? current = await m_apiClient
                    .GetLeaseAsync(m_namespace, m_options.LeaseName, CancellationToken.None)
                    .ConfigureAwait(false);
                if (current != null &&
                    string.Equals(current.Spec.HolderIdentity, m_options.Kubernetes.NodeId, StringComparison.Ordinal))
                {
                    current.Spec.HolderIdentity = null;
                    current.Spec.RenewTime = FormatTime(m_timeProvider.GetUtcNow());
                    await m_apiClient
                        .ReplaceLeaseAsync(m_namespace, m_options.LeaseName, current, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger?.LogError(ex, "Kubernetes Lease election release failed for {NodeId}.",
                    m_options.Kubernetes.NodeId);
            }
        }

        private void SetLeader(bool value)
        {
            bool changed;
            lock (m_lock)
            {
                changed = m_isLeader != value;
                m_isLeader = value;
            }
            if (changed)
            {
                LeadershipChanged?.Invoke(value);
            }
        }

        private static string FormatTime(DateTimeOffset time)
        {
            return time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        private static DateTimeOffset? ParseTime(string? value)
        {
            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
            {
                return parsed.ToUniversalTime();
            }
            return null;
        }

        private static int ToLeaseSeconds(TimeSpan leaseDuration)
        {
            return Math.Max(1, (int)Math.Ceiling(leaseDuration.TotalSeconds));
        }

        private readonly IKubernetesApiClient m_apiClient;
        private readonly KubernetesLeaderElectionOptions m_options;
        private readonly string m_namespace;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger? m_logger;
        private readonly Lock m_lock = new();
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_loop;
        private bool m_isLeader;
        private bool m_started;
        private bool m_disposed;
    }
}
