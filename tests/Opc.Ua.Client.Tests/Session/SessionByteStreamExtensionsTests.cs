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
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Bounded ranged reads are asserted at the Read service and destination boundaries.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class SessionByteStreamExtensionsTests
    {
        [SetUp]
        public void SetUp()
        {
            m_requests.Clear();
            m_values.Clear();
            m_context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            m_session.SetupGet(s => s.MessageContext).Returns(m_context);
            m_session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, age, timestamps, requests, token) =>
                    {
                        Assert.That(requests.Count, Is.EqualTo(1));
                        Assert.That(age, Is.Zero);
                        Assert.That(timestamps, Is.EqualTo(TimestampsToReturn.Neither));
                        m_requests.Add((requests[0], token));
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [m_values.Dequeue()]
                        });
                    });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadBytesToAsyncAdvancesRangesAfterShortReadsUntilExplicitEndAsync(bool noData)
        {
            m_values.Enqueue(Value(10, 11));
            m_values.Enqueue(Value(12));
            m_values.Enqueue(Value(13, 14));
            m_values.Enqueue(noData
                ? DataValue.FromStatusCode(StatusCodes.BadIndexRangeNoData)
                : Value());
            using var destination = new MemoryStream();
            using var cancellation = new CancellationTokenSource();

            long count = await m_session.Object.ReadBytesToAsync(
                new NodeId("content", 3), destination, maxBytes: 5, chunkSize: 4,
                cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(count, Is.EqualTo(5));
                Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 10, 11, 12, 13, 14 }));
                Assert.That(destination.CanWrite, Is.True);
                Assert.That(m_requests.Select(r => r.Request.IndexRange),
                    Is.EqualTo(s_shortReadRanges));
                Assert.That(m_requests.All(r => r.Request.NodeId == new NodeId("content", 3)), Is.True);
                Assert.That(m_requests.All(r => r.Request.AttributeId == Attributes.Value), Is.True);
                Assert.That(m_requests.All(r => r.Token == cancellation.Token), Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadBytesToAsyncEmptyInitialValueReturnsZeroWithoutAnotherReadAsync(bool noData)
        {
            m_values.Enqueue(noData
                ? DataValue.FromStatusCode(StatusCodes.BadIndexRangeNoData)
                : Value());
            using var destination = new MemoryStream();

            long count = await ReadAsync(destination, 1, 1).ConfigureAwait(false);

            Assert.That(count, Is.Zero);
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_requests.Select(r => r.Request.IndexRange), Is.EqualTo(s_singleByteRange));
        }

        [Test]
        public async Task ReadBytesToAsyncRejectsCumulativeLimitBeforeWritingOffendingChunk()
        {
            m_values.Enqueue(Value(1, 2, 3));
            m_values.Enqueue(Value(4, 5, 6));
            using var destination = new MemoryStream();

            await Assert.ThrowsAsync<IOException>(async () =>
                await ReadAsync(destination, maxBytes: 5, chunkSize: 4).ConfigureAwait(false));

            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(m_requests.Select(r => r.Request.IndexRange), Is.EqualTo(s_cumulativeRanges));
            Assert.That(destination.CanWrite, Is.True);
        }

        [Test]
        public async Task ReadBytesToAsyncRejectsServerIgnoringRequestedRangeBeforeWriting()
        {
            m_values.Enqueue(Value(1, 2, 3, 4, 5));
            using var destination = new MemoryStream();

            await Assert.ThrowsAsync<IOException>(async () =>
                await ReadAsync(destination, maxBytes: 20, chunkSize: 4).ConfigureAwait(false));

            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_requests.Select(r => r.Request.IndexRange), Is.EqualTo(s_initialRange));
        }

        [Test]
        public async Task ReadBytesToAsyncHonorsSessionByteStringCapAsync()
        {
            m_context.MaxByteStringLength = 2;
            m_values.Enqueue(Value(21, 22));
            m_values.Enqueue(Value(23));
            m_values.Enqueue(Value());
            using var destination = new MemoryStream();

            long count = await ReadAsync(destination, maxBytes: 3, chunkSize: 9).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 21, 22, 23 }));
            Assert.That(m_requests.Select(r => r.Request.IndexRange), Is.EqualTo(s_cappedRanges));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public async Task ReadBytesToAsyncNonPositiveSessionCapDoesNotShrinkRequestAsync(int cap)
        {
            m_context.MaxByteStringLength = cap;
            m_values.Enqueue(Value(31));
            m_values.Enqueue(Value());
            using var destination = new MemoryStream();

            long count = await ReadAsync(destination, 1, 4).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 31 }));
            Assert.That(m_requests.Select(r => r.Request.IndexRange), Is.EqualTo(s_uncappedRanges));
        }

        [TestCase("string")]
        [TestCase("integer")]
        public async Task ReadBytesToAsyncRejectsWrongValueType(string kind)
        {
            m_values.Enqueue(new DataValue(kind == "string" ? Variant.From("not bytes") : Variant.From(17)));
            using var destination = new MemoryStream();

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(destination, 8, 4).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_requests, Has.Count.EqualTo(1));
        }

        [TestCaseSource(nameof(s_badValueStatuses))]
        public async Task ReadBytesToAsyncPreservesBadValueStatusAndPreviouslyWrittenBytes(StatusCode status)
        {
            m_values.Enqueue(Value(41, 42));
            m_values.Enqueue(DataValue.FromStatusCode(status));
            using var destination = new MemoryStream();

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(destination, 8, 4).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(status));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 41, 42 }));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_requests, Has.Count.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadBytesToAsyncRejectsMalformedResponse(bool diagnostics)
        {
            m_session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = diagnostics ? new[] { Value(1) }.ToArrayOf() : [],
                    DiagnosticInfos = diagnostics
                        ? new[] { new DiagnosticInfo(), new DiagnosticInfo() }.ToArrayOf() : []
                }));
            using var destination = new MemoryStream();

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(destination, 8, 4).ConfigureAwait(false)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            VerifyReads(Times.Once());
        }

        [Test]
        public async Task ReadBytesToAsyncPropagatesCancellationAndRetainsDestination()
        {
            using var destination = new MemoryStream();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var primary = new OperationCanceledException(cancellation.Token);
            m_session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), cancellation.Token))
                .Throws(primary);

            OperationCanceledException exception = (await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await m_session.Object.ReadBytesToAsync(
                    new NodeId("content", 3), destination, 8, 4, cancellation.Token).ConfigureAwait(false)))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            VerifyReads(Times.Once());
        }

        [Test]
        public async Task ReadBytesToAsyncPreservesServiceExceptionAndRetainsDestination()
        {
            var primary = new ServiceResultException(StatusCodes.BadSessionClosed);
            using var destination = new MemoryStream();
            m_session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Throws(primary);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(destination, 8, 4).ConfigureAwait(false)))!;

            Assert.That(exception, Is.SameAs(primary));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            VerifyReads(Times.Once());
        }

        [TestCase(0L, 4, "maxBytes")]
        [TestCase(-1L, 4, "maxBytes")]
        [TestCase(2147483648L, 4, "maxBytes")]
        [TestCase(8L, 0, "chunkSize")]
        [TestCase(8L, -1, "chunkSize")]
        [TestCase(8L, 1048577, "chunkSize")]
        public async Task ReadBytesToAsyncValidatesBoundsBeforeRead(long maxBytes, int chunkSize, string parameter)
        {
            using var destination = new MemoryStream();

            ArgumentOutOfRangeException exception = (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await ReadAsync(destination, maxBytes, chunkSize).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo(parameter));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            VerifyReads(Times.Never());
        }

        [TestCase(1L, 1)]
        [TestCase(2147483647L, 1048576)]
        public async Task ReadBytesToAsyncAcceptsLimitBoundariesAsync(long maxBytes, int chunkSize)
        {
            m_context.MaxByteStringLength = 0;
            m_values.Enqueue(Value(51));
            m_values.Enqueue(Value());
            using var destination = new MemoryStream();

            long count = await ReadAsync(destination, maxBytes, chunkSize).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 51 }));
            Assert.That(m_requests[0].Request.IndexRange, Is.EqualTo(chunkSize == 1 ? "0:0" : "0:1048575"));
        }

        [Test]
        public async Task ReadBytesToAsyncRejectsReadOnlyDestinationWithoutRead()
        {
            using var destination = new MemoryStream([1, 2], writable: false);

            ArgumentException exception = (await Assert.ThrowsAsync<ArgumentException>(
                async () => await ReadAsync(destination, 8, 4).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo("destination"));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(destination.CanRead, Is.True);
            VerifyReads(Times.Never());
        }

        [Test]
        public async Task ReadBytesToAsyncRejectsNullDestinationWithoutRead()
        {
            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await ReadAsync(null!, 8, 4).ConfigureAwait(false)))!;
            Assert.That(exception.ParamName, Is.EqualTo("destination"));
            VerifyReads(Times.Never());
        }

        [Test]
        public async Task ReadBytesToAsyncRejectsNullSession()
        {
            using var destination = new MemoryStream();
            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await SessionByteStreamExtensions.ReadBytesToAsync(
                    null!, NodeId.Null, destination, 8).ConfigureAwait(false)))!;
            Assert.That(exception.ParamName, Is.EqualTo("session"));
            Assert.That(destination.Length, Is.Zero);
        }

        private Task<long> ReadAsync(Stream destination, long maxBytes, int chunkSize)
        {
            return m_session.Object.ReadBytesToAsync(
                new NodeId("content", 3), destination, maxBytes, chunkSize).AsTask();
        }

        private void VerifyReads(Times times)
        {
            m_session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), times);
        }

        private static DataValue Value(params byte[] bytes)
        {
            return new DataValue(Variant.From(ByteString.From(bytes)));
        }

        private static readonly StatusCode[] s_badValueStatuses =
        [
            StatusCodes.BadUserAccessDenied, StatusCodes.BadNodeIdUnknown
        ];
        private static readonly string[] s_shortReadRanges = ["0:3", "2:5", "3:6", "5:8"];
        private static readonly string[] s_singleByteRange = ["0:0"];
        private static readonly string[] s_cumulativeRanges = ["0:3", "3:6"];
        private static readonly string[] s_initialRange = ["0:3"];
        private static readonly string[] s_cappedRanges = ["0:1", "2:3", "3:4"];
        private static readonly string[] s_uncappedRanges = ["0:3", "1:4"];
        private ServiceMessageContext m_context = null!;
        private Mock<ISessionClient> m_session = null!;
        private readonly Queue<DataValue> m_values = new();
        private readonly List<(ReadValueId Request, CancellationToken Token)> m_requests = [];
    }
}
