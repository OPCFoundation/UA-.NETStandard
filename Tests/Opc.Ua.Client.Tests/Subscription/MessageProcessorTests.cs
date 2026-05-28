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

                // Assert
                Assert.That(sut.AvailableInRetransmissionQueue, Is.EqualTo(availableSequenceNumbers));
                Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.True);
                Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.False);
                Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(4));
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

            UnsecureRandom.Shared.Shuffle(messages);

            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 3
            };
            await using (sut.ConfigureAwait(false))
            {
                sut.Block.Wait();
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
                await Task.Delay(10).ConfigureAwait(false);

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
        /// while the worker is blocked. The new <see cref="MessageProcessor"/>
        /// uses <see cref="System.Threading.Channels.Channel.CreateUnboundedPrioritized"/>
        /// keyed by <see cref="NotificationMessage.SequenceNumber"/>, so the
        /// burst is transparently re-ordered and drained as
        /// <c>[1, 2, 3, 4, 5]</c> with no republishes — the reliability
        /// guarantee the classic engine fails to provide.
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
                // Arrange: pin the worker on the first delivery so all five
                // messages queue up under the priority channel before any
                // are drained.
                sut.Block.Wait();
                foreach (uint seq in new uint[] { 3, 2, 4, 1, 5 })
                {
                    await sut.OnPublishReceivedAsync(BuildDataChangeMessage(seq),
                        availableSequenceNumbers, stringTable).ConfigureAwait(false);
                }
                sut.Block.Release();

                // Act
                await WaitForLastSeqNumberAsync(sut, 5).ConfigureAwait(false);

                // Assert
                Assert.That(sut.ReceivedSequenceNumbers,
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

            public async ValueTask WaitAsync()
            {
                await Block.WaitAsync().ConfigureAwait(false);
                Block.Release();
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
                return WaitAsync();
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
                return WaitAsync();
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
                return WaitAsync();
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
                await WaitAsync().ConfigureAwait(false);
            }
        }

        private FakeMessageAckQueue m_completion;
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionServiceSetClientMethods> m_mockServices;
    }
}
