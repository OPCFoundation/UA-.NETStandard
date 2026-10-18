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
                    TcpMessageType.Message, TcpMessageType.Message, TcpMessageType.Flow
                ]));
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
                TcpMessageType.Message, TcpMessageType.Message, TcpMessageType.Flow
            ]));
            Assert.That(fixture.Transport.Sequences, Is.EqualTo(new uint[] { 100, 101, 102 }));
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
            FlowRecord control = FlowControlCodec.Encode(
                new FlowProbe(FlowOperation.Ping, 0, 42), 0, 900, new FlowCodecLimits());
            ByteString payload = FlowCarrierCodec.EncodeUascPayload(
                FlowCarrier.SharedUasc, 0, in control, new FlowCodecLimits(), out uint requestId);
            var controlWrite = new DataChannelWrite(
                DataChannelPath.Management, 0, 0, requestId, payload, false, 0, () => DataChannelResult.Good);
            pending.Add(fixture.Channel.SendDataChannelWriteAsync(controlWrite, CancellationToken.None).AsTask());
            Assert.That(controlWrite.IsCommitted, Is.False);
            Assert.That(dataWrite.IsCommitted, Is.False);

            fixture.Transport.Release.TrySetResult(true);
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            uint[] types = fixture.Transport.MessageTypes;
            int firstFlow = Array.IndexOf(types, TcpMessageType.Flow);
            Assert.Multiple(() =>
            {
                Assert.That(types, Has.Length.EqualTo(27));
                Assert.That(firstFlow, Is.InRange(1, 9),
                    "At most eight newly selected Services may precede a queued control after the in-flight write.");
                Assert.That(fixture.Transport.Chunks[firstFlow][32], Is.EqualTo((byte)FlowRecordKind.Control));
                Assert.That(types[^1], Is.EqualTo(TcpMessageType.Flow));
                Assert.That(fixture.Transport.Chunks[^1][32], Is.EqualTo((byte)FlowRecordKind.Data));
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
                        TcpMessageType.Message, TcpMessageType.Flow, TcpMessageType.Message
                    ]));
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
            int maximumRecord = fixture.Channel.GetMaxDataChannelRecordSize(DataChannelCarrierKind.SharedUasc);
            DataChannelWrite oversized = DataWrite(maximumRecord - 13 + 1);
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

        private static DataChannelWrite DataWrite(int payloadLength = 4, Func<DataChannelResult>? commit = null)
        {
            var record = new FlowRecord(
                FlowRecordKind.Data, 1, 1, new ByteString(new byte[payloadLength]));
            ByteString encoded = FlowCarrierCodec.EncodeUascPayload(
                FlowCarrier.SharedUasc, 1, in record, new FlowCodecLimits(), out uint requestId);
            return new DataChannelWrite(DataChannelPath.Flow, 1, 0, requestId, encoded, true, 0,
                commit ?? (() => DataChannelResult.Good));
        }

        private sealed class WriterFixture : IAsyncDisposable
        {
            internal WriterFixture()
            {
                m_telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.None));
                Pool = new TrackingPool();
                Transport = new RecordingTransport();
                Channel = new WriterChannel(m_telemetry, Pool, Transport);
                Channel.SeedSequence(100);
                Channel.Activate(7);
                ByteString local = new(Enumerable.Repeat((byte)0x31, 32).ToArray());
                ByteString remote = new(Enumerable.Repeat((byte)0x72, 32).ToArray());
                Context = Channel.AttachDataChannels(
                    DataChannelCarrierKind.SharedUasc,
                    new DataChannelPeerIdentity("urn:writer:local", "urn:writer:peer",
                        local, remote, DataChannelProtectionMode.Integrity, true),
                    true, new TestFlowAuthorizer(), new DataChannelOptions { AllowIntegrity = true },
                    new DataChannelApplicationBudget(), m_telemetry);
            }

            internal WriterChannel Channel { get; }
            internal RecordingTransport Transport { get; }
            internal TrackingPool Pool { get; }
            internal DataChannelContext Context { get; }

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
            internal WriterChannel(ITelemetryContext telemetry, ArrayPool<byte> pool, IUaSCByteTransport transport)
                : base("flow-writer-test",
                    new BufferManager("flow-writer-test", TcpMessageLimits.DefaultMaxBufferSize, telemetry, pool),
                    new ChannelQuotas(ServiceMessageContext.CreateEmpty(telemetry)),
                    serverCertificates: null, endpoints: null,
                    securityMode: MessageSecurityMode.Sign, securityPolicyUri: SecurityPolicies.Basic256Sha256,
                    telemetry: telemetry)
            {
                Transport = transport;
                CalculateSymmetricKeySizes();
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

            internal void SignalTransportClosed()
            {
                NotifyChannelClosed();
            }

            internal async Task ServiceAsync()
            {
                var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (await Gate.EnterAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    EncodedSymmetricMessage message = EncodeSymmetricMessage(
                        TcpMessageType.Message, 21, new ArraySegment<byte>([]), true, out bool exceeded);
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
                Interlocked.Increment(ref m_rented);
                byte[] bytes = new byte[minimumLength];
                m_owned.TryAdd(bytes, 0);
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
                if (!CanCoalesceChunks)
                {
                    m_chunks.Enqueue(copy);
                    return;
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
