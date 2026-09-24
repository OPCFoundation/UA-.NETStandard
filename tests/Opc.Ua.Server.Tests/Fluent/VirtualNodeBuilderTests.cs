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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Tests.NodeManager;

#nullable enable

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests the on-demand node-family fluent surface.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class VirtualNodeBuilderTests
    {
        [TestCase("resolver")]
        [TestCase("id")]
        [TestCase("type")]
        [TestCase("service")]
        [TestCase("ambiguity")]
        [TestCase("unrelated-cancellation")]
        public async Task VirtualResolutionFailureDoesNotAbortReadBatchAsync(string failure)
        {
            await using var manager = new TestVirtualManager();
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            manager.Builder.ResolveNodes(id => id == good || id == bad, (_, id, _) =>
                {
                    if (id == bad)
                    {
                        if (failure == "resolver")
                        {
                            throw new InvalidOperationException("Backend lookup failed.");
                        }
                        if (failure == "unrelated-cancellation")
                        {
                            throw new OperationCanceledException("Backend cancelled its own lookup.");
                        }
                        if (failure == "service")
                        {
                            throw new ServiceResultException(StatusCodes.BadCommunicationError);
                        }
                        if (failure == "type")
                        {
                            return new ValueTask<NodeState?>(new BaseObjectState(null) { NodeId = id });
                        }
                    }
                    return new ValueTask<NodeState?>(new BaseDataVariableState(null)
                    {
                        NodeId = id == bad && failure == "id" ? manager.VirtualId("wrong") : id,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentRead,
                        UserAccessLevel = AccessLevels.CurrentRead,
                        Value = 42
                    });
                })
                .OnRead((_, _, ref value) =>
                {
                    value = new Variant(42);
                    return ServiceResult.Good;
                });
            if (failure == "ambiguity")
            {
                manager.Builder.ResolveNodes(id => id == bad, (_, _, _) => new ValueTask<NodeState?>((NodeState?)null));
            }
            await manager.Builder.SealAsync().ConfigureAwait(false);
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var values = new DataValue[3];
            ServiceResult[] errors =
            [
                StatusCodes.BadNodeIdUnknown,
                StatusCodes.BadNodeIdUnknown,
                StatusCodes.BadNodeIdUnknown
            ];
            await manager.ReadAsync(context, 0,
                [
                    new ReadValueId { NodeId = good, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = bad, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = good, AttributeId = Attributes.Value }
                ], values, errors).ConfigureAwait(false);

            StatusCode expectedStatus = failure switch
            {
                "id" => StatusCodes.BadNodeIdInvalid,
                "type" => StatusCodes.BadTypeMismatch,
                "service" => StatusCodes.BadCommunicationError,
                _ => StatusCodes.BadNodeIdUnknown
            };
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(errors[1].StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(ServiceResult.IsGood(errors[2]), Is.True);
            Assert.That(values[0].WrappedValue.TryGetValue(out int first), Is.True);
            Assert.That(first, Is.EqualTo(42));
            Assert.That(values[2].WrappedValue.TryGetValue(out int last), Is.True);
            Assert.That(last, Is.EqualTo(42));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task VirtualValidationFailureDoesNotAbortDispatchedReadAsync(bool withIdentity)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using MonitoredItemQueueFactory queueFactory = queues;
            await using var manager = new TestVirtualManager(server.Object);
            using MasterNodeManager master = CreateMaster(server, manager);
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            RegisterVariables(manager, bad);
            await manager.Builder.SealAsync().ConfigureAwait(false);
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None,
                withIdentity ? new UserIdentity() : null);

            (ArrayOf<DataValue> values, _) = await master.ReadAsync(
                context, 0, TimestampsToReturn.Both,
                [
                    new ReadValueId { NodeId = good, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = bad, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = good, AttributeId = Attributes.Value }
                ]).ConfigureAwait(false);

            Assert.That(values, Has.Count.EqualTo(3));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(values[2].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].WrappedValue, Is.EqualTo(Variant.From(42)));
            Assert.That(values[2].WrappedValue, Is.EqualTo(Variant.From(42)));
        }

        [Test]
        public async Task VirtualValidationFailureDoesNotAbortWriteBatchAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using MonitoredItemQueueFactory queueFactory = queues;
            await using var manager = new TestVirtualManager(server.Object);
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            var written = new List<Variant>();
            RegisterVariables(manager, bad).OnWrite((_, _, ref value) =>
            {
                written.Add(value);
                return ServiceResult.Good;
            });
            await manager.Builder.SealAsync().ConfigureAwait(false);
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Write, RequestLifetime.None);
            ServiceResult[] errors = CreateBatchErrors();

            await manager.WriteAsync(context,
                [
                    new WriteValue { NodeId = new NodeId("foreign", 0), AttributeId = Attributes.Value },
                    new WriteValue
                    {
                        NodeId = good, AttributeId = Attributes.Value, Value = new DataValue(Variant.From(1))
                    },
                    new WriteValue
                    {
                        NodeId = bad, AttributeId = Attributes.Value, Value = new DataValue(Variant.From(2))
                    },
                    new WriteValue
                    {
                        NodeId = good, AttributeId = Attributes.Value, Value = new DataValue(Variant.From(3))
                    }
                ], errors).ConfigureAwait(false);

            AssertIsolatedValidationFailure(errors);
            Assert.That(written, Is.EqualTo([Variant.From(1), Variant.From(3)]));
        }

        [TestCase("raw")]
        [TestCase("modified")]
        [TestCase("processed")]
        [TestCase("at-time")]
        [TestCase("events")]
        [TestCase("release")]
        public async Task VirtualValidationFailureDoesNotAbortHistoryReadBatchAsync(string operation)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using MonitoredItemQueueFactory queueFactory = queues;
            await using var manager = new TestVirtualManager(server.Object);
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            var handled = new List<NodeId>();
            RegisterVariables(manager, bad).OnHistoryRead((_, source, _, _, release, _, result) =>
            {
                Assert.That(release, Is.EqualTo(operation == "release"));
                handled.Add(source.NodeId);
                result.ContinuationPoint = [42];
                return ServiceResult.Good;
            });
            await manager.Builder.SealAsync().ConfigureAwait(false);
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            HistoryReadDetails details = operation switch
            {
                "processed" => new ReadProcessedDetails
                {
                    StartTime = start,
                    EndTime = start.AddHours(1),
                    ProcessingInterval = 1000,
                    AggregateType =
                    [
                        ObjectIds.AggregateFunction_Average, ObjectIds.AggregateFunction_Average,
                        ObjectIds.AggregateFunction_Average, ObjectIds.AggregateFunction_Average
                    ]
                },
                "at-time" => new ReadAtTimeDetails { ReqTimes = [start] },
                "events" => new ReadEventDetails(),
                _ => new ReadRawModifiedDetails
                {
                    StartTime = start,
                    EndTime = start.AddHours(1),
                    IsReadModified = operation == "modified"
                }
            };
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
            NodeId[] ids = [new NodeId("foreign", 0), good, bad, good];
            var requests = new HistoryReadValueId[ids.Length];
            for (int ii = 0; ii < ids.Length; ii++)
            {
                requests[ii] = new HistoryReadValueId
                {
                    NodeId = ids[ii],
                    ContinuationPoint = operation == "events" ? ByteString.From([1]) : default
                };
            }
            ServiceResult[] errors = CreateBatchErrors();
            var results = new HistoryReadResult[ids.Length];

            await manager.HistoryReadAsync(
                context, details, TimestampsToReturn.Both, operation == "release",
                requests, results, errors).ConfigureAwait(false);

            AssertIsolatedValidationFailure(errors);
            Assert.That(handled, Is.EqualTo([good, good]));
            Assert.That(results[1].ContinuationPoint, Is.EqualTo(ByteString.From([42])));
            Assert.That(results[3].ContinuationPoint, Is.EqualTo(ByteString.From([42])));
        }

        [TestCase("data")]
        [TestCase("structure")]
        [TestCase("events")]
        [TestCase("delete-raw")]
        [TestCase("delete-at-time")]
        [TestCase("delete-events")]
        public async Task VirtualValidationFailureDoesNotAbortHistoryUpdateBatchAsync(string operation)
        {
            await using var manager = new TestVirtualManager();
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            var handled = new List<NodeId>();
            RegisterVariables(manager, bad).OnHistoryUpdate((_, source, _, result) =>
            {
                handled.Add(source.NodeId);
                result.OperationResults = [StatusCodes.Good];
                return ServiceResult.Good;
            });
            await manager.Builder.SealAsync().ConfigureAwait(false);
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            NodeId[] ids = [new NodeId("foreign", 0), good, bad, good];
            var requests = new HistoryUpdateDetails[ids.Length];
            for (int ii = 0; ii < ids.Length; ii++)
            {
                requests[ii] = operation switch
                {
                    "data" => new UpdateDataDetails(),
                    "structure" => new UpdateStructureDataDetails(),
                    "events" => new UpdateEventDetails(),
                    "delete-raw" => new DeleteRawModifiedDetails(),
                    "delete-at-time" => new DeleteAtTimeDetails(),
                    "delete-events" => new DeleteEventDetails(),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
                requests[ii].NodeId = ids[ii];
            }
            ServiceResult[] errors = CreateBatchErrors();
            var results = new HistoryUpdateResult[ids.Length];

            await manager.HistoryUpdateAsync(
                context, requests[0].GetType(), requests, results, errors).ConfigureAwait(false);

            AssertIsolatedValidationFailure(errors);
            Assert.That(handled, Is.EqualTo([good, good]));
            Assert.That(results[1].OperationResults, Has.Count.EqualTo(1));
            Assert.That(results[1].OperationResults[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(results[3].OperationResults, Has.Count.EqualTo(1));
            Assert.That(results[3].OperationResults[0], Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task VirtualValidationFailureDoesNotAbortCallBatchAsync(
            bool throughDispatcher, bool failSecondResolution)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using MonitoredItemQueueFactory queueFactory = queues;
            await using var manager = new TestVirtualManager(server.Object);
            using MasterNodeManager master = CreateMaster(server, manager);
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            NodeId methodId = manager.VirtualId("method");
            int badResolutions = 0;
            var called = new List<NodeId>();
            manager.Builder.ResolveNodes(id => id == good || id == bad, (_, id, _) =>
            {
                if (id == bad && ++badResolutions == (failSecondResolution ? 2 : 1))
                {
                    throw new ServiceResultException(StatusCodes.BadCommunicationError);
                }
                var source = new BaseObjectState(null) { NodeId = id };
                source.AddChild(new MethodState(source)
                {
                    NodeId = methodId,
                    BrowseName = new QualifiedName("method", id.NamespaceIndex),
                    Executable = true,
                    UserExecutable = true,
                    OnCallMethod2Async = (_, _, objectId, _, _, _) =>
                    {
                        called.Add(objectId);
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    }
                });
                return new ValueTask<NodeState?>(source);
            });
            await manager.Builder.SealAsync().ConfigureAwait(false);
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);
            ArrayOf<CallMethodRequest> requests =
            [
                new CallMethodRequest { ObjectId = new NodeId("foreign", 0), MethodId = methodId },
                new CallMethodRequest { ObjectId = good, MethodId = methodId },
                new CallMethodRequest { ObjectId = bad, MethodId = methodId },
                new CallMethodRequest { ObjectId = good, MethodId = methodId }
            ];
            ServiceResult[] errors = CreateBatchErrors();
            if (throughDispatcher)
            {
                (ArrayOf<CallMethodResult> results, _) = await master.CallAsync(context, requests)
                    .ConfigureAwait(false);
                for (int ii = 0; ii < errors.Length; ii++)
                {
                    errors[ii] = results[ii].StatusCode;
                }
            }
            else
            {
                await manager.CallAsync(context, requests, new CallMethodResult[4], errors).ConfigureAwait(false);
            }

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(ServiceResult.IsGood(errors[1]), Is.True);
            Assert.That(errors[2].StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(ServiceResult.IsGood(errors[3]), Is.True);
            Assert.That(called, Is.EqualTo([good, good]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task VirtualValidationFailureDoesNotAbortMonitoredItemBatchAsync(bool restore)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using MonitoredItemQueueFactory queueFactory = queues;
            server.SetupGet(value => value.IsRunning).Returns(false);
            var logger = new Mock<ILogger>();
            logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            Mock.Get(server.Object.Telemetry).SetupGet(value => value.LoggerFactory).Returns(loggerFactory.Object);
            await using var manager = new TestVirtualManager(server.Object);
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            RegisterVariables(manager, bad);
            await manager.Builder.SealAsync().ConfigureAwait(false);
            NodeId[] ids = [new NodeId("foreign", 0), good, bad, good];
            var monitoredItems = new IMonitoredItem[ids.Length];

            if (restore)
            {
                var requests = new IStoredMonitoredItem[ids.Length];
                for (int ii = 0; ii < ids.Length; ii++)
                {
                    var stored = new Mock<IStoredMonitoredItem>();
                    stored.SetupAllProperties();
                    stored.Object.NodeId = ids[ii];
                    stored.Object.Id = (uint)ii + 1;
                    stored.Object.SubscriptionId = 1;
                    stored.Object.AttributeId = Attributes.Value;
                    stored.Object.QueueSize = 1;
                    stored.Object.SamplingInterval = 1000;
                    stored.Object.MonitoringMode = MonitoringMode.Disabled;
                    stored.Object.LastValue = new DataValue(Variant.From(42));
                    stored.Object.LastError = ServiceResult.Good;
                    requests[ii] = stored.Object;
                }
                await manager.RestoreMonitoredItemsAsync(requests, monitoredItems, new UserIdentity())
                    .ConfigureAwait(false);

                Assert.That(requests[0].IsRestored, Is.False);
                Assert.That(requests[1].IsRestored, Is.True);
                Assert.That(requests[2].IsRestored, Is.True);
                Assert.That(requests[3].IsRestored, Is.True);
                logger.Verify(value => value.Log(
                    LogLevel.Error,
                    It.Is<EventId>(id => id.Name == "MonitoredItemRestoreFailed"),
                    It.IsAny<It.IsAnyType>(),
                    It.Is<Exception>(error => error is ServiceResultException &&
                        ((ServiceResultException)error).StatusCode == StatusCodes.BadNodeIdInvalid),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            }
            else
            {
                var requests = new MonitoredItemCreateRequest[ids.Length];
                for (int ii = 0; ii < ids.Length; ii++)
                {
                    requests[ii] = new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = ids[ii], AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Disabled,
                        RequestedParameters = new MonitoringParameters { QueueSize = 1, SamplingInterval = 1000 }
                    };
                }
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                ServiceResult[] errors = CreateBatchErrors();
                await manager.CreateMonitoredItemsAsync(
                    context, 1, 1000, TimestampsToReturn.Both, requests, errors,
                    new MonitoringFilterResult[ids.Length], monitoredItems, false, new MonitoredItemIdFactory())
                    .ConfigureAwait(false);
                AssertIsolatedValidationFailure(errors);
            }

            Assert.That(monitoredItems[0], Is.Null);
            Assert.That(monitoredItems[1], Is.Not.Null);
            Assert.That(monitoredItems[1].NodeId, Is.EqualTo(good));
            Assert.That(monitoredItems[2], Is.Null);
            Assert.That(monitoredItems[3], Is.Not.Null);
            Assert.That(monitoredItems[3].NodeId, Is.EqualTo(good));
        }

        [Test]
        public async Task VirtualValidationFailureDoesNotAbortEventCreationBatchAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using MonitoredItemQueueFactory queueFactory = queues;
            using var events = new EventManager(server.Object, 100, 100);
            server.SetupGet(value => value.EventManager).Returns(events);
            await using var manager = new TestVirtualManager(server.Object);
            using MasterNodeManager master = CreateMaster(server, manager);
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            manager.Builder.ResolveNodes(id => id == good || id == bad, (_, id, _) =>
                new ValueTask<NodeState?>(new BaseObjectState(null)
                {
                    NodeId = id == bad ? manager.VirtualId("wrong") : id,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                }));
            await manager.Builder.SealAsync().ConfigureAwait(false);
            NodeId[] ids = [good, bad, good];
            var requests = new MonitoredItemCreateRequest[ids.Length];
            for (int ii = 0; ii < ids.Length; ii++)
            {
                requests[ii] = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = ids[ii], AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        QueueSize = 1,
                        Filter = new ExtensionObject(new EventFilter
                        {
                            SelectClauses =
                            [
                                new SimpleAttributeOperand
                                {
                                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                    AttributeId = Attributes.Value,
                                    BrowsePath = [new QualifiedName(BrowseNames.EventId)]
                                }
                            ]
                        })
                    }
                };
            }
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
            var errors = new ServiceResult[ids.Length];
            var monitoredItems = new IMonitoredItem[ids.Length];

            await master.CreateMonitoredItemsAsync(
                context, 1, 1000, TimestampsToReturn.Both, requests, errors,
                new MonitoringFilterResult[ids.Length], monitoredItems, false).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(ServiceResult.IsGood(errors[2]), Is.True);
            Assert.That(monitoredItems[0].NodeId, Is.EqualTo(good));
            Assert.That(monitoredItems[1], Is.Null);
            Assert.That(monitoredItems[2].NodeId, Is.EqualTo(good));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task VirtualValidationFailureDoesNotAbortTranslateBatchAsync(bool withIdentity)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using MonitoredItemQueueFactory queueFactory = queues;
            await using var manager = new TestVirtualManager(server.Object);
            using MasterNodeManager master = CreateMaster(server, manager);
            NodeId good = manager.VirtualId("good");
            NodeId bad = manager.VirtualId("bad");
            NodeId target = manager.VirtualId("target");
            var targetName = new QualifiedName("Target", manager.TestNamespaceIndex);
            manager.Builder.ResolveNodes(id => id == good || id == bad || id == target, (_, id, _) =>
            {
                var source = new BaseObjectState(null)
                {
                    NodeId = id == bad ? manager.VirtualId("wrong") : id,
                    BrowseName = targetName
                };
                if (id == good)
                {
                    source.AddReference(ReferenceTypeIds.HasComponent, false, target);
                }
                return new ValueTask<NodeState?>(source);
            });
            await manager.Builder.SealAsync().ConfigureAwait(false);
            NodeId[] ids = [good, bad, good];
            var requests = new BrowsePath[ids.Length];
            for (int ii = 0; ii < ids.Length; ii++)
            {
                requests[ii] = new BrowsePath
                {
                    StartingNode = ids[ii],
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                                TargetName = targetName
                            }
                        ]
                    }
                };
            }
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.TranslateBrowsePathsToNodeIds, RequestLifetime.None,
                withIdentity ? new UserIdentity() : null);

            (ArrayOf<BrowsePathResult> results, _) = await master.TranslateBrowsePathsToNodeIdsAsync(context, requests)
                .ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].Targets, Has.Count.EqualTo(1));
            Assert.That(results[0].Targets[0].TargetId, Is.EqualTo(new ExpandedNodeId(target)));
            Assert.That(results[2].Targets, Has.Count.EqualTo(1));
            Assert.That(results[2].Targets[0].TargetId, Is.EqualTo(new ExpandedNodeId(target)));
        }

        [Test]
        public async Task VirtualResolverCancellationStopsReadBatchAsync()
        {
            await using var manager = new TestVirtualManager();
            using var cancellation = new CancellationTokenSource();
            NodeId cancelled = manager.VirtualId("cancelled");
            NodeId later = manager.VirtualId("later");
            var resolved = new List<NodeId>();
            manager.Builder.ResolveNodes(id => id == cancelled || id == later, (_, id, token) =>
            {
                resolved.Add(id);
                cancellation.Cancel();
                return new ValueTask<NodeState?>(Task.FromCanceled<NodeState?>(token));
            });
            await manager.Builder.SealAsync().ConfigureAwait(false);
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            ServiceResult[] errors = [StatusCodes.BadNodeIdUnknown, StatusCodes.BadNodeIdUnknown];

            Assert.CatchAsync<OperationCanceledException>(async () => await manager.ReadAsync(
                context, 0,
                [
                    new ReadValueId { NodeId = cancelled, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = later, AttributeId = Attributes.Value }
                ], new DataValue[2], errors, cancellation.Token).ConfigureAwait(false));

            Assert.That(resolved, Is.EqualTo([cancelled]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task VirtualRetirementRechecksAConcurrentNewSubscriberAsync(bool queuedCreate)
        {
            await using var manager = new TestVirtualManager();
            NodeId id = manager.VirtualId("raced");
            int acquired = 0;
            int released = 0;
            int samples = 0;
            manager.Builder.ResolveNodes(value => value == id, (_, _, _) =>
                    new ValueTask<NodeState?>(new BaseDataVariableState(null) { NodeId = id }))
                .OnFirstSubscriber((_, _, _) =>
                {
                    acquired++;
                    return default;
                })
                .OnLastSubscriber((_, _, _) =>
                {
                    released++;
                    return default;
                })
                .PollWhileMonitored(TimeSpan.FromHours(1), (_, _, _) =>
                    new ValueTask<int>(Interlocked.Increment(ref samples)));
            await manager.Builder.SealAsync().ConfigureAwait(false);
            (NodeHandle? handle, NodeState? node) = await manager.ResolveAsync(
                id, new Dictionary<NodeId, NodeState>()).ConfigureAwait(false);
            Mock<ISampledDataChangeMonitoredItem> first = CreateMonitoredItem(1, id);
            Mock<ISampledDataChangeMonitoredItem> second = CreateMonitoredItem(2, id);
            first.SetupGet(value => value.ManagerHandle).Returns(handle!);
            second.SetupGet(value => value.ManagerHandle).Returns(handle!);
            await manager.NotifyCreatedAsync(handle!, first.Object).ConfigureAwait(false);
            object?[] arguments = [id, null];
            var registration = (MonitoredSourceRegistration)typeof(MonitoredSourceRegistry)
                .GetMethod("Find", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(manager.MonitoredSources, arguments)!;
            var family = (VirtualNodeRegistration)arguments[1]!;
            bool empty = await registration.OnDeletedAsync(manager.SystemContext, node!, first.Object)
                .ConfigureAwait(false);
            Assert.That(empty, Is.True);
            MethodInfo method = typeof(MonitoredSourceRegistry)
                .GetMethod("RemoveVirtualInstanceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
#if NET8_0_OR_GREATER
            Func<VirtualNodeRegistration, NodeId, MonitoredSourceRegistration, ValueTask> retire =
                method.CreateDelegate<Func<VirtualNodeRegistration, NodeId, MonitoredSourceRegistration, ValueTask>>(
                    manager.MonitoredSources);
#else
            var retire = (Func<VirtualNodeRegistration, NodeId, MonitoredSourceRegistration, ValueTask>)
                method.CreateDelegate(
                    typeof(Func<VirtualNodeRegistration, NodeId, MonitoredSourceRegistration, ValueTask>),
                    manager.MonitoredSources);
#endif
            if (queuedCreate)
            {
                var gate = (SemaphoreSlim)typeof(MonitoredSourceRegistration)
                    .GetField("m_updateLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registration)!;
                await gate.WaitAsync().ConfigureAwait(false);
                Task creating;
                Task retiring;
                try
                {
                    creating = manager.MonitoredSources.OnCreatedAsync(manager.SystemContext, node!, second.Object)
                        .AsTask();
                    retiring = retire(family, id, registration).AsTask();
                }
                finally
                {
                    gate.Release();
                }
                await Task.WhenAll(creating, retiring).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            else
            {
                await manager.NotifyCreatedAsync(handle!, second.Object).ConfigureAwait(false);
                await retire(family, id, registration).ConfigureAwait(false);
            }

            Assert.That(acquired, Is.EqualTo(2));
            Assert.That(samples, Is.EqualTo(2));
            await manager.MonitoredSources.OnModifiedAsync(manager.SystemContext, node!, second.Object)
                .ConfigureAwait(false);
            Assert.That(acquired, Is.EqualTo(2));
            Assert.That(released, Is.EqualTo(1));
            Assert.That(samples, Is.EqualTo(2));
            await manager.NotifyDeletedAsync(handle!, second.Object).ConfigureAwait(false);
            Assert.That(released, Is.EqualTo(acquired));
        }

        [Test]
        public void ConfigurationIsRetainedByFluentManager()
        {
            var configuration = new ApplicationConfiguration();
            using var manager = new TestVirtualManager(configuration);

            Assert.That(manager.StartupConfiguration, Is.SameAs(configuration));
        }

        [Test]
        public void ConfigurationlessConstructorExposesNull()
        {
            using var manager = new TestVirtualManager();

            Assert.That(manager.StartupConfiguration, Is.Null);
        }

        [Test]
        public async Task VirtualNodeMaterializesWithRequestedIdAndHandlersAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Temperature");
            NodeId childId = manager.VirtualId("Child");
            int resolverCalls = 0;

            manager.Builder.ResolveNodes(
                    id => id == requestedId,
                    (context, id, cancellationToken) =>
                    {
                        resolverCalls++;
                        var variable =
                            BaseDataVariableState<int>.With<VariantBuilder>(null!);
                        variable.BrowseName =
                            new QualifiedName("Temperature", id.NamespaceIndex);
                        variable.DisplayName = new LocalizedText("Temperature");
                        variable.DataType = DataTypeIds.Int32;
                        variable.ValueRank = ValueRanks.Scalar;
                        variable.AccessLevel = AccessLevels.CurrentRead;
                        variable.UserAccessLevel = AccessLevels.CurrentRead;
                        variable.Value = 0;
                        return new ValueTask<NodeState?>(variable);
                    })
                .OnRead((context, node, ref value) =>
                {
                    value = Variant.From(42);
                    return ServiceResult.Good;
                })
                .OnCreateBrowser((
                    context,
                    node,
                    view,
                    referenceType,
                    includeSubtypes,
                    browseDirection,
                    browseName,
                    additionalReferences,
                    internalOnly) =>
                {
                    var browser = new NodeBrowser(
                        context,
                        view,
                        referenceType,
                        includeSubtypes,
                        browseDirection,
                        browseName,
                        additionalReferences,
                        internalOnly);
                    browser.Add(ReferenceTypeIds.HasComponent, false, childId);
                    return browser;
                });
            await manager.Builder.SealAsync().ConfigureAwait(false);

            var cache = new Dictionary<NodeId, NodeState>();
            (NodeHandle? handle, NodeState? node) = await manager
                .ResolveAsync(requestedId, cache)
                .ConfigureAwait(false);

            var value = new DataValue();
            ServiceResult readResult = node!.ReadAttribute(
                manager.SystemContext,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                ref value);
            using INodeBrowser browser = node.CreateBrowser(
                manager.SystemContext,
                view: null,
                NodeId.Null,
                includeSubtypes: true,
                BrowseDirection.Forward,
                QualifiedName.Null,
                additionalReferences: null,
                internalOnly: false);
            IReference? reference = browser.Next();

            Assert.Multiple(() =>
            {
                Assert.That(handle, Is.Not.Null);
                Assert.That(handle!.Validated, Is.True);
                Assert.That(node.NodeId, Is.EqualTo(requestedId));
                Assert.That(ServiceResult.IsGood(readResult), Is.True);
                Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(42));
                Assert.That(reference, Is.Not.Null);
                Assert.That(reference!.TargetId, Is.EqualTo(new ExpandedNodeId(childId)));
                Assert.That(resolverCalls, Is.EqualTo(1));
                Assert.That(manager.ContainsPredefined(requestedId), Is.False);
            });
        }

        [Test]
        public async Task OperationCacheReusesMaterializedNodeAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Cached");
            int resolverCalls = 0;

            manager.Builder.ResolveNodes(
                id => id == requestedId,
                (context, id, cancellationToken) =>
                {
                    resolverCalls++;
                    return new ValueTask<NodeState?>(
                        new BaseObjectState(null)
                        {
                            NodeId = id,
                            BrowseName = new QualifiedName("Cached", id.NamespaceIndex)
                        });
                });
            await manager.Builder.SealAsync().ConfigureAwait(false);

            var cache = new Dictionary<NodeId, NodeState>();
            (_, NodeState? first) = await manager.ResolveAsync(requestedId, cache)
                .ConfigureAwait(false);
            (_, NodeState? second) = await manager.ResolveAsync(requestedId, cache)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.SameAs(first));
                Assert.That(resolverCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task MissingVirtualNodeIsCachedForOperationAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Missing");
            int resolverCalls = 0;

            manager.Builder.ResolveNodes(
                id => id == requestedId,
                (context, id, cancellationToken) =>
                {
                    resolverCalls++;
                    return new ValueTask<NodeState?>((NodeState?)null);
                });
            await manager.Builder.SealAsync().ConfigureAwait(false);

            var cache = new Dictionary<NodeId, NodeState>();
            (_, NodeState? first) = await manager.ResolveAsync(requestedId, cache)
                .ConfigureAwait(false);
            (_, NodeState? second) = await manager.ResolveAsync(requestedId, cache)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Null);
                Assert.That(second, Is.Null);
                Assert.That(resolverCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task OverlappingVirtualFamiliesAreRejectedAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Overlap");

            manager.Builder.ResolveNodes(
                id => id == requestedId,
                static (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>((NodeState?)null));
            manager.Builder.ResolveNodes(
                id => id == requestedId,
                static (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>((NodeState?)null));
            await manager.Builder.SealAsync().ConfigureAwait(false);

            object handle = await manager.GetManagerHandleAsync(requestedId).ConfigureAwait(false);
            Assert.That(handle, Is.Null);
        }

        /// <summary>
        /// Rejects a mismatched resolver result without caching it or preventing a corrected retry.
        /// </summary>
        [Test]
        public async Task ConflictingMaterializedNodeIdIsRejectedAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Requested");
            NodeId conflictingId = manager.VirtualId("Different");
            int resolverCalls = 0;

            manager.Builder.ResolveNodes(
                id => id == requestedId,
                (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>(
                        new BaseObjectState(null)
                        {
                            NodeId = ++resolverCalls == 1 ? conflictingId : id
                        }));
            await manager.Builder.SealAsync().ConfigureAwait(false);

            var cache = new Dictionary<NodeId, NodeState>();
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ResolveAsync(requestedId, cache).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
                Assert.That(exception.Message, Does.Contain(conflictingId.ToString()));
                Assert.That(exception.Message, Does.Contain(requestedId.ToString()));
                Assert.That(cache, Is.Empty);
                Assert.That(manager.ContainsPredefined(requestedId), Is.False);
            });

            (_, NodeState? node) = await manager.ResolveAsync(requestedId, cache)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(node, Is.Not.Null);
                Assert.That(node!.NodeId, Is.EqualTo(requestedId));
                Assert.That(resolverCalls, Is.EqualTo(2));
                Assert.That(cache[requestedId], Is.SameAs(node));
                Assert.That(manager.ContainsPredefined(requestedId), Is.False);
            });
        }

        /// <summary>
        /// Preserves resolver service errors and retries instead of turning them into cached misses.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ResolverServiceExceptionIsPreservedWithoutNegativeCachingAsync(bool asynchronous)
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Unavailable");
            var failure = new ServiceResultException(StatusCodes.BadCommunicationError, "Device lookup failed.");
            int resolverCalls = 0;
            manager.Builder.ResolveNodes(id => id == requestedId, (_, id, _) =>
            {
                if (++resolverCalls == 1)
                {
                    if (asynchronous)
                    {
                        return new ValueTask<NodeState?>(Task.FromException<NodeState?>(failure));
                    }
                    throw failure;
                }
                return new ValueTask<NodeState?>(new BaseObjectState(null) { NodeId = id });
            });
            await manager.Builder.SealAsync().ConfigureAwait(false);

            var cache = new Dictionary<NodeId, NodeState>();
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ResolveAsync(requestedId, cache).ConfigureAwait(false))!;
            Assert.That(exception, Is.SameAs(failure));
            Assert.That(cache, Is.Empty);

            (_, NodeState? node) = await manager.ResolveAsync(requestedId, cache).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(node, Is.Not.Null);
                Assert.That(node!.NodeId, Is.EqualTo(requestedId));
                Assert.That(resolverCalls, Is.EqualTo(2));
                Assert.That(cache[requestedId], Is.SameAs(node));
            });
        }

        [Test]
        public async Task VirtualHandlerTypeMismatchDoesNotPoisonOperationCacheAsync()
        {
            await using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("variable");
            int resolverCalls = 0;
            manager.Builder.ResolveNodes(id => id == requestedId, (_, id, _) =>
                    new ValueTask<NodeState?>(++resolverCalls == 1
                        ? new BaseObjectState(null) { NodeId = id }
                        : new BaseDataVariableState(null) { NodeId = id }))
                .OnRead((_, _, ref value) =>
                {
                    value = Variant.From(42);
                    return ServiceResult.Good;
                });
            await manager.Builder.SealAsync().ConfigureAwait(false);
            var cache = new Dictionary<NodeId, NodeState>();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ResolveAsync(requestedId, cache).ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(cache, Is.Empty);

            (_, NodeState? node) = await manager.ResolveAsync(requestedId, cache).ConfigureAwait(false);
            Assert.That(node, Is.TypeOf<BaseDataVariableState>());
            Assert.That(cache[requestedId], Is.SameAs(node));
            Assert.That(resolverCalls, Is.EqualTo(2));
        }

        [Test]
        public async Task ResolverCancellationIsPropagatedAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Cancelled");

            manager.Builder.ResolveNodes(
                id => id == requestedId,
                static (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>(
                        Task.FromCanceled<NodeState?>(cancellationToken)));
            await manager.Builder.SealAsync().ConfigureAwait(false);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var cache = new Dictionary<NodeId, NodeState>();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await manager
                    .ResolveAsync(
                        requestedId,
                        cache,
                        cts.Token)
                    .ConfigureAwait(false));
            Assert.That(cache, Is.Empty);
        }

        [Test]
        public async Task VirtualHistoryReadUsesFamilyHandlerAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("History");
            int calls = 0;
            bool releaseSeen = false;

            manager.Builder.ResolveNodes(
                    id => id == requestedId,
                    (context, id, cancellationToken) =>
                    {
                        var variable =
                            BaseDataVariableState<int>.With<VariantBuilder>(null!);
                        variable.NodeId = id;
                        variable.BrowseName =
                            new QualifiedName("History", id.NamespaceIndex);
                        variable.DataType = DataTypeIds.Int32;
                        variable.ValueRank = ValueRanks.Scalar;
                        variable.AccessLevel = AccessLevels.HistoryRead;
                        variable.UserAccessLevel = AccessLevels.HistoryRead;
                        return new ValueTask<NodeState?>(variable);
                    })
                .OnHistoryRead((
                    context,
                    source,
                    details,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodeToRead,
                    result) =>
                {
                    calls++;
                    releaseSeen |= releaseContinuationPoints;
                    result.StatusCode = StatusCodes.Good;
                    return ServiceResult.Good;
                });
            await manager.Builder.SealAsync().ConfigureAwait(false);

            var nodeToRead = new HistoryReadValueId
            {
                NodeId = requestedId
            };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { StatusCodes.BadNodeIdUnknown };
            await manager.HistoryReadAsync(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.HistoryRead,
                    RequestLifetime.None),
                new ReadRawModifiedDetails
                {
                    StartTime = DateTime.UtcNow.AddMinutes(-1),
                    EndTime = DateTime.UtcNow
                },
                TimestampsToReturn.Both,
                releaseContinuationPoints: false,
                [nodeToRead],
                results,
                errors).ConfigureAwait(false);

            var releaseResults = new List<HistoryReadResult> { null! };
            var releaseErrors = new List<ServiceResult>
            {
                StatusCodes.BadNodeIdUnknown
            };
            await manager.HistoryReadAsync(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.HistoryRead,
                    RequestLifetime.None),
                new ReadRawModifiedDetails(),
                TimestampsToReturn.Both,
                releaseContinuationPoints: true,
                [new HistoryReadValueId { NodeId = requestedId }],
                releaseResults,
                releaseErrors).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(releaseSeen, Is.True);
                Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
                Assert.That(ServiceResult.IsGood(releaseErrors[0]), Is.True);
                Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        public async Task VirtualPollingUsesIndependentStatePerNodeAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId firstId = manager.VirtualId("First");
            NodeId secondId = manager.VirtualId("Second");
            var samples = new Dictionary<NodeId, int>();

            manager.Builder.ResolveNodes(
                    id => id == firstId || id == secondId,
                    (context, id, cancellationToken) =>
                    {
                        var variable =
                            BaseDataVariableState<int>.With<VariantBuilder>(null!);
                        variable.NodeId = id;
                        variable.BrowseName =
                            new QualifiedName(id == firstId ? "First" : "Second");
                        variable.DataType = DataTypeIds.Int32;
                        variable.ValueRank = ValueRanks.Scalar;
                        variable.Value = 0;
                        return new ValueTask<NodeState?>(variable);
                    })
                .PollWhileMonitored(
                    TimeSpan.FromHours(1),
                    (context, source, cancellationToken) =>
                    {
                        samples.TryGetValue(source.NodeId, out int count);
                        samples[source.NodeId] = count + 1;
                        return new ValueTask<int>(count + 1);
                    });
            await manager.Builder.SealAsync().ConfigureAwait(false);

            var firstCache = new Dictionary<NodeId, NodeState>();
            (NodeHandle? firstHandle, _) = await manager.ResolveAsync(
                firstId,
                firstCache).ConfigureAwait(false);
            var secondCache = new Dictionary<NodeId, NodeState>();
            (NodeHandle? secondHandle, _) = await manager.ResolveAsync(
                secondId,
                secondCache).ConfigureAwait(false);
            Mock<ISampledDataChangeMonitoredItem> firstItem =
                CreateMonitoredItem(1, firstId);
            Mock<ISampledDataChangeMonitoredItem> secondItem =
                CreateMonitoredItem(2, secondId);
            firstItem.SetupGet(value => value.ManagerHandle)
                .Returns(firstHandle!);
            secondItem.SetupGet(value => value.ManagerHandle)
                .Returns(secondHandle!);

            await manager.NotifyCreatedAsync(
                firstHandle!,
                firstItem.Object).ConfigureAwait(false);
            await manager.NotifyCreatedAsync(
                secondHandle!,
                secondItem.Object).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(samples[firstId], Is.EqualTo(1));
                Assert.That(samples[secondId], Is.EqualTo(1));
            });

            await manager.NotifyDeletedAsync(
                firstHandle!,
                firstItem.Object).ConfigureAwait(false);
            await manager.NotifyDeletedAsync(
                secondHandle!,
                secondItem.Object).ConfigureAwait(false);
        }

        [Test]
        public async Task MultipleAttachedBuildersKeepTheirRegistrationsAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId firstId = manager.VirtualId("FirstBuilder");
            NodeId secondId = manager.VirtualId("SecondBuilder");

            manager.Builder.ResolveNodes(
                id => id == firstId,
                (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>(
                        new BaseObjectState(null)
                        {
                            NodeId = id,
                            BrowseName = new QualifiedName("FirstBuilder")
                        }));
            await manager.Builder.SealAsync().ConfigureAwait(false);

            NodeManagerBuilder secondBuilder = manager.CreateAdditionalBuilder();
            secondBuilder.ResolveNodes(
                id => id == secondId,
                (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>(
                        new BaseObjectState(null)
                        {
                            NodeId = id,
                            BrowseName = new QualifiedName("SecondBuilder")
                        }));
            await secondBuilder.SealAsync().ConfigureAwait(false);

            (_, NodeState? first) = await manager.ResolveAsync(
                firstId,
                new Dictionary<NodeId, NodeState>()).ConfigureAwait(false);
            (_, NodeState? second) = await manager.ResolveAsync(
                secondId,
                new Dictionary<NodeId, NodeState>()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first?.NodeId, Is.EqualTo(firstId));
                Assert.That(second?.NodeId, Is.EqualTo(secondId));
            });
        }

        /// <summary>
        /// Verifies that deleting the address space releases each monitored virtual source still active at shutdown.
        /// </summary>
        [Test]
        public async Task VirtualMonitoredSourcesReleaseEveryLiveInstanceAtShutdownAsync()
        {
            using var manager = new TestVirtualManager();
            NodeId[] ids = [manager.VirtualId("FirstLive"), manager.VirtualId("SecondLive")];
            var active = new HashSet<NodeId>();
            var released = new List<NodeId>();
            manager.Builder.ResolveNodes(id => Array.IndexOf(ids, id) >= 0, (_, id, _) =>
                {
                    var node = BaseDataVariableState<int>.With<VariantBuilder>(null!);
                    node.NodeId = id;
                    node.BrowseName = new QualifiedName("Live", id.NamespaceIndex);
                    node.DataType = DataTypeIds.Int32;
                    return new ValueTask<NodeState?>(node);
                })
                .OnFirstSubscriber((_, source, _) =>
                {
                    active.Add(source.NodeId);
                    return default;
                })
                .OnLastSubscriber((_, source, _) =>
                {
                    active.Remove(source.NodeId);
                    released.Add(source.NodeId);
                    return default;
                })
                .PollWhileMonitored(TimeSpan.FromHours(1), (_, _, _) => new ValueTask<int>(42));
            await manager.Builder.SealAsync().ConfigureAwait(false);
            for (int i = 0; i < ids.Length; i++)
            {
                (NodeHandle? handle, _) = await manager.ResolveAsync(ids[i], new Dictionary<NodeId, NodeState>())
                    .ConfigureAwait(false);
                Mock<ISampledDataChangeMonitoredItem> item = CreateMonitoredItem((uint)i + 1, ids[i]);
                item.SetupGet(value => value.ManagerHandle).Returns(handle!);
                await manager.NotifyCreatedAsync(handle!, item.Object).ConfigureAwait(false);
            }
            Assert.That(active, Is.EquivalentTo(ids));
            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(active, Is.Empty);
            Assert.That(released, Is.EquivalentTo(ids));
        }

        private static IVirtualNodeBuilder RegisterVariables(TestVirtualManager manager, NodeId invalid)
        {
            return manager.Builder.ResolveNodes(id => id.NamespaceIndex == manager.TestNamespaceIndex, (_, id, _) =>
                new ValueTask<NodeState?>(new BaseDataVariableState(null)
                {
                    NodeId = id == invalid ? manager.VirtualId("wrong") : id,
                    BrowseName = new QualifiedName("Value", manager.TestNamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel =
                        AccessLevels.CurrentReadOrWrite | AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
                    UserAccessLevel =
                        AccessLevels.CurrentReadOrWrite | AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
                    Value = 42
                }));
        }

        private static ServiceResult[] CreateBatchErrors()
        {
            return
            [
                StatusCodes.BadNodeIdUnknown, StatusCodes.BadNodeIdUnknown,
                StatusCodes.BadNodeIdUnknown, StatusCodes.BadNodeIdUnknown
            ];
        }

        private static void AssertIsolatedValidationFailure(ServiceResult[] errors)
        {
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(ServiceResult.IsGood(errors[1]), Is.True);
            Assert.That(errors[2].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(ServiceResult.IsGood(errors[3]), Is.True);
        }

        private static MasterNodeManager CreateMaster(Mock<IServerInternal> server, TestVirtualManager manager)
        {
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(new Mock<IConfigurationNodeManager>().Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(new Mock<ICoreNodeManager>().Object);
            server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
            var master = new MasterNodeManager(server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() }, null, [manager]);
            server.SetupGet(value => value.NodeManager).Returns(master);
            return master;
        }

        private static Mock<ISampledDataChangeMonitoredItem> CreateMonitoredItem(
            uint id,
            NodeId nodeId)
        {
            var item = new Mock<ISampledDataChangeMonitoredItem>();
            item.SetupGet(value => value.Id).Returns(id);
            item.SetupGet(value => value.NodeId).Returns(nodeId);
            item.SetupGet(value => value.MonitoringMode)
                .Returns(MonitoringMode.Reporting);
            item.SetupGet(value => value.SamplingInterval).Returns(100);
            return item;
        }

        private sealed class TestVirtualManager : FluentNodeManagerBase
        {
            public TestVirtualManager()
                : base(CreateMockServer(), kNamespaceUri)
            {
                Builder = CreateFluentBuilder(TestNamespaceIndex);
            }

            public TestVirtualManager(IServerInternal server)
                : base(server, kNamespaceUri)
            {
                Builder = CreateFluentBuilder(TestNamespaceIndex);
            }

            public TestVirtualManager(ApplicationConfiguration configuration)
                : base(CreateMockServer(), configuration, kNamespaceUri)
            {
                Builder = CreateFluentBuilder(TestNamespaceIndex);
            }

            public ApplicationConfiguration? StartupConfiguration => Configuration;

            public NodeManagerBuilder Builder { get; }

            public ushort TestNamespaceIndex => NamespaceIndexes[0];

            public NodeId VirtualId(string identifier)
            {
                return new NodeId(identifier, TestNamespaceIndex);
            }

            public bool ContainsPredefined(NodeId nodeId)
            {
                return PredefinedNodes.ContainsKey(nodeId);
            }

            public NodeManagerBuilder CreateAdditionalBuilder()
            {
                return CreateFluentBuilder(TestNamespaceIndex);
            }

            public async ValueTask<(NodeHandle? Handle, NodeState? Node)> ResolveAsync(
                NodeId nodeId,
                IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                NodeHandle handle = await GetManagerHandleAsync(
                    SystemContext,
                    nodeId,
                    cache,
                    cancellationToken).ConfigureAwait(false);
                if (handle == null)
                {
                    return (null, null);
                }

                NodeState node = await ValidateNodeAsync(
                    SystemContext,
                    handle,
                    cache,
                    cancellationToken).ConfigureAwait(false);
                return (handle, node);
            }

            public async ValueTask NotifyCreatedAsync(
                NodeHandle handle,
                ISampledDataChangeMonitoredItem monitoredItem)
            {
                OnMonitoredItemCreated(SystemContext, handle, monitoredItem);
                await OnCreateMonitoredItemsCompleteAsync(
                    SystemContext,
                    [monitoredItem]).ConfigureAwait(false);
            }

            public async ValueTask NotifyDeletedAsync(
                NodeHandle handle,
                ISampledDataChangeMonitoredItem monitoredItem)
            {
                await OnMonitoredItemDeletedAsync(
                    SystemContext,
                    handle,
                    monitoredItem).ConfigureAwait(false);
                await OnDeleteMonitoredItemsCompleteAsync(
                    SystemContext,
                    [monitoredItem]).ConfigureAwait(false);
            }

            private const string kNamespaceUri = "urn:test:virtual-nodes";

            private static IServerInternal CreateMockServer()
            {
                var namespaceUris = new NamespaceTable();
                namespaceUris.Append(Ua.Namespaces.OpcUa);

                var telemetry = new Mock<ITelemetryContext>();
                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
                server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                server.SetupGet(value => value.MessageContext)
                    .Returns(ServiceMessageContext.Create(telemetry.Object));
                server.SetupGet(value => value.DefaultSystemContext)
                    .Returns(new ServerSystemContext(server.Object));
                return server.Object;
            }
        }
    }
}
