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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Coordinates client actions for non-transparent server redundancy in a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// Implements the OPC 10000-4 §6.6.2.4.5 client Failover patterns for Cold, Warm, Hot (a), Hot (b), and
    /// HotAndMirrored modes using <see cref="ManagedSession"/> instances.
    /// </remarks>
    public sealed class RedundantManagedClient : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RedundantManagedClient"/> class.
        /// </summary>
        public RedundantManagedClient(
            IServerRedundancyHandler redundancyHandler,
            ArrayOf<IRedundantManagedClientSession> sessions,
            RedundantManagedClientOptions? options = null)
        {
            m_redundancyHandler = redundancyHandler
                ?? throw new ArgumentNullException(nameof(redundancyHandler));
            if (sessions.IsEmpty)
            {
                throw new ArgumentException("At least one redundant session is required.", nameof(sessions));
            }

            m_sessions = sessions;
            m_options = options ?? new RedundantManagedClientOptions();
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                m_sessions[ii].NotificationReceived += OnSessionNotificationReceived;
            }
        }

        /// <summary>
        /// Raised when a notification is delivered to the consumer.
        /// </summary>
        public event EventHandler<RedundantManagedClientNotificationEventArgs>? NotificationReceived;

        /// <summary>
        /// Gets the active managed session.
        /// </summary>
        public ManagedSession? CurrentSession => CurrentRedundantSession?.Session;

        /// <summary>
        /// Gets the active redundant session adapter.
        /// </summary>
        public IRedundantManagedClientSession? CurrentRedundantSession
        {
            get
            {
                lock (m_syncRoot)
                {
                    return m_currentRedundantSession;
                }
            }
        }

        /// <summary>
        /// Gets the redundancy mode currently managed by the orchestrator.
        /// </summary>
        public RedundancySupport Mode { get; private set; }

        /// <summary>
        /// Starts the redundant client.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            await m_sessions[0].ConnectAsync(ct).ConfigureAwait(false);
            m_redundancyInfo = await m_sessions[0]
                .FetchRedundancyInfoAsync(m_redundancyHandler, ct)
                .ConfigureAwait(false);
            Mode = m_redundancyInfo.Mode;
            ValidateSupportedMode();

            if (Mode is RedundancySupport.Warm or RedundancySupport.Hot)
            {
                await ConnectAllAsync(ct).ConfigureAwait(false);
            }
            else if (Mode == RedundancySupport.HotAndMirrored &&
                m_options.EnableHotAndMirroredStatusChecks)
            {
                await ConnectHotAndMirroredStatusSessionsAsync(ct).ConfigureAwait(false);
                StartHotAndMirroredStatusPolling();
            }

            await RefreshServiceLevelsAsync(ct).ConfigureAwait(false);
            SetCurrentRedundantSession(
                SelectHighestServiceLevelSession(requireConnected: true)
                ?? m_sessions[0]);
            await ApplyModeAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a logical subscription and replicates it according to the current mode.
        /// </summary>
        /// <remarks>
        /// The redundant client owns the supplied template and disposes it with the client.
        /// </remarks>
        public async ValueTask AddSubscriptionAsync(
            string subscriptionKey,
            Subscription template,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(subscriptionKey))
            {
                throw new ArgumentException("Subscription key must not be empty.", nameof(subscriptionKey));
            }

            if (template is null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (m_subscriptionTemplates.TryGetValue(subscriptionKey, out Subscription? existing) &&
                !ReferenceEquals(existing, template))
            {
                existing.Dispose();
            }

            m_subscriptionTemplates[subscriptionKey] = template;
            await ApplySubscriptionAsync(subscriptionKey, template, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Refreshes service levels and re-applies the active server selection.
        /// </summary>
        public async ValueTask RefreshServiceLevelsAsync(CancellationToken ct = default)
        {
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                if (m_sessions[ii].IsConnected)
                {
                    await m_sessions[ii].ReadServiceLevelAsync(ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Performs a failover decision and applies the resulting mode transition.
        /// </summary>
        public async ValueTask FailoverAsync(CancellationToken ct = default)
        {
            if (CurrentRedundantSession == null)
            {
                throw new InvalidOperationException("The redundant client has not been started.");
            }

            if (Mode == RedundancySupport.Cold)
            {
                await FailoverColdAsync(ct).ConfigureAwait(false);
                return;
            }

            if (Mode == RedundancySupport.HotAndMirrored)
            {
                await FailoverHotAndMirroredAsync(ct).ConfigureAwait(false);
                return;
            }

            await RefreshServiceLevelsAsync(ct).ConfigureAwait(false);
            IRedundantManagedClientSession? next = SelectHighestServiceLevelSession(requireConnected: true);
            if (next != null)
            {
                SetCurrentRedundantSession(next);
                await ApplyModeAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await StopHotAndMirroredStatusPollingAsync().ConfigureAwait(false);
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                m_sessions[ii].NotificationReceived -= OnSessionNotificationReceived;
                await m_sessions[ii].DisposeAsync().ConfigureAwait(false);
            }

            foreach (Subscription template in m_subscriptionTemplates.Values)
            {
                template.Dispose();
            }

            m_subscriptionTemplates.Clear();
        }

        private async ValueTask FailoverHotAndMirroredAsync(CancellationToken ct)
        {
            IRedundantManagedClientSession? current = CurrentRedundantSession;
            if (current == null)
            {
                throw new InvalidOperationException("The redundant client has not been started.");
            }

            await RefreshServiceLevelsAsync(ct).ConfigureAwait(false);
            IRedundantManagedClientSession? next = SelectHighestServiceLevelSession(
                requireConnected: false,
                excluded: current);
            if (next == null)
            {
                return;
            }

            await next
                .ActivateMirroredSessionAsync(current, ct)
                .ConfigureAwait(false);
            SetCurrentRedundantSession(next);
            await ApplyColdStateAsync(ct).ConfigureAwait(false);
        }

        private async ValueTask FailoverColdAsync(CancellationToken ct)
        {
            IRedundantManagedClientSession? current = CurrentRedundantSession;
            if (current == null || m_redundancyInfo == null)
            {
                throw new InvalidOperationException("The redundant client has not been started.");
            }

            ConfiguredEndpoint? endpoint = m_redundancyHandler.SelectFailoverTarget(
                m_redundancyInfo,
                current.Endpoint);
            IRedundantManagedClientSession? next = endpoint == null
                ? SelectHighestServiceLevelSession(requireConnected: false)
                : FindSession(endpoint);
            if (next == null)
            {
                return;
            }

            await next.ConnectAsync(ct).ConfigureAwait(false);
            SetCurrentRedundantSession(next);
            await ApplyModeAsync(ct).ConfigureAwait(false);
        }

        private async ValueTask ConnectAllAsync(CancellationToken ct)
        {
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                await m_sessions[ii].ConnectAsync(ct).ConfigureAwait(false);
            }
        }

        private async ValueTask ConnectHotAndMirroredStatusSessionsAsync(CancellationToken ct)
        {
            IRedundantManagedClientSession? current = CurrentRedundantSession;
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                if (ReferenceEquals(m_sessions[ii], current))
                {
                    continue;
                }

                await m_sessions[ii].ConnectAsync(ct).ConfigureAwait(false);
            }
        }

        private async ValueTask ApplyModeAsync(CancellationToken ct)
        {
            foreach (KeyValuePair<string, Subscription> item in m_subscriptionTemplates)
            {
                await ApplySubscriptionAsync(item.Key, item.Value, ct).ConfigureAwait(false);
            }

            if (Mode == RedundancySupport.Cold)
            {
                await ApplyColdStateAsync(ct).ConfigureAwait(false);
                return;
            }

            if (Mode == RedundancySupport.HotAndMirrored)
            {
                await ApplyColdStateAsync(ct).ConfigureAwait(false);
                return;
            }

            if (Mode == RedundancySupport.Warm)
            {
                await ApplyWarmStateAsync(ct).ConfigureAwait(false);
                return;
            }

            if (Mode == RedundancySupport.Hot &&
                m_options.HotNotificationMode == HotRedundancyNotificationMode.ReportingMerge)
            {
                await ApplyAllReportingStateAsync(ct).ConfigureAwait(false);
                return;
            }

            await ApplyReportingHandoffStateAsync(ct).ConfigureAwait(false);
        }

        private async ValueTask ApplySubscriptionAsync(
            string subscriptionKey,
            Subscription template,
            CancellationToken ct)
        {
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                IRedundantManagedClientSession session = m_sessions[ii];
                if (!ShouldHostSubscription(session))
                {
                    continue;
                }

                string appliedKey = GetAppliedKey(session, subscriptionKey);
                if (m_appliedSubscriptions.Contains(appliedKey))
                {
                    continue;
                }

                (MonitoringMode monitoringMode, bool publishingEnabled) = GetDesiredState(session);
                await session.AddSubscriptionAsync(
                    subscriptionKey,
                    template,
                    monitoringMode,
                    publishingEnabled,
                    ct).ConfigureAwait(false);
                m_appliedSubscriptions.Add(appliedKey);
            }
        }

        private bool ShouldHostSubscription(IRedundantManagedClientSession session)
        {
            return Mode is not (RedundancySupport.Cold or RedundancySupport.HotAndMirrored) ||
                ReferenceEquals(session, CurrentRedundantSession);
        }

        private (MonitoringMode monitoringMode, bool publishingEnabled) GetDesiredState(
            IRedundantManagedClientSession session)
        {
            if (Mode == RedundancySupport.Hot &&
                m_options.HotNotificationMode == HotRedundancyNotificationMode.ReportingMerge)
            {
                return (MonitoringMode.Reporting, true);
            }

            IRedundantManagedClientSession? current = CurrentRedundantSession;
            if (Mode == RedundancySupport.Warm && !ReferenceEquals(session, current))
            {
                return (MonitoringMode.Disabled, false);
            }

            return ReferenceEquals(session, current)
                ? (MonitoringMode.Reporting, true)
                : (MonitoringMode.Sampling, false);
        }

        private async ValueTask ApplyColdStateAsync(CancellationToken ct)
        {
            IRedundantManagedClientSession? current = CurrentRedundantSession;
            if (current != null)
            {
                await current
                    .SetSubscriptionStateAsync(MonitoringMode.Reporting, true, ct)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask ApplyReportingHandoffStateAsync(CancellationToken ct)
        {
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                if (!m_sessions[ii].IsConnected)
                {
                    continue;
                }

                IRedundantManagedClientSession? current = CurrentRedundantSession;
                bool isActive = ReferenceEquals(m_sessions[ii], current);
                await m_sessions[ii]
                    .SetSubscriptionStateAsync(
                        isActive ? MonitoringMode.Reporting : MonitoringMode.Sampling,
                        isActive,
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask ApplyWarmStateAsync(CancellationToken ct)
        {
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                if (!m_sessions[ii].IsConnected)
                {
                    continue;
                }

                IRedundantManagedClientSession? current = CurrentRedundantSession;
                bool isActive = ReferenceEquals(m_sessions[ii], current);
                await m_sessions[ii]
                    .SetSubscriptionStateAsync(
                        isActive ? MonitoringMode.Reporting : MonitoringMode.Disabled,
                        isActive,
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask ApplyAllReportingStateAsync(CancellationToken ct)
        {
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                if (m_sessions[ii].IsConnected)
                {
                    await m_sessions[ii]
                        .SetSubscriptionStateAsync(MonitoringMode.Reporting, true, ct)
                        .ConfigureAwait(false);
                }
            }
        }

        private IRedundantManagedClientSession? SelectHighestServiceLevelSession(
            bool requireConnected)
        {
            return SelectHighestServiceLevelSession(requireConnected, excluded: null);
        }

        private IRedundantManagedClientSession? SelectHighestServiceLevelSession(
            bool requireConnected,
            IRedundantManagedClientSession? excluded)
        {
            IRedundantManagedClientSession? best = null;
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                IRedundantManagedClientSession session = m_sessions[ii];
                if (ReferenceEquals(session, excluded))
                {
                    continue;
                }

                if (requireConnected && !session.IsConnected)
                {
                    continue;
                }

                if (best == null || session.ServiceLevel > best.ServiceLevel)
                {
                    best = session;
                }
            }

            return best;
        }

        private void StartHotAndMirroredStatusPolling()
        {
            if (m_hotAndMirroredStatusPolling != null)
            {
                return;
            }

            m_hotAndMirroredStatusCts = new CancellationTokenSource();
            m_hotAndMirroredStatusPolling = PollHotAndMirroredStatusAsync(
                m_hotAndMirroredStatusCts.Token);
        }

        private async Task PollHotAndMirroredStatusAsync(CancellationToken ct)
        {
            TimeSpan interval = m_options.HotAndMirroredStatusCheckInterval;
            if (interval <= TimeSpan.Zero)
            {
                interval = TimeSpan.FromSeconds(5);
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    await RefreshServiceLevelsAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ServiceResultException)
                {
                    // Best-effort status polling: a backup/active server may be
                    // down, disposing, or failing over (e.g. BadNotConnected).
                    // Keep polling the rest of the set rather than tearing down
                    // the poller on a transient read failure.
                }
            }
        }

        private async ValueTask StopHotAndMirroredStatusPollingAsync()
        {
            CancellationTokenSource? cts = m_hotAndMirroredStatusCts;
            Task? polling = m_hotAndMirroredStatusPolling;
            if (cts == null || polling == null)
            {
                return;
            }

            cts.Cancel();
            try
            {
                await polling.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            cts.Dispose();
            m_hotAndMirroredStatusCts = null;
            m_hotAndMirroredStatusPolling = null;
        }

        private IRedundantManagedClientSession? FindSession(ConfiguredEndpoint endpoint)
        {
            string? applicationUri = endpoint.Description.Server?.ApplicationUri;
            for (int ii = 0; ii < m_sessions.Count; ii++)
            {
                string? sessionUri = m_sessions[ii].Endpoint.Description.Server?.ApplicationUri;
                if (string.Equals(sessionUri, applicationUri, StringComparison.Ordinal))
                {
                    return m_sessions[ii];
                }
            }

            return null;
        }

        private static string GetAppliedKey(
            IRedundantManagedClientSession session,
            string subscriptionKey)
        {
            return $"{session.Endpoint.Description.Server?.ApplicationUri}:{subscriptionKey}";
        }

        private void SetCurrentRedundantSession(IRedundantManagedClientSession? session)
        {
            lock (m_syncRoot)
            {
                m_currentRedundantSession = session;
            }
        }

        private void ValidateSupportedMode()
        {
            if (Mode == RedundancySupport.Transparent)
            {
                throw new NotSupportedException(
                    "Transparent redundancy is handled by the server infrastructure.");
            }

        }

        private void OnSessionNotificationReceived(
            object? sender,
            RedundantManagedClientNotificationEventArgs e)
        {
            if (Mode == RedundancySupport.Hot &&
                m_options.HotNotificationMode == HotRedundancyNotificationMode.ReportingMerge)
            {
                var key = new NotificationIdentity(e);
                lock (m_syncRoot)
                {
                    if (!m_seenNotifications.Add(key))
                    {
                        return;
                    }

                    m_seenNotificationOrder.Enqueue(key);
                    while (m_seenNotificationOrder.Count > MaxSeenNotifications)
                    {
                        NotificationIdentity evicted = m_seenNotificationOrder.Dequeue();
                        _ = m_seenNotifications.Remove(evicted);
                    }
                }
            }
            else if (sender is IRedundantManagedClientSession session &&
                !ReferenceEquals(session, CurrentRedundantSession))
            {
                return;
            }

            NotificationReceived?.Invoke(this, e);
        }

        private readonly struct NotificationIdentity : IEquatable<NotificationIdentity>
        {
            public NotificationIdentity(RedundantManagedClientNotificationEventArgs e)
            {
                m_subscriptionKey = e.SubscriptionKey;
                m_clientHandle = e.ClientHandle;
                m_value = e.Value.WrappedValue;
                m_statusCode = e.Value.StatusCode;
                m_sourceTimestamp = e.Value.SourceTimestamp;
                m_sourcePicoseconds = e.Value.SourcePicoseconds;
            }

            public bool Equals(NotificationIdentity other)
            {
                return string.Equals(m_subscriptionKey, other.m_subscriptionKey, StringComparison.Ordinal) &&
                    m_clientHandle == other.m_clientHandle &&
                    m_statusCode == other.m_statusCode &&
                    m_sourceTimestamp == other.m_sourceTimestamp &&
                    m_sourcePicoseconds == other.m_sourcePicoseconds &&
                    m_value.TypeInfo.BuiltInType == other.m_value.TypeInfo.BuiltInType &&
                    m_value.TypeInfo.ValueRank == other.m_value.TypeInfo.ValueRank &&
                    m_value == other.m_value;
            }

            public override bool Equals(object? obj)
            {
                return obj is NotificationIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.Ordinal.GetHashCode(m_subscriptionKey);
                    hash = (hash * 397) ^ (int)m_clientHandle;
                    hash = (hash * 397) ^ m_statusCode.GetHashCode();
                    hash = (hash * 397) ^ m_sourceTimestamp.GetHashCode();
                    hash = (hash * 397) ^ m_sourcePicoseconds.GetHashCode();
                    hash = (hash * 397) ^ m_value.TypeInfo.BuiltInType.GetHashCode();
                    hash = (hash * 397) ^ m_value.TypeInfo.ValueRank.GetHashCode();
                    hash = (hash * 397) ^ m_value.GetHashCode();
                    return hash;
                }
            }

            private readonly string m_subscriptionKey;
            private readonly uint m_clientHandle;
            private readonly Variant m_value;
            private readonly StatusCode m_statusCode;
            private readonly DateTimeUtc m_sourceTimestamp;
            private readonly ushort m_sourcePicoseconds;
        }

        private readonly IServerRedundancyHandler m_redundancyHandler;
        private readonly ArrayOf<IRedundantManagedClientSession> m_sessions;
        private readonly RedundantManagedClientOptions m_options;
        private readonly Dictionary<string, Subscription> m_subscriptionTemplates = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_appliedSubscriptions = new(StringComparer.Ordinal);
        private readonly HashSet<NotificationIdentity> m_seenNotifications = new();
        private readonly Queue<NotificationIdentity> m_seenNotificationOrder = new();
        private readonly object m_syncRoot = new();
        private CancellationTokenSource? m_hotAndMirroredStatusCts;
        private Task? m_hotAndMirroredStatusPolling;
        private IRedundantManagedClientSession? m_currentRedundantSession;
        private ServerRedundancyInfo? m_redundancyInfo;
        private const int MaxSeenNotifications = 4096;
    }
}
