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
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using StreamingMonitoredItemOptions =
    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Positioning.Tests
{
    [TestFixture]
    [Category("Positioning")]
    public sealed class RelativeSpatialLocationClientTests
    {
        [Test]
        public async Task EnumerateFramesWithSpatialAndNonSpatialReferencesReturnsOnlySpatialLocationsAsync()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId folderId = new("Frames", 2);
            NodeId spatialFrameId = new("SpatialFrame", 2);
            NodeId otherVariableId = new("OtherVariable", 2);
            ReferenceDescription[] references =
            [
                CreateReference(
                    spatialFrameId,
                    "SpatialFrame",
                    Rsl.VariableTypeIds.SpatialLocationType),
                CreateReference(
                    otherVariableId,
                    "OtherVariable",
                    new ExpandedNodeId(VariableTypeIds.BaseDataVariableType))
            ];

            SetupBrowseReturns(sessionMock, references);
            SetupTypeChecks(sessionMock);

            ITelemetryContext telemetry = Mock.Of<ITelemetryContext>();
            var client = new Client.RelativeSpatialLocationClient(
                sessionMock.Object,
                telemetry);
            var entries = new List<Client.PositioningObjectEntry>();
            await foreach (Client.PositioningObjectEntry entry in
                client.EnumerateFramesAsync(folderId).ConfigureAwait(false))
            {
                entries.Add(entry);
            }

            Assert.Multiple(() =>
            {
                Assert.That(client.Telemetry, Is.SameAs(telemetry));
                Assert.That(client.OpenSpatialObject(new NodeId("Robot", 2)), Is.Not.Null);
                Assert.That(client.OpenSpatialObjectList(folderId), Is.Not.Null);
                Assert.That(entries, Has.Count.EqualTo(1));
                Assert.That(entries[0].NodeId, Is.EqualTo(spatialFrameId));
                Assert.That(entries[0].BrowseName.Name, Is.EqualTo("SpatialFrame"));
            });
        }

        [Test]
        public async Task ObservePositionFrameWithTypedFrameReturnsBaseNodeAndValueAsync()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId spatialObjectId = new("SpatialObject", 2);
            NodeId frameId = new("PositionFrame", 2);
            NodeId basePropertyId = new("BaseProperty", 2);
            NodeId worldId = new("World", 2);
            ThreeDFrame frame = CreateFrame(1.25);

            SetupTranslateReturns(
                sessionMock,
                new Dictionary<string, NodeId>
                {
                    [BuildPathKey(
                        spatialObjectId,
                        ReferenceTypeIds.HasComponent,
                        "PositionFrame")] = frameId,
                    [BuildPathKey(
                        frameId,
                        ReferenceTypeIds.HasComponent,
                        "Base")] = basePropertyId
                });
            SetupReadReturns(
                sessionMock,
                nodeId =>
                {
                    if (nodeId == basePropertyId)
                    {
                        return new DataValue(new Variant(worldId), StatusCodes.Good);
                    }

                    throw new AssertionException($"Unexpected read for '{nodeId}'.");
                });

            var client = new Client.RelativeSpatialLocationClient(
                sessionMock.Object,
                Mock.Of<ITelemetryContext>());
            Client.RelativeSpatialFrameValue observed =
                await ReadSingleAsync(
                    client.ObservePositionFrameAsync(
                        spatialObjectId,
                        new SingleValueStreamingSubscription(
                            new DataValue(
                                Variant.From(new ExtensionObject(frame)),
                                StatusCodes.Good))))
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(observed.NodeId, Is.EqualTo(frameId));
                Assert.That(observed.BaseNodeId, Is.EqualTo(worldId));
                Assert.That(observed.Frame.CartesianCoordinates.X, Is.EqualTo(1.25));
                Assert.That(observed.Frame.Orientation.A, Is.EqualTo(11.25));
                Assert.That(observed.StatusCode, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        public void ObservePositionFrameWithWrongValueTypeThrowsBadTypeMismatch()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId spatialObjectId = new("SpatialObject", 2);
            NodeId frameId = new("PositionFrame", 2);
            NodeId basePropertyId = new("BaseProperty", 2);
            NodeId worldId = new("World", 2);

            SetupTranslateReturns(
                sessionMock,
                new Dictionary<string, NodeId>
                {
                    [BuildPathKey(
                        spatialObjectId,
                        ReferenceTypeIds.HasComponent,
                        "PositionFrame")] = frameId,
                    [BuildPathKey(
                        frameId,
                        ReferenceTypeIds.HasComponent,
                        "Base")] = basePropertyId
                });
            SetupReadReturns(
                sessionMock,
                nodeId =>
                {
                    if (nodeId == basePropertyId)
                    {
                        return new DataValue(new Variant(worldId), StatusCodes.Good);
                    }

                    throw new AssertionException($"Unexpected read for '{nodeId}'.");
                });

            var client = new Client.RelativeSpatialLocationClient(
                sessionMock.Object,
                Mock.Of<ITelemetryContext>());

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadSingleAsync(
                    client.ObservePositionFrameAsync(
                        spatialObjectId,
                        new SingleValueStreamingSubscription(
                            new DataValue(Variant.From("wrong"), StatusCodes.Good))))
                    .ConfigureAwait(false));

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var sessionMock = new Mock<ISession>();
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(Rsl.Namespaces.RSL);
            sessionMock.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            sessionMock.SetupGet(s => s.MessageContext).Returns(context);
            sessionMock.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
            sessionMock.SetupGet(s => s.ServerCapabilities)
                .Returns(new ServerCapabilities());
            sessionMock.SetupGet(s => s.ContinuationPointPolicy)
                .Returns(ContinuationPointPolicy.Default);
            return sessionMock;
        }

        private static ReferenceDescription CreateReference(
            NodeId nodeId,
            string browseName,
            ExpandedNodeId typeDefinition)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(nodeId),
                BrowseName = new QualifiedName(browseName, nodeId.NamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                TypeDefinition = typeDefinition,
                NodeClass = NodeClass.Variable
            };
        }

        private static async Task<T> ReadSingleAsync<T>(IAsyncEnumerable<T> source)
        {
            await foreach (T value in source.ConfigureAwait(false))
            {
                return value;
            }

            throw new AssertionException("The async sequence produced no value.");
        }

        private static ThreeDFrame CreateFrame(double x)
        {
            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates
                {
                    X = x,
                    Y = x + 1.0,
                    Z = x + 2.0
                },
                Orientation = new ThreeDOrientation
                {
                    A = x + 10.0,
                    B = x + 20.0,
                    C = x + 30.0
                }
            };
        }

        private static string BuildPathKey(
            NodeId parentId,
            NodeId referenceTypeId,
            string browseName)
        {
            return $"{parentId}|{referenceTypeId}|{browseName}";
        }

        private static void SetupBrowseReturns(
            Mock<ISession> sessionMock,
            ReferenceDescription[] references)
        {
            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new BrowseResponse
                    {
                        Results =
                        [
                            new BrowseResult
                            {
                                References = references
                            }
                        ]
                    });
        }

        private static void SetupTypeChecks(Mock<ISession> sessionMock)
        {
            var nodeCacheMock = new Mock<INodeCache>();
            nodeCacheMock
                .Setup(n => n.IsTypeOfAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (
                        ExpandedNodeId actualTypeId,
                        ExpandedNodeId expectedTypeId,
                        CancellationToken _
                    ) => new ValueTask<bool>(actualTypeId == expectedTypeId));
            sessionMock.SetupGet(s => s.NodeCache).Returns(nodeCacheMock.Object);
        }

        private static void SetupTranslateReturns(
            Mock<ISession> sessionMock,
            Dictionary<string, NodeId> targets)
        {
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    (
                        RequestHeader? _,
                        ArrayOf<BrowsePath> browsePaths,
                        CancellationToken _
                    ) =>
                    {
                        var results = new BrowsePathResult[browsePaths.Count];
                        for (int i = 0; i < browsePaths.Count; i++)
                        {
                            BrowsePath path = browsePaths[i];
                            RelativePathElement element = path.RelativePath.Elements[0];
                            if (!targets.TryGetValue(
                                BuildPathKey(
                                    path.StartingNode,
                                    element.ReferenceTypeId,
                                    element.TargetName.Name ?? string.Empty),
                                out NodeId targetId))
                            {
                                results[i] = new BrowsePathResult
                                {
                                    StatusCode = StatusCodes.BadNoMatch
                                };
                                continue;
                            }

                            results[i] = new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = new ExpandedNodeId(targetId),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            };
                        }

                        return new TranslateBrowsePathsToNodeIdsResponse
                        {
                            Results = results
                        };
                    });
        }

        private static void SetupReadReturns(
            Mock<ISession> sessionMock,
            Func<NodeId, DataValue> valueFactory)
        {
            sessionMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    (
                        RequestHeader? _,
                        double _,
                        TimestampsToReturn _,
                        ArrayOf<ReadValueId> nodesToRead,
                        CancellationToken _
                    ) =>
                    {
                        var results = new DataValue[nodesToRead.Count];
                        for (int i = 0; i < nodesToRead.Count; i++)
                        {
                            results[i] = valueFactory(nodesToRead[i].NodeId);
                        }

                        return new ReadResponse
                        {
                            Results = results,
                            DiagnosticInfos = []
                        };
                    });
        }

        private sealed class SingleValueStreamingSubscription :
            IStreamingSubscription
        {
            private readonly DataValue m_value;

            public SingleValueStreamingSubscription(DataValue value)
            {
                m_value = value;
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            public IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                NodeId nodeId,
                StreamingMonitoredItemOptions? options = null,
                CancellationToken ct = default)
            {
                return YieldDataChangeAsync(ct);
            }

            public IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                IReadOnlyList<NodeId> nodeIds,
                StreamingMonitoredItemOptions? options = null,
                CancellationToken ct = default)
            {
                return YieldDataChangeAsync(ct);
            }

            public IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
                NodeId notifierId,
                EventFilter filter,
                StreamingMonitoredItemOptions? options = null,
                CancellationToken ct = default)
            {
                return YieldNoEventsAsync(ct);
            }

            private async IAsyncEnumerable<DataValueChange> YieldDataChangeAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new DataValueChange(null, m_value, null);
            }

            private static async IAsyncEnumerable<EventNotification>
                YieldNoEventsAsync(
                    [System.Runtime.CompilerServices.EnumeratorCancellation]
                    CancellationToken cancellationToken)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield break;
            }
        }
    }
}
