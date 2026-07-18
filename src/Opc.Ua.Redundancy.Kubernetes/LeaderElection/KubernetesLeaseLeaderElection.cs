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

namespace Opc.Ua.Redundancy.Kubernetes
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

        /// <summary>
        /// Creates a Kubernetes Lease election using the specified Kubernetes API client.
        /// </summary>
        /// <param name="apiClient">The Kubernetes API client used to read and update Lease resources.</param>
        /// <param name="options">The leader election options.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <param name="logger">Optional logger.</param>
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
            m_watchdog = m_timeProvider.CreateTimer(
                static state => ((KubernetesLeaseLeaderElection)state!).OnWatchdogElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
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
            ThrowIfDisposed();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, m_cts.Token);
            await m_attemptGate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                return await TryAcquireOrRenewCoreAsync(linkedCts.Token).ConfigureAwait(false);
            }
            finally
            {
                m_attemptGate.Release();
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
            bool leadershipChanged;
            long notificationGeneration;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_fence++;
                leadershipChanged = m_isLeader;
                m_isLeader = false;
                notificationGeneration = m_fence;
            }
            NotifyLeadershipChanged(false, notificationGeneration, leadershipChanged);

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

            await m_attemptGate.WaitAsync().ConfigureAwait(false);
            m_attemptGate.Release();
            await ReleaseIfOwnedAsync().ConfigureAwait(false);
            m_watchdog.Dispose();
            m_attemptGate.Dispose();
            m_cts.Dispose();
        }

        /// <summary>
        /// Determines whether an OPC UA ServiceLevel satisfies a readiness threshold.
        /// </summary>
        /// <param name="serviceLevel">The current OPC UA ServiceLevel value.</param>
        /// <param name="readyMinimumServiceLevel">The minimum ServiceLevel that reports readiness.</param>
        /// <returns><c>true</c> when <paramref name="serviceLevel"/> is high enough; otherwise, <c>false</c>.</returns>
        internal static bool IsReadyServiceLevel(byte serviceLevel, byte readyMinimumServiceLevel)
        {
            return serviceLevel >= readyMinimumServiceLevel;
        }

        private async ValueTask<bool> TryAcquireOrRenewCoreAsync(CancellationToken ct)
        {
            long attemptFence = GetFence();
            try
            {
                if (!m_apiClient.IsInCluster)
                {
                    LoseLeadership();
                    return false;
                }

                DateTimeOffset now = m_timeProvider.GetUtcNow();
                KubernetesLease? current = await ExecuteApiAttemptAsync(
                    token => m_apiClient.GetLeaseAsync(m_namespace, m_options.LeaseName, token),
                    ct)
                    .ConfigureAwait(false);
                if (!IsAttemptCurrent(attemptFence))
                {
                    return false;
                }

                if (current == null)
                {
                    ClearObservedForeignLease();
                    KubernetesLease created = NewLease(now);
                    _ = await ExecuteApiAttemptAsync(
                        token => m_apiClient.CreateLeaseAsync(m_namespace, created, token),
                        ct)
                        .ConfigureAwait(false);
                    return TryConfirmLeadership(attemptFence);
                }

                if (!CanAcquireOrRenew(current))
                {
                    LoseLeadership();
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

                _ = await ExecuteApiAttemptAsync(
                    token => m_apiClient.ReplaceLeaseAsync(
                        m_namespace,
                        m_options.LeaseName,
                        current,
                        token),
                    ct)
                    .ConfigureAwait(false);
                return TryConfirmLeadership(attemptFence);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ClearObservedForeignLease();
                LoseLeadership();
                return false;
            }
            catch
            {
                LoseLeadership();
                throw;
            }
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

        private bool CanAcquireOrRenew(KubernetesLease lease)
        {
            string? holder = lease.Spec.HolderIdentity;
            if (string.IsNullOrEmpty(holder) ||
                string.Equals(holder, m_options.Kubernetes.NodeId, StringComparison.Ordinal))
            {
                ClearObservedForeignLease();
                return true;
            }

            string? resourceVersion = lease.Metadata.ResourceVersion;
            long observedTimestamp = m_timeProvider.GetTimestamp();
            TimeSpan leaseDuration = lease.Spec.LeaseDurationSeconds > 0
                ? TimeSpan.FromSeconds(lease.Spec.LeaseDurationSeconds)
                : GetLeaseDuration();
            // RenewTime is written by another process's wall clock. Treat the lease as expired only after its
            // resource version remains unchanged for a full lease duration measured by this process.
            lock (m_lock)
            {
                if (string.IsNullOrEmpty(resourceVersion))
                {
                    ClearObservedForeignLeaseCore();
                    return false;
                }
                if (!string.Equals(m_observedResourceVersion, resourceVersion, StringComparison.Ordinal) ||
                    !string.Equals(m_observedHolderIdentity, holder, StringComparison.Ordinal))
                {
                    m_observedResourceVersion = resourceVersion;
                    m_observedHolderIdentity = holder;
                    m_observedResourceVersionTimestamp = observedTimestamp;
                    m_observedLeaseDuration = leaseDuration;
                    return false;
                }

                return m_timeProvider.GetElapsedTime(m_observedResourceVersionTimestamp) >=
                    m_observedLeaseDuration;
            }
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
                        m_logger?.KubernetesLeaseElectionRenewFailed(ex, m_options.Kubernetes.NodeId);
                    }

                    await m_timeProvider.Delay(m_options.RenewInterval, ct).ConfigureAwait(false);
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
                KubernetesLease? current = await ExecuteApiAttemptAsync(
                    token => m_apiClient.GetLeaseAsync(m_namespace, m_options.LeaseName, token),
                    CancellationToken.None,
                    stopOnDispose: false)
                    .ConfigureAwait(false);
                if (current != null &&
                    string.Equals(current.Spec.HolderIdentity, m_options.Kubernetes.NodeId, StringComparison.Ordinal))
                {
                    current.Spec.HolderIdentity = null;
                    current.Spec.RenewTime = FormatTime(m_timeProvider.GetUtcNow());
                    _ = await ExecuteApiAttemptAsync(
                        token => m_apiClient.ReplaceLeaseAsync(
                            m_namespace,
                            m_options.LeaseName,
                            current,
                            token),
                        CancellationToken.None,
                        stopOnDispose: false)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger?.KubernetesLeaseElectionReleaseFailed(ex, m_options.Kubernetes.NodeId);
            }
        }

        private long GetFence()
        {
            lock (m_lock)
            {
                return m_fence;
            }
        }

        private bool IsAttemptCurrent(long attemptFence)
        {
            lock (m_lock)
            {
                return !m_disposed && attemptFence == m_fence;
            }
        }

        private bool TryConfirmLeadership(long attemptFence)
        {
            bool changed;
            long notificationGeneration;
            lock (m_lock)
            {
                if (m_disposed || attemptFence != m_fence)
                {
                    return false;
                }

                changed = !m_isLeader;
                m_isLeader = true;
                m_lastConfirmedLeadershipTimestamp = m_timeProvider.GetTimestamp();
                ClearObservedForeignLeaseCore();
                m_watchdog.Change(GetLeaseDuration(), Timeout.InfiniteTimeSpan);
                notificationGeneration = m_fence;
            }
            NotifyLeadershipChanged(true, notificationGeneration, changed);
            return true;
        }

        private void LoseLeadership()
        {
            bool changed;
            long notificationGeneration;
            lock (m_lock)
            {
                m_fence++;
                changed = m_isLeader;
                m_isLeader = false;
                if (!m_disposed)
                {
                    m_watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                notificationGeneration = m_fence;
            }
            NotifyLeadershipChanged(false, notificationGeneration, changed);
        }

        private void OnWatchdogElapsed()
        {
            bool changed = false;
            long notificationGeneration = 0;
            lock (m_lock)
            {
                if (m_disposed || !m_isLeader)
                {
                    return;
                }

                TimeSpan leaseDuration = GetLeaseDuration();
                TimeSpan elapsed = m_timeProvider.GetElapsedTime(m_lastConfirmedLeadershipTimestamp);
                if (elapsed < leaseDuration)
                {
                    m_watchdog.Change(leaseDuration - elapsed, Timeout.InfiniteTimeSpan);
                    return;
                }

                m_fence++;
                m_isLeader = false;
                changed = true;
                notificationGeneration = m_fence;
            }
            NotifyLeadershipChanged(false, notificationGeneration, changed);
        }

        private void NotifyLeadershipChanged(
            bool value,
            long notificationGeneration,
            bool stateChanged)
        {
            Action<bool>? handler;
            lock (m_notificationLock)
            {
                lock (m_lock)
                {
                    if (notificationGeneration != m_fence ||
                        m_isLeader != value ||
                        (value && m_disposed))
                    {
                        return;
                    }
                }

                if (notificationGeneration < m_lastNotificationGeneration)
                {
                    return;
                }
                m_lastNotificationGeneration = notificationGeneration;
                if (m_hasLeadershipNotification && m_lastNotifiedLeadership == value)
                {
                    return;
                }
                if (!m_hasLeadershipNotification && !stateChanged)
                {
                    return;
                }

                m_hasLeadershipNotification = true;
                m_lastNotifiedLeadership = value;
                handler = LeadershipChanged;
                handler?.Invoke(value);
            }
        }

        private void ClearObservedForeignLease()
        {
            lock (m_lock)
            {
                ClearObservedForeignLeaseCore();
            }
        }

        private void ClearObservedForeignLeaseCore()
        {
            m_observedResourceVersion = null;
            m_observedHolderIdentity = null;
            m_observedResourceVersionTimestamp = 0;
            m_observedLeaseDuration = default;
        }

        private async ValueTask<T> ExecuteApiAttemptAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken ct,
            bool stopOnDispose = true)
        {
            using CancellationTokenSource operationCts = stopOnDispose
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, m_cts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(ct);
            using CancellationTokenSource delayCts = stopOnDispose
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, m_cts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task<T> operationTask = operation(operationCts.Token).AsTask();
            Task delayTask = m_timeProvider.Delay(GetApiAttemptTimeout(), delayCts.Token);
            _ = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);
            if (operationTask.IsCompleted)
            {
                delayCts.Cancel();
                return await operationTask.ConfigureAwait(false);
            }

            operationCts.Cancel();
            ObserveFault(operationTask);
            ct.ThrowIfCancellationRequested();
            if (stopOnDispose)
            {
                m_cts.Token.ThrowIfCancellationRequested();
            }
            throw new TimeoutException("The Kubernetes Lease API attempt timed out.");
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private TimeSpan GetApiAttemptTimeout()
        {
            if (m_options.RenewInterval > TimeSpan.Zero)
            {
                return m_options.RenewInterval;
            }
            return GetLeaseDuration();
        }

        private TimeSpan GetLeaseDuration()
        {
            return m_options.LeaseDuration > TimeSpan.Zero
                ? m_options.LeaseDuration
                : TimeSpan.FromSeconds(1);
        }

        private void ThrowIfDisposed()
        {
            lock (m_lock)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
            }
        }

        private static string FormatTime(DateTimeOffset time)
        {
            return time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
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
        private readonly Lock m_notificationLock = new();
        private readonly CancellationTokenSource m_cts = new();
        private readonly SemaphoreSlim m_attemptGate = new(1, 1);
        private readonly ITimer m_watchdog;
        private Task? m_loop;
        private string? m_observedResourceVersion;
        private string? m_observedHolderIdentity;
        private long m_observedResourceVersionTimestamp;
        private long m_lastConfirmedLeadershipTimestamp;
        private long m_fence;
        private long m_lastNotificationGeneration = -1;
        private TimeSpan m_observedLeaseDuration;
        private bool m_isLeader;
        private bool m_hasLeadershipNotification;
        private bool m_lastNotifiedLeadership;
        private bool m_started;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="KubernetesLeaseLeaderElection"/>.
    /// </summary>
    internal static partial class KubernetesLeaseLeaderElectionLog
    {
        [LoggerMessage(EventId = RedundancyKubernetesEventIds.KubernetesLeaseLeaderElection + 0, Level = LogLevel.Error,
            Message = "Kubernetes Lease election renew failed for {NodeId}.")]
        public static partial void KubernetesLeaseElectionRenewFailed(
            this ILogger logger,
            Exception exception,
            string nodeId);

        [LoggerMessage(EventId = RedundancyKubernetesEventIds.KubernetesLeaseLeaderElection + 1, Level = LogLevel.Error,
            Message = "Kubernetes Lease election release failed for {NodeId}.")]
        public static partial void KubernetesLeaseElectionReleaseFailed(
            this ILogger logger,
            Exception exception,
            string nodeId);
    }

}
