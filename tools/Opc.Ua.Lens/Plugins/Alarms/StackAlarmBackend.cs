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
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.ViewModels;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using V2SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Resolves the live host only when opening. The observation, not the connection
/// service, owns the logical V2 subscription and its single monitored event source.
/// </summary>
internal sealed class StackAlarmBackend : IAlarmBackend
{
    public StackAlarmBackend(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async ValueTask<IAlarmObservation> OpenAsync(
        AlarmSource source,
        TimeSpan publishingInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!m_host.Connection.IsConnected || m_host.Session is not ManagedSession session)
        {
            throw new InvalidOperationException("Connect the primary managed session before observing alarms.");
        }
        if (!session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
        {
            throw new NotSupportedException(
                "Alarms requires the Channel V2 subscription engine. Change the connection engine.");
        }
        if (!ExpandedNodeId.TryParse(source.TargetId, out ExpandedNodeId portable) || portable.ServerIndex != 0)
        {
            throw new ArgumentException("Select a local event notifier.", nameof(source));
        }
        NodeId sourceId = ExpandedNodeId.ToNodeId(portable, session.NamespaceUris);
        if (sourceId.IsNull)
        {
            throw new InvalidOperationException("The selected source namespace is not available on this server.");
        }
        ReadResponse response = await session.ReadAsync(
            null,
            0,
            TimestampsToReturn.Neither,
            [new ReadValueId { NodeId = sourceId, AttributeId = Attributes.EventNotifier }],
            cancellationToken).ConfigureAwait(false);
        if (response.Results.Count != 1)
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "The EventNotifier read was incomplete.");
        }
        DataValue value = response.Results[0];
        if (StatusCode.IsBad(value.StatusCode))
        {
            throw new ServiceResultException(value.StatusCode, "Cannot read the selected source's EventNotifier.");
        }
        if (!value.WrappedValue.TryGetValue(out byte notifier) || (notifier & EventNotifiers.SubscribeToEvents) == 0)
        {
            throw new NotSupportedException("The selected Object or View does not allow event subscriptions.");
        }

        var observation = new StackAlarmObservation(session, manager, m_host.Telemetry);
        try
        {
            await observation.InitializeAsync(sourceId, publishingInterval, cancellationToken).ConfigureAwait(false);
            return observation;
        }
        catch
        {
            await observation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private readonly PluginHost m_host;
}

/// <summary>
/// Handler callbacks only project and enqueue bounded data. Refresh and command
/// calls happen on the owning module's task, outside V2 notification callbacks.
/// </summary>
internal sealed class StackAlarmObservation : IAlarmObservation, ISubscriptionNotificationHandler
{
    public StackAlarmObservation(ISession session, ISubscriptionManager manager, ITelemetryContext telemetry)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
        ArgumentNullException.ThrowIfNull(telemetry);
        m_operations = new AlarmCommandAdapter(session, session.NamespaceUris, telemetry);
        m_updates = Channel.CreateBounded<AlarmUpdate>(new BoundedChannelOptions(AlarmLimits.PendingUpdates)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public ChannelReader<AlarmUpdate> Updates => m_updates.Reader;

    public AlarmStreamHealth Health
    {
        get
        {
            ISubscription? subscription = m_subscription;
            IMonitoredItem? item = m_item;
            bool ready = Volatile.Read(ref m_disposed) == 0 &&
                Volatile.Read(ref m_unavailable) == 0 && m_session.Connected &&
                subscription is { Created: true } && item is { Created: true } && !ServiceResult.IsBad(item.Error);
            string detail = ready
                ? "V2 event source created."
                : item is not null && ServiceResult.IsBad(item.Error)
                    ? $"Event source: {item.Error.StatusCode}."
                    : "Waiting for the managed session and event source.";
            return new AlarmStreamHealth(
                ready,
                detail,
                subscription?.CurrentPublishingInterval ?? TimeSpan.Zero,
                Interlocked.Read(ref m_droppedUpdates),
                subscription?.MissingMessageCount ?? 0,
                subscription?.RepublishMessageCount ?? 0,
                subscription is IPartitionedSubscription partitions ? [.. partitions.PartitionIds] : [0]);
        }
    }

    public async Task InitializeAsync(NodeId sourceId, TimeSpan publishingInterval, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        m_subscription = m_manager.Add(this, new OptionsMonitor<V2SubscriptionOptions>(new V2SubscriptionOptions
        {
            PublishingInterval = publishingInterval,
            PublishingEnabled = true,
            KeepAliveCount = 10,
            LifetimeCount = 100,
            MinLifetimeInterval = TimeSpan.FromMinutes(1),
            MaxNotificationsPerPublish = AlarmLimits.PendingUpdates,
            MaxPartitionCount = AlarmLimits.Partitions
        }));
        var options = new OptionsMonitor<V2MonitoredItemOptions>(new V2MonitoredItemOptions
        {
            StartNodeId = sourceId,
            AttributeId = Attributes.EventNotifier,
            MonitoringMode = MonitoringMode.Reporting,
            QueueSize = AlarmLimits.PendingUpdates,
            DiscardOldest = true,
            SamplingInterval = TimeSpan.Zero,
            Filter = m_projection.Filter
        });
        if (!m_subscription.MonitoredItems.TryAdd("alarms-source", options, out m_item) || m_item is null)
        {
            throw new InvalidOperationException("The V2 subscription did not accept the alarm event source.");
        }
        var readiness = Stopwatch.StartNew();
        while (!Health.IsReady)
        {
            if (ServiceResult.IsBad(m_item.Error))
            {
                throw new ServiceResultException(m_item.Error);
            }
            if (readiness.Elapsed >= TimeSpan.FromSeconds(20))
            {
                throw new TimeoutException(
                    "The server did not create the alarm subscription and event source in time.");
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        if (m_item.FilterResult is EventFilterResult result &&
            result.SelectClauseResults.Count > m_projection.ConditionIdIndex &&
            StatusCode.IsBad(result.SelectClauseResults[m_projection.ConditionIdIndex]))
        {
            throw new ServiceResultException(result.SelectClauseResults[m_projection.ConditionIdIndex],
                "The server rejected the real ConditionId select clause; SourceNode is not a safe substitute.");
        }
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        if (m_subscription is not { } subscription)
        {
            throw new InvalidOperationException("The alarm event source is not ready for ConditionRefresh.");
        }
        var readiness = Stopwatch.StartNew();
        while (!Health.IsReady)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_item is { } item && ServiceResult.IsBad(item.Error))
            {
                throw new ServiceResultException(item.Error);
            }
            if (readiness.Elapsed >= TimeSpan.FromSeconds(20))
            {
                throw new TimeoutException("The recreated alarm event source is not ready for ConditionRefresh.");
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        await subscription.ConditionRefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ArrayOf<AlarmOperation>> InspectAsync(
        AlarmCondition condition,
        CancellationToken cancellationToken)
    {
        return m_operations.InspectAsync(condition, cancellationToken);
    }

    public ValueTask ExecuteAsync(AlarmCommand command, CancellationToken cancellationToken)
    {
        if (!Health.IsReady)
        {
            throw new InvalidOperationException("The alarm event source is not connected.");
        }
        return m_operations.ExecuteAsync(command, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (m_disposeLock)
        {
            return new ValueTask(m_disposal ??= DisposeCoreAsync());
        }
    }

    public ValueTask OnDataChangeNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ReadOnlyMemory<DataValueChange> notification,
        PublishState publishStateMask,
        IReadOnlyList<string> stringTable)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnEventDataNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ReadOnlyMemory<EventNotification> notification,
        PublishState publishStateMask,
        IReadOnlyList<string> stringTable)
    {
        ObservePublishState(publishStateMask);
        foreach (EventNotification item in notification.Span)
        {
            Write(m_projection.Decode(item.Fields, item.PartitionServerId));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnKeepAliveNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        PublishState publishStateMask)
    {
        ObservePublishState(publishStateMask);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSubscriptionStateChangedAsync(
        ISubscription subscription,
        V2SubscriptionState state,
        PublishState publishStateMask,
        CancellationToken ct = default)
    {
        if (state == V2SubscriptionState.Created)
        {
            Interlocked.Exchange(ref m_unavailable, 0);
            if (Interlocked.Increment(ref m_createdCount) > 1)
            {
                Write(new AlarmUpdate(AlarmUpdateKind.Created,
                    Detail: "V2 subscription recreated; a retained-condition refresh is required."));
            }
        }
        if (state is V2SubscriptionState.Error or V2SubscriptionState.Deleted)
        {
            Interlocked.Exchange(ref m_unavailable, 1);
            Write(new AlarmUpdate(AlarmUpdateKind.Unavailable,
                Detail: $"V2 subscription {state}; retained branches are unreconciled."));
        }
        ObservePublishState(publishStateMask);
        return ValueTask.CompletedTask;
    }

    private void ObservePublishState(PublishState state)
    {
        if ((state & (PublishState.Stopped | PublishState.Timeout | PublishState.Completed)) != 0)
        {
            if (Interlocked.Exchange(ref m_unavailable, 1) == 0)
            {
                Write(new AlarmUpdate(AlarmUpdateKind.Unavailable,
                    Detail: "Publishing stopped; ManagedSession owns recovery. Condition state is incomplete."));
            }
        }
        else if ((state & (PublishState.Recovered | PublishState.Transferred)) != 0)
        {
            Interlocked.Exchange(ref m_unavailable, 0);
            Write(new AlarmUpdate(AlarmUpdateKind.Recovered,
                Detail: "Publishing recovered/transferred; refresh will reconcile retained branches."));
        }
        if ((state & PublishState.Republish) != 0)
        {
            Write(new AlarmUpdate(AlarmUpdateKind.Loss,
                Detail: "A publish sequence gap triggered republish. This is not proof of complete condition state."));
        }
    }

    private void Write(AlarmUpdate update)
    {
        if (Volatile.Read(ref m_disposed) == 0 && !m_updates.Writer.TryWrite(update))
        {
            Interlocked.Increment(ref m_droppedUpdates);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref m_disposed, 1);
        try
        {
            if (m_subscription is not null)
            {
                await m_subscription.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            m_subscription = null;
            m_item = null;
            m_updates.Writer.TryComplete();
        }
    }

    private readonly ISession m_session;
    private readonly ISubscriptionManager m_manager;
    private readonly AlarmCommandAdapter m_operations;
    private readonly AlarmEventProjection m_projection = new();
    private readonly Channel<AlarmUpdate> m_updates;
    private readonly Lock m_disposeLock = new();
    private ISubscription? m_subscription;
    private IMonitoredItem? m_item;
    private Task? m_disposal;
    private long m_droppedUpdates;
    private int m_unavailable;
    private int m_disposed;
    private int m_createdCount;
}
