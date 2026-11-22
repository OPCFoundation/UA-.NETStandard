// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Live lifecycle integration tests for <see cref="INodeManagerLifecycle"/> and its
    /// <see cref="NodeManagerRegistration"/> handles against a real, running
    /// <see cref="ReferenceServer"/>, focused on registration identity/copy semantics,
    /// monitored-item transitions, and rollback paths for Add/Reload/Remove.
    /// </summary>
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Verifies that prepared cutoff preserves authorized source snapshots and core identity.
        /// </summary>
        /// <param name="events">The events flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedCutoffPreservesAuthorizedSourceSnapshotsAndCoreIdentityAsync(bool events)
        {
            Task exactWriter1Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                TrackingLifecycleNodeManager? first = null;
                TrackingLifecycleNodeManager? second = null;
                TrackingLifecycleNodeManager? survivor = null;
                LifecycleResult publicationResult1 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(
                    CreateTrackingNodeManagementFactory(
                    101, manager => first = manager)), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration firstRegistration = RequireRegistration(publicationResult1.Registrations[0]);
                LifecycleResult publicationResult2 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(
                    CreateTrackingNodeManagementFactory(
                    202, manager => second = manager, kSecondModelNamespaceUri)), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration secondRegistration = RequireRegistration(publicationResult2.Registrations[0]);
                RequireRegistration(
                    (await CommitChangeAsync(lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    303, manager => survivor = manager, kReadinessProbeNamespaceUri)), timeout.Token)
                        .ConfigureAwait(false)).Registrations[0]);
                AssertLifecycleValue(first);
                AssertLifecycleValue(second);
                AssertLifecycleValue(survivor);
                NodeId sourceId = new(events ? kRootNodeId : kValueNodeId, second.NamespaceIndexes[0]);
                NodeState node = RequireLifecycleValue(second.Find(sourceId));
                if (node is BaseVariableState variable)
                {
                    variable.StatusCode = StatusCodes.Good;
                }
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                RequestHeader header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                    header, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
                uint subscriptionId = subscription.SubscriptionId;
                MonitoredItemCreateRequest request = events
                    ? CreateEmissionEventRequest(sourceId, 1)
                    : CreateEmissionDataRequest(sourceId, 1);
                request.RequestedParameters.QueueSize = 32;
                header.Timestamp = DateTimeUtc.Now;
                CreateMonitoredItemsResponse created = await services.CreateMonitoredItemsAsync(
                    header, subscriptionId, TimestampsToReturn.Both,
                    [request, CreateEmissionEventRequest(ObjectIds.Server, 2)], timeout.Token).ConfigureAwait(false);
                foreach (MonitoredItemCreateResult result in created.Results)
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                uint itemId = created.Results[0].MonitoredItemId;
                IEventMonitoredItem allEvents = server.EventManager.GetMonitoredItems()
                    .Single(item => item.Id == created.Results[1].MonitoredItemId);
                ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
                if (!events)
                {
                    (DataValue? initialValue, ArrayOf<SubscriptionAcknowledgement> initialAcknowledgements) =
                        await PublishForDataChangeAsync(services, subscriptionId, default, 1).ConfigureAwait(false);
                    Assert.That(initialValue.GetValueOrDefault().WrappedValue, Is.EqualTo(new Variant(202)));
                    acknowledgements = initialAcknowledgements;
                }
                MonitoredNode2 source = second.GetEmissionSource(sourceId);
                var validating = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseValidation = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var validatingQueuedSource = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseQueuedSource = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseDispatch = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int validationClaims = 0;
                second.EmissionPermissionCallback = async (permission, token) =>
                {
                    if (permission != (events ? PermissionType.ReceiveEvents : PermissionType.Read))
                    {
                        return;
                    }
                    int claim = Interlocked.Increment(ref validationClaims);
                    if (claim == 1)
                    {
                        validating.TrySetResult(true);
                        await releaseValidation.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                    else if (claim == 2)
                    {
                        validatingQueuedSource.TrySetResult(true);
                        await releaseQueuedSource.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                };
                first.ConditionRefreshCallback = async (_, token) =>
                {
                    entered.TrySetResult(true);
                    await releaseDispatch.Task.WaitAsync(token).ConfigureAwait(false);
                };
                Task? dispatch = null;
                Task<LifecycleResult>? commit = null;
                LifecycleResult? retirementResult = null;
                try
                {
                    await ProduceAsync(771).ConfigureAwait(false);
                    await validating.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await ProduceAsync(772).ConfigureAwait(false);
                    await using ILifecycleWriter exactWriter1 = await Capture(
                        m_server.NodeManagerLifecycle, firstRegistration, secondRegistration).BeginAsync(
                        timeout.Token).ConfigureAwait(false);
                    await using IPreparedChanges prepared = await exactWriter1.PrepareAsync([
                            LifecycleChange.Remove(firstRegistration.Slot),
                            LifecycleChange.Remove(secondRegistration.Slot)
                        ], timeout.Token).ConfigureAwait(false);
                    ISession session = RequireLifecycleValue(
                        server.SessionManager.GetSession(m_requestHeader.AuthenticationToken));
                    using (var context = new OperationContext(session, DiagnosticsMasks.None))
                    {
                        dispatch = master.ConditionRefreshAsync(context, [allEvents], timeout.Token).AsTask();
                        await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        commit = prepared.CommitAsync(_ => default, () => published.TrySetResult(true), timeout.Token)
                            .AsTask();
                        exactWriter1Disposal = exactWriter1.DisposeAsync().AsTask();
                        await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Assert.That(prepared.IsCommitted, Is.True);
                        Assert.That(dispatch.IsCompleted, Is.False);
                        Assert.That(second.DisposeCount, Is.Zero);
                        releaseDispatch.TrySetResult(true);
                        releaseValidation.TrySetResult(true);
                        await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                        await validatingQueuedSource.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        await ProduceAsync(999).ConfigureAwait(false);
                        Assert.That(second.DisposeCount, Is.Zero,
                            "The queued source snapshots, not just A's callback, must retain B.");
                        releaseQueuedSource.TrySetResult(true);
                    }
                    LifecycleResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    retirementResult = result;
                    Assert.That(result.CleanupFailure, Is.Null);
                    await survivor.ReportOwnedEmissionAsync("source-drain-fence", timeout.Token).ConfigureAwait(false);
                    var values = new List<DataValue>();
                    var messages = new List<string>();
                    bool fenced = false;
                    NotificationMessage? sent = null;
                    while (!fenced)
                    {
                        header.Timestamp = DateTimeUtc.Now;
                        PublishResponse response = await services.PublishAsync(header, acknowledgements, timeout.Token)
                            .ConfigureAwait(false);
                        Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                        acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequence =>
                            new SubscriptionAcknowledgement
                            {
                                SubscriptionId = subscriptionId,
                                SequenceNumber = sequence
                            });
                        sent = response.NotificationMessage;
                        foreach (ExtensionObject notification in sent.NotificationData)
                        {
                            if (notification.TryGetValue(out DataChangeNotification? data))
                            {
                                foreach (MonitoredItemNotification item in data.MonitoredItems)
                                {
                                    Assert.That(item.ClientHandle, Is.EqualTo(1));
                                    values.Add(item.Value);
                                }
                            }
                            else if (notification.TryGetValue(out EventNotificationList? occurrences))
                            {
                                foreach (EventFieldList occurrence in occurrences.Events)
                                {
                                    Assert.That(
                                        occurrence.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                                    if (occurrence.ClientHandle == 1)
                                    {
                                        messages.Add(RequireLifecycleValue(message.Text));
                                    }
                                    else if (message.Text == "source-drain-fence")
                                    {
                                        fenced = true;
                                    }
                                }
                            }
                        }
                    }
                    if (events)
                    {
                        string[] expectedMessages = ["captured-771", "captured-772"];
                        Assert.That(messages, Is.EqualTo(expectedMessages));
                        Assert.That(values, Is.Empty);
                    }
                    else
                    {
                        Assert.That(values.Where(value => StatusCode.IsGood(value.StatusCode))
                            .Select(value => value.WrappedValue)
                            .ToArray(),
                            Is.EqualTo<Variant[]>([new Variant(771), new Variant(772)]));
                        Assert.That(values[^1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                        Assert.That(messages, Is.Empty);
                    }

                    header.Timestamp = DateTimeUtc.Now;
                    SetMonitoringModeResponse disabled = await services.SetMonitoringModeAsync(
                        header, subscriptionId, MonitoringMode.Disabled, [itemId], timeout.Token).ConfigureAwait(false);
                    Assert.That(disabled.Results[0], Is.EqualTo(StatusCodes.Good));
                    header.Timestamp = DateTimeUtc.Now;
                    ModifyMonitoredItemsResponse modified = await services.ModifyMonitoredItemsAsync(
                        header, subscriptionId, TimestampsToReturn.Both,
                        [
                            new MonitoredItemModifyRequest
                            {
                                MonitoredItemId = itemId,
                                RequestedParameters = request.RequestedParameters
                            }
                        ], timeout.Token).ConfigureAwait(false);
                    Assert.That(modified.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    header.Timestamp = DateTimeUtc.Now;
                    SetMonitoringModeResponse reporting = await services.SetMonitoringModeAsync(
                        header, subscriptionId, MonitoringMode.Reporting, [itemId], timeout.Token)
                            .ConfigureAwait(false);
                    Assert.That(reporting.Results[0], Is.EqualTo(StatusCodes.Good));
                    header.Timestamp = DateTimeUtc.Now;
                    AssertLifecycleValue(sent);
                    RepublishResponse replay = await services.RepublishAsync(
                        header, subscriptionId, sent.SequenceNumber, timeout.Token).ConfigureAwait(false);
                    Assert.That(replay.NotificationMessage.IsEqual(sent), Is.True,
                        "Source retirement and valid item mutations must not retract an already sent message.");
                    await DeleteMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
                    header.Timestamp = DateTimeUtc.Now;
                    DeleteMonitoredItemsResponse repeated = await services.DeleteMonitoredItemsAsync(
                        header, subscriptionId, [itemId], timeout.Token).ConfigureAwait(false);
                    Assert.That(repeated.Results[0], Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                }
                finally
                {
                    releaseDispatch.TrySetResult(true);
                    releaseValidation.TrySetResult(true);
                    releaseQueuedSource.TrySetResult(true);
                    if (dispatch is not null)
                    {
                        await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (commit is not null)
                    {
                        await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                }
                await RequireLifecycleValue(retirementResult).Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(second.DisposeCount, Is.EqualTo(1));

                async ValueTask ProduceAsync(int value)
                {
                    if (events)
                    {
                        var occurrence = new BaseEventState(null);
                        occurrence.Initialize(server.DefaultSystemContext, node, EventSeverity.Medium,
                            new LocalizedText($"captured-{value}"));
                        await source.OnReportEventAsync(server.DefaultSystemContext, node, occurrence, timeout.Token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        var variable = (BaseVariableState)node;
                        variable.Value = value;
                        variable.Timestamp = DateTimeUtc.Now;
                        await source.OnMonitoredNodeChangedAsync(server.DefaultSystemContext, node,
                            NodeStateChangeMasks.Value | NodeStateChangeMasks.RolePermissions, timeout.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                await exactWriter1Disposal.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that rejected publication keeps new data and business events usable.
        /// </summary>
        /// <param name="cancel">The cancel flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedPublicationKeepsNewDataAndBusinessEventsUsableAsync(bool cancel)
        {
            Task exactWriter2Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var decision = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                IServerInternal server = m_server.CurrentInstance;
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                TrackingLifecycleNodeManager? manager = null;
                LifecycleResult publicationResult3 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(
                    CreateTrackingNodeManagementFactory(
                    101, value => manager = value)), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration registration = RequireRegistration(publicationResult3.Registrations[0]);
                AssertLifecycleValue(manager);
                NodeId valueId = new(kValueNodeId, manager.NamespaceIndexes[0]);
                BaseVariableState variable = RequireLifecycleValue(manager.Find(valueId) as BaseVariableState);
                variable.StatusCode = StatusCodes.Good;
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                    services, valueId, 1).ConfigureAwait(false);
                (DataValue? Value, ArrayOf<SubscriptionAcknowledgement> Acknowledgements) initial =
                    await PublishForDataChangeAsync(services, subscriptionId, default, 1).ConfigureAwait(false);
                RequestHeader header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                CreateMonitoredItemsResponse eventCreated = await services.CreateMonitoredItemsAsync(
                    header, subscriptionId, TimestampsToReturn.Both,
                    [CreateEmissionEventRequest(ObjectIds.Server, 2)], timeout.Token).ConfigureAwait(false);
                Assert.That(eventCreated.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                MonitoredNode2 source = manager.GetEmissionSource(valueId);
                IDataChangeMonitoredItem2 item = source.DataChangeMonitoredItems[itemId];
                await using ILifecycleWriter exactWriter2 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter2.PrepareAsync(
                    [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    throw new IOException("The source cutoff was not durably decided.");
                }, decision.Token).AsTask();
                exactWriter2Disposal = exactWriter2.DisposeAsync().AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await AssertServingAsync(551).ConfigureAwait(false);
                    if (cancel)
                    {
                        await decision.CancelAsync().ConfigureAwait(false);
                        await Assert.ThatAsync(() => commit, Throws.InstanceOf<OperationCanceledException>())
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        release.TrySetResult(true);
                        await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                    }
                    await prepared.DisposeAsync().ConfigureAwait(false);
                    await AssertServingAsync(552).ConfigureAwait(false);
                    Assert.That(prepared.IsCommitted, Is.False);
                    Assert.That(manager.DisposeCount, Is.Zero);
                    Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(1));
                    Assert.That(GetBranchRegistration(lifecycle, registration.Id), Is.SameAs(registration));
                }
                finally
                {
                    release.TrySetResult(true);
                    await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                }

                await using ILifecycleWriter exactWriter3 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges finalRemoval = await exactWriter3.PrepareAsync(
                    [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                LifecycleResult finalResult = await finalRemoval
                    .CommitAsync(static _ => default, timeout.Token).ConfigureAwait(false);
                await finalResult.Retired.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                Assert.That(variable.OnStateChangedAsync, Is.Null);

                async Task AssertServingAsync(int value)
                {
                    variable.Value = value;
                    variable.Timestamp = DateTimeUtc.Now;
                    await source.QueueValueAsync(server.DefaultSystemContext, variable, item, timeout.Token)
                        .ConfigureAwait(false);
                    initial = await PublishForDataChangeAsync(
                        services, subscriptionId, initial.Acknowledgements, 1).ConfigureAwait(false);
                    Assert.That(initial.Value.GetValueOrDefault().StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(initial.Value.GetValueOrDefault().WrappedValue, Is.EqualTo(new Variant(value)));
                    await manager.ReportOwnedEmissionAsync($"usable-{value}", timeout.Token).ConfigureAwait(false);
                    (EventFieldList? eventFields, ArrayOf<SubscriptionAcknowledgement> businessAcknowledgements) =
                        await PublishForModelChangeEventAsync(
                            services, subscriptionId, initial.Acknowledgements).ConfigureAwait(false);
                    EventFieldList businessFields = RequireLifecycleEvent(eventFields);
                    Assert.That(businessFields.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                    Assert.That(message.Text, Is.EqualTo($"usable-{value}"));
                    initial = (initial.Value, businessAcknowledgements);
                }
            }
            finally
            {
                await exactWriter2Disposal.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that failed source sampling releases admission before immediate retirement.
        /// </summary>
        /// <param name="cancel">The cancel flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedSourceSamplingReleasesAdmissionBeforeImmediateRetirementAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var failureCancellation = new CancellationTokenSource();
            IServerInternal server = m_server.CurrentInstance;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? manager = null;
            LifecycleResult publicationResult4 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                101, value => manager = value)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult4.Registrations[0]);
            AssertLifecycleValue(manager);
            NodeId valueId = new(kValueNodeId, manager.NamespaceIndexes[0]);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            MonitoredNode2 source = manager.GetEmissionSource(valueId);
            BaseVariableState variable = RequireLifecycleValue(manager.Find(valueId) as BaseVariableState);
            int failedReads = 0;
            variable.OnReadValueAsync = (_, _, _, _, _) =>
            {
                Interlocked.Increment(ref failedReads);
                if (cancel)
                {
                    failureCancellation.Cancel();
                    throw new OperationCanceledException(failureCancellation.Token);
                }
                throw new IOException("The device sample could not be captured.");
            };
            try
            {
                await source.QueueValueAsync(
                    server.DefaultSystemContext, variable, source.DataChangeMonitoredItems[itemId], timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(failedReads, Is.EqualTo(1));
                var failed = await PublishForDataChangeAsync(services, subscriptionId, default, 1)
                    .ConfigureAwait(false);
                Assert.That(failed.Value.GetValueOrDefault().StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
                await source.OnMonitoredNodeChangedAsync(
                    server.DefaultSystemContext, variable, NodeStateChangeMasks.Value, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(failedReads, Is.EqualTo(2));
                variable.OnReadValueAsync = null;
                failureCancellation.Cancel();
                var occurrence = new BaseEventState(null);
                occurrence.Initialize(server.DefaultSystemContext, variable, EventSeverity.Medium,
                    new LocalizedText("cancelled-before-enqueue"));
                await Assert.ThatAsync(async () => await source.OnReportEventAsync(
                    server.DefaultSystemContext, variable, occurrence, failureCancellation.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                await using ILifecycleWriter exactWriter4 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter4.PrepareAsync(
                    [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                await exactWriter4.DisposeAsync().ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                // Retirement completes off the commit path once no admitted emission still holds the generation.
                await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.Retired.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                variable.OnReadValueAsync = null;
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        private sealed partial class TrackingLifecycleNodeManager
        {
            /// <summary>
            /// Gets or sets the emission permission callback for <c>TrackingLifecycleNodeManager</c>.
            /// </summary>
            public Func<PermissionType, CancellationToken, ValueTask>? EmissionPermissionCallback { get; set; }

            /// <inheritdoc/>
            /// <param name="operationContext">The operation context.</param>
            /// <param name="nodeId">The node identifier.</param>
            /// <param name="requestedPermission">The requested permission.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public override async ValueTask<ServiceResult> ValidateRolePermissionsAsync(
                OperationContext operationContext,
                NodeId nodeId,
                PermissionType requestedPermission,
                CancellationToken cancellationToken = default)
            {
                if (EmissionPermissionCallback is not null)
                {
                    await EmissionPermissionCallback(requestedPermission, cancellationToken).ConfigureAwait(false);
                }
                return await base.ValidateRolePermissionsAsync(
                    operationContext, nodeId, requestedPermission, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
