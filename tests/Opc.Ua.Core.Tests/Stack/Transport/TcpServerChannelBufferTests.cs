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
                await CompletesWithinAsync(transport.FirstSendStarted, 30).ConfigureAwait(false),
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
                await CompletesWithinAsync(transport.FirstSendStarted, 30).ConfigureAwait(false),
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
                    await CompletesWithinAsync(transport.FirstSendStarted, 30).ConfigureAwait(false),
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

        [TestCase(65535)]
        [TestCase(65536)]
        public async Task NegotiatedMaxBufferSizeUsesBucketSafeRentalSizeAsync(
            int negotiatedMaxBufferSize)
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
#if DEBUG
            const int expectedRequestedMinimumLength = 64 * 1024;
#else
            int expectedRequestedMinimumLength = negotiatedMaxBufferSize;
#endif
            Assert.That(pool.LastMinimumLength, Is.EqualTo(expectedRequestedMinimumLength));
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));

            transport.Complete();

            Assert.That(
                await WaitForOutstandingCountAsync(pool, expected: 0, 30).ConfigureAwait(false),
                Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task ChannelRejectsChunkAboveNegotiatedReceiveSizeAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.SetReceiveBufferSizeForTest(16);
            byte[] buffer = channel.TakeBufferForTest(17);
            BitConverter.GetBytes(TcpMessageType.Message).CopyTo(buffer, 0);
            BitConverter.GetBytes(17).CopyTo(buffer, 4);

            await channel.FeedReceivedChunkAsync(new ArraySegment<byte>(buffer, 0, 17))
                .ConfigureAwait(false);

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(
                channel.LastTransportError.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadTcpMessageTooLarge));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
        }

        [Test]
        public void UnstoredIntermediateChunkReturnsItsRental()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            byte[] buffer = channel.TakeBufferForTest(32);
            channel.SaveReceivedPartForTest(0, new ArraySegment<byte>(buffer, 0, 32));

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public void ExceededIntermediateMessageLimitReturnsBothOldAndIncomingRentals()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.SetMaxRequestMessageSizeForTest(1);
            byte[] first = channel.TakeBufferForTest(32);
            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(first, 0, 2));
            byte[] second = channel.TakeBufferForTest(32);
            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(second, 0, 1));

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public void TakingSavedChunksWithoutAnotherChunkReturnsEachRentalOnce()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            byte[] first = channel.TakeBufferForTest(32);
            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(first, 0, 2));
            channel.ReleaseSavedPartsForTest(1);

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(7)]
        public async Task TruncatedOpenSecureChannelReturnsDecryptedRentalAsync(int bodyLength)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            ArraySegment<byte> chunk = channel.CreateTruncatedOpenChunkForTest(bodyLength);

            await channel.FeedReceivedChunkAsync(chunk).ConfigureAwait(false);

            Assert.That(pool.RentCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task DiscoveryOnlyRequestsReturnEveryReceiveRentalAsync(bool intermediate, bool rejected)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(expectedSendCount: 1, captureSentChunks: true);
            transport.Complete();
            channel.SetTransport(transport);
            channel.SetDiscoveryOnlyForTest();
            int delivered = 0;
            channel.SetRequestReceivedCallback((_, _, _) => delivered++);
            int count = intermediate ? 1 : 10;
            for (uint index = 1; index <= count; index++)
            {
                IServiceRequest request = rejected ? new ReadRequest() : new GetEndpointsRequest();
                await channel.FeedReceivedChunkAsync(channel.CreateRequestChunkForTest(
                    40 + index, index, request, intermediate)).ConfigureAwait(false);
            }
            Assert.That(await WaitForOutstandingCountAsync(pool, expected: 0, 5).ConfigureAwait(false), Is.True);
            Assert.That(pool.RentCount, Is.GreaterThanOrEqualTo(count));
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(delivered, Is.EqualTo(rejected ? 0 : count));
            if (rejected)
            {
                byte[] sent = transport.LastSentChunk;
                using var decoder = new BinaryDecoder(
                    new ArraySegment<byte>(sent, 24, sent.Length - 24),
                    ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
                ServiceFault fault = decoder.DecodeMessage<ServiceFault>();
                Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            }
            Assert.That(channel.CurrentState,
                Is.EqualTo(intermediate ? TcpChannelState.Closed : TcpChannelState.Open));
        }

        /// <summary>
        /// <c>SaveIntermediateChunk</c> takes ownership unconditionally: a chunk
        /// it does not queue goes straight back to the pool. It used to drop
        /// such a chunk on the floor, which leaked one receive buffer per
        /// request on a discovery-only channel - reachable by any unauthenticated
        /// client of a server with no None endpoint.
        /// </summary>
        [Test]
        public void SaveIntermediateChunkReturnsAChunkItDoesNotQueue()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);

            byte[] buffer = channel.TakeBufferForTest(64);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));

            // request id zero is the "not part of a request" case, which is not
            // queued against a partial message.
            channel.SaveIntermediateChunkForTest(
                requestId: 0,
                new ArraySegment<byte>(buffer, 0, 64));

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        /// <summary>
        /// The chunk that trips the request chunk limit is returned along with
        /// the ones already buffered, rather than being abandoned while the
        /// channel is torn down.
        /// </summary>
        [Test]
        public async Task IntermediateChunksBeyondTheRequestChunkLimitReturnAllBuffersAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.SetMaxRequestChunkCountForTest(1);

            for (uint sequenceNumber = 1; sequenceNumber <= 3; sequenceNumber++)
            {
                await channel.FeedReceivedChunkAsync(
                    channel.CreateRequestChunkForTest(
                        TcpMessageType.Message,
                        isFinal: false,
                        sequenceNumber,
                        requestId: 1))
                    .ConfigureAwait(false);
            }

            Assert.That(pool.RentCount, Is.EqualTo(3));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        /// <summary>
        /// An intermediate CloseSecureChannel chunk is handed to the partial
        /// message and reported as owned. Reporting it as not owned returned the
        /// same buffer to the pool a second time, so two callers could then be
        /// handed the same array.
        /// </summary>
        [Test]
        public async Task IntermediateCloseSecureChannelChunkIsNotReturnedTwiceAsync()
        {
            var pool = new TrackingArrayPool();
            TestServerChannel channel = CreateOpenChannel(pool);

            try
            {
                await channel.FeedReceivedChunkAsync(
                    channel.CreateRequestChunkForTest(
                        TcpMessageType.Close,
                        isFinal: false,
                        sequenceNumber: 1,
                        requestId: 1))
                    .ConfigureAwait(false);

                Assert.That(pool.DuplicateReturnCount, Is.Zero);
                Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            }
            finally
            {
                // the channel still owns the chunk; disposing it hands the
                // unfinished message back.
                channel.Dispose();
            }

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        /// <summary>
        /// The chunks of a message the peer never finished are released when the
        /// channel goes away, so a client that sends one intermediate chunk and
        /// disconnects does not cost the pool a buffer per channel.
        /// </summary>
        [Test]
        public async Task DisposeReleasesTheChunksOfAnUnfinishedMessageAsync()
        {
            var pool = new TrackingArrayPool();
            TestServerChannel channel = CreateOpenChannel(pool);

            try
            {
                await channel.FeedReceivedChunkAsync(
                    channel.CreateRequestChunkForTest(
                        TcpMessageType.Message,
                        isFinal: false,
                        sequenceNumber: 1,
                        requestId: 1))
                    .ConfigureAwait(false);

                Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            }
            finally
            {
                channel.Dispose();
            }

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        /// <summary>
        /// A discovery-only channel reads the first chunk of a message to check
        /// its type, and must do so before handing the chunk over: a request id
        /// of zero is never queued, so the chunk goes straight back to the pool.
        /// Reading it afterwards read an array another caller may already have
        /// rented - reachable by any unauthenticated client of a server without
        /// a None endpoint.
        /// </summary>
        [Test]
        public async Task DiscoveryChannelReadsTheFirstChunkBeforeItIsReturnedAsync()
        {
            // A poisoned pool zeroes what comes back to it. A zeroed body decodes
            // as node id i=0, which the discovery check rejects - synchronously,
            // by closing the channel - so a read after the return is observable
            // without waiting on anything.
            var pool = new TrackingArrayPool(poisonOnReturn: true);
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.MakeDiscoveryOnlyForTest();

            // GetEndpointsRequest, four byte node id encoding: a type the
            // discovery check lets through.
            byte[] body = [0x01, 0x00, 0xAC, 0x01];

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(
                    TcpMessageType.Message,
                    isFinal: false,
                    sequenceNumber: 1,
                    requestId: 0,
                    body: body))
                .ConfigureAwait(false);

            // The type was read from the body that was sent, so a permitted
            // discovery request stays permitted. Reading the returned array
            // instead sees i=0 and closes the channel on it.
            Assert.That(
                channel.CurrentState,
                Is.EqualTo(TcpChannelState.Open),
                "the discovery check read the chunk after it had gone back to the pool.");
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
        }

        /// <summary>
        /// A chunk saved after the channel was disposed goes straight back to the
        /// pool. Disposal has already released the partial message, so a chunk
        /// queued into a fresh collection afterwards would never be released.
        /// </summary>
        [Test]
        public void ChunkSavedAfterDisposeIsReturnedRatherThanQueued()
        {
            var pool = new TrackingArrayPool();
            TestServerChannel channel = CreateOpenChannel(pool);

            byte[] buffer = channel.TakeBufferForTest(64);
            channel.Dispose();

            channel.SaveIntermediateChunkForTest(
                requestId: 1,
                new ArraySegment<byte>(buffer, 0, 64));

            Assert.That(pool.OutstandingCount, Is.Zero);
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

            public TcpChannelState CurrentState => State;

            public ServiceResult LastTransportError { get; private set; } = ServiceResult.Good;

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

            public void SetMaxRequestMessageSizeForTest(int maxRequestMessageSize)
            {
                MaxRequestMessageSize = maxRequestMessageSize;
            }

            public void SaveReceivedPartForTest(uint requestId, ArraySegment<byte> chunk)
            {
                SaveIntermediateChunk(requestId, chunk, true, gateHeld: false);
            }

            public void ReleaseSavedPartsForTest(uint requestId)
            {
                GetSavedChunks(requestId, default, true, gateHeld: false)
                    .Release(BufferManager, nameof(ReleaseSavedPartsForTest));
            }

            public void SetReceiveBufferSizeForTest(int receiveBufferSize)
            {
                ReceiveBufferSize = receiveBufferSize;
            }

            public byte[] TakeBufferForTest(int size)
            {
                return BufferManager.TakeBuffer(size, nameof(TakeBufferForTest));
            }

            public void SetMaxRequestChunkCountForTest(int maxRequestChunkCount)
            {
                MaxRequestChunkCount = maxRequestChunkCount;
            }

            public void SaveIntermediateChunkForTest(uint requestId, ArraySegment<byte> chunk)
            {
                SaveIntermediateChunk(requestId, chunk, isServerContext: true, gateHeld: false);
            }

            /// <summary>
            /// Marks the channel as discovery only, the state a server without a
            /// None endpoint gives every unauthenticated client. The setter is
            /// private to the channel, so it is reached by reflection.
            /// </summary>
            public void MakeDiscoveryOnlyForTest()
            {
                typeof(UaSCUaBinaryChannel)
                    .GetProperty(
                        "DiscoveryOnly",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(this, true);
            }

            /// <summary>
            /// Builds a chunk the symmetric read path accepts: the channel runs
            /// with <see cref="MessageSecurityMode.None"/>, so the body needs no
            /// signature or padding.
            /// </summary>
            public ArraySegment<byte> CreateRequestChunkForTest(
                uint baseMessageType,
                bool isFinal,
                uint sequenceNumber,
                uint requestId,
                int bodySize = 8,
                byte[] body = null)
            {
                if (body != null)
                {
                    bodySize = body.Length;
                }

                int length = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize +
                    bodySize;
                byte[] buffer = BufferManager.TakeBuffer(
                    length,
                    nameof(CreateRequestChunkForTest));

                uint messageType = baseMessageType |
                    (isFinal ? TcpMessageType.Final : TcpMessageType.Intermediate);

                BitConverter.GetBytes(messageType).CopyTo(buffer, 0);
                BitConverter.GetBytes(length).CopyTo(buffer, 4);
                BitConverter.GetBytes(ChannelId).CopyTo(buffer, 8);
                BitConverter.GetBytes(CurrentToken!.TokenId).CopyTo(buffer, 12);
                BitConverter.GetBytes(sequenceNumber).CopyTo(buffer, 16);
                BitConverter.GetBytes(requestId).CopyTo(buffer, 20);

                body?.CopyTo(buffer, TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize);

                return new ArraySegment<byte>(buffer, 0, length);
            }

            public ValueTask FeedReceivedChunkAsync(ArraySegment<byte> chunk)
            {
                return OnChunkReceivedAsync(chunk, CancellationToken.None);
            }

            public void SetDiscoveryOnlyForTest()
            {
                typeof(UaSCUaBinaryChannel).GetProperty(
                    "DiscoveryOnly",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(this, true);
            }

            public ArraySegment<byte> CreateRequestChunkForTest(
                uint requestId, uint sequence, IServiceRequest request, bool intermediate)
            {
                using var body = new System.IO.MemoryStream();
                BinaryEncoder.EncodeMessage(request, body, Quotas.MessageContext, true);
                byte[] buffer = BufferManager.TakeBuffer(8192, nameof(CreateRequestChunkForTest));
                using var encoder = new BinaryEncoder(buffer, 0, 8192, Quotas.MessageContext);
                encoder.WriteUInt32(null, TcpMessageType.Message |
                    (intermediate ? TcpMessageType.Intermediate : TcpMessageType.Final));
                encoder.WriteUInt32(null, (uint)(body.Length + 24));
                encoder.WriteUInt32(null, ChannelId);
                encoder.WriteUInt32(null, CurrentToken!.TokenId);
                encoder.WriteUInt32(null, sequence);
                encoder.WriteUInt32(null, requestId);
                encoder.WriteRawBytes(body.GetBuffer(), 0, (int)body.Length);
                return new ArraySegment<byte>(buffer, 0, encoder.Close());
            }

            public ArraySegment<byte> CreateTruncatedOpenChunkForTest(int bodyLength)
            {
                byte[] buffer = BufferManager.TakeBuffer(1024, nameof(CreateTruncatedOpenChunkForTest));
                using var encoder = new BinaryEncoder(buffer, 0, 1024, Quotas.MessageContext);
                encoder.WriteUInt32(null, TcpMessageType.Open | TcpMessageType.Final);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, SecurityPolicies.None);
                encoder.WriteByteString(null, ByteString.Empty);
                encoder.WriteByteString(null, ByteString.Empty);
                for (int i = 0; i < bodyLength; i++)
                {
                    encoder.WriteByte(null, 0);
                }
                int length = encoder.Close();
                BitConverter.GetBytes(length).CopyTo(buffer, 4);
                return new ArraySegment<byte>(buffer, 0, length);
            }

            protected override void OnTransportError(ServiceResult result)
            {
                LastTransportError = result;
                base.OnTransportError(result);
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

            /// <summary>
            /// Completes as soon as one send has begun. Writes are serialized so
            /// that chunks reach the wire in the order their sequence numbers
            /// were assigned, so only one send is ever in flight; a test that
            /// needs sends to be blocked waits on this rather than on all of
            /// them having started.
            /// </summary>
            public Task FirstSendStarted => m_firstSendStarted.Task;

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
                m_firstSendStarted.TrySetResult(true);
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

            private readonly TaskCompletionSource<bool> m_firstSendStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_closed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_sendCount;
        }

        private sealed class TrackingArrayPool : ArrayPool<byte>
        {
            /// <param name="poisonOnReturn">
            /// Zeroes a returned array, so that anything still reading it
            /// afterwards sees different data from what was written instead of
            /// quietly reading data that happens to still be there.
            /// </param>
            public TrackingArrayPool(bool poisonOnReturn = false)
            {
                m_poisonOnReturn = poisonOnReturn;
            }

            private readonly bool m_poisonOnReturn;

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

                if (m_poisonOnReturn)
                {
                    array.AsSpan().Clear();
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
