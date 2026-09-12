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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Connection;
using UaLens.Diagnostics;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Plugins.Continuity;

internal sealed class V2ContinuityBackendFactory : IContinuityBackendFactory
{
    public V2ContinuityBackendFactory(
        Func<ISession?> primary,
        ITelemetryContext telemetry,
        PublishLogObserver? publishLog = null,
        IContinuitySessionFactory? sessions = null)
    {
        m_primary = primary ?? throw new ArgumentNullException(nameof(primary));
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_publishLog = publishLog;
        m_sessions = sessions;
    }

    public IContinuityBackend Create(ContinuityTimeline timeline)
    {
        return new V2ContinuityBackend(m_primary, timeline, m_telemetry, m_publishLog, m_sessions);
    }

    private readonly Func<ISession?> m_primary;
    private readonly ITelemetryContext m_telemetry;
    private readonly PublishLogObserver? m_publishLog;
    private readonly IContinuitySessionFactory? m_sessions;
}

/// <summary>
/// Coordinates V2 subscription APIs; reconnect and redundancy remain ManagedSession
/// responsibilities. Snapshot buffers contain only this run's subscriptions and
/// never enter ordinary workspace JSON or the redacted evidence export.
/// </summary>
internal sealed class V2ContinuityBackend : IContinuityBackend
{
    public V2ContinuityBackend(
        Func<ISession?> primary,
        ContinuityTimeline timeline,
        ITelemetryContext telemetry,
        PublishLogObserver? publishLog = null,
        IContinuitySessionFactory? sessions = null)
    {
        m_primary = primary ?? throw new ArgumentNullException(nameof(primary));
        m_timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        ArgumentNullException.ThrowIfNull(telemetry);
        m_log = telemetry.CreateLogger<V2ContinuityBackend>();
        m_publishLog = publishLog;
        m_sessions = sessions;
    }

    public ContinuitySetup CheckSetup(ContinuityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ISession? primary = m_primary();
        if (primary is null || !primary.Connected)
        {
            return new(ContinuityAvailability.RequiresConfiguration, "Connect the primary workspace session first.");
        }
        if (!primary.TryGetSubscriptionManager(out _))
        {
            return new(ContinuityAvailability.Unsupported, "Continuity requires the V2 channel subscription engine.");
        }
        if (configuration.Scenario == ContinuityScenario.GracefulDurableRestore &&
            primary.Identity.TokenType == UserTokenType.IssuedToken)
        {
            return new(ContinuityAvailability.Unsupported,
                "The Quickstarts durable store excludes issued-token subscriptions. " +
                "Select a supported identity explicitly; the lab will not downgrade it.");
        }
        if (UsesAuxiliary(configuration.Scenario))
        {
            return m_sessions?.CheckSetup(configuration.Scenario) ??
                new(ContinuityAvailability.RequiresConfiguration,
                    "Configure IContinuitySessionFactory through the identity/trust owner for lab-only sessions. " +
                    "Transfer requires the same user and security. The primary session will not be closed.");
        }
        return new(ContinuityAvailability.Supported,
            "Start creates one bounded logical subscription on the primary session; other documents are untouched.");
    }

    public async Task StartAsync(ContinuityConfiguration configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_configuration is not null)
        {
            throw new InvalidOperationException("The backend already owns a run.");
        }
        ContinuitySetup setup = CheckSetup(configuration);
        if (!setup.CanStart)
        {
            throw new InvalidOperationException(setup.Description);
        }
        m_configuration = configuration;
        m_expectedPrimary = m_primary();
        if (UsesAuxiliary(configuration.Scenario))
        {
            await OpenOwnedSessionAsync(ContinuitySessionPurpose.Source, ct).ConfigureAwait(false);
        }
        else
        {
            Attach(m_primary() ?? throw new InvalidOperationException("The primary connection was released."));
        }
        await CreateSubscriptionAsync(ct).ConfigureAwait(false);
        if (m_log.IsEnabled(LogLevel.Information))
        {
            m_log.ContinuityOperation("Started", configuration.Scenario.ToString());
        }
    }

    public async Task<ContinuityStepResult> StepAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ContinuityConfiguration configuration = m_configuration ??
            throw new InvalidOperationException("Start a scenario first.");
        ct.ThrowIfCancellationRequested();
        if (m_stepCompleted)
        {
            throw new InvalidOperationException(
                "This scenario step already ran. Stop and explicitly start a new run.");
        }
        switch (configuration.Scenario)
        {
            case ContinuityScenario.Observe:
                throw new InvalidOperationException(
                    "Observation has no injected step. A user-managed outage is only observed.");
            case ContinuityScenario.RecreateOwnedSubscription:
                ISubscription subscription = RequireSubscription();
                m_timeline.Record(ContinuityEvidenceKind.Recreated,
                    "Explicit own-subscription recreation requested; retransmission history may be lost.",
                    m_clientSessionId, DiagnosticCorrelation.Subscription(subscription));
                await subscription.RecreateAsync(ct).ConfigureAwait(false);
                await WaitForItemsAsync(subscription, ct).ConfigureAwait(false);
                m_stepCompleted = true;
                return new(false, "Own-subscription recreation completed. See lifecycle and new partition evidence.");
            case ContinuityScenario.GracefulDurableRestore when m_snapshot.IsNull:
                await SaveAndCloseAsync(retain: true, ct).ConfigureAwait(false);
                return new(true,
                    "Runtime snapshot retained in this document only. Gracefully shut down and restart the " +
                    "configured durable sample yourself, then choose Restore. " +
                    "No process was stopped or restarted by UaLens.");
            case ContinuityScenario.GracefulDurableRestore:
                return await RestoreAsync(ContinuitySessionPurpose.Restore, transfer: true, ct).ConfigureAwait(false);
            case ContinuityScenario.TransferOnLoad:
                await SaveAndCloseAsync(retain: true, ct).ConfigureAwait(false);
                return await RestoreAsync(ContinuitySessionPurpose.Restore, transfer: true, ct).ConfigureAwait(false);
            case ContinuityScenario.RecreateOnLoad:
                await SaveAndCloseAsync(retain: false, ct).ConfigureAwait(false);
                return await RestoreAsync(ContinuitySessionPurpose.Restore, transfer: false, ct).ConfigureAwait(false);
            case ContinuityScenario.ConfiguredFailover:
                await SaveAndCloseAsync(retain: true, ct).ConfigureAwait(false);
                return await RestoreAsync(ContinuitySessionPurpose.RedundantTarget, transfer: true, ct)
                    .ConfigureAwait(false);
            default:
                throw new InvalidOperationException("Unsupported scenario.");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Exception? cleanupFailure = null;
        try
        {
            await ReleaseSubscriptionsAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            TimeoutException or IOException or InvalidOperationException or NotSupportedException or
            UnauthorizedAccessException or AggregateException)
        {
            cleanupFailure = exception;
            RecordFailure("Subscription cleanup", exception);
        }
        finally
        {
            Detach();
            IContinuitySessionLease? lease = m_lease;
            m_lease = null;
            try
            {
                if (lease is not null)
                {
                    try
                    {
                        await lease.CloseAsync(retainSubscriptions: false, ct).ConfigureAwait(false);
                        m_timeline.Record(ContinuityEvidenceKind.CleanupConfirmed,
                            "CloseSession(deleteSubscriptions:true) completed for the owned auxiliary session.",
                            m_clientSessionId, statusCode: StatusCodes.Good.Code);
                    }
                    catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
                        TimeoutException or IOException or InvalidOperationException or NotSupportedException or
                        UnauthorizedAccessException or AggregateException)
                    {
                        cleanupFailure ??= exception;
                        RecordFailure("Owned-session cleanup", exception);
                    }
                    finally
                    {
                        await lease.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_snapshot = default;
                m_expectedPrimary = null;
            }
        }
        if (m_retainedResourcesUnconfirmed)
        {
            m_timeline.Record(ContinuityEvidenceKind.CleanupUncertain,
                "Previously retained source subscriptions could not all be reclaimed. They may remain until " +
                "their revised lifetime expires. Stop does not transfer potentially stale server ids automatically.");
            foreach (uint id in m_savedPartitionIds)
            {
                m_timeline.Record(ContinuityEvidenceKind.CleanupUncertain,
                    "Last observed retained source partition; current existence/deletion is unconfirmed.",
                    m_savedClientSessionId, m_savedClientSubscriptionId, id);
            }
            throw new InvalidOperationException("Retained source-resource cleanup remains unconfirmed.");
        }
        if (cleanupFailure is not null)
        {
            throw new InvalidOperationException("Owned-resource cleanup failed.", cleanupFailure);
        }
    }

    public ArrayOf<DiagnosticMetric> CaptureDiagnostics()
    {
        List<DiagnosticMetric> rows = CorrelatedDiagnostics.Capture(m_session, m_publishLog).ToList();
        ContinuityConfiguration? configuration = m_configuration;
        if (configuration is null)
        {
            return new ArrayOf<DiagnosticMetric>(rows.ToArray());
        }
        rows.Add(new("Requested publish / sample ms",
            $"{Number(configuration.PublishingIntervalMs)} / {Number(configuration.SamplingIntervalMs)}"));
        rows.Add(new("Requested lifetime / keep-alive counts",
            $"{configuration.LifetimeCount} / {configuration.KeepAliveCount}"));
        rows.Add(new("Requested item queue / partition cap",
            $"{configuration.QueueSize} / {configuration.ItemsPerPartition}"));
        rows.Add(new("Requested max notifications / publish",
            ContinuityState.MaxNotificationsPerPublish.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new("Durable lifetime requested / revised",
            configuration.Durable
                ? $"{configuration.DurableLifetimeHours} h / " +
                    (m_revisedDurable is TimeSpan revised ? Number(revised.TotalHours) + " h" : "unknown")
                : "Not requested"));
        if (configuration.Durable)
        {
            rows.Add(new("Durability observation limits",
                "Revised hours are the last SetAsDurable result, not remaining lifetime. V2 snapshots do not " +
                "persist the imperative durability policy for future automatic recreation. A transferred " +
                "server subscription may remain durable; later creation requires fresh confirmation."));
        }
        ISubscription? subscription = m_subscription;
        ContinuityNotificationHandler? handler = m_handler;
        if (handler is not null)
        {
            rows.Add(new("Last V2 state callback", handler.StateSummary));
            rows.Add(new("Last data callback UTC",
                handler.LastDataUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "Unknown / none observed"));
            rows.Add(new("Last keep-alive callback UTC",
                handler.LastKeepAliveUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "Unknown / none observed"));
        }
        if (subscription is not null && m_session?.Connected == true && subscription.Created)
        {
            rows.Add(new("Client subscription correlation",
                DiagnosticCorrelation.Subscription(subscription).ToString(CultureInfo.InvariantCulture)));
            rows.Add(new("Revised publishing interval ms",
                Number(subscription.CurrentPublishingInterval.TotalMilliseconds)));
            rows.Add(new("Revised lifetime / keep-alive counts",
                $"{subscription.CurrentLifetimeCount} / {subscription.CurrentKeepAliveCount}"));
            rows.Add(new("Applied max notifications / publish",
                subscription.CurrentMaxNotificationsPerPublish.ToString(CultureInfo.InvariantCulture)));
            if (subscription is IPartitionedSubscription partitioned)
            {
                rows.Add(new("Server partition ids", string.Join(", ", partitioned.PartitionIds)));
            }
            else
            {
                rows.Add(new("Server partition ids", "Unknown: not exposed by this subscription."));
            }
            int i = 0;
            foreach (IMonitoredItem item in subscription.MonitoredItems.Items)
            {
                if (++i > ContinuityState.MaxTargets)
                {
                    break;
                }
                rows.Add(new($"Item {i}: sample ms / queue / status",
                    $"{Number(item.CurrentSamplingInterval.TotalMilliseconds)} / {item.CurrentQueueSize} / " +
                    (item.Created
                        ? item.Error?.StatusCode.ToString() ?? "Good (no item error reported)"
                        : "creation unconfirmed")));
            }
        }
        rows.Add(new("Runtime subscription snapshot",
            m_snapshot.IsNull ? "None" : "Memory only; not workspace/export data"));
        rows.Add(new("Sample interpretation",
            configuration.MonotonicSample
                ? "Integer +1 observation enabled. Skipped numbers may be sampling/filtering, not delivery loss."
                : "No counter contract asserted for the selected source."));
        return new ArrayOf<DiagnosticMetric>(rows.ToArray());
    }

    public ValueTask DisposeAsync()
    {
        lock (m_disposeGate)
        {
            m_disposeTask ??= DisposeCoreAsync();
            return new ValueTask(m_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        m_disposed = true;
        try
        {
            if (m_subscription is not null || m_lease is not null || !m_snapshot.IsNull)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await StopAsync(cleanup.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            Detach();
            m_snapshot = default;
            m_expectedPrimary = null;
        }
    }

    private async Task CreateSubscriptionAsync(CancellationToken ct)
    {
        ContinuityConfiguration configuration = m_configuration!;
        ISession session = m_session!;
        m_handler = new ContinuityNotificationHandler(
            m_timeline, m_clientSessionId, configuration.MonotonicSample, m_publishLog, continueSampleSeries: true);
        var options = new V2SubscriptionOptions
        {
            PublishingInterval = TimeSpan.FromMilliseconds(configuration.PublishingIntervalMs),
            KeepAliveCount = configuration.KeepAliveCount,
            LifetimeCount = configuration.LifetimeCount,
            PublishingEnabled = true,
            MaxNotificationsPerPublish = ContinuityState.MaxNotificationsPerPublish,
            MaxMonitoredItemsPerPartition = configuration.ItemsPerPartition,
            MaxPartitionCount = ContinuityState.MaxTargets,
            MinLifetimeInterval = TimeSpan.FromSeconds(1),
            RecoveryPolicy = SubscriptionRecoveryPolicy.ReportOnly
        };
        ISubscription subscription = RequireManager().Add(m_handler, new OptionsMonitor<V2SubscriptionOptions>(options));
        m_subscription = subscription;
        await WaitUntilAsync(() => subscription.Created, ct).ConfigureAwait(false);
        if (configuration.Durable)
        {
            m_revisedDurable = await subscription.SetAsDurableAsync(
                TimeSpan.FromHours(configuration.DurableLifetimeHours), ct).ConfigureAwait(false);
            m_timeline.Record(ContinuityEvidenceKind.Durable,
                $"Durability requested before monitored items: {configuration.DurableLifetimeHours} h; " +
                $"revised {Number(m_revisedDurable.Value.TotalHours)} h.",
                m_clientSessionId, DiagnosticCorrelation.Subscription(subscription));
        }
        int index = 0;
        foreach (ExpandedNodeId target in configuration.Targets)
        {
            ct.ThrowIfCancellationRequested();
            NodeId node = ExpandedNodeId.ToNodeId(target, session.NamespaceUris);
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            var itemOptions = new V2MonitoredItemOptions
            {
                StartNodeId = node,
                AttributeId = Attributes.Value,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = TimeSpan.FromMilliseconds(configuration.SamplingIntervalMs),
                QueueSize = configuration.QueueSize,
                DiscardOldest = true
            };
            string key = "sample-" + (++index).ToString(CultureInfo.InvariantCulture);
            if (!subscription.MonitoredItems.TryAdd(
                key, new OptionsMonitor<V2MonitoredItemOptions>(itemOptions), out IMonitoredItem? item) || item is null)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyMonitoredItems);
            }
        }
        await WaitForItemsAsync(subscription, ct).ConfigureAwait(false);
    }

    private async Task SaveAndCloseAsync(bool retain, CancellationToken ct)
    {
        ISubscription subscription = RequireSubscription();
        IContinuitySessionLease lease = m_lease ??
            throw new InvalidOperationException("The primary workspace session cannot be closed by a lab scenario.");
        if (!m_snapshot.IsNull)
        {
            throw new InvalidOperationException("Restore the existing runtime snapshot before saving another.");
        }
        using var stream = new MemoryStream(new byte[MaxSnapshotBytes], writable: true);
        stream.SetLength(0);
        await RequireManager().SaveAsync(
            stream, m_session!.MessageContext, new[] { subscription }, ct).ConfigureAwait(false);
        if (stream.Length > MaxSnapshotBytes)
        {
            throw new InvalidOperationException("The lab runtime snapshot exceeds its 2 MiB limit.");
        }
        m_snapshot = ByteString.From(stream.ToArray());
        m_savedPartitionIds = PartitionIds(subscription);
        m_savedClientSessionId = m_clientSessionId;
        m_savedClientSubscriptionId = DiagnosticCorrelation.Subscription(subscription);
        m_timeline.Record(ContinuityEvidenceKind.SnapshotSaved,
            "Only the lab subscription was saved using V2 binary SaveAsync; queues are server-owned.",
            m_clientSessionId, DiagnosticCorrelation.Subscription(subscription));
        if (!retain)
        {
            await ReleaseSubscriptionsAsync().ConfigureAwait(false);
        }
        // Closing the owned session first is intentional. Disposing a live
        // subscription before Close(deleteSubscriptions:false) would delete it.
        m_retainedResourcesUnconfirmed = retain;
        await lease.CloseAsync(retain, ct).ConfigureAwait(false);
        Detach();
        m_subscription = null;
        m_handler = null;
        m_lease = null;
        await lease.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<ContinuityStepResult> RestoreAsync(
        ContinuitySessionPurpose purpose,
        bool transfer,
        CancellationToken ct)
    {
        if (m_snapshot.IsNull)
        {
            throw new InvalidOperationException("No lab-owned runtime snapshot is available.");
        }
        await OpenOwnedSessionAsync(purpose, ct).ConfigureAwait(false);
        var handler = new ContinuityNotificationHandler(
            m_timeline, m_clientSessionId, m_configuration!.MonotonicSample, m_publishLog, continueSampleSeries: true);
        m_handler = handler;
        m_timeline.Record(ContinuityEvidenceKind.RestoreRequested,
            transfer
                ? "V2 transfer-on-load requested; rejected transfers may recreate. Completion is not transfer proof."
                : "V2 recreate-on-load requested; old retransmission queues cannot be recovered.",
            m_clientSessionId);
        using var stream = new MemoryStream(m_snapshot.ToArray(), writable: false);
        IReadOnlyList<ISubscription> loaded = await RequireManager().LoadAsync(
            stream, m_session!.MessageContext, _ => handler, transfer, ct).ConfigureAwait(false);
        if (loaded.Count != 1)
        {
            throw new InvalidOperationException("A lab snapshot must restore exactly one logical subscription.");
        }
        m_subscription = loaded[0];
        await WaitForItemsAsync(m_subscription, ct).ConfigureAwait(false);
        ArrayOf<uint> restoredIds = PartitionIds(m_subscription);
        bool recreated = !transfer || handler.CreationObservations > 0 ||
            (!m_savedPartitionIds.IsEmpty && !restoredIds.IsEmpty &&
                !SameIds(m_savedPartitionIds, restoredIds));
        bool transferObserved = handler.TransferObservations > 0;
        if (recreated)
        {
            m_timeline.Record(ContinuityEvidenceKind.Recreated,
                transfer
                    ? "Creation/new partition evidence after transfer-on-load: recreate fallback observed."
                    : "Recreate-on-load completed with newly created server resources.",
                m_clientSessionId, DiagnosticCorrelation.Subscription(m_subscription));
            if (m_configuration.Durable)
            {
                // V2's current serialized options do not persist the imperative
                // durability call. A recreated subscription already has items.
                await ReleaseSubscriptionsAsync().ConfigureAwait(false);
                m_timeline.Record(ContinuityEvidenceKind.Durable,
                    "Restore recreated non-durable state; replacing only that lab subscription to set durability " +
                    "before its monitored items. Old queued values are not preserved.");
                await CreateSubscriptionAsync(ct).ConfigureAwait(false);
            }
        }
        if (transferObserved && !recreated && SameIds(m_savedPartitionIds, restoredIds) &&
            purpose == ContinuitySessionPurpose.Restore)
        {
            m_retainedResourcesUnconfirmed = false;
        }
        m_snapshot = default;
        m_stepCompleted = true;
        m_log.ContinuityOperation(
            "Restore", recreated ? "Recreated" : transferObserved ? "TransferObserved" : "Unknown");
        return new(false,
            recreated
                ? "Recreation observed, not seamless transfer. Any retained source resources may expire separately."
                : transferObserved
                    ? "Transfer transition observed. Per-partition recovery and queue completeness remain unproven."
                    : "Load completed, but no transfer/creation callback established the outcome yet.");
    }

    private async Task OpenOwnedSessionAsync(ContinuitySessionPurpose purpose, CancellationToken ct)
    {
        if (m_sessions is null)
        {
            throw new InvalidOperationException("Configure the scenario-owned session factory first.");
        }
        IContinuitySessionLease lease = await m_sessions.OpenAsync(purpose, ct).ConfigureAwait(false);
        if (ReferenceEquals(lease.Session, m_primary()))
        {
            // A broken factory must never cause us to dispose the primary session.
            throw new InvalidOperationException("The auxiliary factory returned the primary workspace session.");
        }
        m_lease = lease;
        m_sessionPurpose = purpose;
        RequireEquivalentSecurity(lease.Session, purpose);
        Attach(lease.Session);
        ct.ThrowIfCancellationRequested();
        _ = RequireManager();
    }

    private void Attach(ISession session)
    {
        if (!session.Connected)
        {
            throw new InvalidOperationException("The scenario session is not connected.");
        }
        m_session = session;
        m_clientSessionId = DiagnosticCorrelation.Session(session);
        m_timeline.Record(ContinuityEvidenceKind.Connection,
            m_lease is null
                ? "Borrowing the primary session; ownership unchanged."
                : "Opened a scenario-owned session.",
            m_clientSessionId);
        if (session is ManagedSession managed)
        {
            managed.ConnectionStateChanged += OnConnectionStateChanged;
            managed.ChannelStateChanged += OnChannelStateChanged;
        }
    }

    private void RequireEquivalentSecurity(ISession session, ContinuitySessionPurpose purpose)
    {
        ISession primary = m_expectedPrimary ??
            throw new InvalidOperationException("The original workspace connection is no longer available.");
        EndpointDescription expected = primary.Endpoint;
        EndpointDescription actual = session.Endpoint;
        IUserIdentity expectedIdentity = primary.Identity;
        IUserIdentity actualIdentity = session.Identity;
        if (expected.SecurityMode != actual.SecurityMode ||
            !string.Equals(expected.SecurityPolicyUri, actual.SecurityPolicyUri, StringComparison.Ordinal) ||
            expectedIdentity.TokenType != actualIdentity.TokenType ||
            (purpose != ContinuitySessionPurpose.RedundantTarget &&
                !string.Equals(expectedIdentity.PolicyId, actualIdentity.PolicyId, StringComparison.Ordinal)) ||
            !string.Equals(expectedIdentity.DisplayName, actualIdentity.DisplayName, StringComparison.Ordinal))
        {
            throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                "The configured auxiliary session differs from the primary identity/security selection.");
        }
        if (purpose != ContinuitySessionPurpose.RedundantTarget &&
            (!ConnectionProfile.EndpointUrlsMatch(expected.EndpointUrl, actual.EndpointUrl) ||
                !string.Equals(
                    expected.Server?.ApplicationUri, actual.Server?.ApplicationUri, StringComparison.Ordinal)))
        {
            throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected,
                "Ordinary transfer/restore must use the original server endpoint, not an unconfigured failover.");
        }
    }

    private void Detach()
    {
        if (m_session is ManagedSession managed)
        {
            managed.ConnectionStateChanged -= OnConnectionStateChanged;
            managed.ChannelStateChanged -= OnChannelStateChanged;
        }
        m_session = null;
    }

    private async Task ReleaseSubscriptionsAsync()
    {
        ISubscription? subscription = m_subscription;
        m_subscription = null;
        if (subscription is not null)
        {
            bool connected = m_session?.Connected == true;
            ArrayOf<uint> ids = PartitionIds(subscription);
            await subscription.DisposeAsync().ConfigureAwait(false);
            m_timeline.Record(ContinuityEvidenceKind.CleanupUncertain,
                connected
                    ? "V2 client disposal completed. Server deletion is requested internally, but its result is " +
                        "not exposed by ISubscription.DisposeAsync; consult correlated stack telemetry."
                    : "Client subscription disposed while disconnected; server deletion cannot be confirmed.",
                m_clientSessionId, DiagnosticCorrelation.Subscription(subscription));
            foreach (uint id in ids)
            {
                m_timeline.Record(ContinuityEvidenceKind.CleanupUncertain,
                    "Last observed owned partition; deletion response is not exposed.",
                    m_clientSessionId, DiagnosticCorrelation.Subscription(subscription), id);
            }
            if (m_sessionPurpose == ContinuitySessionPurpose.Source && m_lease is not null &&
                !m_retainedResourcesUnconfirmed)
            {
                m_timeline.Record(ContinuityEvidenceKind.Lifecycle,
                    "The owned source session will also be closed with deleteSubscriptions:true.");
            }
            // Do not retry DeleteSubscriptions with old ids: a reconnect/server
            // restart can reassign those ids to an unrelated document.
        }
        // An unsuccessful Load can register a subset before throwing. Only an
        // owned auxiliary session can have all of its manager items reclaimed.
        if (m_lease is not null && m_session is not null &&
            m_session.TryGetSubscriptionManager(out ISubscriptionManager? manager) && manager is not null)
        {
            var remaining = new List<ISubscription>(manager.Items);
            foreach (ISubscription item in remaining)
            {
                if (!ReferenceEquals(item, subscription))
                {
                    await item.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        m_handler = null;
    }

    private ISubscription RequireSubscription()
    {
        return m_subscription ?? throw new InvalidOperationException("No active lab subscription.");
    }

    private ISubscriptionManager RequireManager()
    {
        return m_session is not null &&
            m_session.TryGetSubscriptionManager(out ISubscriptionManager? manager) && manager is not null
            ? manager
            : throw new InvalidOperationException("A V2 subscription manager is required.");
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs args)
    {
        if (!ReferenceEquals(sender, m_session))
        {
            return;
        }
        m_timeline.Record(ContinuityEvidenceKind.Connection,
            $"ManagedSession {args.PreviousState} → {args.NewState}; attempt {args.ReconnectAttempt}.",
            m_clientSessionId, statusCode: args.Error?.StatusCode.Code);
    }

    private void OnChannelStateChanged(ManagedSession sender, ChannelStateChange change)
    {
        if (!ReferenceEquals(sender, m_session))
        {
            return;
        }
        m_timeline.Record(ContinuityEvidenceKind.Transport,
            $"Managed channel {change.PreviousState} → {change.NewState}; attempt {change.ReconnectAttempt}.",
            m_clientSessionId, statusCode: change.Error?.StatusCode.Code);
    }

    private void RecordFailure(string operation, Exception exception)
    {
        string failure = CorrelatedDiagnostics.Failure(exception);
        m_log.ContinuityOperationFailed(operation, failure);
        m_timeline.Record(ContinuityEvidenceKind.CleanupUncertain, operation + ": " + failure, m_clientSessionId);
    }

    private static Task WaitForItemsAsync(ISubscription subscription, CancellationToken ct)
    {
        return WaitUntilAsync(() =>
        {
            if (!subscription.Created)
            {
                return false;
            }
            bool created = true;
            foreach (IMonitoredItem item in subscription.MonitoredItems.Items)
            {
                if (ServiceResult.IsBad(item.Error))
                {
                    throw new ServiceResultException(item.Error);
                }
                created &= item.Created;
            }
            return created && subscription.MonitoredItems.Count > 0;
        }, ct);
    }

    private static async Task WaitUntilAsync(Func<bool> ready, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            while (!ready())
            {
                await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The server did not confirm lab resource creation within 20 seconds.");
        }
    }

    private static ArrayOf<uint> PartitionIds(ISubscription subscription)
    {
        return subscription is IPartitionedSubscription partitioned
            ? new ArrayOf<uint>(new List<uint>(partitioned.PartitionIds).ToArray())
            : default;
    }

    private static bool SameIds(ArrayOf<uint> before, ArrayOf<uint> after)
    {
        if (before.IsEmpty || before.Count != after.Count)
        {
            return false;
        }
        for (int i = 0; i < before.Count; i++)
        {
            if (before[i] == 0 || before[i] != after[i])
            {
                return false;
            }
        }
        return true;
    }

    private static bool UsesAuxiliary(ContinuityScenario scenario)
    {
        return scenario is not (ContinuityScenario.Observe or ContinuityScenario.RecreateOwnedSubscription);
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private const int MaxSnapshotBytes = 2 * 1024 * 1024;
    private readonly System.Threading.Lock m_disposeGate = new();
    private readonly Func<ISession?> m_primary;
    private readonly ContinuityTimeline m_timeline;
    private readonly ILogger m_log;
    private readonly PublishLogObserver? m_publishLog;
    private readonly IContinuitySessionFactory? m_sessions;
    private ContinuityConfiguration? m_configuration;
    private ISession? m_session;
    private ISession? m_expectedPrimary;
    private IContinuitySessionLease? m_lease;
    private ISubscription? m_subscription;
    private ContinuityNotificationHandler? m_handler;
    private Guid m_clientSessionId;
    private TimeSpan? m_revisedDurable;
    private ByteString m_snapshot;
    private ArrayOf<uint> m_savedPartitionIds;
    private Guid m_savedClientSessionId;
    private long m_savedClientSubscriptionId;
    private bool m_retainedResourcesUnconfirmed;
    private bool m_stepCompleted;
    private ContinuitySessionPurpose m_sessionPurpose;
    private bool m_disposed;
    private Task? m_disposeTask;
}

internal static partial class V2ContinuityBackendLog
{
    [LoggerMessage(EventId = UaLensEventIds.ContinuityOperation, Level = LogLevel.Information,
        Message = "Continuity operation {Operation}: {Outcome}.")]
    public static partial void ContinuityOperation(this ILogger logger, string operation, string outcome);

    [LoggerMessage(EventId = UaLensEventIds.ContinuityOperationFailed, Level = LogLevel.Warning,
        Message = "Continuity operation {Operation} failed: {Status}.")]
    public static partial void ContinuityOperationFailed(this ILogger logger, string operation, string status);
}
