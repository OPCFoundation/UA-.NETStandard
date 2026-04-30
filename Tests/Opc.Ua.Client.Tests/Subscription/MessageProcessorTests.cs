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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Nito.AsyncEx;
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
            m_mockObservability = new Mock<ITelemetryContext>();
            m_mockServices = new Mock<ISubscriptionServiceSetClientMethods>();
            m_mockLogger = new Mock<ILogger<Subscription>>();
            m_mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(m_mockLogger.Object);
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
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);

            // Act
            await sut.DisposeAsync().ConfigureAwait(false);

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
            await sut.OnPublishReceivedAsync(message, availableSequenceNumbers, stringTable).ConfigureAwait(false);
            await sut.KeepAliveNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

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
            await sut.DataChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

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
            await sut.EventNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

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
            NotificationMessage[] messages = Enumerable.Range(2, 99).Select(i => new NotificationMessage
            {
                SequenceNumber = (uint)i
            }).ToArray();

            UnsecureRandom.Shared.Shuffle(messages);

            await using var sut = new TestMessageProcessor(m_mockServices.Object,
                m_mockCompletion.Object, m_mockObservability.Object)
            {
                Id = 3
            };

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
            }, availableSequenceNumbers, stringTable).ConfigureAwait(false);
            await sut.StatusChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

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
            await sut.StatusChangeNotificationReceived.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            // Assert
            Assert.That(sut.StatusChangeNotificationReceived.IsSet, Is.True);
            Assert.That(sut.ReceivedSequenceNumbers, Does.Contain(4));
            Assert.That(sut.LastSequenceNumberProcessed, Is.EqualTo(4));
            Assert.That(sut.PublishState, Is.EqualTo(PublishState.Timeout));
        }

        private sealed class TestMessageProcessor : MessageProcessor
        {
            public TestMessageProcessor(ISubscriptionServiceSetClientMethods session,
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
                await Block.WaitAsync().ConfigureAwait(false);
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
                await WaitAsync().ConfigureAwait(false);
            }
        }
        private Mock<IMessageAckQueue> m_mockCompletion;
        private Mock<ILogger<Subscription>> m_mockLogger;
        private Mock<ITelemetryContext> m_mockObservability;
        private Mock<ISubscriptionServiceSetClientMethods> m_mockServices;
    }
}
