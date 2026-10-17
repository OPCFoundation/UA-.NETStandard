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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    public class DataChannelManagerDeepTests
    {
        [SetUp]
        public void SetUp()
        {
            m_timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 7, 29, 5, 0, 0, TimeSpan.Zero));
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("manager-deep", 65536, m_telemetry);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            foreach (DataChannelManager manager in m_managers)
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }

            m_managers.Clear();
        }

        [Test]
        public async Task SchedulerWeightsPriorityButStillServicesLowPriorityChannels()
        {
            var serverTransport = new BlockingFirstDataTransport(m_bufferManager, m_timeProvider);
            DataChannelManager server = CreateManager(serverTransport, isServer: true);

            DataChannel high = RegisterOpen(
                server,
                1,
                priority: 7,
                maxFrameSize: 64,
                initialCredit: 4096);
            DataChannel medium = RegisterOpen(
                server,
                2,
                priority: 3,
                maxFrameSize: 64,
                initialCredit: 4096);
            DataChannel low = RegisterOpen(
                server,
                3,
                priority: 0,
                maxFrameSize: 64,
                initialCredit: 4096);

            high.Write(new byte[64], DataChannelFrameFlags.MessageStart);
            await serverTransport.FirstDataStarted.Task.ConfigureAwait(false);

            for (int ii = 1; ii < 16; ii++)
            {
                high.Write(new byte[64], DataChannelFrameFlags.MessageStart);
            }

            for (int ii = 0; ii < 16; ii++)
            {
                medium.Write(new byte[64], DataChannelFrameFlags.MessageStart);
                low.Write(new byte[64], DataChannelFrameFlags.MessageStart);
            }

            serverTransport.ReleaseFirstData();

            await WaitForAsync(
                () => serverTransport.Sent.Count(f => f.FrameType == DataChannelFrameType.Data) >= 48)
                .ConfigureAwait(false);

            DataChannelFrame[] data = serverTransport.Sent
                .Where(f => f.FrameType == DataChannelFrameType.Data)
                .ToArray();
            uint highId = high.ChannelId;
            uint mediumId = medium.ChannelId;
            uint lowId = low.ChannelId;

            Assert.Multiple(() =>
            {
                Assert.That(data.Take(8).Select(f => f.ChannelId), Is.All.EqualTo(highId));
                Assert.That(data.Skip(8).Take(4).Select(f => f.ChannelId), Is.All.EqualTo(mediumId));
                Assert.That(data[12].ChannelId, Is.EqualTo(lowId));
                Assert.That(data.Take(13).Count(f => f.ChannelId == lowId), Is.EqualTo(1));
                Assert.That(data.Count(f => f.ChannelId == lowId), Is.EqualTo(16));
            });
        }

        [Test]
        public async Task ConnectionCreditIsAnAggregateWindowAcrossChannels()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);

            DataChannel first = RegisterOpen(manager, 1, priority: 0, maxFrameSize: 64, initialCredit: 4096);
            DataChannel second = RegisterOpen(manager, 2, priority: 0, maxFrameSize: 64, initialCredit: 4096);

            manager.HandleFrame(DataChannelFrame.Credit(
                DataChannelConstants.ConnectionControlChannelId,
                1,
                0,
                96));

            first.Write(new byte[64], DataChannelFrameFlags.MessageStart);
            second.Write(new byte[64], DataChannelFrameFlags.MessageStart);

            await WaitForAsync(() => transport.CountOf(DataChannelFrameType.Data) == 1)
                .ConfigureAwait(false);
            await AssertNoChangeForAsync(() => transport.CountOf(DataChannelFrameType.Data), 1)
                .ConfigureAwait(false);

            manager.HandleFrame(DataChannelFrame.Credit(
                DataChannelConstants.ConnectionControlChannelId,
                2,
                0,
                32));

            await WaitForAsync(() => transport.CountOf(DataChannelFrameType.Data) == 2)
                .ConfigureAwait(false);

            Assert.That(
                transport.Sent.Where(f => f.FrameType == DataChannelFrameType.Data).Select(f => f.ChannelId),
                Is.EquivalentTo(new[] { first.ChannelId, second.ChannelId }));
        }

        [Test]
        public void TryAllocateChannelIdStopsAtTheConfiguredActiveChannelLimit()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(
                transport,
                isServer: true,
                maxDataChannels: 2);

            Assert.That(manager.TryAllocateChannelId(out uint first), Is.True);
            RegisterOpen(manager, first);
            Assert.That(manager.TryAllocateChannelId(out uint second), Is.True);
            RegisterOpen(manager, second);

            Assert.Multiple(() =>
            {
                Assert.That(manager.TryAllocateChannelId(out uint exhausted), Is.False);
                Assert.That(exhausted, Is.Zero);
                Assert.That(manager.ActiveChannelCount, Is.EqualTo(2));
                Assert.That(manager.Channels.Select(c => c.ChannelId), Is.EqualTo(new[] { first, second }));
            });
        }

        [Test]
        public void TryAllocateChannelIdReportsExhaustionAfterUInt32Wrap()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);
            typeof(DataChannelManager)
                .GetField("m_nextChannelId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(manager, uint.MaxValue);

            Assert.That(manager.TryAllocateChannelId(out uint last), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(last, Is.EqualTo(uint.MaxValue));
                Assert.That(manager.TryAllocateChannelId(out uint exhausted), Is.False);
                Assert.That(exhausted, Is.Zero);
            });
        }

        [Test]
        public void DuplicateRegisterDisposesTheRejectedChannelAndKeepsTheOriginal()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);
            DataChannel original = RegisterOpen(manager, 7);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                manager.Register(7, new NodeId(1u), DefaultSettings(), isSource: true))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDataChannelIdInvalid));
                Assert.That(manager.ActiveChannelCount, Is.EqualTo(1));
                Assert.That(manager.TryGetChannel(7, out DataChannel? current), Is.True);
                Assert.That(current, Is.SameAs(original));
            });
        }

        [Test]
        public async Task PendingUnknownFramesAreBoundedReplayedAndDeliveredWhenChannelOpens()
        {
            var serverTransport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            var clientTransport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager server = CreateManager(serverTransport, isServer: true);
            DataChannelManager client = CreateManager(clientTransport, isServer: false);
            serverTransport.Peer = client;
            clientTransport.Peer = server;

            DataChannel removed = server.Register(55, new NodeId(1u), DefaultSettings(), isSource: true);
            server.Remove(removed.ChannelId);

            byte[] payload = [0xAA, 0xBB, 0xCC];
            server.HandleFrame(DataChannelFrame.Data(
                55,
                1,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                payload));
            server.HandleFrame(DataChannelFrame.Data(
                55,
                2,
                DataChannelFrameFlags.MessageStart,
                new byte[DataChannelConstants.UnknownChannelBufferFrames * 4096 + 1]));

            DataChannel replayTarget = server.Register(55, new NodeId(1u), DefaultSettings(), isSource: false);
            server.MarkOpen(replayTarget.ChannelId);

            using DataChannelMessage? message = await ReadWithTimeoutAsync(replayTarget)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(message, Is.Not.Null);
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(payload));
                Assert.That(message.FrameSequenceNumber, Is.EqualTo(1u));
                Assert.That(serverTransport.Faults, Is.Empty);
            });
        }

        /// <summary>
        /// A control frame that overtakes its OpenDataChannel response is
        /// replayed with its own fields intact.
        /// </summary>
        /// <remarks>
        /// Over a transport with no ordering between streams any frame can
        /// arrive before the response that names its ChannelId (7.4), not
        /// just DATA. The buffer holds a copy rather than the frame, so each
        /// frame type has to carry its own fields through: a CREDIT replayed
        /// with a zero grant would leave the sender blocked, and a GAP
        /// replayed with the wrong range would report the wrong frames
        /// discarded.
        /// </remarks>
        [Test]
        public async Task ControlFramesThatOvertakeTheOpenResponseReplayIntactAsync()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);

            // GAP is only legal where the delivery mode allows discard.
            var settings = new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.PartiallyReliable,
                MaxFrameSize = 4096,
                InitialCredit = 65536,
                DrainTimeout = DataChannelConstants.DefaultDrainTimeout
            };

            DataChannel removed = manager.Register(61, new NodeId(1u), settings, isSource: true);
            manager.Remove(removed.ChannelId);

            manager.HandleFrame(DataChannelFrame.Credit(61, 1, 4096, 8192));
            manager.HandleFrame(DataChannelFrame.Gap(61, 2, 1, 2));
            manager.HandleFrame(DataChannelFrame.Data(
                61,
                3,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                new byte[] { 0x01, 0x02 }));

            DataChannel target = manager.Register(61, new NodeId(1u), settings, isSource: false);
            manager.MarkOpen(target.ChannelId);

            using DataChannelMessage? message = await ReadWithTimeoutAsync(target)
                .ConfigureAwait(false);

            DataChannelDiagnosticsDataType diagnostics = target.GetDiagnostics();

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.LastGapSequenceNumber,
                    Is.EqualTo(2u),
                    "The buffered GAP replayed without its range, so the wrong frames " +
                        "are reported discarded.");
                Assert.That(message, Is.Not.Null);
                Assert.That(message!.FrameSequenceNumber, Is.EqualTo(3u));
                Assert.That(
                    transport.Faults,
                    Is.Empty,
                    "A frame that legitimately overtook the response was treated as a fault.");
            });
        }

        [Test]
        public async Task ConnectionPingRatePongAndRttAreTrackedAtManagerLevel()
        {
            var serverTransport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            var clientTransport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager server = CreateManager(serverTransport, isServer: true);
            DataChannelManager client = CreateManager(clientTransport, isServer: false);
            serverTransport.Peer = client;
            clientTransport.Peer = server;

            Assert.That(server.TryPingConnection(), Is.True);
            await WaitForAsync(() => clientTransport.CountOf(DataChannelFrameType.Pong) == 1)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.RoundTripTime, Is.GreaterThanOrEqualTo(0d));
                Assert.That(
                    server.TryPingConnection(),
                    Is.False,
                    "a completed ping is still rate limited to one per second");
            });
        }

        [Test]
        public void ConnectionPingAllowsOnlyOneOutstandingProbe()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);

            Assert.Multiple(() =>
            {
                Assert.That(manager.TryPingConnection(), Is.True);
                Assert.That(manager.TryPingConnection(), Is.False);
            });
        }

        [Test]
        public void InvalidConnectionControlFrameFaultsTheTransport()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);

            manager.HandleFrame(DataChannelFrame.Data(
                DataChannelConstants.ConnectionControlChannelId,
                1,
                DataChannelFrameFlags.None,
                ReadOnlyMemory<byte>.Empty));

            Assert.That(transport.Faults, Is.EqualTo(new[] { DataChannelFrameError.InvalidControlChannelFrame }));
        }

        [Test]
        public void UnknownNonPendingChannelFaultsTheTransport()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);

            manager.HandleFrame(DataChannelFrame.Data(
                999,
                1,
                DataChannelFrameFlags.MessageStart,
                new byte[] { 0x01 }));

            Assert.That(transport.Faults, Is.EqualTo(new[] { DataChannelFrameError.InvalidControlChannelFrame }));
        }

        [Test]
        public void ConnectionCreditIsReplenishedByAPerChannelCreditFrame()
        {
            // Part 6 errata 5.8.2: a CREDIT frame on a non-zero ChannelId
            // grants ChannelCredit to that channel and, where ConnectionCredit
            // is non-zero, that amount to the connection as well. Dropping the
            // connection half leaves the window granted once at open and never
            // refilled, so every channel stalls permanently once it is spent.
            // No volume test reached that point, which is why this is asserted
            // on the accounting rather than by moving megabytes.
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);
            DataChannel channel = RegisterOpen(manager, 1);

            uint before = ConnectionSendAvailable(manager);

            manager.HandleFrame(DataChannelFrame.Credit(channel.ChannelId, 1, 4096, 4096));

            Assert.Multiple(() =>
            {
                Assert.That(
                    ConnectionSendAvailable(manager),
                    Is.EqualTo(before + 4096),
                    "the connection window shall grow by the frame's ConnectionCredit");
                Assert.That(channel.State, Is.Not.EqualTo(DataChannelState.Faulted));
            });
        }

        [Test]
        public void ConnectionCreditOverflowFromAPerChannelFrameAbortsEveryChannel()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);
            DataChannel first = RegisterOpen(manager, 1);
            DataChannel second = RegisterOpen(manager, 2);

            manager.HandleFrame(DataChannelFrame.Credit(first.ChannelId, 1, 1, uint.MaxValue));
            manager.HandleFrame(DataChannelFrame.Credit(first.ChannelId, 2, 1, 1));

            Assert.Multiple(() =>
            {
                Assert.That(first.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(second.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    first.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadDataChannelCreditExceeded));
            });
        }

        [Test]
        public void ConnectionCreditOverflowAbortsEveryOpenChannel()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);
            DataChannel first = RegisterOpen(manager, 1);
            DataChannel second = RegisterOpen(manager, 2);

            manager.HandleFrame(DataChannelFrame.Credit(
                DataChannelConstants.ConnectionControlChannelId,
                1,
                0,
                uint.MaxValue));
            manager.HandleFrame(DataChannelFrame.Credit(
                DataChannelConstants.ConnectionControlChannelId,
                2,
                0,
                1));

            Assert.Multiple(() =>
            {
                Assert.That(first.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(second.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(first.Status, Is.EqualTo((StatusCode)StatusCodes.BadDataChannelCreditExceeded));
                Assert.That(second.Status, Is.EqualTo((StatusCode)StatusCodes.BadDataChannelCreditExceeded));
            });
        }

        [Test]
        public async Task ClosingDrainedSourceChannelQueuesEndAndClosesOnTheSchedulerTick()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);
            DataChannel channel = RegisterOpen(
                manager,
                1,
                maxFrameSize: 64,
                initialCredit: 4096,
                drainTimeout: 50);

            channel.Close();

            await PumpSchedulerRoundAsync(manager).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(channel.Status, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(transport.CountOf(DataChannelFrameType.Data), Is.Zero);
                Assert.That(transport.CountOf(DataChannelFrameType.End), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DisposeIsIdempotentAndReturnsBufferedPendingPayload()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, isServer: true);
            m_managers.Remove(manager);

            DataChannel removed = manager.Register(77, new NodeId(1u), DefaultSettings(), isSource: true);
            manager.Remove(removed.ChannelId);
            manager.HandleFrame(DataChannelFrame.Data(
                77,
                1,
                DataChannelFrameFlags.MessageStart,
                new byte[32]));

            await manager.DisposeAsync().ConfigureAwait(false);
            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(manager.ActiveChannelCount, Is.Zero);
                Assert.That(transport.Faults, Is.Empty);
            });
        }

        private DataChannelManager CreateManager(
            IDataChannelTransport transport,
            bool isServer,
            ushort maxDataChannels = 16,
            uint maxCreditPerChannel = 1024 * 1024)
        {
            var manager = new DataChannelManager(
                transport,
                isServer,
                m_telemetry,
                maxDataChannels,
                maxCreditPerChannel);
            m_managers.Add(manager);
            return manager;
        }

        private static (DataChannel server, DataChannel client) OpenPair(
            DataChannelManager server,
            DataChannelManager client,
            byte priority = 0,
            uint maxFrameSize = 4096,
            uint initialCredit = 65536)
        {
            Assert.That(server.TryAllocateChannelId(out uint channelId), Is.True);

            var settings = DefaultSettings(priority, maxFrameSize, initialCredit);
            DataChannel serverChannel = server.Register(
                channelId,
                new NodeId(1u),
                settings,
                isSource: true);
            DataChannel clientChannel = client.Register(
                channelId,
                new NodeId(1u),
                settings,
                isSource: false);

            server.MarkOpen(channelId);
            client.MarkOpen(channelId);

            return (serverChannel, clientChannel);
        }

        private static uint ConnectionSendAvailable(DataChannelManager manager)
        {
            object window = typeof(DataChannelManager)
                .GetField("m_connectionSend", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(manager)!;

            return (uint)window.GetType()
                .GetProperty("Available", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(window)!;
        }

        private static DataChannel RegisterOpen(
            DataChannelManager manager,
            uint channelId,
            byte priority = 0,
            uint maxFrameSize = 4096,
            uint initialCredit = 65536,
            int drainTimeout = DataChannelConstants.DefaultDrainTimeout)
        {
            DataChannel channel = manager.Register(
                channelId,
                new NodeId(1u),
                DefaultSettings(priority, maxFrameSize, initialCredit, drainTimeout),
                isSource: true);
            manager.MarkOpen(channelId);
            return channel;
        }

        private static DataChannelSettings DefaultSettings(
            byte priority = 0,
            uint maxFrameSize = 4096,
            uint initialCredit = 65536,
            int drainTimeout = DataChannelConstants.DefaultDrainTimeout)
        {
            return new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = maxFrameSize,
                InitialCredit = initialCredit,
                Priority = priority,
                DrainTimeout = drainTimeout
            };
        }

        private static async Task<DataChannelMessage?> ReadWithTimeoutAsync(DataChannel channel)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                return await channel.ReadAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.Fail("Timed out waiting for condition.");
        }

        private static async Task PumpSchedulerRoundAsync(DataChannelManager manager)
        {
            MethodInfo method = typeof(DataChannelManager).GetMethod(
                "RunRoundAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var ready = new List<DataChannel>();
            var result = (ValueTask)method.Invoke(manager, [ready, CancellationToken.None])!;
            await result.ConfigureAwait(false);
        }

        private static async Task AssertNoChangeForAsync<T>(Func<T> read, T expected)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(200))
            {
                Assert.That(read(), Is.EqualTo(expected));
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private sealed class BlockingFirstDataTransport : IDataChannelTransport
        {
            public BlockingFirstDataTransport(BufferManager bufferManager, TimeProvider timeProvider)
            {
                BufferManager = bufferManager;
                TimeProvider = timeProvider;
            }

            public TaskCompletionSource<bool> FirstDataStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

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

            public BufferManager BufferManager { get; }

            public TimeProvider TimeProvider { get; }

            public async ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                lock (m_lock)
                {
                    m_sent.Add(frame);
                }

                if (frame.FrameType == DataChannelFrameType.Data &&
                    Interlocked.Exchange(ref m_blockedFirstData, 1) == 0)
                {
                    FirstDataStarted.TrySetResult(true);
                    await m_releaseFirstData.Task.ConfigureAwait(false);
                }
            }

            public void OnProtocolFault(DataChannelFrameError error)
            {
            }

            public void ReleaseFirstData()
            {
                m_releaseFirstData.TrySetResult(true);
            }

            private readonly List<DataChannelFrame> m_sent = [];
            private readonly object m_lock = new();
            private readonly TaskCompletionSource<bool> m_releaseFirstData =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_blockedFirstData;
        }

        private FakeTimeProvider m_timeProvider = null!;
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;
        private readonly List<DataChannelManager> m_managers = [];
    }
}
