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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the validation done by the monitored item services
    /// (CreateMonitoredItems, ModifyMonitoredItems, SetMonitoringMode, DeleteMonitoredItems)
    /// on a running reference server.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MonitoredItemServiceValidationRegressionTests
    {
        private const int kMaxMonitoredItemsPerSubscription = 5;
        private const int kMaxMonitoredItemCount = 8;

        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private ServerTestServices m_services;
        private ushort m_namespaceIndex;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AllNodeManagers = true,
                DurableSubscriptionsEnabled = false,
                UseSamplingGroupsInReferenceNodeManager = false,
                AutoAccept = true
            };
            await m_fixture.LoadConfigurationAsync().ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.MaxMonitoredItemsPerSubscription =
                kMaxMonitoredItemsPerSubscription;
            m_fixture.Config.ServerConfiguration.MaxMonitoredItemCount = kMaxMonitoredItemCount;
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_namespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            (m_requestHeader, m_secureChannelContext) = await m_server.CreateAndActivateSessionAsync(
                TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            m_requestHeader.TimeoutHint = 10000;
            m_services = new ServerTestServices(m_server, m_secureChannelContext);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// M7-2: items beyond MaxMonitoredItemsPerSubscription are rejected one by one with
        /// Bad_TooManyMonitoredItems (Part 4 §5.13.2.4); the others are created.
        /// </summary>
        [Test]
        public async Task CreateMonitoredItemsBeyondPerSubscriptionLimitReturnsBadTooManyMonitoredItemsAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse response = await CreateValueItemsAsync(
                    subscriptionId, kMaxMonitoredItemsPerSubscription + 2).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(kMaxMonitoredItemsPerSubscription + 2));
                for (int ii = 0; ii < kMaxMonitoredItemsPerSubscription; ii++)
                {
                    Assert.That(response.Results[ii].StatusCode, Is.EqualTo(StatusCodes.Good), $"item {ii}");
                }
                for (int ii = kMaxMonitoredItemsPerSubscription; ii < response.Results.Count; ii++)
                {
                    Assert.That(
                        response.Results[ii].StatusCode,
                        Is.EqualTo(StatusCodes.BadTooManyMonitoredItems),
                        $"item {ii}");
                }

                // the subscription is full: a further item is rejected until one is deleted.
                CreateMonitoredItemsResponse full = await CreateValueItemsAsync(subscriptionId, 1)
                    .ConfigureAwait(false);
                Assert.That(full.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadTooManyMonitoredItems));

                await DeleteItemsAsync(subscriptionId, response.Results[0].MonitoredItemId).ConfigureAwait(false);

                CreateMonitoredItemsResponse again = await CreateValueItemsAsync(subscriptionId, 1)
                    .ConfigureAwait(false);
                Assert.That(again.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// M7-2: the server-wide MaxMonitoredItemCount spans all subscriptions.
        /// </summary>
        [Test]
        public async Task CreateMonitoredItemsBeyondServerLimitReturnsBadTooManyMonitoredItemsAsync()
        {
            uint first = await CreateSubscriptionAsync().ConfigureAwait(false);
            uint second = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse firstResponse = await CreateValueItemsAsync(
                    first, kMaxMonitoredItemsPerSubscription).ConfigureAwait(false);
                Assert.That(
                    firstResponse.Results.ToArray()!.All(r => r.StatusCode == StatusCodes.Good),
                    Is.True);

                const int remaining = kMaxMonitoredItemCount - kMaxMonitoredItemsPerSubscription;
                CreateMonitoredItemsResponse secondResponse = await CreateValueItemsAsync(
                    second, remaining + 1).ConfigureAwait(false);
                for (int ii = 0; ii < remaining; ii++)
                {
                    Assert.That(secondResponse.Results[ii].StatusCode, Is.EqualTo(StatusCodes.Good), $"item {ii}");
                }
                Assert.That(
                    secondResponse.Results[remaining].StatusCode,
                    Is.EqualTo(StatusCodes.BadTooManyMonitoredItems));
            }
            finally
            {
                await DeleteSubscriptionAsync(first).ConfigureAwait(false);
                await DeleteSubscriptionAsync(second).ConfigureAwait(false);
            }

            // deleting the subscriptions frees the room again.
            uint third = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse thirdResponse = await CreateValueItemsAsync(
                    third, kMaxMonitoredItemsPerSubscription).ConfigureAwait(false);
                Assert.That(
                    thirdResponse.Results.ToArray()!.All(r => r.StatusCode == StatusCodes.Good),
                    Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(third).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// M7-2: the monitored item limits and the data queue cap are published in
        /// ServerCapabilities (Part 5 §6.3.2).
        /// </summary>
        [Test]
        public async Task ServerCapabilitiesPublishMonitoredItemLimitsAsync()
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerCapabilities_MaxMonitoredItems,
                    AttributeId = Attributes.Value
                },
                new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription,
                    AttributeId = Attributes.Value
                },
                new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerCapabilities_MaxMonitoredItemsQueueSize,
                    AttributeId = Attributes.Value
                }
            ];

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse response = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(response.Results[0].WrappedValue.TryGetValue(out uint maxItems), Is.True);
            Assert.That(maxItems, Is.EqualTo((uint)kMaxMonitoredItemCount));
            Assert.That(response.Results[1].WrappedValue.TryGetValue(out uint maxPerSubscription), Is.True);
            Assert.That(maxPerSubscription, Is.EqualTo((uint)kMaxMonitoredItemsPerSubscription));
            Assert.That(response.Results[2].WrappedValue.TryGetValue(out uint maxQueueSize), Is.True);
            Assert.That(
                maxQueueSize,
                Is.EqualTo((uint)m_fixture.Config.ServerConfiguration!.MaxNotificationQueueSize));
        }

        /// <summary>
        /// M7-7: SetMonitoringMode with an unknown MonitoringMode is a service fault
        /// Bad_MonitoringModeInvalid (Part 4 §5.13.4.3) and the item keeps its mode.
        /// </summary>
        [Test]
        public async Task SetMonitoringModeWithInvalidModeReturnsBadMonitoringModeInvalidAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse created = await CreateValueItemsAsync(subscriptionId, 1)
                    .ConfigureAwait(false);
                uint itemId = created.Results[0].MonitoredItemId;

                m_requestHeader.Timestamp = DateTimeUtc.Now;
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_services.SetMonitoringModeAsync(
                        m_requestHeader,
                        subscriptionId,
                        (MonitoringMode)7,
                        [itemId]).ConfigureAwait(false));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadMonitoringModeInvalid));

                // a valid mode is still accepted.
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                SetMonitoringModeResponse response = await m_services.SetMonitoringModeAsync(
                    m_requestHeader,
                    subscriptionId,
                    MonitoringMode.Sampling,
                    [itemId]).ConfigureAwait(false);
                Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// M7-11: ModifyMonitoredItems with an invalid TimestampsToReturn is a service fault even
        /// when none of the monitored item ids exist (Part 4 §5.13.3.3).
        /// </summary>
        [Test]
        public async Task ModifyMonitoredItemsWithInvalidTimestampsAndUnknownIdsReturnsBadTimestampsToReturnInvalidAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_services.ModifyMonitoredItemsAsync(
                        m_requestHeader,
                        subscriptionId,
                        (TimestampsToReturn)9,
                        [
                            new MonitoredItemModifyRequest
                            {
                                MonitoredItemId = 999,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 100,
                                    QueueSize = 1,
                                    DiscardOldest = true
                                }
                            }
                        ]).ConfigureAwait(false));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimestampsToReturnInvalid));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// M7-3/M7-4: an event filter with one invalid select clause creates the item, reports
        /// the clause in selectClauseResults without diagnostic infos when none were requested,
        /// and delivers a null value for that event field (Part 4 §7.22.3).
        /// </summary>
        [Test]
        public async Task CreateEventItemWithOneInvalidSelectClauseCreatesItemAndReturnsClauseResultAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventId));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
                filter.SelectClauses = filter.SelectClauses.AddItem(new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    BrowsePath = [QualifiedName.From(BrowseNames.Severity)],
                    AttributeId = 999
                });

                CreateMonitoredItemsResponse response = await CreateEventItemAsync(
                    subscriptionId, ObjectIds.Server, filter).ConfigureAwait(false);

                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    response.Results[0].FilterResult.TryGetValue(out EventFilterResult? filterResult),
                    Is.True);
                Assert.That(filterResult!.SelectClauseResults.Count, Is.EqualTo(3));
                Assert.That(filterResult.SelectClauseResults[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(filterResult.SelectClauseResults[1], Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    filterResult.SelectClauseResults[2],
                    Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
                Assert.That(filterResult.SelectClauseDiagnosticInfos.Count, Is.Zero);

                // the event is delivered with a null third field.
                const string message = "Partially valid select clause";
                ReportServerEvent(message);
                EventFieldList fields = await PublishEventAsync(message).ConfigureAwait(false);
                Assert.That(fields, Is.Not.Null, "Did not receive the event.");
                Assert.That(fields.EventFields.Count, Is.EqualTo(3));
                Assert.That(fields.EventFields[0].IsNull, Is.False);
                Assert.That(fields.EventFields[2].IsNull, Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// M7-3/M7-5: an event filter without any valid select clause, or without any select
        /// clause at all, is rejected with Bad_EventFilterInvalid.
        /// </summary>
        [Test]
        public async Task CreateEventItemWithoutValidSelectClauseReturnsBadEventFilterInvalidAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                var allInvalid = new EventFilter
                {
                    SelectClauses =
                    [
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            BrowsePath = [QualifiedName.From(BrowseNames.EventId)],
                            AttributeId = 999
                        }
                    ],
                    WhereClause = new ContentFilter()
                };
                CreateMonitoredItemsResponse invalid = await CreateEventItemAsync(
                    subscriptionId, ObjectIds.Server, allInvalid).ConfigureAwait(false);
                Assert.That(invalid.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadEventFilterInvalid));

                var empty = new EventFilter { WhereClause = new ContentFilter() };
                CreateMonitoredItemsResponse emptyResponse = await CreateEventItemAsync(
                    subscriptionId, ObjectIds.Server, empty).ConfigureAwait(false);
                Assert.That(
                    emptyResponse.Results[0].StatusCode,
                    Is.EqualTo(StatusCodes.BadEventFilterInvalid));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// M7-10: when the owner rejects the re-subscription of a modified event item the
        /// operation fails and the item keeps its previous filter (Part 4 §5.13.3).
        /// </summary>
        [Test]
        public async Task ModifyEventItemRejectedByOwnerKeepsPreviousFilterAsync()
        {
            var notifierId = new NodeId("CTT", m_namespaceIndex);
            (object? handle, IAsyncNodeManager? _) = await m_server.CurrentInstance.NodeManager
                .GetManagerHandleAsync(notifierId).ConfigureAwait(false);
            Assert.That(handle, Is.InstanceOf<NodeHandle>(), "CTT notifier not found.");
            var notifier = (BaseObjectState)((NodeHandle)handle).Node;
            byte originalNotifier = notifier.EventNotifier;
            Assert.That(originalNotifier & EventNotifiers.SubscribeToEvents, Is.Not.Zero);

            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventId));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
                CreateMonitoredItemsResponse created = await CreateEventItemAsync(
                    subscriptionId, notifierId, filter).ConfigureAwait(false);
                Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

                var newFilter = new EventFilter();
                newFilter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventId));
                newFilter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
                newFilter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Severity));

                notifier.EventNotifier = EventNotifiers.None;
                ModifyMonitoredItemsResponse modified;
                try
                {
                    m_requestHeader.Timestamp = DateTimeUtc.Now;
                    modified = await m_services.ModifyMonitoredItemsAsync(
                        m_requestHeader,
                        subscriptionId,
                        TimestampsToReturn.Both,
                        [
                            new MonitoredItemModifyRequest
                            {
                                MonitoredItemId = created.Results[0].MonitoredItemId,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 0,
                                    QueueSize = 100,
                                    DiscardOldest = true,
                                    Filter = new ExtensionObject(newFilter)
                                }
                            }
                        ]).ConfigureAwait(false);
                }
                finally
                {
                    notifier.EventNotifier = originalNotifier;
                }

                Assert.That(StatusCode.IsBad(modified.Results[0].StatusCode), Is.True);

                // the item still reports events with the previous two fields.
                const string message = "Event after rejected modify";
                ISystemContext context = m_server.CurrentInstance.DefaultSystemContext;
                var e = new BaseEventState(null);
                e.Initialize(context, notifier, EventSeverity.Medium, new LocalizedText(message));
                notifier.ReportEvent(context, e);

                EventFieldList fields = await PublishEventAsync(message).ConfigureAwait(false);
                Assert.That(fields, Is.Not.Null, "Did not receive the event.");
                Assert.That(fields.EventFields.Count, Is.EqualTo(2));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// M7-8: overlapping CreateMonitoredItems and DeleteMonitoredItems calls on one
        /// subscription keep the session CurrentMonitoredItemsCount equal to the real count.
        /// </summary>
        [Test]
        public async Task ConcurrentCreateAndDeleteKeepSessionMonitoredItemCountAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse previous = await CreateValueItemsAsync(subscriptionId, 2)
                    .ConfigureAwait(false);
                for (int iteration = 0; iteration < 20; iteration++)
                {
                    uint[] toDelete = [.. previous.Results.ToArray()!.Select(r => r.MonitoredItemId)];
                    Task<CreateMonitoredItemsResponse> create = CreateValueItemsAsync(subscriptionId, 2);
                    Task delete = DeleteItemsAsync(subscriptionId, toDelete);
                    await Task.WhenAll(create, delete).ConfigureAwait(false);
                    previous = await create.ConfigureAwait(false);
                }

                ISession session = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(s => s.SessionName == TestContext.CurrentContext.Test.Name);
                uint sessionCount = session.ReadDiagnostics(d => d.CurrentMonitoredItemsCount);
                ISubscription subscription = m_server.CurrentInstance.SubscriptionManager.GetSubscriptions()
                    .Single(s => s.Id == subscriptionId);
                Assert.That(sessionCount, Is.EqualTo((uint)subscription.MonitoredItemCount));
                Assert.That(sessionCount, Is.EqualTo(2u));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        private async Task<uint> CreateSubscriptionAsync()
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse response = await m_services.CreateSubscriptionAsync(
                requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            return response.SubscriptionId;
        }

        private async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            await m_services.DeleteSubscriptionsAsync(requestHeader, [subscriptionId]).ConfigureAwait(false);
        }

        private async Task<CreateMonitoredItemsResponse> CreateValueItemsAsync(uint subscriptionId, int count)
        {
            var items = new List<MonitoredItemCreateRequest>(count);
            for (int ii = 0; ii < count; ii++)
            {
                items.Add(new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)ii + 1,
                        SamplingInterval = 1000,
                        QueueSize = 1,
                        DiscardOldest = true
                    }
                });
            }

            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            return await m_services.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                TimestampsToReturn.Both,
                items.ToArrayOf()).ConfigureAwait(false);
        }

        private async Task DeleteItemsAsync(uint subscriptionId, params uint[] monitoredItemIds)
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            await m_services.DeleteMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                monitoredItemIds.ToArrayOf()).ConfigureAwait(false);
        }

        private async Task<CreateMonitoredItemsResponse> CreateEventItemAsync(
            uint subscriptionId,
            NodeId notifierId,
            EventFilter filter)
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            return await m_services.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = notifierId,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            QueueSize = 100,
                            DiscardOldest = true,
                            Filter = new ExtensionObject(filter)
                        }
                    }
                ]).ConfigureAwait(false);
        }

        private void ReportServerEvent(string message)
        {
            IServerInternal serverInternal = m_server.CurrentInstance;
            ISystemContext context = serverInternal.DefaultSystemContext;
            var e = new BaseEventState(null);
            e.Initialize(context, serverInternal.ServerObject, EventSeverity.Medium, new LocalizedText(message));
            serverInternal.ReportEvent(context, e);
        }

        /// <summary>
        /// Publishes until an event with the given message arrives (the message is matched
        /// against the second event field) or the attempts are exhausted.
        /// </summary>
        private async Task<EventFieldList> PublishEventAsync(string message)
        {
            var acknowledgements = new List<SubscriptionAcknowledgement>();
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var requestHeader = (RequestHeader)m_requestHeader.Clone();
                requestHeader.Timestamp = DateTimeUtc.Now;
                PublishResponse response = await m_services.PublishAsync(
                    requestHeader,
                    acknowledgements.ToArrayOf()).ConfigureAwait(false);
                acknowledgements.Clear();
                NotificationMessage notificationMessage = response.NotificationMessage;
                if (notificationMessage.NotificationData.Count > 0)
                {
                    acknowledgements.Add(new SubscriptionAcknowledgement
                    {
                        SubscriptionId = response.SubscriptionId,
                        SequenceNumber = notificationMessage.SequenceNumber
                    });
                }

                foreach (ExtensionObject data in notificationMessage.NotificationData)
                {
                    if (!data.TryGetValue(out EventNotificationList? events))
                    {
                        continue;
                    }

                    foreach (EventFieldList fields in events.Events)
                    {
                        if (fields.EventFields.Count > 1 &&
                            fields.EventFields[1].TryGetValue(out LocalizedText text) &&
                            text.Text == message)
                        {
                            return fields;
                        }
                    }
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            return null!;
        }
    }
}
