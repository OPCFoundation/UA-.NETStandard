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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedCutoffPreservesAuthorizedSourceSnapshotsAndCoreIdentityAsync(bool events)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager first = null;
            TrackingLifecycleNodeManager second = null;
            TrackingLifecycleNodeManager survivor = null;
            NodeManagerRegistration firstRegistration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, manager => first = manager),
                null, timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration secondRegistration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(202, manager => second = manager, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                303, manager => survivor = manager, kReadinessProbeNamespaceUri), null, timeout.Token)
                .ConfigureAwait(false);
            NodeId sourceId = new(events ? kRootNodeId : kValueNodeId, second.NamespaceIndexes[0]);
            NodeState node = second.Find(sourceId);
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
                var initial = await PublishForDataChangeAsync(services, subscriptionId, default, 1)
                    .ConfigureAwait(false);
                Assert.That(initial.Value.GetValueOrDefault().WrappedValue, Is.EqualTo(new Variant(202)));
                acknowledgements = initial.Acknowledgements;
            }
            MonitoredNode2 source = second.GetEmissionSource(sourceId);
            var validating = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseValidation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDispatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int validationClaims = 0;
            second.EmissionPermissionCallback = async (permission, token) =>
            {
                if (permission == (events ? PermissionType.ReceiveEvents : PermissionType.Read) &&
                    Interlocked.CompareExchange(ref validationClaims, 1, 0) == 0)
                {
                    validating.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(token).ConfigureAwait(false);
                }
            };
            first.ConditionRefreshCallback = async (_, token) =>
            {
                entered.TrySetResult(true);
                await releaseDispatch.Task.WaitAsync(token).ConfigureAwait(false);
            };
            Task dispatch = null;
            Task<NodeManagerBatchResult> commit = null;
            try
            {
                await ProduceAsync(771).ConfigureAwait(false);
                await validating.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await ProduceAsync(772).ConfigureAwait(false);
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        NodeManagerBatchChange.Remove(firstRegistration, true),
                        NodeManagerBatchChange.Remove(secondRegistration, true)
                    ], timeout.Token).ConfigureAwait(false);
                ISession session = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken);
                using var context = new OperationContext(session, DiagnosticsMasks.None);
                dispatch = master.ConditionRefreshAsync(context, [allEvents], timeout.Token).AsTask();
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                commit = prepared.CommitAsync(_ => default, () => published.TrySetResult(true), timeout.Token)
                    .AsTask();
                await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await ProduceAsync(999).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(dispatch.IsCompleted, Is.False);
                Assert.That(second.DisposeCount, Is.Zero);
                releaseDispatch.TrySetResult(true);
                await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(second.DisposeCount, Is.Zero,
                    "The queued source snapshots, not just A's callback, must retain B.");
                releaseValidation.TrySetResult(true);
                NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(second.DisposeCount, Is.EqualTo(1));
                await survivor.ReportOwnedEmissionAsync("source-drain-fence", timeout.Token).ConfigureAwait(false);
                var values = new List<DataValue>();
                var messages = new List<string>();
                bool fenced = false;
                NotificationMessage sent = null;
                while (!fenced)
                {
                    header.Timestamp = DateTimeUtc.Now;
                    PublishResponse response = await services.PublishAsync(header, acknowledgements, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                    acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequence =>
                        new SubscriptionAcknowledgement { SubscriptionId = subscriptionId, SequenceNumber = sequence });
                    sent = response.NotificationMessage;
                    foreach (ExtensionObject notification in sent.NotificationData)
                    {
                        if (notification.TryGetValue(out DataChangeNotification data))
                        {
                            foreach (MonitoredItemNotification item in data.MonitoredItems)
                            {
                                Assert.That(item.ClientHandle, Is.EqualTo(1));
                                values.Add(item.Value);
                            }
                        }
                        else if (notification.TryGetValue(out EventNotificationList occurrences))
                        {
                            foreach (EventFieldList occurrence in occurrences.Events)
                            {
                                Assert.That(occurrence.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                                if (occurrence.ClientHandle == 1)
                                {
                                    messages.Add(message.Text);
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
                        .Select(value => value.WrappedValue),
                        Is.EqualTo(new[] { new Variant(771), new Variant(772) }));
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
                    header, subscriptionId, MonitoringMode.Reporting, [itemId], timeout.Token).ConfigureAwait(false);
                Assert.That(reporting.Results[0], Is.EqualTo(StatusCodes.Good));
                header.Timestamp = DateTimeUtc.Now;
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

        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedPublicationKeepsNewDataAndBusinessEventsUsableAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var decision = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager manager = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, value => manager = value), null, timeout.Token)
                .ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId, manager.NamespaceIndexes[0]);
            var variable = (BaseVariableState)manager.Find(valueId);
            variable.StatusCode = StatusCodes.Good;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            var initial = await PublishForDataChangeAsync(services, subscriptionId, default, 1).ConfigureAwait(false);
            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse eventCreated = await services.CreateMonitoredItemsAsync(
                header, subscriptionId, TimestampsToReturn.Both,
                [CreateEmissionEventRequest(ObjectIds.Server, 2)], timeout.Token).ConfigureAwait(false);
            Assert.That(eventCreated.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            MonitoredNode2 source = manager.GetEmissionSource(valueId);
            IDataChangeMonitoredItem2 item = source.DataChangeMonitoredItems[itemId];
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(registration, true)], timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new IOException("The source cutoff was not durably decided.");
            }, decision.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await AssertServingAsync(551).ConfigureAwait(false);
                if (cancel)
                {
                    decision.Cancel();
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
                Assert.That(lifecycle.Registrations.Count, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations[0], Is.SameAs(registration));
            }
            finally
            {
                release.TrySetResult(true);
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

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
                var business = await PublishForModelChangeEventAsync(
                    services, subscriptionId, initial.Acknowledgements).ConfigureAwait(false);
                Assert.That(business.EventFields.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo($"usable-{value}"));
                initial = (initial.Value, business.Acknowledgements);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedSourceSamplingReleasesAdmissionBeforeImmediateRetirementAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var failureCancellation = new CancellationTokenSource();
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager manager = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, value => manager = value), null, timeout.Token)
                .ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId, manager.NamespaceIndexes[0]);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            MonitoredNode2 source = manager.GetEmissionSource(valueId);
            var variable = (BaseVariableState)manager.Find(valueId);
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
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(registration, true)], timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Retired, Is.EqualTo(1u));
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
            public Func<PermissionType, CancellationToken, ValueTask> EmissionPermissionCallback { get; set; }

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
