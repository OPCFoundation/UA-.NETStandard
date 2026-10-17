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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Exercises the bounded result-transfer lifecycle against controlled
    /// service responses, including ownership and streaming limits.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed partial class MachineryResultTransferTests
    {
        [SetUp]
        public void SetUp()
        {
            m_transfer = new TransferHarness();
        }

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(7, 8)]
        [TestCase(8, 8)]
        [TestCase(65536, 65536)]
        [TestCase(65537, 65537)]
        public async Task PayloadWithinLimitIsCopiedAndClosedAsync(int payloadSize, long limit)
        {
            m_transfer.Payload = MakePayload(payloadSize);
            using var destination = new MemoryStream();

            long copied = await m_transfer.Client.DownloadResultAsync(
                TransferHarness.MachineId,
                "selected",
                destination,
                limit);

            Assert.That(copied, Is.EqualTo(payloadSize));
            Assert.That(destination.ToArray(), Is.EqualTo(m_transfer.Payload.ToArray()));
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(m_transfer.ReadLengths[^1], Is.EqualTo(limit - payloadSize + 1));
            Assert.That(m_transfer.ReadLengths, Has.All.LessThanOrEqualTo(65536));
            AssertSuccessfulCleanup();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(65536)]
        public void FirstByteOverLimitStopsBeforeWritingTheOffendingChunk(int limit)
        {
            m_transfer.Payload = MakePayload(limit + 2);
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    "selected",
                    destination,
                    limit))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(m_transfer.Position, Is.EqualTo(limit + 1));
            Assert.That(m_transfer.ReadLengths, Has.Count.EqualTo(limit / 65536 + 1));
            Assert.That(destination.Length, Is.EqualTo(limit >= 65536 ? 65536 : 0));
            Assert.That(m_transfer.ReadLengths[^1], Is.EqualTo(limit >= 65536 ? 1 : limit + 1));
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task ShortReadsContinueUntilAnEmptyByteStringAsync()
        {
            m_transfer.Payload = MakePayload(7);
            m_transfer.MaxReturnedBytes = 2;
            using var destination = new MemoryStream();

            long copied = await m_transfer.Client.DownloadResultAsync(
                TransferHarness.MachineId,
                "selected",
                destination,
                7);

            Assert.That(copied, Is.EqualTo(7));
            Assert.That(destination.ToArray(), Is.EqualTo(m_transfer.Payload.ToArray()));
            Assert.That(m_transfer.ReadLengths, Is.EqualTo(s_shortReadLengths));
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task MaximumLongLimitDoesNotOverflowReadLengthAsync()
        {
            m_transfer.Payload = MakePayload(1);
            using var destination = new MemoryStream();

            long copied = await m_transfer.Client.DownloadResultAsync(
                TransferHarness.MachineId,
                "selected",
                destination,
                long.MaxValue);

            Assert.That(copied, Is.EqualTo(1));
            Assert.That(m_transfer.ReadLengths, Is.EqualTo(s_maximumLimitReadLengths));
            AssertSuccessfulCleanup();
        }

        [Test]
        public void ShortReadRequestBudgetIsFinite()
        {
            m_transfer.Payload = MakePayload(2048);
            m_transfer.MaxReturnedBytes = 1;
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    "selected",
                    destination,
                    4096))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(m_transfer.ReadLengths, Has.Count.EqualTo(1024));
            Assert.That(m_transfer.Position, Is.EqualTo(1024));
            Assert.That(destination.Length, Is.EqualTo(1024));
            AssertSuccessfulCleanup();
        }

        [TestCase(3, 3)]
        [TestCase(int.MaxValue, 65536)]
        public async Task FileTransferFactoryHonorsTheSmallerChunkLimitAsync(int configuredSize, int expectedRead)
        {
            m_transfer.Payload = MakePayload(expectedRead);
            var factory = new FileTransferClientFactory(m_transfer.Context.Telemetry);
            TemporaryFileTransferClient client = factory.CreateTemporaryFileTransfer(
                m_transfer.Session.Object,
                TransferHarness.TransferId,
                new FileSystemClientOptions { ChunkSize = configuredSize });
            using var destination = new MemoryStream();

            long copied = await client.CopyToAsync(
                Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "selected" }),
                destination,
                expectedRead);

            Assert.That(copied, Is.EqualTo(expectedRead));
            Assert.That(m_transfer.ReadLengths, Is.EqualTo(new[] { expectedRead, 1 }));
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task OnlyTheExactSelectedResultAndTransferPathAreUsedAsync()
        {
            const string selected = " result/42?variant=original ";
            m_transfer.Payload = MakePayload(2);
            using var destination = new MemoryStream();
            MachineryClient client = m_transfer.Session.Object.Machinery(m_transfer.Context.Telemetry);

            await client.DownloadResultAsync(TransferHarness.MachineId, selected, destination, 2);

            Assert.That(m_transfer.SelectedResultId, Is.EqualTo(selected));
            Assert.That(m_transfer.Paths, Has.Count.EqualTo(3));
            BrowsePath path = m_transfer.Paths[0];
            Assert.That(path.StartingNode, Is.EqualTo(TransferHarness.MachineId));
            Assert.That(path.RelativePath.Elements, Has.Count.EqualTo(2));
            Assert.That(path.RelativePath.Elements[0].TargetName.Name, Is.EqualTo("ResultManagement"));
            Assert.That(path.RelativePath.Elements[1].TargetName.Name, Is.EqualTo("ResultTransfer"));
            Assert.That(path.RelativePath.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(2));
            Assert.That(path.RelativePath.Elements[1].TargetName.NamespaceIndex, Is.EqualTo(2));
            Assert.That(m_transfer.Calls[0].ObjectId, Is.EqualTo(TransferHarness.TransferId));
            Assert.That(m_transfer.Calls[0].MethodId, Is.EqualTo(TransferHarness.GenerateMethodId));
            Assert.That(m_transfer.AttributeReads, Is.Zero, "File Size and URI attributes must not drive copying.");
            AssertSuccessfulCleanup();
        }

        [Test]
        public void OversizedReturnedChunkIsRejectedWithoutCopyingOrAnotherRead()
        {
            m_transfer.Read = (_, _) => new ValueTask<CallResponse>(
                TransferHarness.Response([Variant.From(MakePayload(100))]));
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    "selected",
                    destination,
                    8))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(m_transfer.ReadLengths, Is.EqualTo(s_oversizedReadLengths));
            Assert.That(destination.Length, Is.Zero);
            AssertSuccessfulCleanup();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void BlankResultIdIsRejectedBeforeAnyService(string? resultId)
        {
            using var destination = new MemoryStream();
            ArgumentException error = Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    resultId!,
                    destination,
                    1))!;

            Assert.That(error.ParamName, Is.EqualTo("resultId"));
            AssertNoRequests();
        }

        [TestCase(-1)]
        [TestCase(long.MinValue)]
        public void NegativeLimitIsRejectedBeforeAnyService(long limit)
        {
            using var destination = new MemoryStream();
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    "selected",
                    destination,
                    limit))!;

            Assert.That(error.ParamName, Is.EqualTo("maxBytes"));
            AssertNoRequests();
        }

        [Test]
        public void NullMachineIsRejectedBeforeAnyService()
        {
            using var destination = new MemoryStream();
            ArgumentException error = Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_transfer.Client.DownloadResultAsync(NodeId.Null, "selected", destination, 1))!;

            Assert.That(error.ParamName, Is.EqualTo("machine"));
            AssertNoRequests();
        }

        [Test]
        public void NullDestinationIsRejectedBeforeAnyService()
        {
            ArgumentNullException error = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    "selected",
                    null!,
                    1))!;

            Assert.That(error.ParamName, Is.EqualTo("destination"));
            AssertNoRequests();
        }

        [Test]
        public void NonWritableDestinationIsRejectedBeforeAnyService()
        {
            using var destination = new MemoryStream(Array.Empty<byte>(), writable: false);
            ArgumentException error = Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    "selected",
                    destination,
                    1))!;

            Assert.That(error.ParamName, Is.EqualTo("destination"));
            AssertNoRequests();
        }

        [Test]
        public void PreCancelledTransferDoesNotAcquireAHandle()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var destination = new MemoryStream();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId,
                    "selected",
                    destination,
                    1,
                    cancellation.Token));

            AssertNoRequests();
        }

        [TestCase("null")]
        [TestCase("read-only")]
        [TestCase("negative")]
        [TestCase("cancelled")]
        public void DirectFileTransferRejectsInvalidArgumentsWithoutAcquiringAHandle(string kind)
        {
            var client = new TemporaryFileTransferClient(m_transfer.Session.Object, TransferHarness.TransferId);
            using var destination = new MemoryStream(Array.Empty<byte>(), writable: kind != "read-only");
            using var cancellation = new CancellationTokenSource();
            if (kind == "cancelled")
            {
                cancellation.Cancel();
            }
            async Task InvokeAsync()
            {
                await client.CopyToAsync(
                    Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "selected" }),
                    kind == "null" ? null! : destination,
                    kind == "negative" ? -1 : 0,
                    cancellation.Token);
            }

            switch (kind)
            {
                case "null":
                    Assert.That(
                        InvokeAsync,
                        Throws.ArgumentNullException.With.Property(nameof(ArgumentException.ParamName))
                            .EqualTo("destination"));
                    break;
                case "read-only":
                    Assert.That(
                        InvokeAsync,
                        Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName))
                            .EqualTo("destination"));
                    break;
                case "negative":
                    Assert.That(InvokeAsync, Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Property(nameof(ArgumentException.ParamName)).EqualTo("maxBytes"));
                    break;
                case "cancelled":
                    Assert.That(InvokeAsync, Throws.TypeOf<OperationCanceledException>());
                    break;
            }
            AssertNoRequests();
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void MutatedInvalidChunkSizeIsRejectedBeforeGeneration(int chunkSize)
        {
            var client = new TemporaryFileTransferClient(m_transfer.Session.Object, TransferHarness.TransferId);
            client.Options.ChunkSize = chunkSize;
            using var destination = new MemoryStream();

            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await client.CopyToAsync(Variant.Null, destination, 1))!;

            Assert.That(error.ParamName, Is.EqualTo(nameof(FileSystemClientOptions.ChunkSize)));
            AssertNoRequests();
        }

        [Test]
        public async Task ChunkSizeIsSnapshottedBeforeGenerationAsync()
        {
            var client = new TemporaryFileTransferClient(
                m_transfer.Session.Object,
                TransferHarness.TransferId,
                new FileSystemClientOptions { ChunkSize = 3 });
            m_transfer.Payload = MakePayload(4);
            m_transfer.Generated = () => client.Options.ChunkSize = -1;
            using var destination = new MemoryStream();

            long copied = await client.CopyToAsync(
                Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "selected" }),
                destination,
                4);

            Assert.That(copied, Is.EqualTo(4));
            Assert.That(destination.ToArray(), Is.EqualTo(m_transfer.Payload.ToArray()));
            Assert.That(m_transfer.ReadLengths, Is.EqualTo(s_snapshottedReadLengths));
            AssertSuccessfulCleanup();
        }

        private void AssertNoRequests()
        {
            Assert.That(m_transfer.Paths, Is.Empty);
            Assert.That(m_transfer.Calls, Is.Empty);
            Assert.That(m_transfer.OwnsHandle, Is.False);
            m_transfer.Session.Verify(session => session.Dispose(), Times.Never);
        }

        private void AssertSuccessfulCleanup()
        {
            Assert.That(m_transfer.GenerateCount, Is.EqualTo(1));
            Assert.That(m_transfer.CloseCount, Is.EqualTo(1));
            Assert.That(m_transfer.OwnsHandle, Is.False);
            Assert.That(m_transfer.CloseToken.CanBeCanceled, Is.True);
            Assert.That(m_transfer.CloseWasCancelledOnEntry, Is.False);
            Assert.That(m_transfer.Calls[^1].ObjectId, Is.EqualTo(TransferHarness.FileId));
            Assert.That(m_transfer.Calls[^1].MethodId, Is.EqualTo(MethodIds.FileType_Close));
            Assert.That(m_transfer.Calls[^1].InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(m_transfer.Handle));
            Assert.That(m_transfer.Paths, Has.None.Matches<BrowsePath>(static path =>
                path.StartingNode == TransferHarness.FileId &&
                path.RelativePath.Elements[0].TargetName.Name == "Close"));
            m_transfer.Session.Verify(session => session.Dispose(), Times.Never);
        }

        private static ByteString MakePayload(int size)
        {
            var bytes = new byte[size];
            for (int ii = 0; ii < size; ii++)
            {
                bytes[ii] = (byte)(ii % 251);
            }
            return new ByteString(bytes);
        }

        private TransferHarness m_transfer = null!;
        private static readonly int[] s_shortReadLengths = [8, 6, 4, 2, 1];
        private static readonly int[] s_maximumLimitReadLengths = [65536, 65536];
        private static readonly int[] s_oversizedReadLengths = [9];
        private static readonly int[] s_snapshottedReadLengths = [3, 2, 1];
    }
}
