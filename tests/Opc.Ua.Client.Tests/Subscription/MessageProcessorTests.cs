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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    [TestFixture]
    public sealed class MessageProcessorTests
    {
        [SetUp]
        public void SetUp()
        {
            m_completion = new FakeMessageAckQueue();
            m_telemetry = NUnitTelemetryContext.Create();
            m_mockServices = new Mock<ISubscriptionServiceSetClientMethods>();
        }

        [Test]
        public async Task DisposeAsyncShouldCompleteMessageWriterAndCancelTokenAsync()
        {
            // Arrange
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 3
            };

            // Act
            await sut.DisposeAsync().ConfigureAwait(false);

            // Assert
            Assert.That(sut.PublishState, Is.EqualTo(PublishState.Completed));
            Assert.That(m_completion.CompletedSubscriptions,
                Is.EqualTo(new uint[] { 3 }));
            // The subscription must be retired by identity, not only by id.
            Assert.That(m_completion.CompletedProcessors,
                Is.EqualTo(new IMessageProcessor[] { sut }));
        }

        /// <summary>
        /// The ack-wait helper the tests below use must fail on its own
        /// deadline rather than returning quietly. A silent return would push
        /// the diagnosis down to whatever the caller asserts next - a bare
        /// count mismatch that cannot tell an acknowledgement that was never
        /// queued from one that merely arrived late - and would hide the race
        /// entirely if that assertion were ever loosened.
        /// </summary>
        [Test]
        public void WaitForQueuedAckAsyncShouldThrowWhenTheAckNeverArrives()
        {
            var queue = new FakeMessageAckQueue();

            TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(
                async () => await queue.WaitForQueuedAckAsync(1, 200).ConfigureAwait(false));

            Assert.That(ex.Message, Does.Contain("only 0 arrived"));
        }

        [Test]
        public async Task OnPublishReceivedKeepAliveShouldDispatchKeepAliveAsync()
        {
            // Arrange
            var message = new NotificationMessage
            {
                SequenceNumber = 3
            };
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 3
            };
            await using (sut.ConfigureAwait(false))
            {
                // Act
                await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.KeepAliveNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                // Assert
                Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.True);
                Assert.That(sut.AvailableInRetransmissionQueue, Is.EqualTo(availableSequenceNumbers));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(3));
                Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.False);
                Assert.That(m_completion.QueuedAcks, Is.Empty);

                // Arrange
                sut.KeepAliveNotificationReceived.Reset();
                sut.DataChangeNotificationReceived.Reset();
                message = new NotificationMessage
                {
                    SequenceNumber = 4,
                    NotificationData =
                    [
                        new ExtensionObject(new DataChangeNotification
                        {
                            MonitoredItems =
                            [
                                new MonitoredItemNotification()
                            ]
                        })
                    ]
                };

                // Act
                await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.DataChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                // The processor dispatches the notification (which sets the
                // event awaited above) and only then queues the acknowledgement,
                // so asserting on QueuedAcks straight away races that last step.
                await m_completion.WaitForQueuedAckAsync(1).ConfigureAwait(false);

                // Assert
                Assert.That(sut.AvailableInRetransmissionQueue, Is.EqualTo(availableSequenceNumbers));
                Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.True);
                Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.False);
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(4));
                Assert.That(m_completion.QueuedAcks, Has.Count.EqualTo(1));
                Assert.That(m_completion.QueuedAcks[0].SubscriptionId, Is.EqualTo(3u));
                Assert.That(m_completion.QueuedAcks[0].SequenceNumber, Is.EqualTo(4u));
            }
        }

        [TestCase("data")]
        [TestCase("event")]
        [TestCase("keepalive")]
        [TestCase("status")]
        public async Task NotificationCallbacksHaveActiveReentryScopeAsync(string kind)
        {
            var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                sut.NotificationCallback = _ =>
                {
                    observed.TrySetResult(sut.IsDispatchingForTest);
                    return default;
                };

                await sut.OnPublishReceivedAsync(BuildNotificationMessage(kind, 1), null, [])
                    .ConfigureAwait(false);

                Assert.That(await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.True);
            }
        }

        [TestCase("data")]
        [TestCase("event")]
        [TestCase("keepalive")]
        [TestCase("status")]
        public async Task ChildOfCompletedCallbackCanDisposeDuringLaterDispatchAsync(string kind)
        {
            var releaseChild = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposalStarted = new TaskCompletionSource<InvalidOperationException?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task child = Task.CompletedTask;
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                sut.NotificationCallback = async sequenceNumber =>
                {
                    if (sequenceNumber == 1)
                    {
                        child = Task.Run(async () =>
                        {
                            await releaseChild.Task.ConfigureAwait(false);
                            try
                            {
                                ValueTask disposing = sut.DisposeAsync();
                                disposalStarted.TrySetResult(null);
                                await disposing.ConfigureAwait(false);
                            }
                            catch (InvalidOperationException exception)
                            {
                                disposalStarted.TrySetResult(exception);
                            }
                        });
                    }
                    else
                    {
                        secondEntered.TrySetResult(true);
                        await releaseSecond.Task.ConfigureAwait(false);
                    }
                };

                try
                {
                    await sut.OnPublishReceivedAsync(BuildNotificationMessage(kind, 1), null, [])
                        .ConfigureAwait(false);
                    await sut.OnPublishReceivedAsync(BuildNotificationMessage(kind, 2), null, [])
                        .ConfigureAwait(false);
                    await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    releaseChild.TrySetResult(true);

                    Assert.That(
                        await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                        Is.Null,
                        "The completed callback's child is not re-entering the later callback.");
                }
                finally
                {
                    releaseChild.TrySetResult(true);
                    releaseSecond.TrySetResult(true);
                    await child.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task DeferredDataCallbackTaskDoesNotInheritDispatchGuardAsync()
        {
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry)
            {
                Id = 3,
                DeferredCallbackMode = DeferredCallbackMode.Data
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.OnPublishReceivedAsync(
                    new NotificationMessage
                    {
                        SequenceNumber = 1,
                        NotificationData = [new ExtensionObject(new DataChangeNotification())]
                    },
                    null,
                    []).ConfigureAwait(false);
                await sut.DataChangeNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await m_completion.WaitForQueuedAckAsync(1).ConfigureAwait(false);
                sut.CallbackReturned.TrySetResult(true);
                Assert.That(await sut.DeferredDispatchGuard.ConfigureAwait(false), Is.False);
            }
        }

        [Test]
        public async Task DeferredEventCallbackTaskDoesNotInheritDispatchGuardAsync()
        {
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry)
            {
                Id = 3,
                DeferredCallbackMode = DeferredCallbackMode.Event
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.OnPublishReceivedAsync(
                    new NotificationMessage
                    {
                        SequenceNumber = 1,
                        NotificationData = [new ExtensionObject(new EventNotificationList())]
                    },
                    null,
                    []).ConfigureAwait(false);
                await sut.EventNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await m_completion.WaitForQueuedAckAsync(1).ConfigureAwait(false);
                sut.CallbackReturned.TrySetResult(true);
                Assert.That(await sut.DeferredDispatchGuard.ConfigureAwait(false), Is.False);
            }
        }

        [Test]
        public async Task DeferredStatusCallbackTaskDoesNotInheritDispatchGuardAsync()
        {
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry)
            {
                Id = 3,
                DeferredCallbackMode = DeferredCallbackMode.Status
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.OnPublishReceivedAsync(
                    new NotificationMessage
                    {
                        SequenceNumber = 1,
                        NotificationData =
                        [
                            new ExtensionObject(new StatusChangeNotification
                            {
                                Status = StatusCodes.BadTimeout
                            })
                        ]
                    },
                    null,
                    []).ConfigureAwait(false);
                await sut.StatusChangeNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await m_completion.WaitForQueuedAckAsync(1).ConfigureAwait(false);
                sut.CallbackReturned.TrySetResult(true);
                Assert.That(await sut.DeferredDispatchGuard.ConfigureAwait(false), Is.False);
            }
        }

        [Test]
        public async Task PublishingDrainDoesNotWaitForIngressBlockedByServiceWriterAsync()
        {
            var session = new FakeSubscriptionManagerContext();
            var manager = new SubscriptionManager(session, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry) { Id = 3 };
            using var serviceGate = new SemaphoreSlim(1, 1);
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ingressBlocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await serviceGate.WaitAsync().ConfigureAwait(false);
            sut.NotificationCallback = async _ =>
            {
                callbackEntered.TrySetResult(true);
                await serviceGate.WaitAsync().ConfigureAwait(false);
                serviceGate.Release();
            };
            var subscription = new FakeManagedSubscription
            {
                Id = 3,
                Created = true,
                OnPublishReceivedAsyncFunc = (message, available, strings) =>
                {
                    ValueTask receiving = sut.OnPublishReceivedAsync(message, available, strings);
                    ingressBlocked.TrySetResult(!receiving.IsCompleted);
                    return receiving;
                }
            };
            session.CreateSubscriptionFactory = (_, _, _) => subscription;
            session.OnPublishAsync = (_, _, _) => new ValueTask<PublishResponse>(new PublishResponse
            {
                SubscriptionId = 3,
                MoreNotifications = true,
                NotificationMessage = BuildNotificationMessage("keepalive", 1025)
            });
            Task quiescence = Task.CompletedTask;
            try
            {
                for (uint sequenceNumber = 1; sequenceNumber <= 1024; sequenceNumber++)
                {
                    await sut.OnPublishReceivedAsync(
                        BuildNotificationMessage("keepalive", sequenceNumber), null, []).ConfigureAwait(false);
                }
                await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                manager.Add(Mock.Of<ISubscriptionNotificationHandler>(), OptionsFactory.Create<SubscriptionOptions>());
                manager.Resume();
                Assert.That(await ingressBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.True);

                quiescence = manager.RunWithPublishingQuiescedAsync(_ =>
                {
                    manager.Pause();
                    drained.TrySetResult(true);
                    return default;
                }, CancellationToken.None).AsTask();

                await drained.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(serviceGate.CurrentCount, Is.Zero, "The service writer still owns the gate.");
                Assert.That(session.PublishCalls, Has.Count.EqualTo(1));
            }
            finally
            {
                serviceGate.Release();
                await quiescence.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await manager.DisposeAsync().ConfigureAwait(false);
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CapacityWaitCannotRelabelRetiredMessageAsNewGenerationAsync()
        {
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry);
            var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.NotificationCallback = sequenceNumber =>
            {
                if (sequenceNumber == 6000)
                {
                    barrier.TrySetResult(true);
                }
                return default;
            };
            await sut.Block.WaitAsync().ConfigureAwait(false);
            await using (sut.ConfigureAwait(false))
            {
                for (uint sequenceNumber = 1; sequenceNumber <= 1024; sequenceNumber++)
                {
                    await sut.OnPublishReceivedAsync(
                        BuildNotificationMessage("keepalive", sequenceNumber), null, []).ConfigureAwait(false);
                }
                await sut.KeepAliveNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task pending = sut.OnPublishReceivedAsync(BuildDataChangeMessage(5000), null, []).AsTask();
                Assert.That(pending.IsCompleted, Is.False);

                Task reset = sut.ResetGenerationAsync().AsTask();
                sut.Block.Release();
                await reset.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await sut.OnPublishReceivedAsync(BuildNotificationMessage("keepalive", 6000), null, [])
                    .ConfigureAwait(false);
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(sut.ReceivedSequenceNumbers, Does.Not.Contain(5000u));
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(1), null, []).ConfigureAwait(false);
                await m_completion.WaitForQueuedAckAsync(1).ConfigureAwait(false);
                Assert.That(m_completion.QueuedAcks.Single().SequenceNumber, Is.EqualTo(1u));
            }
        }

        [Test]
        public async Task BackpressuredBurstDeliversEveryNotificationAsync()
        {
            const int kMessageCount = 1100;
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry)
            {
                NotificationCallback = async sequenceNumber =>
                {
                    if (sequenceNumber == 1)
                    {
                        firstEntered.TrySetResult(true);
                        await releaseFirst.Task.ConfigureAwait(false);
                    }
                }
            };
            await using (sut.ConfigureAwait(false))
            {
                Task producing = ProduceAsync();
                try
                {
                    await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(producing.IsCompleted, Is.False);
                    releaseFirst.TrySetResult(true);
                    await producing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await m_completion.WaitForQueuedAckAsync(kMessageCount).ConfigureAwait(false);

                    Assert.That(sut.ReceivedSequenceNumbers,
                        Is.EqualTo(Enumerable.Range(1, kMessageCount).Select(value => (uint)value)));
                    Assert.That(m_completion.QueuedAcks.Select(acknowledgement => acknowledgement.SequenceNumber),
                        Is.EqualTo(Enumerable.Range(1, kMessageCount).Select(value => (uint)value)));
                }
                finally
                {
                    releaseFirst.TrySetResult(true);
                    await producing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }

            async Task ProduceAsync()
            {
                for (uint sequenceNumber = 1; sequenceNumber <= kMessageCount; sequenceNumber++)
                {
                    await sut.OnPublishReceivedAsync(BuildDataChangeMessage(sequenceNumber), null, [])
                        .ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task IngressBackpressureBlocksBeyondBoundedCapacityAsync()
        {
            var sut = new TestMessageProcessor(m_mockServices.Object, m_completion, m_telemetry)
            {
                Id = 3
            };
            await sut.Block.WaitAsync().ConfigureAwait(false);
            await using (sut.ConfigureAwait(false))
            {
                var writes = new List<Task>();
                for (uint sequence = 1; sequence <= 1024; sequence++)
                {
                    writes.Add(sut.OnPublishReceivedAsync(
                        new NotificationMessage { SequenceNumber = sequence },
                        null,
                        []).AsTask());
                }
                await Task.WhenAll(writes).ConfigureAwait(false);
                Task blocked = sut.OnPublishReceivedAsync(
                    new NotificationMessage { SequenceNumber = 1025 },
                    null,
                    []).AsTask();
                Assert.That(blocked.IsCompleted, Is.False);
                sut.Block.Release();
                await blocked.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ProcessMessageAsyncShouldRepublishMissingMessagesAsync()
        {
            // Arrange
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 2
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 1
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);

                m_mockServices
                    .Setup(c => c.RepublishAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<uint>(id => id == sut.Id),
                        It.Is<uint>(s => s == 2),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RepublishResponse
                    {
                        NotificationMessage = new NotificationMessage
                        {
                            SequenceNumber = 2,
                            NotificationData =
                            [
                                new ExtensionObject(new DataChangeNotification
                                {
                                    MonitoredItems =
                                    [
                                        new MonitoredItemNotification()
                                    ]
                                })
                            ]
                        }
                    })
                    .Verifiable(Times.Once);

                // Act
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 3,
                    NotificationData =
                    [
                        new ExtensionObject(new EventNotificationList
                        {
                            Events =
                            [
                                new EventFieldList()
                            ]
                        })
                    ]
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.EventNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                // Assert
                Assert.That(sut.EventNotificationReceived.IsSet, Is.True);
                Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.True);
                Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.True);

                m_mockServices.Verify();
            }
        }

        [Test]
        public async Task ProcessReceivedMessagesAsyncShouldProcessMessagesInOrderAsync()
        {
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            // Arrange
            NotificationMessage[] messages = [.. Enumerable.Range(2, 99).Select(i => new NotificationMessage
            {
                SequenceNumber = (uint)i
            })];

            Array.Reverse(messages);

            var lastNotification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 3,
                NotificationCallback = sequenceNumber =>
                {
                    if (sequenceNumber == 100)
                    {
                        lastNotification.TrySetResult(true);
                    }
                    return default;
                }
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.Block.WaitAsync().ConfigureAwait(false);
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 1u
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                foreach (NotificationMessage message in messages)
                {
                    await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                }
                sut.Block.Release();

                // Act
                await lastNotification.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(sut.ReceivedSequenceNumbers, Is.EqualTo(
                    Enumerable.Range(1, 100).Select(i => (uint)i)));
                Assert.That(sut.AvailableInRetransmissionQueue, Is.EqualTo(availableSequenceNumbers));
                Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.False);
                Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.True);
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(100));
            }
        }

        [Test]
        public async Task DuplicateSequenceNumberShouldNotRedispatchAsync()
        {
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 7
            };
            await using (sut.ConfigureAwait(false))
            {
                var message = new NotificationMessage
                {
                    SequenceNumber = 5,
                    NotificationData =
                    [
                        new ExtensionObject(new DataChangeNotification
                        {
                            MonitoredItems =
                            [
                                new MonitoredItemNotification()
                            ]
                        })
                    ]
                };

                // First arrival
                await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable)
                    .ConfigureAwait(false);
                await sut.DataChangeNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                int firstCount = sut.ReceivedSequenceNumbers.Count;
                Assert.That(firstCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(5));

                // Reset signaling
                sut.DataChangeNotificationReceived.Reset();

                // Same sequence number arriving again
                await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable)
                    .ConfigureAwait(false);
                // Give the processor a moment in case it tries to dispatch
                await Task.Delay(50).ConfigureAwait(false);

                // The duplicate should not re-fire the data-change handler.
                Assert.That(
                    sut.DataChangeNotificationReceived.IsSet, Is.False,
                    "Duplicate sequence number must not re-dispatch the notification");
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(5));
            }
        }

        [Test]
        public async Task KeepAliveInterleavedWithNotificationsAsync()
        {
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 5
            };
            await using (sut.ConfigureAwait(false))
            {
                // 1: notification
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 1,
                    NotificationData =
                    [
                        new ExtensionObject(new DataChangeNotification
                        {
                            MonitoredItems = [new MonitoredItemNotification()]
                        })
                    ]
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.DataChangeNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                // 2: keep-alive (no NotificationData)
                sut.KeepAliveNotificationReceived.Reset();
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 2
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.KeepAliveNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                // 3: another notification
                sut.DataChangeNotificationReceived.Reset();
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 3,
                    NotificationData =
                    [
                        new ExtensionObject(new DataChangeNotification
                        {
                            MonitoredItems = [new MonitoredItemNotification()]
                        })
                    ]
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.DataChangeNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                // All three sequence numbers should appear in order.
                Assert.That(sut.ReceivedSequenceNumbers,
                    Is.EqualTo(new uint[] { 1, 2, 3 }));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(3));
            }
        }

        [Test]
        public async Task EmptyNotificationDataIsTreatedAsKeepAliveAsync()
        {
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 9
            };
            await using (sut.ConfigureAwait(false))
            {
                // NotificationData is null/default.
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 7
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.KeepAliveNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.True);
                Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.False);
                Assert.That(sut.EventNotificationReceived.IsSet, Is.False);
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(7));
            }
        }

        [Test]
        public async Task RepublishFailureLogsButContinuesProcessingAsync()
        {
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 11
            };
            await using (sut.ConfigureAwait(false))
            {
                // First message at sequence 1.
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 1
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);

                // Republish for the gap (sequence 2) returns an error.
                m_mockServices
                    .Setup(c => c.RepublishAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<uint>(id => id == sut.Id),
                        It.Is<uint>(s => s == 2),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new ServiceResultException(
                        StatusCodes.BadMessageNotAvailable))
                    .Verifiable(Times.AtLeastOnce);

                // Skip to sequence 3 to force the gap.
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 3,
                    NotificationData =
                    [
                        new ExtensionObject(new EventNotificationList
                        {
                            Events = [new EventFieldList()]
                        })
                    ]
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.EventNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                // Even though republish failed, the next message must still
                // be dispatched.
                Assert.That(sut.EventNotificationReceived.IsSet, Is.True);
                m_mockServices.Verify();
            }
        }

        [Test]
        public async Task SequenceNumberWraparoundAdvancesLastProcessedAsync()
        {
            // Per OPC UA Part 4 §7.30.5, sequence numbers wrap from
            // uint.MaxValue to 1 (skipping 0). The processor must accept
            // this wrap as forward progress, not silently drop the
            // post-wrap message as "old".
            var availableSequenceNumbers = new List<uint>();
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 13
            };
            await using (sut.ConfigureAwait(false))
            {
                // Republish for the first message's "missing" gap (we start
                // from LastSequenceNumberProcessed=0 so no gap is computed
                // for this first one) — but for the second message, the gap
                // is empty because we wrap directly from uint.MaxValue to 1.
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = uint.MaxValue
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.KeepAliveNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(sut.LastSequenceNumberProcessed,
                    Is.EqualTo(uint.MaxValue));

                sut.KeepAliveNotificationReceived.Reset();
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 1
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.KeepAliveNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.True,
                    "Wrapped sequence number must be accepted as forward progress, " +
                    "not dropped as duplicate/old.");
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ReceivingTransferStatusUpdateShouldUpdatePublishStateAsync()
        {
            // Arrange
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 3
            };
            await using (sut.ConfigureAwait(false))
            {
                // Act
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 3,
                    NotificationData =
                    [
                        new ExtensionObject(new StatusChangeNotification
                        {
                            Status = StatusCodes.GoodSubscriptionTransferred
                        })
                    ]
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.StatusChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                // Assert
                Assert.That(sut.StatusChangeNotificationReceived.IsSet, Is.True);
                Assert.That(sut.ReceivedSequenceNumbers, Does.Contain(3));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(3));
                Assert.That(sut.PublishState, Is.EqualTo(PublishState.Transferred));

                sut.StatusChangeNotificationReceived.Reset();

                // Act
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 4,
                    NotificationData =
                    [
                        new ExtensionObject(new StatusChangeNotification
                        {
                            Status = StatusCodes.BadTimeout
                        })
                    ]
                }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.StatusChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                // Assert
                Assert.That(sut.StatusChangeNotificationReceived.IsSet, Is.True);
                Assert.That(sut.ReceivedSequenceNumbers, Does.Contain(4));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(4));
                Assert.That(sut.PublishState, Is.EqualTo(PublishState.Timeout));
            }
        }

        /// <summary>
        /// V2 equivalent of the classic-engine regression captured by
        /// <c>OldMessagesAbandonedAfterRepublishTimeoutInSequentialModeAsync</c>
        /// (currently <c>[Explicit]</c> in <c>Classic/SubscriptionUnitTests.cs</c>).
        /// </summary>
        /// <remarks>
        /// Mirrors the classic test's exact arrival order — <c>[3]</c>,
        /// <c>[2]</c>, <c>[4]</c>, <c>[1]</c>, <c>[5]</c> — but queues all five
        /// while the worker is parked inside a sentinel keep-alive dispatch.
        /// The new <see cref="MessageProcessor"/> uses
        /// <see cref="System.Threading.Channels.Channel.CreateUnboundedPrioritized"/>
        /// keyed by <see cref="NotificationMessage.SequenceNumber"/>, so any
        /// items present in the channel at a single read are sorted by
        /// sequence number. With the consumer held, the five data messages
        /// land together and drain as <c>[1, 2, 3, 4, 5]</c> with no
        /// republishes — the reliability guarantee the classic engine fails
        /// to provide. (Without the sentinel, a fast consumer could read the
        /// first arrival before the rest queue, which is what the priority
        /// channel cannot recover from on its own.)
        /// </remarks>
        [Test]
        public async Task OutOfOrderArrivalsReorderedByPriorityChannelAsync()
        {
            var availableSequenceNumbers = new List<uint> { 1, 2, 3, 4, 5 };
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 11
            };
            await using (sut.ConfigureAwait(false))
            {
                // Arrange: drain the gate, then park the worker inside a
                // sentinel keep-alive dispatch so all five data messages
                // queue up in the priority channel before any are read.
                // The keep-alive does not advance the data dedup gate
                // (LastDataSequenceNumberProcessed) so the burst is still
                // processed as a fresh sequence starting at 1.
                sut.Block.Wait();
                await sut.OnPublishReceivedAsync(
                    new NotificationMessage { SequenceNumber = 1u },
                    availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.KeepAliveNotificationReceived.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                foreach (uint seq in new uint[] { 3, 2, 4, 1, 5 })
                {
                    await sut.OnPublishReceivedAsync(BuildDataChangeMessage(seq),
                        availableSequenceNumbers, stringTable).ConfigureAwait(false);
                }
                sut.Block.Release();

                // Act
                await WaitForLastSeqNumberAsync(sut, 5).ConfigureAwait(false);

                // Assert: the first entry is the sentinel keep-alive; the
                // remaining entries must be the data burst in strict
                // sequence-number order.
                Assert.That(sut.ReceivedSequenceNumbers,
                    Has.Count.EqualTo(6));
                Assert.That(sut.ReceivedSequenceNumbers[0], Is.EqualTo(1u),
                    "first entry should be the sentinel keep-alive");
                Assert.That(sut.ReceivedSequenceNumbers.Skip(1),
                    Is.EqualTo(new uint[] { 1, 2, 3, 4, 5 }));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(5));
                Assert.That(sut.MissingMessageCount, Is.Zero);
                Assert.That(sut.RepublishMessageCount, Is.Zero);
                m_mockServices.Verify(
                    c => c.RepublishAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<uint>(),
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        /// <summary>
        /// V2 equivalent of the classic-engine regression captured by
        /// <c>OldMessagesAbandonedAfterRepublishTimeoutInSequentialModeAsync</c>,
        /// stressed in serial timing instead of as a burst.
        /// </summary>
        /// <remarks>
        /// Same arrival order — <c>[3]</c>, <c>[2]</c>, <c>[4]</c>, <c>[1]</c>,
        /// <c>[5]</c> — but each forward-progressing push waits for delivery
        /// before the next is queued. The V2 engine's monotonic dedup gate
        /// (<c>LastDataSequenceNumberProcessed</c>) silently discards the
        /// stale <c>[2]</c> and <c>[1]</c> instead of re-inserting them
        /// out of order; <c>[3, 4, 5]</c> are delivered in strict ascending
        /// order with no republishes.
        /// </remarks>
        [Test]
        public async Task LateArrivalsDiscardedAfterDedupGateAdvancedAsync()
        {
            var availableSequenceNumbers = new List<uint>();
            var stringTable = new List<string> { "test" };

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 12
            };
            await using (sut.ConfigureAwait(false))
            {
                // [3]: first data message after create. The empty
                // AvailableInRetransmissionQueue suppresses the
                // first-after-create speculative republish path, so [3]
                // goes straight to the handler.
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(3),
                    availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await WaitForLastSeqNumberAsync(sut, 3).ConfigureAwait(false);

                // [2] is strictly behind LastDataSeq=3. Queue [2] then [4]
                // back-to-back; the priority channel picks [2] first
                // (lowest SequenceNumber) so the worker reads [2], discards
                // it silently, then reads [4] and delivers it. Waiting for
                // LastSeq == 4 confirms both happened in that order.
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(2),
                    availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(4),
                    availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await WaitForLastSeqNumberAsync(sut, 4).ConfigureAwait(false);

                // Same shape for [1] (even staler) followed by [5].
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(1),
                    availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(5),
                    availableSequenceNumbers, stringTable).ConfigureAwait(false);
                await WaitForLastSeqNumberAsync(sut, 5).ConfigureAwait(false);

                // Assert: only forward-progressing messages were delivered,
                // in strict order; the late [2] and [1] were discarded.
                Assert.That(sut.ReceivedSequenceNumbers,
                    Is.EqualTo(new uint[] { 3, 4, 5 }));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(5));
                Assert.That(sut.MissingMessageCount, Is.Zero);
                Assert.That(sut.RepublishMessageCount, Is.Zero);
                m_mockServices.Verify(
                    c => c.RepublishAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<uint>(),
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        /// <summary>
        /// A transferred subscription that stays quiet must still recover the
        /// notifications the server kept in its retransmission queue: the
        /// gap-walk republish only runs when a new data message arrives, so
        /// the transfer itself has to drive the recovery.
        /// </summary>
        [Test]
        public async Task RecoverTransferredMessagesRepublishesWithoutNewNotificationsAsync()
        {
            SetupRepublish();

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 21
            };
            await using (sut.ConfigureAwait(false))
            {
                // Act: transfer reports three messages still in the server
                // retransmission queue, reported out of order.
                await sut.RecoverTransferredMessagesAsync([4, 2, 3], default)
                    .ConfigureAwait(false);

                // Assert: all three are recovered, in ascending order, and
                // flagged as republished notifications.
                Assert.That(sut.ReceivedSequenceNumbers,
                    Is.EqualTo(new uint[] { 2, 3, 4 }));
                Assert.That(sut.PublishState, Is.EqualTo(PublishState.Republish));
                Assert.That(sut.RepublishMessageCount, Is.EqualTo(3));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(4));
                Assert.That(sut.AvailableInRetransmissionQueue, Is.Empty,
                    "transfer recovery must not publish its local snapshot");
                await m_completion.WaitForQueuedAckAsync(3).ConfigureAwait(false);

                // The dedup gate advanced past the recovered messages, so the
                // next publish is not mistaken for a gap and nothing is
                // republished a second time.
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(5),
                    [4, 2, 3], []).ConfigureAwait(false);
                await WaitForLastSeqNumberAsync(sut, 5).ConfigureAwait(false);

                Assert.That(sut.ReceivedSequenceNumbers,
                    Is.EqualTo(new uint[] { 2, 3, 4, 5 }));
                Assert.That(sut.MissingMessageCount, Is.Zero);
                Assert.That(sut.RepublishMessageCount, Is.EqualTo(3));
            }
        }

        [Test]
        public async Task RecoverTransferredMessagesPreservesNewerAvailableSetAsync()
        {
            int republishCalls = 0;
            var firstRepublishStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstRepublish = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            m_mockServices
                .Setup(c => c.RepublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader _, uint _, uint sequenceNumber,
                    CancellationToken _) =>
                {
                    if (Interlocked.Increment(ref republishCalls) == 1)
                    {
                        firstRepublishStarted.SetResult(true);
                        await releaseFirstRepublish.Task
                            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }

                    return new RepublishResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        NotificationMessage = BuildDataChangeMessage(sequenceNumber)
                    };
                });

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 25
            };
            await using (sut.ConfigureAwait(false))
            {
                Task recoverTask = sut.RecoverTransferredMessagesAsync([7, 8], default)
                    .AsTask();

                await firstRepublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await sut.OnPublishReceivedAsync(
                    new NotificationMessage { SequenceNumber = 20 },
                    [11, 12],
                    []);

                Assert.That(sut.AvailableInRetransmissionQueue,
                    Is.EqualTo(new uint[] { 11, 12 }));

                releaseFirstRepublish.SetResult(true);
                await recoverTask.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.That(sut.AvailableInRetransmissionQueue,
                    Is.EqualTo(new uint[] { 11, 12 }));
                Assert.That(sut.RepublishMessageCount, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task RecoverTransferredMessagesWithoutAvailableSequenceNumbersDoesNothingAsync()
        {
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 22
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.RecoverTransferredMessagesAsync([], default)
                    .ConfigureAwait(false);

                Assert.That(sut.ReceivedSequenceNumbers, Is.Empty);
                Assert.That(sut.RepublishMessageCount, Is.Zero);
                Assert.That(sut.LastSequenceNumberProcessed, Is.Zero);
                m_mockServices.Verify(
                    c => c.RepublishAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<uint>(),
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        /// <summary>
        /// Sequence numbers wrap from <see cref="uint.MaxValue"/> to 1
        /// (Part 4 §7.30.5), so a retransmission queue spanning the wrap must
        /// still be recovered oldest-first.
        /// </summary>
        [Test]
        public async Task RecoverTransferredMessagesOrdersAcrossSequenceNumberWrapAsync()
        {
            SetupRepublish();

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 23
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.RecoverTransferredMessagesAsync(
                    [3, uint.MaxValue - 5, 5], default)
                    .ConfigureAwait(false);

                Assert.That(sut.ReceivedSequenceNumbers,
                    Is.EqualTo(new uint[]
                    {
                        uint.MaxValue - 5, 3, 5
                    }));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(5));
            }
        }

        [Test]
        public async Task RecoverTransferredMessagesAdvancesGateWhenRepublishFailsAsync()
        {
            m_mockServices
                .Setup(c => c.RepublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(
                    StatusCodes.BadMessageNotAvailable));

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 24
            };
            await using (sut.ConfigureAwait(false))
            {
                // A server that dropped the messages must not fail the
                // recovery - the gate still advances past the sequence
                // numbers the server reported as sent.
                await sut.RecoverTransferredMessagesAsync([7, 8], default)
                    .ConfigureAwait(false);

                Assert.That(sut.ReceivedSequenceNumbers, Is.Empty);
                Assert.That(sut.RepublishMessageCount, Is.EqualTo(2));
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(8));

                // The next message is therefore delivered without a gap walk.
                await sut.OnPublishReceivedAsync(BuildDataChangeMessage(9),
                    [], []).ConfigureAwait(false);
                await WaitForLastSeqNumberAsync(sut, 9).ConfigureAwait(false);

                Assert.That(sut.ReceivedSequenceNumbers,
                    Is.EqualTo(new uint[] { 9 }));
                Assert.That(sut.MissingMessageCount, Is.Zero);
            }
        }

        [Test]
        public async Task RecoverTransferredMessagesPropagatesCancellationDuringRepublishCallbackAsync()
        {
            SetupRepublish();

            using var cts = new CancellationTokenSource();
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 26
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.Block.WaitAsync();
                bool blockHeld = true;
                try
                {
                    Task recoverTask = sut.RecoverTransferredMessagesAsync([10], cts.Token)
                        .AsTask();

                    await sut.DataChangeNotificationReceived.WaitAsync()
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    cts.Cancel();
                    sut.Block.Release();
                    blockHeld = false;

                    Assert.ThrowsAsync<OperationCanceledException>(
                        async () => await recoverTask.ConfigureAwait(false));
                    Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(10));
                    Assert.That(sut.RepublishMessageCount, Is.EqualTo(1));
                }
                finally
                {
                    if (blockHeld)
                    {
                        sut.Block.Release();
                    }
                }
            }
        }

        private void SetupRepublish()
        {
            m_mockServices
                .Setup(c => c.RepublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, uint _, uint sequenceNumber,
                    CancellationToken _) => new RepublishResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        NotificationMessage = BuildDataChangeMessage(sequenceNumber)
                    });
        }

        private static NotificationMessage BuildNotificationMessage(string kind, uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData = kind switch
                {
                    "data" => [new ExtensionObject(new DataChangeNotification())],
                    "event" => [new ExtensionObject(new EventNotificationList())],
                    "status" => [new ExtensionObject(new StatusChangeNotification { Status = StatusCodes.Good })],
                    "keepalive" => [],
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                }
            };
        }

        private static NotificationMessage BuildDataChangeMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData =
                [
                    new ExtensionObject(new DataChangeNotification
                    {
                        MonitoredItems =
                        [
                            new MonitoredItemNotification()
                        ]
                    })
                ]
            };
        }

        private static async Task WaitForLastSeqNumberAsync(
            TestMessageProcessor sut,
            uint expected,
            int timeoutSeconds = 5)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
            while (sut.LastSequenceNumberProcessed < expected)
            {
                if (DateTimeOffset.UtcNow > deadline)
                {
                    Assert.Fail(
                        $"Timeout: LastSequenceNumberProcessed = {sut.LastSequenceNumberProcessed}, " +
                        $"expected = {expected}, received = " +
                        $"[{string.Join(",", sut.ReceivedSequenceNumbers)}]");
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private sealed class TestMessageProcessor : MessageProcessor
        {
            public TestMessageProcessor(ISubscriptionServiceSetClientMethods session,
                IMessageAckQueue completion, ITelemetryContext telemetry)
                : base(session, completion, telemetry)
            {
            }

            public new IReadOnlyList<uint> AvailableInRetransmissionQueue
            {
                get => base.AvailableInRetransmissionQueue;
                set => base.AvailableInRetransmissionQueue = value;
            }

            public SemaphoreSlim Block { get; } = new(1, 1);

            public AsyncManualResetEvent DataChangeNotificationReceived { get; } = new();

            public AsyncManualResetEvent EventNotificationReceived { get; } = new();

            public AsyncManualResetEvent KeepAliveNotificationReceived { get; } = new();

            public new uint LastSequenceNumberProcessed
            {
                get => base.LastSequenceNumberProcessed;
                set => base.LastSequenceNumberProcessed = value;
            }

            public PublishState PublishState { get; set; }
            public List<uint> ReceivedSequenceNumbers { get; } = [];
            public AsyncManualResetEvent StatusChangeNotificationReceived { get; } = new();
            public DeferredCallbackMode DeferredCallbackMode { get; init; }
            public TaskCompletionSource<bool> CallbackReturned { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<bool> DeferredDispatchGuard { get; private set; } =
                Task.FromResult(false);
            public bool IsDispatchingForTest => IsDispatchingNotification;
            public Func<uint, ValueTask>? NotificationCallback { get; set; }

            private void StartDeferredProbe()
            {
                DeferredDispatchGuard = Task.Run(async () =>
                {
                    await CallbackReturned.Task.ConfigureAwait(false);
                    return IsDispatchingForTest;
                });
            }

            public new ValueTask RecoverTransferredMessagesAsync(
                IReadOnlyList<uint> availableSequenceNumbers, CancellationToken ct)
            {
                return base.RecoverTransferredMessagesAsync(
                    availableSequenceNumbers, ct);
            }

            public async ValueTask WaitAsync()
            {
                await Block.WaitAsync().ConfigureAwait(false);
                Block.Release();
            }

            public ValueTask ResetGenerationAsync()
            {
                return ResetMessageGenerationAsync(_ => default, _ => default, CancellationToken.None);
            }

            private async ValueTask InvokeNotificationAsync(uint sequenceNumber)
            {
                if (NotificationCallback != null)
                {
                    await NotificationCallback(sequenceNumber).ConfigureAwait(false);
                }
                await WaitAsync().ConfigureAwait(false);
            }

            protected override ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
                DateTime publishTime, DataChangeNotification notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
                DataChangeNotificationReceived.Set();
                if (DeferredCallbackMode == DeferredCallbackMode.Data)
                {
                    StartDeferredProbe();
                }
                return InvokeNotificationAsync(sequenceNumber);
            }

            protected override ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
                DateTime publishTime, EventNotificationList notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
                EventNotificationReceived.Set();
                if (DeferredCallbackMode == DeferredCallbackMode.Event)
                {
                    StartDeferredProbe();
                }
                return InvokeNotificationAsync(sequenceNumber);
            }

            protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
                                        DateTime publishTime, PublishState publishStateMask)
            {
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
                KeepAliveNotificationReceived.Set();
                return InvokeNotificationAsync(sequenceNumber);
            }

            protected override void OnPublishStateChanged(PublishState stateMask)
            {
                PublishState = stateMask;
                base.OnPublishStateChanged(stateMask);
            }

            protected override async ValueTask OnStatusChangeNotificationAsync(uint sequenceNumber,
                DateTime publishTime, StatusChangeNotification notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
                StatusChangeNotificationReceived.Set();
                if (DeferredCallbackMode == DeferredCallbackMode.Status)
                {
                    StartDeferredProbe();
                }
                await InvokeNotificationAsync(sequenceNumber).ConfigureAwait(false);
            }
        }

        private enum DeferredCallbackMode
        {
            None,
            Data,
            Event,
            Status
        }

        private FakeMessageAckQueue m_completion;
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionServiceSetClientMethods> m_mockServices;
    }
}
