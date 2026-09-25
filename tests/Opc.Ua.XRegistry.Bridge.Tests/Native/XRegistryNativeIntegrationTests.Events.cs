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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task NativeInvalidationFeedUsesManagedSubscriptionsAndReleasesThemAsync()
        {
            Assert.That(
                m_session.TryGetSubscriptionManager(out Opc.Ua.Client.Subscriptions.ISubscriptionManager? manager),
                Is.True);
            int before = manager!.Count;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IAsyncEnumerator<XRegistryChangeHint> watcher = m_native.WatchAsync(s_writer, timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            try
            {
                Assert.That(await watcher.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(watcher.Current.Reason, Is.EqualTo("subscribed"));
                Assert.That(manager.Count, Is.EqualTo(before + 1));
                Assert.That((await m_native.ExecuteAsync(
                    Request(XRegistryAction.Replace, "/schemagroups/hinted", "{}"), timeout.Token).ConfigureAwait(
                        false))
                    .StatusCode, Is.EqualTo(201));
                Assert.That(await watcher.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(watcher.Current.RequiresFullInventory, Is.True);
                Assert.That(watcher.Current.Reason, Is.EqualTo("registry-changed"));
            }
            finally
            {
                await watcher.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(manager.Count, Is.EqualTo(before));
        }

        [Test]
        public async Task AnonymousNativeSessionCannotSubscribeToTheOperatorProjectionAsync()
        {
            await using ManagedSession anonymous = await ConnectAsync(anonymous: true).ConfigureAwait(false);
            EventFilter filter = GroupCreatedEventTypeRecord.EventFilters.Build(anonymous.NamespaceUris,
                new EventRecordDecoderRegistry().RegisterxRegistryDecoders(anonymous.NamespaceUris));
            CreateSubscriptionResponse subscription = await anonymous.CreateSubscriptionAsync(
                null, 20, 1000, 10, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            try
            {
                ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await anonymous.CreateMonitoredItemsAsync(
                        null, subscription.SubscriptionId, TimestampsToReturn.Neither,
                        [EventRequest(m_manager.RegistryNodeId, filter)], CancellationToken.None).ConfigureAwait(
                            false));
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            finally
            {
                _ = await anonymous.DeleteSubscriptionsAsync(
                    null, [subscription.SubscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RevokedEventReceiverCannotReuseItsCachedPermissionAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"initial"}""").ConfigureAwait(false);
            EventFilter filter = VersionUpdatedEventTypeRecord.EventFilters.Build(m_session.NamespaceUris,
                new EventRecordDecoderRegistry().RegisterxRegistryDecoders(m_session.NamespaceUris));
            uint subscription = await SubscribeEventsAsync(m_manager.RegistryNodeId, filter).ConfigureAwait(false);
            try
            {
                Assert.That((await m_native.ExecuteAsync(
                    Request(XRegistryAction.Merge, "/schemagroups/g/schemas/r/versions/v1", "{}"))
                    .ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
                _ = await NextEventAsync(filter, "/schemagroups/g/schemas/r/versions/v1").ConfigureAwait(false);
                m_denyReads = true;
                Assert.That((await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Merge, "/schemagroups/g/schemas/r/versions/v1", "{}"))
                    .ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
                await m_manager.RefreshAsync().ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                PublishResponse response = await m_session.PublishAsync(null, [], timeout.Token).ConfigureAwait(false);
                Assert.That(response.NotificationMessage.NotificationData.Count, Is.Zero,
                    "A primed permission cache must not release events after the receiver is revoked.");
            }
            finally
            {
                m_denyReads = false;
                _ = await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeGroupEventsReachAnEncryptedSubscriptionAfterCommitAsync(bool serverNotifier)
        {
            EventFilter filter = GroupCreatedEventTypeRecord.EventFilters.Build(m_session.NamespaceUris,
                new EventRecordDecoderRegistry().RegisterxRegistryDecoders(m_session.NamespaceUris));
            uint subscription = await SubscribeEventsAsync(
                serverNotifier ? Ua.ObjectIds.Server : m_manager.RegistryNodeId, filter).ConfigureAwait(false);
            try
            {
                XRegistryResponse created = await m_native.ExecuteAsync(
                    Request(XRegistryAction.Replace, "/schemagroups/notified", "{}")).ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(201));
                EventFieldList fields = await NextEventAsync(filter, "/schemagroups/notified").ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        EventField(fields, filter, BrowseNames.Subject).TryGetValue(out string subject), Is.True);
                    Assert.That(subject, Is.EqualTo("/schemagroups/notified"));
                    Assert.That(EventField(fields, filter, BrowseNames.Epoch).TryGetValue(out uint epoch), Is.True);
                    Assert.That(epoch, Is.Zero);
                    Assert.That(
                        EventField(fields, filter, BrowseNames.SourceUrl).TryGetValue(out string source), Is.True);
                    Assert.That(source, Is.EqualTo("https://registry.example.test/"));
                });
            }
            finally
            {
                _ = await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(null)]
        [TestCase("upstream-interaction")]
        public async Task NativeEventCorrelationMatchesOnlyAnActuallyReturnedUpstreamValueAsync(string? correlationId)
        {
            m_forwarder.CorrelationId = correlationId;
            EventFilter filter = GroupCreatedEventTypeRecord.EventFilters.Build(m_session.NamespaceUris,
                new EventRecordDecoderRegistry().RegisterxRegistryDecoders(m_session.NamespaceUris));
            uint subscription = await SubscribeEventsAsync(m_manager.RegistryNodeId, filter).ConfigureAwait(false);
            try
            {
                XRegistryResponse response = await m_native.ExecuteAsync(
                    Request(XRegistryAction.Replace, "/schemagroups/correlated", "{}") with
                    { OperationId = "client-journal-is-not-event-correlation" }).ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(201));
                Assert.That(response.CorrelationId, Is.EqualTo(correlationId));
                EventFieldList fields = await NextEventAsync(filter, "/schemagroups/correlated").ConfigureAwait(false);
                Variant actual = EventField(fields, filter, BrowseNames.CorrelationId);
                if (correlationId is null)
                {
                    Assert.That(actual.IsNull, Is.True);
                }
                else
                {
                    Assert.That(actual.TryGetValue(out string value), Is.True);
                    Assert.That(value, Is.EqualTo(correlationId));
                }
            }
            finally
            {
                _ = await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ChangedCommitCorrelationCannotPublishMisattributedEventsAsync()
        {
            m_forwarder.CorrelationId = "prepared-correlation";
            m_forwarder.ChangeCorrelationOnCommit = true;
            ILocalAddressSpace space = ((ILocalAddressSpaceSource)m_manager).CreateLocalAddressSpace();
            Assert.That(space.TryGetNode(m_manager.RegistryNodeId, out NodeState? node), Is.True);
            var events = new ConcurrentQueue<string>();
            var previous = node!.OnReportEventAsync;
            node.OnReportEventAsync = (_, _, value, _) =>
            {
                if (value is XRegistryEventState evt && evt.Subject!.Value == "/schemagroups/mismatch")
                {
                    events.Enqueue(evt.Subject.Value);
                }
                return default;
            };
            try
            {
                XRegistryResponse response = await m_native.ExecuteAsync(
                    Request(XRegistryAction.Replace, "/schemagroups/mismatch", "{}")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(201));
                    Assert.That(events, Is.Empty);
                    Assert.That(m_manager.IsNotificationDegraded, Is.True);
                });
                XRegistryResponse committed = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/mismatch")).ConfigureAwait(false);
                Assert.That(committed.StatusCode, Is.EqualTo(200));
            }
            finally
            {
                node.OnReportEventAsync = previous;
            }
        }

        [Test]
        public async Task LeafUpdatesEmitDistinctEventsWithoutUsingTheRootEpochAsASequenceAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"initial"}""").ConfigureAwait(false);
            EventFilter filter = VersionUpdatedEventTypeRecord.EventFilters.Build(m_session.NamespaceUris,
                new EventRecordDecoderRegistry().RegisterxRegistryDecoders(m_session.NamespaceUris));
            uint subscription = await SubscribeEventsAsync(m_manager.RegistryNodeId, filter).ConfigureAwait(false);
            try
            {
                XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                    .ConfigureAwait(false);
                for (int index = 1; index <= 2; index++)
                {
                    XRegistryResponse changed = await m_native.ExecuteAsync(Request(
                        XRegistryAction.Merge, "/schemagroups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
                    Assert.That(changed.StatusCode, Is.EqualTo(200));
                    EventFieldList fields = await NextEventAsync(filter, "/schemagroups/g/schemas/r/versions/v1")
                        .ConfigureAwait(false);
                    Assert.That(EventField(fields, filter, BrowseNames.Epoch).TryGetValue(out uint epoch), Is.True);
                    Assert.That(epoch, Is.EqualTo(index));
                }
                XRegistryResponse after = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                    .ConfigureAwait(false);
                Assert.That(after.Metadata.GetProperty("epoch").GetInt32(),
                    Is.EqualTo(root.Metadata.GetProperty("epoch").GetInt32()));
            }
            finally
            {
                _ = await m_session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FailedAndReplayedOperationsDoNotReportUncommittedOrDuplicateEventsAsync()
        {
            ILocalAddressSpace space = ((ILocalAddressSpaceSource)m_manager).CreateLocalAddressSpace();
            Assert.That(space.TryGetNode(m_manager.RegistryNodeId, out NodeState? node), Is.True);
            var events = new ConcurrentQueue<string>();
            var previousHandler = node!.OnReportEventAsync;
            node.OnReportEventAsync = async (_, _, value, ct) =>
            {
                if (value is XRegistryEventState evt && evt.Subject!.Value == "/schemagroups/once")
                {
                    XRegistryResponse committed = await m_forwarder.Inner.ExecuteAsync(
                        Request(XRegistryAction.Read, "/schemagroups/once"), ct).ConfigureAwait(false);
                    Assert.That(committed.StatusCode, Is.EqualTo(200), "Events must follow the authoritative commit.");
                    events.Enqueue(evt.Subject.Value);
                }
            };
            try
            {
                XRegistryRequest create = Request(XRegistryAction.Replace, "/schemagroups/once", "{}") with
                {
                    OperationId = "event-once"
                };
                IXRegistryPreparedOperation prepared = await m_native.PrepareAsync(create).ConfigureAwait(false);
                Assert.That(events, Is.Empty);
                await prepared.DisposeAsync().ConfigureAwait(false);
                Assert.That(events, Is.Empty);
                XRegistryResponse rejected = await m_native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"schemagroups":{"once":{},"bad":null}}""")).ConfigureAwait(false);
                Assert.That(rejected.IsSuccess, Is.False);
                Assert.That(events, Is.Empty);
                Assert.That((await m_native.ExecuteAsync(create).ConfigureAwait(false)).StatusCode, Is.EqualTo(201));
                int count = events.Count;
                Assert.That(count, Is.GreaterThan(0));
                Assert.That((await m_native.ExecuteAsync(create).ConfigureAwait(false)).StatusCode, Is.EqualTo(201));
                Assert.That(events, Has.Count.EqualTo(count));
            }
            finally
            {
                node.OnReportEventAsync = previousHandler;
            }
        }

        private async Task<uint> SubscribeEventsAsync(NodeId node, EventFilter filter)
        {
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 20, 1000, 10, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            CreateMonitoredItemsResponse created = await m_session.CreateMonitoredItemsAsync(
                null, subscription.SubscriptionId, TimestampsToReturn.Neither,
                [EventRequest(node, filter)], CancellationToken.None).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return subscription.SubscriptionId;
        }

        private static MonitoredItemCreateRequest EventRequest(NodeId node, EventFilter filter)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = node, AttributeId = Attributes.EventNotifier },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 67,
                    SamplingInterval = 0,
                    QueueSize = 100,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(filter)
                }
            };
        }

        private async Task<EventFieldList> NextEventAsync(EventFilter filter, string subject)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            for (int index = 0; index < 20; index++)
            {
                PublishResponse response = await m_session.PublishAsync(null, [], timeout.Token).ConfigureAwait(false);
                for (int item = 0; item < response.NotificationMessage.NotificationData.Count; item++)
                {
                    ExtensionObject notification = response.NotificationMessage.NotificationData[item];
                    if (notification.TryGetValue(out EventNotificationList? events))
                    {
                        foreach (EventFieldList fields in events.Events)
                        {
                            if (EventField(fields, filter, BrowseNames.Subject).TryGetValue(out string value) &&
                                value == subject)
                            {
                                return fields;
                            }
                        }
                    }
                }
            }
            throw new InvalidOperationException("The expected committed native event was not received.");
        }

        private static Variant EventField(EventFieldList fields, EventFilter filter, string name)
        {
            for (int index = 0; index < filter.SelectClauses.Count; index++)
            {
                ArrayOf<QualifiedName> path = filter.SelectClauses[index].BrowsePath;
                if (path.Count != 0 && path[^1].Name == name)
                {
                    return fields.EventFields[index];
                }
            }
            throw new InvalidOperationException("The generated filter does not contain " + name);
        }
    }
}
