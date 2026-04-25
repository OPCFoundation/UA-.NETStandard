#if OPCUA_CLIENT_V2
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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    [TestFixture]
    public sealed class MonitoredItemTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockContext = new Mock<IMonitoredItemContext>();
            m_options = OptionsFactory.Create<MonitoredItemOptions>();
            m_mockLogger = new Mock<ILogger<MonitoredItem>>();
        }

        [Test]
        public void ServerIdShouldGet()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
               m_options, m_mockLogger.Object);
            const uint serverId = 123u;

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                MonitoredItemId = serverId,
                RevisedSamplingInterval = 10000,
                RevisedQueueSize = 10
            }, 0, [], new ResponseHeader());

            // Assert
            Assert.That(sut.ServerId, Is.EqualTo(serverId));
            Assert.That(sut.Created, Is.True);
        }

        [Test]
        public void CreatedShouldReturnFalseWhenServerIdIsNotSet()
        {
            // Act
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
              m_options, m_mockLogger.Object);
            var result = sut.Created;

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void SetMonitoringModeShouldNotifyItemChangeResultWhenStatusCodeIsBad()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                MonitoringMode = MonitoringMode.Sampling,
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
                m_options, m_mockLogger.Object);

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetMonitoringModeResult(MonitoringMode.Sampling, StatusCodes.Bad,
                0, [], new ResponseHeader());

            // Assert
            m_mockContext.Verify(s => s.NotifyItemChangeResult(sut, 1,
                m_options.CurrentValue,
                It.Is<ServiceResult>(s => s.StatusCode == StatusCodes.Bad),
                false, null),
                Times.Once);
        }

        [Test]
        public void CreateShouldNotifyItemChangeResultWhenStatusCodeIsBad()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
               m_options, m_mockLogger.Object);

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Bad
            }, 0, [], new ResponseHeader());

            // Assert
            m_mockContext.Verify(s => s.NotifyItemChangeResult(sut, 1,
                m_options.CurrentValue,
                It.Is<ServiceResult>(s => s.StatusCode == StatusCodes.Bad),
                false, null),
                Times.Once);
        }

        [Test]
        public void CreateShouldNotifyItemChangeResultWithFilterResult()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
               m_options, m_mockLogger.Object);
            var filterResult = new EventFilterResult();

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                FilterResult = new ExtensionObject(filterResult)
            }, 0, [], new ResponseHeader());

            // Assert
            m_mockContext.Verify(s => s.NotifyItemChangeResult(sut, 0,
                m_options.CurrentValue, ServiceResult.Good, true,
                It.Is<MonitoringFilterResult>(o => Utils.IsEqual(o, filterResult))),
                Times.Once);
        }

        [Test]
        public void CurrentMonitoringModeShouldSetAndGet()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                MonitoringMode = MonitoringMode.Reporting,
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
                m_options, m_mockLogger.Object);

            Assert.That(sut.CurrentMonitoringMode, Is.Not.EqualTo(MonitoringMode.Sampling));

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good
            }, 0, [], new ResponseHeader());

            // Assert
            Assert.That(sut.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
        }

        [Test]
        public void CurrentMonitoringModeShouldUpdate()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                MonitoringMode = MonitoringMode.Sampling,
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
                m_options, m_mockLogger.Object);

            Assert.That(sut.CurrentMonitoringMode, Is.Not.EqualTo(MonitoringMode.Sampling));

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetMonitoringModeResult(MonitoringMode.Sampling, StatusCodes.Good,
                0, [], new ResponseHeader());

            // Assert
            Assert.That(sut.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
        }

        [Test]
        public void CurrentSamplingIntervalShouldSetAndGet()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
               m_options, m_mockLogger.Object);
            var currentSamplingInterval = TimeSpan.FromMilliseconds(500);

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                RevisedSamplingInterval = currentSamplingInterval.TotalMilliseconds,
                StatusCode = StatusCodes.Good
            }, 0, [], new ResponseHeader());

            // Assert
            Assert.That(sut.CurrentSamplingInterval, Is.EqualTo(currentSamplingInterval));
        }

        [Test]
        public void CurrentQueueSizeShouldSetAndGet()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
               m_options, m_mockLogger.Object);
            const uint currentQueueSize = 5u;

            // Act
            Assert.That(sut.TryGetPendingChange(out var change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                RevisedQueueSize = currentQueueSize,
                StatusCode = StatusCodes.Good
            }, 0, [], new ResponseHeader());

            // Assert
            Assert.That(sut.CurrentQueueSize, Is.EqualTo(currentQueueSize));
        }

        [Test]
        public void ClientHandleShouldGet()
        {
            // Act
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
               m_options, m_mockLogger.Object);
            var result = sut.ClientHandle;

            // Assert
            Assert.That(result, Is.Not.EqualTo(0));
        }

        [Test]
        public async Task DisposeShouldCallRemoveItemOnSubscriptionAsync()
        {
            // Act
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
              m_options, m_mockLogger.Object);
            await sut.DisposeAsync();

            // Assert
            m_mockContext.Verify(s => s.NotifyItemChange(sut, true), Times.Once);
        }

        [Test]
        public async Task DisposeCanBeCalledTwiceWithoutExceptionAsync()
        {
            // Act
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_mockContext.Object,
              m_options, m_mockLogger.Object);
            await sut.DisposeAsync();
            await sut.DisposeAsync();

            // Assert
            m_mockContext.Verify(s => s.NotifyItemChange(sut, true), Times.Once);
        }

        [Test]
        public void OnSubscriptionStateChangeShouldAdjustQueueSizeWhenAutoSetQueueSizeIsTrue()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out var change), Is.True);
            Assert.That(change?.Create, Is.Not.Null);
            Assert.That(change.Create.RequestedParameters.QueueSize, Is.EqualTo(6)); // (500 / 100) + 1 = 6
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenAutoSetQueueSizeIsFalse()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = false,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out var change), Is.False);
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenPublishingIntervalIsZero()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.Zero);

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out var change), Is.False);
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenSamplingIntervalIsZero()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.Zero
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out var change), Is.False);
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenSamplingIntervalIsNegative()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(-100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out var change), Is.False);
        }

        [Test]
        public void ToStringShouldReturnExpectedString()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode")
            });

            mockContext.Setup(c => c.ToString()).Returns("Test");
            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);

            // Act
            var result = monitoredItem.ToString();

            // Assert
            Assert.That(result, Is.EqualTo($"Test#{monitoredItem.ClientHandle}|0 (TestItem)"));
        }

        private sealed class TestMonitoredItem : MonitoredItem
        {
            public TestMonitoredItem(IMonitoredItemContext subscription,
                OptionsMonitor<MonitoredItemOptions> options, ILogger logger)
                : base(subscription, "TestItem", options, logger)
            {
                options.Configure(o => o with
                {
                    StartNodeId = new NodeId("test", 0)
                });
            }
        }

        private Mock<IMonitoredItemContext> m_mockContext;
        private OptionsMonitor<MonitoredItemOptions> m_options;
        private Mock<ILogger<MonitoredItem>> m_mockLogger;
    }
}
#endif
