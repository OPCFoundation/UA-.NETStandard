/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests fluent monitored-item creation and lifecycle routing.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class MonitoredItemFluentTests
    {
        [Test]
        public async Task PreCreationHandlerCanRefuseRequestsAsync()
        {
            using var harness = await MonitoredItemHarness.CreateAsync(builder =>
            {
                builder.Node("Value")
                    .OnCreateMonitoredItem((context, cancellationToken) =>
                    {
                        ReadValueId item = context.Request.ItemToMonitor;
                        if (!context.Request.RequestedParameters.Filter.IsNull)
                        {
                            return new ValueTask<MonitoredItemCreateDecision>(
                                MonitoredItemCreateDecision.Refuse(
                                    StatusCodes.BadFilterNotAllowed));
                        }
                        if (!item.ParsedIndexRange.IsNull)
                        {
                            return new ValueTask<MonitoredItemCreateDecision>(
                                MonitoredItemCreateDecision.Refuse(
                                    StatusCodes.BadIndexRangeInvalid));
                        }
                        if (!item.DataEncoding.IsNull)
                        {
                            return new ValueTask<MonitoredItemCreateDecision>(
                                MonitoredItemCreateDecision.Refuse(
                                    StatusCodes.BadDataEncodingUnsupported));
                        }
                        return new ValueTask<MonitoredItemCreateDecision>(
                            MonitoredItemCreateDecision.UseDefault());
                    });
            }).ConfigureAwait(false);

            ServiceResult filterResult = await harness.CreateAndGetErrorAsync(
                CreateRequest(filter: new ExtensionObject(new DataChangeFilter())))
                .ConfigureAwait(false);
            ServiceResult rangeResult = await harness.CreateAndGetErrorAsync(
                CreateRequest(
                    indexRange: "0",
                    parsedIndexRange: NumericRange.Parse("0")))
                .ConfigureAwait(false);
            ServiceResult encodingResult = await harness.CreateAndGetErrorAsync(
                CreateRequest(dataEncoding: new QualifiedName("Default Binary")))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    filterResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadFilterNotAllowed));
                Assert.That(
                    rangeResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
                Assert.That(
                    encodingResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadDataEncodingUnsupported));
                Assert.That(harness.OwnedMonitoredItemCount, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomItemUsesStackLifecycleAndBatchHooksAsync(
            bool useSamplingGroups)
        {
            int created = 0;
            int modified = 0;
            int deleted = 0;
            int modeChanged = 0;
            int createBatches = 0;
            int deleteBatches = 0;

            using var harness = await MonitoredItemHarness.CreateAsync(
                builder =>
                {
                    builder.OnMonitoredItemsCreated((context, items, cancellationToken) =>
                    {
                        createBatches++;
                        Assert.That(items.Count, Is.EqualTo(1));
                        return default;
                    });
                    builder.OnMonitoredItemsDeleted((context, items, cancellationToken) =>
                    {
                        deleteBatches++;
                        Assert.That(items.Count, Is.EqualTo(1));
                        return default;
                    });
                    builder.Node("Value")
                        .OnCreateMonitoredItem((context, cancellationToken) =>
                            new ValueTask<MonitoredItemCreateDecision>(
                                MonitoredItemCreateDecision.Use(
                                    factoryContext =>
                                        new PushMonitoredItem(factoryContext))))
                        .OnMonitoredItemCreated((context, node, item) => created++)
                        .OnMonitoredItemModified((context, node, item, cancellationToken) =>
                        {
                            modified++;
                            return default;
                        })
                        .OnMonitoredItemDeleted((context, node, item, cancellationToken) =>
                        {
                            deleted++;
                            return default;
                        })
                        .OnMonitoringModeChanged((
                            context,
                            node,
                            item,
                            previousMode,
                            monitoringMode,
                            cancellationToken) =>
                        {
                            modeChanged++;
                            Assert.That(previousMode, Is.EqualTo(MonitoringMode.Reporting));
                            Assert.That(monitoringMode, Is.EqualTo(MonitoringMode.Sampling));
                            return default;
                        });
                },
                useSamplingGroups).ConfigureAwait(false);

            (ServiceResult createResult, IMonitoredItem? item) =
                await harness.CreateAsync(CreateRequest()).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(createResult), Is.True);
            Assert.That(item, Is.TypeOf<PushMonitoredItem>());

            var pushed = new DataValue(Variant.From(123), StatusCodes.Good);
            ((IDataChangeMonitoredItem2)item!).QueueValue(pushed, ServiceResult.Good, true);

            ServiceResult modifyResult = await harness.ModifyAsync(item)
                .ConfigureAwait(false);
            ServiceResult modeResult = await harness.SetModeAsync(
                item,
                MonitoringMode.Sampling).ConfigureAwait(false);
            ServiceResult deleteResult = await harness.DeleteAsync(item)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(modifyResult), Is.True);
                Assert.That(ServiceResult.IsGood(modeResult), Is.True);
                Assert.That(ServiceResult.IsGood(deleteResult), Is.True);
                Assert.That(created, Is.EqualTo(1));
                Assert.That(modified, Is.EqualTo(1));
                Assert.That(modeChanged, Is.EqualTo(1));
                Assert.That(deleted, Is.EqualTo(1));
                Assert.That(createBatches, Is.EqualTo(1));
                Assert.That(deleteBatches, Is.EqualTo(1));
                Assert.That(harness.OwnedMonitoredItemCount, Is.Zero);
            });
        }

        [Test]
        public async Task PollWhileMonitoredTracksActiveItemsAndFastestIntervalAsync()
        {
            int samples = 0;
            int firstSubscribers = 0;
            int lastSubscribers = 0;

            using var harness = await MonitoredItemHarness.CreateAsync(builder =>
            {
                IVariableBuilder<int> variable = builder.Variable<int>("Value");
                variable.OnFirstSubscriber((context, node, cancellationToken) =>
                {
                    firstSubscribers++;
                    return default;
                });
                variable.OnLastSubscriber((context, node, cancellationToken) =>
                {
                    lastSubscribers++;
                    return default;
                });
                variable.PollWhileMonitored(
                    TimeSpan.FromMilliseconds(50),
                    context => Interlocked.Increment(ref samples));
            }).ConfigureAwait(false);

            harness.Time.Advance(TimeSpan.FromSeconds(1));
            await DrainAsync().ConfigureAwait(false);
            Assert.That(samples, Is.Zero);

            (_, IMonitoredItem? slowItem) = await harness.CreateAsync(
                CreateRequest(samplingInterval: 300)).ConfigureAwait(false);
            await DrainAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(samples, Is.EqualTo(1));
                Assert.That(firstSubscribers, Is.EqualTo(1));
                Assert.That(lastSubscribers, Is.Zero);
            });

            harness.Time.Advance(TimeSpan.FromMilliseconds(299));
            await DrainAsync().ConfigureAwait(false);
            Assert.That(samples, Is.EqualTo(1));
            harness.Time.Advance(TimeSpan.FromMilliseconds(1));
            await WaitForAsync(
                () => Volatile.Read(ref samples) == 2,
                "The 300 ms poll must run after fake time reaches its due time.")
                .ConfigureAwait(false);
            Assert.That(samples, Is.EqualTo(2));

            (_, IMonitoredItem? fastItem) = await harness.CreateAsync(
                CreateRequest(samplingInterval: 100)).ConfigureAwait(false);
            await DrainAsync().ConfigureAwait(false);
            int afterFastActivation = samples;
            harness.Time.Advance(TimeSpan.FromMilliseconds(100));
            await WaitForAsync(
                () => Volatile.Read(ref samples) == afterFastActivation + 1,
                "The fastest active 100 ms poll must run after its due time.")
                .ConfigureAwait(false);
            Assert.That(samples, Is.EqualTo(afterFastActivation + 1));
            Assert.That(firstSubscribers, Is.EqualTo(1));

            await harness.SetModeAsync(
                fastItem!,
                MonitoringMode.Disabled).ConfigureAwait(false);
            await harness.SetModeAsync(
                slowItem!,
                MonitoringMode.Disabled).ConfigureAwait(false);
            await DrainAsync().ConfigureAwait(false);
            int stoppedAt = samples;
            harness.Time.Advance(TimeSpan.FromSeconds(1));
            await DrainAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(samples, Is.EqualTo(stoppedAt));
                Assert.That(lastSubscribers, Is.EqualTo(1));
            });

            await harness.SetModeAsync(
                slowItem!,
                MonitoringMode.Reporting).ConfigureAwait(false);
            await DrainAsync().ConfigureAwait(false);
            Assert.That(firstSubscribers, Is.EqualTo(2));

            await harness.DeleteAsync(fastItem!).ConfigureAwait(false);
            await harness.DeleteAsync(slowItem!).ConfigureAwait(false);
            await DrainAsync().ConfigureAwait(false);
            Assert.That(lastSubscribers, Is.EqualTo(2));
        }

        [Test]
        public async Task FirstSubscriberCanReenterMonitoringOperationsAsync()
        {
            MonitoredItemHarness harness = null!;
            IMonitoredItem? capturedItem = null;
            int firstSubscribers = 0;
            int lastSubscribers = 0;
            int samples = 0;

            harness = await MonitoredItemHarness.CreateAsync(builder =>
            {
                IVariableBuilder<int> variable = builder.Variable<int>("Value");
                variable.OnMonitoredItemCreated(
                    (context, node, item) => capturedItem = item);
                variable.OnFirstSubscriber(async (context, node, cancellationToken) =>
                {
                    firstSubscribers++;
                    ServiceResult result = await harness.SetModeAsync(
                        capturedItem!,
                        MonitoringMode.Disabled).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(result), Is.True);
                });
                variable.OnLastSubscriber((context, node, cancellationToken) =>
                {
                    lastSubscribers++;
                    return default;
                });
                variable.PollWhileMonitored(
                    TimeSpan.FromMilliseconds(50),
                    context => Interlocked.Increment(ref samples));
            }).ConfigureAwait(false);
            using (harness)
            {
                Task<(ServiceResult Error, IMonitoredItem? Item)> createTask =
                    harness.CreateAsync(CreateRequest()).AsTask();
                Task completed = await Task.WhenAny(
                    createTask,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

                Assert.That(
                    completed,
                    Is.SameAs(createTask),
                    "OnFirstSubscriber re-entry must not deadlock reconciliation.");
                (ServiceResult error, IMonitoredItem? item) =
                    await createTask.ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(error), Is.True);
                    Assert.That(item, Is.Not.Null);
                    Assert.That(item!.MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
                    Assert.That(firstSubscribers, Is.EqualTo(1));
                    Assert.That(lastSubscribers, Is.EqualTo(1));
                    Assert.That(samples, Is.Zero);
                });

                await harness.DeleteAsync(item!).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomFactoryFailureRollsBackRegistrationAsync(
            bool useSamplingGroups)
        {
            using var harness = await MonitoredItemHarness.CreateAsync(
                builder =>
                {
                    builder.Node("Value")
                        .OnCreateMonitoredItem((context, cancellationToken) =>
                            new ValueTask<MonitoredItemCreateDecision>(
                                MonitoredItemCreateDecision.Use(
                                    factoryContext =>
                                        throw new InvalidOperationException("factory failed"))));
                },
                useSamplingGroups).ConfigureAwait(false);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await harness.CreateAsync(CreateRequest())
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(harness.OwnedMonitoredItemCount, Is.Zero);
                Assert.That(harness.OwnedMonitoredNodeCount, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomItemCanQueueInitialValueAsync(
            bool useSamplingGroups)
        {
            using var harness = await MonitoredItemHarness.CreateAsync(
                builder =>
                {
                    builder.Node("Value")
                        .OnCreateMonitoredItem((context, cancellationToken) =>
                            new ValueTask<MonitoredItemCreateDecision>(
                                MonitoredItemCreateDecision.Use(
                                    factoryContext =>
                                        new PushMonitoredItem(factoryContext),
                                    queueInitialValue: true)));
                },
                useSamplingGroups).ConfigureAwait(false);

            (ServiceResult error, IMonitoredItem? item) =
                await harness.CreateAsync(CreateRequest()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(error), Is.True);
                Assert.That(item, Is.Not.Null);
                Assert.That(item!.IsReadyToPublish, Is.True);
            });
            await harness.DeleteAsync(item!).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FatalInitialReadRollsBackCustomItemAsync(
            bool useSamplingGroups)
        {
            using var harness = await MonitoredItemHarness.CreateAsync(
                builder =>
                {
                    builder.Node("Value")
                        .OnCreateMonitoredItem((context, cancellationToken) =>
                            new ValueTask<MonitoredItemCreateDecision>(
                                MonitoredItemCreateDecision.Use(
                                    factoryContext =>
                                        new PushMonitoredItem(factoryContext),
                                    queueInitialValue: true)));
                },
                useSamplingGroups).ConfigureAwait(false);

            (ServiceResult error, IMonitoredItem? item) = await harness.CreateAsync(
                CreateRequest(dataEncoding: new QualifiedName("Unsupported")))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    error.StatusCode,
                    Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
                Assert.That(item, Is.Null);
                Assert.That(harness.OwnedMonitoredItemCount, Is.Zero);
                Assert.That(harness.OwnedMonitoredNodeCount, Is.Zero);
            });
        }

        private static MonitoredItemCreateRequest CreateRequest(
            ExtensionObject filter = default,
            string? indexRange = null,
            NumericRange parsedIndexRange = default,
            QualifiedName dataEncoding = default,
            double samplingInterval = 100)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = NodeId.Null,
                    AttributeId = Attributes.Value,
                    IndexRange = indexRange,
                    ParsedIndexRange = parsedIndexRange,
                    DataEncoding = dataEncoding
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = samplingInterval,
                    QueueSize = 10,
                    DiscardOldest = true,
                    Filter = filter
                }
            };
        }

        private static async ValueTask DrainAsync()
        {
            for (int ii = 0; ii < 10; ii++)
            {
                await Task.Yield();
            }
        }

        private static async ValueTask WaitForAsync(
            Func<bool> condition,
            string message,
            int timeoutMilliseconds = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(
                timeoutMilliseconds);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail(message);
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private sealed class PushMonitoredItem : MonitoredItem
        {
            public PushMonitoredItem(MonitoredItemFactoryContext context)
                : base(
                    context.Server,
                    context.NodeManager,
                    context.Handle,
                    context.SubscriptionId,
                    context.MonitoredItemId,
                    context.Request.ItemToMonitor,
                    context.DiagnosticsMasks,
                    context.TimestampsToReturn,
                    context.Request.MonitoringMode,
                    context.Request.RequestedParameters.ClientHandle,
                    context.Filter,
                    context.Filter,
                    context.EuRange,
                    context.SamplingInterval,
                    context.QueueSize,
                    context.Request.RequestedParameters.DiscardOldest,
                    sourceSamplingInterval: 0,
                    context.CreateDurable)
            {
            }
        }

        private sealed class MonitoredItemHarness : IDisposable
        {
            private MonitoredItemHarness(
                TestMonitoredItemManager manager,
                MonitoredItemQueueFactory queueFactory,
                FakeTimeProvider time)
            {
                Manager = manager;
                m_queueFactory = queueFactory;
                Time = time;
            }

            public TestMonitoredItemManager Manager { get; }

            public int OwnedMonitoredItemCount => Manager.OwnedMonitoredItemCount;

            public int OwnedMonitoredNodeCount => Manager.OwnedMonitoredNodeCount;

            public FakeTimeProvider Time { get; }

            public static async ValueTask<MonitoredItemHarness> CreateAsync(
                Action<INodeManagerBuilder> configure,
                bool useSamplingGroups = false)
            {
                var time = new FakeTimeProvider();
                Mock<IServerInternal> server =
                    CreateServer(time, out MonitoredItemQueueFactory queueFactory);
                var manager = new TestMonitoredItemManager(
                    server.Object,
                    useSamplingGroups);
                try
                {
                    await manager.InitializeAsync(configure).ConfigureAwait(false);
                    return new MonitoredItemHarness(manager, queueFactory, time);
                }
                catch
                {
                    manager.Dispose();
                    queueFactory.Dispose();
                    throw;
                }
            }

            public async ValueTask<ServiceResult> CreateAndGetErrorAsync(
                MonitoredItemCreateRequest request)
            {
                (ServiceResult error, _) = await CreateAsync(request).ConfigureAwait(false);
                return error;
            }

            public async ValueTask<(ServiceResult Error, IMonitoredItem? Item)> CreateAsync(
                MonitoredItemCreateRequest request)
            {
                request.ItemToMonitor.NodeId = Manager.VariableId;
                var errors = new List<ServiceResult> { ServiceResult.Good };
                var filterErrors = new List<MonitoringFilterResult> { null! };
                var monitoredItems = new List<IMonitoredItem> { null! };

                await Manager.CreateMonitoredItemsAsync(
                    NewContext(RequestType.CreateMonitoredItems),
                    subscriptionId: 1,
                    publishingInterval: 1000,
                    TimestampsToReturn.Both,
                    [request],
                    errors,
                    filterErrors,
                    monitoredItems,
                    createDurable: false,
                    new MonitoredItemIdFactory()).ConfigureAwait(false);

                return (errors[0], monitoredItems[0]);
            }

            public async ValueTask<ServiceResult> ModifyAsync(IMonitoredItem item)
            {
                var request = new MonitoredItemModifyRequest
                {
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 2,
                        SamplingInterval = 50,
                        QueueSize = 5,
                        DiscardOldest = true
                    }
                };
                var errors = new List<ServiceResult> { ServiceResult.Good };
                var filterErrors = new List<MonitoringFilterResult> { null! };

                await Manager.ModifyMonitoredItemsAsync(
                    NewContext(RequestType.ModifyMonitoredItems),
                    TimestampsToReturn.Both,
                    [item],
                    [request],
                    errors,
                    filterErrors).ConfigureAwait(false);
                return errors[0];
            }

            public async ValueTask<ServiceResult> SetModeAsync(
                IMonitoredItem item,
                MonitoringMode monitoringMode)
            {
                var processed = new List<bool> { false };
                var errors = new List<ServiceResult> { ServiceResult.Good };
                await Manager.SetMonitoringModeAsync(
                    NewContext(RequestType.SetMonitoringMode),
                    monitoringMode,
                    [item],
                    processed,
                    errors).ConfigureAwait(false);
                return errors[0];
            }

            public async ValueTask<ServiceResult> DeleteAsync(IMonitoredItem item)
            {
                var processed = new List<bool> { false };
                var errors = new List<ServiceResult> { ServiceResult.Good };
                await Manager.DeleteMonitoredItemsAsync(
                    NewContext(RequestType.DeleteMonitoredItems),
                    [item],
                    processed,
                    errors).ConfigureAwait(false);
                return errors[0];
            }

            public void Dispose()
            {
                Manager.Dispose();
                m_queueFactory.Dispose();
            }

            private static OperationContext NewContext(RequestType requestType)
            {
                return new OperationContext(
                    new RequestHeader(),
                    null,
                    requestType,
                    RequestLifetime.None);
            }

            private static Mock<IServerInternal> CreateServer(
                FakeTimeProvider time,
                out MonitoredItemQueueFactory queueFactory)
            {
                var server = new Mock<IServerInternal>();
                server.As<ITimeProviderProvider>()
                    .SetupGet(value => value.TimeProvider)
                    .Returns(time);

                var master = new Mock<IMasterNodeManager>();
                var configuration = new Mock<IConfigurationNodeManager>();
                var core = new Mock<ICoreNodeManager>();
                var namespaceUris = new NamespaceTable();
                namespaceUris.Append(kNamespaceUri);

                server.SetupGet(value => value.NamespaceUris)
                    .Returns(namespaceUris);
                server.SetupGet(value => value.ServerUris)
                    .Returns(new StringTable());
                server.SetupGet(value => value.TypeTree)
                    .Returns(new TypeTable(namespaceUris));
                server.SetupGet(value => value.Factory)
                    .Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.NodeManager)
                    .Returns(master.Object);
                server.SetupGet(value => value.CoreNodeManager)
                    .Returns(core.Object);
                server.SetupGet(value => value.IsRunning)
                    .Returns(true);
                master.SetupGet(value => value.ConfigurationNodeManager)
                    .Returns(configuration.Object);
                master.SetupGet(value => value.CoreNodeManager)
                    .Returns(core.Object);

                var telemetry = new Mock<ITelemetryContext>();
                server.SetupGet(value => value.Telemetry)
                    .Returns(telemetry.Object);
                queueFactory = new MonitoredItemQueueFactory(telemetry.Object);
                server.SetupGet(value => value.MonitoredItemQueueFactory)
                    .Returns(queueFactory);
                server.SetupGet(value => value.DefaultSystemContext)
                    .Returns(new ServerSystemContext(server.Object));
                return server;
            }

            private readonly MonitoredItemQueueFactory m_queueFactory;
        }

        private sealed class TestMonitoredItemManager : FluentNodeManagerBase
        {
            public TestMonitoredItemManager(
                IServerInternal server,
                bool useSamplingGroups)
                : base(
                    server,
                    CreateConfiguration(),
                    useSamplingGroups,
                    server.Telemetry.CreateLogger<TestMonitoredItemManager>(),
                    kNamespaceUri)
            {
            }

            public int OwnedMonitoredItemCount => MonitoredItems.Count;

            public int OwnedMonitoredNodeCount => MonitoredNodes.Count;

            public NodeId VariableId { get; private set; }

            public async ValueTask InitializeAsync(Action<INodeManagerBuilder> configure)
            {
                var variable =
                    BaseDataVariableState<int>.With<VariantBuilder>(null!);
                variable.CreateAsPredefinedNode(SystemContext);
                ushort namespaceIndex = NamespaceIndexes[0];
                variable.NodeId = VariableId = new NodeId("Value", namespaceIndex);
                variable.BrowseName = new QualifiedName("Value", namespaceIndex);
                variable.DisplayName = new LocalizedText("Value");
                variable.DataType = DataTypeIds.Int32;
                variable.ValueRank = ValueRanks.Scalar;
                variable.Value = 0;
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                await AddPredefinedNodeAsync(variable).ConfigureAwait(false);

                NodeManagerBuilder builder = CreateFluentBuilder(namespaceIndex);
                configure(builder);
                builder.Seal();
            }

            private static ApplicationConfiguration CreateConfiguration()
            {
                return new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 200,
                        AvailableSamplingRates = []
                    }
                };
            }
        }

        private const string kNamespaceUri =
            "urn:opcfoundation:server:tests:fluent-monitored-item";
    }
}
