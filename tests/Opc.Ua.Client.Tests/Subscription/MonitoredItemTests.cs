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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    [TestFixture]
    [Category("Client")]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class MonitoredItemTests
    {
        [SetUp]
        public void SetUp()
        {
            m_context = new FakeMonitoredItemContext();
            m_options = OptionsFactory.Create<MonitoredItemOptions>();
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ServerIdShouldGet()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
               m_options, m_telemetry.CreateLogger("MonitoredItem"));
            const uint serverId = 123u;

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
            Assert.That(change, Is.Not.Null);
            Assert.That(((IMonitoredItemApplyState)sut).HasPendingChanges, Is.True);
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
            Assert.That(((IMonitoredItemApplyState)sut).HasPendingChanges, Is.False);
        }

        [Test]
        public void CreatedShouldReturnFalseWhenServerIdIsNotSet()
        {
            // Act
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
              m_options, m_telemetry.CreateLogger("MonitoredItem"));
            bool result = sut.Created;

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
            var sut = new TestMonitoredItem(m_context,
                m_options, m_telemetry.CreateLogger("MonitoredItem"));

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
            Assert.That(change, Is.Not.Null);
            change.SetMonitoringModeResult(MonitoringMode.Sampling, StatusCodes.Bad,
                0, [], new ResponseHeader());

            // Assert
            Assert.That(m_context.NotifyItemChangeResultCalls
                .Count(c => c.MonitoredItem == sut &&
                    c.RetryCount == 1 &&
                    c.Source == m_options.CurrentValue &&
                    c.ServiceResult.StatusCode == StatusCodes.Bad &&
                    !c.Final &&
                    c.FilterResult == null), Is.EqualTo(1));
        }

        [Test]
        public void CreateShouldNotifyItemChangeResultWhenStatusCodeIsBad()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
               m_options, m_telemetry.CreateLogger("MonitoredItem"));

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
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
            Assert.That(m_context.NotifyItemChangeResultCalls
                .Count(c => c.MonitoredItem == sut &&
                    c.RetryCount == 1 &&
                    c.Source == m_options.CurrentValue &&
                    c.ServiceResult.StatusCode == StatusCodes.Bad &&
                    !c.Final &&
                    c.FilterResult == null), Is.EqualTo(1));
        }

        [Test]
        public void CreateShouldNotifyItemChangeResultWithFilterResult()
        {
            // Arrange
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
               m_options, m_telemetry.CreateLogger("MonitoredItem"));
            var filterResult = new EventFilterResult();

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
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
            Assert.That(m_context.NotifyItemChangeResultCalls
                .Count(c => c.MonitoredItem == sut &&
                    c.RetryCount == 0 &&
                    c.Source == m_options.CurrentValue &&
                    c.ServiceResult == ServiceResult.Good &&
                    c.Final &&
                    Utils.IsEqual(c.FilterResult, filterResult)), Is.EqualTo(1));
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
            var sut = new TestMonitoredItem(m_context,
                m_options, m_telemetry.CreateLogger("MonitoredItem"));

            Assert.That(sut.CurrentMonitoringMode, Is.Not.EqualTo(MonitoringMode.Sampling));

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
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
            var sut = new TestMonitoredItem(m_context,
                m_options, m_telemetry.CreateLogger("MonitoredItem"));

            Assert.That(sut.CurrentMonitoringMode, Is.Not.EqualTo(MonitoringMode.Sampling));

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
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
            var sut = new TestMonitoredItem(m_context,
               m_options, m_telemetry.CreateLogger("MonitoredItem"));
            var currentSamplingInterval = TimeSpan.FromMilliseconds(500);

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
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
            var sut = new TestMonitoredItem(m_context,
               m_options, m_telemetry.CreateLogger("MonitoredItem"));
            const uint currentQueueSize = 5u;

            // Act
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
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
            var sut = new TestMonitoredItem(m_context,
               m_options, m_telemetry.CreateLogger("MonitoredItem"));
            uint result = sut.ClientHandle;

            // Assert
            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public async Task DisposeShouldCallRemoveItemOnSubscriptionAsync()
        {
            // Act
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
              m_options, m_telemetry.CreateLogger("MonitoredItem"));
            await sut.DisposeAsync().ConfigureAwait(false);

            // Assert
            Assert.That(m_context.NotifyItemChangeCalls
                .Count(c => c.MonitoredItem == sut && c.ItemDisposed), Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeCanBeCalledTwiceWithoutExceptionAsync()
        {
            // Act
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
              m_options, m_telemetry.CreateLogger("MonitoredItem"));
            await sut.DisposeAsync().ConfigureAwait(false);
            await sut.DisposeAsync().ConfigureAwait(false);

            // Assert
            Assert.That(m_context.NotifyItemChangeCalls
                .Count(c => c.MonitoredItem == sut && c.ItemDisposed), Is.EqualTo(1));
        }

        [Test]
        public void OnSubscriptionStateChangeShouldAdjustQueueSizeWhenAutoSetQueueSizeIsTrue()
        {
            // Arrange
            var mockContext = new FakeMonitoredItemContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            OptionsMonitor<MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext, options, telemetry.CreateLogger("MonitoredItem"));
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change change), Is.True);
            Assert.That(change?.Create, Is.Not.Null);
            Assert.That(change.Create.RequestedParameters.QueueSize, Is.EqualTo(6)); // (500 / 100) + 1 = 6
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenAutoSetQueueSizeIsFalse()
        {
            // Arrange
            var mockContext = new FakeMonitoredItemContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            OptionsMonitor<MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = false,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext, options, telemetry.CreateLogger("MonitoredItem"));
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change change), Is.False);
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenPublishingIntervalIsZero()
        {
            // Arrange
            var mockContext = new FakeMonitoredItemContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            OptionsMonitor<MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext, options, telemetry.CreateLogger("MonitoredItem"));
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.Zero);

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change change), Is.False);
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenSamplingIntervalIsZero()
        {
            // Arrange
            var mockContext = new FakeMonitoredItemContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            OptionsMonitor<MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.Zero
            });

            var monitoredItem = new TestMonitoredItem(mockContext, options, telemetry.CreateLogger("MonitoredItem"));
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change change), Is.False);
        }

        [Test]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenSamplingIntervalIsNegative()
        {
            // Arrange
            var mockContext = new FakeMonitoredItemContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            OptionsMonitor<MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(-100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext, options, telemetry.CreateLogger("MonitoredItem"));
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change change), Is.False);
        }

        [Test]
        public void ToStringShouldReturnExpectedString()
        {
            // Arrange
            var mockContext = new FakeMonitoredItemContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            OptionsMonitor<MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode")
            });

            mockContext.ToStringValue = "Test";
            var monitoredItem = new TestMonitoredItem(mockContext, options, telemetry.CreateLogger("MonitoredItem"));

            // Act
            string result = monitoredItem.ToString();

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

        private FakeMonitoredItemContext m_context;
        private OptionsMonitor<MonitoredItemOptions> m_options;
        private ITelemetryContext m_telemetry;
    }
}
