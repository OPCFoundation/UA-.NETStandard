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
 *
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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [Category("BufferManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class TcpServerChannelBufferTests
    {
        [Test]
        public async Task QueuedResponsesReturnAllBuffersAfterSendsCompleteAsync()
        {
            const int responseCount = 16;
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(responseCount);
            channel.SetTransport(transport);

            for (uint requestId = 1; requestId <= responseCount; requestId++)
            {
                channel.SendResponse(requestId, CreateResponse());
            }

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.RentCount, Is.EqualTo(responseCount));
            Assert.That(pool.OutstandingCount, Is.EqualTo(responseCount));
            Assert.That(pool.LastMinimumLength, Is.EqualTo(64 * 1024));

            transport.Complete();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(responseCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task FailedResponseSendReturnsBufferAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(expectedSendCount: 1);
            channel.SetTransport(transport);

            channel.SendResponse(1, CreateResponse());

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));

            transport.Fail(new InvalidOperationException("Injected send failure."));

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task MultiChunkResponseReturnsAllBuffersAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(expectedSendCount: 1);
            channel.SetTransport(transport);
            ReadResponse response = CreateResponse();
            response.Results =
            [
                new DataValue(
                    new Variant(new ByteString(new byte[256 * 1024])),
                    StatusCodes.Good)
            ];

            channel.SendResponse(1, response);

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.RentCount, Is.GreaterThan(1));
            Assert.That(pool.OutstandingCount, Is.EqualTo(pool.RentCount));

            transport.Complete();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task ChannelCloseWhileSendsAreBlockedReturnsAllBuffersAsync()
        {
            const int responseCount = 16;
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(responseCount);
            channel.SetTransport(transport);

            for (uint requestId = 1; requestId <= responseCount; requestId++)
            {
                channel.SendResponse(requestId, CreateResponse());
            }

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.OutstandingCount, Is.EqualTo(responseCount));

            channel.CloseForTest();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task DisposeWhileSendsAreBlockedReturnsAllBuffersAsync()
        {
            const int responseCount = 16;
            var pool = new TrackingArrayPool();
            TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(responseCount);
            channel.SetTransport(transport);

            try
            {
                for (uint requestId = 1; requestId <= responseCount; requestId++)
                {
                    channel.SendResponse(requestId, CreateResponse());
                }

                Assert.That(
                    await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                    Is.True);
                Assert.That(pool.OutstandingCount, Is.EqualTo(responseCount));

                channel.Dispose();

                Assert.That(
                    await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                    Is.True);
                Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
                Assert.That(pool.DuplicateReturnCount, Is.Zero);
            }
            finally
            {
                channel.Dispose();
            }
        }

        [Test]
        public async Task CancelledResponseSendReturnsBufferAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(expectedSendCount: 1);
            channel.SetTransport(transport);

            channel.SendResponse(1, CreateResponse());

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));

            transport.Cancel();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task TransportCloseWhileResponseSendIsBlockedReturnsBufferAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(expectedSendCount: 1);
            channel.SetTransport(transport);

            channel.SendResponse(1, CreateResponse());

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));

            transport.Close();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task MessageLimitAbortResponseReturnsAllBuffersAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.SetMaxResponseMessageSizeForTest(1);
            var transport = new GateByteTransport(expectedSendCount: 1, captureSentChunks: true);
            channel.SetTransport(transport);

            channel.SendResponse(1, CreateLargeResponse(256 * 1024));

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.RentCount, Is.GreaterThan(1));
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            byte[] sentChunk = transport.LastSentChunk;
            Assert.That(sentChunk, Is.Not.Null);
            Assert.That(
                TcpMessageType.IsAbort(GetMessageType(sentChunk)),
                Is.True);
            Assert.That(
                GetAbortStatusCode(sentChunk),
                Is.EqualTo((uint)StatusCodes.BadResponseTooLarge));

            transport.Complete();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [TestCase(65535, 65536)]
        [TestCase(65536, 65536)]
        public async Task NegotiatedMaxBufferSizeUsesBucketSafeRentalSizeAsync(
            int negotiatedMaxBufferSize,
            int expectedRequestedMinimumLength)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool, negotiatedMaxBufferSize);
            var transport = new GateByteTransport(expectedSendCount: 1);
            channel.SetTransport(transport);

            channel.SendResponse(1, CreateResponse());

            Assert.That(
                await CompletesWithinAsync(transport.AllSendsStarted, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.LastMinimumLength, Is.EqualTo(expectedRequestedMinimumLength));
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));

            transport.Complete();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        private static TestServerChannel CreateOpenChannel(
            TrackingArrayPool pool,
            int maxBufferSize = 64 * 1024)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
            var quotas = new ChannelQuotas(context)
            {
                MaxBufferSize = maxBufferSize,
                MaxMessageSize = 4 * 1024 * 1024
            };
            var manager = new BufferManager(
                nameof(TcpServerChannelBufferTests),
                quotas.MaxBufferSize,
                telemetry,
                pool);
            var listener = new Mock<ITcpChannelListener>();
            listener.SetupGet(value => value.EndpointUrl)
                .Returns(new Uri("opc.tcp://localhost:4840"));
            var channel = new TestServerChannel(
                listener.Object,
                manager,
                quotas,
                telemetry,
                new FakeTimeProvider());
            channel.OpenForTest();
            return channel;
        }

        private static ReadResponse CreateResponse()
        {
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good
                },
                Results = []
            };
        }

        private static ReadResponse CreateLargeResponse(int payloadSize)
        {
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good
                },
                Results =
                [
                    new DataValue(
                        new Variant(new ByteString(new byte[payloadSize])),
                        StatusCodes.Good)
                ]
            };
        }

        private static uint GetMessageType(byte[] buffer)
        {
            return BitConverter.ToUInt32(buffer, 0);
        }

        private static uint GetAbortStatusCode(byte[] buffer)
        {
            return BitConverter.ToUInt32(
                buffer,
                TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize);
        }

        private static async Task<bool> CompletesWithinAsync(Task task, int seconds)
        {
            Task completed = await Task
                .WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)))
                .ConfigureAwait(false);
            return ReferenceEquals(completed, task);
        }

        private static async Task<bool> WaitForOutstandingCountAsync(
            TrackingArrayPool pool,
            int expected,
            int seconds)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                if (pool.OutstandingCount == expected)
                {
                    return true;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            return pool.OutstandingCount == expected;
        }

        private sealed class TestServerChannel : TcpServerChannel
        {
            public TestServerChannel(
                ITcpChannelListener listener,
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(
                    nameof(TcpServerChannelBufferTests),
                    listener,
                    bufferManager,
                    quotas,
                    null!,
                    [],
                    telemetry,
                    timeProvider)
            {
            }

            public void OpenForTest()
            {
                State = TcpChannelState.Open;
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(
                    new ChannelToken
                    {
                        ChannelId = 1,
                        TokenId = 1,
                        SecurityPolicy = SecurityPolicyInfo.None,
                        CreatedAt = DateTime.UtcNow,
                        CreatedAtTimestamp = TimeProvider.GetTimestamp(),
                        Lifetime = 60000
                    },
                    previous: null);
            }

            public void SetTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            public void CloseForTest()
            {
                ChannelClosed();
            }

            public void SetMaxResponseMessageSizeForTest(int maxResponseMessageSize)
            {
                MaxResponseMessageSize = maxResponseMessageSize;
            }
        }

        private sealed class GateByteTransport : IUaSCByteTransport
        {
            public GateByteTransport(int expectedSendCount, bool captureSentChunks = false)
            {
                m_expectedSendCount = expectedSendCount;
                m_captureSentChunks = captureSentChunks;
            }

            public EndPoint LocalEndpoint => null;

            public EndPoint RemoteEndpoint => null;

            public TransportChannelFeatures Features => default;

            public string Implementation => "UA-GATE";

            public Task AllSendsStarted => m_allSendsStarted.Task;

            public byte[] LastSentChunk
            {
                get
                {
                    lock (m_lock)
                    {
                        if (m_sentChunks.Count == 0)
                        {
                            return null;
                        }
                        return m_sentChunks[^1];
                    }
                }
            }

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                CaptureChunk(chunk);
                return WaitForCompletionAsync();
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                CaptureChunk(buffers);
                return WaitForCompletionAsync();
            }

            public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                await m_closed.Task.ConfigureAwait(false);
                throw new ServiceResultException(StatusCodes.BadConnectionClosed);
            }

            public void Close()
            {
                m_closed.TrySetResult(true);
                m_completion.TrySetResult(true);
            }

            public void Complete()
            {
                m_completion.TrySetResult(true);
            }

            public void Fail(Exception exception)
            {
                m_completion.TrySetException(exception);
            }

            public void Cancel()
            {
                m_completion.TrySetCanceled();
            }

            public void Dispose()
            {
                Close();
            }

            private void CaptureChunk(ReadOnlyMemory<byte> chunk)
            {
                if (!m_captureSentChunks)
                {
                    return;
                }

                lock (m_lock)
                {
                    m_sentChunks.Add(chunk.ToArray());
                }
            }

            private void CaptureChunk(BufferCollection buffers)
            {
                if (!m_captureSentChunks)
                {
                    return;
                }

                byte[] copy = new byte[buffers.TotalSize];
                int offset = 0;

                foreach (ArraySegment<byte> buffer in buffers)
                {
                    Buffer.BlockCopy(
                        buffer.GetArray(),
                        buffer.Offset,
                        copy,
                        offset,
                        buffer.Count);
                    offset += buffer.Count;
                }

                lock (m_lock)
                {
                    m_sentChunks.Add(copy);
                }
            }

            private async ValueTask WaitForCompletionAsync()
            {
                if (Interlocked.Increment(ref m_sendCount) == m_expectedSendCount)
                {
                    m_allSendsStarted.TrySetResult(true);
                }
                await m_completion.Task.ConfigureAwait(false);
            }

            private readonly int m_expectedSendCount;
            private readonly bool m_captureSentChunks;
            private readonly Lock m_lock = new();
            private readonly List<byte[]> m_sentChunks = [];

            private readonly TaskCompletionSource<bool> m_allSendsStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_closed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_sendCount;
        }

        private sealed class TrackingArrayPool : ArrayPool<byte>
        {
            public override byte[] Rent(int minimumLength)
            {
                byte[] buffer = new byte[minimumLength];
                lock (m_lock)
                {
                    RentCount++;
                    LastMinimumLength = minimumLength;
                    m_outstanding.Add(buffer);
                }
                return buffer;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                lock (m_lock)
                {
                    ReturnCount++;
                    if (!m_outstanding.Remove(array))
                    {
                        DuplicateReturnCount++;
                    }
                }
            }

            public int RentCount { get; private set; }

            public int ReturnCount { get; private set; }

            public int LastMinimumLength { get; private set; }

            public int OutstandingCount
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_outstanding.Count;
                    }
                }
            }

            public int DuplicateReturnCount { get; private set; }

            private readonly Lock m_lock = new();
            private readonly HashSet<byte[]> m_outstanding = [];
        }
    }
}
