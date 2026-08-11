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
    /// Covers the gap notification lifecycle of the Part 6 errata §5.10:
    /// the sender discarding a frame whose deadline passed and reporting the
    /// run it dropped, and the receiver's two refusals of a GAP that cannot
    /// legitimately arrive.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class DataChannelGapTests
    {
        [SetUp]
        public void SetUp()
        {
            m_timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 7, 29, 5, 0, 0, TimeSpan.Zero));
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("gap", 65536, m_telemetry);
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
        /// §5.10: a droppable frame whose deadline has passed is discarded
        /// rather than transmitted, and the sender reports the contiguous run
        /// it dropped so the receiver can tell loss from silence.
        /// </summary>
        [Test]
        public async Task ExpiredDroppableFramesAreDiscardedAndReportedAsAGapAsync()
        {
            DataChannel channel = Register(
                ChannelId,
                DataChannelDeliveryMode.PartiallyReliable,
                frameDeadline: 50);

            channel.Write([1, 2, 3], DataChannelFrameFlags.MessageStart |
                DataChannelFrameFlags.Droppable);
            channel.Write([4, 5, 6], DataChannelFrameFlags.MessageEnd |
                DataChannelFrameFlags.Droppable);

            // Move past the deadline before the scheduler ever gets to send
            // them, which is the condition the discard exists for.
            m_timeProvider!.Advance(TimeSpan.FromMilliseconds(500));

            await WaitForAsync(() => GapFrames().Count > 0).ConfigureAwait(false);

            IReadOnlyList<DataChannelFrame> gaps = GapFrames();
            DataChannelDiagnosticsDataType diagnostics = channel.GetDiagnostics();

            Assert.Multiple(() =>
            {
                Assert.That(gaps, Has.Count.EqualTo(1));
                Assert.That(gaps[0].FirstDiscarded, Is.EqualTo(1u));
                Assert.That(gaps[0].LastDiscarded, Is.EqualTo(2u));
                Assert.That(diagnostics.FramesDiscarded, Is.EqualTo(2ul));
                Assert.That(diagnostics.LastGapSequenceNumber, Is.EqualTo(2u));
                Assert.That(
                    m_transport!.Sent.Any(frame =>
                        frame.ChannelId == ChannelId &&
                        frame.FrameType == DataChannelFrameType.Data),
                    Is.False,
                    "An expired droppable frame shall not reach the transport.");
            });
        }

        /// <summary>
        /// A reliable channel may discard nothing, so no amount of elapsed
        /// time produces a GAP on one.
        /// </summary>
        [Test]
        public async Task AReliableChannelNeverReportsAGapAsync()
        {
            DataChannel channel = Register(
                ChannelId,
                DataChannelDeliveryMode.ReliableOrdered,
                frameDeadline: 0);

            channel.Write([1, 2, 3], DataChannelFrameFlags.MessageStart);
            m_timeProvider!.Advance(TimeSpan.FromSeconds(30));

            await WaitForAsync(() => m_transport!.Sent.Any(frame =>
                frame.ChannelId == ChannelId &&
                frame.FrameType == DataChannelFrameType.Data)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(GapFrames(), Is.Empty);
                Assert.That(channel.GetDiagnostics().FramesDiscarded, Is.Zero);
            });
        }

        /// <summary>
        /// §5.10: nothing may be discarded on a reliable channel, so a GAP
        /// arriving on one is a protocol error rather than information.
        /// </summary>
        [Test]
        public async Task AGapOnAReliableChannelResetsItAsAProtocolErrorAsync()
        {
            DataChannel channel = Register(
                ChannelId,
                DataChannelDeliveryMode.ReliableOrdered,
                frameDeadline: 0);

            m_manager!.HandleFrame(DataChannelFrame.Gap(ChannelId, 1, 1, 4));

            await WaitForAsync(() => ResetFrames().Count > 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    ResetFrames().Select(frame => frame.Status.Code),
                    Does.Contain((uint)StatusCodes.BadDeliveryModeUnsupported));
            });
        }

        /// <summary>
        /// §5.2.1: a GAP arriving in a direction that carries no DATA is how
        /// a peer would create unbounded receive-window state for free, since
        /// control frames are credit exempt and only DATA advances the
        /// highest received sequence number.
        /// </summary>
        [Test]
        public async Task AGapInADirectionThatCarriesNoDataResetsTheChannelAsync()
        {
            // This peer is the source of a SourceToSink channel, so the peer
            // sending this GAP is the sink and sends no DATA at all.
            DataChannel channel = Register(
                ChannelId,
                DataChannelDeliveryMode.PartiallyReliable,
                frameDeadline: 50);

            m_manager!.HandleFrame(DataChannelFrame.Gap(ChannelId, 1, 1, 4));

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
        /// The legitimate case: a GAP in a direction that does carry DATA on
        /// a channel that permits discard is accepted, and the sequence
        /// number it reports becomes the channel's last known gap.
        /// </summary>
        [Test]
        public async Task AGapInADirectionThatCarriesDataIsAcceptedAsync()
        {
            DataChannel channel = Register(
                ChannelId,
                DataChannelDeliveryMode.PartiallyReliable,
                frameDeadline: 50,
                direction: DataChannelDirection.Bidirectional);

            m_manager!.HandleFrame(DataChannelFrame.Gap(ChannelId, 1, 2, 6));

            // Let the scheduler run, so the absence of a RESET below is
            // evidence that none was queued rather than that none has been
            // drained yet.
            long roundsBefore = m_manager.SchedulerRounds;
            await WaitForAsync(() => m_manager.SchedulerRounds >= roundsBefore + 3)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.Not.EqualTo(DataChannelState.Faulted));
                Assert.That(channel.GetDiagnostics().LastGapSequenceNumber, Is.EqualTo(6u));
                Assert.That(ResetFrames(), Is.Empty);
            });
        }

        private DataChannel Register(
            uint channelId,
            DataChannelDeliveryMode deliveryMode,
            double frameDeadline,
            DataChannelDirection direction = DataChannelDirection.SourceToSink)
        {
            DataChannel channel = m_manager!.Register(
                channelId,
                new NodeId(1u),
                new DataChannelSettings
                {
                    Direction = direction,
                    DeliveryMode = deliveryMode,
                    MaxFrameSize = 4096,
                    InitialCredit = 65536,
                    FrameDeadline = frameDeadline
                },
                isSource: true);
            m_manager.MarkOpen(channelId);
            return channel;
        }

        private IReadOnlyList<DataChannelFrame> GapFrames()
        {
            return [.. m_transport!.Sent.Where(frame =>
                frame.ChannelId == ChannelId &&
                frame.FrameType == DataChannelFrameType.Gap)];
        }

        private IReadOnlyList<DataChannelFrame> ResetFrames()
        {
            return [.. m_transport!.Sent.Where(frame =>
                frame.ChannelId == ChannelId &&
                frame.FrameType == DataChannelFrameType.Reset)];
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

            public bool HasTransportFlowControl => true;

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
