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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Regressions for the V2 subscription defects reported by the
    /// Opc.Ua.Client audit. Each test names the defect it pins down.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("AuditRegression")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SubscriptionAuditRegressionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_completion = new FakeMessageAckQueue();
            m_services = new Mock<ISubscriptionServiceSetClientMethods>();
            m_managerContext = new FakeMonitoredItemManagerContext
            {
                CreateMonitoredItemFactory = (name, options, context) =>
                    new TestItem(
                        context,
                        name,
                        options,
                        m_telemetry.CreateLogger("AuditRegressionItem"))
            };
        }

        /// <summary>
        /// A loaded item installed its snapshot client handle without raising
        /// the process-wide handle counter, so a fresh process that loaded
        /// handles 40..49 minted 40 again and MonitoredItemManager.TryAdd threw
        /// out of Dictionary.Add on the duplicate key.
        /// </summary>
        [Test]
        public async Task LoadedClientHandlesRaiseTheGlobalHandleCounterAsync()
        {
            var sut = new MonitoredItemManager(m_managerContext, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                // A handle far above anything the counter has minted so far.
                const uint loadedHandle = 900_000u;
                Assert.That(
                    sut.AddLoaded(new MonitoredItemLoadState(
                        "Loaded",
                        OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>(),
                        loadedHandle,
                        7,
                        [])),
                    Is.True);

                // Every handle minted from here on must be above the loaded one,
                // otherwise it eventually collides with it.
                Assert.That(
                    sut.TryAdd(
                        "Fresh",
                        OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>(),
                        out IMonitoredItem fresh),
                    Is.True);
                Assert.That(fresh, Is.Not.Null);
                Assert.That(
                    fresh.ClientHandle,
                    Is.GreaterThan(loadedHandle),
                    "the global counter must be raised past every loaded handle");
            }
        }

        /// <summary>
        /// A transfer-restored item is healthy and already established on the
        /// server, but abandoning the constructor's change stamped
        /// BadOperationAbandoned on it and left the monitoring mode at its
        /// default, so TryRequeue force-recreated it.
        /// </summary>
        [Test]
        public async Task LoadedItemIsNotStampedAsFailedAsync()
        {
            var sut = new MonitoredItemManager(m_managerContext, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options =
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                options.Configure(o => o with
                {
                    StartNodeId = new NodeId("Loaded", 2),
                    MonitoringMode = MonitoringMode.Reporting
                });

                Assert.That(
                    sut.AddLoaded(new MonitoredItemLoadState(
                        "Loaded",
                        options,
                        42,
                        7,
                        [])),
                    Is.True);

                Assert.That(
                    sut.TryGetMonitoredItemByClientHandle(42, out IMonitoredItem item),
                    Is.True);
                var loaded = (MonitoredItems.MonitoredItem)item;
                Assert.Multiple(() =>
                {
                    Assert.That(
                        ServiceResult.IsGood(loaded.Error),
                        Is.True,
                        "a loaded item must not report BadOperationAbandoned");
                    Assert.That(
                        loaded.CurrentMonitoringMode,
                        Is.EqualTo(MonitoringMode.Reporting),
                        "leaving the mode at Disabled makes TryRequeue recreate a healthy item");
                });
            }
        }

        /// <summary>
        /// Queued triggering operations were not reported as pending changes,
        /// so the subscription stopped scheduling apply passes and a caller
        /// awaiting SetTriggeringAsync waited forever.
        /// </summary>
        [Test]
        public async Task QueuedTriggeringOperationsCountAsPendingChangesAsync()
        {
            var sut = new MonitoredItemManager(m_managerContext, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                Assert.That(
                    sut.TryAdd(
                        "Trigger",
                        OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>(),
                        out IMonitoredItem trigger),
                    Is.True);

                // Drain the create change the add queued so only the triggering
                // operation is left as pending work.
                sut.TryGetMonitoredItemChanges(out _, out _);

                sut.EnqueueTriggeringOperation(
                    new MonitoredItemManager.TriggeringOperation(trigger, [], []));

                Assert.That(
                    sut.HasPendingChanges,
                    Is.True,
                    "a queued triggering operation still needs an apply pass");
            }
        }

        /// <summary>
        /// Disposing the manager abandoned queued triggering operations without
        /// completing them, so every SetTriggeringAsync awaiting one hung.
        /// </summary>
        [Test]
        public async Task DisposeCompletesQueuedTriggeringOperationsAsync()
        {
            var sut = new MonitoredItemManager(m_managerContext, m_telemetry);
            Assert.That(
                sut.TryAdd(
                    "Trigger",
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>(),
                    out IMonitoredItem trigger),
                Is.True);

            var completion = new TaskCompletionSource<SetTriggeringResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            sut.EnqueueTriggeringOperation(
                new MonitoredItemManager.TriggeringOperation(trigger, [], [], completion));

            await sut.DisposeAsync();

            Assert.That(
                completion.Task.IsCompleted,
                Is.True,
                "an abandoned operation leaves its caller awaiting forever");
        }

        /// <summary>
        /// AutoSetQueueSize applied the "+1" outside the Max, so every
        /// Created/Modified notification raised the queue size by one and the
        /// item was modified forever.
        /// </summary>
        [Test]
        public async Task AutoSetQueueSizeIsIdempotentAsync()
        {
            var sut = new MonitoredItemManager(m_managerContext, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options =
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                options.Configure(o => o with
                {
                    StartNodeId = new NodeId("Auto", 2),
                    AutoSetQueueSize = true,
                    SamplingInterval = TimeSpan.FromMilliseconds(100)
                });
                Assert.That(sut.TryAdd("Auto", options, out IMonitoredItem item), Is.True);

                var monitoredItem = (TestItem)item;
                TimeSpan publishingInterval = TimeSpan.FromMilliseconds(1000);

                monitoredItem.RaiseSubscriptionStateChange(
                    SubscriptionState.Created, publishingInterval);

                for (int i = 0; i < 5; i++)
                {
                    monitoredItem.RaiseSubscriptionStateChange(
                        SubscriptionState.Modified, publishingInterval);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(
                        monitoredItem.RequestedQueueSizes,
                        Has.Count.EqualTo(1),
                        "only the first state change may resize; the following " +
                        "ones must be no-ops instead of ratcheting by one each time");
                    Assert.That(
                        monitoredItem.RequestedQueueSizes[0],
                        Is.EqualTo(11u),
                        "ceil(1000/100) + 1");
                });
            }
        }

        /// <summary>
        /// The sequence-number gap walk issued one Republish per missing
        /// sequence number, so a server jumping from 1 to 2,000,000,000 kept
        /// the worker busy for hours. It has to be bounded by what the server
        /// says it still holds.
        /// </summary>
        [Test]
        public async Task SequenceNumberGapWalkIsBoundedByTheRetransmissionQueueAsync()
        {
            m_services
                .Setup(s => s.RepublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RepublishResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.BadMessageNotAvailable
                    },
                    NotificationMessage = new NotificationMessage()
                });

            var sut = new RecordingProcessor(m_services.Object, m_completion, m_telemetry)
            {
                Id = 1
            };
            await using (sut.ConfigureAwait(false))
            {
                // Anchor the data dedup gate at 1 ...
                await sut.OnPublishReceivedAsync(
                    DataMessage(1),
                    new List<uint> { 1 },
                    []);
                await m_completion.WaitForQueuedAckAsync(1);

                // ... then jump two billion sequence numbers ahead while the
                // server only offers two retransmittable messages.
                await sut.OnPublishReceivedAsync(
                    DataMessage(2_000_000_000),
                    new List<uint> { 5, 6 },
                    []);
                await m_completion.WaitForQueuedAckAsync(2, 20_000);

                // Only what the server still holds may be republished; the raw
                // gap is two billion wide.
                m_services.Verify(
                    s => s.RepublishAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<uint>(),
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()),
                    Times.AtMost(2));
                Assert.That(
                    sut.MissingMessageCount,
                    Is.EqualTo(1_999_999_998L),
                    "the gap is still accounted for in full");
            }
        }

        /// <summary>
        /// The publish response string table was captured but never forwarded:
        /// handlers always received an empty table, so localized text in a
        /// notification could not be resolved.
        /// </summary>
        [Test]
        public async Task PublishResponseStringTableReachesTheHandlerAsync()
        {
            var sut = new RecordingProcessor(m_services.Object, m_completion, m_telemetry)
            {
                Id = 1
            };
            await using (sut.ConfigureAwait(false))
            {
                var stringTable = new List<string> { "de-DE", "a localized string" };

                await sut.OnPublishReceivedAsync(DataMessage(1), new List<uint> { 1 }, stringTable);
                await m_completion.WaitForQueuedAckAsync(1);

                Assert.That(
                    sut.LastStringTable,
                    Is.EqualTo(stringTable),
                    "the response string table must reach the notification handler");
            }
        }

        /// <summary>
        /// A throwing notification handler suppressed the acknowledgement even
        /// though the dedup gate had already advanced, so the server kept
        /// retransmitting a message the client discards as a duplicate.
        /// </summary>
        [Test]
        public async Task AcknowledgementIsQueuedEvenWhenTheHandlerThrowsAsync()
        {
            var sut = new RecordingProcessor(m_services.Object, m_completion, m_telemetry)
            {
                Id = 1,
                ThrowFromHandler = true
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.OnPublishReceivedAsync(DataMessage(1), new List<uint> { 1 }, []);
                await m_completion.WaitForQueuedAckAsync(1);

                Assert.That(m_completion.QueuedAcks, Has.Count.EqualTo(1));
                Assert.That(m_completion.QueuedAcks[0].SequenceNumber, Is.EqualTo(1u));
            }
        }

        /// <summary>
        /// The snapshot clamped the sampling interval at 0, turning the
        /// "use the publishing interval" sentinel (-1, Part 4 §7.21) into
        /// "fastest practical rate" on every save/load round trip.
        /// </summary>
        [Test]
        public void MonitoredItemSnapshotKeepsTheNegativeSamplingIntervalSentinel()
        {
            var options = new MonitoredItems.MonitoredItemOptions
            {
                StartNodeId = new NodeId("Item", 2),
                SamplingInterval = TimeSpan.FromMilliseconds(-1)
            };

            MonitoredItemStateSnapshot snapshot = MonitoredItemStateSnapshot.AsOptions(
                "Item",
                options,
                1,
                2,
                null);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.SamplingIntervalMs, Is.EqualTo(-1));
                Assert.That(
                    snapshot.ToOptions().SamplingInterval,
                    Is.EqualTo(TimeSpan.FromMilliseconds(-1)));
            });
        }

        /// <summary>
        /// The subscription snapshot dropped the partition and recovery
        /// options, so a restored subscription silently lost its configured
        /// partitioning behaviour.
        /// </summary>
        [Test]
        public void SubscriptionSnapshotRoundTripsThePartitionOptions()
        {
            var options = new SubscriptionOptions
            {
                RecoveryPolicy = SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer,
                DisableUnboundedItemMode = true,
                MaxMonitoredItemsPerPartition = 1234,
                MaxPartitionCount = 7,
                SecondaryPartitionIdleTimeout = TimeSpan.FromSeconds(42)
            };

            SubscriptionOptions restored = SubscriptionStateSnapshot
                .AsOptions(options, 5, default, default)
                .ToOptions();

            Assert.Multiple(() =>
            {
                Assert.That(
                    restored.RecoveryPolicy,
                    Is.EqualTo(SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer));
                Assert.That(restored.DisableUnboundedItemMode, Is.True);
                Assert.That(restored.MaxMonitoredItemsPerPartition, Is.EqualTo(1234u));
                Assert.That(restored.MaxPartitionCount, Is.EqualTo(7u));
                Assert.That(
                    restored.SecondaryPartitionIdleTimeout,
                    Is.EqualTo(TimeSpan.FromSeconds(42)));
            });
        }

        /// <summary>
        /// A subscription whose options happened to equal the defaults never
        /// signalled its state manager, because the options-changed hook
        /// short-circuits on the record comparison - so it was never created on
        /// the server until some unrelated change woke the manager.
        /// </summary>
        [Test]
        public async Task SubscriptionWithDefaultOptionsIsStillCreatedAsync()
        {
            var created = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_services
                .Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    created.TrySetResult(true);
                    return new CreateSubscriptionResponse
                    {
                        SubscriptionId = 11,
                        RevisedLifetimeCount = 10,
                        RevisedMaxKeepAliveCount = 5,
                        RevisedPublishingInterval = 1000
                    };
                });

            var context = new FakeSubscriptionContext
            {
                SubscriptionServiceSet = m_services.Object,
                MethodServiceSet = new Mock<IMethodServiceSetClientMethods>().Object,
                MonitoredItemServiceSet =
                    new Mock<IMonitoredItemServiceSetClientMethods>().Object
            };

            // Options that are equal to the defaults - the exact case the
            // record comparison used to swallow.
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();

            var sut = new DefaultOptionsSubscription(
                context,
                new Mock<ISubscriptionNotificationHandler>().Object,
                m_completion,
                options,
                m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                Task completed = await Task
                    .WhenAny(created.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(
                    ReferenceEquals(completed, created.Task),
                    Is.True,
                    "an all-default subscription must still be created on the server");
            }
        }

        private static NotificationMessage DataMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                PublishTime = DateTimeUtc.Now,
                NotificationData = [new ExtensionObject(new DataChangeNotification())]
            };
        }

        /// <summary>
        /// Monitored item that exposes the subscription state hook so the
        /// queue-size auto-sizing can be driven directly.
        /// </summary>
        private sealed class TestItem : MonitoredItems.MonitoredItem
        {
            public TestItem(
                IMonitoredItemContext context,
                string name,
                IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options,
                ILogger logger)
                : base(context, name, options, logger)
            {
            }

            /// <summary>
            /// Queue sizes the auto-sizing asked for, in order.
            /// </summary>
            public List<uint> RequestedQueueSizes { get; } = [];

            public void RaiseSubscriptionStateChange(
                SubscriptionState state,
                TimeSpan publishingInterval)
            {
                OnSubscriptionStateChange(state, publishingInterval);
            }

            protected override void OnOptionsChanged(
                MonitoredItems.MonitoredItemOptions options)
            {
                RequestedQueueSizes.Add(options.QueueSize);
                base.OnOptionsChanged(options);
            }
        }

        /// <summary>
        /// Records what reaches the notification handlers, and optionally
        /// throws from them.
        /// </summary>
        private sealed class RecordingProcessor : MessageProcessor
        {
            public RecordingProcessor(
                ISubscriptionServiceSetClientMethods services,
                IMessageAckQueue completion,
                ITelemetryContext telemetry)
                : base(services, completion, telemetry)
            {
            }

            public IReadOnlyList<string> LastStringTable { get; private set; } = [];
            public bool ThrowFromHandler { get; init; }

            protected override ValueTask OnDataChangeNotificationAsync(
                uint sequenceNumber,
                DateTime publishTime,
                DataChangeNotification notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                LastStringTable = stringTable;
                if (ThrowFromHandler)
                {
                    throw new InvalidOperationException("handler failure");
                }
                return default;
            }

            protected override ValueTask OnEventDataNotificationAsync(
                uint sequenceNumber,
                DateTime publishTime,
                EventNotificationList notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                LastStringTable = stringTable;
                return default;
            }

            protected override ValueTask OnKeepAliveNotificationAsync(
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            protected override ValueTask OnStatusChangeNotificationAsync(
                uint sequenceNumber,
                DateTime publishTime,
                StatusChangeNotification notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                LastStringTable = stringTable;
                return default;
            }
        }

        /// <summary>
        /// Subscription that keeps the supplied options untouched so the
        /// "options equal the defaults" case is exercised verbatim.
        /// </summary>
        private sealed class DefaultOptionsSubscription : Subscription
        {
            public DefaultOptionsSubscription(
                ISubscriptionContext context,
                ISubscriptionNotificationHandler handler,
                IMessageAckQueue completion,
                OptionsMonitor<SubscriptionOptions> options,
                ITelemetryContext telemetry)
                : base(context, handler, completion, options, telemetry)
            {
            }

            protected override MonitoredItems.MonitoredItem CreateMonitoredItem(
                string name,
                IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options,
                IMonitoredItemContext context,
                ITelemetryContext telemetry)
            {
                return new TestItem(
                    context,
                    name,
                    options,
                    telemetry.CreateLogger("AuditRegressionItem"));
            }
        }

        private ITelemetryContext m_telemetry;
        private FakeMessageAckQueue m_completion;
        private Mock<ISubscriptionServiceSetClientMethods> m_services;
        private FakeMonitoredItemManagerContext m_managerContext;
    }
}
