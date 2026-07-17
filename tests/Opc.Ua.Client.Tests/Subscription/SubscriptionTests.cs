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
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SubscriptionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockSubscriptionServices = new Mock<ISubscriptionServiceSetClientMethods>();
            m_mockMonitoredItemServices = new Mock<IMonitoredItemServiceSetClientMethods>();
            m_mockMethodServices = new Mock<IMethodServiceSetClientMethods>();
            m_session = new FakeSubscriptionContext
            {
                SubscriptionServiceSet = m_mockSubscriptionServices.Object,
                MethodServiceSet = m_mockMethodServices.Object,
                MonitoredItemServiceSet = m_mockMonitoredItemServices.Object
            };
            m_completion = new FakeMessageAckQueue();
            m_options = OptionsFactory.Create<SubscriptionOptions>();

            m_telemetry = NUnitTelemetryContext.Create();
            m_mockNotificationDataHandler = new Mock<ISubscriptionNotificationHandler>();
        }

        [Test]
        public async Task AddMonitoredItemShouldAddItemToMonitoredItemsAsync()
        {
            // Arrange
            OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                // Act
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);

                // Assert
                Assert.That(success, Is.True);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(sut.MonitoredItems.Items, Does.Contain(monitoredItem));
            }
        }

        [Test]
        public async Task ChangeSubscriptionOptionsShouldCallCreateAsyncIfNotCreatedAsync()
        {
            // Arrange
            var publishingInterval = TimeSpan.FromSeconds(100);
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    publishingInterval.TotalMilliseconds, 21, 7, 10, true,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 22,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);

            m_options.Configure(o => o with { Disabled = true });
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                Assert.That(sut.Created, Is.False);
                Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.Zero));
                Assert.That(sut.CurrentKeepAliveCount, Is.Zero);
                Assert.That(sut.CurrentLifetimeCount, Is.Zero);
                Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.Zero);
                Assert.That(sut.CurrentPriority, Is.Zero);

                // Act
                sut.SubscriptionStateChanged.Reset();
                m_options.Configure(o => o with
                {
                    Disabled = false,
                    PublishingEnabled = true,
                    PublishingInterval = publishingInterval,
                    KeepAliveCount = 7,
                    LifetimeCount = 15,
                    Priority = 3,
                    MaxNotificationsPerPublish = 10
                });
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                // Assert
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
                Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
                Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
                Assert.That(sut.CurrentPriority, Is.EqualTo(3));
                Assert.That(sut.Id, Is.EqualTo(22));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task ChangeSubscriptionOptionsShouldCallModifyAsyncIfCreatedAsync()
        {
            // Arrange

            var publishingInterval = TimeSpan.FromSeconds(100);

            m_mockSubscriptionServices
                .Setup(s => s.ModifySubscriptionAsync(It.IsAny<RequestHeader>(),
                    22, publishingInterval.TotalMilliseconds, 30, 10, 0,
                    4, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifySubscriptionResponse
                {
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);
            m_mockSubscriptionServices
                .Setup(s => s.SetPublishingModeAsync(It.IsAny<RequestHeader>(),
                    true, It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetPublishingModeResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.CurrentPublishingEnabled, Is.False);
                Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(publishingInterval));
                Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(7));
                Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(21));
                Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
                Assert.That(sut.CurrentPriority, Is.EqualTo(3));

                // Act
                sut.SubscriptionStateChanged.Reset();
                m_options.Configure(o => o with
                {
                    Disabled = false,
                    PublishingInterval = publishingInterval,
                    PublishingEnabled = true,
                    Priority = 4
                });
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                // Assert
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
                Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
                Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.Zero);
                Assert.That(sut.CurrentPriority, Is.EqualTo(4));
                Assert.That(sut.CurrentPublishingEnabled, Is.True);
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task ChangeSubscriptionOptionsShouldCallSetPublishingModeIfItIsTheOnlyChangeAsync()
        {
            // Arrange

            var publishingInterval = TimeSpan.FromSeconds(100);

            m_mockSubscriptionServices
                .Setup(s => s.SetPublishingModeAsync(It.IsAny<RequestHeader>(),
                    true, It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetPublishingModeResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.CurrentPublishingEnabled, Is.False);

                // Act
                sut.SubscriptionStateChanged.Reset();
                m_options.Configure(o => o with
                {
                    Disabled = false,
                    PublishingEnabled = true,
                    PublishingInterval = publishingInterval,
                    KeepAliveCount = 7,
                    LifetimeCount = 15,
                    Priority = 3,
                    MaxNotificationsPerPublish = 10
                });
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                // Assert
                Assert.That(sut.CurrentPublishingEnabled, Is.True);
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task ChangeMonitoredItemOptionsShouldAddCreatedItemsAsync()
        {
            // Arrange
            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 100,
                            RevisedSamplingInterval = 10000,
                            RevisedQueueSize = 10
                        }
                    ]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                sut.SubscriptionStateChanged.Reset();
                bool success = sut.MonitoredItems.TryAdd("Test", OptionsFactory.Create(new MonitoredItems.MonitoredItemOptions
                {
                    StartNodeId = NodeId.Parse("ns=2;s=Demo")
                }), out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);

                // Act
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(monitoredItem.ServerId, Is.EqualTo(100));
                Assert.That(monitoredItem.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(monitoredItem.CurrentQueueSize, Is.EqualTo(10));
                Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task ChangeMonitoredItemOptionsShouldChangeSubscriptionAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);

                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(monitoredItem.ServerId, Is.EqualTo(monitoredItem.ClientHandle));
                Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));

                m_mockMonitoredItemServices
                    .Setup(s => s.ModifyMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        TimestampsToReturn.Both,
                        It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ModifyMonitoredItemsResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                RevisedSamplingInterval = 100000,
                                RevisedQueueSize = 1000
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                sut.SubscriptionStateChanged.Reset();
                options.Configure(o => o with
                {
                    StartNodeId = NodeId.Parse("ns=2;s=Demo"),
                    MonitoringMode = MonitoringMode.Sampling,
                    SamplingInterval = TimeSpan.FromSeconds(555),
                    QueueSize = 3333,
                    DiscardOldest = true
                });
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                Assert.That(monitoredItem.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(100)));
                Assert.That(monitoredItem.CurrentQueueSize, Is.EqualTo(1000));
                Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task ChangeMonitoredItemOptionsNameShouldDeleteAndRecreateMonitoredItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
              m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);

                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(monitoredItem.ServerId, Is.EqualTo(monitoredItem.ClientHandle));
                Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));

                m_mockMonitoredItemServices
                    .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    .Verifiable(Times.Once);
                m_mockMonitoredItemServices
                    .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        TimestampsToReturn.Both,
                        It.Is<ArrayOf<MonitoredItemCreateRequest>>(r =>
                            r.Count == 1 &&
                            r[0].ItemToMonitor.NodeId.ToString().Contains("NewDemo")),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CreateMonitoredItemsResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                MonitoredItemId = 400,
                                RevisedSamplingInterval = 10000,
                                RevisedQueueSize = 10
                            }
                        ]
                    })
                    .Verifiable(Times.Once);
                // Act
                sut.SubscriptionStateChanged.Reset();
                options.Configure(o => o with
                {
                    StartNodeId = NodeId.Parse("ns=3;s=NewDemo"), // Changed
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = TimeSpan.FromSeconds(555),
                    QueueSize = 3333,
                    DiscardOldest = true
                });
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                Assert.That(monitoredItem.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(monitoredItem.CurrentQueueSize, Is.EqualTo(10));
                Assert.That(monitoredItem.ServerId, Is.EqualTo(400));
                Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task UpdatingMonitoringModeOnlyShouldCallSetMonitoringModeAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);

                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(monitoredItem.ServerId, Is.EqualTo(monitoredItem.ClientHandle));
                Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
                // Now we have a monitored item in sampling mode

                // Only set monitoring mode should be called for the item
                m_mockMonitoredItemServices
                    .Setup(s => s.SetMonitoringModeAsync(It.IsAny<RequestHeader>(),
                        sut.Id, MonitoringMode.Reporting,
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SetMonitoringModeResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    .Verifiable(Times.Once);

                // Act
                sut.SubscriptionStateChanged.Reset();
                options.Configure(o => TestMonitoredItem.CreatedOptions with
                {
                    MonitoringMode = MonitoringMode.Reporting
                });
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                // Assert
                Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task RemovingMonitoredItemShouldRemoveRemovedItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
              m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);
                Assert.That(sut.MonitoredItems.Count, Is.EqualTo(1));
                Assert.That(monitoredItem, Is.Not.Null);
                // Now we got an item that is created

                // Only delete monitored item should be called
                m_mockMonitoredItemServices
                    .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    .Verifiable(Times.Once);

                // Act
                sut.SubscriptionStateChanged.Reset();

                success = sut.MonitoredItems.TryRemove(monitoredItem.ClientHandle);
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                // Assert
                Assert.That(success, Is.True);
                Assert.That(sut.MonitoredItems.Count, Is.Zero);
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task RemovingMonitoredItemShouldTryAgainIfDeleteFailsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
              m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);
                Assert.That(sut.MonitoredItems.Count, Is.EqualTo(1));
                // Now we got an item that is created

                // Only delete monitored item should be called
                m_mockMonitoredItemServices
                    .SetupSequence(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(),
                        2, It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            ServiceResult = StatusCodes.Bad
                        },
                        Results = []
                    })
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        Results = [StatusCodes.Bad]
                    })
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    ;

                // Act
                sut.SubscriptionStateChanged.Reset();
                success = sut.MonitoredItems.TryRemove(monitoredItem.ClientHandle);
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                // Assert
                Assert.That(success, Is.True);
                Assert.That(sut.MonitoredItems.Count, Is.Zero);
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task ConditionRefreshAsyncShouldCallSessionCallAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                // Assert
                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                await sut.ConditionRefreshAsync(default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task ConditionRefreshAsyncThrowsIfNotYetCreatedAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                // Act
                async Task act() => await sut.ConditionRefreshAsync(CancellationToken.None).ConfigureAwait(false);

                // Assert
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act().ConfigureAwait(false));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task DeleteAsyncShouldCallSessionDeleteSubscriptionsAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                m_mockSubscriptionServices
                    .Setup(s => s.DeleteSubscriptionsAsync(
                        It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteSubscriptionsResponse
                    {
                        Results = [StatusCodes.Good]
                    }).
                    Verifiable(Times.Once);

                // Act
                await sut.DeleteAsync(default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task DisposeAsyncShouldCallSessionDeleteSubscriptionsAndCleanupMonitoredItemsAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);

            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                }).
                Verifiable(Times.Once);

            // Act
            await sut.DisposeAsync().ConfigureAwait(false);

            // Assert
            // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            Assert.That(sut.MonitoredItems.Items, Is.Empty);
        }

        [Test]
        public async Task DisableShouldCallSessionDeleteSubscriptionsButNotMonitoredItemsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);

                m_mockSubscriptionServices
                    .Setup(s => s.DeleteSubscriptionsAsync(
                        It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteSubscriptionsResponse
                    {
                        Results = [StatusCodes.Good]
                    }).
                    Verifiable(Times.Once);

                // Act
                sut.SubscriptionStateChanged.Reset();
                m_options.Configure(o => o with { Disabled = true });
                await sut.SubscriptionStateChanged.WaitAsync().ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
                Assert.That(sut.MonitoredItems.Items, Is.Not.Empty);
            }
        }

        [Test]
        public async Task DeleteAsyncShouldCatchAllExceptionsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                m_mockSubscriptionServices
                    .Setup(s => s.DeleteSubscriptionsAsync(
                        It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteSubscriptionsResponse
                    {
                        Results = [StatusCodes.Bad]
                    }).
                    Verifiable(Times.Once);

                // Act
                await sut.DeleteAsync(default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task DisposeAsyncShouldDisposeCleanlyAsync()
        {
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            // Act & Assert - should not throw
            await sut.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task FindItemByClientHandleShouldReturnMonitoredItemAsync()
        {
            // Arrange
            OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);

                // Act
                success = sut.MonitoredItems.TryGetMonitoredItemByClientHandle(
                    monitoredItem.ClientHandle, out IMonitoredItem result);

                // Assert
                Assert.That(success, Is.True);
                Assert.That(result, Is.EqualTo(monitoredItem));
            }
        }

        [Test]
        public async Task FindItemByClientHandleShouldReturnNullIfNotFoundAsync()
        {
            // Arrange
            OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                bool success = sut.MonitoredItems.TryAdd("Test", options, out _);
                Assert.That(success, Is.True);

                // Act
                success = sut.MonitoredItems.TryGetMonitoredItemByClientHandle(55,
                    out IMonitoredItem result);

                // Assert
                Assert.That(success, Is.False);
                Assert.That(result, Is.Null);
            }
        }

        [Test]
        public async Task OnPublishReceivedAsyncShouldProcessNotificationAsync()
        {
            // Arrange
            var message = new NotificationMessage();

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                // Act & Assert - should not throw
                await sut.OnPublishReceivedAsync(message, null, null).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RecreateAsyncFromNotificationCallbackShouldFailFastAsync()
        {
            var observed = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            await using (sut.ConfigureAwait(false))
            {
                sut.OnKeepAliveAsync = async () =>
                {
                    try
                    {
                        await sut.RecreateAsync(default).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        observed.TrySetResult(ex);
                    }
                };

                await sut.OnPublishReceivedAsync(BuildKeepAliveMessage(1), null, [])
                    .ConfigureAwait(false);
                Exception exception = await observed.Task
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(m_completion.PublishingQuiescenceCalls, Is.Zero);
            }
        }

        [Test]
        public async Task DisposeAsyncFromNotificationCallbackShouldFailFastAsync()
        {
            var observed = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            await using (sut.ConfigureAwait(false))
            {
                sut.OnKeepAliveAsync = async () =>
                {
                    try
                    {
                        await sut.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        observed.TrySetResult(ex);
                    }
                };

                await sut.OnPublishReceivedAsync(BuildKeepAliveMessage(1), null, [])
                    .ConfigureAwait(false);
                Exception exception = await observed.Task
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(exception, Is.TypeOf<InvalidOperationException>());
            }
        }

        [Test]
        public async Task RecreateAsyncShouldReCreateSubscriptionAndMonitoredItemsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);

                // We have a running subscription with one monitored item.

                m_mockSubscriptionServices
                    .Setup(s => s.DeleteSubscriptionsAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 10u),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteSubscriptionsResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    .Verifiable(Times.Once);
                m_mockSubscriptionServices
                    .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                        TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                        3, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CreateSubscriptionResponse
                    {
                        SubscriptionId = 22,
                        RevisedLifetimeCount = 10,
                        RevisedMaxKeepAliveCount = 5,
                        RevisedPublishingInterval = 10000
                    })
                    .Verifiable(Times.Once);
                m_mockMonitoredItemServices
                    .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 22,
                        TimestampsToReturn.Both,
                        It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CreateMonitoredItemsResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                MonitoredItemId = 200,
                                RevisedSamplingInterval = 10000,
                                RevisedQueueSize = 10
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                Assert.That(sut.Created, Is.True);
                sut.SetMessageStateForTest(17, 19, [11, 13]);

                // Act
                await sut.RecreateAsync(default).ConfigureAwait(false);

                // Assert
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
                Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
                Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
                Assert.That(sut.CurrentPriority, Is.EqualTo(3));
                Assert.That(sut.Id, Is.EqualTo(22));
                Assert.That(monitoredItem.ServerId, Is.EqualTo(200));
                Assert.That(sut.LastSequenceNumberForTest, Is.Zero);
                Assert.That(sut.LastDataSequenceNumberForTest, Is.Zero);
                Assert.That(sut.AvailableSequenceNumbersForTest, Is.Empty);
                Assert.That(m_completion.PublishingQuiescenceCalls, Is.EqualTo(1));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task RecreateAsyncShouldReCreateSubscriptionsAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);

                m_mockSubscriptionServices
                    .Setup(s => s.DeleteSubscriptionsAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 10u),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteSubscriptionsResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    .Verifiable(Times.Once);
                m_mockSubscriptionServices
                    .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                        TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                        3, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CreateSubscriptionResponse
                    {
                        SubscriptionId = 22,
                        RevisedLifetimeCount = 10,
                        RevisedMaxKeepAliveCount = 5,
                        RevisedPublishingInterval = 10000
                    })
                    .Verifiable(Times.Once);

                m_mockMonitoredItemServices
                    .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 22,
                        TimestampsToReturn.Both,
                        It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CreateMonitoredItemsResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                MonitoredItemId = 200,
                                RevisedSamplingInterval = 10000,
                                RevisedQueueSize = 10
                            }
                        ]
                    })
                    .Verifiable(Times.Once);
                Assert.That(sut.Created, Is.True);

                // Act
                await sut.RecreateAsync(default).ConfigureAwait(false);

                // Assert
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
                Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
                Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
                Assert.That(sut.CurrentPriority, Is.EqualTo(3));
                Assert.That(sut.Id, Is.EqualTo(22));
                Assert.That(sut.MonitoredItems.Count, Is.EqualTo(1));
                Assert.That(monitoredItem.ServerId, Is.EqualTo(200));
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task RecreateAsyncShouldPreserveCurrentGenerationWhenDeleteFailsAsync()
        {
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 10u),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.BadUserAccessDenied]
                });

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            await using (sut.ConfigureAwait(false))
            {
                sut.SetMessageStateForTest(17, 19, [11, 13]);

                ServiceResultException exception =
                    Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await sut.RecreateAsync(default).ConfigureAwait(false));

                Assert.That(exception.StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(sut.Id, Is.EqualTo(10u));
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.LastSequenceNumberForTest, Is.EqualTo(17u));
                Assert.That(sut.LastDataSequenceNumberForTest, Is.EqualTo(19u));
                Assert.That(sut.AvailableSequenceNumbersForTest,
                    Is.EqualTo(new uint[] { 11, 13 }));
                Assert.That(m_completion.DroppedSubscriptions,
                    Does.Not.Contain(10u));
                Assert.That(m_completion.PublishingQuiescenceCalls, Is.EqualTo(1));
                m_mockSubscriptionServices.Verify(s =>
                    s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                        It.IsAny<double>(), It.IsAny<uint>(), It.IsAny<uint>(),
                        It.IsAny<uint>(), It.IsAny<bool>(), It.IsAny<byte>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        [Test]
        public async Task RecreateAsyncShouldDiscardQueuedMessageFromRetiredGenerationAsync()
        {
            var deleteEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var continueDelete = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 10u),
                    It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader _, ArrayOf<uint> _,
                    CancellationToken _) =>
                {
                    deleteEntered.TrySetResult(true);
                    await continueDelete.Task.ConfigureAwait(false);
                    return new DeleteSubscriptionsResponse
                    {
                        Results = [StatusCodes.Good]
                    };
                });
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 22,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                });

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            await using (sut.ConfigureAwait(false))
            {
                Task recreate = sut.RecreateAsync(default).AsTask();
                await deleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                await sut.OnPublishReceivedAsync(BuildKeepAliveMessage(9), null, [])
                    .ConfigureAwait(false);
                continueDelete.TrySetResult(true);
                await recreate.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                await sut.OnPublishReceivedAsync(BuildKeepAliveMessage(1), null, [])
                    .ConfigureAwait(false);
                await WaitForAsync(() => sut.KeepAliveNotificationCount == 1,
                    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);

                Assert.That(sut.KeepAliveNotificationCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task DisposeAsyncShouldWaitForActiveRecreateAsync()
        {
            var deleteEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var continueDelete = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader _, ArrayOf<uint> ids,
                    CancellationToken _) =>
                {
                    if (ids[0] == 10u)
                    {
                        deleteEntered.TrySetResult(true);
                        await continueDelete.Task.ConfigureAwait(false);
                    }
                    return new DeleteSubscriptionsResponse
                    {
                        Results = [StatusCodes.Good]
                    };
                });
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 22,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                });

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            Task recreate = sut.RecreateAsync(default).AsTask();
            await deleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            Task dispose = sut.DisposeAsync().AsTask();
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(dispose.IsCompleted, Is.False);

            continueDelete.TrySetResult(true);
            await recreate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        [Test]
        public async Task RecreateAsyncShouldRunAfterCreateHookBeforeMonitoredItemsOnEveryCreateAsync()
        {
            var order = new List<string>();
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                });
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                    3, It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("subscription"))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 22,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                });
            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 22,
                    TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("items"))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 200,
                            RevisedSamplingInterval = 10000,
                            RevisedQueueSize = 10
                        }
                    ]
                });

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 10);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options =
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                Assert.That(sut.MonitoredItems.TryAdd("Test", options, out _), Is.True);
                sut.OnAfterCreateAsync = _ =>
                {
                    order.Add("durable");
                    return default;
                };

                await sut.RecreateAsync(default).ConfigureAwait(false);
                await sut.RecreateAsync(default).ConfigureAwait(false);

                Assert.That(order,
                    Is.EqualTo(s_recreateOperationOrder));
            }
        }

        [Test]
        public async Task RemoveItemShouldRemoveItemFromMonitoredItemsAsync()
        {
            // Arrange
            OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry);
            await using (sut.ConfigureAwait(false))
            {
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);

                // Act
                success = sut.MonitoredItems.TryRemove(monitoredItem.ClientHandle);

                // Assert
                Assert.That(success, Is.True);
                Assert.That(sut.MonitoredItems.Items, Does.Not.Contain(monitoredItem));
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldReturnFalseWhenResponseWrong1Async()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);

                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant([1990u, 2200u, 3300u]), // serverHandles
                                    new Variant([22222u])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                success = await sut.TryCompleteTransferAsync([], CancellationToken.None).ConfigureAwait(false);

                // Assert
                Assert.That(success, Is.False);
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldReturnFalseWhenResponseWrong2Async()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(success, Is.True);

                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant(["string"]), // serverHandles
                                    new Variant([22u])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                success = await sut.TryCompleteTransferAsync([], CancellationToken.None).ConfigureAwait(false);

                // Assert
                Assert.That(success, Is.False);
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndCreateServerItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);

                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant([19900u]), // serverHandles
                                    new Variant([monitoredItem.ClientHandle])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                success = await sut.TryCompleteTransferAsync([], default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
                Assert.That(success, Is.True);
                Assert.That(monitoredItem.ServerId, Is.EqualTo(19900));
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndDoNothingIfStateIsCorrectAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);
                uint serverId = monitoredItem.ServerId;

                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant([monitoredItem.ServerId]), // serverHandles
                                    new Variant([monitoredItem.ClientHandle])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                success = await sut.TryCompleteTransferAsync([], default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
                Assert.That(monitoredItem.ServerId, Is.EqualTo(serverId));
                Assert.That(success, Is.True);
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndDeleteItemIfNotInSubscriptionAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant([19900u]), // serverHandles
                                    new Variant([300u])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                m_mockMonitoredItemServices
                    .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 19900u), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    .Verifiable(Times.Once);

                // Act
                bool success = await sut.TryCompleteTransferAsync([], default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
                Assert.That(success, Is.True);
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldReturnFalseWhenDeletingExtraneousServerItemFailsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant([19900u]), // serverHandles
                                    new Variant([300u])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                m_mockMonitoredItemServices
                    .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 19900u), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            ServiceResult = StatusCodes.Good
                        },
                        Results = [StatusCodes.BadUnexpectedError]
                    })
                    .Verifiable(Times.Once);

                // Act
                bool success = await sut.TryCompleteTransferAsync([], default).ConfigureAwait(false);

                // Assert
                Assert.That(success, Is.False);
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndReturnFalseIfFailingAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);
                Assert.That(monitoredItem.Created, Is.True);

                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Bad,
                                OutputArguments = []
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                success = await sut.TryCompleteTransferAsync([], default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
                Assert.That(success, Is.False);
                Assert.That(monitoredItem.Created, Is.False);
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldAssignServerIdToMonitoredItemWithClientIdAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);
                Assert.That(monitoredItem.Created, Is.True);
                uint clientId = monitoredItem.ClientHandle;
                uint serverId = monitoredItem.ServerId;

                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant([serverId]), // serverHandles
                                    new Variant([19900u])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                success = await sut.TryCompleteTransferAsync([], default).ConfigureAwait(false);

                // Assert
                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
                Assert.That(success, Is.True);
                Assert.That(monitoredItem.ClientHandle, Is.EqualTo(19900));
                Assert.That(monitoredItem.ServerId, Is.EqualTo(serverId));
            }
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCreateWhatIsMissingOnServerAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 2);
            await using (sut.ConfigureAwait(false))
            {
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options = OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                bool success = sut.MonitoredItems.TryAdd("Test", options, out IMonitoredItem monitoredItem);
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(success, Is.True);
                Assert.That(monitoredItem.Created, Is.True);
                uint clientId = monitoredItem.ClientHandle;
                uint serverId = monitoredItem.ServerId;

                m_mockMethodServices
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.Is<ArrayOf<CallMethodRequest>>(r =>
                            r.Count == 1 &&
                            r[0].InputArguments.Count == 1 &&
                            r[0].InputArguments[0].AsBoxedObject().Equals(2u) &&
                            r[0].ObjectId == ObjectIds.Server &&
                            r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CallResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant([30000u]), // serverHandles
                                    new Variant([19900u])  // clientHandles
                                ]
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Delete monitored item should be called
                m_mockMonitoredItemServices
                    .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 30000u), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeleteMonitoredItemsResponse
                    {
                        Results = [StatusCodes.Good]
                    })
                    .Verifiable(Times.Once);
                m_mockMonitoredItemServices
                    .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                        TimestampsToReturn.Both,
                        It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CreateMonitoredItemsResponse
                    {
                        Results =
                        [
                            new ()
                            {
                                StatusCode = StatusCodes.Good,
                                MonitoredItemId = 44444,
                                RevisedSamplingInterval = 10000,
                                RevisedQueueSize = 10
                            }
                        ]
                    })
                    .Verifiable(Times.Once);

                // Act
                success = await sut.TryCompleteTransferAsync([], default).ConfigureAwait(false);

                // Assert
                Assert.That(success, Is.True);
                Assert.That(monitoredItem.Created, Is.True);
                Assert.That(monitoredItem.ClientHandle, Is.EqualTo(clientId));
                Assert.That(monitoredItem.ServerId, Is.EqualTo(44444));

                // m_mockSession.Verify() was no-op (no Verifiable setups on the context); inner-mock verifications retained.
                m_mockMonitoredItemServices.Verify();
            }
        }

        [Test]
        public async Task SetTriggeringAsyncShouldThrowOnNullTriggeringItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                // Act + Assert
                Assert.That(async () => await sut.SetTriggeringAsync(
                    null!, null, null, default).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentNullException>());
            }
        }

        [Test]
        public async Task SetTriggeringAsyncShouldThrowWhenItemNotInSubscriptionAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                bool added = sut.MonitoredItems.TryAdd("trig",
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>(),
                    out IMonitoredItem trig);
                Assert.That(added, Is.True);

                // Construct a stray item that belongs to a different
                // subscription's MonitoredItemManager.
                var strayContext = new FakeMonitoredItemContext();
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> strayOpts =
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>();
                var stray = new TestMonitoredItem(strayContext, "stray",
                    strayOpts, m_telemetry.CreateLogger("stray"));

                // Act + Assert: validation by reference identity
                // rejects the stray item.
                Assert.That(async () => await sut.SetTriggeringAsync(
                    trig, [stray], null, default)
                    .ConfigureAwait(false),
                    Throws.ArgumentException);
            }
        }

        [Test]
        public async Task SetTriggeringAsyncShouldQueueAndApplyAsync()
        {
            // Arrange — set up the SetTriggering RPC mock first so
            // the apply pass succeeds when the queued op fires.
            m_mockMonitoredItemServices
                .Setup(s => s.SetTriggeringAsync(
                    It.IsAny<RequestHeader?>(), 22u, It.IsAny<uint>(),
                    It.Is<ArrayOf<uint>>(a => a.Count == 1),
                    It.Is<ArrayOf<uint>>(r => r.Count == 0),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    AddResults = new[] { StatusCodes.Good }.ToArrayOf(),
                    RemoveResults = Array.Empty<StatusCode>().ToArrayOf()
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                bool addedTrig = sut.MonitoredItems.TryAdd("trig",
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>(),
                    out IMonitoredItem trig);
                bool addedTgt = sut.MonitoredItems.TryAdd("tgt",
                    OptionsFactory.Create<MonitoredItems.MonitoredItemOptions>(),
                    out IMonitoredItem tgt);
                Assert.That(addedTrig && addedTgt, Is.True);
                Assert.That(trig.Created, Is.True);
                Assert.That(tgt.Created, Is.True);

                // Act
                SetTriggeringResult result = await sut.SetTriggeringAsync(
                    trig,
                    [tgt],
                    null,
                    default).ConfigureAwait(false);

                // Assert
                Assert.That(result, Is.Not.Null);
                Assert.That(result.TriggeringItem, Is.SameAs(trig));
                Assert.That(result.AddResults, Has.Count.EqualTo(1));
                Assert.That(StatusCode.IsGood(result.AddResults[0].Status), Is.True);
                Assert.That(result.RemoveResults, Is.Empty);
                Assert.That(tgt.TriggeringItems, Contains.Item(trig));
                m_mockMonitoredItemServices.Verify();
            }
        }

        /// <summary>
        /// Regression coverage for OPCFoundation/UA-.NETStandard#3540.
        /// The V2 engine's <c>OnStatusChangeNotificationAsync</c> must
        /// always surface the
        /// <see cref="PublishState.Transferred"/> flag via
        /// <c>OnPublishStateChanged</c> so listeners can react —
        /// previously the override was a no-op TODO that swallowed the
        /// flag.
        /// </summary>
        [Test]
        public async Task GoodSubscriptionTransferredRaisesPublishStateTransferredAsync()
        {
            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22);
            await using (sut.ConfigureAwait(false))
            {
                Assert.That(sut.Created, Is.True);
                sut.PublishStateChanged.Reset();

                await sut.OnPublishReceivedAsync(BuildStatusChangeMessage(
                    sequenceNumber: 1,
                    StatusCodes.GoodSubscriptionTransferred), null, []).ConfigureAwait(false);

                await sut.PublishStateChanged.WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.That(sut.LastPublishState & PublishState.Transferred,
                    Is.EqualTo(PublishState.Transferred));
                Assert.That(sut.Id, Is.EqualTo(22u),
                    "ReportOnly default must not recreate the subscription.");
            }
        }

        /// <summary>
        /// Regression coverage for OPCFoundation/UA-.NETStandard#3540.
        /// With <see cref="SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer"/>
        /// the V2 engine must recreate the subscription in place on the
        /// same session (driven through <see cref="ISubscription.RecreateAsync"/>)
        /// so
        /// the application keeps receiving data after a server-side
        /// quirk emits Good_SubscriptionTransferred against a freshly
        /// created subscription.
        /// </summary>
        [Test]
        public async Task GoodSubscriptionTransferredRecreatesSubscriptionWhenPolicyEnabledAsync()
        {
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 33,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22,
                SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer);
            await using (sut.ConfigureAwait(false))
            {
                Assert.That(sut.Created, Is.True);
                Assert.That(sut.Id, Is.EqualTo(22u));

                // Queue a stale ack for the doomed id so we can assert
                // the ack-pruning step ran after successful retirement.
                await m_completion.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 22u,
                    SequenceNumber = 7u
                }).ConfigureAwait(false);

                await sut.OnPublishReceivedAsync(BuildStatusChangeMessage(
                    sequenceNumber: 1,
                    StatusCodes.GoodSubscriptionTransferred), null, []).ConfigureAwait(false);

                await WaitForAsync(() => sut.Id == 33u, TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.That(sut.Id, Is.EqualTo(33u),
                    "Recovery must assign the new server-side id.");
                Assert.That(m_completion.DroppedSubscriptions,
                    Does.Contain(22u),
                    "Ack-pruning must run for the retired subscription id.");
                m_mockSubscriptionServices.Verify();
            }
        }

        /// <summary>
        /// A burst of unsolicited Good_SubscriptionTransferred messages
        /// must collapse into a single recreate via the
        /// <c>m_recreateAfterTransferInProgress</c> guard — the
        /// service set must see exactly one CreateSubscriptionAsync
        /// call.
        /// </summary>
        [Test]
        public async Task GoodSubscriptionTransferredCollapsesConcurrentRecreatesAsync()
        {
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 44,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_session, m_mockNotificationDataHandler.Object,
                m_completion, m_options, m_telemetry, 22,
                SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer);
            await using (sut.ConfigureAwait(false))
            {
                for (uint i = 1; i <= 3; i++)
                {
                    await sut.OnPublishReceivedAsync(BuildStatusChangeMessage(
                        sequenceNumber: i,
                        StatusCodes.GoodSubscriptionTransferred), null, [])
                        .ConfigureAwait(false);
                }

                await WaitForAsync(() => sut.Id == 44u, TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                await Task.Delay(300).ConfigureAwait(false);

                Assert.That(sut.Id, Is.EqualTo(44u));
                m_mockSubscriptionServices.Verify(s =>
                    s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                        It.IsAny<double>(), It.IsAny<uint>(), It.IsAny<uint>(),
                        It.IsAny<uint>(), It.IsAny<bool>(), It.IsAny<byte>(),
                        It.IsAny<CancellationToken>()),
                    Times.Once,
                    "Idempotency guard must prevent duplicate recreates from the burst.");
            }
        }

        private static NotificationMessage BuildStatusChangeMessage(
            uint sequenceNumber, StatusCode status)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData =
                [
                    new ExtensionObject(new StatusChangeNotification
                    {
                        Status = status
                    })
                ]
            };
        }

        private static NotificationMessage BuildKeepAliveMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData = []
            };
        }

        private static async Task WaitForAsync(
            Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!predicate() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
        }

        private sealed class TestMonitoredItem : MonitoredItems.MonitoredItem
        {
            public static MonitoredItems.MonitoredItemOptions CreatedOptions => new()
            {
                StartNodeId = NodeId.Parse("ns=2;s=Demo"),
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = TimeSpan.FromSeconds(1),
                QueueSize = 5,
                TimestampsToReturn = TimestampsToReturn.Both,
                AttributeId = Attributes.Value,
                DiscardOldest = true
            };

            public TestMonitoredItem(IMonitoredItemContext subscription, string name,
                OptionsMonitor<MonitoredItems.MonitoredItemOptions> options, ILogger logger)
                : base(subscription, name, options, logger)
            {
                if (options.CurrentValue.StartNodeId.IsNull)
                {
                    // Auto create
                    options.Configure(o => CreatedOptions);
                    Assert.That(TryGetPendingChange(out Change change), Is.True);
                    Assert.That(change, Is.Not.Null);
                    change.SetCreateResult(new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            AttributeId = Options.CurrentValue.AttributeId,
                            NodeId = Options.CurrentValue.StartNodeId
                        },
                        MonitoringMode = MonitoringMode.Sampling,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = ClientHandle,
                            SamplingInterval = Options.CurrentValue.SamplingInterval.TotalMilliseconds,
                            QueueSize = Options.CurrentValue.QueueSize,
                            DiscardOldest = Options.CurrentValue.DiscardOldest
                        }
                    },
                    new MonitoredItemCreateResult
                    {
                        StatusCode = StatusCodes.Good,
                        MonitoredItemId = ClientHandle,
                        RevisedSamplingInterval = 10000,
                        RevisedQueueSize = 10
                    }, 0, [], new ResponseHeader());
                }
            }
        }

        private sealed class TestSubscription : Subscription
        {
            public static SubscriptionOptions SubscriptionOptions => new()
            {
                Disabled = false,
                PublishingEnabled = false,
                PublishingInterval = TimeSpan.FromSeconds(100),
                KeepAliveCount = 7,
                LifetimeCount = 15,
                Priority = 3,
                MaxNotificationsPerPublish = 10
            };

            public TestSubscription(ISubscriptionContext session, ISubscriptionNotificationHandler handler,
                IMessageAckQueue completion, OptionsMonitor<SubscriptionOptions> options,
                ITelemetryContext telemetry, uint? subscriptionIdForAlreadyCreatedState = null,
                SubscriptionRecoveryPolicy recoveryPolicy = SubscriptionRecoveryPolicy.ReportOnly)
                : base(session, handler, completion, !subscriptionIdForAlreadyCreatedState.HasValue ?
                      options : options.Configure(o => o with { Disabled = true }), telemetry)
            {
                // Let the subscription create itself
                if (subscriptionIdForAlreadyCreatedState.HasValue)
                {
                    Options = SubscriptionOptions with { RecoveryPolicy = recoveryPolicy };
                    OnSubscriptionUpdateComplete(true, subscriptionIdForAlreadyCreatedState.Value,
                        TimeSpan.FromSeconds(100), 7, 21, 3, 10, false);
                    // Now we have a created subscription
                }
            }

            public SemaphoreSlim Block { get; } = new(0, 1);
            public AsyncManualResetEvent SubscriptionStateChanged { get; } = new();
            public AsyncManualResetEvent PublishStateChanged { get; } = new();
            public PublishState LastPublishState { get; private set; }
            public uint LastSequenceNumberForTest => LastSequenceNumberProcessed;
            public uint LastDataSequenceNumberForTest => LastDataSequenceNumberProcessed;
            public IReadOnlyList<uint> AvailableSequenceNumbersForTest
                => AvailableInRetransmissionQueue;
            public int KeepAliveNotificationCount
                => Volatile.Read(ref m_keepAliveNotificationCount);
            public Func<ValueTask>? OnKeepAliveAsync { get; set; }

            public void SetMessageStateForTest(uint lastSequenceNumber,
                uint lastDataSequenceNumber,
                IReadOnlyList<uint> availableSequenceNumbers)
            {
                LastSequenceNumberProcessed = lastSequenceNumber;
                LastDataSequenceNumberProcessed = lastDataSequenceNumber;
                AvailableInRetransmissionQueue = availableSequenceNumbers;
            }

            public async ValueTask WaitAsync()
            {
                await Block.WaitAsync().ConfigureAwait(false);
                Block.Release();
            }

            protected override void OnSubscriptionStateChanged(SubscriptionState state)
            {
                if (state != SubscriptionState.Opened)
                {
                    SubscriptionStateChanged.Set();
                }
                base.OnSubscriptionStateChanged(state);
            }

            protected override void OnPublishStateChanged(PublishState stateMask)
            {
                LastPublishState = stateMask;
                PublishStateChanged.Set();
                base.OnPublishStateChanged(stateMask);
            }

            protected override MonitoredItems.MonitoredItem CreateMonitoredItem(string name,
                IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options, IMonitoredItemContext context,
                ITelemetryContext telemetry)
            {
                return new TestMonitoredItem(context, name,
                    (OptionsMonitor<MonitoredItems.MonitoredItemOptions>)options,
                    telemetry.CreateLogger("TestMonitoredItem"));
            }

            protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
                DateTime publishTime, PublishState publishStateMask)
            {
                Interlocked.Increment(ref m_keepAliveNotificationCount);
                return OnKeepAliveAsync?.Invoke() ?? default;
            }

            private int m_keepAliveNotificationCount;
        }

        private static readonly string[] s_recreateOperationOrder =
        [
            "subscription", "durable", "items",
            "subscription", "durable", "items"
        ];
        private FakeMessageAckQueue m_completion;
        private OptionsMonitor<SubscriptionOptions> m_options;
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionServiceSetClientMethods> m_mockSubscriptionServices;
        private Mock<IMonitoredItemServiceSetClientMethods> m_mockMonitoredItemServices;
        private Mock<IMethodServiceSetClientMethods> m_mockMethodServices;
        private FakeSubscriptionContext m_session;
        private Mock<ISubscriptionNotificationHandler> m_mockNotificationDataHandler;
    }
}
