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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task PreparedBatchPublicationStopsEveryImmediateDataSourceAsync(
            bool reverse, bool replace, bool immediate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager first = null;
            TrackingLifecycleNodeManager second = null;
            NodeManagerRegistration firstRegistration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, manager => first = manager),
                null, timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration secondRegistration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(202, manager => second = manager, kSecondModelNamespaceUri),
                null, timeout.Token).ConfigureAwait(false);
            var firstNode = (BaseVariableState)first.Find(new NodeId(kValueNodeId, first.NamespaceIndexes[0]));
            var secondNode = (BaseVariableState)second.Find(new NodeId(kValueNodeId, second.NamespaceIndexes[0]));
            firstNode.StatusCode = StatusCodes.Good;
            secondNode.StatusCode = StatusCodes.Good;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint firstItemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, firstNode.NodeId, 1).ConfigureAwait(false);
            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse created = await services.CreateMonitoredItemsAsync(
                header, subscriptionId, TimestampsToReturn.Both,
                [
                    CreateEmissionDataRequest(secondNode.NodeId, 2),
                    CreateEmissionDataRequest(secondNode.NodeId, 3)
                ], timeout.Token).ConfigureAwait(false);
            Assert.That(created.Results.Count, Is.EqualTo(2));
            foreach (MonitoredItemCreateResult result in created.Results)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            uint eventItemId = await CreateEventMonitoredItemAsync(
                services, subscriptionId, ObjectIds.Server, 4).ConfigureAwait(false);
            IEventMonitoredItem eventItem = server.EventManager.GetMonitoredItems()
                .Single(item => item.Id == eventItemId);
            MonitoredNode2 firstSource = first.GetEmissionSource(firstNode.NodeId);
            MonitoredNode2 secondSource = second.GetEmissionSource(secondNode.NodeId);
            IDataChangeMonitoredItem2 firstItem = firstSource.DataChangeMonitoredItems[firstItemId];
            IDataChangeMonitoredItem2[] secondItems = [.. secondSource.DataChangeMonitoredItems.Values];
            Assert.That(secondItems, Has.Length.EqualTo(2));
            var initial = await PublishEmissionDataAsync(services, subscriptionId, default, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(initial.Values.Values.Select(value => value.StatusCode),
                Is.All.EqualTo(StatusCodes.Good));
            Assert.That(initial.Values[1].WrappedValue, Is.EqualTo(new Variant(101)));
            Assert.That(initial.Values[2].WrappedValue, Is.EqualTo(new Variant(202)));
            Assert.That(initial.Values[3].WrappedValue, Is.EqualTo(new Variant(202)));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            first.ConditionRefreshCallback = async (_, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            NodeManagerBatchChange firstChange = replace
                ? NodeManagerBatchChange.Replace(firstRegistration,
                    CreateTrackingNodeManagementFactory(301, _ => { }), immediate)
                : NodeManagerBatchChange.Remove(firstRegistration, immediate);
            NodeManagerBatchChange secondChange = replace
                ? NodeManagerBatchChange.Replace(secondRegistration,
                    CreateTrackingNodeManagementFactory(302, _ => { }, kSecondModelNamespaceUri), immediate)
                : NodeManagerBatchChange.Remove(secondRegistration, immediate);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                reverse ? [secondChange, firstChange] : [firstChange, secondChange], timeout.Token)
                .ConfigureAwait(false);
            ISession session = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken);
            using var context = new OperationContext(session, DiagnosticsMasks.None);
            Task dispatch = master.ConditionRefreshAsync(context, [eventItem], timeout.Token).AsTask();
            Task<NodeManagerBatchResult> commit = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                commit = prepared.CommitAsync(_ => default, () => published.TrySetResult(true), timeout.Token)
                    .AsTask();
                await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(dispatch.IsCompleted, Is.False);
                Assert.That(first.DisposeCount, Is.Zero);
                Assert.That(second.DisposeCount, Is.Zero);

                DateTimeUtc producedAt = DateTimeUtc.Now;
                firstNode.Value = 501;
                firstNode.Timestamp = producedAt;
                secondNode.Value = 602;
                secondNode.Timestamp = producedAt;
                await firstSource.QueueValueAsync(server.DefaultSystemContext, firstNode, firstItem, timeout.Token)
                    .ConfigureAwait(false);
                foreach (IDataChangeMonitoredItem2 item in secondItems)
                {
                    await secondSource.QueueValueAsync(server.DefaultSystemContext, secondNode, item, timeout.Token)
                        .ConfigureAwait(false);
                }
                var delivered = await PublishEmissionDataAsync(
                    services, subscriptionId, initial.Acknowledgements, timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    foreach (uint handle in new uint[] { 1, 2, 3 })
                    {
                        DataValue value = delivered.Values[handle];
                        Assert.That(value.StatusCode,
                            Is.EqualTo(immediate ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good),
                            $"Source for client handle {handle} sampled after the joint publication while A was held.");
                        if (!immediate)
                        {
                            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(handle == 1 ? 501 : 602)));
                            Assert.That(value.SourceTimestamp, Is.EqualTo(producedAt));
                        }
                    }
                    Assert.That(dispatch.IsCompleted, Is.False);
                    Assert.That(first.AllEventsUnsubscribeCount, Is.Zero);
                    Assert.That(second.AllEventsUnsubscribeCount, Is.Zero);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (commit is not null)
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task PreparedBatchPublicationStopsEveryImmediateEventSourceAsync(
            bool reverse, bool replace, bool immediate)
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
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                header, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;
            NodeId secondRoot = new(kRootNodeId, second.NamespaceIndexes[0]);
            header.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse created = await services.CreateMonitoredItemsAsync(
                header, subscriptionId, TimestampsToReturn.Both,
                [
                    CreateEmissionEventRequest(ObjectIds.Server, 1),
                    CreateEmissionEventRequest(secondRoot, 2)
                ], timeout.Token).ConfigureAwait(false);
            foreach (MonitoredItemCreateResult result in created.Results)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            IEventMonitoredItem allEvents = server.EventManager.GetMonitoredItems()
                .Single(item => item.Id == created.Results[0].MonitoredItemId);
            IEventMonitoredItem localEvents = second.GetEmissionSource(secondRoot)
                .EventMonitoredItems[created.Results[1].MonitoredItemId];
            await first.ReportOwnedEmissionAsync("before-A", timeout.Token).ConfigureAwait(false);
            await second.ReportOwnedEmissionAsync("before-B", timeout.Token).ConfigureAwait(false);
            QueueLocal("before-local");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            first.ConditionRefreshCallback = async (_, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            NodeManagerBatchChange firstChange = replace
                ? NodeManagerBatchChange.Replace(firstRegistration,
                    CreateTrackingNodeManagementFactory(401, _ => { }), immediate)
                : NodeManagerBatchChange.Remove(firstRegistration, immediate);
            NodeManagerBatchChange secondChange = replace
                ? NodeManagerBatchChange.Replace(secondRegistration,
                    CreateTrackingNodeManagementFactory(402, _ => { }, kSecondModelNamespaceUri), immediate)
                : NodeManagerBatchChange.Remove(secondRegistration, immediate);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                reverse ? [secondChange, firstChange] : [firstChange, secondChange], timeout.Token)
                .ConfigureAwait(false);
            ISession session = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken);
            using var context = new OperationContext(session, DiagnosticsMasks.None);
            Task dispatch = master.ConditionRefreshAsync(context, [allEvents], timeout.Token).AsTask();
            Task<NodeManagerBatchResult> commit = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                commit = prepared.CommitAsync(_ => default, () => published.TrySetResult(true), timeout.Token)
                    .AsTask();
                await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await second.ReportOwnedEmissionAsync("after-B-602", timeout.Token).ConfigureAwait(false);
                await first.ReportOwnedEmissionAsync("after-A-501", timeout.Token).ConfigureAwait(false);
                QueueLocal("after-local-703");
                await survivor.ReportOwnedEmissionAsync("survivor-fence-804", timeout.Token).ConfigureAwait(false);

                var messages = new Dictionary<uint, List<string>> { [1] = [], [2] = [] };
                ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
                while (!messages[1].Contains("survivor-fence-804", StringComparer.Ordinal))
                {
                    header.Timestamp = DateTimeUtc.Now;
                    PublishResponse response = await services.PublishAsync(header, acknowledgements, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                    acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequence =>
                        new SubscriptionAcknowledgement { SubscriptionId = subscriptionId, SequenceNumber = sequence });
                    foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                    {
                        if (!notification.TryGetValue(out EventNotificationList events))
                        {
                            continue;
                        }
                        foreach (EventFieldList occurrence in events.Events)
                        {
                            Assert.That(occurrence.EventFields[1].TryGetValue(out LocalizedText message), Is.True);
                            messages[occurrence.ClientHandle].Add(message.Text);
                        }
                    }
                }
                string[] expectedGlobal = immediate
                    ? ["before-A", "before-B", "survivor-fence-804"]
                    : ["before-A", "before-B", "after-B-602", "after-A-501", "survivor-fence-804"];
                string[] expectedLocal = immediate ? ["before-local"] : ["before-local", "after-local-703"];
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(messages[1], Is.EqualTo(expectedGlobal));
                    Assert.That(messages[2], Is.EqualTo(expectedLocal));
                    Assert.That(dispatch.IsCompleted, Is.False);
                    Assert.That(first.AllEventsUnsubscribeCount, Is.Zero);
                    Assert.That(second.AllEventsUnsubscribeCount, Is.Zero);
                    Assert.That(server.EventManager.GetMonitoredItems()
                        .Single(item => item.Id == allEvents.Id), Is.SameAs(allEvents));
                }
            }
            finally
            {
                release.TrySetResult(true);
                await dispatch.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (commit is not null)
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            void QueueLocal(string message)
            {
                var occurrence = new BaseEventState(null);
                occurrence.Initialize(server.DefaultSystemContext, second.Find(secondRoot),
                    EventSeverity.Medium, new LocalizedText(message));
                localEvents.QueueEvent(occurrence);
            }
        }

        private static MonitoredItemCreateRequest CreateEmissionEventRequest(NodeId nodeId, uint clientHandle)
        {
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.EventNotifier },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    Filter = new ExtensionObject(filter),
                    SamplingInterval = 0,
                    QueueSize = 32,
                    DiscardOldest = true
                }
            };
        }

        private static MonitoredItemCreateRequest CreateEmissionDataRequest(NodeId nodeId, uint clientHandle)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = 0,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            };
        }

        private async Task<(
            Dictionary<uint, DataValue> Values,
            ArrayOf<SubscriptionAcknowledgement> Acknowledgements)> PublishEmissionDataAsync(
                ServerTestServices services,
                uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                CancellationToken cancellationToken)
        {
            var values = new Dictionary<uint, DataValue>();
            while (values.Count < 3)
            {
                RequestHeader header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                PublishResponse response = await services.PublishAsync(header, acknowledgements, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequence =>
                    new SubscriptionAcknowledgement { SubscriptionId = subscriptionId, SequenceNumber = sequence });
                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    if (!notification.TryGetValue(out DataChangeNotification data))
                    {
                        continue;
                    }
                    foreach (MonitoredItemNotification item in data.MonitoredItems)
                    {
                        Assert.That(item.Value.IsNull, Is.False);
                        values.Add(item.ClientHandle, item.Value);
                    }
                }
            }
            Assert.That(values.Keys, Is.EquivalentTo(new uint[] { 1, 2, 3 }));
            return (values, acknowledgements);
        }

        private sealed partial class TrackingLifecycleNodeManager
        {
            public MonitoredNode2 GetEmissionSource(NodeId nodeId)
            {
                return m_monitoredItemManager.MonitoredNodes[nodeId];
            }

            public ValueTask ReportOwnedEmissionAsync(string message, CancellationToken cancellationToken)
            {
                NodeState root = Find(new NodeId(kRootNodeId, NamespaceIndexes[0]));
                var occurrence = new BaseEventState(null);
                occurrence.Initialize(SystemContext, root, EventSeverity.Medium, new LocalizedText(message));
                return OnReportEventAsync(SystemContext, root, occurrence, cancellationToken);
            }
        }
    }
}
