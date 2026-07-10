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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("SessionClientExtensions")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionClientExtensionsTests
    {
        [Test]
        public async Task ReadValuesAsyncWithMatchingTypesReturnsValuesAndErrorsAsync()
        {
            using ISession session = CreateSession(
                readResponseFactory: _ => new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new DataValue(new Variant(13u)),
                        new DataValue(new Variant("hello"))
                    ],
                    DiagnosticInfos = []
                });

            (ArrayOf<Variant> values, ArrayOf<ServiceResult> errors) = await session.ReadValuesAsync(
                [new NodeId(1, 0), new NodeId(2, 0)],
                [TypeInfo.Scalars.UInt32, TypeInfo.Scalars.String]).ConfigureAwait(false);

            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(values[0].TryGetValue(out uint first), Is.True);
            Assert.That(first, Is.EqualTo(13u));
            Assert.That(values[1].TryGetValue(out string? second), Is.True);
            Assert.That(second, Is.EqualTo("hello"));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ReadValuesAsyncWithMismatchedTypesReturnsTypeMismatchErrorsAsync()
        {
            using ISession session = CreateSession(
                readResponseFactory: _ => new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new DataValue(new Variant(13u)),
                        new DataValue(new Variant("hello"))
                    ],
                    DiagnosticInfos = []
                });

            (ArrayOf<Variant> values, ArrayOf<ServiceResult> errors) = await session.ReadValuesAsync(
                [new NodeId(1, 0), new NodeId(2, 0)],
                [TypeInfo.Scalars.String, TypeInfo.Scalars.UInt32]).ConfigureAwait(false);

            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public async Task BrowseAsyncWithGoodResultReturnsContinuationAndReferencesAsync()
        {
            ReferenceDescription reference = new()
            {
                NodeId = new ExpandedNodeId(new NodeId(2, 0)),
                BrowseName = QualifiedName.From("Child"),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true
            };

            using ISession session = CreateSession(
                browseResponseFactory: _ => new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = ByteString.From([1, 2, 3]),
                            References = [reference]
                        }
                    ],
                    DiagnosticInfos = []
                });

            (ResponseHeader responseHeader, ByteString continuationPoint, ArrayOf<ReferenceDescription> references) =
                await session.BrowseAsync(
                    null,
                    null,
                    new NodeId(1, 0),
                    0,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.Organizes,
                    false,
                    0).ConfigureAwait(false);

            Assert.That(responseHeader, Is.Not.Null);
            Assert.That(continuationPoint, Is.EqualTo(ByteString.From([1, 2, 3])));
            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].BrowseName, Is.EqualTo(QualifiedName.From("Child")));
        }

        [Test]
        public void BrowseWithBadResultThrowsServiceResultException()
        {
            using ISession session = CreateSession(
                browseResponseFactory: _ => new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.BadUnexpectedError,
                            References = []
                        }
                    ],
                    DiagnosticInfos = []
                });

            Assert.ThrowsAsync<ServiceResultException>(async () => await session.BrowseAsync(
                null,
                null,
                new NodeId(1, 0),
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                false,
                0).ConfigureAwait(false));
        }

        [Test]
        public async Task BrowseNextAsyncWithGoodResultReturnsRevisedContinuationAndReferencesAsync()
        {
            ReferenceDescription reference = new()
            {
                NodeId = new ExpandedNodeId(new NodeId(2, 0)),
                BrowseName = QualifiedName.From("Child"),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true
            };

            using ISession session = CreateSession(
                browseNextResponseFactory: _ => new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = ByteString.From([4, 5, 6]),
                            References = [reference]
                        }
                    ],
                    DiagnosticInfos = []
                });

            (ResponseHeader responseHeader, ByteString revisedContinuationPoint, ArrayOf<ReferenceDescription> references) =
                await session.BrowseNextAsync(
                    null,
                    false,
                    ByteString.From([1, 2, 3])).ConfigureAwait(false);

            Assert.That(responseHeader, Is.Not.Null);
            Assert.That(revisedContinuationPoint, Is.EqualTo(ByteString.From([4, 5, 6])));
            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].BrowseName, Is.EqualTo(QualifiedName.From("Child")));
        }

        [Test]
        public void BrowseNextWithBadResultThrowsServiceResultException()
        {
            using ISession session = CreateSession(
                browseNextResponseFactory: _ => new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.BadUnexpectedError,
                            References = []
                        }
                    ],
                    DiagnosticInfos = []
                });

            Assert.ThrowsAsync<ServiceResultException>(async () => await session.BrowseNextAsync(
                null,
                false,
                ByteString.From([1, 2, 3])).ConfigureAwait(false));
        }

        [Test]
        public async Task ReadBytesAsyncWithSmallChunkReturnsChunkAsync()
        {
            using ISession session = CreateSession(
                readResponseFactory: _ => new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new DataValue(new Variant(ByteString.From([1, 2, 3])))],
                    DiagnosticInfos = []
                });

            ByteString result = await session.ReadBytesAsync(new NodeId(1, 0), 10).ConfigureAwait(false);

            Assert.That(result.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public async Task ReadBytesAsyncWithChunkedResponseConcatenatesChunksAsync()
        {
            using ISession session = CreateSession();
            var readSetup = Mock.Get(session);
            readSetup
                .SetupSequence(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new DataValue(new Variant(ByteString.From([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])))],
                    DiagnosticInfos = []
                }))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new DataValue(new Variant(ByteString.From([11, 12, 13])))],
                    DiagnosticInfos = []
                }));

            ByteString result = await session.ReadBytesAsync(new NodeId(1, 0), 10).ConfigureAwait(false);

            Assert.That(
                result.ToArray(),
                Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }));
        }

        [Test]
        public async Task CallAsyncWithGoodResultReturnsOutputArgumentsAsync()
        {
            var capturedRequests = ArrayOf<CallMethodRequest>.Empty;
            using ISession session = CreateSession(
                callResponseFactory: requests =>
                {
                    capturedRequests = requests;
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = [Variant.From(42u)]
                            }
                        ],
                        DiagnosticInfos = []
                    };
                });

            ArrayOf<Variant> outputArguments = await session.CallAsync(
                new NodeId(1, 0),
                new NodeId(2, 0),
                default,
                new Variant("input")).ConfigureAwait(false);

            Assert.That(capturedRequests.Count, Is.EqualTo(1));
            Assert.That(capturedRequests[0].ObjectId, Is.EqualTo(new NodeId(1, 0)));
            Assert.That(capturedRequests[0].MethodId, Is.EqualTo(new NodeId(2, 0)));
            Assert.That(outputArguments.Count, Is.EqualTo(1));
            Assert.That(outputArguments[0].TryGetValue(out uint value), Is.True);
            Assert.That(value, Is.EqualTo(42u));
        }

        [Test]
        public void CallWithBadResultThrowsServiceResultException()
        {
            using ISession session = CreateSession(
                callResponseFactory: _ => new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.BadUnexpectedError,
                            OutputArguments = []
                        }
                    ],
                    DiagnosticInfos = []
                });

            Assert.ThrowsAsync<ServiceResultException>(async () => await session.CallAsync(
                new NodeId(1, 0),
                new NodeId(2, 0),
                default,
                new Variant("input")).ConfigureAwait(false));
        }

        [Test]
        public async Task ResendDataAsyncReturnsStatusPerSubscriptionAsync()
        {
            using ISession session = CreateSession(
                callResponseFactory: requests => new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new CallMethodResult { StatusCode = StatusCodes.Good },
                        new CallMethodResult { StatusCode = StatusCodes.BadUnexpectedError }
                    ],
                    DiagnosticInfos = []
                });

            ArrayOf<ServiceResult> errors = await session.ResendDataAsync([1u, 2u]).ConfigureAwait(false);

            Assert.That(errors.Count, Is.EqualTo(2));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        private static ISession CreateSession(
            Func<ArrayOf<ReadValueId>, ReadResponse>? readResponseFactory = null,
            Func<ArrayOf<BrowseDescription>, BrowseResponse>? browseResponseFactory = null,
            Func<ArrayOf<ByteString>, BrowseNextResponse>? browseNextResponseFactory = null,
            Func<ArrayOf<CallMethodRequest>, CallResponse>? callResponseFactory = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.MaxByteStringLength = 1024;

            var session = new Mock<ISession>(MockBehavior.Loose);
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            session.SetupGet(s => s.ServerUris).Returns(messageContext.ServerUris);

            if (readResponseFactory != null)
            {
                session.Setup(s => s.ReadAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<double>(),
                        It.IsAny<TimestampsToReturn>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                        (_, _, _, readValueIds, _) => new ValueTask<ReadResponse>(readResponseFactory(readValueIds)));
            }

            if (browseResponseFactory != null)
            {
                session.Setup(s => s.BrowseAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ViewDescription>(),
                        It.IsAny<uint>(),
                        It.IsAny<ArrayOf<BrowseDescription>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                        (_, _, _, browseDescriptions, _) => new ValueTask<BrowseResponse>(browseResponseFactory(browseDescriptions)));
            }

            if (browseNextResponseFactory != null)
            {
                session.Setup(s => s.BrowseNextAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<bool>(),
                        It.IsAny<ArrayOf<ByteString>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, bool, ArrayOf<ByteString>, CancellationToken>(
                        (_, _, continuationPoints, _) => new ValueTask<BrowseNextResponse>(browseNextResponseFactory(continuationPoints)));
            }

            if (callResponseFactory != null)
            {
                session.Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<CallMethodRequest>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                        (_, requests, _) => new ValueTask<CallResponse>(callResponseFactory(requests)));
            }

            return session.Object;
        }
    }
}
