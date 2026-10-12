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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises final-value ordering through real opc.tcp Publish requests.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("ValuePublication")]
    [NonParallelizable]
    public sealed class ValuePublicationIntegrationTests
    {
        /// <summary>
        /// Starts a minimal server and a real client without an automatic subscription worker.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.GetFullPath(Path.Combine(
                Environment.GetEnvironmentVariable("OPCUA_TEST_PKI_ROOT") ?? ".p",
                Guid.NewGuid().ToString("N")[..8]));
            m_acknowledgements.Clear();
            m_monitoredItemIds.Clear();
            m_fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.SetupGet(value => value.NamespacesUris).Returns([kNamespaceUri]);
                factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((IServerInternal instance, ApplicationConfiguration configuration, CancellationToken _) =>
                    {
                        m_manager = new PublicationNodeManager(instance, configuration);
                        return new ValueTask<IAsyncNodeManager>(m_manager);
                    });
                server.AddNodeManager(factory.Object);
                return server;
            })
            {
                SecurityNone = true,
                AutoAccept = true
            };
            await m_fixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.MinSubscriptionLifetime = 100;
            await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            await m_manager.AddJobAsync().ConfigureAwait(false);

            m_client = new ClientFixture(NUnitTelemetryContext.Create());
            await m_client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await m_client.GetEndpointAsync(
                new Uri("opc.tcp://localhost:" + m_fixture.Port.ToString(CultureInfo.InvariantCulture)),
                SecurityPolicies.None).ConfigureAwait(false);
            m_session = await m_client.ConnectAsync(endpoint).ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the client, server, and short-path certificate stores.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// A final value reaches the client before deletion even when the item can retain only one value.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(60_000)]
        public async Task QueueSizeOneReceivesFinalValueBeforeNodeDeletion(
            bool samplingGroups,
            CancellationToken cancellationToken)
        {
            if (samplingGroups)
            {
                m_manager.UseSamplingGroups(m_fixture.Config);
            }
            uint subscriptionId = await SubscribeAsync(queueSize: 1, cancellationToken).ConfigureAwait(false);
            DataValue initial = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(initial.WrappedValue, Is.EqualTo(new Variant(1)));

            Task removal = FinalizeJobAsync(cancellationToken);
            DataValue final = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(final.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(final.WrappedValue, Is.EqualTo(new Variant(2)));

            await removal.ConfigureAwait(false);
            DataValue deleted = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Publication waits for both clients rather than letting the fastest subscriber authorize deletion.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task EveryActiveSessionReceivesFinalValueBeforeDeletion(CancellationToken cancellationToken)
        {
            Client.ISession second = await m_client.ConnectAsync(m_session.ConfiguredEndpoint).ConfigureAwait(false);
            await using (second.ConfigureAwait(false))
            {
                uint firstSubscription = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
                uint secondSubscription = await SubscribeAsync(1, cancellationToken, second).ConfigureAwait(false);
                await PublishValueAsync(firstSubscription, cancellationToken).ConfigureAwait(false);
                await PublishValueAsync(secondSubscription, cancellationToken, second).ConfigureAwait(false);
                Task removal = FinalizeJobAsync(cancellationToken);

                DataValue firstFinal = await PublishValueAsync(firstSubscription, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(firstFinal.WrappedValue, Is.EqualTo(new Variant(2)));
                Assert.That(removal.IsCompleted, Is.False);
                Assert.That(m_manager.FindPredefinedNode<NodeState>(m_manager.Job.NodeId), Is.SameAs(m_manager.Job));

                DataValue secondFinal = await PublishValueAsync(
                    secondSubscription, cancellationToken, second).ConfigureAwait(false);
                Assert.That(secondFinal.WrappedValue, Is.EqualTo(new Variant(2)));
                await removal.ConfigureAwait(false);
                Assert.That((await PublishValueAsync(firstSubscription, cancellationToken).ConfigureAwait(false))
                    .StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                DataValue secondDeleted = await PublishValueAsync(secondSubscription, cancellationToken, second)
                    .ConfigureAwait(false);
                Assert.That(secondDeleted.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                await second.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An in-flight sample cannot replace the deletion notification after a successful terminal observation.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task LateSampleCannotReplaceDeletionAfterFinalPublication(CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            MonitoredItem item = m_manager.GetItem(m_monitoredItemIds[subscriptionId]);
            m_manager.Value.Value = 2;
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(2)));
            await observation.ConfigureAwait(false);
            await m_manager.DeleteNodeAsync(
                m_manager.SystemContext, m_manager.Job.NodeId, cancellationToken).ConfigureAwait(false);

            item.QueueValue(new DataValue(99), ServiceResult.Good);
            DataValue deleted = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// A final entry prebatched behind earlier messages cannot complete before its own response is selected.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(60_000)]
        public async Task PrebatchedMessagesDoNotCompleteTheFinalEntryEarly(
            bool discardOldest,
            CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(
                3, cancellationToken, discardOldest: discardOldest).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            MonitoredItem item = m_manager.GetItem(m_monitoredItemIds[subscriptionId]);
            item.QueueValue(new DataValue(10), ServiceResult.Good);
            item.QueueValue(new DataValue(11), ServiceResult.Good);
            m_manager.Value.Value = 12;
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();

            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(10)));
            Assert.That(observation.IsCompleted, Is.False);
            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(11)));
            Assert.That(observation.IsCompleted, Is.False);
            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(12)));
            await observation.ConfigureAwait(false);
        }

        /// <summary>
        /// The bounded monitored queue retains the final sample under both overflow policies.
        /// </summary>
        [TestCase(true, 11)]
        [TestCase(false, 10)]
        [CancelAfter(60_000)]
        public async Task FullQueueRetainsTheFinalSample(
            bool discardOldest,
            int retainedOlderValue,
            CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(
                2, cancellationToken, discardOldest: discardOldest).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            MonitoredItem item = m_manager.GetItem(m_monitoredItemIds[subscriptionId]);
            item.QueueValue(new DataValue(10), ServiceResult.Good);
            item.QueueValue(new DataValue(11), ServiceResult.Good);
            m_manager.Value.Value = 12;
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();

            Assert.That(item.ItemsInQueue, Is.EqualTo(2));
            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(retainedOlderValue)));
            Assert.That(observation.IsCompleted, Is.False);
            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(12)));
            await observation.ConfigureAwait(false);
        }

        /// <summary>
        /// Data-change suppression cannot swallow a terminal sample, including unchanged values.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [CancelAfter(60_000)]
        public async Task FilteredOrUnchangedFinalValueStillEntersAResponse(
            int finalValue,
            CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(
                1,
                cancellationToken,
                filter: new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.Absolute,
                    DeadbandValue = 100
                }).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            m_manager.Value.Value = finalValue;
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            Assert.That(observation.IsCompleted, Is.False);
            DataValue final = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(final.WrappedValue, Is.EqualTo(new Variant(finalValue)));
            await observation.ConfigureAwait(false);
        }

        /// <summary>
        /// A live subscription with publishing disabled keeps its obligation; keep-alives do not release it.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task PublishingDisabledWaitsForARealDataResponse(CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            await m_session.SetPublishingModeAsync(
                null!, false, [subscriptionId], cancellationToken).ConfigureAwait(false);
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();

            PublishResponse keepAlive = await m_session.PublishAsync(null!, default, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(keepAlive.NotificationMessage.NotificationData, Is.Empty);
            Assert.That(observation.IsCompleted, Is.False);
            await m_session.SetPublishingModeAsync(
                null!, true, [subscriptionId], cancellationToken).ConfigureAwait(false);
            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(1)));
            await observation.ConfigureAwait(false);
        }

        /// <summary>
        /// Timeout and cancellation retain the node and allow another final observation afterwards.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(60_000)]
        public async Task FailedObservationRetainsNodesAndAllowsRetry(
            bool cancel,
            CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task observation = m_manager.WaitForValuePublicationAsync(
                [m_manager.Job], TimeSpan.FromMilliseconds(100), cancelled.Token).AsTask();
            if (cancel)
            {
                cancelled.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await observation.ConfigureAwait(false));
            }
            else
            {
                Assert.ThrowsAsync<TimeoutException>(async () => await observation.ConfigureAwait(false));
            }
            Assert.That(m_manager.FindPredefinedNode<NodeState>(m_manager.Job.NodeId), Is.SameAs(m_manager.Job));
            DataValue stillReadable = await m_session.ReadValueAsync(m_manager.Value.NodeId, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(stillReadable.WrappedValue, Is.EqualTo(new Variant(1)));

            m_manager.Value.Value = 2;
            Task retry = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                .WrappedValue, Is.EqualTo(new Variant(2)));
            await retry.ConfigureAwait(false);
        }

        /// <summary>
        /// Removing a subscriber or ending its reporting obligation cannot strand an observation.
        /// </summary>
        [TestCase("item")]
        [TestCase("subscription")]
        [TestCase("session")]
        [TestCase("abandon")]
        [TestCase("disabled")]
        [TestCase("sampling")]
        [CancelAfter(60_000)]
        public async Task SubscriberTeardownReleasesOnlyItsObligation(
            string teardown,
            CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            switch (teardown)
            {
                case "item":
                    await m_session.DeleteMonitoredItemsAsync(
                        null!, subscriptionId, [m_monitoredItemIds[subscriptionId]], cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "subscription":
                    await m_session.DeleteSubscriptionsAsync(null!, [subscriptionId], cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "session":
                case "abandon":
                    m_session.DeleteSubscriptionsOnClose = teardown != "abandon";
                    await m_session.CloseAsync(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await m_session.SetMonitoringModeAsync(
                        null!,
                        subscriptionId,
                        teardown == "disabled" ? MonitoringMode.Disabled : MonitoringMode.Sampling,
                        [m_monitoredItemIds[subscriptionId]],
                        cancellationToken).ConfigureAwait(false);
                    break;
            }
            await observation.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            Assert.That(m_manager.FindPredefinedNode<NodeState>(m_manager.Job.NodeId), Is.SameAs(m_manager.Job));
        }

        /// <summary>
        /// Subscription lifetime expiry releases the obligation through the real server timer.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task SubscriptionExpiryReleasesObservation(CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(
                1, cancellationToken, lifetimeCount: 3, keepAliveCount: 1).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            await m_manager.WaitForValuePublicationAsync(
                [m_manager.Job], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

            PublishResponse expired = await m_session.PublishAsync(null!, default, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(expired.NotificationMessage.NotificationData[0].TryGetValue(
                out StatusChangeNotification? status), Is.True);
            Assert.That(status!.Status, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(m_manager.FindPredefinedNode<NodeState>(m_manager.Job.NodeId), Is.SameAs(m_manager.Job));
        }

        /// <summary>
        /// Multiple Value descendants with the same client handle are tracked by notification identity.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task SubtreeValuesWithDuplicateClientHandlesAreTrackedIndependently(
            CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            var second = new BaseDataVariableState(m_manager.Job)
            {
                NodeId = new NodeId("Job.Second", m_manager.NamespaceIndex),
                BrowseName = new QualifiedName("Second", m_manager.NamespaceIndex),
                DisplayName = new LocalizedText("Second"),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 0,
                Value = 42
            };
            m_manager.Job.AddChild(second);
            await m_manager.AddPredefinedNodeAsync(second, cancellationToken).ConfigureAwait(false);
            CreateMonitoredItemsResponse created = await m_session.CreateMonitoredItemsAsync(
                null!,
                subscriptionId,
                TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = second.NodeId, AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            QueueSize = 1,
                            DiscardOldest = true
                        }
                    }
                ],
                cancellationToken).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);

            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            DataValue first = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(observation.IsCompleted, Is.False);
            DataValue last = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Variant[] observed = [first.WrappedValue, last.WrappedValue];
            Variant[] expected = [new Variant(1), new Variant(42)];
            Assert.That(observed, Is.EquivalentTo(expected));
            await observation.ConfigureAwait(false);
        }

        /// <summary>
        /// Manager disposal cancels observation before draining admitted operations.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ManagerShutdownDoesNotDeadlockItsPublicationWait(CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            Task shutdown = m_manager.DisposeAsync().AsTask();
            Assert.CatchAsync<OperationCanceledException>(async () => await observation.ConfigureAwait(false));
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Custom items that lack the optional capability are rejected, not omitted from the barrier.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task UnsupportedCustomItemFailsWithoutRemovingItsNode(CancellationToken cancellationToken)
        {
            m_manager.UseUnsupportedCustomItem = true;
            await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            Assert.ThrowsAsync<NotSupportedException>(
                async () => await m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken)
                    .ConfigureAwait(false));
            Assert.That(m_manager.FindPredefinedNode<NodeState>(m_manager.Job.NodeId), Is.SameAs(m_manager.Job));
        }

        /// <summary>
        /// Final reads preserve each monitored item's index range rather than publishing the whole variable.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task FinalSampleRespectsTheMonitoredIndexRange(CancellationToken cancellationToken)
        {
            m_manager.Value.ValueRank = ValueRanks.OneDimension;
            m_manager.Value.Value = new Variant([10, 20, 30]);
            uint subscriptionId = await SubscribeAsync(1, cancellationToken, indexRange: "1").ConfigureAwait(false);
            DataValue initial = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(initial.WrappedValue, Is.EqualTo(new Variant([20])));

            m_manager.Value.Value = new Variant([11, 21, 31]);
            Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            DataValue final = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            Assert.That(final.WrappedValue, Is.EqualTo(new Variant([21])));
            await observation.ConfigureAwait(false);
        }

        /// <summary>
        /// A changed subscriber access level is checked before the final value is placed into its queue.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task FinalSamplingDoesNotBypassSubscriberAccess(CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            m_manager.Value.OnReadUserAccessLevel =
                (ISystemContext _, NodeState _, ref byte accessLevel) =>
                {
                    accessLevel = AccessLevels.None;
                    return ServiceResult.Good;
                };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken)
                    .ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(m_manager.FindPredefinedNode<NodeState>(m_manager.Job.NodeId), Is.SameAs(m_manager.Job));
        }

        /// <summary>
        /// Prior report-by-exception work must drain before the final sample; enqueue alone is not a fence.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task PendingReportByExceptionWorkIsDrainedBeforeFinalSampling(CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            m_manager.BlockNextPermissionCheck = true;
            m_manager.Value.Value = 2;
            await m_manager.Value.ClearChangeMasksAsync(
                m_manager.SystemContext, false, cancellationToken).ConfigureAwait(false);
            await m_manager.PermissionCheckEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            try
            {
                m_manager.Value.Value = 3;
                Task observation = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
                PublishResponse beforeDrain = await m_session.PublishAsync(null!, default, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(beforeDrain.NotificationMessage.NotificationData, Is.Empty,
                    "Only a keep-alive is possible while a preceding notification is still being processed.");
                Assert.That(observation.IsCompleted, Is.False);
                m_manager.PermissionCheckReleased.TrySetResult(true);

                DataValue final;
                do
                {
                    final = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
                }
                while (final.WrappedValue == new Variant(2));
                Assert.That(final.WrappedValue, Is.EqualTo(new Variant(3)));
                await observation.ConfigureAwait(false);
                await m_manager.DeleteNodeAsync(
                    m_manager.SystemContext, m_manager.Job.NodeId, cancellationToken).ConfigureAwait(false);
                Assert.That((await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false))
                    .StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            finally
            {
                m_manager.PermissionCheckReleased.TrySetResult(true);
            }
        }

        /// <summary>
        /// A timed-out consumer fence releases its registration and does not delete the subtree.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task BlockedReportConsumerHonorsThePublicationDeadline(CancellationToken cancellationToken)
        {
            uint subscriptionId = await SubscribeAsync(1, cancellationToken).ConfigureAwait(false);
            await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            m_manager.BlockNextPermissionCheck = true;
            m_manager.Value.Value = 2;
            await m_manager.Value.ClearChangeMasksAsync(
                m_manager.SystemContext, false, cancellationToken).ConfigureAwait(false);
            await m_manager.PermissionCheckEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            try
            {
                Assert.ThrowsAsync<TimeoutException>(
                    async () => await m_manager.WaitForValuePublicationAsync(
                        [m_manager.Job], TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false));
                Assert.That(m_manager.FindPredefinedNode<NodeState>(m_manager.Job.NodeId), Is.SameAs(m_manager.Job));
            }
            finally
            {
                m_manager.PermissionCheckReleased.TrySetResult(true);
            }

            m_manager.Value.Value = 3;
            Task retry = m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).AsTask();
            DataValue final;
            do
            {
                final = await PublishValueAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            }
            while (final.WrappedValue == new Variant(2));
            Assert.That(final.WrappedValue, Is.EqualTo(new Variant(3)));
            await retry.ConfigureAwait(false);
        }

        private async Task FinalizeJobAsync(CancellationToken cancellationToken)
        {
            m_manager.Value.Value = 2;
            await m_manager.WaitForValuePublicationAsync([m_manager.Job], cancellationToken).ConfigureAwait(false);
            bool removed = await m_manager.DeleteNodeAsync(
                m_manager.SystemContext, m_manager.Job.NodeId, cancellationToken).ConfigureAwait(false);
            Assert.That(removed, Is.True);
        }

        private async Task<uint> SubscribeAsync(
            uint queueSize,
            CancellationToken cancellationToken,
            Client.ISession? session = null,
            bool discardOldest = true,
            DataChangeFilter? filter = null,
            string? indexRange = null,
            uint lifetimeCount = 3000,
            uint keepAliveCount = 10)
        {
            session ??= m_session;
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null!, 20, lifetimeCount, keepAliveCount, 1, true, 0, cancellationToken).ConfigureAwait(false);
            CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                null!,
                subscription.SubscriptionId,
                TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = m_manager.Value.NodeId,
                            AttributeId = Attributes.Value,
                            IndexRange = indexRange
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            QueueSize = queueSize,
                            DiscardOldest = discardOldest,
                            Filter = filter == null ? default : new ExtensionObject(filter)
                        }
                    }
                ],
                cancellationToken).ConfigureAwait(false);
            Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            m_monitoredItemIds[subscription.SubscriptionId] = items.Results[0].MonitoredItemId;
            return subscription.SubscriptionId;
        }

        private async Task<DataValue> PublishValueAsync(
            uint subscriptionId,
            CancellationToken cancellationToken,
            Client.ISession? session = null)
        {
            session ??= m_session;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                m_acknowledgements.TryGetValue(
                    session.SessionId, out ArrayOf<SubscriptionAcknowledgement> acknowledgements);
                PublishResponse response = await session.PublishAsync(
                    null!, acknowledgements, deadline.Token).ConfigureAwait(false);
                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                if (response.NotificationMessage.NotificationData.Count == 0)
                {
                    continue;
                }
                m_acknowledgements[session.SessionId] =
                [
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscriptionId,
                        SequenceNumber = response.NotificationMessage.SequenceNumber
                    }
                ];
                foreach (ExtensionObject extension in response.NotificationMessage.NotificationData)
                {
                    if (extension.TryGetValue(out DataChangeNotification? notification))
                    {
                        Assert.That(notification.MonitoredItems, Has.Count.EqualTo(1));
                        return notification.MonitoredItems[0].Value;
                    }
                }
            }
        }

        private sealed class PublicationNodeManager : AsyncCustomNodeManager
        {
            public PublicationNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, kNamespaceUri)
            {
                Job = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Job", NamespaceIndex),
                    BrowseName = new QualifiedName("Job", NamespaceIndex),
                    DisplayName = new LocalizedText("Job"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                Value = new BaseDataVariableState(Job)
                {
                    NodeId = new NodeId("Job.Value", NamespaceIndex),
                    BrowseName = new QualifiedName("Value", NamespaceIndex),
                    DisplayName = new LocalizedText("Value"),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    MinimumSamplingInterval = 0,
                    Value = 1
                };
                Job.AddChild(Value);
            }

            public BaseObjectState Job { get; }

            public BaseDataVariableState Value { get; }

            public bool UseUnsupportedCustomItem { get; set; }

            public bool BlockNextPermissionCheck
            {
                get => Volatile.Read(ref m_blockNextPermissionCheck) != 0;
                set => Volatile.Write(ref m_blockNextPermissionCheck, value ? 1 : 0);
            }

            public TaskCompletionSource<bool> PermissionCheckEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> PermissionCheckReleased { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask AddJobAsync()
            {
                return AddPredefinedNodeAsync(Job);
            }

            public void UseSamplingGroups(ApplicationConfiguration configuration)
            {
                m_monitoredItemManager.Dispose();
                m_monitoredItemManager = new SamplingGroupMonitoredItemManager(this, Server, configuration);
            }

            public MonitoredItem GetItem(uint monitoredItemId)
            {
                return (MonitoredItem)MonitoredItems[monitoredItemId];
            }

            public override async ValueTask<ServiceResult> ValidateRolePermissionsAsync(
                OperationContext context,
                NodeId nodeId,
                PermissionType requestedPermission,
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Exchange(ref m_blockNextPermissionCheck, 0) != 0)
                {
                    PermissionCheckEntered.TrySetResult(true);
                    await PermissionCheckReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                return await base.ValidateRolePermissionsAsync(
                    context, nodeId, requestedPermission, cancellationToken).ConfigureAwait(false);
            }

            protected override ValueTask<MonitoredItemCreateDecision> OnCreatingMonitoredItemAsync(
                MonitoredItemCreateContext context,
                CancellationToken cancellationToken = default)
            {
                return UseUnsupportedCustomItem
                    ? new ValueTask<MonitoredItemCreateDecision>(MonitoredItemCreateDecision.Use(CreateUnsupportedItem))
                    : base.OnCreatingMonitoredItemAsync(context, cancellationToken);
            }

            private static ISampledDataChangeMonitoredItem CreateUnsupportedItem(MonitoredItemFactoryContext context)
            {
                var item = new Mock<ISampledDataChangeMonitoredItem>();
                item.SetupGet(value => value.NodeManager).Returns(context.NodeManager);
                item.SetupGet(value => value.ManagerHandle).Returns(context.Handle);
                item.SetupGet(value => value.Id).Returns(context.MonitoredItemId);
                item.SetupGet(value => value.SubscriptionId).Returns(context.SubscriptionId);
                item.SetupGet(value => value.NodeId).Returns(context.Source.NodeId);
                item.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
                item.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                item.SetupProperty(value => value.SubscriptionCallback);
                item.SetupGet(value => value.Session).Returns(() => item.Object.SubscriptionCallback?.Session!);
                item.Setup(value => value.GetReadValueId()).Returns(context.Request.ItemToMonitor);
                var result = new MonitoredItemCreateResult
                {
                    MonitoredItemId = context.MonitoredItemId,
                    StatusCode = StatusCodes.Good,
                    RevisedQueueSize = context.QueueSize,
                    RevisedSamplingInterval = context.SamplingInterval
                };
                item.Setup(value => value.GetCreateResult(out result)).Returns(ServiceResult.Good);
                return item.Object;
            }

            private int m_blockNextPermissionCheck;
        }

        private const string kNamespaceUri = "urn:opcfoundation.org:Tests:ValuePublication";
        private string m_pkiRoot = null!;
        private ServerFixture<StandardServer> m_fixture = null!;
        private ClientFixture m_client = null!;
        private Client.ISession m_session = null!;
        private PublicationNodeManager m_manager = null!;
        private readonly Dictionary<NodeId, ArrayOf<SubscriptionAcknowledgement>> m_acknowledgements = [];
        private readonly Dictionary<uint, uint> m_monitoredItemIds = [];
    }
}
