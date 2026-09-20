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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiredItemsKeepNativeCoreServiceResultsAsync(bool events, bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager original = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(101, manager => original = manager),
                null, timeout.Token).ConfigureAwait(false);
            NodeId nodeId = new(events ? kRootNodeId : kValueNodeId, original.NamespaceIndexes[0]);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;
            MonitoredItemCreateRequest request = events
                ? CreateEmissionEventRequest(nodeId, 1)
                : CreateEmissionDataRequest(nodeId, 1);
            CreateMonitoredItemsResponse created = await session.CreateMonitoredItemsAsync(
                null, subscriptionId, TimestampsToReturn.Both, [request], timeout.Token).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            uint itemId = created.Results[0].MonitoredItemId;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    replace
                        ? NodeManagerBatchChange.Replace(registration,
                            CreateTrackingNodeManagementFactory(202, _ => { }), true)
                        : NodeManagerBatchChange.Remove(registration, true)
                ], timeout.Token).ConfigureAwait(false);
            try
            {
                NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(committed.CleanupFailure, Is.Null);
                Assert.That(original.DisposeCount, Is.EqualTo(1));
                original.ValidateNodeCallback = (_, _) =>
                    throw new InvalidOperationException("A survivor service re-entered its retired source.");
                request.RequestedParameters.ClientHandle = 81;
                request.RequestedParameters.QueueSize = 4;
                ModifyMonitoredItemsResponse modified = await session.ModifyMonitoredItemsAsync(
                    null, subscriptionId, TimestampsToReturn.Both,
                    [Modify(itemId, request.RequestedParameters)], timeout.Token).ConfigureAwait(false);
                Assert.That(modified.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(modified.Results[0].RevisedQueueSize, Is.EqualTo(4));

                var invalidFilter = new MonitoringParameters
                {
                    ClientHandle = 999,
                    SamplingInterval = 0,
                    QueueSize = 4,
                    Filter = new ExtensionObject(events ? new DataChangeFilter() : new EventFilter())
                };
                ModifyMonitoredItemsResponse mixed = await session.ModifyMonitoredItemsAsync(
                    null, subscriptionId, TimestampsToReturn.Both,
                    [
                        Modify(itemId, invalidFilter),
                        Modify(uint.MaxValue, request.RequestedParameters),
                        Modify(itemId, request.RequestedParameters)
                    ], timeout.Token).ConfigureAwait(false);
                Assert.That(mixed.Results.Count, Is.EqualTo(3));
                Assert.That(mixed.Results[0].StatusCode,
                    Is.EqualTo(events ? StatusCodes.BadEventFilterInvalid : StatusCodes.BadFilterNotAllowed));
                Assert.That(mixed.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                Assert.That(mixed.Results[2].StatusCode, Is.EqualTo(StatusCodes.Good));

                await Assert.ThatAsync(async () => await session.ModifyMonitoredItemsAsync(
                    null, uint.MaxValue, TimestampsToReturn.Both,
                    [Modify(itemId, request.RequestedParameters)], timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadSubscriptionIdInvalid)).ConfigureAwait(false);
                await Assert.ThatAsync(async () => await session.SetMonitoringModeAsync(
                    null, subscriptionId, (MonitoringMode)99, [itemId], timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadMonitoringModeInvalid)).ConfigureAwait(false);
                await Assert.ThatAsync(async () => await session.SetMonitoringModeAsync(
                    null, uint.MaxValue, MonitoringMode.Reporting, [itemId], timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadSubscriptionIdInvalid)).ConfigureAwait(false);

                SetMonitoringModeResponse disabled = await session.SetMonitoringModeAsync(
                    null, subscriptionId, MonitoringMode.Disabled, [itemId, uint.MaxValue], timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(disabled.Results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(disabled.Results[1], Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                SetMonitoringModeResponse reporting = await session.SetMonitoringModeAsync(
                    null, subscriptionId, MonitoringMode.Reporting, [itemId], timeout.Token).ConfigureAwait(false);
                Assert.That(reporting.Results[0], Is.EqualTo(StatusCodes.Good));
                ISubscription owner = server.SubscriptionManager.GetSubscriptions()
                    .Single(value => value.Id == subscriptionId);
                owner.GetMonitoredItems(out ArrayOf<uint> serverHandles, out ArrayOf<uint> clientHandles);
                Assert.That(serverHandles.Count, Is.EqualTo(1));
                Assert.That(serverHandles[0], Is.EqualTo(itemId));
                Assert.That(clientHandles.Count, Is.EqualTo(1));
                Assert.That(clientHandles[0], Is.EqualTo(81));
                if (!events)
                {
                    PublishResponse published = await session.PublishAsync(null, [], timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(published.NotificationMessage.NotificationData[0]
                        .TryGetValue(out DataChangeNotification data), Is.True);
                    Assert.That(data.MonitoredItems[0].ClientHandle, Is.EqualTo(81));
                    Assert.That(data.MonitoredItems[0].Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                }
                await Assert.ThatAsync(async () => await session.DeleteMonitoredItemsAsync(
                    null, uint.MaxValue, [itemId], timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadSubscriptionIdInvalid)).ConfigureAwait(false);
                DeleteMonitoredItemsResponse deleted = await session.DeleteMonitoredItemsAsync(
                    null, subscriptionId, [itemId, uint.MaxValue], timeout.Token).ConfigureAwait(false);
                Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(deleted.Results[1], Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                DeleteMonitoredItemsResponse repeated = await session.DeleteMonitoredItemsAsync(
                    null, subscriptionId, [itemId], timeout.Token).ConfigureAwait(false);
                Assert.That(repeated.Results[0], Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                Assert.That(owner.MonitoredItemCount, Is.Zero);
                Assert.That(original.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await session.DeleteSubscriptionsAsync(null, [subscriptionId], timeout.Token).ConfigureAwait(false);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }

            static MonitoredItemModifyRequest Modify(uint id, MonitoringParameters parameters)
            {
                return new MonitoredItemModifyRequest { MonitoredItemId = id, RequestedParameters = parameters };
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task DetachedDataFiltersUseSourceCapabilitySnapshotAsync(bool text, bool range)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateNodeManagementFactory(101, range), null, timeout.Token).ConfigureAwait(false);
            var manager = (AsyncCustomNodeManager)registration.NodeManager;
            NodeId valueId = new(kValueNodeId, manager.NamespaceIndexes[0]);
            var variable = (BaseVariableState)manager.Find(valueId);
            variable.StatusCode = StatusCodes.Good;
            if (text)
            {
                variable.DataType = DataTypeIds.String;
                variable.Value = "source-value";
            }
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            try
            {
                await CheckFiltersAsync().ConfigureAwait(false);
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(registration, true)], timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult committed = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(committed.CleanupFailure, Is.Null);
                Assert.That(manager.Find(valueId), Is.Null);
                await CheckFiltersAsync().ConfigureAwait(false);
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            async Task CheckFiltersAsync()
            {
                DeadbandType[] deadbands = [DeadbandType.None, DeadbandType.Absolute, DeadbandType.Percent];
                foreach (DeadbandType deadband in deadbands)
                {
                    RequestHeader header = m_requestHeader;
                    header.Timestamp = DateTimeUtc.Now;
                    ModifyMonitoredItemsResponse response = await services.ModifyMonitoredItemsAsync(
                        header, subscriptionId, TimestampsToReturn.Both,
                        [
                            new MonitoredItemModifyRequest
                            {
                                MonitoredItemId = itemId,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 0,
                                    QueueSize = 4,
                                    Filter = new ExtensionObject(new DataChangeFilter
                                    {
                                        Trigger = DataChangeTrigger.StatusValue,
                                        DeadbandType = (uint)deadband,
                                        DeadbandValue = 10
                                    })
                                }
                            }
                        ], timeout.Token).ConfigureAwait(false);
                    StatusCode expected = deadband == DeadbandType.None
                        ? StatusCodes.Good
                        : text
                            ? StatusCodes.BadFilterNotAllowed
                            : deadband == DeadbandType.Percent && !range
                                ? StatusCodes.BadMonitoredItemFilterUnsupported
                                : StatusCodes.Good;
                    Assert.That(response.Results[0].StatusCode, Is.EqualTo(expected), $"Filter: {deadband}.");
                }
            }
        }
    }
}
