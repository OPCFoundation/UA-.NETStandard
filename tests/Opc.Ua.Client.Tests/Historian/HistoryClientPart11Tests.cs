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
using Opc.Ua.Client.Historian;

namespace Opc.Ua.Client.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistoryClientPart11Tests
    {
        [Test]
        public async Task ReadModifiedAsyncPairsValuesWithModificationInfoAsync()
        {
            DateTime sourceTime = DateTime.UtcNow.AddMinutes(-5);
            DateTime modificationTime = DateTime.UtcNow.AddMinutes(-1);
            var expectedInfo = new ModificationInfo
            {
                ModificationTime = modificationTime,
                UpdateType = HistoryUpdateType.Replace,
                UserName = "operator"
            };
            ExtensionObject? capturedDetails = null;
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ExtensionObject, TimestampsToReturn, bool,
                    ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, details, _, _, _, _) => capturedDetails = details)
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results =
                    [
                        new HistoryReadResult
                        {
                            StatusCode = StatusCodes.Good,
                            HistoryData = new ExtensionObject(new HistoryModifiedData
                            {
                                DataValues =
                                [
                                    new DataValue(
                                        new Variant(42),
                                        StatusCodes.Good,
                                        sourceTime)
                                ],
                                ModificationInfos = [expectedInfo]
                            })
                        }
                    ]
                }));

            var client = new HistoryClient(mockSession.Object);
            var values = new List<ModifiedDataValue>();

            await foreach (ModifiedDataValue value in client.ReadModifiedAsync(
                new NodeId("ModifiedNode", 2),
                sourceTime.AddHours(-1),
                sourceTime.AddHours(1),
                maxValuesPerNode: 10,
                timestampsToReturn: TimestampsToReturn.Both).ConfigureAwait(false))
            {
                values.Add(value);
            }

            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(values[0].Value.SourceTimestamp, Is.EqualTo(sourceTime));
            Assert.That(values[0].Value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(42));
            Assert.That(values[0].Info.ModificationTime, Is.EqualTo(modificationTime));
            Assert.That(values[0].Info.UpdateType, Is.EqualTo(HistoryUpdateType.Replace));
            Assert.That(values[0].Info.UserName, Is.EqualTo("operator"));
            Assert.That(capturedDetails, Is.Not.Null);
            ExtensionObject modifiedDetails = capturedDetails ??
                throw new AssertionException("No ReadRawModifiedDetails captured.");
            Assert.That(
                modifiedDetails.TryGetValue(out ReadRawModifiedDetails details),
                Is.True);
            Assert.That(details.IsReadModified, Is.True);
            Assert.That(details.NumValuesPerNode, Is.EqualTo(10u));
        }

        [Test]
        public Task ReadModifiedAsyncRejectsMismatchedMetadataAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results =
                    [
                        new HistoryReadResult
                        {
                            StatusCode = StatusCodes.Good,
                            HistoryData = new ExtensionObject(new HistoryModifiedData
                            {
                                DataValues = [new DataValue(new Variant(1))],
                                ModificationInfos = []
                            })
                        }
                    ]
                }));
            var client = new HistoryClient(mockSession.Object);

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    await foreach (ModifiedDataValue value in client.ReadModifiedAsync(
                        new NodeId("ModifiedNode", 2),
                        DateTime.UtcNow.AddHours(-1),
                        DateTime.UtcNow).ConfigureAwait(false))
                    {
                        Assert.Fail($"Unexpected modified value {value}.");
                    }
                })!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            return Task.CompletedTask;
        }

        [Test]
        public Task ReadModifiedAsyncRejectsUnexpectedPayloadTypeAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results =
                    [
                        new HistoryReadResult
                        {
                            StatusCode = StatusCodes.Good,
                            HistoryData = new ExtensionObject(new HistoryData
                            {
                                DataValues = [new DataValue(new Variant(1))]
                            })
                        }
                    ]
                }));
            var client = new HistoryClient(mockSession.Object);

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    await foreach (ModifiedDataValue value in client.ReadModifiedAsync(
                        new NodeId("ModifiedNode", 2),
                        DateTime.UtcNow.AddHours(-1),
                        DateTime.UtcNow).ConfigureAwait(false))
                    {
                        Assert.Fail($"Unexpected modified value {value}.");
                    }
                })!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            return Task.CompletedTask;
        }

        [Test]
        public async Task ReadEventsAsyncFollowsContinuationPointsAsync()
        {
            var continuationPoint = (ByteString)new byte[] { 0x41 };
            var seenContinuationPoints = new List<ByteString>();
            ExtensionObject? capturedDetails = null;
            int readCalls = 0;
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ExtensionObject, TimestampsToReturn, bool,
                    ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, details, _, release, nodes, _) =>
                    {
                        Assert.That(release, Is.False);
                        capturedDetails = details;
                        seenContinuationPoints.Add(nodes[0].ContinuationPoint);
                        string text = readCalls++ == 0 ? "first" : "second";
                        return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                        {
                            Results =
                            [
                                new HistoryReadResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    ContinuationPoint = readCalls == 1
                                        ? continuationPoint
                                        : ByteString.Empty,
                                    HistoryData = new ExtensionObject(new HistoryEvent
                                    {
                                        Events =
                                        [
                                            new HistoryEventFieldList
                                            {
                                                EventFields = [new Variant(text)]
                                            }
                                        ]
                                    })
                                }
                            ]
                        });
                    });
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Message,
                Attributes.Value);
            var client = new HistoryClient(mockSession.Object);
            var events = new List<HistoryEventFieldList>();

            await foreach (HistoryEventFieldList fields in client.ReadEventsAsync(
                new NodeId("Notifier", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow,
                filter,
                maxValuesPerNode: 1,
                timestampsToReturn: TimestampsToReturn.Both).ConfigureAwait(false))
            {
                events.Add(fields);
            }

            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].EventFields[0].TryGetValue(out string first), Is.True);
            Assert.That(events[1].EventFields[0].TryGetValue(out string second), Is.True);
            Assert.That(first, Is.EqualTo("first"));
            Assert.That(second, Is.EqualTo("second"));
            Assert.That(seenContinuationPoints, Has.Count.EqualTo(2));
            Assert.That(seenContinuationPoints[0].IsEmpty, Is.True);
            Assert.That(seenContinuationPoints[1], Is.EqualTo(continuationPoint));
            Assert.That(capturedDetails, Is.Not.Null);
            ExtensionObject eventDetails = capturedDetails ??
                throw new AssertionException("No ReadEventDetails captured.");
            Assert.That(eventDetails.TryGetValue(out ReadEventDetails details), Is.True);
            Assert.That(details.Filter, Is.SameAs(filter));
            Assert.That(details.NumValuesPerNode, Is.EqualTo(1u));
        }

        [Test]
        public async Task ReadEventsAsyncReleasesContinuationPointWhenDisposedAsync()
        {
            var continuationPoint = (ByteString)new byte[] { 0x42 };
            ByteString releasedContinuationPoint = ByteString.Empty;
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ExtensionObject, TimestampsToReturn, bool,
                    ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, _, _, release, nodes, _) =>
                    {
                        if (release)
                        {
                            releasedContinuationPoint = nodes[0].ContinuationPoint;
                            return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                            {
                                Results = [new HistoryReadResult { StatusCode = StatusCodes.Good }]
                            });
                        }
                        return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                        {
                            Results =
                            [
                                new HistoryReadResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    ContinuationPoint = continuationPoint,
                                    HistoryData = new ExtensionObject(new HistoryEvent
                                    {
                                        Events = [new HistoryEventFieldList()]
                                    })
                                }
                            ]
                        });
                    });
            var client = new HistoryClient(mockSession.Object);
            var filter = new EventFilter();
            IAsyncEnumerator<HistoryEventFieldList> enumerator = client.ReadEventsAsync(
                new NodeId("Notifier", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow,
                filter).GetAsyncEnumerator();

            try
            {
                Assert.That(
                    await enumerator.MoveNextAsync().ConfigureAwait(false),
                    Is.True);
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(releasedContinuationPoint, Is.EqualTo(continuationPoint));
        }

        [Test]
        public async Task EventUpdateMethodsBuildMatchingServiceDetailsAsync()
        {
            var capturedDetails = new List<ExtensionObject>();
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<ExtensionObject>, CancellationToken>(
                    (_, details, _) => capturedDetails.Add(details[0]))
                .Returns(new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                {
                    Results =
                    [
                        new HistoryUpdateResult
                        {
                            StatusCode = StatusCodes.Good,
                            OperationResults = [StatusCodes.Good]
                        }
                    ]
                }));
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventId,
                Attributes.Value);
            var eventId = (ByteString)new byte[] { 0x10, 0x20 };
            var fields = new HistoryEventFieldList
            {
                EventFields = [new Variant(eventId)]
            };
            var nodeId = new NodeId("Notifier", 2);
            var client = new HistoryClient(mockSession.Object);

            ArrayOf<StatusCode> inserted = await client.InsertEventsAsync(
                nodeId,
                filter,
                [fields]).ConfigureAwait(false);
            ArrayOf<StatusCode> replaced = await client.ReplaceEventsAsync(
                nodeId,
                filter,
                [fields]).ConfigureAwait(false);
            ArrayOf<StatusCode> updated = await client.UpdateEventsAsync(
                nodeId,
                filter,
                [fields]).ConfigureAwait(false);
            ArrayOf<StatusCode> deleted = await client.DeleteEventsAsync(
                nodeId,
                [eventId]).ConfigureAwait(false);

            Assert.That(inserted[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(replaced[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(updated[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(deleted[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(capturedDetails, Has.Count.EqualTo(4));

            PerformUpdateType[] expectedUpdates =
            [
                PerformUpdateType.Insert,
                PerformUpdateType.Replace,
                PerformUpdateType.Update
            ];
            for (int i = 0; i < expectedUpdates.Length; i++)
            {
                Assert.That(
                    capturedDetails[i].TryGetValue(out UpdateEventDetails details),
                    Is.True);
                Assert.That(details.NodeId, Is.EqualTo(nodeId));
                Assert.That(details.PerformInsertReplace, Is.EqualTo(expectedUpdates[i]));
                Assert.That(details.Filter, Is.SameAs(filter));
                Assert.That(details.EventData, Has.Count.EqualTo(1));
                Assert.That(details.EventData[0], Is.SameAs(fields));
            }
            Assert.That(
                capturedDetails[3].TryGetValue(out DeleteEventDetails deleteDetails),
                Is.True);
            Assert.That(deleteDetails.NodeId, Is.EqualTo(nodeId));
            Assert.That(deleteDetails.EventIds, Is.EqualTo(new[] { eventId }));
        }

        [Test]
        public async Task WriteAnnotationsAsyncSendsOneStructuredRemoveBatchAsync()
        {
            var annotationsNodeId = new NodeId("Annotations", 2);
            ExtensionObject? capturedDetails = null;
            int updateCalls = 0;
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResult(mockSession, annotationsNodeId);
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<ExtensionObject>, CancellationToken>(
                    (_, details, _) =>
                    {
                        updateCalls++;
                        capturedDetails = details[0];
                    })
                .Returns(new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                {
                    Results =
                    [
                        new HistoryUpdateResult
                        {
                            StatusCode = StatusCodes.Good,
                            OperationResults =
                            [
                                StatusCodes.Good,
                                StatusCodes.BadNoEntryExists
                            ]
                        }
                    ]
                }));
            DateTime firstTime = DateTime.UtcNow.AddMinutes(-2);
            DateTime secondTime = DateTime.UtcNow.AddMinutes(-1);
            var client = new HistoryClient(mockSession.Object);

            ArrayOf<StatusCode> statuses = await client.WriteAnnotationsAsync(
                new NodeId("Variable", 2),
                [
                    new Annotation
                    {
                        AnnotationTime = firstTime,
                        Message = "first",
                        UserName = "operator"
                    },
                    new Annotation
                    {
                        AnnotationTime = secondTime,
                        Message = "second",
                        UserName = "operator"
                    }
                ],
                PerformUpdateType.Remove).ConfigureAwait(false);

            Assert.That(updateCalls, Is.EqualTo(1));
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(statuses[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(statuses[1], Is.EqualTo(StatusCodes.BadNoEntryExists));
            Assert.That(capturedDetails, Is.Not.Null);
            ExtensionObject structuredDetails = capturedDetails ??
                throw new AssertionException("No UpdateStructureDataDetails captured.");
            Assert.That(
                structuredDetails.TryGetValue(out UpdateStructureDataDetails details),
                Is.True);
            Assert.That(details.NodeId, Is.EqualTo(annotationsNodeId));
            Assert.That(details.PerformInsertReplace, Is.EqualTo(PerformUpdateType.Remove));
            Assert.That(details.UpdateValues, Has.Count.EqualTo(2));
            Assert.That(details.UpdateValues[0].SourceTimestamp, Is.EqualTo(firstTime));
            Assert.That(details.UpdateValues[1].SourceTimestamp, Is.EqualTo(secondTime));
        }

        private static Mock<ISession> CreateSessionWithNamespaceTable()
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("urn:test:history-client-part11");
            var mockSession = new Mock<ISession>();
            mockSession.SetupGet(s => s.NamespaceUris).Returns(namespaceTable);
            return mockSession;
        }

        private static void SetupBrowsePathResult(
            Mock<ISession> mockSession,
            NodeId targetId)
        {
            mockSession
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = targetId,
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            }
                        ],
                        DiagnosticInfos = []
                    }));
        }
    }
}
