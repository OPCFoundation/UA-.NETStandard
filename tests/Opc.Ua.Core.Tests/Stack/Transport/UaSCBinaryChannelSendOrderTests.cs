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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

// CA5394: the random choices only vary the interleaving of the tests; the seeds are fixed or logged.
#pragma warning disable CA5394

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Responses are encoded on the producer's thread without the channel gate and
    /// numbered and secured by the channel's single writer (see
    /// <c>UaSCUaBinaryChannel.SendOrder</c>). These tests check what that must keep:
    /// sequence numbers that increase by one in wire order, contiguous chunks, the
    /// token rules across a renewal, and buffers returned exactly once.
    /// </summary>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class UaSCBinaryChannelSendOrderTests
    {
        [Test]
        public async Task ConcurrentResponsesReachTheWireInSequenceOrderAsync()
        {
            const int producers = 8;
            const int perProducer = 150;
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: 3);
            channel.SetTransport(transport);

            await RunProducersAsync(producers, async (producer, random) =>
            {
                for (int ii = 0; ii < perProducer; ii++)
                {
                    uint requestId = (uint)((producer * perProducer) + ii + 1);
                    bool retained = await channel
                        .SendResponseAsync(requestId, CreateResponse(random))
                        .ConfigureAwait(false);
                    Assert.That(retained, Is.False);
                    if (random.Next(4) == 0)
                    {
                        await Task.Yield();
                    }
                }
            }).ConfigureAwait(false);

            List<WireChunk> chunks = await transport
                .WaitForFinalChunksAsync(producers * perProducer)
                .ConfigureAwait(false);
            AssertWireOrder(chunks, Enumerable.Range(1, producers * perProducer).Select(id => (uint)id));
            Assert.That(chunks.Any(chunk => !chunk.IsFinal), Is.True, "no response was split into chunks.");

            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        [Test]
        public async Task MessagesNumberedUnderSendOrderKeepTheWireOrderAsync()
        {
            // An asymmetric message (an OpenSecureChannel response to a renewal) takes
            // its sequence number when it is built, while responses are numbered by
            // the writer. Both kinds sent concurrently must still be in order.
            const int producers = 6;
            const int perProducer = 120;
            const int numbered = 200;
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: 2);
            channel.SetTransport(transport);

            await RunProducersAsync(producers + 1, async (producer, random) =>
            {
                if (producer == producers)
                {
                    for (int ii = 0; ii < numbered; ii++)
                    {
                        channel.QueueNumberedMessageForTest((uint)(100_000 + ii));
                        if (random.Next(3) == 0)
                        {
                            await Task.Yield();
                        }
                    }
                    return;
                }

                for (int ii = 0; ii < perProducer; ii++)
                {
                    uint requestId = (uint)((producer * perProducer) + ii + 1);
                    await channel.SendResponseAsync(requestId, CreateResponse(random)).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            IEnumerable<uint> expected = Enumerable.Range(1, producers * perProducer).Select(id => (uint)id)
                .Concat(Enumerable.Range(100_000, numbered).Select(id => (uint)id));
            List<WireChunk> chunks = await transport
                .WaitForFinalChunksAsync((producers * perProducer) + numbered)
                .ConfigureAwait(false);
            AssertWireOrder(chunks, expected);

            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        [TestCase(SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(SecurityPolicies.ECC_nistP256, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(SecurityPolicies.ECC_nistP256_AesGcm, MessageSecurityMode.SignAndEncrypt)]
        public async Task TokenRenewalDuringConcurrentSendsKeepsMessagesVerifiableAsync(
            string policyUri,
            MessageSecurityMode mode)
        {
            SecurityPolicyInfo? policy = SecurityPolicies.Default.GetInfo(policyUri);
            if (policy == null)
            {
                Assert.Ignore("The policy is not supported on this platform.");
                return;
            }

            const int producers = 4;
            const int perProducer = 100;
            const int renewals = 12;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var pool = new CountingPool();
            using SealProbe writer = SealProbe.Create(policyUri, mode, telemetry, pool);
            using SealProbe reader = SealProbe.Create(policyUri, mode, telemetry, new CountingPool());
            var transport = new RecordingTransport(asyncSendEvery: 2);
            writer.SetTransport(transport);

            var readerTokens = new Dictionary<uint, ChannelToken>();
            try
            {
                SealProbe.Renew(writer, reader, 1, readerTokens);

                // the OpenSecureChannel exchange consumed the first sequence number;
                // the AEAD policies feed the previous one into the nonce.
                Assert.That(reader.AcceptSequenceNumber(writer.TakeSequenceNumber()), Is.True);

                using var sendsDone = new CancellationTokenSource();
                Task renewer = Task.Run(async () =>
                {
                    for (uint tokenId = 2; tokenId <= renewals + 1; tokenId++)
                    {
                        await Task.Delay(2).ConfigureAwait(false);
                        lock (readerTokens)
                        {
                            SealProbe.Renew(writer, reader, tokenId, readerTokens);
                        }
                    }
                });

                await RunProducersAsync(producers, async (producer, random) =>
                {
                    for (int ii = 0; ii < perProducer; ii++)
                    {
                        uint requestId = (uint)((producer * perProducer) + ii + 1);
                        writer.Send(requestId, CreateResponse(random));
                        if (random.Next(3) == 0)
                        {
                            await Task.Delay(random.Next(2)).ConfigureAwait(false);
                        }
                    }
                }).ConfigureAwait(false);
                await renewer.ConfigureAwait(false);

                List<WireChunk> chunks = await transport
                    .WaitForMessageCountAsync(producers * perProducer)
                    .ConfigureAwait(false);

                // decode in wire order, as the client does: every chunk verifies with the
                // token it names, the numbers follow on, and once a newer token was used
                // the older one is not used again.
                uint lastTokenId = 0;
                var decoded = new List<WireChunk>(chunks.Count);
                foreach (WireChunk chunk in chunks)
                {
                    Assert.That(chunk.TokenId, Is.GreaterThanOrEqualTo(lastTokenId), "a token was used after its successor.");
                    lastTokenId = chunk.TokenId;
                    decoded.Add(reader.Read(chunk, readerTokens[chunk.TokenId]));
                }

                Assert.That(lastTokenId, Is.GreaterThan(1u), "no message was secured with a renewed token.");
                AssertWireOrder(decoded, Enumerable.Range(1, producers * perProducer).Select(id => (uint)id));
                await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
            }
            finally
            {
                reader.ReleaseTokens();
                foreach (ChannelToken token in readerTokens.Values)
                {
                    token.Dispose();
                }
            }
        }

        [Test]
        public async Task ClosingWithUnsealedResponsesQueuedReturnsEveryBufferOnceAsync()
        {
            var pool = new CountingPool();
            OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: 1);
            transport.HoldSends();
            channel.SetTransport(transport);

            var random = new Random(7);
            for (uint requestId = 1; requestId <= 40; requestId++)
            {
                await channel.SendResponseAsync(requestId, CreateResponse(random)).ConfigureAwait(false);
            }

            // the first send is in flight; the rest wait in the queue unsealed.
            await transport.FirstSendStarted.ConfigureAwait(false);
            Assert.That(channel.HasQueuedWritesForTest, Is.True);

            // disposing discards the tokens; the writer finds no token to seal with.
            channel.Dispose();
            transport.ReleaseSends();

            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
            Assert.That(channel.HasQueuedWritesForTest, Is.False);
        }

        [Test]
        public async Task SealFailureIsReportedAndLaterResponsesStillSendAsync()
        {
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: 1);
            transport.HoldSends();
            channel.SetTransport(transport);

            await channel.SendResponseAsync(1, CreateResponse(new Random(1), 0)).ConfigureAwait(false);
            await transport.FirstSendStarted.ConfigureAwait(false);

            // queued behind the held send, then the token goes away and comes back:
            // the queued ones fail to seal, the ones after are sent.
            await channel.SendResponseAsync(2, CreateResponse(new Random(2), 0)).ConfigureAwait(false);
            await channel.SendResponseAsync(3, CreateResponse(new Random(3), 0)).ConfigureAwait(false);
            channel.LoadTokenForTest(null);
            transport.ReleaseSends();
            await WaitUntilAsync(() => !channel.HasQueuedWritesForTest).ConfigureAwait(false);
            channel.LoadTokenForTest(2);

            await channel.SendResponseAsync(4, CreateResponse(new Random(4), 0)).ConfigureAwait(false);

            List<WireChunk> chunks = await transport.WaitForFinalChunksAsync(2).ConfigureAwait(false);
            Assert.That(chunks.Select(chunk => chunk.RequestId), Is.EqualTo(new uint[] { 1, 4 }));
            Assert.That(chunks[1].SequenceNumber, Is.EqualTo(chunks[0].SequenceNumber + 1));
            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        /// <summary>
        /// A response that cannot be encoded is answered with a ServiceFault carrying
        /// its RequestHandle, whether it is sent on the receive loop while that
        /// dispatches the request (with the gate free) or from another thread.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task AResponseThatCannotBeEncodedIsAnsweredWithAFaultAsync(bool sentWhileDispatching)
        {
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool, maxByteStringLength: 100);
            var transport = new RecordingTransport(asyncSendEvery: 2);
            channel.SetTransport(transport);
            channel.SetRequestReceivedCallback((target, requestId, request) =>
            {
                ReadResponse response = CreateResponse(null, requestId == 2 ? 1000 : 10);
                response.ResponseHeader.RequestHandle = requestId + 100;
                if (sentWhileDispatching)
                {
                    _ = ((TcpServerChannel)target).SendResponseAsync(requestId, response).AsTask();
                }
                else
                {
                    _ = Task.Run(() => ((TcpServerChannel)target).SendResponseAsync(requestId, response).AsTask());
                }
            });

            for (uint ii = 1; ii <= 3; ii++)
            {
                transport.Feed(channel.CreateRequestChunkForTest(ii, ii));
            }
            channel.StartReceiveLoopForTest();

            List<WireChunk> chunks = await transport.WaitForFinalChunksAsync(3).ConfigureAwait(false);
            AssertWireOrder(chunks, [1, 2, 3]);
            foreach (WireChunk chunk in chunks)
            {
                IServiceResponse response = DecodeBody(channel, chunk);
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(chunk.RequestId + 100));
                if (chunk.RequestId == 2)
                {
                    Assert.That(response, Is.InstanceOf<ServiceFault>());
                    // the status of the encoding failure, as the gate path reported it before.
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                }
                else
                {
                    Assert.That(response, Is.InstanceOf<ReadResponse>());
                }
            }

            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        /// <summary>
        /// A message whose security fails after its chunks took sequence numbers leaves a
        /// gap the peer would reject: the writer numbers nothing more for the transport and
        /// reports a transport error, which faults the channel.
        /// </summary>
        [Test]
        public async Task ASealFailureAfterNumberingStopsTheChannelAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var pool = new CountingPool();
            using SealProbe writer = SealProbe.Create(
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, telemetry, pool);
            using SealProbe reader = SealProbe.Create(
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, telemetry, new CountingPool());
            var transport = new RecordingTransport(asyncSendEvery: 2);
            writer.SetTransport(transport);
            var readerTokens = new Dictionary<uint, ChannelToken>();
            try
            {
                SealProbe.Renew(writer, reader, 1, readerTokens);
                writer.Send(1, CreateResponse(null, 10));
                await transport.WaitForMessageCountAsync(1).ConfigureAwait(false);

                byte[]? key = writer.BreakEncryptionForTest();
                writer.Send(2, CreateResponse(null, 10));
                await WaitUntilAsync(() => writer.TransportErrorCount > 0).ConfigureAwait(false);

                // a producer numbers a message before the channel faults, as an
                // OpenSecureChannel response or a fault does: it follows the gap too.
                writer.QueueNumberedMessageForTest(4);

                // sealable again, but the numbers after the gap would be rejected.
                writer.RestoreEncryptionForTest(key);
                writer.Send(3, CreateResponse(null, 10));
                await WaitUntilAsync(() => !writer.HasPendingWritesForTest).ConfigureAwait(false);
                List<WireChunk> chunks = await transport.WaitForMessageCountAsync(1).ConfigureAwait(false);
                Assert.That(chunks, Has.Count.EqualTo(1), "nothing was sent after the failed message.");
                Assert.That(writer.TransportErrorCount, Is.EqualTo(1));
                await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
            }
            finally
            {
                reader.ReleaseTokens();
                foreach (ChannelToken token in readerTokens.Values)
                {
                    token.Dispose();
                }
            }
        }

        [Test]
        public async Task ResponsesQueuedForAReplacedTransportAreNotNumberedAsync()
        {
            // A reconnect replaces the transport and answers on the new one with an
            // OpenSecureChannel response numbered under the send order lock. Responses
            // still queued for the dropped transport must not take numbers after it:
            // the client would see a gap on the new transport.
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var dropped = new RecordingTransport(asyncSendEvery: 1);
            dropped.HoldSends();
            channel.SetTransport(dropped);

            await channel.SendResponseAsync(1, CreateResponse(null, 0)).ConfigureAwait(false);
            await dropped.FirstSendStarted.ConfigureAwait(false);
            for (uint requestId = 2; requestId <= 10; requestId++)
            {
                await channel.SendResponseAsync(requestId, CreateResponse(null, requestId % 3 == 0 ? 20000 : 0)).ConfigureAwait(false);
            }

            var replacement = new RecordingTransport(asyncSendEvery: 2);
            channel.SetTransport(replacement);
            channel.QueueNumberedMessageForTest(1000);
            dropped.ReleaseSends();

            for (uint requestId = 11; requestId <= 20; requestId++)
            {
                await channel.SendResponseAsync(requestId, CreateResponse(null, requestId % 3 == 0 ? 20000 : 0)).ConfigureAwait(false);
            }

            List<WireChunk> chunks = await replacement.WaitForFinalChunksAsync(11).ConfigureAwait(false);
            AssertWireOrder(chunks, Enumerable.Range(11, 10).Select(id => (uint)id).Prepend(1000u));
            Assert.That(chunks[0].RequestId, Is.EqualTo(1000u));
            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        [Test]
        public async Task ReceiveBackpressureEngagesAndReleasesAsync()
        {
            // OPC UA servers stop reading while more than 100 responses wait to be
            // written, and read again once half of them are gone.
            const int requests = 180;
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: 1);
            transport.HoldSends();
            channel.SetTransport(transport);
            channel.SetRequestReceivedCallback((target, requestId, request) =>
                _ = ((TcpServerChannel)target).SendResponseAsync(requestId, CreateResponse(null, 0)).AsTask());

            for (uint ii = 1; ii <= requests; ii++)
            {
                transport.Feed(channel.CreateRequestChunkForTest(ii, ii));
            }
            channel.StartReceiveLoopForTest();

            await WaitUntilAsync(() => channel.ChannelFull).ConfigureAwait(false);

            // the loop stops reading: the reads stay where they are.
            int readsWhenFull = transport.ReceiveCalls;
            await Task.Delay(200).ConfigureAwait(false);
            Assert.That(transport.ReceiveCalls, Is.EqualTo(readsWhenFull));
            Assert.That(readsWhenFull, Is.LessThan(requests));

            transport.ReleaseSends();

            List<WireChunk> chunks = await transport.WaitForFinalChunksAsync(requests).ConfigureAwait(false);
            AssertWireOrder(chunks, Enumerable.Range(1, requests).Select(id => (uint)id));
            Assert.That(channel.ChannelFull, Is.False);
        }

        [Test]
        [Repeat(3)]
        public async Task RandomReceiveSendAndRenewalInterleavingKeepsTheWireOrderAsync()
        {
            // A fuzz-style mix on one channel: requests arrive on the receive loop, each
            // is answered from the pool after a random delay, with a random size, while
            // tokens are activated and numbered messages are queued.
            const int requests = 400;
            int seed = Environment.TickCount;
            TestContext.Out.WriteLine($"Seed {seed}");
            var random = new Random(seed);
            int[] sizes = [.. Enumerable.Range(0, requests + 1).Select(_ => random.Next(4) == 0 ? random.Next(7000, 26000) : random.Next(0, 200))];
            int[] delays = [.. Enumerable.Range(0, requests + 1).Select(_ => random.Next(3))];

            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: 4);
            channel.SetTransport(transport);
            int answered = 0;
            channel.SetRequestReceivedCallback((target, requestId, request) =>
                _ = Task.Run(async () =>
                {
                    if (delays[requestId] > 0)
                    {
                        await Task.Delay(delays[requestId] - 1).ConfigureAwait(false);
                    }
                    await ((TcpServerChannel)target)
                        .SendResponseAsync(requestId, CreateResponse(null, sizes[requestId]))
                        .ConfigureAwait(false);
                    Interlocked.Increment(ref answered);
                }));

            channel.StartReceiveLoopForTest();

            uint tokenId = 1;
            uint fed = 0;
            uint numberedId = 500_000;
            var numberedIds = new List<uint>();
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (Volatile.Read(ref answered) < requests && DateTime.UtcNow < deadline)
            {
                // a request is secured with the newest token when the loop reads it, so
                // a renewed token is activated by the receive loop as a client does.
                for (int ii = random.Next(1, 8); ii > 0 && fed < requests; ii--)
                {
                    uint requestId = ++fed;
                    transport.Feed(() => channel.CreateRequestChunkForTest(
                        requestId,
                        requestId,
                        channel.NewestTokenIdForTest));
                }

                // one renewal at a time: the next once the client used the last one.
                if (random.Next(3) == 0 && channel.CurrentTokenIdForTest == tokenId)
                {
                    channel.RenewTokenForTest(++tokenId);
                }
                else
                {
                    channel.QueueNumberedMessageForTest(numberedId);
                    numberedIds.Add(numberedId++);
                }
                await Task.Delay(1).ConfigureAwait(false);
            }

            Assert.That(Volatile.Read(ref answered), Is.EqualTo(requests));
            List<WireChunk> chunks = await transport
                .WaitForFinalChunksAsync(requests + numberedIds.Count)
                .ConfigureAwait(false);
            AssertWireOrder(chunks, Enumerable.Range(1, requests).Select(id => (uint)id).Concat(numberedIds));
            Assert.That(channel.CurrentTokenIdForTest, Is.GreaterThan(1u), "no renewed token was activated.");

            uint lastTokenId = 0;
            foreach (WireChunk chunk in chunks)
            {
                Assert.That(chunk.TokenId, Is.GreaterThanOrEqualTo(lastTokenId));
                lastTokenId = chunk.TokenId;
            }
        }

        /// <summary>
        /// Producers queue their responses without the channel gate, and the writer numbers
        /// them. With more producer threads than cores, while numbered messages are queued
        /// and tokens are activated, the wire still carries consecutive sequence numbers and
        /// contiguous chunks, and every response exactly once.
        /// </summary>
        [Test]
        [Repeat(3)]
        public async Task ManyProducersWithNumberedMessagesAndTokenChangesKeepTheWireOrderAsync()
        {
            int producers = Math.Max(32, Environment.ProcessorCount * 2);
            const int perProducer = 60;
            const int numbered = 150;
            const int activations = 15;
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: 3);
            channel.SetTransport(transport);

            await RunProducersAsync(producers + 2, async (producer, random) =>
            {
                if (producer == producers)
                {
                    for (int ii = 0; ii < numbered; ii++)
                    {
                        channel.QueueNumberedMessageForTest((uint)(1_000_000 + ii));
                        if (random.Next(4) == 0)
                        {
                            await Task.Yield();
                        }
                    }
                    return;
                }

                if (producer == producers + 1)
                {
                    for (uint tokenId = 2; tokenId <= activations + 1; tokenId++)
                    {
                        await Task.Yield();
                        channel.ActivateTokenForTest(tokenId);
                    }
                    return;
                }

                for (int ii = 0; ii < perProducer; ii++)
                {
                    uint requestId = (uint)((producer * perProducer) + ii + 1);
                    await channel.SendResponseAsync(requestId, CreateResponse(random)).ConfigureAwait(false);
                    if (random.Next(8) == 0)
                    {
                        await Task.Yield();
                    }
                }
            }).ConfigureAwait(false);

            IEnumerable<uint> expected = Enumerable.Range(1, producers * perProducer).Select(id => (uint)id)
                .Concat(Enumerable.Range(1_000_000, numbered).Select(id => (uint)id));
            List<WireChunk> chunks = await transport
                .WaitForFinalChunksAsync((producers * perProducer) + numbered)
                .ConfigureAwait(false);
            AssertWireOrder(chunks, expected);
            Assert.That(chunks.Any(chunk => !chunk.IsFinal), Is.True, "no response was split into chunks.");

            uint lastTokenId = 0;
            foreach (WireChunk chunk in chunks)
            {
                Assert.That(chunk.TokenId, Is.GreaterThanOrEqualTo(lastTokenId), "a token was used after its successor.");
                lastTokenId = chunk.TokenId;
            }
            Assert.That(lastTokenId, Is.GreaterThan(1u), "no message was secured with an activated token.");

            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        /// <summary>
        /// A producer queues each response the moment the previous one is on the wire,
        /// which is when the writer finds its queues empty and stops. A response queued
        /// while it stops must start it again, or be taken by it.
        /// </summary>
        [Test]
        public async Task ResponsesQueuedWhileTheWriterStopsAreSentAsync()
        {
            // one producer: a response stranded by the writer is not taken along by the next.
            const int producers = 1;
            const int perProducer = 20000;
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var transport = new RecordingTransport(asyncSendEvery: int.MaxValue);
            channel.SetTransport(transport);

            await RunProducersAsync(producers, async (producer, random) =>
            {
                for (int ii = 0; ii < perProducer; ii++)
                {
                    int sent = transport.SendCount;
                    uint requestId = (uint)((producer * perProducer) + ii + 1);
                    await channel.SendResponseAsync(requestId, CreateResponse(null, 0)).ConfigureAwait(false);

                    // wait for a send, which the writer follows by stopping. Yielding, not
                    // spinning: the writer was queued to this thread's local queue.
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    while (transport.SendCount == sent && DateTime.UtcNow < deadline)
                    {
                        await Task.Yield();
                    }
                    Assert.That(transport.SendCount, Is.GreaterThan(sent), $"response {requestId} was not sent.");
                }
            }).ConfigureAwait(false);

            List<WireChunk> chunks = await transport
                .WaitForFinalChunksAsync(producers * perProducer)
                .ConfigureAwait(false);
            AssertWireOrder(chunks, Enumerable.Range(1, producers * perProducer).Select(id => (uint)id));
            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        /// <summary>
        /// A reconnect while producers keep queuing responses: the OpenSecureChannel
        /// response is the first message on the new transport, and the responses
        /// queued for the dropped transport take no sequence numbers on it.
        /// </summary>
        [Test]
        [Repeat(3)]
        public async Task AReconnectWhileProducersQueueSendsTheOpenResponseFirstAsync()
        {
            const int producers = 8;
            const int perPhase = 40;
            const uint openResponseId = 9_000_000;
            var pool = new CountingPool();
            using OrderServerChannel channel = OrderServerChannel.Create(pool);
            var dropped = new RecordingTransport(asyncSendEvery: 2);
            dropped.HoldSends();
            channel.SetTransport(dropped);
            var replacement = new RecordingTransport(asyncSendEvery: 2);

            int reconnected = 0;
            int beforeDone = 0;
            using var reconnect = new SemaphoreSlim(0);
            var afterIds = new List<uint>();
            await RunProducersAsync(producers + 1, async (producer, random) =>
            {
                if (producer == producers)
                {
                    // reconnect while the first phase is still being queued.
                    while (Volatile.Read(ref beforeDone) < producers / 2)
                    {
                        await Task.Yield();
                    }
                    channel.ReconnectForTest(replacement, openResponseId);
                    Volatile.Write(ref reconnected, 1);
                    dropped.ReleaseSends();
                    return;
                }

                for (int phase = 0; phase < 2; phase++)
                {
                    for (int ii = 0; ii < perPhase; ii++)
                    {
                        uint requestId = (uint)((((producer * 2) + phase) * perPhase) + ii + 1);
                        bool after = Volatile.Read(ref reconnected) == 1;
                        await channel.SendResponseAsync(
                            requestId,
                            CreateResponse(random, random.Next(5) == 0 ? 20000 : null)).ConfigureAwait(false);
                        if (after)
                        {
                            lock (afterIds)
                            {
                                afterIds.Add(requestId);
                            }
                        }
                        if (random.Next(4) == 0)
                        {
                            await Task.Yield();
                        }
                    }

                    if (phase == 0)
                    {
                        Interlocked.Increment(ref beforeDone);
                        while (Volatile.Read(ref reconnected) == 0)
                        {
                            await Task.Yield();
                        }
                    }
                }
            }).ConfigureAwait(false);

            await WaitUntilAsync(() => !channel.HasQueuedWritesForTest).ConfigureAwait(false);

            List<uint> onDropped = AssertContiguous(dropped.SentChunks());
            List<WireChunk> chunks = replacement.SentChunks();
            List<uint> onReplacement = AssertContiguous(chunks);
            Assert.That(chunks[0].RequestId, Is.EqualTo(openResponseId), "the OpenSecureChannel response is not first.");
            Assert.That(onReplacement, Is.SupersetOf(afterIds), "a response queued after the reconnect was lost.");
            Assert.That(onDropped.Intersect(onReplacement), Is.Empty, "a response was sent on both transports.");
            Assert.That(afterIds, Is.Not.Empty);

            await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
        }

        /// <summary>
        /// A message whose security fails after it took sequence numbers, with more
        /// messages behind it in the same batch: those take no numbers and are not
        /// sent, and neither is anything queued later.
        /// </summary>
        [Test]
        public async Task ASealFailureInTheMiddleOfABatchStopsTheNumberingAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var pool = new CountingPool();
            using SealProbe writer = SealProbe.Create(
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, telemetry, pool);
            using SealProbe reader = SealProbe.Create(
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, telemetry, new CountingPool());
            var transport = new RecordingTransport(asyncSendEvery: 1);
            writer.SetTransport(transport);
            var readerTokens = new Dictionary<uint, ChannelToken>();
            try
            {
                SealProbe.Renew(writer, reader, 1, readerTokens);
                writer.Send(1, CreateResponse(null, 10));
                await transport.WaitForMessageCountAsync(1).ConfigureAwait(false);

                // the writer waits on the second send while the next ones queue up.
                transport.HoldSends();
                writer.Send(2, CreateResponse(null, 10));
                await WaitUntilAsync(() => transport.SendCount >= 2).ConfigureAwait(false);
                for (uint requestId = 3; requestId <= 8; requestId++)
                {
                    writer.Send(requestId, CreateResponse(null, requestId % 2 == 0 ? 20000 : 10));
                }

                byte[]? key = writer.BreakEncryptionForTest();
                transport.ReleaseSends();
                await WaitUntilAsync(() => writer.TransportErrorCount > 0).ConfigureAwait(false);
                await WaitUntilAsync(() => !writer.HasPendingWritesForTest).ConfigureAwait(false);

                writer.RestoreEncryptionForTest(key);
                writer.Send(9, CreateResponse(null, 10));
                await WaitUntilAsync(() => !writer.HasPendingWritesForTest).ConfigureAwait(false);

                List<WireChunk> chunks = await transport.WaitForMessageCountAsync(2).ConfigureAwait(false);
                Assert.That(chunks, Has.Count.EqualTo(2), "a message was sent after the failed one.");
                Assert.That(writer.TransportErrorCount, Is.EqualTo(1));
                await AssertBuffersReturnedAsync(pool).ConfigureAwait(false);
            }
            finally
            {
                reader.ReleaseTokens();
                foreach (ChannelToken token in readerTokens.Values)
                {
                    token.Dispose();
                }
            }
        }

        /// <summary>
        /// Checks that the chunks carry consecutive sequence numbers, that the chunks
        /// of a message are contiguous and that no message is complete twice, and
        /// returns the messages in wire order.
        /// </summary>
        private static List<uint> AssertContiguous(List<WireChunk> chunks)
        {
            var completed = new List<uint>();
            if (chunks.Count == 0)
            {
                return completed;
            }

            var seen = new HashSet<uint>();
            uint? open = null;
            for (int ii = 0; ii < chunks.Count; ii++)
            {
                WireChunk chunk = chunks[ii];
                if (ii > 0)
                {
                    Assert.That(
                        chunk.SequenceNumber,
                        Is.EqualTo(chunks[ii - 1].SequenceNumber + 1),
                        $"chunk {ii} (request {chunk.RequestId}) is out of sequence.");
                }
                if (open != null)
                {
                    Assert.That(chunk.RequestId, Is.EqualTo(open.Value), $"chunk {ii} interleaves request {chunk.RequestId}.");
                }
                if (chunk.IsFinal || chunk.IsAbort)
                {
                    Assert.That(seen.Add(chunk.RequestId), Is.True, $"request {chunk.RequestId} was sent twice.");
                    completed.Add(chunk.RequestId);
                    open = null;
                }
                else
                {
                    open = chunk.RequestId;
                }
            }

            Assert.That(open, Is.Null, "the last message is incomplete.");
            return completed;
        }

        /// <summary>
        /// Checks that the chunks carry consecutive sequence numbers, that the chunks
        /// of a message are contiguous, and that every expected message is complete
        /// exactly once.
        /// </summary>
        private static void AssertWireOrder(List<WireChunk> chunks, IEnumerable<uint> expectedRequestIds)
        {
            Assert.That(chunks, Is.Not.Empty);
            var completed = new HashSet<uint>();
            uint? open = null;
            for (int ii = 0; ii < chunks.Count; ii++)
            {
                WireChunk chunk = chunks[ii];
                if (ii > 0)
                {
                    Assert.That(
                        chunk.SequenceNumber,
                        Is.EqualTo(chunks[ii - 1].SequenceNumber + 1),
                        $"chunk {ii} (request {chunk.RequestId}) is out of sequence.");
                }

                if (open != null)
                {
                    Assert.That(
                        chunk.RequestId,
                        Is.EqualTo(open.Value),
                        $"chunk {ii} interleaves request {chunk.RequestId} into request {open}.");
                }

                if (chunk.IsFinal || chunk.IsAbort)
                {
                    Assert.That(completed.Add(chunk.RequestId), Is.True, $"request {chunk.RequestId} was sent twice.");
                    open = null;
                }
                else
                {
                    open = chunk.RequestId;
                }
            }

            Assert.That(open, Is.Null, "the last message is incomplete.");
            Assert.That(completed, Is.EquivalentTo(expectedRequestIds));
        }

        private static async Task AssertBuffersReturnedAsync(CountingPool pool)
        {
            await WaitUntilAsync(() => pool.OutstandingCount == 0).ConfigureAwait(false);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5).ConfigureAwait(false);
            }
            Assert.That(condition(), Is.True, "the condition was not reached in time.");
        }

        private static async Task RunProducersAsync(int count, Func<int, Random, Task> producer)
        {
            using var start = new ManualResetEventSlim();
            Task[] tasks = [.. Enumerable.Range(0, count).Select(index => Task.Run(async () =>
            {
                start.Wait();
                await producer(index, new Random(index + 1)).ConfigureAwait(false);
            }))];
            start.Set();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Decodes the body of a single-chunk message sent on a None channel.
        /// </summary>
        private static IServiceResponse DecodeBody(OrderServerChannel channel, WireChunk chunk)
        {
            const int headerSize = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
            Assert.That(chunk.IsFinal, Is.True);
            using var stream = new System.IO.MemoryStream(chunk.Bytes, headerSize, chunk.Bytes.Length - headerSize);
            return BinaryDecoder.DecodeMessage<IServiceResponse>(stream, channel.MessageContextForTest);
        }

        private static ReadResponse CreateResponse(Random? random, int? payloadSize = null)
        {
            int size = payloadSize ??
                (random!.Next(4) == 0 ? random.Next(7000, 26000) : random.Next(0, 200));
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good
                },
                Results =
                [
                    new DataValue(new Variant(new ByteString(new byte[size])), StatusCodes.Good)
                ]
            };
        }

        /// <summary>
        /// The header fields of one chunk as it was sent.
        /// </summary>
        private sealed record WireChunk(
            uint MessageType,
            uint TokenId,
            uint SequenceNumber,
            uint RequestId,
            byte[] Bytes)
        {
            public bool IsFinal => TcpMessageType.IsFinal(MessageType);

            public bool IsAbort => TcpMessageType.IsAbort(MessageType);
        }

        /// <summary>
        /// A server channel opened with a None token, as the buffer tests use.
        /// </summary>
        private sealed class OrderServerChannel : TcpServerChannel
        {
            private OrderServerChannel(
                ITcpChannelListener listener,
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry)
                : base(
                    nameof(UaSCBinaryChannelSendOrderTests),
                    listener,
                    bufferManager,
                    quotas,
                    null!,
                    [],
                    telemetry,
                    new FakeTimeProvider(DateTimeOffset.UtcNow))
            {
            }

            public static OrderServerChannel Create(CountingPool pool, int maxByteStringLength = 0)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var context = ServiceMessageContext.Create(telemetry);
                if (maxByteStringLength > 0)
                {
                    context.MaxByteStringLength = maxByteStringLength;
                }
                var quotas = new ChannelQuotas(context)
                {
                    MaxBufferSize = 8192,
                    MaxMessageSize = 4 * 1024 * 1024
                };
                var manager = new BufferManager(
                    nameof(UaSCBinaryChannelSendOrderTests),
                    quotas.MaxBufferSize,
                    telemetry,
                    pool);
                var listener = new Mock<ITcpChannelListener>();
                listener.SetupGet(value => value.EndpointUrl)
                    .Returns(new Uri("opc.tcp://localhost:4840"));
                var channel = new OrderServerChannel(listener.Object, manager, quotas, telemetry);
                channel.ChannelId = 1;
                channel.State = TcpChannelState.Open;
                channel.LoadTokenForTest(1);
                return channel;
            }

            public bool HasQueuedWritesForTest => HasPendingWrites;

            public IServiceMessageContext MessageContextForTest => Quotas.MessageContext;

            public void SetTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            public void StartReceiveLoopForTest()
            {
                StartReceiveLoop();
            }

            /// <summary>
            /// Replaces the tokens without the activation path, or removes them.
            /// </summary>
            public void LoadTokenForTest(uint? tokenId)
            {
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(
                    tokenId == null ? null : NewToken(tokenId.Value),
                    previous: null);
            }

            public uint CurrentTokenIdForTest => CurrentToken!.TokenId;

            public uint NewestTokenIdForTest => (RenewedToken ?? CurrentToken)!.TokenId;

            /// <summary>
            /// Issues a renewed token as answering a renewal request does; the receive
            /// loop activates it when a request secured with it arrives.
            /// </summary>
            public void RenewTokenForTest(uint tokenId)
            {
                using (Gate.Enter())
                {
                    SetRenewedToken(NewToken(tokenId));
                }
            }

            /// <summary>
            /// Queues a message numbered by its producer under the send order lock,
            /// the way an asymmetric message is.
            /// </summary>
            public void QueueNumberedMessageForTest(uint requestId)
            {
                using (SendOrder.Enter())
                {
                    QueueNumberedMessage(requestId);
                }
            }

            /// <summary>
            /// Attaches a new transport and queues the OpenSecureChannel response on it
            /// under one hold of the send order lock, as a reconnect does.
            /// </summary>
            public void ReconnectForTest(IUaSCByteTransport transport, uint openResponseId)
            {
                using (SendOrder.Enter())
                {
                    Transport = transport;
                    QueueNumberedMessage(openResponseId);
                }
            }

            /// <summary>
            /// Activates a new token as the receive loop does when a request secured
            /// with a renewed token arrives.
            /// </summary>
            public void ActivateTokenForTest(uint tokenId)
            {
                using (Gate.Enter())
                {
                    ActivateToken(NewToken(tokenId));
                }
            }

            private void QueueNumberedMessage(uint requestId)
            {
                const int size = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
                byte[] buffer = BufferManager.TakeBuffer(size, nameof(QueueNumberedMessageForTest));
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), TcpMessageType.Message | TcpMessageType.Final);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), size);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), ChannelId);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), CurrentToken!.TokenId);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), GetNewSequenceNumber());
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), requestId);
                BeginWriteMessage(new ArraySegment<byte>(buffer, 0, size), null);
            }

            /// <summary>
            /// Encodes a request chunk the None channel reads.
            /// </summary>
            public ArraySegment<byte> CreateRequestChunkForTest(uint requestId, uint sequenceNumber, uint? tokenId = null)
            {
                using var body = new System.IO.MemoryStream();
                BinaryEncoder.EncodeMessage(new ReadRequest(), body, Quotas.MessageContext, true);
                int length = (int)body.Length + 24;
                byte[] buffer = BufferManager.TakeBuffer(length, nameof(CreateRequestChunkForTest));
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), TcpMessageType.Message | TcpMessageType.Final);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)length);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), ChannelId);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), tokenId ?? CurrentToken!.TokenId);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), sequenceNumber);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), requestId);
                body.GetBuffer().AsSpan(0, (int)body.Length).CopyTo(buffer.AsSpan(24));
                return new ArraySegment<byte>(buffer, 0, length);
            }

            private ChannelToken NewToken(uint tokenId)
            {
                return new ChannelToken
                {
                    ChannelId = ChannelId,
                    TokenId = tokenId,
                    SecurityPolicy = SecurityPolicyInfo.None,
                    CreatedAt = DateTime.UtcNow,
                    CreatedAtTimestamp = TimeProvider.GetTimestamp(),
                    Lifetime = 600000
                };
            }
        }

        /// <summary>
        /// A channel with real keys: one instance writes through the writer, a
        /// second one reads what it sent, as the peer does.
        /// </summary>
        private sealed class SealProbe : UaSCUaBinaryChannel
        {
            private SealProbe(
                BufferManager buffers,
                ChannelQuotas quotas,
                string policyUri,
                MessageSecurityMode mode,
                ITelemetryContext telemetry)
                : base(
                    "send-order",
                    buffers,
                    quotas,
                    (Certificate?)null,
                    null,
                    mode,
                    policyUri,
                    telemetry)
            {
                Buffers = buffers;
                ChannelId = 7;
            }

            public BufferManager Buffers { get; }

            public static SealProbe Create(
                string policyUri,
                MessageSecurityMode mode,
                ITelemetryContext telemetry,
                CountingPool pool)
            {
                var quotas = new ChannelQuotas(ServiceMessageContext.Create(telemetry))
                {
                    MaxBufferSize = 8192,
                    MaxMessageSize = 4 * 1024 * 1024
                };
                var buffers = new BufferManager("send-order", quotas.MaxBufferSize, telemetry, pool);
                return new SealProbe(buffers, quotas, policyUri, mode, telemetry);
            }

            /// <summary>
            /// Agrees a token on both sides; the writer activates it while it may be
            /// sealing, the reader only keeps it.
            /// </summary>
            public static void Renew(
                SealProbe writer,
                SealProbe reader,
                uint tokenId,
                Dictionary<uint, ChannelToken> readerTokens)
            {
                byte[] readerNonce = reader.CreateNonce(null)!;
                byte[] writerNonce = writer.CreateNonce(null)!;
                Assert.That(reader.ValidateNonce(null, writerNonce), Is.True);
                Assert.That(writer.ValidateNonce(null, readerNonce), Is.True);

                ChannelToken readerToken = reader.NewToken(tokenId, readerNonce, writerNonce, reader.m_lastSecret);
                reader.ComputeKeys(readerToken);
                reader.m_lastSecret = readerToken.Secret;
                readerTokens[tokenId] = readerToken;

                ChannelToken writerToken = writer.NewToken(tokenId, readerNonce, writerNonce, writer.m_lastSecret);
                writer.ActivateToken(writerToken);
                writer.m_lastSecret = writerToken.Secret;
            }

            public void SetTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            public uint TakeSequenceNumber()
            {
                return GetNewSequenceNumber();
            }

            public bool AcceptSequenceNumber(uint sequenceNumber)
            {
                return VerifySequenceNumber(sequenceNumber, "test");
            }

            public void Send(uint requestId, IServiceResponse response)
            {
                EncodedSymmetricMessage encoded = EncodeSymmetricMessage(
                    TcpMessageType.Message,
                    requestId,
                    response,
                    false,
                    out bool limitsExceeded);
                Assert.That(limitsExceeded, Is.False);
                Assert.That(BeginWriteSymmetricMessage(encoded, null), Is.True);
            }

            /// <summary>
            /// Verifies and decrypts a chunk with the token it names, and checks its
            /// sequence number follows the last one read.
            /// </summary>
            public WireChunk Read(WireChunk chunk, ChannelToken token)
            {
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(token, previous: null);
                byte[] copy = Buffers.TakeBuffer(chunk.Bytes.Length, "test");
                try
                {
                    Buffer.BlockCopy(chunk.Bytes, 0, copy, 0, chunk.Bytes.Length);
                    ReadSymmetricMessage(
                        new ArraySegment<byte>(copy, 0, chunk.Bytes.Length),
                        false,
                        out ChannelToken used,
                        out uint requestId,
                        out uint sequenceNumber);
                    Assert.That(used.TokenId, Is.EqualTo(chunk.TokenId));
                    Assert.That(
                        VerifySequenceNumber(sequenceNumber, "test"),
                        Is.True,
                        $"sequence number {sequenceNumber} does not follow on.");
                    return chunk with { SequenceNumber = sequenceNumber, RequestId = requestId };
                }
                finally
                {
                    Buffers.ReturnBuffer(copy, "test");
                }
            }

            /// <summary>
            /// Forgets the tokens the reader borrowed, so disposing it does not
            /// dispose them.
            /// </summary>
            /// <summary>
            /// Number of transport errors the channel reported.
            /// </summary>
            public int TransportErrorCount => Volatile.Read(ref m_transportErrors);

            public bool HasPendingWritesForTest => HasPendingWrites;

            /// <summary>
            /// Replaces the current token's encrypting key with one AES rejects, so the
            /// next message fails after its chunks took their sequence numbers.
            /// </summary>
            /// <summary>
            /// Numbers and queues a message under the send order lock, as the producers
            /// of OpenSecureChannel responses and faults do.
            /// </summary>
            public void QueueNumberedMessageForTest(uint requestId)
            {
                const int size = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
                using (SendOrder.Enter())
                {
                    byte[] buffer = BufferManager.TakeBuffer(size, nameof(QueueNumberedMessageForTest));
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), TcpMessageType.Message | TcpMessageType.Final);
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), size);
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), ChannelId);
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), CurrentToken!.TokenId);
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), GetNewSequenceNumber());
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), requestId);
                    BeginWriteMessage(new ArraySegment<byte>(buffer, 0, size), null);
                }
            }

            public byte[]? BreakEncryptionForTest()
            {
                byte[]? key = CurrentToken!.ServerEncryptingKey;
                CurrentToken!.ServerEncryptingKey = new byte[3];
                return key;
            }

            public void RestoreEncryptionForTest(byte[]? key)
            {
                CurrentToken!.ServerEncryptingKey = key;
            }

            protected override void HandleSocketError(ServiceResult result)
            {
                Interlocked.Increment(ref m_transportErrors);
            }

            private int m_transportErrors;

            public void ReleaseTokens()
            {
                ((IDiagnosticsChannelMutation)this).LoadTokensForOfflineDecode(null, previous: null);
            }

            private ChannelToken NewToken(uint tokenId, byte[] clientNonce, byte[] serverNonce, byte[]? previousSecret)
            {
                ChannelToken token = CreateToken();
                token.TokenId = tokenId;
                token.ChannelId = ChannelId;
                token.ClientNonce = clientNonce;
                token.ServerNonce = serverNonce;
                token.PreviousSecret = previousSecret;
                token.Lifetime = 600000;
                return token;
            }

            private byte[]? m_lastSecret;
        }

        /// <summary>
        /// Records every send in order, completing some of them asynchronously, and
        /// serves request chunks to a receive loop.
        /// </summary>
        private sealed class RecordingTransport : IUaSCByteTransport, IUaSCChunkCoalescing
        {
            public RecordingTransport(int asyncSendEvery)
            {
                m_asyncSendEvery = asyncSendEvery;
            }

            public EndPoint LocalEndpoint => null!;

            public EndPoint RemoteEndpoint => null!;

            public TransportChannelFeatures Features => default;

            public string Implementation => "UA-RECORDING";

            public bool CanCoalesceChunks => true;

            public Task FirstSendStarted => m_firstSendStarted.Task;

            public int ReceiveCalls => Volatile.Read(ref m_receiveCalls);

            /// <summary>
            /// The number of sends started.
            /// </summary>
            public int SendCount => Volatile.Read(ref m_recorded);

            /// <summary>
            /// The chunks sent so far, with clear headers.
            /// </summary>
            public List<WireChunk> SentChunks()
            {
                return Parse(clearHeaders: true);
            }

            public void HoldSends()
            {
                m_hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void ReleaseSends()
            {
                m_hold?.TrySetResult(true);
            }

            public void Feed(ArraySegment<byte> chunk)
            {
                Feed(() => chunk);
            }

            /// <summary>
            /// Queues a chunk that is created when the receive loop reads it.
            /// </summary>
            public void Feed(Func<ArraySegment<byte>> chunk)
            {
                lock (m_lock)
                {
                    m_incoming.Enqueue(chunk);
                }
            }

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                Record(chunk.ToArray());
                return CompleteAsync();
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                byte[] bytes = new byte[buffers.TotalSize];
                int offset = 0;
                foreach (ArraySegment<byte> buffer in buffers)
                {
                    Buffer.BlockCopy(buffer.Array!, buffer.Offset, bytes, offset, buffer.Count);
                    offset += buffer.Count;
                }
                Record(bytes);
                return CompleteAsync();
            }

            public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                Interlocked.Increment(ref m_receiveCalls);
                while (true)
                {
                    Func<ArraySegment<byte>>? next = null;
                    lock (m_lock)
                    {
                        if (m_incoming.Count > 0)
                        {
                            next = m_incoming.Dequeue();
                        }
                    }
                    if (next != null)
                    {
                        return next();
                    }
                    await Task.Delay(5, ct).ConfigureAwait(false);
                }
            }

            public void Close()
            {
                ReleaseSends();
            }

            public void Dispose()
            {
                Close();
            }

            /// <summary>
            /// Waits until the given number of final chunks was sent, and returns all
            /// chunks sent so far.
            /// </summary>
            public async Task<List<WireChunk>> WaitForFinalChunksAsync(int count)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(60);
                while (true)
                {
                    List<WireChunk> chunks = Parse(clearHeaders: true);
                    int finals = chunks.Count(chunk => chunk.IsFinal || chunk.IsAbort);
                    if (finals >= count || DateTime.UtcNow > deadline)
                    {
                        Assert.That(finals, Is.EqualTo(count), "an unexpected number of messages was sent.");
                        return chunks;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// For encrypted chunks, whose sequence header cannot be read here: waits
            /// until the given number of final chunks was sent.
            /// </summary>
            public async Task<List<WireChunk>> WaitForMessageCountAsync(int count)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(60);
                while (true)
                {
                    List<WireChunk> chunks = Parse(clearHeaders: false);
                    int finals = chunks.Count(chunk => chunk.IsFinal || chunk.IsAbort);
                    if (finals >= count || DateTime.UtcNow > deadline)
                    {
                        Assert.That(finals, Is.EqualTo(count), "an unexpected number of messages was sent.");
                        return chunks;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
            }

            private List<WireChunk> Parse(bool clearHeaders)
            {
                byte[] stream;
                lock (m_lock)
                {
                    stream = [.. m_sent];
                }

                var chunks = new List<WireChunk>();
                int offset = 0;
                while (offset + 24 <= stream.Length)
                {
                    uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset));
                    int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset + 4));
                    uint tokenId = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset + 12));
                    uint sequenceNumber = clearHeaders
                        ? BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset + 16))
                        : 0;
                    uint requestId = clearHeaders
                        ? BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset + 20))
                        : 0;
                    chunks.Add(new WireChunk(
                        messageType,
                        tokenId,
                        sequenceNumber,
                        requestId,
                        stream.AsSpan(offset, size).ToArray()));
                    offset += size;
                }
                return chunks;
            }

            private void Record(byte[] bytes)
            {
                lock (m_lock)
                {
                    m_sent.AddRange(bytes);
                }
                Interlocked.Increment(ref m_recorded);
                m_firstSendStarted.TrySetResult(true);
            }

            private ValueTask CompleteAsync()
            {
                TaskCompletionSource<bool>? hold = m_hold;
                if (hold != null && !hold.Task.IsCompleted)
                {
                    return new ValueTask(hold.Task);
                }

                if (Interlocked.Increment(ref m_sends) % m_asyncSendEvery == 0)
                {
                    return YieldAsync();
                }
                return default;

                static async ValueTask YieldAsync()
                {
                    await Task.Yield();
                }
            }

            private readonly int m_asyncSendEvery;
            private readonly Lock m_lock = new();
            private readonly List<byte> m_sent = [];
            private readonly Queue<Func<ArraySegment<byte>>> m_incoming = new();
            private readonly TaskCompletionSource<bool> m_firstSendStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TaskCompletionSource<bool>? m_hold;
            private int m_sends;
            private int m_recorded;
            private int m_receiveCalls;
        }

        /// <summary>
        /// Counts outstanding rentals and returns of arrays that were not rented.
        /// </summary>
        private sealed class CountingPool : ArrayPool<byte>
        {
            public override byte[] Rent(int minimumLength)
            {
                byte[] buffer = new byte[minimumLength];
                lock (m_lock)
                {
                    m_outstanding.Add(buffer);
                }
                return buffer;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                lock (m_lock)
                {
                    if (!m_outstanding.Remove(array))
                    {
                        DuplicateReturnCount++;
                    }
                }
            }

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
