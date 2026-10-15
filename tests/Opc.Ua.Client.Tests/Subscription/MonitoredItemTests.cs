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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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

        /// <summary>
        /// Loaded handles require a modify response for revised parameters, not another create or invented defaults.
        /// </summary>
        [Test]
        public async Task LoadedItemRefreshesRevisedParametersOnceWithoutRecreationAsync()
        {
            m_options.Configure(options => options with
            {
                StartNodeId = new NodeId("loaded", 2),
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = TimeSpan.FromMilliseconds(20),
                QueueSize = 17
            });
            var item = new TestMonitoredItem(m_context, m_options, m_telemetry.CreateLogger("MonitoredItem"));
            await using (item.ConfigureAwait(false))
            {
                item.ApplyLoadState(new MonitoredItemLoadState("loaded", m_options, 42, 7, []));
                Assert.That(item.TryGetPendingChange(out _), Is.False);

                item.SetTransferResult(42, 9);

                Assert.That(item.TryGetPendingChange(out MonitoredItem.Change? refresh), Is.True);
                Assert.That(refresh!.ForceRecreate, Is.False);
                Assert.That(refresh.MonitoringModeChange, Is.Null);
                MonitoredItemModifyRequest request = refresh.BindModifyRequest()!;
                Assert.That(request.MonitoredItemId, Is.EqualTo(9));
                Assert.That(request.RequestedParameters.ClientHandle, Is.EqualTo(42));
                Assert.That(request.RequestedParameters.SamplingInterval, Is.EqualTo(20));
                Assert.That(request.RequestedParameters.QueueSize, Is.EqualTo(17));
                refresh.SetModifyResult(request, new MonitoredItemModifyResult
                {
                    StatusCode = StatusCodes.Good,
                    RevisedSamplingInterval = 1000,
                    RevisedQueueSize = 19
                }, 0, [], new ResponseHeader());
                Assert.That(item.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(item.CurrentQueueSize, Is.EqualTo(19));
                Assert.That(item.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
                Assert.That(item.ServerId, Is.EqualTo(9));
                Assert.That(item.TryGetPendingChange(out _), Is.False);
                item.SetTransferResult(42, 9);
                Assert.That(item.TryGetPendingChange(out _), Is.False);
            }
        }

        /// <summary>
        /// An options callback between refresh capture and enqueue cannot restore stale monitoring parameters.
        /// </summary>
        [Test]
        public async Task LoadedItemRefreshPreservesOptionsChangedBeforeEnqueueAsync()
        {
            var initialFilter = new Mock<DataChangeFilter>();
            initialFilter.SetupGet(filter => filter.TypeId).Returns(DataTypeIds.DataChangeFilter);
            var updatedFilter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp };
            var captured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var resume = new ManualResetEventSlim();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using TestMonitoredItem item = CreateLoadedItem(initialFilter.Object);
            int captures = 0;
            initialFilter.SetupGet(filter => filter.TypeId).Returns(() =>
            {
                if (Interlocked.Increment(ref captures) == 1)
                {
                    captured.TrySetResult(true);
                    // The synchronous filter getter runs after options capture but before enqueue.
                    resume.Wait(deadline.Token);
                }
                return DataTypeIds.DataChangeFilter;
            });
            Task transfer = Task.Run(() => item.SetTransferResult(42, 9), deadline.Token);
            ArrayOf<MonitoredItemModifyRequest> requests;
            try
            {
                await captured.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                UpdateMonitoringParameters(updatedFilter);
                Assert.That(item.TryGetPendingChange(out MonitoredItem.Change? optionsChange), Is.True);
                Assert.That(optionsChange!.Options, Is.SameAs(m_options.CurrentValue));
                resume.Set();
                await transfer.WaitAsync(deadline.Token).ConfigureAwait(false);
                requests = ApplyPendingModifications(item);
            }
            finally
            {
                resume.Set();
                await transfer.WaitAsync(deadline.Token).ConfigureAwait(false);
            }

            Assert.That(captures, Is.EqualTo(1));
            Assert.That(requests.Count, Is.EqualTo(2));
            foreach (MonitoredItemModifyRequest request in requests)
            {
                AssertUpdatedMonitoringParameters(request, updatedFilter);
            }
            Assert.That(item.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
            Assert.That(item.CurrentQueueSize, Is.EqualTo(31));
            Assert.That(m_options.CurrentValue.SamplingInterval, Is.EqualTo(item.CurrentSamplingInterval));
            Assert.That(m_options.CurrentValue.QueueSize, Is.EqualTo(item.CurrentQueueSize));
            Assert.That(item.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(item.ServerId, Is.EqualTo(9));
            Assert.That(item.HasPendingChanges, Is.False);
            item.SetTransferResult(42, 9);
            Assert.That(item.HasPendingChanges, Is.False);
        }

        /// <summary>
        /// Sequential options changes on either side of transfer retain the latest requested settings.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LoadedItemRefreshPreservesOptionsBeforeOrAfterTransferAsync(bool updateBeforeTransfer)
        {
            var initialFilter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue };
            var updatedFilter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp };
            await using TestMonitoredItem item = CreateLoadedItem(initialFilter);
            if (updateBeforeTransfer)
            {
                UpdateMonitoringParameters(updatedFilter);
            }
            item.SetTransferResult(42, 9);
            if (!updateBeforeTransfer)
            {
                UpdateMonitoringParameters(updatedFilter);
            }

            ArrayOf<MonitoredItemModifyRequest> requests = ApplyPendingModifications(item);

            Assert.That(requests.Count, Is.EqualTo(2));
            AssertUpdatedMonitoringParameters(requests[^1], updatedFilter);
            Assert.That(item.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
            Assert.That(item.CurrentQueueSize, Is.EqualTo(31));
            Assert.That(item.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(item.ServerId, Is.EqualTo(9));
            Assert.That(item.HasPendingChanges, Is.False);
        }

        /// <summary>
        /// A null and an empty IndexRange both mean "not used" (Part 4
        /// §7.27). A restored item carries "" from the snapshot while the
        /// application re-applies null; that must not force a recreate.
        /// </summary>
        [Test]
        public void NullAndEmptyIndexRangeDoNotForceRecreate()
        {
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
                m_options, m_telemetry.CreateLogger("MonitoredItem"));
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
            change!.SetCreateResult(new MonitoredItemCreateRequest
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
                MonitoredItemId = 5,
                RevisedSamplingInterval = 1000,
                RevisedQueueSize = 5
            }, 0, [], new ResponseHeader());
            Assert.That(sut.Created, Is.True);

            MonitoredItemOptions options = m_options.CurrentValue;
            Assert.That(options.IndexRange, Is.Null);
            var sameRange = new MonitoredItem.Change(sut, options,
                options with { IndexRange = string.Empty });
            Assert.That(sameRange.ForceRecreate, Is.False);
            var otherRange = new MonitoredItem.Change(sut, options,
                options with { IndexRange = "1:2" });
            Assert.That(otherRange.ForceRecreate, Is.True);

            // The snapshot round trip restores the options default.
            MonitoredItemOptions restored = MonitoredItemStateSnapshot
                .AsOptions("test", options, sut.ClientHandle, sut.ServerId, null)
                .ToOptions();
            Assert.That(restored.IndexRange, Is.Null);
        }

        [Test]
        public void FailedChangeCanBeRequeued()
        {
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
                m_options, m_telemetry.CreateLogger("MonitoredItem"));
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change),
                Is.True);
            change!.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.BadNodeIdUnknown
            }, 0, [], new ResponseHeader());

            Assert.That(sut.HasPendingChanges, Is.False);
            Assert.That(sut.Error.StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(sut.TryRequeue(), Is.True);
            Assert.That(sut.HasPendingChanges, Is.True);
            Assert.That(sut.TryGetPendingChange(
                out MonitoredItem.Change? requeued), Is.True);
            Assert.That(requeued, Is.Not.SameAs(change));
            Assert.That(requeued.ForceRecreate, Is.True);
            Assert.That(sut.TryRequeue(), Is.False);
        }

        [Test]
        public void GoodButUnappliedChangeCanBeRequeued()
        {
            m_options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(m_context,
                m_options, m_telemetry.CreateLogger("MonitoredItem"));
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change),
                Is.True);
            change!.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                MonitoredItemId = 1,
                RevisedSamplingInterval = 1000,
                RevisedQueueSize = 1
            }, 0, [], new ResponseHeader());

            Assert.That(sut.Error.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(sut.Created, Is.True);
            Assert.That(sut.CurrentMonitoringMode,
                Is.Not.EqualTo(m_options.CurrentValue.MonitoringMode));
            Assert.That(sut.TryRequeue(), Is.True);
            Assert.That(sut.HasPendingChanges, Is.True);
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            Assert.That(sut.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change? c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change? change), Is.True);
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
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change? c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change? change), Is.False);
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
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change? c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.Zero);

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change? change), Is.False);
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
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change? c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change? change), Is.False);
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
            while (monitoredItem.TryGetPendingChange(out MonitoredItem.Change? c))
            {
                monitoredItem.CompleteChange(c);
            }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.That(monitoredItem.TryGetPendingChange(out MonitoredItem.Change? change), Is.False);
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
            string result = monitoredItem.ToString()!;

            // Assert
            Assert.That(result, Is.EqualTo($"Test#{monitoredItem.ClientHandle}|0 (TestItem)"));
        }

        /// <summary>
        /// Creates a healthy saved item that needs its revised parameters refreshed after transfer.
        /// </summary>
        private TestMonitoredItem CreateLoadedItem(MonitoringFilter filter)
        {
            m_options.Configure(options => options with
            {
                StartNodeId = new NodeId("loaded", 2),
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = TimeSpan.FromMilliseconds(20),
                QueueSize = 17,
                DiscardOldest = true,
                Filter = filter
            });
            var item = new TestMonitoredItem(m_context, m_options, m_telemetry.CreateLogger("MonitoredItem"));
            item.ApplyLoadState(new MonitoredItemLoadState("loaded", m_options, 42, 7, []));
            Assert.That(item.HasPendingChanges, Is.False);
            return item;
        }

        /// <summary>
        /// Applies a distinct set of desired monitoring parameters through the real options callback.
        /// </summary>
        private void UpdateMonitoringParameters(MonitoringFilter filter)
        {
            m_options.Configure(options => options with
            {
                SamplingInterval = TimeSpan.FromMilliseconds(250),
                QueueSize = 31,
                DiscardOldest = false,
                Filter = filter
            });
        }

        /// <summary>
        /// Completes every FIFO change using its requested parameters as the server's revised result.
        /// </summary>
        private static ArrayOf<MonitoredItemModifyRequest> ApplyPendingModifications(MonitoredItem item)
        {
            var requests = new List<MonitoredItemModifyRequest>();
            while (item.TryGetPendingChange(out MonitoredItem.Change? change))
            {
                Assert.That(change.ForceRecreate, Is.False);
                Assert.That(change.MonitoringModeChange, Is.Null);
                MonitoredItemModifyRequest request = change.BindModifyRequest()!;
                Assert.That(request, Is.Not.Null);
                requests.Add(request);
                change.SetModifyResult(request, new MonitoredItemModifyResult
                {
                    StatusCode = StatusCodes.Good,
                    RevisedSamplingInterval = request.RequestedParameters.SamplingInterval,
                    RevisedQueueSize = request.RequestedParameters.QueueSize
                }, 0, [], new ResponseHeader());
            }
            return requests.ToArrayOf();
        }

        /// <summary>
        /// Checks the exact latest parameters and preserved transferred identifiers sent to the server.
        /// </summary>
        private static void AssertUpdatedMonitoringParameters(
            MonitoredItemModifyRequest request, DataChangeFilter filter)
        {
            Assert.That(request.MonitoredItemId, Is.EqualTo(9));
            Assert.That(request.RequestedParameters.ClientHandle, Is.EqualTo(42));
            Assert.That(request.RequestedParameters.SamplingInterval, Is.EqualTo(250));
            Assert.That(request.RequestedParameters.QueueSize, Is.EqualTo(31));
            Assert.That(request.RequestedParameters.DiscardOldest, Is.False);
            Assert.That(request.RequestedParameters.Filter.TryGetValue(out DataChangeFilter? actual), Is.True);
            Assert.That(actual, Is.SameAs(filter));
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
