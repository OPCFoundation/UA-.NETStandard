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
using Opc.Ua.Gpos;
using StreamingMonitoredItemOptions =
    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Positioning.Tests
{
    [TestFixture]
    [Category("Positioning")]
    public sealed class GlobalPositioningClientTests
    {
        [Test]
        public async Task ObserveGlobalPositionWithTypedValueReturnsMetadataAsync()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId positionId = new("Position", 2);
            NodeId sourcePropertyId = new("SourceId", 2);
            NodeId crsId = new("CoordinateReferenceSystem", 2);
            NodeId zoneId = new("Zone", 2);
            GlobalPositionDataType position = new()
            {
                Longitude = 8.55,
                Latitude = 47.37,
                Elevation = 408.2,
                EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.Elevation
            };

            SetupTranslateReturns(
                sessionMock,
                new Dictionary<string, NodeId>
                {
                    [BuildPathKey(
                        positionId,
                        ReferenceTypeIds.HasProperty,
                        "SourceId")] = sourcePropertyId,
                    [BuildPathKey(
                        positionId,
                        ReferenceTypeIds.HasComponent,
                        "CoordinateReferenceSystem")] = crsId
                });
            SetupReadReturns(
                sessionMock,
                nodeId =>
                {
                    if (nodeId == sourcePropertyId)
                    {
                        return new DataValue(new Variant(zoneId), StatusCodes.Good);
                    }

                    if (nodeId == crsId)
                    {
                        return new DataValue(new Variant((uint)4326), StatusCodes.Good);
                    }

                    throw new AssertionException($"Unexpected read for '{nodeId}'.");
                });

            ITelemetryContext telemetry = Mock.Of<ITelemetryContext>();
            var client = new Client.GlobalPositioningClient(
                sessionMock.Object,
                telemetry);

            Client.GlobalPositionValue observed =
                await ReadSingleAsync(
                    client.ObserveGlobalPositionAsync(
                        positionId,
                        new SingleValueStreamingSubscription(
                            new DataValue(
                                Variant.From(new ExtensionObject(position)),
                                StatusCodes.Good))))
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(client.Telemetry, Is.SameAs(telemetry));
                Assert.That(client.OpenZone(zoneId), Is.Not.Null);
                Assert.That(observed.NodeId, Is.EqualTo(positionId));
                Assert.That(observed.SourceNodeId, Is.EqualTo(zoneId));
                Assert.That(observed.CoordinateReferenceSystem, Is.EqualTo(4326));
                Assert.That(observed.Position.Longitude, Is.EqualTo(8.55));
                Assert.That(observed.Position.Latitude, Is.EqualTo(47.37));
                Assert.That(observed.StatusCode, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        public void ObserveGlobalPositionWithWrongValueTypeThrowsBadTypeMismatch()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId positionId = new("Position", 2);
            NodeId sourcePropertyId = new("SourceId", 2);
            NodeId crsId = new("CoordinateReferenceSystem", 2);
            NodeId zoneId = new("Zone", 2);

            SetupTranslateReturns(
                sessionMock,
                new Dictionary<string, NodeId>
                {
                    [BuildPathKey(
                        positionId,
                        ReferenceTypeIds.HasProperty,
                        "SourceId")] = sourcePropertyId,
                    [BuildPathKey(
                        positionId,
                        ReferenceTypeIds.HasComponent,
                        "CoordinateReferenceSystem")] = crsId
                });
            SetupReadReturns(
                sessionMock,
                nodeId =>
                {
                    if (nodeId == sourcePropertyId)
                    {
                        return new DataValue(new Variant(zoneId), StatusCodes.Good);
                    }

                    if (nodeId == crsId)
                    {
                        return new DataValue(new Variant((uint)4326), StatusCodes.Good);
                    }

                    throw new AssertionException($"Unexpected read for '{nodeId}'.");
                });

            var client = new Client.GlobalPositioningClient(
                sessionMock.Object,
                Mock.Of<ITelemetryContext>());

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadSingleAsync(
                    client.ObserveGlobalPositionAsync(
                        positionId,
                        new SingleValueStreamingSubscription(
                            new DataValue(Variant.From("wrong"), StatusCodes.Good))))
                    .ConfigureAwait(false));

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public async Task ReadGlobalLocationRetriesAnIncoherentMovingSnapshotAsync()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId locationId = new("Location", 2);
            NodeId positionId = new("Position", 2);
            NodeId sourcePropertyId = new("SourceId", 2);
            NodeId crsId = new("CoordinateReferenceSystem", 2);
            NodeId zoneId = new("Zone", 2);
            GlobalLocationDataType first = CreateLocation(8.0);
            GlobalLocationDataType second = CreateLocation(9.0);
            int aggregateReads = 0;

            SetupTranslateReturns(
                sessionMock,
                new Dictionary<string, NodeId>
                {
                    [BuildPathKey(
                        locationId,
                        ReferenceTypeIds.HasComponent,
                        "Position")] = positionId,
                    [BuildPathKey(
                        positionId,
                        ReferenceTypeIds.HasProperty,
                        "SourceId")] = sourcePropertyId,
                    [BuildPathKey(
                        positionId,
                        ReferenceTypeIds.HasComponent,
                        "CoordinateReferenceSystem")] = crsId
                });
            SetupReadReturns(
                sessionMock,
                nodeId =>
                {
                    if (nodeId == locationId)
                    {
                        GlobalLocationDataType value =
                            aggregateReads++ == 0 ? first : second;
                        return new DataValue(
                            Variant.From(new ExtensionObject(value)),
                            StatusCodes.Good);
                    }

                    if (nodeId == positionId)
                    {
                        return new DataValue(
                            Variant.From(new ExtensionObject(second.Position)),
                            StatusCodes.Good);
                    }

                    if (nodeId == sourcePropertyId)
                    {
                        return new DataValue(new Variant(zoneId), StatusCodes.Good);
                    }

                    if (nodeId == crsId)
                    {
                        return new DataValue(new Variant((uint)4326), StatusCodes.Good);
                    }

                    throw new AssertionException($"Unexpected read for '{nodeId}'.");
                });

            var client = new Client.GlobalPositioningClient(
                sessionMock.Object,
                Mock.Of<ITelemetryContext>());
            Client.GlobalLocationValue value =
                await client.ReadGlobalLocationAsync(locationId)
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(aggregateReads, Is.EqualTo(2));
                Assert.That(value.Location.Position.Longitude, Is.EqualTo(9.0));
                Assert.That(value.SourceNodeId, Is.EqualTo(zoneId));
            });
        }

        [Test]
        public void ReadGlobalLocationWithPersistentMismatchThrowsBadDataEncodingInvalid()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId locationId = new("Location", 2);
            NodeId positionId = new("Position", 2);
            GlobalLocationDataType aggregate = CreateLocation(8.0);
            GlobalLocationDataType component = CreateLocation(9.0);

            SetupTranslateReturns(
                sessionMock,
                new Dictionary<string, NodeId>
                {
                    [BuildPathKey(
                        locationId,
                        ReferenceTypeIds.HasComponent,
                        "Position")] = positionId
                });
            SetupReadReturns(
                sessionMock,
                nodeId =>
                {
                    if (nodeId == locationId)
                    {
                        return new DataValue(
                            Variant.From(new ExtensionObject(aggregate)),
                            StatusCodes.Good);
                    }

                    if (nodeId == positionId)
                    {
                        return new DataValue(
                            Variant.From(new ExtensionObject(component.Position)),
                            StatusCodes.Good);
                    }

                    throw new AssertionException($"Unexpected read for '{nodeId}'.");
                });

            var client = new Client.GlobalPositioningClient(
                sessionMock.Object,
                Mock.Of<ITelemetryContext>());

            ServiceResultException? exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.ReadGlobalLocationAsync(locationId)
                        .ConfigureAwait(false));

            Assert.That(
                exception!.StatusCode,
                Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
        }

        private static GlobalLocationDataType CreateLocation(double longitude)
        {
            return new GlobalLocationDataType
            {
                Position = new GlobalPositionDataType
                {
                    Longitude = longitude,
                    Latitude = 47.0
                }
            };
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var sessionMock = new Mock<ISession>();
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(Gpos.Namespaces.GPOS);
            context.NamespaceUris.GetIndexOrAppend(Rsl.Namespaces.RSL);
            sessionMock.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            sessionMock.SetupGet(s => s.MessageContext).Returns(context);
            sessionMock.SetupGet(s => s.Factory).Returns(context.Factory);
            sessionMock.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
            sessionMock.SetupGet(s => s.ServerCapabilities)
                .Returns(new ServerCapabilities());
            sessionMock.SetupGet(s => s.ContinuationPointPolicy)
                .Returns(ContinuationPointPolicy.Default);
            sessionMock.SetupGet(s => s.NodeCache)
                .Returns(new Mock<INodeCache>(MockBehavior.Loose).Object);
            return sessionMock;
        }

        private static async Task<T> ReadSingleAsync<T>(IAsyncEnumerable<T> source)
        {
            await foreach (T value in source.ConfigureAwait(false))
            {
                return value;
            }

            throw new AssertionException("The async sequence produced no value.");
        }

        private static string BuildPathKey(
            NodeId parentId,
            NodeId referenceTypeId,
            string browseName)
        {
            return $"{parentId}|{referenceTypeId}|{browseName}";
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
