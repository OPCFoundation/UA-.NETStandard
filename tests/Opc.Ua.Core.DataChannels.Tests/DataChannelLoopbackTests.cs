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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Drives two data channel managers against each other over an
    /// in-memory transport, so the bootstrap, scheduling, delivery and
    /// close paths are exercised together rather than in isolation.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public class DataChannelLoopbackTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("loopback", 65536, m_telemetry);

            m_serverTransport = new LoopbackTransport(m_bufferManager, TimeProvider.System);
            m_clientTransport = new LoopbackTransport(m_bufferManager, TimeProvider.System);

            m_server = new DataChannelManager(m_serverTransport, true, m_telemetry);
            m_client = new DataChannelManager(m_clientTransport, false, m_telemetry);

            m_serverTransport.Peer = m_client;
            m_clientTransport.Peer = m_server;
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_server.DisposeAsync().ConfigureAwait(false);
            await m_client.DisposeAsync().ConfigureAwait(false);
        }

        // DCF-012: a sender does not transmit before connection credit
        // arrives, even though its channel window is healthy.
        [Test]
        public async Task DcF012NoDataBeforeConnectionCreditArrives()
        {
            m_serverTransport.DropOutbound = true;
            m_clientTransport.DropOutbound = true;

            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);

            source.Write(new byte[64], DataChannelFrameFlags.MessageStart);

            await WaitForAsync(() => m_serverTransport.Sent.Count > 0, expectTrue: false)
                .ConfigureAwait(false);

            Assert.That(
                m_serverTransport.CountOf(DataChannelFrameType.Data),
                Is.Zero,
                "no DATA frame may be transmitted until the peer has granted connection credit");
        }

        [Test]
        public async Task PayloadFlowsEndToEndOnceCreditIsGranted()
        {
            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);
            DataChannel sink = m_client.Channels[0];

            byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];

            source.Write(
                payload,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd);

            using DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                .ConfigureAwait(false);

            Assert.That(message, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(payload));
                Assert.That(message.IsMessageStart, Is.True);
                Assert.That(message.IsMessageEnd, Is.True);
                Assert.That(message.FrameSequenceNumber, Is.EqualTo(1u));
                Assert.That(message.Status, Is.EqualTo((StatusCode)StatusCodes.Good));
            });
        }

        [Test]
        public async Task FramesArriveInAscendingSequenceOrder()
        {
            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);
            DataChannel sink = m_client.Channels[0];

            for (int ii = 0; ii < 16; ii++)
            {
                source.Write([(byte)ii], DataChannelFrameFlags.MessageStart);
            }

            for (uint expected = 1; expected <= 16; expected++)
            {
                using DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                    .ConfigureAwait(false);

                Assert.That(message, Is.Not.Null, $"frame {expected} was never delivered");
                Assert.That(message!.FrameSequenceNumber, Is.EqualTo(expected));
                Assert.That(message.Payload.Span[0], Is.EqualTo((byte)(expected - 1)));
            }
        }

        // DCF-014: control frames flow while the credit window is zero.
        [Test]
        public async Task DcF014PingIsAnsweredWhileCreditIsExhausted()
        {
            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);

            Assert.That(source.TryPing(), Is.True);

            await WaitForAsync(
                () => m_clientTransport.CountOf(DataChannelFrameType.Pong) > 0)
                .ConfigureAwait(false);

            Assert.That(
                m_clientTransport.CountOf(DataChannelFrameType.Pong),
                Is.EqualTo(1),
                "a receiver answers PING even while it is withholding credit");
        }

        // DCF-026: a sender bounds its own PING rate to one per channel
        // per second, and never a second while one is unanswered.
        [Test]
        public void DcF026PingRateIsBounded()
        {
            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);

            Assert.Multiple(() =>
            {
                Assert.That(source.TryPing(), Is.True);
                Assert.That(
                    source.TryPing(),
                    Is.False,
                    "never a second PING while one is unanswered");
            });
        }

        // DCF-028: receiving END marks only the peer's direction ended.
        // The receiver's own direction is untouched, which is what makes
        // END a half close rather than a close.
        [Test]
        public async Task DcF028ReceivingEndDoesNotCloseTheReceiversOwnDirection()
        {
            DataChannel client = OpenPair(DataChannelDirection.Bidirectional, fromServer: false);
            DataChannel server = m_server.Channels[0];

            client.Close();

            await WaitForAsync(
                () => m_clientTransport.CountOf(DataChannelFrameType.End) > 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    server.State,
                    Is.Not.EqualTo(DataChannelState.Faulted),
                    "the peer's half close must not fault this end");
                Assert.That(
                    () => server.Write(new byte[8], DataChannelFrameFlags.MessageStart),
                    Throws.Nothing,
                    "this end may keep sending after the peer half closed");
            });
        }

        // DCF-029: a RESET carrying Good closes rather than faults, on
        // both peers. The StatusCode is the only wire signal that
        // distinguishes the two.
        [Test]
        public async Task DcF029ResetCarryingGoodClosesBothPeers()
        {
            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);
            DataChannel sink = m_client.Channels[0];

            source.Reset(StatusCodes.Good);

            await WaitForAsync(() => sink.State == DataChannelState.Closed)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(source.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(sink.State, Is.EqualTo(DataChannelState.Closed));
            });
        }

        [Test]
        public async Task ResetCarryingABadStatusFaultsBothPeers()
        {
            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);
            DataChannel sink = m_client.Channels[0];

            source.Reset(StatusCodes.BadDataChannelClosed);

            await WaitForAsync(() => sink.State == DataChannelState.Faulted)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(source.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(sink.State, Is.EqualTo(DataChannelState.Faulted));
            });
        }

        // DCF-022: DATA in a direction the channel forbids is rejected,
        // and the channel is reset rather than the connection closed.
        [Test]
        public async Task DcF022DataFromTheSinkOnASourceToSinkChannelIsRejected()
        {
            DataChannel source = OpenPair(DataChannelDirection.SourceToSink);
            DataChannel sink = m_client.Channels[0];

            // The sink bypasses its own direction check and puts a DATA
            // frame on the wire, which is what a non conforming peer
            // would do.
            await m_clientTransport.SendFrameAsync(
                DataChannelFrame.Data(sink.ChannelId, 1, DataChannelFrameFlags.MessageStart, new byte[4]),
                default).ConfigureAwait(false);

            await WaitForAsync(() => source.State == DataChannelState.Faulted)
                .ConfigureAwait(false);

            Assert.That(
                source.State,
                Is.EqualTo(DataChannelState.Faulted),
                "the receiver resets the channel; the SecureChannel is untouched");
        }

        // DCF-025: a ChannelId is never reassigned while the SecureChannel
        // that owns it is open.
        [Test]
        public void DcF025ChannelIdsAreNeverReused()
        {
            var seen = new System.Collections.Generic.HashSet<uint>();

            for (int ii = 0; ii < 8; ii++)
            {
                Assert.That(m_server.TryAllocateChannelId(out uint channelId), Is.True);
                Assert.That(seen.Add(channelId), Is.True, $"ChannelId {channelId} was reused");

                DataChannel channel = m_server.Register(
                    channelId,
                    new NodeId(1u),
                    new DataChannelSettings(),
                    isSource: true);

                m_server.MarkOpen(channelId);
                channel.Reset(StatusCodes.Good);
                m_server.Remove(channelId);
            }

            Assert.That(seen, Has.Count.EqualTo(8));
        }

        // DCF-013: a receiver replenishes credit once it has consumed
        // payload and released the buffer holding it.
        [Test]
        public async Task DcF013ReceiverReplenishesCreditOnRelease()
        {
            DataChannel source = OpenPair(
                DataChannelDirection.SourceToSink,
                initialCredit: 512,
                maxFrameSize: 128);

            DataChannel sink = m_client.Channels[0];

            int creditsBefore = m_clientTransport.CountOf(DataChannelFrameType.Credit);

            for (int ii = 0; ii < 4; ii++)
            {
                source.Write(new byte[128], DataChannelFrameFlags.MessageStart);
            }

            for (int ii = 0; ii < 4; ii++)
            {
                using DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                    .ConfigureAwait(false);

                Assert.That(message, Is.Not.Null);
            }

            await WaitForAsync(
                () => m_clientTransport.CountOf(DataChannelFrameType.Credit) > creditsBefore)
                .ConfigureAwait(false);

            Assert.That(
                m_clientTransport.CountOf(DataChannelFrameType.Credit),
                Is.GreaterThan(creditsBefore),
                "releasing consumed payload obliges a CREDIT frame");
        }

        [Test]
        public async Task ConnectionCreditIsGrantedByBothPeersWithoutSolicitation()
        {
            OpenPair(DataChannelDirection.SourceToSink);

            await WaitForAsync(
                () => m_serverTransport.CountOf(DataChannelFrameType.Credit) > 0 &&
                      m_clientTransport.CountOf(DataChannelFrameType.Credit) > 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    m_serverTransport.CountOf(DataChannelFrameType.Credit),
                    Is.GreaterThan(0),
                    "the server grants without waiting to be asked");
                Assert.That(
                    m_clientTransport.CountOf(DataChannelFrameType.Credit),
                    Is.GreaterThan(0),
                    "and so does the client, because the grant flows opposite to the data");
            });
        }

        /// <summary>
        /// The scheduler's deficit bounds how much a channel may send in
        /// one round, not how often rounds happen. A round that leaves
        /// payload queued has to schedule the next one immediately.
        /// </summary>
        /// <remarks>
        /// Regression: the loop originally waited for its idle tick
        /// between rounds, which capped a channel at one quantum per tick
        /// - about fifty frames a second, useless for media. The sample
        /// measured 0.5 Mbit/s before the fix and 1.3 Gbit/s after it, so
        /// this test fails loudly rather than merely slowly if the wake
        /// is ever dropped again.
        /// </remarks>
        [Test]
        public async Task ManyFramesDrainWithoutWaitingForTheIdleTick()
        {
            const int frameCount = 200;
            const int frameSize = 512;

            DataChannel source = OpenPair(
                DataChannelDirection.SourceToSink,
                initialCredit: frameCount * frameSize * 2,
                maxFrameSize: frameSize);

            DataChannel sink = m_client.Channels[0];

            var stopwatch = Stopwatch.StartNew();

            for (int ii = 0; ii < frameCount; ii++)
            {
                source.Write(new byte[frameSize], DataChannelFrameFlags.MessageStart);
            }

            for (int ii = 0; ii < frameCount; ii++)
            {
                using DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                    .ConfigureAwait(false);

                Assert.That(message, Is.Not.Null, $"frame {ii + 1} was never delivered");
            }

            stopwatch.Stop();

            // One quantum per 20 ms tick would need four seconds for two
            // hundred frames. Anything near that means the wake was lost.
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(2)),
                "a queued channel must not wait for the idle tick between rounds");
        }

        private DataChannel OpenPair(
            DataChannelDirection direction,
            bool fromServer = true,
            uint initialCredit = 65536,
            uint maxFrameSize = 4096)
        {
            var settings = new DataChannelSettings
            {
                Direction = direction,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = maxFrameSize,
                InitialCredit = initialCredit
            };

            Assert.That(m_server.TryAllocateChannelId(out uint channelId), Is.True);

            DataChannel server = m_server.Register(
                channelId,
                new NodeId(1u),
                settings,
                isSource: true);

            DataChannel client = m_client.Register(
                channelId,
                new NodeId(1u),
                settings,
                isSource: false);

            m_server.MarkOpen(channelId);
            m_client.MarkOpen(channelId);

            return fromServer ? server : client;
        }

        private static async Task<DataChannelMessage?> ReadWithTimeoutAsync(DataChannel channel)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                return await channel.ReadAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private static async Task WaitForAsync(Func<bool> condition, bool expectTrue = true)
        {
            var stopwatch = Stopwatch.StartNew();
            TimeSpan limit = expectTrue ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1);

            while (stopwatch.Elapsed < limit)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;
        private LoopbackTransport m_serverTransport = null!;
        private LoopbackTransport m_clientTransport = null!;
        private DataChannelManager m_server = null!;
        private DataChannelManager m_client = null!;
    }
}
