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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

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
                .OnRead((ISystemContext context, NodeState node, ref Variant value) =>
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
            manager.Builder.Seal();

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
            manager.Builder.Seal();

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
            manager.Builder.Seal();

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
        public void OverlappingVirtualFamiliesAreRejected()
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
            manager.Builder.Seal();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.GetManagerHandleAsync(requestedId).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void ConflictingMaterializedNodeIdIsRejected()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Requested");

            manager.Builder.ResolveNodes(
                id => id == requestedId,
                (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>(
                        new BaseObjectState(null)
                        {
                            NodeId = manager.VirtualId("Different")
                        }));
            manager.Builder.Seal();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager
                    .ResolveAsync(requestedId, new Dictionary<NodeId, NodeState>())
                    .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ResolverCancellationIsPropagated()
        {
            using var manager = new TestVirtualManager();
            NodeId requestedId = manager.VirtualId("Cancelled");

            manager.Builder.ResolveNodes(
                id => id == requestedId,
                static (context, id, cancellationToken) =>
                    new ValueTask<NodeState?>(
                        Task.FromCanceled<NodeState?>(cancellationToken)));
            manager.Builder.Seal();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await manager
                    .ResolveAsync(
                        requestedId,
                        new Dictionary<NodeId, NodeState>(),
                        cts.Token)
                    .ConfigureAwait(false));
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
            manager.Builder.Seal();

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
            manager.Builder.Seal();

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
            manager.Builder.Seal();

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
            secondBuilder.Seal();

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
