// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Nito.AsyncEx;
    using Opc.Ua.Client.Services;
    using Xunit;

    public sealed class MessageProcessorTests
    {
        public MessageProcessorTests()
        {
            _mockCompletion = new Mock<IMessageAckQueue>();
            _mockObservability = new Mock<ITelemetryContext>();
            _mockTimeProvider = new Mock<TimeProvider>();
            _mockServices = new Mock<ISubscriptionServiceSet>();
            _mockLogger = new Mock<ILogger<Subscription>>();
            _mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(_mockLogger.Object);
            _mockObservability.Setup(o => o.TimeProvider).Returns(_mockTimeProvider.Object);
        }

        [Fact]
        public async Task DisposeAsyncShouldCompleteMessageWriterAndCancelTokenAsync()
        {
            // Arrange
            var sut = new TestMessageProcessor(_mockServices.Object,
                _mockCompletion.Object, _mockObservability.Object)
            {
                Id = 3
            };
            _mockCompletion
                .Setup(c => c.CompleteAsync(
                    It.Is<uint>(i => i == 3),
                    It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            await sut.DisposeAsync();

            // Assert
            sut.PublishState.Should().Be(PublishState.Completed);
            _mockCompletion.Verify();
        }

        [Fact]
        public async Task OnPublishReceivedKeepAliveShouldDispatchKeepAliveAsync()
        {
            // Arrange
            var message = new NotificationMessage
            {
                SequenceNumber = 3
            };
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };
            await using var sut = new TestMessageProcessor(_mockServices.Object,
                _mockCompletion.Object, _mockObservability.Object)
            {
                Id = 3
            };

            // Act
            await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable);
            await sut.KeepAliveNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1));

            // Assert
            sut.KeepAliveNotificationReceived.IsSet.Should().BeTrue();
            sut.AvailableInRetransmissionQueue.Should().BeEquivalentTo(availableSequenceNumbers);
            sut.LastSequenceNumberProcessed.Should().Be(3);
            sut.DataChangeNotificationReceived.IsSet.Should().BeFalse();

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
            sut.AvailableInRetransmissionQueue.Should().BeEquivalentTo(availableSequenceNumbers);
            sut.DataChangeNotificationReceived.IsSet.Should().BeTrue();
            sut.KeepAliveNotificationReceived.IsSet.Should().BeFalse();
            sut.LastSequenceNumberProcessed.Should().Be(4);
        }

        [Fact]
        public async Task ProcessMessageAsyncShouldRepublishMissingMessagesAsync()
        {
            // Arrange
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };

            await using var sut = new TestMessageProcessor(_mockServices.Object,
                _mockCompletion.Object, _mockObservability.Object)
            {
                Id = 2
            };

            await sut.OnPublishReceivedAsync(new NotificationMessage
            {
                SequenceNumber = 1
            }, availableSequenceNumbers, stringTable);

            _mockServices
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
            sut.EventNotificationReceived.IsSet.Should().BeTrue();
            sut.KeepAliveNotificationReceived.IsSet.Should().BeTrue();
            sut.DataChangeNotificationReceived.IsSet.Should().BeTrue();

            _mockServices.Verify();
        }

        [Fact]
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

            await using var sut = new TestMessageProcessor(_mockServices.Object,
                _mockCompletion.Object, _mockObservability.Object)
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

            sut.ReceivedSequenceNumbers.Should().BeEquivalentTo(
                Enumerable.Range(1, 100).Select(i => (uint)i));
            sut.AvailableInRetransmissionQueue.Should().BeEquivalentTo(availableSequenceNumbers);
            sut.DataChangeNotificationReceived.IsSet.Should().BeFalse();
            sut.KeepAliveNotificationReceived.IsSet.Should().BeTrue();
            sut.LastSequenceNumberProcessed.Should().Be(100);
        }
        [Fact]
        public async Task ReceivingTransferStatusUpdateShouldUpdatePublishStateAsync()
        {
            // Arrange
            var availableSequenceNumbers = new List<uint> { 1, 2, 3 };
            var stringTable = new List<string> { "test" };
            await using var sut = new TestMessageProcessor(_mockServices.Object,
                _mockCompletion.Object, _mockObservability.Object)
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
            sut.StatusChangeNotificationReceived.IsSet.Should().BeTrue();
            sut.ReceivedSequenceNumbers.Should().Contain(3);
            sut.LastSequenceNumberProcessed.Should().Be(3);
            sut.PublishState.Should().Be(PublishState.Transferred);

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
            sut.StatusChangeNotificationReceived.IsSet.Should().BeTrue();
            sut.ReceivedSequenceNumbers.Should().Contain(4);
            sut.LastSequenceNumberProcessed.Should().Be(4);
            sut.PublishState.Should().Be(PublishState.Timeout);
        }

        private sealed class TestMessageProcessor : MessageProcessor
        {
            public TestMessageProcessor(ISubscriptionServiceSet session,
                IMessageAckQueue completion, ITelemetryContext telemetry)
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
        private readonly Mock<IMessageAckQueue> _mockCompletion;
        private readonly Mock<ILogger<Subscription>> _mockLogger;
        private readonly Mock<ITelemetryContext> _mockObservability;
        private readonly Mock<ISubscriptionServiceSet> _mockServices;
        private readonly Mock<TimeProvider> _mockTimeProvider;
    }
}
