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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Machinery.Client;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryResultTransferTests
    {
        [TestCaseSource(nameof(InvalidGenerationOutputs))]
        public async Task InvalidGenerationOutputClosesOnlyAnActuallyReturnedHandle(
            ArrayOf<Variant> outputs,
            bool acquiredHandle)
        {
            m_transfer.GenerateResponse = TransferHarness.Response(outputs);
            using var destination = new MemoryStream();

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(m_transfer.GenerateCount, Is.EqualTo(1));
            Assert.That(m_transfer.ReadLengths, Is.Empty);
            Assert.That(destination.Length, Is.Zero);
            Assert.That(m_transfer.CloseCount, Is.EqualTo(acquiredHandle ? 1 : 0));
            Assert.That(m_transfer.OwnsHandle, Is.False);
            if (acquiredHandle)
            {
                AssertSuccessfulCleanup();
            }
        }

        [Test]
        public async Task OpaqueUInt32ZeroHandleIsClosedExactlyAsReturnedAsync()
        {
            m_transfer.Handle = 0;
            using var destination = new MemoryStream();

            long copied = await m_transfer.Client.DownloadResultAsync(
                TransferHarness.MachineId, "selected", destination, 0);

            Assert.That(copied, Is.Zero);
            Assert.That(m_transfer.Handle, Is.Zero);
            AssertSuccessfulCleanup();
        }

        [TestCaseSource(nameof(NonGoodCallStatuses))]
        public async Task NonGoodServiceOrMethodStatusIsRejected(
            string phase,
            StatusCode status,
            bool serviceStatus,
            string handleKind)
        {
            CallResponse response = TransferHarness.Response(
                phase == "generate"
                    ? [Variant.From(TransferHarness.FileId), Variant.From(37u), Variant.From(NodeId.Null)]
                    : phase == "read" ? [Variant.From(ByteString.Empty)] : []);
            if (phase == "generate" && handleKind != "valid")
            {
                response.Results[0].OutputArguments = handleKind == "missing"
                    ? [Variant.From(TransferHarness.FileId)]
                    : [Variant.From(TransferHarness.FileId), Variant.From(37ul), Variant.From(NodeId.Null)];
            }
            if (serviceStatus)
            {
                response.ResponseHeader.ServiceResult = status;
            }
            else
            {
                response.Results[0].StatusCode = status;
            }
            if (phase == "generate")
            {
                m_transfer.GenerateResponse = response;
            }
            else if (phase == "read")
            {
                m_transfer.Read = (_, _) => new ValueTask<CallResponse>(response);
            }
            else
            {
                m_transfer.Close = _ => new ValueTask<CallResponse>(response);
            }
            using var destination = new MemoryStream();

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8)))!;

            Assert.That(error.StatusCode, Is.EqualTo(status));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(m_transfer.GenerateCount, Is.EqualTo(1));
            bool acquiredHandle = phase != "generate" || handleKind == "valid";
            Assert.That(m_transfer.CloseCount, Is.EqualTo(acquiredHandle ? 1 : 0));
            Assert.That(m_transfer.ReadLengths, Has.Count.EqualTo(phase == "generate" ? 0 : 1));
            Assert.That(m_transfer.OwnsHandle, Is.EqualTo(phase == "close"));
            m_transfer.Session.Verify(session => session.Dispose(), Times.Never);
            if (acquiredHandle && phase != "close")
            {
                AssertSuccessfulCleanup();
            }
        }

        [Test]
        public async Task FailedGenerateIsNotReplayedOrGivenAnInventedClose()
        {
            m_transfer.GenerateResponse = TransferHarness.Response(
                [Variant.From(TransferHarness.FileId), Variant.From(37u), Variant.From(NodeId.Null)]);
            m_transfer.GenerateResponse.Results[0].StatusCode = StatusCodes.BadMethodInvalid;
            using var destination = new MemoryStream();

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
            Assert.That(m_transfer.Calls, Has.Count.EqualTo(2));
            Assert.That(m_transfer.Paths, Has.Count.EqualTo(2));
            Assert.That(m_transfer.ReadLengths, Is.Empty);
            Assert.That(destination.Length, Is.Zero);
            AssertSuccessfulCleanup();
        }

        [TestCase("missing")]
        [TestCase("type")]
        [TestCase("extra")]
        [TestCase("null-response")]
        [TestCase("result-count")]
        public async Task InvalidReadResponseIsRejectedAndClosed(string kind)
        {
            CallResponse? response = kind switch
            {
                "missing" => TransferHarness.Response([]),
                "type" => TransferHarness.Response([Variant.From("not bytes")]),
                "extra" => TransferHarness.Response([Variant.From(ByteString.Empty), Variant.From(1)]),
                _ => TransferHarness.Response([Variant.From(ByteString.Empty)])
            };
            if (kind == "result-count")
            {
                response.Results = [];
            }
            if (kind == "null-response")
            {
                response = null;
            }
            m_transfer.Read = (_, _) => new ValueTask<CallResponse>(response!);
            using var destination = new MemoryStream();

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8));

            Assert.That(destination.Length, Is.Zero);
            Assert.That(m_transfer.ReadLengths, Has.Count.EqualTo(1));
            AssertSuccessfulCleanup();
        }

        [TestCase("generate")]
        [TestCase("read")]
        public async Task InvalidInputArgumentStatusRetainsTheAcquiredHandleForCleanup(string phase)
        {
            CallResponse response = TransferHarness.Response(
                phase == "generate"
                    ? [Variant.From(TransferHarness.FileId), Variant.From(37u), Variant.From(NodeId.Null)]
                    : [Variant.From(ByteString.Empty)]);
            response.Results[0].InputArgumentResults = phase == "generate"
                ? [StatusCodes.Uncertain]
                : [StatusCodes.Good, StatusCodes.Uncertain];
            if (phase == "generate")
            {
                m_transfer.GenerateResponse = response;
            }
            else
            {
                m_transfer.Read = (_, _) => new ValueTask<CallResponse>(response);
            }
            using var destination = new MemoryStream();

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(destination.Length, Is.Zero);
            AssertSuccessfulCleanup();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidAuxiliaryGenerationMetadataStillClosesTheReturnedHandle(bool invalidDiagnostics)
        {
            m_transfer.GenerateResponse = TransferHarness.Response(
                [Variant.From(TransferHarness.FileId), Variant.From(37u), Variant.From(NodeId.Null)]);
            if (invalidDiagnostics)
            {
                m_transfer.GenerateResponse.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
            }
            else
            {
                m_transfer.GenerateResponse.Results[0].InputArgumentResults = [StatusCodes.Good, StatusCodes.Good];
            }
            using var destination = new MemoryStream();

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8));

            Assert.That(m_transfer.ReadLengths, Is.Empty);
            Assert.That(destination.Length, Is.Zero);
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task ReadFailureIsRethrownAfterSuccessfulClose()
        {
            var failure = new IOException("read failed");
            m_transfer.Read = (_, _) => throw failure;
            using var destination = new MemoryStream();

            IOException error = (await Assert.ThrowsAsync<IOException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8)))!;

            Assert.That(error, Is.SameAs(failure));
            Assert.That(destination.Length, Is.Zero);
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task ReadAndCloseFailuresRemainVisibleInTheirOriginalOrder()
        {
            var readFailure = new IOException("read failed");
            var closeFailure = new ServiceResultException(StatusCodes.BadCommunicationError);
            m_transfer.Read = (_, _) => throw readFailure;
            m_transfer.Close = _ => throw closeFailure;
            using var destination = new MemoryStream();

            AggregateException error = (await Assert.ThrowsAsync<AggregateException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8)))!;

            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.SameAs(readFailure));
            Assert.That(error.InnerExceptions[1], Is.SameAs(closeFailure));
            Assert.That(m_transfer.ReadLengths, Has.Count.EqualTo(1));
            Assert.That(m_transfer.CloseCount, Is.EqualTo(1));
            Assert.That(m_transfer.CloseWasCancelledOnEntry, Is.False);
            m_transfer.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [Test]
        public async Task CloseFailureAloneIsNotSwallowedOrWrapped()
        {
            m_transfer.Payload = MakePayload(3);
            var closeFailure = new IOException("close failed");
            m_transfer.Close = _ => throw closeFailure;
            using var destination = new MemoryStream();

            IOException error = (await Assert.ThrowsAsync<IOException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 3)))!;

            Assert.That(error, Is.SameAs(closeFailure));
            Assert.That(destination.ToArray(), Is.EqualTo(m_transfer.Payload.ToArray()));
            Assert.That(m_transfer.CloseCount, Is.EqualTo(1));
            Assert.That(destination.CanWrite, Is.True);
        }

        [Test]
        public async Task UnexpectedCloseOutputIsNotReportedAsSuccess()
        {
            m_transfer.Close = _ => new ValueTask<CallResponse>(TransferHarness.Response([Variant.From(1)]));
            using var destination = new MemoryStream();

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 0)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(m_transfer.CloseCount, Is.EqualTo(1));
            Assert.That(m_transfer.OwnsHandle, Is.False);
        }

        [Test]
        public async Task DestinationWriteFailureStillClosesTheRemoteHandle()
        {
            m_transfer.Payload = MakePayload(3);
            var writeFailure = new IOException("destination write failed");
            var destination = new Mock<Stream>(MockBehavior.Strict);
            destination.SetupGet(stream => stream.CanWrite).Returns(true);
#if NET
            destination.Setup(stream => stream.WriteAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>())).Throws(writeFailure);
#else
            destination.Setup(stream => stream.WriteAsync(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>())).Throws(writeFailure);
#endif

            IOException error = (await Assert.ThrowsAsync<IOException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination.Object, 3)))!;

            Assert.That(error, Is.SameAs(writeFailure));
            Assert.That(m_transfer.ReadLengths, Has.Count.EqualTo(1));
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task ReadCancellationStillClosesWithAFreshUncancelledToken()
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new OperationCanceledException(cancellation.Token);
            m_transfer.Read = (_, _) =>
            {
                cancellation.Cancel();
                throw failure;
            };
            using var destination = new MemoryStream();

            OperationCanceledException error = (await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 3, cancellation.Token)))!;

            Assert.That(error, Is.SameAs(failure));
            Assert.That(m_transfer.CloseToken, Is.Not.EqualTo(cancellation.Token));
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task HandleReturnedAfterCancellationIsStillClosed()
        {
            using var cancellation = new CancellationTokenSource();
            m_transfer.Generated = cancellation.Cancel;
            using var destination = new MemoryStream();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 3, cancellation.Token));

            Assert.That(m_transfer.ReadLengths, Is.Empty);
            Assert.That(m_transfer.CloseToken, Is.Not.EqualTo(cancellation.Token));
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task CancellationAndCloseFailureAreBothPreserved()
        {
            using var cancellation = new CancellationTokenSource();
            var cancelled = new OperationCanceledException(cancellation.Token);
            var closeFailure = new IOException("close failed");
            m_transfer.Read = (_, _) =>
            {
                cancellation.Cancel();
                throw cancelled;
            };
            m_transfer.Close = _ => throw closeFailure;
            using var destination = new MemoryStream();

            AggregateException error = (await Assert.ThrowsAsync<AggregateException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 3, cancellation.Token)))!;

            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.SameAs(cancelled));
            Assert.That(error.InnerExceptions[1], Is.SameAs(closeFailure));
            Assert.That(m_transfer.CloseToken, Is.Not.EqualTo(cancellation.Token));
            Assert.That(m_transfer.CloseWasCancelledOnEntry, Is.False);
            Assert.That(m_transfer.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RebindingTheCallerDoesNotRedirectCleanupToAnotherSessionAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var enteredRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingRead = new TaskCompletionSource<CallResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_transfer.Read = (_, _) =>
            {
                enteredRead.TrySetResult(true);
                return new ValueTask<CallResponse>(pendingRead.Task);
            };
            MachineryClient current = m_transfer.Client;
            using var destination = new MemoryStream();
            Task<long> download = current.DownloadResultAsync(
                TransferHarness.MachineId, "selected", destination, 3, cancellation.Token).AsTask();
            await enteredRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(m_transfer.OwnsHandle, Is.True);

            var replacement = new TransferHarness();
            current = replacement.Client;
            cancellation.Cancel();
            pendingRead.TrySetException(new OperationCanceledException(cancellation.Token));
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await download);

            Assert.That(current, Is.SameAs(replacement.Client));
            Assert.That(replacement.Calls, Is.Empty);
            Assert.That(replacement.Paths, Is.Empty);
            replacement.Session.Verify(session => session.Dispose(), Times.Never);
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task InPlaceSessionReplacementNeverClosesAHandleOnTheReplacementSession()
        {
            m_transfer.Read = (_, _) =>
            {
                m_transfer.SessionId = new NodeId("replacement", 1);
                return new ValueTask<CallResponse>(TransferHarness.Response([Variant.From(MakePayload(1))]));
            };
            using var destination = new MemoryStream();

            AggregateException error = (await Assert.ThrowsAsync<AggregateException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 3)))!;

            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(error.InnerExceptions[1], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(m_transfer.CloseCount, Is.Zero);
            Assert.That(destination.Length, Is.Zero);
            m_transfer.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [Test]
        public async Task UnresponsiveCloseHasAFiniteDeadlineAndRetainsTheReadFailureAsync()
        {
            var pendingClose = new TaskCompletionSource<CallResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var readFailure = new IOException("read failed");
            m_transfer.Read = (_, _) => throw readFailure;
            m_transfer.Close = _ => new ValueTask<CallResponse>(pendingClose.Task);
            using var destination = new MemoryStream();
            try
            {
                Task<long> transfer = m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 3).AsTask();
                AggregateException error = (await Assert.ThrowsAsync<AggregateException>(
                    async () => await transfer.WaitAsync(TimeSpan.FromSeconds(15))))!;

                Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(error.InnerExceptions[0], Is.SameAs(readFailure));
                Assert.That(error.InnerExceptions[1], Is.InstanceOf<OperationCanceledException>());
                Assert.That(m_transfer.CloseToken.IsCancellationRequested, Is.True);
                Assert.That(m_transfer.CloseWasCancelledOnEntry, Is.False);
                Assert.That(m_transfer.CloseCount, Is.EqualTo(1));
            }
            finally
            {
                pendingClose.TrySetResult(TransferHarness.Response([]));
                await pendingClose.Task;
            }
        }

        private static IEnumerable<TestCaseData> InvalidGenerationOutputs()
        {
            Variant file = Variant.From(TransferHarness.FileId);
            Variant handle = Variant.From(37u);
            Variant completion = Variant.From(NodeId.Null);
            yield return new TestCaseData(ArrayOf<Variant>.Empty, false);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { file }), false);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { Variant.From("file"), handle, completion }), false);
            yield return new TestCaseData(
                ArrayOf.Wrapped(new[] { Variant.From(NodeId.Null), handle, completion }),
                false);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { file, Variant.Null, completion }), false);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { file, Variant.From(-1), completion }), false);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { file, Variant.From(37ul), completion }), false);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { file, handle }), true);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { file, handle, Variant.From("state") }), true);
            yield return new TestCaseData(ArrayOf.Wrapped(new[] { file, handle, completion, Variant.From(1) }), true);
        }

        private static IEnumerable<TestCaseData> NonGoodCallStatuses()
        {
            foreach (string phase in new[] { "generate", "read", "close" })
            {
                string[] handleKinds = phase == "generate" ? ["valid", "missing", "type"] : ["valid"];
                foreach (StatusCode status in new StatusCode[]
                {
                    StatusCodes.Uncertain,
                    StatusCodes.BadCommunicationError
                })
                {
                    foreach (string handleKind in handleKinds)
                    {
                        yield return new TestCaseData(phase, status, true, handleKind);
                        yield return new TestCaseData(phase, status, false, handleKind);
                    }
                }
            }
        }
    }
}
