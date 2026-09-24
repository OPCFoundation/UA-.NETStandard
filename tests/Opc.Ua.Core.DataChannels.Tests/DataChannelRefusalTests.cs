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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Covers the refusals a channel makes: what the local application is not
    /// allowed to enqueue, and what a peer is not allowed to send. These are
    /// the paths that stop a misbehaving peer from consuming resources, so a
    /// silent success on any of them is a fault that would only show up under
    /// attack.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class DataChannelRefusalTests
    {
        [SetUp]
        public void SetUp()
        {
            m_timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 7, 29, 5, 0, 0, TimeSpan.Zero));
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("refusal", 65536, m_telemetry);
            m_transport = new RecordingTransport(m_bufferManager, m_timeProvider);
            m_manager = new DataChannelManager(
                m_transport,
                isServer: true,
                m_telemetry,
                maxDataChannels: 8,
                maxCreditPerChannel: 1024 * 1024);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_manager != null)
            {
                await m_manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// §5.3: this peer is the sink of a SinkToSource channel's reverse
        /// direction, so it may not enqueue payload at all.
        /// </summary>
        [Test]
        public void WriteInADirectionThisPeerDoesNotSendIsRefused()
        {
            DataChannel channel = Register(direction: DataChannelDirection.SinkToSource);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => channel.Write([1, 2, 3], DataChannelFrameFlags.MessageStart))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadDataChannelDirectionUnsupported));
        }

        /// <summary>
        /// §5.13: once this peer has decided to close its direction, nothing
        /// more may be enqueued on it. The queue already there still drains.
        /// </summary>
        [Test]
        public void WriteAfterTheLocalDirectionIsClosingIsRefused()
        {
            DataChannel channel = Register();
            channel.Close();

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => channel.Write([1, 2, 3], DataChannelFrameFlags.MessageStart))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDataChannelClosed));

                // On a SourceToSink channel the reverse direction counted as
                // ended at open, so the single END this peer owes completes
                // the close as soon as the scheduler drains it - the channel
                // is therefore Closing or already Closed, never Open.
                Assert.That(
                    channel.State,
                    Is.AnyOf(DataChannelState.Closing, DataChannelState.Closed));
            });
        }

        /// <summary>
        /// Droppable is meaningless without a delivery mode that permits
        /// discard and a deadline to discard against.
        /// </summary>
        [Test]
        public void WriteOfADroppableFrameOnAReliableChannelIsRefused()
        {
            DataChannel channel = Register(deliveryMode: DataChannelDeliveryMode.ReliableOrdered);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => channel.Write(
                    [1, 2, 3],
                    DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.Droppable))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadDeliveryModeUnsupported));
        }

        /// <summary>
        /// The negotiated MaxFrameSize binds the sender too, so payload that
        /// would not fit is refused before it is queued rather than after the
        /// peer resets the channel for it.
        /// </summary>
        [Test]
        public void WriteOfPayloadLargerThanMaxFrameSizeIsRefused()
        {
            DataChannel channel = Register(maxFrameSize: 64);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => channel.Write(new byte[65], DataChannelFrameFlags.MessageStart))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadDataChannelLimitsExceeded));
        }

        /// <summary>
        /// The flag bits held back for a future revision shall be zero, so a
        /// sender cannot claim a semantic this revision does not define.
        /// </summary>
        [Test]
        public void WriteWithAReservedFlagBitIsRefused()
        {
            DataChannel channel = Register();

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => channel.Write(
                    [1],
                    (DataChannelFrameFlags)DataChannelConstants.ReservedFlagMask))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// §5.13: END ends the peer's direction. DATA after it is a protocol
        /// error, not late payload, because the sequence space that would
        /// order it has been closed.
        /// </summary>
        [Test]
        public async Task DataArrivingAfterThePeerSentEndResetsTheChannelAsync()
        {
            DataChannel channel = Register(direction: DataChannelDirection.Bidirectional);

            m_manager!.HandleFrame(DataChannelFrame.End(ChannelId, 1));
            m_manager.HandleFrame(Data(2, [1, 2, 3]));

            await WaitForAsync(() => ResetFrames().Count > 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    ResetFrames().Select(frame => frame.Status.Code),
                    Does.Contain((uint)StatusCodes.BadDataChannelClosed));
            });
        }

        /// <summary>
        /// §5.3: a peer sending payload in a direction the negotiated
        /// Direction does not permit is reset rather than delivered.
        /// </summary>
        [Test]
        public async Task DataArrivingInADirectionThePeerMayNotSendResetsTheChannelAsync()
        {
            // This peer is the source of a SourceToSink channel, so the peer
            // sending this DATA is the sink and may send none.
            DataChannel channel = Register(direction: DataChannelDirection.SourceToSink);

            m_manager!.HandleFrame(Data(1, [1, 2, 3]));

            await WaitForAsync(() => ResetFrames().Count > 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    ResetFrames().Select(frame => frame.Status.Code),
                    Does.Contain((uint)StatusCodes.BadDataChannelDirectionUnsupported));
            });
        }

        /// <summary>
        /// A peer that sends more than the credit it was granted is reset.
        /// Without this the receive buffer is bounded only by what the peer
        /// chooses to send, which is what the credit window exists to
        /// prevent (§5.8).
        /// </summary>
        [Test]
        public async Task DataBeyondTheGrantedReceiveCreditResetsTheChannelAsync()
        {
            m_transport!.HasFlowControl = false;
            DataChannel channel = Register(
                direction: DataChannelDirection.Bidirectional,
                initialCredit: 8,
                maxFrameSize: 16);

            m_manager!.HandleFrame(Data(1, new byte[8]));
            m_manager.HandleFrame(Data(2, new byte[8]));

            await WaitForAsync(() => ResetFrames().Count > 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    ResetFrames().Select(frame => frame.Status.Code),
                    Does.Contain((uint)StatusCodes.BadDataChannelCreditExceeded));
            });
        }

        /// <summary>
        /// A frame the receive window discards still has to give its credit
        /// back, or a lossy channel bleeds its window away one duplicate at a
        /// time until it stalls for good.
        /// </summary>
        [Test]
        public async Task CreditIsReleasedForAFrameTheReceiveWindowDiscardsAsync()
        {
            m_transport!.HasFlowControl = false;
            DataChannel channel = Register(
                direction: DataChannelDirection.Bidirectional,
                initialCredit: 24,
                maxFrameSize: 16);

            m_manager!.HandleFrame(Data(1, new byte[8]));

            // The same sequence number again is a duplicate the window
            // discards. Were its credit not released, the third frame would
            // exceed the window and reset the channel.
            m_manager.HandleFrame(Data(1, new byte[8]));
            m_manager.HandleFrame(Data(2, new byte[8]));

            long roundsBefore = m_manager.SchedulerRounds;
            await WaitForAsync(() => m_manager.SchedulerRounds >= roundsBefore + 3)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.Not.EqualTo(DataChannelState.Faulted));
                Assert.That(ResetFrames(), Is.Empty);
                Assert.That(channel.GetDiagnostics().FramesReceived, Is.EqualTo(2ul));
            });
        }

        /// <summary>
        /// Part 6 errata §5.11: a PONG copies the PING's Timestamp verbatim,
        /// because the value is opaque to the responder and only the prober
        /// can interpret it.
        /// </summary>
        [Test]
        public async Task APingIsAnsweredWithAPongEchoingTheTimestampAsync()
        {
            Register(direction: DataChannelDirection.Bidirectional);

            const long probeTimestamp = 0x0123_4567_89AB_CDEF;
            m_manager!.HandleFrame(DataChannelFrame.Ping(ChannelId, 1, probeTimestamp));

            await WaitForAsync(() => PongFrames().Count > 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(PongFrames(), Has.Count.EqualTo(1));
                Assert.That(PongFrames()[0].Timestamp, Is.EqualTo(probeTimestamp));
            });
        }

        /// <summary>
        /// §5.11: PING is exempt from flow control and compels a PONG ahead of
        /// queued payload, so a peer that exceeds one PING per second per
        /// ChannelId would otherwise have an amplification surface with no
        /// window to close against it. The excess is discarded, not answered.
        /// </summary>
        [Test]
        public async Task PingsBeyondTheRateBoundAreDiscardedRatherThanAnsweredAsync()
        {
            DataChannel channel = Register(direction: DataChannelDirection.Bidirectional);

            // Below MaxPingRateViolations, so this exercises the discard on its
            // own rather than escalating to the reset the next test covers.
            for (uint ii = 1; ii <= 5; ii++)
            {
                m_manager!.HandleFrame(DataChannelFrame.Ping(ChannelId, ii, ii));
            }

            await WaitForAsync(() => PongFrames().Count > 0).ConfigureAwait(false);

            long roundsBefore = m_manager!.SchedulerRounds;
            await WaitForAsync(() => m_manager.SchedulerRounds >= roundsBefore + 3)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    PongFrames(),
                    Has.Count.EqualTo(1),
                    "A flood of PINGs inside one interval shall yield exactly one PONG.");
                Assert.That(channel.State, Is.Not.EqualTo(DataChannelState.Faulted));
            });
        }

        /// <summary>
        /// The bound is a rate, not a one-shot: once the interval has elapsed
        /// the next PING is answered normally.
        /// </summary>
        [Test]
        public async Task APingIsAnsweredAgainOnceTheIntervalHasElapsedAsync()
        {
            Register(direction: DataChannelDirection.Bidirectional);

            m_manager!.HandleFrame(DataChannelFrame.Ping(ChannelId, 1, 11));
            await WaitForAsync(() => PongFrames().Count == 1).ConfigureAwait(false);

            m_timeProvider!.Advance(
                TimeSpan.FromMilliseconds(DataChannelConstants.MinPingInterval));
            m_manager.HandleFrame(DataChannelFrame.Ping(ChannelId, 2, 22));

            await WaitForAsync(() => PongFrames().Count == 2).ConfigureAwait(false);

            Assert.That(
                PongFrames().Select(frame => frame.Timestamp),
                Is.EqualTo(new long[] { 11, 22 }));
        }

        /// <summary>
        /// §5.11 lets a receiver reset the channel once the breach persists.
        /// Being ignored is the first response; a peer that keeps flooding
        /// after that is treated as hostile rather than merely noisy.
        /// </summary>
        [Test]
        public async Task ASustainedPingFloodResetsTheChannelAsync()
        {
            DataChannel channel = Register(direction: DataChannelDirection.Bidirectional);

            for (uint ii = 1; ii <= DataChannelConstants.MaxPingRateViolations + 2; ii++)
            {
                m_manager!.HandleFrame(DataChannelFrame.Ping(ChannelId, ii, ii));
            }

            await WaitForAsync(() => ResetFrames().Count > 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    ResetFrames().Select(frame => frame.Status.Code),
                    Does.Contain((uint)StatusCodes.BadDataChannelLimitsExceeded));
            });
        }

        /// <summary>
        /// ChannelId 0 is a ChannelId, so the same bound applies to it.
        /// Without this the connection-level amplification surface stays open
        /// even once every data channel enforces its own bound.
        /// </summary>
        [Test]
        public async Task PingsOnTheConnectionControlChannelAreRateLimitedAsync()
        {
            Register(direction: DataChannelDirection.Bidirectional);

            for (uint ii = 1; ii <= 20; ii++)
            {
                m_manager!.HandleFrame(DataChannelFrame.Ping(
                    DataChannelConstants.ConnectionControlChannelId,
                    ii,
                    ii));
            }

            await WaitForAsync(() => ControlChannelPongFrames().Count > 0).ConfigureAwait(false);

            long roundsBefore = m_manager!.SchedulerRounds;
            await WaitForAsync(() => m_manager.SchedulerRounds >= roundsBefore + 3)
                .ConfigureAwait(false);

            Assert.That(ControlChannelPongFrames(), Has.Count.EqualTo(1));
        }

        private static DataChannelFrame Data(uint sequenceNumber, byte[] payload)
        {
            return DataChannelFrame.Data(
                ChannelId,
                sequenceNumber,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                payload);
        }

        private DataChannel Register(
            DataChannelDirection direction = DataChannelDirection.SourceToSink,
            DataChannelDeliveryMode deliveryMode = DataChannelDeliveryMode.ReliableOrdered,
            uint maxFrameSize = 4096,
            uint initialCredit = 65536)
        {
            DataChannel channel = m_manager!.Register(
                ChannelId,
                new NodeId(1u),
                new DataChannelSettings
                {
                    Direction = direction,
                    DeliveryMode = deliveryMode,
                    MaxFrameSize = maxFrameSize,
                    InitialCredit = initialCredit
                },
                isSource: true);
            m_manager.MarkOpen(ChannelId);
            return channel;
        }

        private IReadOnlyList<DataChannelFrame> ResetFrames()
        {
            return [.. m_transport!.Sent.Where(frame =>
                frame.ChannelId == ChannelId &&
                frame.FrameType == DataChannelFrameType.Reset)];
        }

        private IReadOnlyList<DataChannelFrame> PongFrames()
        {
            return [.. m_transport!.Sent.Where(frame =>
                frame.ChannelId == ChannelId &&
                frame.FrameType == DataChannelFrameType.Pong)];
        }

        private IReadOnlyList<DataChannelFrame> ControlChannelPongFrames()
        {
            return [.. m_transport!.Sent.Where(frame =>
                frame.ChannelId == DataChannelConstants.ConnectionControlChannelId &&
                frame.FrameType == DataChannelFrameType.Pong)];
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            for (int ii = 0; ii < 500; ii++)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.Fail("Timed out waiting for condition.");
        }

        private sealed class RecordingTransport(
            BufferManager bufferManager,
            TimeProvider timeProvider) : IDataChannelTransport
        {
            public bool HasFlowControl { get; set; } = true;

            public IReadOnlyList<DataChannelFrame> Sent
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_sent];
                    }
                }
            }

            public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

            public int MaxFrameBodySize => 4096;

            public bool HasTransportFlowControl => HasFlowControl;

            public BufferManager BufferManager { get; } = bufferManager;

            public TimeProvider TimeProvider { get; } = timeProvider;

            public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                lock (m_lock)
                {
                    m_sent.Add(frame);
                }

                return default;
            }

            public void OnProtocolFault(DataChannelFrameError error)
            {
            }

            private readonly List<DataChannelFrame> m_sent = [];
            private readonly Lock m_lock = new();
        }

        private const uint ChannelId = 1;

        private FakeTimeProvider? m_timeProvider;
        private ITelemetryContext? m_telemetry;
        private BufferManager? m_bufferManager;
        private RecordingTransport? m_transport;
        private DataChannelManager? m_manager;
    }
}
