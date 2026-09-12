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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;

namespace Opc.Ua.Subscriptions.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class SubscriptionSemanticsServiceTests : TestFixture
    {
        [TestCase(MonitoringMode.Disabled)]
        [TestCase(MonitoringMode.Sampling)]
        [TestCase(MonitoringMode.Reporting)]
        public async Task AggregateCompletionRespectsMonitoringModeOverPublishServiceAsync(MonitoringMode mode)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            AggregateManager aggregates = ReferenceServer.CurrentInstance.AggregateManager;
            var calculator = new Mock<IAggregateCalculator>();
            calculator.Setup(value => value.HasEndTimePassed(It.IsAny<DateTimeUtc>())).Returns(true);
            calculator.Setup(value => value.QueueRawValue(It.IsAny<DataValue>())).Returns(true);
            var aggregateValue = new DataValue(42.0);
            calculator.Setup(value => value.TryGetProcessedValue(true, out aggregateValue)).Returns(true);
            var aggregateId = new NodeId(Guid.NewGuid(), 1);
            await aggregates.RegisterFactoryAsync(
                aggregateId,
                "Subscription readiness regression",
                (id, start, end, interval, stepped, configuration, telemetry) => calculator.Object,
                deadline.Token).ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await Session.CreateSubscriptionAsync(
                null, 50, 1000, 1, 0, true, 0, deadline.Token).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse created = await Session.CreateMonitoredItemsAsync(
                    null,
                    subscription.SubscriptionId,
                    TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = new NodeId(Variables.Server_ServerStatus_SecondsTillShutdown),
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = mode,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 9,
                                SamplingInterval = 50,
                                QueueSize = 4,
                                DiscardOldest = true,
                                Filter = new ExtensionObject(new AggregateFilter
                                {
                                    AggregateType = aggregateId,
                                    StartTime = DateTime.UtcNow.AddSeconds(-1),
                                    ProcessingInterval = 1000,
                                    AggregateConfiguration = new AggregateConfiguration
                                    {
                                        UseServerCapabilitiesDefaults = true
                                    }
                                })
                            }
                        }
                    ],
                    deadline.Token).ConfigureAwait(false);
                Assert.That(created.Results, Has.Count.EqualTo(1));
                Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

                bool received = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    PublishResponse response = await Session.PublishAsync(
                        new RequestHeader { TimeoutHint = 2000 }, [], deadline.Token).ConfigureAwait(false);
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(response.SubscriptionId, Is.EqualTo(subscription.SubscriptionId));
                    if (mode != MonitoringMode.Reporting)
                    {
                        Assert.That(response.NotificationMessage.NotificationData, Is.Empty);
                        continue;
                    }
                    foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                    {
                        Assert.That(notification.TryGetValue(out DataChangeNotification? data), Is.True);
                        Assert.That(data!.MonitoredItems, Has.Count.EqualTo(1));
                        Assert.That(data.MonitoredItems[0].ClientHandle, Is.EqualTo(9u));
                        AssertValue(data.MonitoredItems[0].Value, 42.0);
                        received = true;
                    }
                    if (received)
                    {
                        break;
                    }
                }
                Assert.That(received, Is.EqualTo(mode == MonitoringMode.Reporting));
            }
            finally
            {
                await Session.DeleteSubscriptionsAsync(
                    null, [subscription.SubscriptionId], CancellationToken.None).ConfigureAwait(false);
                aggregates.RegisterFactory(aggregateId);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task PublishWithoutOwnSubscriptionsIgnoresOtherSessionsAsync(
            bool otherHasSubscription, bool deletedOwnSubscription)
        {
            using var other = await OpenAuxSessionAsync().ConfigureAwait(false);
            uint otherId = 0;
            if (otherHasSubscription)
            {
                CreateSubscriptionResponse created = await other.CreateSubscriptionAsync(
                    null, 1000, 1000, 10, 0, false, 0, CancellationToken.None).ConfigureAwait(false);
                otherId = created.SubscriptionId;
            }
            try
            {
                if (deletedOwnSubscription)
                {
                    CreateSubscriptionResponse own = await Session.CreateSubscriptionAsync(
                        null, 1000, 1000, 10, 0, false, 0, CancellationToken.None).ConfigureAwait(false);
                    await Session.DeleteSubscriptionsAsync(null, [own.SubscriptionId], CancellationToken.None)
                        .ConfigureAwait(false);
                }
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await Session.PublishAsync(
                        new RequestHeader { TimeoutHint = 1000 }, [], CancellationToken.None).ConfigureAwait(false));
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
            }
            finally
            {
                if (otherId != 0)
                {
                    await other.DeleteSubscriptionsAsync(null, [otherId], CancellationToken.None).ConfigureAwait(false);
                }
                await other.CloseAsync(5000, true).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RepublishCountersCountEachAuthorizedRequestOnceAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            CreateSubscriptionResponse created = await Session.CreateSubscriptionAsync(
                null, 50, 1000, 1, 0, true, 0, deadline.Token).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse items = await Session.CreateMonitoredItemsAsync(
                    null, created.SubscriptionId, TimestampsToReturn.Both,
                    [new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_SecondsTillShutdown,
                            AttributeId = Attributes.Value
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters { ClientHandle = 317, QueueSize = 1 }
                    }], deadline.Token).ConfigureAwait(false);
                Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                PublishResponse published;
                do
                {
                    published = await Session.PublishAsync(
                        new RequestHeader { TimeoutHint = 2000 }, [], deadline.Token).ConfigureAwait(false);
                } while (published.NotificationMessage.NotificationData.IsEmpty);

                Assert.That(ReferenceServer.CurrentInstance.SubscriptionManager.TryGetSubscription(
                    created.SubscriptionId, out ISubscription? subscription), Is.True);
                RepublishResponse republished = await Session.RepublishAsync(
                    null, created.SubscriptionId, published.NotificationMessage.SequenceNumber, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(republished.NotificationMessage.SequenceNumber,
                    Is.EqualTo(published.NotificationMessage.SequenceNumber));
                Assert.That(republished.NotificationMessage.NotificationData.Count,
                    Is.EqualTo(published.NotificationMessage.NotificationData.Count));
                Assert.That(subscription!.ReadDiagnostics(d => d.RepublishRequestCount), Is.EqualTo(1));
                Assert.That(subscription.ReadDiagnostics(d => d.RepublishMessageRequestCount), Is.EqualTo(1));
                Assert.That(subscription.ReadDiagnostics(d => d.RepublishMessageCount), Is.EqualTo(1));

                ServiceResultException missing = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await Session.RepublishAsync(null, created.SubscriptionId, uint.MaxValue, deadline.Token)
                        .ConfigureAwait(false));
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadMessageNotAvailable));
                Assert.That(subscription.ReadDiagnostics(d => d.RepublishRequestCount), Is.EqualTo(2));
                Assert.That(subscription.ReadDiagnostics(d => d.RepublishMessageRequestCount), Is.EqualTo(2));
                Assert.That(subscription.ReadDiagnostics(d => d.RepublishMessageCount), Is.EqualTo(1));

                using var other = await OpenAuxSessionAsync().ConfigureAwait(false);
                try
                {
                    ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await other.RepublishAsync(
                            null, created.SubscriptionId, published.NotificationMessage.SequenceNumber, deadline.Token)
                            .ConfigureAwait(false));
                    Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                    Assert.That(subscription.ReadDiagnostics(d => d.RepublishRequestCount), Is.EqualTo(2));
                    Assert.That(subscription.ReadDiagnostics(d => d.RepublishMessageRequestCount), Is.EqualTo(2));
                }
                finally
                {
                    await other.CloseAsync(5000, true).ConfigureAwait(false);
                }
            }
            finally
            {
                await Session.DeleteSubscriptionsAsync(null, [created.SubscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private static void AssertValue(in DataValue value, double expected)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
