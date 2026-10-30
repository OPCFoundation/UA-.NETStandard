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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Preserves actual UASC writer, cryptographic commitment and pooled-buffer regressions.
    /// The transport is in-process; live authenticated sockets are covered by transport integration tests.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class UascWriterTests
    {
        [Test]
        public async Task AlreadyQueuedServicePrecedesUnsealedFlowRecordWithOneSequenceSpace()
        {
            await using var fixture = new WriterFixture();
            fixture.Transport.BlockCall = 1;
            Task first = fixture.Channel.ServiceAsync();
            await fixture.Transport.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task service = fixture.Channel.ServiceAsync();
            DataChannelWrite write = DataWrite();
            Task<DataChannelResult> data = fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .AsTask();
            Assert.That(write.IsCommitted, Is.False);
            fixture.Transport.Release.TrySetResult(true);
            await Task.WhenAll(first, service, data).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100, 101, 102 }));
                Assert.That(fixture.Transport.MessageTypes, Is.EqualTo(
                [
                    TcpMessageType.Message, TcpMessageType.Message, TcpMessageType.Message
                ]));
                Assert.That(fixture.Transport.RequestIds, Is.EqualTo(new uint[] { 21, 21, 0x80000001u }));
                Assert.That(fixture.Channel.SequenceNumbersIssuedUnderCurrentToken, Is.EqualTo(3));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            });
        }

        [Test]
        public async Task ServiceHasPrecedenceOverDataThatHasNotYetBeenSecured()
        {
            await using var fixture = new WriterFixture();
            fixture.Transport.BlockCall = 1;
            Task first = fixture.Channel.ServiceAsync();
            await fixture.Transport.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannelWrite write = DataWrite();
            Task<DataChannelResult> data = fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .AsTask();
            Task laterService = fixture.Channel.ServiceAsync();
            Assert.That(write.IsCommitted, Is.False);
            fixture.Transport.Release.TrySetResult(true);
            await Task.WhenAll(first, laterService, data).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(fixture.Transport.MessageTypes, Is.EqualTo(
            [
                TcpMessageType.Message, TcpMessageType.Message, TcpMessageType.Message
            ]));
            Assert.That(fixture.Transport.RequestIds, Is.EqualTo(new uint[] { 21, 21, 0x80000001u }));
            Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100, 101, 102 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ClosingRefusesUnsealedFlowWritesBeforeSequenceConsumption(bool control)
        {
            await using var fixture = new WriterFixture();
            fixture.Transport.BlockCall = 1;
            Task first = fixture.Channel.ServiceAsync();
            await fixture.Transport.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannelWrite write = control ? ControlWrite() : DataWrite();
            Task<DataChannelResult> flow = fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .AsTask();
            Assert.That(write.IsCommitted, Is.False);
            fixture.Channel.BeginClosing();
            Task close = fixture.Channel.ServiceAsync(TcpMessageType.Close);
            fixture.Transport.Release.TrySetResult(true);
            await Task.WhenAll(first, close, flow).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannelResult result = await flow.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(DataChannelResult.Closed));
                Assert.That(write.IsCommitted, Is.False);
                Assert.That(fixture.Transport.MessageTypes, Is.EqualTo(
                [
                    TcpMessageType.Message, TcpMessageType.Close
                ]));
                Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100, 101 }));
                Assert.That(fixture.Channel.SequenceNumbersIssuedUnderCurrentToken, Is.EqualTo(2));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            });
        }

        /// <summary>
        /// Queued controls make bounded progress before security assignment while DATA remains below Services.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LifecycleControlMakesBoundedProgressBehindQueuedServices(bool coalesce)
        {
            await using var fixture = new WriterFixture();
            fixture.Transport.CanCoalesceChunks = coalesce;
            fixture.Transport.BlockCall = 1;
            var pending = new List<Task> { fixture.Channel.ServiceAsync() };
            await fixture.Transport.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannelWrite dataWrite = DataWrite();
            pending.Add(fixture.Channel.SendDataChannelWriteAsync(dataWrite, CancellationToken.None).AsTask());
            for (int index = 0; index < 24; index++)
            {
                pending.Add(fixture.Channel.ServiceAsync());
            }
            DataChannelWrite controlWrite = ControlWrite();
            pending.Add(fixture.Channel.SendDataChannelWriteAsync(controlWrite, CancellationToken.None).AsTask());
            Assert.That(controlWrite.IsCommitted, Is.False);
            Assert.That(dataWrite.IsCommitted, Is.False);

            fixture.Transport.Release.TrySetResult(true);
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            uint[] types = fixture.Transport.MessageTypes;
            byte[][] chunks = fixture.Transport.Chunks;
            int bodyOffset = TcpMessageLimits.MessageTypeAndSize +
                TcpMessageLimits.SymmetricHeaderSize;
            int firstFlowControl = Array.FindIndex(chunks, chunk => (BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask) ==
                    TcpMessageType.Message &&
                    chunk.Length >= bodyOffset + UascFlowMessageCodec.ControlPrefixSize + 1 &&
                    chunk[bodyOffset] == 0 && chunk[bodyOffset + 1] == 255 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(bodyOffset + 2)) == 0 &&
                    chunk[bodyOffset + UascFlowMessageCodec.ControlPrefixSize] == (byte)FlowOperation.Ping);
            Assert.Multiple(() =>
            {
                Assert.That(types, Has.Length.EqualTo(27));
                Assert.That(firstFlowControl, Is.InRange(1, 9),
                    "At most eight newly selected Services may precede a queued control after the in-flight write.");
                Assert.That(types.All(type => type == TcpMessageType.Message), Is.True);
                Assert.That(fixture.Transport.RequestIds[^1], Is.EqualTo(0x80000001u));
                Assert.That(chunks[^1].AsSpan(bodyOffset, 6).ToArray(),
                    Is.EqualTo(new byte[] { 0, 254, 0, 0, 0, 0 }));
                Assert.That(fixture.Transport.Sequences, Is.EqualTo(Enumerable.Range(100, 27).Select(value => (uint)value)));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            });
        }

        [Test]
        public async Task CancelledCommittedWaitPreservesPooledBuffersAndSecuredMessageOrder()
        {
            await using var fixture = new WriterFixture();
            fixture.Transport.BlockCall = 2;
            await fixture.Channel.ServiceAsync().ConfigureAwait(false);
            DataChannelWrite write = DataWrite();
            Task<DataChannelResult> writer = fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .AsTask();
            await fixture.Transport.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(write.IsCommitted, Is.True);
            var cancellation = new CancellationTokenSource();
            CancellationToken callerCancellation = cancellation.Token;
            Task<DataChannelResult> wait = writer.WaitAsync(callerCancellation);
            Task following = Task.CompletedTask;
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                try
                {
                    await wait.ConfigureAwait(false);
                    Assert.Fail("The caller's wait must observe cancellation.");
                }
                catch (OperationCanceledException)
                {
                    Assert.That(wait.IsCanceled, Is.True);
                }
                following = fixture.Channel.ServiceAsync();
                Assert.That(fixture.Pool.OutstandingCount, Is.EqualTo(2),
                    "Only the secured flow chunk and following service chunk remain writer-owned.");
                fixture.Transport.Release.TrySetResult(true);
                await Task.WhenAll(writer, following).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100, 101, 102 }));
                    Assert.That(fixture.Transport.MessageTypes, Is.EqualTo(
                    [
                        TcpMessageType.Message, TcpMessageType.Message, TcpMessageType.Message
                    ]));
                    Assert.That(fixture.Transport.RequestIds, Is.EqualTo(new uint[] { 21, 0x80000001u, 21 }));
                    Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                    Assert.That(fixture.Transport.Chunks.All(chunk => chunk[3] == (byte)'F'), Is.True);
                });
            }
            finally
            {
                fixture.Transport.Release.TrySetResult(true);
                try
                {
                    try
                    {
                        await wait.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Its cancellation has already been asserted; still join every lifetime on assertion failure.
                    }
                    await Task.WhenAll(writer, following).ConfigureAwait(false);
                }
                finally
                {
                    cancellation.Dispose();
                }
            }
        }

        [Test]
        public async Task PreSecurityRefusalDoesNotNumberOrPoisonFollowingServiceWrites()
        {
            await using var fixture = new WriterFixture();
            fixture.Transport.BlockCall = 1;
            Task first = fixture.Channel.ServiceAsync();
            await fixture.Transport.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannelWrite expiring = DataWrite(commit: () => DataChannelResult.Timeout);
            Task<DataChannelResult> refused = fixture.Channel.SendDataChannelWriteAsync(
                expiring, CancellationToken.None).AsTask();
            Task following = fixture.Channel.ServiceAsync();
            fixture.Transport.Release.TrySetResult(true);
            await Task.WhenAll(first, following).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(await refused.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Timeout));
            Assert.Multiple(() =>
            {
                Assert.That(expiring.IsCommitted, Is.False);
                Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100, 101 }));
                Assert.That(fixture.Transport.MessageTypes, Is.EqualTo(
                [
                    TcpMessageType.Message, TcpMessageType.Message
                ]));
                Assert.That(fixture.Channel.SequenceNumbersIssuedUnderCurrentToken, Is.EqualTo(2));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            });
        }

        [Test]
        public async Task OversizeIsRefusedBeforePoolRentOrSequenceConsumption()
        {
            await using var fixture = new WriterFixture();
            fixture.Channel.SetMessageLimits(4096, 2);
            int maximumRecord = fixture.Channel.GetMaxDataChannelRecordSize(DataChannelCarrierKind.SharedUasc);
            DataChannelWrite oversized = DataWrite(maximumRecord - 1);
            Assert.That(await fixture.Channel.SendDataChannelWriteAsync(oversized, CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(fixture.Channel.SequenceNumbersIssuedUnderCurrentToken, Is.Zero);
            Assert.That(fixture.Pool.RentedCount, Is.Zero);
            Assert.That(await fixture.Channel.SendDataChannelWriteAsync(DataWrite(), CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100 }));
        }

        [Test]
        public async Task SequenceBudgetStallsBeforeCommitThenRenewsWithoutReusingNumber()
        {
            await using var fixture = new WriterFixture();
            fixture.Channel.SequenceBudget.ObserveConsumed(uint.MaxValue);
            DataChannelWrite write = DataWrite();
            Task<DataChannelResult> pending = fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .AsTask();
            Assert.Multiple(() =>
            {
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(write.IsCommitted, Is.False);
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            });
            fixture.Channel.Activate(8);
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100 }));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(fixture.Transport.Chunks.Single().AsSpan(12)),
                Is.EqualTo(8));
        }

        [Test]
        public async Task ClosingASequenceStalledContextWakesItsWriterWithoutSpinning()
        {
            await using var fixture = new WriterFixture();
            using var cancellation = new CancellationTokenSource();
            fixture.Channel.SequenceBudget.ObserveConsumed(uint.MaxValue);
            Task<DataChannelResult> pending = fixture.Channel.SendDataChannelWriteAsync(DataWrite(), cancellation.Token)
                .AsTask();
            try
            {
                fixture.Channel.SignalTransportClosed();
                Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Closed));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                try
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Stop a defective pre-fix wait without leaking its worker into subsequent tests.
                }
            }
        }

        /// <summary>
        /// A selected multipart MSG retains the writer until its final chunk before the next Service.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MultipartMsgRemainsContiguousAndNumbersEveryChunk(bool coalesce)
        {
            await using var fixture = new WriterFixture();
            fixture.Transport.CanCoalesceChunks = coalesce;
            fixture.Transport.BlockCall = 1;
            DataChannelWrite write = DataWrite(200000);
            Task<DataChannelResult> data = fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .AsTask();
            await fixture.Transport.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task service = fixture.Channel.ServiceAsync();
            fixture.Transport.Release.TrySetResult(true);
            await Task.WhenAll(data, service).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            byte[][] chunks = fixture.Transport.Chunks;
            int dataChunks = chunks.Length - 1;
            int bodyOffset = TcpMessageLimits.MessageTypeAndSize + TcpMessageLimits.SymmetricHeaderSize;
            int signatureSize = 32;
            var body = new List<byte>();
            for (int index = 0; index < dataChunks; index++)
            {
                Assert.That(chunks[index][3], Is.EqualTo(index == dataChunks - 1 ? (byte)'F' : (byte)'C'));
                body.AddRange(chunks[index].AsSpan(bodyOffset,
                    chunks[index].Length - bodyOffset - signatureSize).ToArray());
            }
            Assert.Multiple(() =>
            {
                Assert.That(dataChunks, Is.GreaterThan(1));
                Assert.That(data.Result, Is.EqualTo(DataChannelResult.Good));
                Assert.That(fixture.Transport.RequestIds,
                    Is.EqualTo(Enumerable.Repeat(0x80000001u, dataChunks).Append(21u)));
                Assert.That(fixture.Transport.Sequences,
                    Is.EqualTo(Enumerable.Range(100, chunks.Length).Select(value => (uint)value)));
                Assert.That(fixture.Channel.SequenceBudget.Remaining,
                    Is.EqualTo(uint.MaxValue - chunks.Length));
                Assert.That(body.ToArray(), Is.EqualTo(write.Payload.ToArray()));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            });
        }

        /// <summary>
        /// No prefix of a multipart MSG commits when only part of its sequence space remains.
        /// </summary>
        [Test]
        public async Task MultipartMsgWaitsForEnoughSequenceSpaceBeforeCommit()
        {
            await using var fixture = new WriterFixture();
            fixture.Channel.SequenceBudget.ObserveConsumed(uint.MaxValue - 1L);
            DataChannelWrite write = DataWrite(200000);
            Task<DataChannelResult> pending = fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .AsTask();
            await FlowLoopback.EventuallyAsync(() =>
                fixture.Pool.RentedCount > 0 && fixture.Pool.OutstandingCount == 0).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(write.IsCommitted, Is.False);
                Assert.That(fixture.Transport.Chunks, Is.Empty);
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Channel.SequenceBudget.Remaining, Is.EqualTo(1));
            });
            fixture.Channel.Activate(8);
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(fixture.Transport.Chunks.All(
                chunk => BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(12)) == 8), Is.True);
        }

        /// <summary>
        /// A valid abort releases pooled partial chunks and shared capacity without admitting application traffic.
        /// </summary>
        [Test]
        public async Task VerifiedMultipartAbortRestoresCapacityWithoutClosingTheContext()
        {
            await using var fixture = new WriterFixture();
            Assert.That(fixture.Channel.ReceiveVerified([0, 255], (byte)'C'), Is.True);
            Assert.That(fixture.Channel.ReceiveVerified([0, 0, 0], (byte)'C'), Is.True);
            Assert.That(fixture.Pool.OutstandingCount, Is.EqualTo(2));
            Assert.That(fixture.Budget.Usage.BufferedBytes, Is.GreaterThan(0));
            byte[] abort = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(abort, StatusCodes.BadRequestCancelledByClient.Code);
            BinaryPrimitives.WriteInt32LittleEndian(abort.AsSpan(4), -1);
            Assert.That(fixture.Channel.ReceiveVerified(abort, (byte)'A'), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
                Assert.That(fixture.Transport.Chunks, Is.Empty);
            });
            using (IDisposable? reservation = fixture.Context.TryReserveUascReassembly(1, false))
            {
                Assert.That(reservation, Is.Not.Null, "The same context still admits a new message after abort.");
            }
            fixture.Channel.SignalTransportClosed();
            Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
        }

        /// <summary>
        /// The first chunk must contain the complete canonical TypeId, and DATA requires HELLO.
        /// </summary>
        [TestCase("")]
        [TestCase("00")]
        [TestCase("01fe")]
        [TestCase("0001")]
        [TestCase("00fe")]
        public async Task VerifiedFirstChunkRejectsInvalidOrPrematureClassification(string hex)
        {
            await using var fixture = new WriterFixture();
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                fixture.Channel.ReceiveVerified(ByteString.FromHexString(hex).ToArray(), (byte)'C'))!;
            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
            });
        }

        /// <summary>
        /// Neither a Service nor another RequestId may interrupt an incomplete channel MSG.
        /// </summary>
        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public async Task VerifiedPartialMsgRejectsInterleavedServiceAndCloseReleasesItsBuffers(uint requestId)
        {
            await using var fixture = new WriterFixture();
            fixture.Channel.ReceiveVerified([0, 255], (byte)'C');
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                fixture.Channel.ReceiveVerified([1, 0, 1, 0], (byte)'F', requestId))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(fixture.Pool.OutstandingCount, Is.EqualTo(1));
            fixture.Channel.SignalTransportClosed();
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
            });
        }

        /// <summary>
        /// Renewal, shutdown and other outer message types cannot be interleaved into channel reassembly.
        /// </summary>
        [TestCase(TcpMessageType.Open)]
        [TestCase(TcpMessageType.Close)]
        [TestCase(TcpMessageType.Hello)]
        public async Task VerifiedPartialMsgRejectsAnotherOuterMessageType(uint messageType)
        {
            await using var fixture = new WriterFixture();
            fixture.Channel.ReceiveVerified([0, 255], (byte)'C');
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                fixture.Channel.ReceiveVerified([0, 0], (byte)'F', messageType: messageType))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            fixture.Channel.SignalTransportClosed();
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
            });
        }

        /// <summary>
        /// MaxControlBytes excludes the ten-byte prefix, while every retained byte remains budgeted.
        /// </summary>
        [Test]
        public async Task VerifiedControlBoundIncludesExactlyItsSeparatePrefix()
        {
            await using var fixture = new WriterFixture(new DataChannelOptions
            {
                AllowIntegrity = true,
                MaxControlBytes = 32
            });
            byte[] body = new byte[32 + UascFlowMessageCodec.ControlPrefixSize];
            body[1] = 255;
            fixture.Channel.ReceiveVerified(body, (byte)'C');
            Assert.That(fixture.Pool.OutstandingCount, Is.EqualTo(1));
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                fixture.Channel.ReceiveVerified([0], (byte)'C'))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            fixture.Channel.SignalTransportClosed();
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
            });
        }

        /// <summary>
        /// The exact single-chunk body bound includes TypeId bytes and the selected policy's padding.
        /// </summary>
        [TestCase(MessageSecurityMode.Sign, -1)]
        [TestCase(MessageSecurityMode.Sign, 0)]
        [TestCase(MessageSecurityMode.Sign, 1)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, -1)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, 0)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, 1)]
        public async Task MsgCapacityUsesTheActualSecurityFooter(MessageSecurityMode securityMode, int delta)
        {
            await using var fixture = new WriterFixture(securityMode: securityMode);
            fixture.Channel.SetMessageLimits(1000000, 1);
            int maximum = fixture.Channel.GetMaxDataChannelRecordSize(DataChannelCarrierKind.SharedUasc);
            DataChannelWrite write = DataWrite(maximum - UascFlowMessageCodec.TypeIdSize + delta);
            DataChannelResult result = await fixture.Channel.SendDataChannelWriteAsync(write, CancellationToken.None)
                .ConfigureAwait(false);
            if (delta > 0)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(DataChannelResult.TooLarge));
                    Assert.That(write.IsCommitted, Is.False);
                    Assert.That(fixture.Pool.RentedCount, Is.Zero);
                    Assert.That(fixture.Channel.SequenceNumbersIssuedUnderCurrentToken, Is.Zero);
                });
                return;
            }
            byte[] chunk = fixture.Transport.Chunks.Single();
            ByteString decoded = fixture.Channel.ReadProtectedBody((byte[])chunk.Clone());
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(DataChannelResult.Good));
                Assert.That(chunk, Has.Length.LessThanOrEqualTo(TcpMessageLimits.DefaultMaxBufferSize));
                Assert.That(chunk[3], Is.EqualTo((byte)'F'));
                Assert.That(decoded, Is.EqualTo(write.Payload));
                Assert.That(decoded, Has.Length.EqualTo(maximum + delta));
                Assert.That(fixture.Channel.SequenceNumbersIssuedUnderCurrentToken, Is.EqualTo(1));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
            });
        }

        /// <summary>
        /// Abort reasons are bounded by UTF-8 bytes and must exactly consume their declared body.
        /// </summary>
        [TestCase(0, true)]
        [TestCase(4095, true)]
        [TestCase(4096, true)]
        [TestCase(4097, false)]
        public async Task VerifiedAbortReasonHonorsItsExactByteBoundary(int length, bool accepted)
        {
            await using var fixture = new WriterFixture();
            fixture.Channel.ReceiveVerified([0, 255], (byte)'C');
            byte[] abort = new byte[8 + length];
            BinaryPrimitives.WriteUInt32LittleEndian(abort, StatusCodes.BadRequestCancelledByClient.Code);
            BinaryPrimitives.WriteInt32LittleEndian(abort.AsSpan(4), length);
            abort.AsSpan(8).Fill((byte)'a');
            if (accepted)
            {
                Assert.That(fixture.Channel.ReceiveVerified(abort, (byte)'A'), Is.True);
            }
            else
            {
                ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                    fixture.Channel.ReceiveVerified(abort, (byte)'A'))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            }
            fixture.Channel.SignalTransportClosed();
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
                Assert.That(fixture.Transport.Chunks, Is.Empty);
            });
        }

        /// <summary>
        /// Invalid abort statuses, lengths, UTF-8 and repeated aborts cannot release another message's resources.
        /// </summary>
        [TestCase("0000000000000000")]
        [TestCase("0000004000000000")]
        [TestCase("00000080feffffff")]
        [TestCase("0000008001000000ff")]
        [TestCase("000000800000000000")]
        [TestCase("0000008001000000")]
        [TestCase("00000080")]
        public async Task VerifiedAbortRejectsMalformedErrorBodies(string hex)
        {
            await using var fixture = new WriterFixture();
            fixture.Channel.ReceiveVerified([0, 255], (byte)'C');
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                fixture.Channel.ReceiveVerified(ByteString.FromHexString(hex).ToArray(), (byte)'A'))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            fixture.Channel.SignalTransportClosed();
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
            });
        }

        /// <summary>
        /// An abort cannot be used without a partial message or release its previous reservation twice.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task VerifiedAbortRequiresAnActivePartialMessage(bool previousAbort)
        {
            await using var fixture = new WriterFixture();
            byte[] abort = ByteString.FromHexString("00000080ffffffff").ToArray();
            if (previousAbort)
            {
                fixture.Channel.ReceiveVerified([0, 255], (byte)'C');
                fixture.Channel.ReceiveVerified(abort, (byte)'A');
            }
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                fixture.Channel.ReceiveVerified(abort, (byte)'A'))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                Assert.That(fixture.Pool.OutstandingCount, Is.Zero);
                Assert.That(fixture.Budget.Usage.BufferedBytes, Is.Zero);
            });
        }

        private static DataChannelWrite ControlWrite()
        {
            FlowRecord control = FlowControlCodec.Encode(
                new FlowProbe(FlowOperation.Ping, 0, 42), 0, 900, new FlowCodecLimits());
            ByteString payload = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 0, in control, new FlowCodecLimits(), out uint requestId);
            return new DataChannelWrite(
                DataChannelPath.Management, 0, 0, requestId, payload, false, 0, () => DataChannelResult.Good);
        }

        private static DataChannelWrite DataWrite(int payloadLength = 4, Func<DataChannelResult>? commit = null)
        {
            var record = new FlowRecord(
                FlowRecordKind.Data, 1, 1, new ByteString(new byte[payloadLength]));
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 1, in record, new FlowCodecLimits(), out uint requestId);
            return new DataChannelWrite(DataChannelPath.Flow, 1, 0, requestId, encoded, true, 0,
                commit ?? (() => DataChannelResult.Good));
        }

        private sealed class WriterFixture : IAsyncDisposable
        {
            internal WriterFixture(
                DataChannelOptions? options = null,
                MessageSecurityMode securityMode = MessageSecurityMode.Sign)
            {
                m_telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.None));
                Pool = new TrackingPool();
                Transport = new RecordingTransport();
                Channel = new WriterChannel(m_telemetry, Pool, Transport, securityMode);
                Channel.SeedSequence(100);
                Channel.Activate(7);
                ByteString local = new(Enumerable.Repeat((byte)0x31, 32).ToArray());
                ByteString remote = new(Enumerable.Repeat((byte)0x72, 32).ToArray());
                Context = Channel.AttachDataChannels(
                    DataChannelCarrierKind.SharedUasc,
                    new DataChannelPeerIdentity("urn:writer:local", "urn:writer:peer",
                        local, remote, securityMode == MessageSecurityMode.SignAndEncrypt
                            ? DataChannelProtectionMode.Confidential : DataChannelProtectionMode.Integrity, true),
                    true, new TestFlowAuthorizer(), options ?? new DataChannelOptions { AllowIntegrity = true },
                    Budget, m_telemetry);
            }

            internal WriterChannel Channel { get; }
            internal RecordingTransport Transport { get; }
            internal TrackingPool Pool { get; }
            internal DataChannelContext Context { get; }
            internal DataChannelApplicationBudget Budget { get; } = new();

            public async ValueTask DisposeAsync()
            {
                Transport.Release.TrySetResult(true);
                Channel.SignalTransportClosed();
                await Context.DisposeAsync().ConfigureAwait(false);
                Channel.Dispose();
                if (m_telemetry is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            private readonly ITelemetryContext m_telemetry;
        }

        private sealed class WriterChannel : UaSCUaBinaryChannel
        {
            internal WriterChannel(
                ITelemetryContext telemetry,
                ArrayPool<byte> pool,
                IUaSCByteTransport transport,
                MessageSecurityMode securityMode)
                : base("flow-writer-test",
                    new BufferManager("flow-writer-test", TcpMessageLimits.DefaultMaxBufferSize, telemetry, pool),
                    new ChannelQuotas(ServiceMessageContext.CreateEmpty(telemetry)),
                    serverCertificates: null, endpoints: null,
                    securityMode: securityMode, securityPolicyUri: SecurityPolicies.Basic256Sha256,
                    telemetry: telemetry)
            {
                Transport = transport;
                State = TcpChannelState.Open;
                CalculateSymmetricKeySizes();
            }

            internal void BeginClosing()
            {
                State = TcpChannelState.Closing;
            }

            internal void Activate(uint tokenId)
            {
                ChannelId = 19;
                ChannelToken token = CreateToken();
                token.TokenId = tokenId;
                token.ClientNonce = Random(32);
                token.ServerNonce = Random(32);
                token.Lifetime = 600000;
                ActivateToken(token);
            }

            internal void SeedSequence(uint first)
            {
                for (uint i = 1; i < first; i++)
                {
                    GetNewSequenceNumber();
                }
            }

            internal void SetMessageLimits(int maximumBytes, int maximumChunks)
            {
                MaxRequestMessageSize = maximumBytes;
                MaxRequestChunkCount = maximumChunks;
            }

            internal bool ReceiveVerified(
                byte[] body, byte finality, uint requestId = 0, uint messageType = TcpMessageType.Message)
            {
                byte[] buffer = BufferManager.TakeBuffer(
                    Math.Max(body.Length, 1), nameof(ReceiveVerified));
                body.CopyTo(buffer, 0);
                bool ownsBuffer = false;
                try
                {
                    return TryProcessVerifiedDataChannelMessage(
                        messageType | ((uint)finality << 24),
                        requestId, new ArraySegment<byte>(buffer, 0, body.Length), true, out ownsBuffer);
                }
                finally
                {
                    if (!ownsBuffer)
                    {
                        BufferManager.ReturnBuffer(buffer, nameof(ReceiveVerified));
                    }
                }
            }

            internal ByteString ReadProtectedBody(byte[] chunk)
            {
                ArraySegment<byte> body = ReadSymmetricMessage(
                    new ArraySegment<byte>(chunk), true, out _, out _, out uint sequence);
                if (!VerifySequenceNumber(sequence, nameof(ReadProtectedBody)))
                {
                    throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid);
                }
                return ByteString.From(body.AsSpan());
            }

            internal void SignalTransportClosed()
            {
                NotifyChannelClosed();
            }

            internal async Task ServiceAsync(uint messageType = TcpMessageType.Message)
            {
                var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (await Gate.EnterAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    EncodedSymmetricMessage message = EncodeSymmetricMessage(
                        messageType, 21, new ArraySegment<byte>([]), true, out bool exceeded);
                    if (exceeded || !BeginWriteSymmetricMessage(message, completed))
                    {
                        message.Chunks.Release(BufferManager, nameof(ServiceAsync));
                        throw new InvalidOperationException("The service write was not admitted.");
                    }
                }
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            protected override void HandleWriteComplete(
                BufferCollection? buffers, object? state, int bytesWritten, ServiceResult result)
            {
                base.HandleWriteComplete(buffers, state, bytesWritten, result);
                if (state is TaskCompletionSource<bool> completion)
                {
                    if (ServiceResult.IsBad(result))
                    {
                        completion.TrySetException(new ServiceResultException(result));
                    }
                    else
                    {
                        completion.TrySetResult(true);
                    }
                }
            }

            protected override ValueTask<bool> HandleIncomingMessageAsync(
                uint messageType, ArraySegment<byte> messageChunk, CancellationToken ct)
            {
                return new ValueTask<bool>(false);
            }

            private static byte[] Random(int length)
            {
                byte[] bytes = new byte[length];
                using var generator = RandomNumberGenerator.Create();
                generator.GetBytes(bytes);
                return bytes;
            }
        }

        private sealed class TrackingPool : ArrayPool<byte>
        {
            internal int OutstandingCount => m_owned.Count;
            internal int RentedCount => Volatile.Read(ref m_rented);

            public override byte[] Rent(int minimumLength)
            {
                byte[] bytes = new byte[minimumLength];
                m_owned.TryAdd(bytes, 0);
                Interlocked.Increment(ref m_rented);
                return bytes;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                if (!m_owned.TryRemove(array, out _))
                {
                    throw new InvalidOperationException("A writer buffer was returned twice.");
                }
                array.AsSpan().Fill(0xDD);
            }

            private readonly ConcurrentDictionary<byte[], byte> m_owned = new();
            private int m_rented;
        }

        private sealed class RecordingTransport : IUaSCByteTransport, IUaSCChunkCoalescing
        {
            public bool CanCoalesceChunks { get; set; }
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public string Implementation => "flow-runtime-writer-test";
            internal int BlockCall { get; set; }

            internal TaskCompletionSource<bool> Blocked { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal byte[][] Chunks => [.. m_chunks];

            internal uint[] Sequences => [.. m_chunks.Select(bytes =>
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)))];

            internal uint[] MessageTypes => [.. m_chunks.Select(bytes =>
                BinaryPrimitives.ReadUInt32LittleEndian(bytes) & TcpMessageType.MessageTypeMask)];

            internal uint[] RequestIds => [.. m_chunks.Select(bytes =>
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20)))];

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                await BeforeSendAsync(ct).ConfigureAwait(false);
                m_chunks.Enqueue(chunk.ToArray());
            }

            public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                await BeforeSendAsync(ct).ConfigureAwait(false);
                byte[] copy = new byte[buffers.Sum(buffer => buffer.Count)];
                int offset = 0;
                foreach (ArraySegment<byte> buffer in buffers)
                {
                    buffer.AsSpan().CopyTo(copy.AsSpan(offset));
                    offset += buffer.Count;
                }
                offset = 0;
                while (offset < copy.Length)
                {
                    int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(offset + 4)));
                    m_chunks.Enqueue(copy.AsSpan(offset, length).ToArray());
                    offset += length;
                }
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public void Close()
            {
                Release.TrySetResult(true);
            }

            private async Task BeforeSendAsync(CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref m_calls) == BlockCall)
                {
                    Blocked.TrySetResult(true);
                    await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            private readonly ConcurrentQueue<byte[]> m_chunks = new();
            private int m_calls;
        }
    }
}
