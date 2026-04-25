#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Nito.AsyncEx;
using Opc.Ua.Client.Services;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions
{
    [TestFixture]
    public sealed class MessageProcessorTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockCompletion = new Mock<IMessageAckQueue>();
            m_mockObservability = new Mock<IV2TelemetryContext>();
            m_mockTimeProvider = new Mock<TimeProvider>();
            m_mockServices = new Mock<ISubscriptionServiceSet>();
            m_mockLogger = new Mock<ILogger<Subscription>>();
            m_mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(m_mockLogger.Object);
            m_mockObservability.Setup(o => o.TimeProvider).Returns(m_mockTimeProvider.Object);
        }

        [Test]
        public async Task DisposeAsyncShouldCompleteMessageWriterAndCancelTokenAsync()
        {
            // Arrange
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_mockCompletion.Object, m_mockObservability.Object)
            {
                Id = 3
            };
            m_mockCompletion
                .Setup(c => c.CompleteAsync(
                    It.Is<uint>(i => i == 3),
                    It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            await sut.DisposeAsync();

            // Assert
            Assert.That(sut.PublishState, Is.EqualTo(PublishState.Completed));
            m_mockCompletion.Verify();
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
            await using var sut = new TestMessageProcessor(m_mockServices.Object,
                m_mockCompletion.Object, m_mockObservability.Object)
            {
                Id = 3
            };

            // Act
            await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable);
            await sut.KeepAliveNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1));

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
            await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable);
            await sut.DataChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.That(sut.AvailableInRetransmissionQueue, Is.EqualTo(availableSequenceNumbers));
            Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.True);
            Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.False);
            Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(4));
        }

        [Test]
        public async Task ProcessMessageAsyncShouldRepublishMissingMessagesAsync()
        {
            // Arrange
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            await using var sut = new TestMessageProcessor(m_mockServices.Object,
                m_mockCompletion.Object, m_mockObservability.Object)
            {
                Id = 2
            };

            await sut.OnPublishReceivedAsync(new NotificationMessage
            {
                SequenceNumber = 1
            }, availableSequenceNumbers, stringTable);

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
            }, availableSequenceNumbers, stringTable);
            await sut.EventNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.That(sut.EventNotificationReceived.IsSet, Is.True);
            Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.True);
            Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.True);

            m_mockServices.Verify();
        }

        [Test]
        public async Task ProcessReceivedMessagesAsyncShouldProcessMessagesInOrderAsync()
        {
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            // Arrange
            var messages = Enumerable.Range(2, 99).Select(i => new NotificationMessage
            {
                SequenceNumber = (uint)i
            }).ToArray();

#pragma warning disable CA5394 // Do not use insecure randomness
            Random.Shared.Shuffle(messages);
#pragma warning restore CA5394 // Do not use insecure randomness

            await using var sut = new TestMessageProcessor(m_mockServices.Object,
                m_mockCompletion.Object, m_mockObservability.Object)
            {
                Id = 3
            };

            sut.Block.Wait();
            await sut.OnPublishReceivedAsync(new NotificationMessage
            {
                SequenceNumber = 1u
            }, availableSequenceNumbers, stringTable);
            foreach (var message in messages)
            {
                await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable);
            }
            sut.Block.Release();

            // Act
            await Task.Delay(10);

            Assert.That(sut.ReceivedSequenceNumbers, Is.EqualTo(
                Enumerable.Range(1, 100).Select(i => (uint)i)));
            Assert.That(sut.AvailableInRetransmissionQueue, Is.EqualTo(availableSequenceNumbers));
            Assert.That(sut.DataChangeNotificationReceived.IsSet, Is.False);
            Assert.That(sut.KeepAliveNotificationReceived.IsSet, Is.True);
            Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(100));
        }
        [Test]
        public async Task ReceivingTransferStatusUpdateShouldUpdatePublishStateAsync()
        {
            // Arrange
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };
            await using var sut = new TestMessageProcessor(m_mockServices.Object,
                m_mockCompletion.Object, m_mockObservability.Object)
            {
                Id = 3
            };

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
            }, availableSequenceNumbers, stringTable);
            await sut.StatusChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1));

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
            }, availableSequenceNumbers, stringTable);
            await sut.StatusChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.That(sut.StatusChangeNotificationReceived.IsSet, Is.True);
            Assert.That(sut.ReceivedSequenceNumbers, Does.Contain(4));
            Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(4));
            Assert.That(sut.PublishState, Is.EqualTo(PublishState.Timeout));
        }

        private sealed class TestMessageProcessor : MessageProcessor
        {
            public TestMessageProcessor(ISubscriptionServiceSet session,
                IMessageAckQueue completion, IV2TelemetryContext telemetry)
                : base(session, completion, telemetry)
            {
            }

            public IReadOnlyList<uint> AvailableInRetransmissionQueue
            {
                get => _availableInRetransmissionQueue;
                set => _availableInRetransmissionQueue = value;
            }
            public SemaphoreSlim Block { get; } = new(1, 1);

            public AsyncManualResetEvent DataChangeNotificationReceived { get; } = new();

            public AsyncManualResetEvent EventNotificationReceived { get; } = new();

            public AsyncManualResetEvent KeepAliveNotificationReceived { get; } = new();

            public uint LastSequenceNumberProcessed
            {
                get => _lastSequenceNumberProcessed;
                set => _lastSequenceNumberProcessed = value;
            }
            public PublishState PublishState { get; set; }
            public List<uint> ReceivedSequenceNumbers { get; } = [];
            public AsyncManualResetEvent StatusChangeNotificationReceived { get; } = new();
            public async ValueTask WaitAsync()
            {
                await Block.WaitAsync();
                Block.Release();
            }
            protected override ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
                DateTime publishTime, DataChangeNotification notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                DataChangeNotificationReceived.Set();
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
                return WaitAsync();
            }

            protected override ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
                DateTime publishTime, EventNotificationList notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                EventNotificationReceived.Set();
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
                return WaitAsync();
            }

            protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
                                        DateTime publishTime, PublishState publishStateMask)
            {
                KeepAliveNotificationReceived.Set();
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
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
                StatusChangeNotificationReceived.Set();
                ReceivedSequenceNumbers.Add(sequenceNumber);
                if (publishStateMask != PublishState.None)
                {
                    PublishState = publishStateMask;
                }
                await WaitAsync();
            }
        }
        private Mock<IMessageAckQueue> m_mockCompletion;
        private Mock<ILogger<Subscription>> m_mockLogger;
        private Mock<IV2TelemetryContext> m_mockObservability;
        private Mock<ISubscriptionServiceSet> m_mockServices;
        private Mock<TimeProvider> m_mockTimeProvider;
    }
}
#endif
