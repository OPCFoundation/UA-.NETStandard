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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Historian;

namespace Opc.Ua.Client.Tests.Historian
{
    /// <summary>
    /// Unit tests for <see cref="HistoryClient"/> that use a mocked
    /// <see cref="ISession"/> to exercise error paths that are difficult
    /// to trigger over the wire.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    public class HistoryClientUnitTests
    {
        [Test]
        public void ConstructorWithNullSessionThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new HistoryClient(null!));
        }

        [Test]
        public Task ReadRawThrowsServiceResultExceptionOnBadStatusAsync()
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
                    Results = new HistoryReadResult[]
                    {
                        new()
                        {
                            StatusCode = StatusCodes.BadHistoryOperationInvalid
                        }
                    }.ToArrayOf()
                }));

            var client = new HistoryClient(mockSession.Object);
            var nodeId = new NodeId("TestNode", 2);
            DateTime start = DateTime.UtcNow.AddHours(-1);
            DateTime end = DateTime.UtcNow;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (DataValue v in client.ReadRawAsync(nodeId, start, end).ConfigureAwait(false))
                {
                    // should not reach here
                }
            });

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationInvalid));
            return Task.CompletedTask;
        }

        [Test]
        public async Task ReadRawWithEmptyResultsYieldsNothingAsync()
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
                    Results = Array.Empty<HistoryReadResult>().ToArrayOf()
                }));

            var client = new HistoryClient(mockSession.Object);
            var values = new List<DataValue>();

            await foreach (DataValue v in client.ReadRawAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow).ConfigureAwait(false))
            {
                values.Add(v);
            }

            Assert.That(values, Is.Empty,
                "When HistoryRead returns an empty Results array the iterator should yield break.");
        }

        [Test]
        public Task ReadRawWithThreeEmptyPagesAndContinuationThrowsBadInternalErrorAsync()
        {
            int callCount = 0;
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    callCount++;
                    return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                    {
                        Results = new HistoryReadResult[]
                        {
                            new()
                            {
                                StatusCode = StatusCodes.Good,
                                ContinuationPoint = (ByteString)new byte[] { 0x01 }
                            }
                        }.ToArrayOf()
                    });
                });

            var client = new HistoryClient(mockSession.Object);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (DataValue v in client.ReadRawAsync(
                    new NodeId("TestNode", 2),
                    DateTime.UtcNow.AddHours(-1),
                    DateTime.UtcNow).ConfigureAwait(false))
                {
                    // should never yield
                }
            });

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(callCount, Is.GreaterThanOrEqualTo(3),
                "The guard should trigger after three consecutive empty pages.");
            return Task.CompletedTask;
        }

        [Test]
        public async Task PerformUpdateWithEmptyResultsReturnsEmptyListAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                {
                    Results = Array.Empty<HistoryUpdateResult>().ToArrayOf()
                }));

            var client = new HistoryClient(mockSession.Object);
            DateTime now = DateTime.UtcNow;

            IList<StatusCode> result = await client.InsertAsync(
                new NodeId("TestNode", 2),
                [
                    new DataValue(
                        new Variant(1.0),
                        StatusCodes.Good,
                        sourceTimestamp: now,
                        serverTimestamp: now)
                ]).ConfigureAwait(false);

            Assert.That(result, Is.Empty,
                "When HistoryUpdate returns an empty Results array, InsertAsync should return an empty list.");
        }

        [Test]
        public async Task ReadAtTimeAsyncReturnsNoValuesAndCapturesDetailsAsync()
        {
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
                .Callback<RequestHeader, ExtensionObject, TimestampsToReturn, bool, ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, details, _, _, _, _) => capturedDetails = details)
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results = [new HistoryReadResult
                    {
                        StatusCode = StatusCodes.Good,
                        ContinuationPoint = ByteString.Empty
                    }]
                }));

            var client = new HistoryClient(mockSession.Object);
            var values = new List<DataValue>();

            await foreach (DataValue value in client.ReadAtTimeAsync(
                new NodeId("TestNode", 2),
                [DateTime.UtcNow.AddHours(-1), DateTime.UtcNow],
                useSimpleBounds: true).ConfigureAwait(false))
            {
                values.Add(value);
            }

            Assert.That(values, Is.Empty);
            Assert.That(capturedDetails, Is.Not.Null);
            ExtensionObject detailsObject = capturedDetails ?? throw new AssertionException("No ReadAtTimeDetails captured.");
            Assert.That(detailsObject.TryGetValue(out ReadAtTimeDetails details), Is.True);
            Assert.That(details.UseSimpleBounds, Is.True);
            Assert.That(details.ReqTimes.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ReadProcessedAsyncReturnsNoValuesAndUsesDefaultConfigurationAsync()
        {
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
                .Callback<RequestHeader, ExtensionObject, TimestampsToReturn, bool, ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, details, _, _, _, _) => capturedDetails = details)
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results = [new HistoryReadResult
                    {
                        StatusCode = StatusCodes.Good,
                        ContinuationPoint = ByteString.Empty
                    }]
                }));

            var client = new HistoryClient(mockSession.Object);
            var values = new List<DataValue>();

            await foreach (DataValue value in client.ReadProcessedAsync(
                new NodeId("TestNode", 2),
                new NodeId("Aggregate", 2),
                DateTime.UtcNow.AddHours(-2),
                DateTime.UtcNow,
                2.5).ConfigureAwait(false))
            {
                values.Add(value);
            }

            Assert.That(values, Is.Empty);
            Assert.That(capturedDetails, Is.Not.Null);
            ExtensionObject detailsObject = capturedDetails ?? throw new AssertionException("No ReadProcessedDetails captured.");
            Assert.That(detailsObject.TryGetValue(out ReadProcessedDetails details), Is.True);
            Assert.That(details.AggregateType.Count, Is.EqualTo(1));
            Assert.That(details.AggregateType[0], Is.EqualTo(new NodeId("Aggregate", 2)));
            Assert.That(details.AggregateConfiguration.UseServerCapabilitiesDefaults, Is.True);
            Assert.That(details.ProcessingInterval, Is.EqualTo(2.5));
        }

        [Test]
        public async Task ReadAnnotationsAsyncReturnsEmptyWhenPropertyIsMissingAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [],
                        DiagnosticInfos = []
                    }));

            var client = new HistoryClient(mockSession.Object);
            var annotations = new List<Annotation>();

            await foreach (Annotation annotation in client.ReadAnnotationsAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow).ConfigureAwait(false))
            {
                annotations.Add(annotation);
            }

            Assert.That(annotations, Is.Empty);
        }

        [Test]
        public async Task WriteAnnotationAsyncReturnsBadNodeIdUnknownWhenPropertyIsMissingAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [],
                        DiagnosticInfos = []
                    }));

            var client = new HistoryClient(mockSession.Object);

            StatusCode result = await client.WriteAnnotationAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow,
                "note").ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task DeleteAnnotationAsyncReturnsBadNodeIdUnknownWhenPropertyIsMissingAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [],
                        DiagnosticInfos = []
                    }));

            var client = new HistoryClient(mockSession.Object);

            StatusCode result = await client.DeleteAnnotationAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task GetServerCapabilitiesAsyncReadsValuesAndUsesDefaultsForBadEntriesAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        DataValue.FromStatusCode(StatusCodes.BadUnexpectedError),
                        new DataValue(new Variant(true)),
                        new DataValue(new Variant(123u)),
                        DataValue.FromStatusCode(StatusCodes.BadUnexpectedError),
                        new DataValue(new Variant(true)),
                        new DataValue(new Variant(false)),
                        DataValue.FromStatusCode(StatusCodes.BadUnexpectedError),
                        new DataValue(new Variant(true)),
                        new DataValue(new Variant(false)),
                        new DataValue(new Variant(true)),
                        new DataValue(new Variant(true))
                    ],
                    DiagnosticInfos = []
                }));

            var client = new HistoryClient(mockSession.Object);

            HistoryServerCapabilitiesInfo capabilities = await client.GetServerCapabilitiesAsync().ConfigureAwait(false);

            Assert.That(capabilities.AccessHistoryData, Is.False);
            Assert.That(capabilities.AccessHistoryEvents, Is.True);
            Assert.That(capabilities.MaxReturnDataValues, Is.EqualTo(123u));
            Assert.That(capabilities.MaxReturnEventValues, Is.Zero);
            Assert.That(capabilities.InsertData, Is.True);
            Assert.That(capabilities.ReplaceData, Is.False);
            Assert.That(capabilities.UpdateData, Is.False);
            Assert.That(capabilities.DeleteRaw, Is.True);
            Assert.That(capabilities.DeleteAtTime, Is.False);
            Assert.That(capabilities.InsertAnnotation, Is.True);
            Assert.That(capabilities.ServerTimestampSupported, Is.True);
        }

        [Test]
        public async Task ReadModifiedAsyncYieldsHistoryDataAndUsesModifiedDetailsAsync()
        {
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
                .Callback<RequestHeader, ExtensionObject, TimestampsToReturn, bool, ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, details, _, _, _, _) => capturedDetails = details)
                .Returns(new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results = [new HistoryReadResult
                    {
                        StatusCode = StatusCodes.Good,
                        ContinuationPoint = ByteString.Empty,
                        HistoryData = new ExtensionObject(new HistoryData
                        {
                            DataValues =
                            [
                                new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow)
                            ]
                        })
                    }]
                }));

            var client = new HistoryClient(mockSession.Object);
            var values = new List<DataValue>();

            await foreach (DataValue value in client.ReadModifiedAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow,
                maxValuesPerNode: 10u,
                timestampsToReturn: TimestampsToReturn.Both).ConfigureAwait(false))
            {
                values.Add(value);
            }

            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(values[0].WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(42));
            Assert.That(capturedDetails, Is.Not.Null);
            ExtensionObject detailsObject = capturedDetails ?? throw new AssertionException("No ReadModified details captured.");
            Assert.That(detailsObject.TryGetValue(out ReadRawModifiedDetails details), Is.True);
            Assert.That(details.IsReadModified, Is.True);
            Assert.That(details.NumValuesPerNode, Is.EqualTo(10u));
        }

        [Test]
        public async Task ReadRawAsyncReleasesContinuationPointWhenEnumeratorIsDisposedAsync()
        {
            var releaseCalls = new List<HistoryReadValueId>();
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ExtensionObject, TimestampsToReturn, bool, ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, _, _, releaseContinuationPoints, nodesToRead, _) =>
                    {
                        if (releaseContinuationPoints)
                        {
                            releaseCalls.Add(nodesToRead[0]);
                            return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                            {
                                Results = [new HistoryReadResult { StatusCode = StatusCodes.Good }]
                            });
                        }

                        return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                        {
                            Results = [new HistoryReadResult
                            {
                                StatusCode = StatusCodes.Good,
                                ContinuationPoint = (ByteString)new byte[] { 0x7a },
                                HistoryData = new ExtensionObject(new HistoryData
                                {
                                    DataValues =
                                    [
                                        new DataValue(new Variant("first"), StatusCodes.Good, DateTime.UtcNow)
                                    ]
                                })
                            }]
                        });
                    });

            var client = new HistoryClient(mockSession.Object);
            IAsyncEnumerator<DataValue> enumerator = client
                .ReadRawAsync(new NodeId("TestNode", 2), DateTime.UtcNow.AddHours(-1), DateTime.UtcNow)
                .GetAsyncEnumerator();

            try
            {
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current.WrappedValue.TryGetValue(out string value), Is.True);
                Assert.That(value, Is.EqualTo("first"));
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            Assert.That(releaseCalls, Has.Count.EqualTo(1));
            Assert.That(releaseCalls[0].ContinuationPoint, Is.EqualTo((ByteString)new byte[] { 0x7a }));
        }

        [Test]
        public async Task DeleteMethodsReturnOperationStatusesAsync()
        {
            ExtensionObject? deleteRawDetails = null;
            ExtensionObject? deleteAtTimeDetails = null;
            int callCount = 0;
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<ExtensionObject>, CancellationToken>((_, details, _) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        deleteRawDetails = details[0];
                        return new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                        {
                            Results = [new HistoryUpdateResult { StatusCode = StatusCodes.Good }]
                        });
                    }

                    deleteAtTimeDetails = details[0];
                    return new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                    {
                        Results = [new HistoryUpdateResult
                        {
                            StatusCode = StatusCodes.Good,
                            OperationResults =
                            [
                                StatusCodes.Good,
                                StatusCodes.BadNoData
                            ]
                        }]
                    });
                });

            var client = new HistoryClient(mockSession.Object);

            StatusCode deleteRaw = await client.DeleteRawAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow.AddHours(-2),
                DateTime.UtcNow,
                isDeleteModified: true).ConfigureAwait(false);
            IList<StatusCode> deleteAtTime = await client.DeleteAtTimeAsync(
                new NodeId("TestNode", 2),
                [DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow]).ConfigureAwait(false);

            Assert.That(deleteRaw, Is.EqualTo(StatusCodes.Good));
            Assert.That(deleteRawDetails, Is.Not.Null);
            ExtensionObject rawDetailsObject = deleteRawDetails ?? throw new AssertionException("No raw delete details.");
            Assert.That(rawDetailsObject.TryGetValue(out DeleteRawModifiedDetails rawDetails), Is.True);
            Assert.That(rawDetails.IsDeleteModified, Is.True);
            Assert.That(deleteAtTime, Has.Count.EqualTo(2));
            Assert.That(deleteAtTime[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(deleteAtTime[1], Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(deleteAtTimeDetails, Is.Not.Null);
            ExtensionObject atTimeDetailsObject = deleteAtTimeDetails ?? throw new AssertionException("No at-time delete details.");
            Assert.That(atTimeDetailsObject.TryGetValue(out DeleteAtTimeDetails atTimeDetails), Is.True);
            Assert.That(atTimeDetails.ReqTimes.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ReadAnnotationsAsyncYieldsResolvedAnnotationValuesAsync()
        {
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResults(mockSession, new NodeId("Annotations", 2));
            var expected = new Annotation
            {
                Message = "operator note",
                UserName = "user",
                AnnotationTime = DateTime.UtcNow
            };
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
                    Results = [new HistoryReadResult
                    {
                        StatusCode = StatusCodes.Good,
                        ContinuationPoint = ByteString.Empty,
                        HistoryData = new ExtensionObject(new HistoryData
                        {
                            DataValues =
                            [
                                new DataValue(new Variant(123)),
                                new DataValue(new Variant(new ExtensionObject(expected)))
                            ]
                        })
                    }]
                }));

            var client = new HistoryClient(mockSession.Object);
            var annotations = new List<Annotation>();

            await foreach (Annotation annotation in client.ReadAnnotationsAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow).ConfigureAwait(false))
            {
                annotations.Add(annotation);
            }

            Assert.That(annotations, Has.Count.EqualTo(1));
            Assert.That(annotations[0].Message, Is.EqualTo("operator note"));
            Assert.That(annotations[0].UserName, Is.EqualTo("user"));
        }

        [Test]
        public async Task WriteAnnotationAsyncUsesResolvedPropertyAndReturnsOperationResultAsync()
        {
            ExtensionObject? capturedDetails = null;
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResults(mockSession, new NodeId("Annotations", 2));
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<ExtensionObject>, CancellationToken>(
                    (_, details, _) => capturedDetails = details[0])
                .Returns(new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                {
                    Results = [new HistoryUpdateResult
                    {
                        StatusCode = StatusCodes.Good,
                        OperationResults = [StatusCodes.BadNoData]
                    }]
                }));

            var client = new HistoryClient(mockSession.Object);

            StatusCode result = await client.WriteAnnotationAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow,
                "note",
                "operator",
                PerformUpdateType.Replace).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(capturedDetails, Is.Not.Null);
            ExtensionObject detailsObject = capturedDetails ?? throw new AssertionException("No update details.");
            Assert.That(detailsObject.TryGetValue(out UpdateStructureDataDetails details), Is.True);
            Assert.That(details.NodeId, Is.EqualTo(new NodeId("Annotations", 2)));
            Assert.That(details.PerformInsertReplace, Is.EqualTo(PerformUpdateType.Replace));
            Assert.That(details.UpdateValues.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task GetConfigurationAsyncReadsResolvedPropertyValuesAsync()
        {
            DateTime startOfArchive = DateTime.UtcNow.AddDays(-10);
            DateTime startOfOnline = DateTime.UtcNow.AddDays(-1);
            var nodes = new Queue<NodeId>(new[]
            {
                new NodeId("HAConfiguration", 2),
                new NodeId("Stepped", 2),
                new NodeId("Definition", 2),
                new NodeId("MaxTimeInterval", 2),
                new NodeId("MinTimeInterval", 2),
                new NodeId("ExceptionDeviation", 2),
                new NodeId("StartOfArchive", 2),
                new NodeId("StartOfOnlineArchive", 2),
                new NodeId("AggregateConfiguration", 2),
                new NodeId("PercentDataGood", 2),
                new NodeId("PercentDataBad", 2),
                new NodeId("TreatUncertainAsBad", 2),
                new NodeId("UseSlopedExtrapolation", 2)
            });
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResults(mockSession, nodes);
            int readCall = 0;
            mockSession
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    DataValue[] results = readCall++ == 0
                        ?
                    [
                        new DataValue(new Variant(true)),
                        new DataValue(new Variant("configured")),
                        new DataValue(new Variant(1000d)),
                        new DataValue(new Variant(1d)),
                        new DataValue(new Variant(0.5d)),
                        new DataValue(new Variant((DateTimeUtc)startOfArchive)),
                        new DataValue(new Variant((DateTimeUtc)startOfOnline))
                    ]
                        :
                    [
                        new DataValue(new Variant((byte)100)),
                        new DataValue(new Variant((byte)100)),
                        new DataValue(new Variant(true)),
                        new DataValue(new Variant(false))
                    ];
                    return new ValueTask<ReadResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results,
                        DiagnosticInfos = []
                    });
                });

            var client = new HistoryClient(mockSession.Object);

            HistoricalDataConfigurationInfo configuration = await client.GetConfigurationAsync(
                new NodeId("TestNode", 2)).ConfigureAwait(false);

            Assert.That(configuration.HasConfiguration, Is.True);
            Assert.That(configuration.Stepped, Is.True);
            Assert.That(configuration.Definition, Is.EqualTo("configured"));
            Assert.That(configuration.MaxTimeInterval, Is.EqualTo(1000d));
            Assert.That(configuration.MinTimeInterval, Is.EqualTo(1d));
            Assert.That(configuration.ExceptionDeviation, Is.EqualTo(0.5d));
            Assert.That(configuration.StartOfArchive, Is.EqualTo(startOfArchive));
            Assert.That(configuration.StartOfOnlineArchive, Is.EqualTo(startOfOnline));
            Assert.That(configuration.AggregateConfiguration, Is.Not.Null);
            Assert.That(configuration.AggregateConfiguration!.PercentDataGood, Is.EqualTo((byte)100));
            Assert.That(configuration.AggregateConfiguration.PercentDataBad, Is.EqualTo((byte)100));
            Assert.That(configuration.AggregateConfiguration.TreatUncertainAsBad, Is.True);
            Assert.That(configuration.AggregateConfiguration.UseSlopedExtrapolation, Is.False);
        }

        [Test]
        public void ReadAtTimeAsyncWithNullTimesThrowsArgumentNullException()
        {
            var client = new HistoryClient(new Mock<ISession>().Object);

            Assert.That(
                () => client.ReadAtTimeAsync(new NodeId("TestNode", 2), null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("times"));
        }

        [Test]
        public async Task ReplaceAndUpdateAsyncReturnOperationResultsAsync()
        {
            var capturedUpdates = new List<UpdateDataDetails>();
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<ExtensionObject>, CancellationToken>((_, details, _) =>
                {
                    Assert.That(details[0].TryGetValue(out UpdateDataDetails update), Is.True);
                    capturedUpdates.Add(update);
                    return new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                    {
                        Results = [new HistoryUpdateResult
                        {
                            StatusCode = StatusCodes.Good,
                            OperationResults = [StatusCodes.GoodClamped]
                        }]
                    });
                });
            var client = new HistoryClient(mockSession.Object);
            var values = new List<DataValue>
            {
                new(new Variant(1), StatusCodes.Good, DateTime.UtcNow)
            };

            IList<StatusCode> replace = await client.ReplaceAsync(new NodeId("TestNode", 2), values).ConfigureAwait(false);
            IList<StatusCode> update = await client.UpdateAsync(new NodeId("TestNode", 2), values).ConfigureAwait(false);

            Assert.That(replace, Has.Count.EqualTo(1));
            Assert.That(update, Has.Count.EqualTo(1));
            Assert.That(replace[0], Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(update[0], Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(capturedUpdates, Has.Count.EqualTo(2));
            Assert.That(capturedUpdates[0].PerformInsertReplace, Is.EqualTo(PerformUpdateType.Replace));
            Assert.That(capturedUpdates[1].PerformInsertReplace, Is.EqualTo(PerformUpdateType.Update));
        }

        [Test]
        public async Task DeleteRawAsyncReturnsBadInternalErrorForEmptyResultsAsync()
        {
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                {
                    Results = []
                }));
            var client = new HistoryClient(mockSession.Object);

            StatusCode result = await client.DeleteRawAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public async Task WriteAnnotationAsyncFallsBackToStatusAndBadInternalErrorAsync()
        {
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResults(mockSession, new NodeId("Annotations", 2));
            int callCount = 0;
            mockSession
                .Setup(s => s.HistoryUpdateAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<ExtensionObject>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    callCount++;
                    return new ValueTask<HistoryUpdateResponse>(new HistoryUpdateResponse
                    {
                        Results = callCount == 1
                            ? [new HistoryUpdateResult { StatusCode = StatusCodes.BadNoData }]
                            : []
                    });
                });
            var client = new HistoryClient(mockSession.Object);

            StatusCode statusFallback = await client.WriteAnnotationAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow,
                "note").ConfigureAwait(false);
            StatusCode emptyResults = await client.WriteAnnotationAsync(
                new NodeId("TestNode", 2),
                DateTime.UtcNow,
                "note").ConfigureAwait(false);

            Assert.That(statusFallback, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(emptyResults, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public async Task GetConfigurationAsyncReturnsEmptyWhenConfigurationNodeIsMissingAsync()
        {
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResults(mockSession, NodeId.Null);
            var client = new HistoryClient(mockSession.Object);

            HistoricalDataConfigurationInfo configuration = await client.GetConfigurationAsync(
                new NodeId("TestNode", 2)).ConfigureAwait(false);

            Assert.That(configuration.HasConfiguration, Is.False);
        }

        [Test]
        public async Task GetConfigurationAsyncUsesNullsWhenChildPropertiesAreMissingAsync()
        {
            var nodes = new Queue<NodeId>(new[]
            {
                new NodeId("HAConfiguration", 2),
                NodeId.Null,
                NodeId.Null,
                NodeId.Null,
                NodeId.Null,
                NodeId.Null,
                NodeId.Null,
                NodeId.Null,
                NodeId.Null
            });
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResults(mockSession, nodes);
            mockSession
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [],
                    DiagnosticInfos = []
                }));
            var client = new HistoryClient(mockSession.Object);

            HistoricalDataConfigurationInfo configuration = await client.GetConfigurationAsync(
                new NodeId("TestNode", 2)).ConfigureAwait(false);

            Assert.That(configuration.HasConfiguration, Is.True);
            Assert.That(configuration.Stepped, Is.Null);
            Assert.That(configuration.Definition, Is.Null);
            Assert.That(configuration.MaxTimeInterval, Is.Null);
            Assert.That(configuration.MinTimeInterval, Is.Null);
            Assert.That(configuration.ExceptionDeviation, Is.Null);
            Assert.That(configuration.StartOfArchive, Is.Null);
            Assert.That(configuration.StartOfOnlineArchive, Is.Null);
            Assert.That(configuration.AggregateConfiguration, Is.Null);
        }

        [Test]
        public async Task ReadAtTimeAsyncReleasesContinuationPointWhenEnumeratorIsDisposedAsync()
        {
            var releaseCalls = new List<HistoryReadValueId>();
            var mockSession = new Mock<ISession>();
            mockSession
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ExtensionObject, TimestampsToReturn, bool, ArrayOf<HistoryReadValueId>, CancellationToken>(
                    (_, _, _, releaseContinuationPoints, nodesToRead, _) =>
                    {
                        if (releaseContinuationPoints)
                        {
                            releaseCalls.Add(nodesToRead[0]);
                            return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                            {
                                Results = [new HistoryReadResult { StatusCode = StatusCodes.Good }]
                            });
                        }

                        return new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                        {
                            Results = [new HistoryReadResult
                            {
                                StatusCode = StatusCodes.Good,
                                ContinuationPoint = (ByteString)new byte[] { 0x22 },
                                HistoryData = new ExtensionObject(new HistoryData
                                {
                                    DataValues =
                                    [
                                        new DataValue(new Variant(1234), StatusCodes.Good, DateTime.UtcNow)
                                    ]
                                })
                            }]
                        });
                    });
            var client = new HistoryClient(mockSession.Object);
            IAsyncEnumerator<DataValue> enumerator = client
                .ReadAtTimeAsync(new NodeId("TestNode", 2), [DateTime.UtcNow])
                .GetAsyncEnumerator();

            try
            {
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            Assert.That(releaseCalls, Has.Count.EqualTo(1));
            Assert.That(releaseCalls[0].ContinuationPoint, Is.EqualTo((ByteString)new byte[] { 0x22 }));
        }

        [Test]
        public void ReadAtTimeAsyncWithThreeEmptyPagesAndContinuationThrowsBadInternalError()
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
                    Results = [new HistoryReadResult
                    {
                        StatusCode = StatusCodes.Good,
                        ContinuationPoint = (ByteString)new byte[] { 0x33 }
                    }]
                }));
            var client = new HistoryClient(mockSession.Object);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (DataValue value in client.ReadAtTimeAsync(
                    new NodeId("TestNode", 2),
                    [DateTime.UtcNow]).ConfigureAwait(false))
                {
                    Assert.Fail($"Unexpected value {value}.");
                }
            })!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public async Task GetConfigurationAsyncUsesDefaultValuesForBadPropertyReadsAsync()
        {
            var nodes = new Queue<NodeId>(new[]
            {
                new NodeId("HAConfiguration", 2),
                new NodeId("Stepped", 2),
                new NodeId("Definition", 2),
                new NodeId("MaxTimeInterval", 2),
                new NodeId("MinTimeInterval", 2),
                new NodeId("ExceptionDeviation", 2),
                new NodeId("StartOfArchive", 2),
                new NodeId("StartOfOnlineArchive", 2),
                new NodeId("AggregateConfiguration", 2),
                new NodeId("PercentDataGood", 2),
                new NodeId("PercentDataBad", 2),
                new NodeId("TreatUncertainAsBad", 2),
                new NodeId("UseSlopedExtrapolation", 2)
            });
            var mockSession = CreateSessionWithNamespaceTable();
            SetupBrowsePathResults(mockSession, nodes);
            mockSession
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        DataValue.FromStatusCode(StatusCodes.BadNoData),
                        DataValue.FromStatusCode(StatusCodes.BadNoData),
                        DataValue.FromStatusCode(StatusCodes.BadNoData),
                        DataValue.FromStatusCode(StatusCodes.BadNoData),
                        DataValue.FromStatusCode(StatusCodes.BadNoData),
                        DataValue.FromStatusCode(StatusCodes.BadNoData),
                        DataValue.FromStatusCode(StatusCodes.BadNoData)
                    ],
                    DiagnosticInfos = []
                }));
            var client = new HistoryClient(mockSession.Object);

            HistoricalDataConfigurationInfo configuration = await client.GetConfigurationAsync(
                new NodeId("TestNode", 2)).ConfigureAwait(false);

            Assert.That(configuration.HasConfiguration, Is.True);
            Assert.That(configuration.Stepped, Is.False);
            Assert.That(configuration.Definition, Is.Null);
            Assert.That(configuration.MaxTimeInterval, Is.Zero);
            Assert.That(configuration.MinTimeInterval, Is.Zero);
            Assert.That(configuration.ExceptionDeviation, Is.Zero);
            Assert.That(configuration.StartOfArchive, Is.EqualTo(DateTimeUtc.MinValue.ToDateTime()));
            Assert.That(configuration.StartOfOnlineArchive, Is.EqualTo(DateTimeUtc.MinValue.ToDateTime()));
            Assert.That(configuration.AggregateConfiguration, Is.Not.Null);
            Assert.That(configuration.AggregateConfiguration!.PercentDataGood, Is.Zero);
            Assert.That(configuration.AggregateConfiguration.PercentDataBad, Is.Zero);
            Assert.That(configuration.AggregateConfiguration.TreatUncertainAsBad, Is.False);
            Assert.That(configuration.AggregateConfiguration.UseSlopedExtrapolation, Is.False);
        }

        private static Mock<ISession> CreateSessionWithNamespaceTable()
        {
            var mockSession = new Mock<ISession>();
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("urn:test");
            mockSession.SetupGet(s => s.NamespaceUris).Returns(namespaceTable);
            return mockSession;
        }

        private static void SetupBrowsePathResults(Mock<ISession> mockSession, NodeId nodeId)
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
                        Results = [CreateBrowsePathResult(nodeId)],
                        DiagnosticInfos = []
                    }));
        }

        private static void SetupBrowsePathResults(Mock<ISession> mockSession, Queue<NodeId> nodeIds)
        {
            mockSession
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [CreateBrowsePathResult(nodeIds.Dequeue())],
                        DiagnosticInfos = []
                    }));
        }

        private static BrowsePathResult CreateBrowsePathResult(NodeId nodeId)
        {
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets =
                [
                    new BrowsePathTarget
                    {
                        TargetId = nodeId,
                        RemainingPathIndex = uint.MaxValue
                    }
                ]
            };
        }
    }
}
