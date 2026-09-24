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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
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

        /// <summary>
        /// Verifies a chunk without a request owner is returned immediately and exactly once.
        /// </summary>
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

        /// <summary>
        /// Verifies a request-size failure returns both saved chunks and the incoming chunk.
        /// </summary>
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

        /// <summary>
        /// Verifies retrieving saved chunks without a new chunk transfers every rental for one release.
        /// </summary>
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

        /// <summary>
        /// Verifies truncated OpenSecureChannel bodies release the decrypted buffer as well as received input.
        /// </summary>
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

        /// <summary>
        /// Verifies sequence and certificate failures after decryption preserve their audit status and return every
        /// rental.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OpenSecureChannelRejectionAfterDecryptionReturnsEveryRentalAsync(bool sequenceFailure)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            using Certificate expectedCertificate = CertificateBuilder
                .Create("CN=Open Rejection")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            if (sequenceFailure)
            {
                Assert.That(channel.AcceptSequenceForTest(1), Is.True);
            }
            else
            {
                channel.ExpectClientCertificateForTest(expectedCertificate);
            }
            Exception auditedError = null;
            channel.SetReportOpenSecureChannelAuditCallback((_, _, _, error) => auditedError = error);

            await channel.FeedReceivedChunkAsync(channel.CreateOpenChunkForTest(sequenceNumber: 0))
                .ConfigureAwait(false);

            Assert.That(auditedError, Is.InstanceOf<ServiceResultException>());
            Assert.That(((ServiceResultException)auditedError).StatusCode,
                Is.EqualTo(sequenceFailure ? StatusCodes.BadSequenceNumberInvalid : StatusCodes.BadCertificateInvalid));
            Assert.That(await WaitForOutstandingCountAsync(pool, expected: 0, seconds: 5).ConfigureAwait(false), Is.True);
            Assert.That(pool.RentCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        /// <summary>
        /// Verifies discovery-only admission or rejection releases all receive buffers with the correct service
        /// outcome.
        /// </summary>
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
        /// Verifies decoded NodeId and ByteString values remain intact after returned receive buffers are poisoned.
        /// </summary>
        [Test]
        public async Task DecodedRequestRetainsValuesAfterInputBuffersAreReturnedAsync()
        {
            var pool = new TrackingArrayPool(poisonOnReturn: true);
            using TestServerChannel channel = CreateOpenChannel(pool);
            WriteRequest received = null;
            channel.SetRequestReceivedCallback((_, _, request) => received = request as WriteRequest);
            var request = new WriteRequest
            {
                NodesToWrite =
                [
                    new WriteValue
                    {
                        NodeId = new NodeId("retained-node", 1),
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(ByteString.From([1, 2, 3, 4])))
                    }
                ]
            };

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(1, 1, request, intermediate: false)).ConfigureAwait(false);

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(received, Is.Not.Null);
            Assert.That(received.NodesToWrite.Count, Is.EqualTo(1));
            Assert.That(received.NodesToWrite[0].NodeId, Is.EqualTo(new NodeId("retained-node", 1)));
            Assert.That(received.NodesToWrite[0].Value.WrappedValue.TryGetValue(out ByteString value), Is.True);
            Assert.That(value, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
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

        [Test]
        public void AdmissionDoesNotReclaimActiveHandshakePartialMessageOrServiceRequest()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.StartOpeningForTest(1);
            Assert.That(channel.TryIdleCleanupForAdmission(), Is.False);
            channel.OpenForTest();
            using (IDisposable request = channel.TrackPendingRequest())
            {
                Assert.That(channel.TryIdleCleanupForAdmission(), Is.False);
            }
            byte[] part = channel.TakeBufferForTest(32);
            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(part));
            Assert.That(channel.TryIdleCleanupForAdmission(), Is.False);
            channel.ReleaseSavedPartsForTest(1);
            Assert.That(channel.TryIdleCleanupForAdmission(), Is.True);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(pool.OutstandingCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FirstChunkExceedingTheRequestChunkLimitReleasesThePartialMessageAsync(bool isFinal)
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            channel.SetMaxRequestChunkCountForTest(1);

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(
                    TcpMessageType.Message,
                    isFinal: false,
                    sequenceNumber: 1,
                    requestId: 1)).ConfigureAwait(false);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            Assert.That(budget.ReservedBytes, Is.GreaterThan(0));

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(
                    TcpMessageType.Message,
                    isFinal,
                    sequenceNumber: 2,
                    requestId: 1)).ConfigureAwait(false);

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task IncompleteMessagesReleaseBuffersDespiteContinuingChunksAsync(bool opening)
        {
            const int channelCount = 3;
            var pool = new TrackingArrayPool();
            var clock = new FakeTimeProvider();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(telemetry))
            {
                MaxBufferSize = 8192,
                MaxMessageSize = 32768,
                ChannelLifetime = 1000,
                ChunkReassemblyBudget = budget
            };
            var buffers = new BufferManager(nameof(TcpServerChannelBufferTests), 8192, telemetry, pool);
            await using var listener = new TcpTransportListener(telemetry, clock);
            var registeredChannels = new ConcurrentDictionary<uint, TcpListenerChannel>();
            typeof(TcpTransportListener).GetField("m_channels", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(listener, registeredChannels);
            typeof(TcpTransportListener).GetField("m_quotas", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(listener, quotas);
            var channels = new List<TestServerChannel>();
            for (uint channelId = 1; channelId <= channelCount; channelId++)
            {
                var channel = new TestServerChannel(listener, buffers, quotas, telemetry, clock);
                if (opening)
                {
                    channel.StartOpeningForTest(channelId);
                }
                else
                {
                    channel.OpenForTest(channelId);
                }
                channels.Add(channel);
                registeredChannels[channelId] = channel;
                await channel.FeedReceivedChunkAsync(
                    opening
                        ? channel.CreateOpenChunkForTest(1, intermediate: true)
                        : channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1))
                    .ConfigureAwait(false);
            }

            Assert.That(pool.OutstandingCount, Is.EqualTo(channelCount));
            clock.Advance(TimeSpan.FromMilliseconds(600));
            foreach (TestServerChannel channel in channels)
            {
                await channel.FeedReceivedChunkAsync(
                    opening
                        ? channel.CreateOpenChunkForTest(2, intermediate: true)
                        : channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 1))
                    .ConfigureAwait(false);
            }
            Assert.That(pool.OutstandingCount, Is.EqualTo(channelCount * 2));
            Assert.That(budget.ReservedBytes, Is.GreaterThan(0));
            MethodInfo detect = typeof(TcpTransportListener).GetMethod(
                "DetectInactiveChannels", BindingFlags.Instance | BindingFlags.NonPublic)!;
            clock.Advance(TimeSpan.FromMilliseconds(399));
            detect.Invoke(listener, [null]);
            Assert.That(registeredChannels, Has.Count.EqualTo(channelCount));
            Assert.That(pool.OutstandingCount, Is.EqualTo(channelCount * 2));

            clock.Advance(TimeSpan.FromMilliseconds(1));
            detect.Invoke(listener, [null]);
            foreach (TestServerChannel channel in channels)
            {
                await channel.WaitForBackgroundWorkAsync().ConfigureAwait(false);
            }

            Assert.That(registeredChannels, Is.Empty);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [TestCase(15, true)]
        [TestCase(16, false)]
        public async Task IncomingChunkIsIncludedInTheMessageSizeLimitAsync(int messageSizeLimit, bool rejected)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.SetMaxRequestMessageSizeForTest(messageSizeLimit);
            for (uint sequenceNumber = 1; sequenceNumber <= 2; sequenceNumber++)
            {
                await channel.FeedReceivedChunkAsync(
                    channel.CreateRequestChunkForTest(TcpMessageType.Message, false, sequenceNumber, 1))
                    .ConfigureAwait(false);
            }

            Assert.That(pool.OutstandingCount, Is.EqualTo(rejected ? 0 : 2));
            Assert.That(channel.CurrentState, Is.EqualTo(rejected ? TcpChannelState.Closed : TcpChannelState.Open));
            channel.ReleaseSavedPartsForTest(1);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task InvalidCloseChunkReleasesAPreviouslyBufferedMessageAsync()
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            Assert.That(budget.ReservedBytes, Is.GreaterThan(0));
            ArraySegment<byte> invalidClose = channel.CreateRequestChunkForTest(TcpMessageType.Close, true, 2, 2);
            BitConverter.GetBytes(uint.MaxValue).CopyTo(invalidClose.Array!, 12);

            await channel.FeedReceivedChunkAsync(invalidClose).ConfigureAwait(false);

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [Test]
        public async Task ReassemblyExhaustionClosesTheChannelWhenAnErrorResponseCannotBeAllocatedAsync()
        {
            var pool = new TrackingArrayPool();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(telemetry))
            {
                MaxBufferSize = 8192,
                ChunkReassemblyBudget = new ChunkReassemblyBudget(64)
            };
            IBufferManager inner = BufferManager.CreateImplementation(
                nameof(TcpServerChannelBufferTests), 8192, telemetry, BufferManagerImplementationKind.Fast, pool);
            var manager = new Mock<IBufferManager>();
            manager.Setup(value => value.TakeBuffer(It.IsAny<int>(), It.IsAny<string>()))
                .Returns((int size, string owner) => size == 8192
                    ? throw new ServiceResultException(StatusCodes.BadTcpNotEnoughResources)
                    : inner.TakeBuffer(size, owner));
            manager.Setup(value => value.ReturnBuffer(It.IsAny<byte[]>(), It.IsAny<string>()))
                .Callback((byte[] buffer, string owner) => inner.ReturnBuffer(buffer, owner));
            var buffers = new BufferManager(manager.Object);
            var listener = new Mock<ITcpChannelListener>();
            using var channel = new TestServerChannel(
                listener.Object, buffers, quotas, telemetry, new FakeTimeProvider());
            channel.OpenForTest();
            var transport = new GateByteTransport(expectedSendCount: 1);
            channel.SetTransport(transport);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            Assert.That(quotas.ChunkReassemblyBudget.ReservedBytes, Is.EqualTo(32));
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 1)).ConfigureAwait(false);

            listener.Verify(value => value.ChannelClosed(1), Times.Once);
            Assert.That(transport.FirstSendStarted.IsCompleted, Is.False);
            Assert.That(
                () => channel.SendResponse(1, CreateResponse()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecureChannelClosed));
            Assert.That(quotas.ChunkReassemblyBudget.ReservedBytes, Is.Zero);
            Assert.That(pool.RentCount, Is.EqualTo(2));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task PartialMessageDeadlineDoesNotDependOnTheListenerSweepAsync()
        {
            var pool = new TrackingArrayPool();
            var clock = new FakeTimeProvider();
            using TestServerChannel channel = CreateOpenChannel(pool, clock: clock, channelLifetime: 1000);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 1)).ConfigureAwait(false);
            Assert.That(pool.OutstandingCount, Is.EqualTo(2));

            clock.Advance(TimeSpan.FromMilliseconds(400));
            await channel.WaitForBackgroundWorkAsync().ConfigureAwait(false);

            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MessageDeadlineDoesNotBlockTheTimerWhileTheChannelGateIsBusyAsync(bool discardMessage)
        {
            var pool = new TrackingArrayPool();
            var clock = new FakeTimeProvider();
            using TestServerChannel channel = CreateOpenChannel(pool, clock: clock, channelLifetime: 1000);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            ChannelGate.Releaser gate = channel.Gate.Enter();
            Task advance = Task.Run(() => clock.Advance(TimeSpan.FromMilliseconds(3000)));
            bool timerReturned;
            try
            {
                timerReturned = await CompletesWithinAsync(advance, 1).ConfigureAwait(false);
                if (timerReturned)
                {
                    Assert.That(channel.PendingBackgroundWork, Is.EqualTo(1));
                }
                if (discardMessage)
                {
                    channel.ReleaseSavedPartsForTest(1);
                }
            }
            finally
            {
                gate.Dispose();
                await advance.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            Assert.That(timerReturned, Is.True, "The timer callback must not occupy a thread waiting for the gate.");
            await channel.WaitForBackgroundWorkAsync().ConfigureAwait(false);
            Assert.That(await WaitForOutstandingCountAsync(pool, 0, 5).ConfigureAwait(false), Is.True);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(
                channel.CurrentState,
                Is.EqualTo(discardMessage ? TcpChannelState.Open : TcpChannelState.Closed));
        }

        [Test]
        public async Task CompletedMessageClearsItsAssemblyDeadlineAndTheNextMessageCanCompleteAsync()
        {
            var pool = new TrackingArrayPool();
            var clock = new FakeTimeProvider();
            using TestServerChannel channel = CreateOpenChannel(pool, clock: clock, channelLifetime: 1000);
            var handles = new List<uint>();
            channel.SetRequestReceivedCallback((_, _, request) => handles.Add(request.RequestHeader.RequestHandle));
            byte[] body = BinaryEncoder.EncodeMessage(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 123 } },
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            int split = body.Length / 2;
            channel.SetMaxRequestMessageSizeForTest(body.Length);
            channel.SetMaxRequestChunkCountForTest(2);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(
                    TcpMessageType.Message, false, 1, 1, body: body.AsSpan(0, split).ToArray()))
                .ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(
                    TcpMessageType.Message, true, 2, 1, body: body.AsSpan(split).ToArray()))
                .ConfigureAwait(false);
            Assert.That(handles, Is.EqualTo(new uint[] { 123 }));
            Assert.That(pool.OutstandingCount, Is.Zero);

            clock.Advance(TimeSpan.FromMilliseconds(400));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 3, 2, body: body))
                .ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, true, 4, 2, bodySize: 0))
                .ConfigureAwait(false);

            Assert.That(handles, Is.EqualTo(new uint[] { 123, 123 }));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task ReplacementRequestDoesNotInheritThePreviousMessagesQuotaAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.SetMaxRequestMessageSizeForTest(8);
            channel.SetMaxRequestChunkCountForTest(1);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 2)).ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            channel.ReleaseSavedPartsForTest(2);
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
                Assert.That(pool.OutstandingCount, Is.Zero);
                Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            }
            finally
            {
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
        /// A request the decoder rejects, here for a string above MaxStringLength,
        /// is answered with a ServiceFault that echoes the RequestHandle of its
        /// RequestHeader (OPC 10000-4 §7.33) and carries a response timestamp. The
        /// channel used to send RequestHandle 0, which the CTT reports.
        /// </summary>
        [Test]
        public async Task UndecodableRequestServiceFaultEchoesRequestHandleAsync()
        {
            const int maxStringLength = 64;
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool, maxStringLength: maxStringLength);
            var transport = new GateByteTransport(expectedSendCount: 1, captureSentChunks: true);
            channel.SetTransport(transport);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    AuthenticationToken = new NodeId("session", 0),
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 4711
                },
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(new string('n', 10 * maxStringLength), 1),
                        AttributeId = Attributes.Value
                    }
                ]
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var encodeContext = ServiceMessageContext.Create(telemetry);
            encodeContext.MaxStringLength = 0;
            byte[] body = BinaryEncoder.EncodeMessage(request, encodeContext);

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(
                    TcpMessageType.Message,
                    isFinal: true,
                    sequenceNumber: 1,
                    requestId: 9,
                    body: body))
                .ConfigureAwait(false);

            ServiceFault fault = await ReadSentServiceFaultAsync(transport, expectedRequestId: 9)
                .ConfigureAwait(false);
            Assert.That(
                fault.ResponseHeader.ServiceResult,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(4711u));
            Assert.That(
                (DateTime)fault.ResponseHeader.Timestamp,
                Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
        }

        /// <summary>
        /// A discovery-only channel rejects a non-discovery request from its
        /// encoding id before decoding it; the ServiceFault still echoes the
        /// RequestHandle, for a single-chunk request as well as for the first
        /// chunk of a multi-chunk request.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DiscoveryOnlyRejectionEchoesRequestHandleAsync(bool isFinal)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            channel.MakeDiscoveryOnlyForTest();
            var transport = new GateByteTransport(expectedSendCount: 1, captureSentChunks: true);
            channel.SetTransport(transport);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, RequestHandle = 815 },
                NodesToRead = [new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value }]
            };
            byte[] body = BinaryEncoder.EncodeMessage(
                request,
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(
                    TcpMessageType.Message,
                    isFinal,
                    sequenceNumber: 1,
                    requestId: 5,
                    body: body))
                .ConfigureAwait(false);

            ServiceFault fault = await ReadSentServiceFaultAsync(transport, expectedRequestId: 5)
                .ConfigureAwait(false);
            Assert.That(
                fault.ResponseHeader.ServiceResult,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityPolicyRejected));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(815u));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task DiscoveryQuotaRejectionDoesNotSendAServiceFaultAfterClosureAsync(bool isFinal)
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            channel.MakeDiscoveryOnlyForTest();
            var transport = new GateByteTransport(1, captureSentChunks: true);
            transport.Complete();
            channel.SetTransport(transport);
            byte[] body = BinaryEncoder.EncodeMessage(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 815 } },
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            channel.SetMaxRequestMessageSizeForTest(body.Length - 1);

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, isFinal, 1, 5, body: body))
                .ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(pool.RentCount, Is.EqualTo(1), "A rejected request must not allocate an outgoing fault.");
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(transport.LastSentChunk, Is.Null);
        }

        /// <summary>
        /// The symmetric <see cref="TcpListenerChannel"/> fault overload, also used
        /// by the Kestrel TCP listener, writes the RequestHandle and a Timestamp;
        /// the three-argument overload keeps writing 0.
        /// </summary>
        [TestCase(1234u)]
        [TestCase(0u)]
        public async Task ListenerChannelServiceFaultWritesRequestHandleAndTimestampAsync(uint requestHandle)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel channel = CreateOpenChannel(pool);
            var transport = new GateByteTransport(expectedSendCount: 1, captureSentChunks: true);
            channel.SetTransport(transport);

            if (requestHandle == 0)
            {
                channel.CallSymmetricSendServiceFault(3, new ServiceResult(StatusCodes.BadTimeout));
            }
            else
            {
                channel.CallSymmetricSendServiceFault(3, new ServiceResult(StatusCodes.BadTimeout), requestHandle);
            }

            ServiceFault fault = await ReadSentServiceFaultAsync(transport, expectedRequestId: 3)
                .ConfigureAwait(false);
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(requestHandle));
            Assert.That(
                (DateTime)fault.ResponseHeader.Timestamp,
                Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
        }

        private static async Task<ServiceFault> ReadSentServiceFaultAsync(
            GateByteTransport transport,
            uint expectedRequestId)
        {
            Assert.That(
                await CompletesWithinAsync(transport.FirstSendStarted, 30).ConfigureAwait(false),
                Is.True,
                "the channel never sent the service fault");
            byte[] sentChunk = transport.LastSentChunk;
            transport.Complete();

            Assert.That(sentChunk, Is.Not.Null);
            Assert.That(TcpMessageType.IsFinal(GetMessageType(sentChunk)), Is.True);
            Assert.That(
                BitConverter.ToUInt32(sentChunk, TcpMessageLimits.SymmetricHeaderSize + 4),
                Is.EqualTo(expectedRequestId),
                "request id of the fault");

            int bodyOffset = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
            return BinaryDecoder.DecodeMessage<ServiceFault>(
                sentChunk.AsSpan(bodyOffset).ToArray(),
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
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

        [TestCase(true)]
        [TestCase(false)]
        public async Task AbortAtTheMessageLimitReleasesOnlyTheMessageAndKeepsTheChannelOpenAsync(bool chunkLimit)
        {
            var pool = new TrackingArrayPool(poisonOnReturn: true);
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            if (chunkLimit)
            {
                channel.SetMaxRequestChunkCountForTest(1);
            }
            else
            {
                channel.SetMaxRequestMessageSizeForTest(8);
            }
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            Assert.That(budget.ReservedBytes, Is.GreaterThan(0));
            ArraySegment<byte> abort = channel.CreateRequestChunkForTest(TcpMessageType.Message, true, 2, 1);
            BitConverter.GetBytes(TcpMessageType.Message | TcpMessageType.Abort).CopyTo(abort.Array!, 0);
            BitConverter.GetBytes((uint)StatusCodes.BadRequestTooLarge).CopyTo(abort.Array!, 24);
            BitConverter.GetBytes(-1).CopyTo(abort.Array!, 28);

            await channel.FeedReceivedChunkAsync(abort).ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            channel.SetMaxRequestMessageSizeForTest(4 * 1024 * 1024);
            int delivered = 0;
            channel.SetRequestReceivedCallback((_, _, _) => delivered++);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(2, 3, new ReadRequest(), intermediate: false)).ConfigureAwait(false);
            Assert.That(delivered, Is.EqualTo(1));
        }

        [Test]
        public async Task LateFinalAfterQuotaClosureIsNotDecodedOrRetainedAsync()
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            channel.SetMaxRequestChunkCountForTest(1);
            int delivered = 0;
            channel.SetRequestReceivedCallback((_, _, _) => delivered++);
            ArraySegment<byte> lateFinal =
                channel.CreateRequestChunkForTest(1, 3, new ReadRequest(), intermediate: false);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 1)).ConfigureAwait(false);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));

            await channel.FeedReceivedChunkAsync(lateFinal).ConfigureAwait(false);

            Assert.That(delivered, Is.Zero);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(budget.ReservedBytes, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(100)]
        public async Task IntermediateChunksReserveTheirBackingArraysAndAbortReleasesThemAsync(int bodySize)
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            ArraySegment<byte> first = channel.CreateRequestChunkForTest(
                TcpMessageType.Message, false, 1, 1, bodySize);
            ArraySegment<byte> second = channel.CreateRequestChunkForTest(
                TcpMessageType.Message, false, 2, 1, bodySize);
            long expected = first.Array!.Length + second.Array!.Length;

            await channel.FeedReceivedChunkAsync(first).ConfigureAwait(false);
            await channel.FeedReceivedChunkAsync(second).ConfigureAwait(false);
            Assert.That(budget.ReservedBytes, Is.EqualTo(expected));
            Assert.That(pool.OutstandingCount, Is.EqualTo(2));

            ArraySegment<byte> abort = channel.CreateRequestChunkForTest(TcpMessageType.Message, true, 3, 1);
            BitConverter.GetBytes(TcpMessageType.Message | TcpMessageType.Abort).CopyTo(abort.Array!, abort.Offset);
            await channel.FeedReceivedChunkAsync(abort).ConfigureAwait(false);

            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
        }

        [Test]
        public async Task ChunkBeyondTheBudgetClosesWithNotEnoughResourcesAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel probe = CreateOpenChannel(pool);
            int rented = probe.GetRentedLengthForTest(32);
            var budget = new ChunkReassemblyBudget(2L * rented, rented);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            var transport = new GateByteTransport(1, captureSentChunks: true);
            transport.Complete();
            channel.SetTransport(transport);

            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            Assert.That(budget.ReservedBytes, Is.EqualTo(rented));
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 1)).ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            byte[] sent = transport.LastSentChunk;
            Assert.That(sent, Is.Not.Null);
            Assert.That(BitConverter.ToUInt32(sent, 0), Is.EqualTo(TcpMessageType.Error));
            ErrorMessage error = TcpMessageParsers.ReadErrorMessage(new ArraySegment<byte>(sent, 8, sent.Length - 8));
            Assert.That(error.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpNotEnoughResources));
            Assert.That(
                () => channel.SendResponse(1, CreateResponse()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecureChannelClosed));
        }

        [Test]
        public async Task SessionlessChannelsLeaveBudgetHeadroomForActivatedSessionsAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel probe = CreateOpenChannel(pool);
            int rented = probe.GetRentedLengthForTest(32);
            var budget = new ChunkReassemblyBudget(2L * rented, rented);
            using TestServerChannel sessionless = CreateOpenChannel(pool, budget: budget);
            using TestServerChannel refusedSessionless = CreateOpenChannel(pool, budget: budget);
            using TestServerChannel session = CreateOpenChannel(pool, budget: budget);
            using TestServerChannel refusedSession = CreateOpenChannel(pool, budget: budget);
            session.ActivateSessionForTest();
            refusedSession.ActivateSessionForTest();

            foreach (TestServerChannel channel in new[] { sessionless, refusedSessionless, session, refusedSession })
            {
                await channel.FeedReceivedChunkAsync(
                    channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            }

            Assert.That(sessionless.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(refusedSessionless.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(session.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(refusedSession.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(budget.ReservedBytes, Is.EqualTo(budget.MaxBytes));
            sessionless.Dispose();
            session.Dispose();
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FinalChunkIsProcessedWhileTheReassemblyBudgetIsFullAsync(bool hasIntermediate)
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel probe = CreateOpenChannel(pool);
            int rented = probe.GetRentedLengthForTest(8192);
            var budget = new ChunkReassemblyBudget(rented, rented);
            using TestServerChannel holder = CreateOpenChannel(pool, budget: budget);
            using TestServerChannel receiver = CreateOpenChannel(pool, budget: budget);
            int delivered = 0;
            receiver.SetRequestReceivedCallback((_, _, request) =>
            {
                Assert.That(request.RequestHeader.RequestHandle, Is.EqualTo(123u));
                delivered++;
            });
            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 123 } };
            TestServerChannel owner = hasIntermediate ? receiver : holder;
            await owner.FeedReceivedChunkAsync(
                owner.CreateRequestChunkForTest(1, 1, request, intermediate: true)).ConfigureAwait(false);
            Assert.That(budget.ReservedBytes, Is.EqualTo(budget.MaxBytes));

            ArraySegment<byte> final = hasIntermediate
                ? receiver.CreateRequestChunkForTest(TcpMessageType.Message, true, 2, 1, bodySize: 0)
                : receiver.CreateRequestChunkForTest(1, 1, request, intermediate: false);
            await receiver.FeedReceivedChunkAsync(final).ConfigureAwait(false);

            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(receiver.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(budget.ReservedBytes, Is.EqualTo(hasIntermediate ? 0 : budget.MaxBytes));
            holder.Dispose();
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task ReplacementRequestReusesTheDiscardedMessagesReservationAsync()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel probe = CreateOpenChannel(pool);
            int rented = probe.GetRentedLengthForTest(32);
            var budget = new ChunkReassemblyBudget(rented, rented);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            for (uint requestId = 1; requestId <= 3; requestId++)
            {
                await channel.FeedReceivedChunkAsync(
                    channel.CreateRequestChunkForTest(TcpMessageType.Message, false, requestId, requestId))
                    .ConfigureAwait(false);
                Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
                Assert.That(budget.ReservedBytes, Is.EqualTo(rented));
                Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            }
            channel.Dispose();
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public void ClosedChannelsDoNotAcquireNewReassemblyReservations()
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget);
            byte[] buffer = channel.TakeBufferForTest(32);
            channel.CloseForTest();
            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(buffer));

            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public void MembershipCallbackClosingChannelCannotRetainTheIncomingChunk()
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024);
            var provider = new Mock<ISessionBindingProvider>();
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget, bindingProvider: provider.Object);
            provider.Setup(value => value.HasSession(It.IsAny<string>())).Returns(() =>
            {
                channel.CloseForTest();
                return true;
            });
            byte[] buffer = channel.TakeBufferForTest(32);

            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(buffer, 0, 32));

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public void MembershipCallbackFailureReturnsTheIncomingChunk()
        {
            var pool = new TrackingArrayPool();
            var budget = new ChunkReassemblyBudget(1024);
            var provider = new Mock<ISessionBindingProvider>();
            provider.Setup(value => value.HasSession(It.IsAny<string>()))
                .Throws(new InvalidOperationException("Membership lookup failed."));
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget, bindingProvider: provider.Object);
            byte[] buffer = channel.TakeBufferForTest(32);

            Assert.That(
                () => channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(buffer, 0, 32)),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task ManagedMembershipIgnoresSuccessfulResponseCountsAsync()
        {
            var pool = new TrackingArrayPool();
            var provider = new Mock<ISessionBindingProvider>();
            bool hasSession = false;
            provider.Setup(p => p.HasSession(It.IsAny<string>())).Returns(() => hasSession);
            using TestServerChannel channel = CreateOpenChannel(pool, bindingProvider: provider.Object);
            var transport = new GateByteTransport(expectedSendCount: 3);
            channel.SetTransport(transport);
            for (uint id = 1; id <= 2; id++)
            {
                channel.SendResponse(id, new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            }
            channel.ActivateSessionForTest();
            Assert.That(channel.UsedBySession, Is.False, "Response and legacy hints cannot override managed state.");
            hasSession = true;
            channel.SendResponse(3, new CloseSessionResponse
            {
                ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
            });
            Assert.That(channel.UsedBySession, Is.True, "A response cannot remove another session's membership.");
            hasSession = false;
            Assert.That(channel.UsedBySession, Is.False);
            provider.Verify(p => p.HasSession(channel.GlobalChannelId), Times.Exactly(3));
            transport.Complete();
            Assert.That(await WaitForOutstandingCountAsync(pool, 0, 30).ConfigureAwait(false), Is.True);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public void ManagedMembershipControlsExistingReassemblyHeadroom()
        {
            var pool = new TrackingArrayPool();
            using TestServerChannel probe = CreateOpenChannel(pool);
            int rented = probe.GetRentedLengthForTest(32);
            var budget = new ChunkReassemblyBudget(2 * rented, rented);
            var provider = new Mock<ISessionBindingProvider>();
            bool hasSession = true;
            provider.Setup(p => p.HasSession(It.IsAny<string>())).Returns(() => hasSession);
            using TestServerChannel holder = CreateOpenChannel(pool, budget: budget);
            using TestServerChannel channel = CreateOpenChannel(pool, budget: budget, bindingProvider: provider.Object);
            holder.SaveReceivedPartForTest(1, new ArraySegment<byte>(holder.TakeBufferForTest(32)));
            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(channel.TakeBufferForTest(32)));
            Assert.That(budget.ReservedBytes, Is.EqualTo(2 * rented));
            channel.ReleaseSavedPartsForTest(1);
            hasSession = false;
            channel.SaveReceivedPartForTest(2, new ArraySegment<byte>(channel.TakeBufferForTest(32)));
            Assert.That(budget.ReservedBytes, Is.EqualTo(rented));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            holder.Dispose();
            Assert.That(budget.ReservedBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RenewalUsesBoundedHandshakeCapacityAndReleasesItAsync(bool reject)
        {
            var pool = new TrackingArrayPool();
            var policy = new RecordingIsolation { Refuse = reject, ExpectedStage = ResourceIsolationStage.Handshake };
            using TestServerChannel channel = CreateOpenChannel(pool, isolation: policy);

            await channel.FeedReceivedChunkAsync(channel.CreateTruncatedOpenChunkForTest(0)).ConfigureAwait(false);

            Assert.That(policy.AcquireCalls, Is.EqualTo(1));
            Assert.That(policy.OutstandingBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
            if (reject)
            {
                Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
                Assert.That(pool.RentCount, Is.EqualTo(1), "Rejected renewal must not allocate a decrypted body.");
            }
        }

        [TestCase("complete")]
        [TestCase("abort")]
        [TestCase("close")]
        [TestCase("quota")]
        public async Task ReassemblyIsolationLeasesFollowMessageOwnershipAsync(string release)
        {
            var pool = new TrackingArrayPool();
            var policy = new RecordingIsolation();
            using TestServerChannel channel = CreateOpenChannel(pool, isolation: policy);
            if (release == "quota")
            {
                channel.SetMaxRequestChunkCountForTest(2);
            }
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 1)).ConfigureAwait(false);
            Assert.That(policy.Classifications, Is.EqualTo(1));
            Assert.That(policy.OutstandingBytes, Is.EqualTo(2 * pool.LastMinimumLength));
            if (release == "close")
            {
                channel.CloseForTest();
            }
            else if (release == "complete")
            {
                channel.ReleaseSavedPartsForTest(1);
            }
            else
            {
                ArraySegment<byte> chunk = channel.CreateRequestChunkForTest(
                    TcpMessageType.Message, release == "abort", 3, 1);
                if (release == "abort")
                {
                    BitConverter.GetBytes(TcpMessageType.Message | TcpMessageType.Abort).CopyTo(chunk.Array!, 0);
                }
                await channel.FeedReceivedChunkAsync(chunk).ConfigureAwait(false);
            }
            Assert.That(policy.OutstandingBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        [Test]
        public async Task IsolationRejectionDiscardsPartialMessageAndFinalOnlyRequestDoesNotConsumeCapacityAsync()
        {
            var pool = new TrackingArrayPool();
            var policy = new RecordingIsolation();
            using TestServerChannel channel = CreateOpenChannel(pool, isolation: policy);
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 1, 1)).ConfigureAwait(false);
            policy.Refuse = true;
            await channel.FeedReceivedChunkAsync(
                channel.CreateRequestChunkForTest(TcpMessageType.Message, false, 2, 1)).ConfigureAwait(false);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(policy.OutstandingBytes, Is.Zero);
            using TestServerChannel healthy = CreateOpenChannel(pool, isolation: policy);
            int delivered = 0;
            healthy.SetRequestReceivedCallback((_, _, _) => delivered++);
            await healthy.FeedReceivedChunkAsync(
                healthy.CreateRequestChunkForTest(1, 1, new ReadRequest(), intermediate: false)).ConfigureAwait(false);
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(policy.Classifications, Is.EqualTo(1));
            Assert.That(pool.OutstandingCount, Is.Zero);
        }

        [Test]
        public void IsolationClassificationClosureReleasesCandidateLeaseAndBuffer()
        {
            var pool = new TrackingArrayPool();
            var policy = new RecordingIsolation();
            using TestServerChannel channel = CreateOpenChannel(pool, isolation: policy);
            policy.OnClassify = channel.CloseForTest;
            byte[] buffer = channel.TakeBufferForTest(32);

            channel.SaveReceivedPartForTest(1, new ArraySegment<byte>(buffer));

            Assert.That(policy.OutstandingBytes, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
        }

        private static TestServerChannel CreateOpenChannel(
            TrackingArrayPool pool,
            int maxBufferSize = 64 * 1024,
            int? maxStringLength = null,
            FakeTimeProvider clock = null,
            int? channelLifetime = null,
            ChunkReassemblyBudget budget = null,
            ISessionBindingProvider bindingProvider = null,
            IServerResourceIsolationProvider isolation = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
            if (maxStringLength.HasValue)
            {
                context.MaxStringLength = maxStringLength.Value;
            }
            var quotas = new ChannelQuotas(context)
            {
                MaxBufferSize = maxBufferSize,
                MaxMessageSize = 4 * 1024 * 1024,
                ChunkReassemblyBudget = budget,
                SessionBindingProvider = bindingProvider,
                ResourceIsolationProvider = isolation
            };
            if (channelLifetime.HasValue)
            {
                quotas.ChannelLifetime = channelLifetime.Value;
            }
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
                clock ?? new FakeTimeProvider());
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

        /// <summary>
        /// Exposes the real server-channel ownership paths with controllable quotas, transport, and incoming chunks.
        /// </summary>
        private sealed class TestServerChannel : TcpServerChannel
        {
            /// <summary>
            /// Creates a server channel using the supplied pool, quotas, telemetry, and test clock.
            /// </summary>
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

            /// <summary>
            /// Gets the state reached after the controlled transport operation.
            /// </summary>
            public TcpChannelState CurrentState => State;

            /// <summary>
            /// Gets the last error reported through the channel's transport-failure hook.
            /// </summary>
            public ServiceResult LastTransportError { get; private set; } = ServiceResult.Good;

            public int PendingBackgroundWork => BackgroundWork.PendingCount;

            /// <summary>
            /// Opens the channel with a deterministic token without performing a network handshake.
            /// </summary>
            public void OpenForTest(uint channelId = 1)
            {
                ChannelId = channelId;
                State = TcpChannelState.Open;
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(
                    new ChannelToken
                    {
                        ChannelId = channelId,
                        TokenId = 1,
                        SecurityPolicy = SecurityPolicyInfo.None,
                        CreatedAt = DateTime.UtcNow,
                        CreatedAtTimestamp = TimeProvider.GetTimestamp(),
                        Lifetime = 60000
                    },
                    previous: null);
            }

            public void StartOpeningForTest(uint channelId)
            {
                ChannelId = channelId;
                State = TcpChannelState.Opening;
            }

            public async Task WaitForBackgroundWorkAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (BackgroundWork.PendingCount != 0)
                {
                    await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Installs the transport used for controlled send and receive observations.
            /// </summary>
            public void SetTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            /// <summary>
            /// Invokes the real channel-closed cleanup path.
            /// </summary>
            public void CloseForTest()
            {
                ChannelClosed();
            }

            /// <summary>
            /// Sets the encoded response-size limit used by send-path tests.
            /// </summary>
            public void SetMaxResponseMessageSizeForTest(int maxResponseMessageSize)
            {
                MaxResponseMessageSize = maxResponseMessageSize;
            }

            /// <summary>
            /// Sets the request-size limit used when admitting intermediate chunks.
            /// </summary>
            public void SetMaxRequestMessageSizeForTest(int maxRequestMessageSize)
            {
                MaxRequestMessageSize = maxRequestMessageSize;
            }

            /// <summary>
            /// Transfers a received chunk to the intermediate-request ownership path.
            /// </summary>
            public void SaveReceivedPartForTest(uint requestId, ArraySegment<byte> chunk)
            {
                SaveIntermediateChunk(requestId, chunk, true, gateHeld: false);
            }

            /// <summary>
            /// Takes and releases the accumulated chunks without supplying another received chunk.
            /// </summary>
            public void ReleaseSavedPartsForTest(uint requestId)
            {
                GetSavedChunks(requestId, default, true, gateHeld: false)
                    .Release(BufferManager, nameof(ReleaseSavedPartsForTest));
            }

            /// <summary>
            /// Sets the accepted receive-chunk size limit.
            /// </summary>
            public void SetReceiveBufferSizeForTest(int receiveBufferSize)
            {
                ReceiveBufferSize = receiveBufferSize;
            }

            /// <summary>
            /// Rents input storage from the channel's tracked buffer manager.
            /// </summary>
            public byte[] TakeBufferForTest(int size)
            {
                return BufferManager.TakeBuffer(size, nameof(TakeBufferForTest));
            }

            public int GetRentedLengthForTest(int size)
            {
                byte[] buffer = BufferManager.TakeBuffer(size, nameof(GetRentedLengthForTest));
                int length = buffer.Length;
                BufferManager.ReturnBuffer(buffer, nameof(GetRentedLengthForTest));
                return length;
            }

            public void ActivateSessionForTest()
            {
                AddSession();
            }

            /// <summary>
            /// Sets the number of chunks permitted in one incoming request.
            /// </summary>
            public void SetMaxRequestChunkCountForTest(int maxRequestChunkCount)
            {
                MaxRequestChunkCount = maxRequestChunkCount;
            }

            /// <summary>
            /// Transfers an incoming chunk into server-side intermediate-message ownership.
            /// </summary>
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

            /// <summary>
            /// Delivers a pooled chunk through the asynchronous server receive handler.
            /// </summary>
            public ValueTask FeedReceivedChunkAsync(ArraySegment<byte> chunk)
            {
                return OnChunkReceivedAsync(chunk, CancellationToken.None);
            }

            /// <summary>
            /// Enables the channel mode that permits discovery requests without a secured endpoint.
            /// </summary>
            public void SetDiscoveryOnlyForTest()
            {
                typeof(UaSCUaBinaryChannel).GetProperty(
                    "DiscoveryOnly",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(this, true);
            }

            /// <summary>
            /// Encodes a pooled request chunk with controlled sequence, request identifier, and continuation flag.
            /// </summary>
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

            /// <summary>
            /// Encodes an OpenSecureChannel header followed by a deliberately short body.
            /// </summary>
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

            /// <summary>
            /// Seeds accepted sequence state for a later post-decryption rejection.
            /// </summary>
            public bool AcceptSequenceForTest(uint sequenceNumber)
            {
                return VerifySequenceNumber(sequenceNumber, nameof(AcceptSequenceForTest));
            }

            /// <summary>
            /// Retains the client certificate expected during OpenSecureChannel validation.
            /// </summary>
            public void ExpectClientCertificateForTest(Certificate certificate)
            {
                ClientCertificate = certificate.AddRef();
            }

            /// <summary>
            /// Creates an OpenSecureChannel chunk with a controlled sequence header and request identifier.
            /// </summary>
            public ArraySegment<byte> CreateOpenChunkForTest(uint sequenceNumber, bool intermediate = false)
            {
                ArraySegment<byte> chunk = CreateTruncatedOpenChunkForTest(TcpMessageLimits.SequenceHeaderSize);
                if (intermediate)
                {
                    BitConverter.GetBytes(TcpMessageType.Open | TcpMessageType.Intermediate).CopyTo(chunk.Array!, 0);
                }
                BitConverter.GetBytes(sequenceNumber).CopyTo(chunk.Array!, chunk.Count - 8);
                BitConverter.GetBytes(1u).CopyTo(chunk.Array!, chunk.Count - 4);
                return chunk;
            }

            /// <summary>
            /// Sends a symmetric fault through the compatibility overload when no request handle is available.
            /// </summary>
            public void CallSymmetricSendServiceFault(uint requestId, ServiceResult fault)
            {
                SendServiceFault(CurrentToken!, requestId, fault);
            }

            /// <summary>
            /// Sends a symmetric fault with the request handle recovered from the failed request.
            /// </summary>
            public void CallSymmetricSendServiceFault(uint requestId, ServiceResult fault, uint requestHandle)
            {
                SendServiceFault(CurrentToken!, requestId, fault, requestHandle);
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

        private sealed class RecordingIsolation : IServerResourceIsolationProvider
        {
            public bool UseFairScheduling => true;
            public event Action CapacityAvailable
            {
                add { }
                remove { }
            }

            public int Classifications { get; private set; }
            public long OutstandingBytes { get; private set; }
            public int AcquireCalls { get; private set; }
            public ResourceIsolationStage ExpectedStage { get; set; } = ResourceIsolationStage.ReassemblyBytes;
            public bool Refuse { get; set; }
            public Action OnClassify { get; set; }

            public ResourceIsolationOwner ClassifyConnection(IPEndPoint remoteEndpoint)
            {
                throw new NotSupportedException();
            }

            public ResourceIsolationOwner Classify(
                SecureChannelContext channelContext, NodeId authenticationToken = default,
                bool sessionEstablishment = false, bool controlRequest = false)
            {
                Classifications++;
                OnClassify?.Invoke();
                return new ResourceIsolationOwner(
                    "test-owner", ResourceIsolationClass.Established, 1,
                    [1024, 1024, 1024, 1024, 1024, 1024, 1024, 1024]);
            }

            public bool IsCurrent(
                ResourceIsolationOwner owner, SecureChannelContext channelContext,
                NodeId authenticationToken = default, bool sessionEstablishment = false, bool controlRequest = false)
            {
                return true;
            }

            public bool TryAcquire(
                ResourceIsolationStage stage, ResourceIsolationOwner owner, long amount,
                out IDisposable lease, out ResourceIsolationFailure failure)
            {
                AcquireCalls++;
                Assert.That(stage, Is.EqualTo(ExpectedStage));
                failure = default;
                if (Refuse)
                {
                    lease = null;
                    return false;
                }
                OutstandingBytes += amount;
                lease = new Reservation(this, amount);
                return true;
            }

            private sealed class Reservation(RecordingIsolation owner, long amount) : IDisposable
            {
                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_released, 1) == 0)
                    {
                        owner.OutstandingBytes -= amount;
                    }
                }
                private int m_released;
            }
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
