/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Direct tests of the xRegistry fast-path and ResourceType Stream overloads.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [Parallelizable]
    public sealed class XRegistryStreamingTests
    {
        [SetUp]
        public void SetUp()
        {
            m_reads.Clear();
            m_calls.Clear();
            m_values.Clear();
            m_fileChunks.Clear();
            m_beforeCall = null;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
            m_namespace = context.NamespaceUris.GetIndexOrAppend(kRegistryNamespace);
            context.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            m_session = new Mock<ISession>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext).Returns(context);
            m_session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            m_session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, age, timestamps, requests, token) =>
                    {
                        Assert.That(age, Is.Zero);
                        Assert.That(timestamps, Is.EqualTo(TimestampsToReturn.Neither));
                        Assert.That(requests.Count, Is.EqualTo(1));
                        m_reads.Add((requests[0], token));
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [m_values.Dequeue()]
                        });
                    });
            m_session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, token) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    CallMethodRequest request = requests[0];
                    m_calls.Add((request, token));
                    m_beforeCall?.Invoke(request, token);
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = request.MethodId == Ua.MethodIds.FileType_Open
                                    ? [Variant.From(kHandle)]
                                    : request.MethodId == Ua.MethodIds.FileType_Read
                                        ? [Variant.From(m_fileChunks.Dequeue())] : []
                            }
                        ]
                    });
                });
            m_registry = new GenericXRegistryClient(m_session.Object, kRegistryNamespace, telemetry);
            m_resource = m_registry.GetResource(new NodeId("version-7", m_namespace));
        }

        [Test]
        public async Task ResolveResourceToAsyncUsesContentIdentityNamespaceAndAdvancingRangesAsync()
        {
            ByteString contentId = ByteString.From([0xA1, 0xB2]);
            m_values.Enqueue(Value(2));
            m_values.Enqueue(Value(3, 4));
            m_values.Enqueue(Value());
            using var destination = new MemoryStream();
            destination.WriteByte(9);
            using var cancellation = new CancellationTokenSource();

            long count = await m_registry.ResolveResourceToAsync(
                contentId, destination, 3, 4, cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(3),
                    "Only bytes transferred, not pre-existing destination bytes, are counted.");
                Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 9, 2, 3, 4 }));
                Assert.That(destination.CanWrite, Is.True);
                Assert.That(m_reads.Select(r => r.Request.IndexRange), Is.EqualTo(s_resolveRanges));
                Assert.That(m_reads.All(r => r.Request.NodeId == new NodeId(contentId, m_namespace)), Is.True);
                Assert.That(m_reads.All(r => r.Request.NodeId.IdType == IdType.Opaque), Is.True);
                Assert.That(m_reads.All(r => r.Request.AttributeId == Attributes.Value), Is.True);
                Assert.That(m_reads.All(r => r.Token == cancellation.Token), Is.True);
                Assert.That(m_calls, Is.Empty,
                    "The fast path is a ranged Read, not a whole-document FileType download.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResolveResourceToAsyncRejectsNullOrEmptyIdentityWithoutRead(bool emptyNotNull)
        {
            using var destination = new MemoryStream();

            ArgumentException exception = (await Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_registry.ResolveResourceToAsync(
                    emptyNotNull ? ByteString.Empty : default, destination, 8).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo("contentId"));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_reads, Is.Empty);
            Assert.That(m_calls, Is.Empty);
        }

        [TestCaseSource(nameof(s_badReadStatuses))]
        public async Task ResolveResourceToAsyncPreservesFailedReadRatherThanReturningEmptySuccess(StatusCode status)
        {
            m_values.Enqueue(DataValue.FromStatusCode(status));
            using var destination = new MemoryStream();

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_registry.ResolveResourceToAsync(ByteString.From([7]), destination, 8, 4)
                    .ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(status));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_reads, Has.Count.EqualTo(1));
            Assert.That(m_calls, Is.Empty);
        }

        [Test]
        public async Task ResolveResourceToAsyncNoRemainingRangeReturnsZeroWithoutAnotherReadAsync()
        {
            m_values.Enqueue(DataValue.FromStatusCode(StatusCodes.BadIndexRangeNoData));
            using var destination = new MemoryStream();

            long count = await m_registry.ResolveResourceToAsync(
                ByteString.From([7]), destination, 8, 4).ConfigureAwait(false);

            Assert.That(count, Is.Zero);
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_reads.Select(r => r.Request.IndexRange), Is.EqualTo(s_initialRange));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResolveResourceToAsyncEnforcesRangeAndCumulativeLimitBeforeDestinationWrite(bool ignoredRange)
        {
            m_values.Enqueue(Value(11, 12));
            m_values.Enqueue(ignoredRange ? Value(13, 14, 15, 16, 17) : Value(13, 14));
            using var destination = new MemoryStream();

            await Assert.ThrowsAsync<IOException>(async () =>
                await m_registry.ResolveResourceToAsync(
                    ByteString.From([7]), destination, ignoredRange ? 20 : 3, 4).ConfigureAwait(false));

            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 11, 12 }));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_reads.Select(r => r.Request.IndexRange), Is.EqualTo(s_limitRanges));
            Assert.That(m_calls, Is.Empty);
        }

        [Test]
        public async Task WriteDocumentAsyncStreamUsesSuppliedHandleExactChunksAndRetainsSourceAsync()
        {
            using var source = new MemoryStream([0, 21, 22, 23, 24, 25]) { Position = 1 };
            using var cancellation = new CancellationTokenSource();

            long count = await m_resource.WriteDocumentAsync(kHandle, source, 5, 2, cancellation.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(5));
                Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [21, 22], [23, 24], [25] }));
                Assert.That(source.Position, Is.EqualTo(6));
                Assert.That(source.CanRead, Is.True);
                Assert.That(m_calls.Select(c => c.Request.MethodId), Is.EqualTo(new[]
                {
                    Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Write,
                    Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Close
                }));
                Assert.That(m_calls.All(c => c.Token == cancellation.Token), Is.True);
                Assert.That(m_reads, Is.Empty);
            });
            AssertFileTargetAndHandle();
        }

        [Test]
        public async Task WriteDocumentAsyncStreamLimitFailureClosesHandleWithoutWritingOffendingChunk()
        {
            using var source = new MemoryStream([31, 32, 33, 34, 35, 36]);

            await Assert.ThrowsAsync<IOException>(async () =>
                await m_resource.WriteDocumentAsync(kHandle, source, 5, 2).ConfigureAwait(false));

            Assert.That(WrittenChunks(), Is.EqualTo(new byte[][] { [31, 32], [33, 34] }));
            Assert.That(source.Position, Is.EqualTo(6));
            Assert.That(source.CanRead, Is.True);
            Assert.That(m_calls.Select(c => c.Request.MethodId),
                Is.EqualTo(new[]
                {
                    Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Close
                }));
            Assert.That(m_calls[m_calls.Count - 1].Token.CanBeCanceled, Is.False);
            AssertFileTargetAndHandle();
        }

        [Test]
        public async Task WriteDocumentAsyncStreamPreservesWriteFailureAndReleasesOwnedHandle()
        {
            var primary = new ServiceResultException(StatusCodes.BadCommunicationError);
            using var source = new MemoryStream([41, 42]);
            m_beforeCall = (request, _) =>
            {
                if (request.MethodId == Ua.MethodIds.FileType_Write)
                {
                    throw primary;
                }
            };

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_resource.WriteDocumentAsync(kHandle, source, 2, 4).ConfigureAwait(false)))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(m_calls.Select(c => c.Request.MethodId),
                Is.EqualTo(new[] { Ua.MethodIds.FileType_Write, Ua.MethodIds.FileType_Close }));
            Assert.That(m_calls[1].Token.CanBeCanceled, Is.False);
            Assert.That(source.CanRead, Is.True);
            AssertFileTargetAndHandle();
        }

        [Test]
        public async Task ReadDocumentAsyncStreamFollowsShortReadsThroughExactLimitAndClosesAsync()
        {
            m_fileChunks.Enqueue(ByteString.From([51]));
            m_fileChunks.Enqueue(ByteString.From([52, 53]));
            m_fileChunks.Enqueue(ByteString.From([54]));
            m_fileChunks.Enqueue(ByteString.Empty);
            using var destination = new MemoryStream();
            using var cancellation = new CancellationTokenSource();

            long count = await m_resource.ReadDocumentAsync(destination, 4, 4, cancellation.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(4));
                Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 51, 52, 53, 54 }));
                Assert.That(destination.CanWrite, Is.True);
                Assert.That(m_calls[0].Request.InputArguments, Is.EqualTo(new[] { Variant.From((byte)1) }));
                Assert.That(m_calls.Count(c => c.Request.MethodId == Ua.MethodIds.FileType_Read), Is.EqualTo(4));
                Assert.That(m_calls.Count(c => c.Request.MethodId == Ua.MethodIds.FileType_Close), Is.EqualTo(1));
                Assert.That(m_calls.Where(c => c.Request.MethodId == Ua.MethodIds.FileType_Read)
                    .Select(c => c.Request.InputArguments[1]), Is.EqualTo(Enumerable.Repeat(Variant.From(4), 4)));
                Assert.That(m_calls.Where(c => c.Request.MethodId != Ua.MethodIds.FileType_Close)
                    .All(c => c.Token == cancellation.Token), Is.True);
                Assert.That(m_calls[m_calls.Count - 1].Token.CanBeCanceled, Is.False);
                Assert.That(m_reads, Is.Empty);
            });
            AssertFileTargetAndHandle();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadDocumentAsyncStreamRejectsLimitOrIgnoredRangeAndCloses(bool ignoredRange)
        {
            m_fileChunks.Enqueue(ByteString.From([61, 62]));
            m_fileChunks.Enqueue(ignoredRange ? ByteString.From([63, 64, 65, 66, 67]) : ByteString.From([63, 64]));
            using var destination = new MemoryStream();

            await Assert.ThrowsAsync<IOException>(async () =>
                await m_resource.ReadDocumentAsync(destination, ignoredRange ? 20 : 3, 4).ConfigureAwait(false));

            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 61, 62 }));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_calls.Count(c => c.Request.MethodId == Ua.MethodIds.FileType_Read), Is.EqualTo(2));
            Assert.That(m_calls.Count(c => c.Request.MethodId == Ua.MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(m_calls[m_calls.Count - 1].Token.CanBeCanceled, Is.False);
            AssertFileTargetAndHandle();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResourceStreamOverloadsHandleEmptyDocumentsWithoutDisposingCallerAsync(bool upload)
        {
            using var stream = new MemoryStream();
            m_fileChunks.Enqueue(ByteString.Empty);

            long count = upload
                ? await m_resource.WriteDocumentAsync(kHandle, stream, 1, 1).ConfigureAwait(false)
                : await m_resource.ReadDocumentAsync(stream, 1, 1).ConfigureAwait(false);

            Assert.That(count, Is.Zero);
            Assert.That(stream.Length, Is.Zero);
            Assert.That(stream.CanRead && stream.CanWrite, Is.True);
            Assert.That(m_calls.Select(c => c.Request.MethodId), Is.EqualTo(upload
                ? new[] { Ua.MethodIds.FileType_Close }
                : new[] { Ua.MethodIds.FileType_Open, Ua.MethodIds.FileType_Read, Ua.MethodIds.FileType_Close }));
            AssertFileTargetAndHandle();
        }

        [TestCase(false, 0L, 4, "maxBytes")]
        [TestCase(true, 0L, 4, "maxBytes")]
        [TestCase(false, 8L, 0, "chunkSize")]
        [TestCase(true, 8L, 1048577, "chunkSize")]
        public async Task ResourceStreamOverloadsValidateLimitsBeforeRemoteCalls(
            bool upload, long maxBytes, int chunkSize, string parameter)
        {
            using var stream = new MemoryStream();

            ArgumentOutOfRangeException exception = (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            {
                if (upload)
                {
                    await m_resource.WriteDocumentAsync(kHandle, stream, maxBytes, chunkSize).ConfigureAwait(false);
                }
                else
                {
                    await m_resource.ReadDocumentAsync(stream, maxBytes, chunkSize).ConfigureAwait(false);
                }
            }))!;

            Assert.That(exception.ParamName, Is.EqualTo(parameter));
            Assert.That(m_calls, Is.Empty);
            Assert.That(stream.CanRead && stream.CanWrite, Is.True);
        }

        private byte[][] WrittenChunks()
        {
            return m_calls.Where(c => c.Request.MethodId == Ua.MethodIds.FileType_Write).Select(c =>
            {
                Assert.That(c.Request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                return bytes.Span.ToArray();
            }).ToArray();
        }

        private void AssertFileTargetAndHandle()
        {
            Assert.That(m_calls, Is.Not.Empty);
            foreach ((CallMethodRequest request, _) in m_calls)
            {
                Assert.That(request.ObjectId, Is.EqualTo(new NodeId("version-7", m_namespace)));
                if (request.MethodId != Ua.MethodIds.FileType_Open)
                {
                    Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(kHandle)));
                }
            }
        }

        private static DataValue Value(params byte[] bytes)
        {
            return new DataValue(Variant.From(ByteString.From(bytes)));
        }

        private const uint kHandle = 67;
        private const string kRegistryNamespace = "urn:test:streaming-registry";
        private static readonly StatusCode[] s_badReadStatuses =
        [
            StatusCodes.BadNodeIdUnknown, StatusCodes.BadUserAccessDenied
        ];
        private static readonly string[] s_resolveRanges = ["0:3", "1:4", "3:6"];
        private static readonly string[] s_initialRange = ["0:3"];
        private static readonly string[] s_limitRanges = ["0:3", "2:5"];
        private ushort m_namespace;
        private Mock<ISession> m_session = null!;
        private GenericXRegistryClient m_registry = null!;
        private ResourceTypeClient m_resource = null!;
        private Action<CallMethodRequest, CancellationToken>? m_beforeCall;
        private readonly List<(ReadValueId Request, CancellationToken Token)> m_reads = [];
        private readonly List<(CallMethodRequest Request, CancellationToken Token)> m_calls = [];
        private readonly Queue<DataValue> m_values = new();
        private readonly Queue<ByteString> m_fileChunks = new();
    }
}
