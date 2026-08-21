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
    public class DataChannelLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            m_timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 29, 4, 45, 0, TimeSpan.Zero));
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("lifecycle", 65536, m_telemetry);

            m_serverTransport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            m_clientTransport = new LoopbackTransport(m_bufferManager, m_timeProvider);

            m_server = new DataChannelManager(m_serverTransport, true, m_telemetry);
            m_client = new DataChannelManager(m_clientTransport, false, m_telemetry);

            m_serverTransport.Peer = m_client;
            m_clientTransport.Peer = m_server;
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_server != null)
            {
                await m_server.DisposeAsync().ConfigureAwait(false);
            }

            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DataChannelLifecycle_PauseAndResumeAreTrackedPerSendDirection()
        {
            (DataChannel server, DataChannel client) = OpenPair(
                DataChannelDirection.Bidirectional,
                initialCredit: 64,
                maxFrameSize: 64);

            server.Write(new byte[64], DataChannelFrameFlags.MessageStart);
            server.Write(new byte[64], DataChannelFrameFlags.MessageStart);

            using DataChannelMessage? heldByClient = await ReadWithTimeoutAsync(client)
                .ConfigureAwait(false);

            Assert.That(heldByClient, Is.Not.Null);

            await WaitForAsync(() => server.State == DataChannelState.Paused)
                .ConfigureAwait(false);

            client.Write([0xCA], DataChannelFrameFlags.MessageStart);

            using DataChannelMessage? receivedWhileServerPaused = await ReadWithTimeoutAsync(server)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.State, Is.EqualTo(DataChannelState.Paused));
                Assert.That(receivedWhileServerPaused, Is.Not.Null);
                Assert.That(receivedWhileServerPaused!.Payload.Span[0], Is.EqualTo(0xCA));
                Assert.That(client.State, Is.EqualTo(DataChannelState.Open));
            });

            heldByClient.Dispose();

            await WaitForAsync(() => server.State == DataChannelState.Open)
                .ConfigureAwait(false);

            using DataChannelMessage? releasedFrame = await ReadWithTimeoutAsync(client)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(releasedFrame, Is.Not.Null);
                Assert.That(releasedFrame!.FrameSequenceNumber, Is.EqualTo(2u));
            });
        }

        [Test]
        public async Task DataChannelLifecycle_CloseHalfOpenDirectionsRemainIndependentAndCloseIsIdempotent()
        {
            (DataChannel server, DataChannel client) = OpenPair(DataChannelDirection.Bidirectional);

            client.Close();

            await WaitForAsync(() => m_clientTransport.CountOf(DataChannelFrameType.End) > 0)
                .ConfigureAwait(false);

            Assert.That(() => server.Write([0x01], DataChannelFrameFlags.MessageStart), Throws.Nothing);

            using DataChannelMessage? afterPeerHalfClose = await ReadWithTimeoutAsync(client)
                .ConfigureAwait(false);

            server.Close();

            await WaitForAsync(() =>
                server.State == DataChannelState.Closed &&
                client.State == DataChannelState.Closed).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(afterPeerHalfClose, Is.Not.Null);
                Assert.That(afterPeerHalfClose!.Payload.Span[0], Is.EqualTo(0x01));
                Assert.That(() => server.Close(), Throws.Nothing);
                Assert.That(() => client.Close(), Throws.Nothing);
                Assert.That(server.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(client.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(
                    () => server.Write([0x02], DataChannelFrameFlags.MessageStart),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public async Task DataChannelLifecycle_ResetWithBadStatusFaultsPeerAndMakesBothEndsUnusable()
        {
            (DataChannel server, DataChannel client) = OpenPair(DataChannelDirection.SourceToSink);

            server.Reset(StatusCodes.BadDataChannelClosed);

            await WaitForAsync(() => client.State == DataChannelState.Faulted)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(client.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(server.Status, Is.EqualTo((StatusCode)StatusCodes.BadDataChannelClosed));
                Assert.That(client.Status, Is.EqualTo((StatusCode)StatusCodes.BadDataChannelClosed));
                Assert.That(
                    () => server.Write([0x01], DataChannelFrameFlags.MessageStart),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public async Task DataChannelLifecycle_TryPingLatchesUntilTheProbeIsAnswered()
        {
            (DataChannel server, _) = OpenPair(DataChannelDirection.SourceToSink);

            // Hold the peer's PONG so the latch can be observed. Without this
            // the loopback answers before the assertion runs and the check
            // races the transport rather than testing the latch.
            m_clientTransport.DropOutbound = true;

            bool firstPing = server.TryPing();

            // The latch is set synchronously by TryPing; the frame itself is
            // written by the scheduler, so it is waited for rather than
            // asserted in the same breath.
            Assert.Multiple(() =>
            {
                Assert.That(firstPing, Is.True);
                Assert.That(IsPingOutstanding(server), Is.True);
                Assert.That(server.TryPing(), Is.False);
            });

            await WaitForAsync(() => m_serverTransport.CountOf(DataChannelFrameType.Ping) > 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(m_serverTransport.CountOf(DataChannelFrameType.Ping), Is.EqualTo(1));
                Assert.That(IsPingOutstanding(server), Is.True, "the dropped PONG leaves the latch set");
            });
        }

        [Test]
        public async Task DataChannelLifecycle_TryPingAllowsOneLiveProbeAndPeerAnswersIt()
        {
            (DataChannel server, _) = OpenPair(DataChannelDirection.SourceToSink);

            Assert.That(server.TryPing(), Is.True);

            await WaitForAsync(() => m_clientTransport.CountOf(DataChannelFrameType.Pong) > 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(m_serverTransport.CountOf(DataChannelFrameType.Ping), Is.EqualTo(1));
                Assert.That(m_clientTransport.CountOf(DataChannelFrameType.Pong), Is.EqualTo(1));
                Assert.That(IsPingOutstanding(server), Is.False);
            });
        }

        [Test]
        public async Task DataChannelLifecycle_TryPingReturnsFalseOnClosedChannelWithoutQueueing()
        {
            (DataChannel server, DataChannel client) = OpenPair(DataChannelDirection.SourceToSink);

            server.Reset(StatusCodes.Good);

            await WaitForAsync(() => client.State == DataChannelState.Closed)
                .ConfigureAwait(false);

            int pingsBefore = m_serverTransport.CountOf(DataChannelFrameType.Ping);

            Assert.Multiple(() =>
            {
                Assert.That(server.TryPing(), Is.False);
                Assert.That(m_serverTransport.CountOf(DataChannelFrameType.Ping), Is.EqualTo(pingsBefore));
                Assert.That(IsPingOutstanding(server), Is.False);
            });
        }

        [Test]
        public async Task DataChannelLifecycle_TryPingReturnsFalseOnFaultedChannelWithoutQueueing()
        {
            (DataChannel server, DataChannel client) = OpenPair(DataChannelDirection.SourceToSink);

            server.Reset(StatusCodes.BadDataChannelClosed);

            await WaitForAsync(() => client.State == DataChannelState.Faulted)
                .ConfigureAwait(false);

            int pingsBefore = m_serverTransport.CountOf(DataChannelFrameType.Ping);

            Assert.Multiple(() =>
            {
                Assert.That(server.TryPing(), Is.False);
                Assert.That(m_serverTransport.CountOf(DataChannelFrameType.Ping), Is.EqualTo(pingsBefore));
                Assert.That(IsPingOutstanding(server), Is.False);
            });
        }

        [Test]
        public async Task DataChannelLifecycle_ControlFramesDoNotAdvanceHighestReceivedForData()
        {
            (DataChannel server, DataChannel client) = OpenPair(DataChannelDirection.SourceToSink);

            await m_clientTransport.SendFrameAsync(
                DataChannelFrame.Credit(client.ChannelId, 100, 64, 0),
                default).ConfigureAwait(false);

            await m_clientTransport.SendFrameAsync(
                DataChannelFrame.Ping(client.ChannelId, 101, m_timeProvider.GetTimestamp()),
                default).ConfigureAwait(false);

            await m_serverTransport.SendFrameAsync(
                DataChannelFrame.Data(
                    server.ChannelId,
                    1,
                    DataChannelFrameFlags.MessageStart,
                    new byte[] { 0x42 }),
                default).ConfigureAwait(false);

            using DataChannelMessage? firstData = await ReadWithTimeoutAsync(client)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(client.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(firstData, Is.Not.Null);
                Assert.That(firstData!.FrameSequenceNumber, Is.EqualTo(1u));
                Assert.That(firstData.Status, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(firstData.GapFrom, Is.Zero);
                Assert.That(firstData.GapTo, Is.Zero);
            });
        }

        [Test]
        public async Task DataChannelLifecycle_ChannelIdLimitReportsExhaustionAndReusesCapacityAfterClose()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            var manager = new DataChannelManager(transport, true, m_telemetry, maxDataChannels: 2);

            try
            {
                Assert.That(manager.TryAllocateChannelId(out uint first), Is.True);
                DataChannel firstChannel = RegisterOpen(manager, first);

                Assert.That(manager.TryAllocateChannelId(out uint second), Is.True);
                DataChannel secondChannel = RegisterOpen(manager, second);

                Assert.Multiple(() =>
                {
                    Assert.That(first, Is.Not.EqualTo(second));
                    Assert.That(manager.TryAllocateChannelId(out uint exhausted), Is.False);
                    Assert.That(exhausted, Is.Zero);
                });

                firstChannel.Close();
                manager.Remove(first);

                Assert.That(manager.TryAllocateChannelId(out uint afterClose), Is.True);
                DataChannel afterCloseChannel = RegisterOpen(manager, afterClose);

                Assert.Multiple(() =>
                {
                    Assert.That(afterClose, Is.Not.EqualTo(second));
                    Assert.That(afterCloseChannel.ChannelId, Is.EqualTo(afterClose));
                    Assert.That(manager.Channels.Select(static c => c.ChannelId), Is.EquivalentTo(new[] { second, afterClose }));
                });

                secondChannel.Reset(StatusCodes.Good);
                afterCloseChannel.Reset(StatusCodes.Good);
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DataChannelLifecycle_ChannelIdAllocatorDoesNotWrapOntoLiveChannelAtUintMaximum()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            var manager = new DataChannelManager(transport, true, m_telemetry, maxDataChannels: 3);

            try
            {
                DataChannel liveOne = RegisterOpen(manager, 1);
                SetNextChannelId(manager, uint.MaxValue);

                Assert.Multiple(() =>
                {
                    Assert.That(manager.TryAllocateChannelId(out uint last), Is.True);
                    Assert.That(last, Is.EqualTo(uint.MaxValue));
                    Assert.That(manager.TryAllocateChannelId(out uint wrapped), Is.False);
                    Assert.That(wrapped, Is.Zero);
                    Assert.That(manager.Channels.Select(static c => c.ChannelId), Is.EquivalentTo(SingleLiveChannelId));
                });

                liveOne.Reset(StatusCodes.Good);
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DataChannelLifecycle_ManagerDisposalWhileSendIsInFlightCompletes()
        {
            var delayedTransport = new DelayingTransport(m_bufferManager, m_timeProvider);
            var manager = new DataChannelManager(delayedTransport, true, m_telemetry);

            Assert.That(manager.TryAllocateChannelId(out uint channelId), Is.True);

            DataChannel channel = manager.Register(
                channelId,
                new NodeId(1u),
                new DataChannelSettings { MaxFrameSize = 64 },
                isSource: true);

            manager.MarkOpen(channelId);
            channel.Write(new byte[64], DataChannelFrameFlags.MessageStart);

            await WaitWithTimeoutAsync(delayedTransport.SendStarted.Task).ConfigureAwait(false);
            await WaitWithTimeoutAsync(manager.DisposeAsync().AsTask()).ConfigureAwait(false);
        }

        [Test]
        public void DataChannelLifecycle_SettingsApplyDefaultsAndRoundTripToParameters()
        {
            var settings = new DataChannelSettings();

            DataChannelParametersDataType parameters = settings.ToParameters();
            DataChannelSettings fromEmptyContentType = DataChannelSettings.FromParameters(
                new DataChannelParametersDataType
                {
                    Direction = DataChannelDirection.SinkToSource,
                    DeliveryMode = DataChannelDeliveryMode.Unreliable,
                    ContentType = string.Empty,
                    MaxFrameSize = 1200,
                    InitialCredit = 2400,
                    Priority = 4,
                    MaxRetransmits = 2,
                    FrameDeadline = 50
                });

            Assert.Multiple(() =>
            {
                Assert.That(parameters.Direction, Is.EqualTo(DataChannelDirection.SourceToSink));
                Assert.That(parameters.DeliveryMode, Is.EqualTo(DataChannelDeliveryMode.ReliableOrdered));
                Assert.That(parameters.ContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(parameters.MaxFrameSize, Is.EqualTo(4096u));
                Assert.That(parameters.InitialCredit, Is.EqualTo(65536u));
                Assert.That(settings.OpenTimeout, Is.EqualTo(DataChannelConstants.DefaultOpenTimeout));
                Assert.That(settings.DrainTimeout, Is.EqualTo(DataChannelConstants.DefaultDrainTimeout));
                Assert.That(settings.MaxGapRuns, Is.EqualTo(DataChannelConstants.DefaultMaxGapRuns));
                Assert.That(settings.AllowsDiscard, Is.False);
                Assert.That(fromEmptyContentType.ContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(fromEmptyContentType.AllowsDiscard, Is.True);
                Assert.That(fromEmptyContentType.ToParameters().FrameDeadline, Is.EqualTo(50d));
            });
        }

        [Test]
        public void DataChannelLifecycle_SettingsRejectNullParametersAndExposeDirectionAndDeadlines()
        {
            var lossy = new DataChannelSettings
            {
                Direction = DataChannelDirection.SinkToSource,
                DeliveryMode = DataChannelDeliveryMode.PartiallyReliable,
                FrameDeadline = 12.5
            };

            long expectedDeadline = m_timeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc() +
                (long)(12.5 * DataChannelConstants.DeadlineTicksPerMillisecond);

            Assert.Multiple(() =>
            {
                Assert.That(() => DataChannelSettings.FromParameters(null!), Throws.TypeOf<ArgumentNullException>());
                Assert.That(lossy.AllowsDiscard, Is.True);
                Assert.That(lossy.CanSendData(isSource: true), Is.False);
                Assert.That(lossy.CanSendData(isSource: false), Is.True);
                Assert.That(lossy.ComputeDeadline(m_timeProvider), Is.EqualTo(expectedDeadline));
                Assert.That(new DataChannelSettings { FrameDeadline = 0 }.ComputeDeadline(m_timeProvider), Is.Zero);
            });
        }

        [Test]
        public void DataChannelLifecycle_OutOfRangeSettingsAreRejectedAtChannelBoundary()
        {
            (DataChannel server, _) = OpenPair(
                DataChannelDirection.SourceToSink,
                maxFrameSize: 4);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => server.Write(new byte[5], DataChannelFrameFlags.MessageStart),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(
                    () => server.Write([0x01], (DataChannelFrameFlags)0xE0),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(
                    () => server.Write([0x01], DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.Droppable),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public async Task DataChannelLifecycle_ConcurrentChannelsAllProgressIncludingLowPriority()
        {
            const int channelCount = 4;
            const int framesPerChannel = 12;
            const int frameSize = 32;

            var pairs = new List<(DataChannel server, DataChannel client)>();

            for (int ii = 0; ii < channelCount; ii++)
            {
                pairs.Add(OpenPair(
                    DataChannelDirection.SourceToSink,
                    initialCredit: framesPerChannel * frameSize,
                    maxFrameSize: frameSize,
                    priority: ii == 0 ? (byte)0 : DataChannelConstants.MaxPriority));
            }

            await Task.WhenAll(pairs.Select((pair, index) => Task.Run(() =>
            {
                for (int frame = 0; frame < framesPerChannel; frame++)
                {
                    pair.server.Write([(byte)index, (byte)frame], DataChannelFrameFlags.MessageStart);
                }
            }))).ConfigureAwait(false);

            int[] received = new int[channelCount];
            var stopwatch = Stopwatch.StartNew();

            while (received.Any(static count => count < framesPerChannel) &&
                stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                for (int ii = 0; ii < pairs.Count; ii++)
                {
                    while (received[ii] < framesPerChannel)
                    {
                        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(25));

                        try
                        {
                            using DataChannelMessage? message = await pairs[ii].client.ReadAsync(cts.Token)
                                .ConfigureAwait(false);

                            if (message == null)
                            {
                                break;
                            }

                            received[ii]++;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(received[0], Is.EqualTo(framesPerChannel), "low priority channel must not starve");

                for (int ii = 1; ii < received.Length; ii++)
                {
                    Assert.That(received[ii], Is.EqualTo(framesPerChannel), $"channel {ii} did not drain");
                }
            });
        }

        /// <summary>
        /// Part 6 errata §7.4 and Part 4 errata §5.1: a peer "shall not
        /// transmit a DATA, GAP, END or RESET frame for a ChannelId before the
        /// OpenDataChannel response carrying that ChannelId has been handed to
        /// the transport", which the §5.13 state table restates by permitting
        /// no frames at all while a channel is Opening. A source that starts
        /// streaming the moment it is handed the channel must therefore be
        /// queued, not transmitted.
        /// </summary>
        [Test]
        public async Task DataChannelLifecycle_NoFrameIsTransmittedWhileTheChannelIsOpening()
        {
            var settings = new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = 4096,
                InitialCredit = 65536
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
            m_client.MarkOpen(channelId);

            await WaitForAsync(
                () => m_clientTransport.Sent.Any(
                    frame => frame.ChannelId == DataChannelConstants.ConnectionControlChannelId &&
                        frame.FrameType == DataChannelFrameType.Credit))
                .ConfigureAwait(false);

            server.Write([0x01, 0x02], DataChannelFrameFlags.MessageStart);

            // Wait for the scheduler to have actually considered the channels
            // several times, so the negative below is evidence that the
            // Opening gate held rather than that nothing had run yet.
            long roundsBefore = m_server.SchedulerRounds;
            await WaitForAsync(() => m_server.SchedulerRounds >= roundsBefore + 3)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.State, Is.EqualTo(DataChannelState.Opening));
                Assert.That(
                    m_serverTransport.Sent.Any(frame => frame.ChannelId == channelId),
                    Is.False,
                    "A frame was transmitted before the OpenDataChannel response was dispatched.");
            });

            m_server.MarkOpen(channelId);

            await WaitForAsync(
                () => m_serverTransport.Sent.Any(
                    frame => frame.ChannelId == channelId &&
                        frame.FrameType == DataChannelFrameType.Data))
                .ConfigureAwait(false);

            using DataChannelMessage? delivered = await ReadWithTimeoutAsync(client)
                .ConfigureAwait(false);

            Assert.That(delivered, Is.Not.Null);
        }

        private (DataChannel server, DataChannel client) OpenPair(
            DataChannelDirection direction,
            uint initialCredit = 65536,
            uint maxFrameSize = 4096,
            byte priority = 0)
        {
            var settings = new DataChannelSettings
            {
                Direction = direction,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = maxFrameSize,
                InitialCredit = initialCredit,
                Priority = priority
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

            return (server, client);
        }

        private static DataChannel RegisterOpen(DataChannelManager manager, uint channelId)
        {
            DataChannel channel = manager.Register(
                channelId,
                new NodeId(1u),
                new DataChannelSettings(),
                isSource: true);

            manager.MarkOpen(channelId);
            return channel;
        }

        private static void SetNextChannelId(DataChannelManager manager, uint value)
        {
            FieldInfo? field = typeof(DataChannelManager).GetField(
                "m_nextChannelId",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field!.SetValue(manager, value);
        }

        private static bool IsPingOutstanding(DataChannel channel)
        {
            FieldInfo? field = typeof(DataChannel).GetField(
                "m_pingOutstanding",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (bool)field!.GetValue(channel)!;
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

        private static async Task WaitWithTimeoutAsync(Task task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)))
                .ConfigureAwait(false);

            Assert.That(completed, Is.SameAs(task));
            await task.ConfigureAwait(false);
        }

        private sealed class DelayingTransport : IDataChannelTransport
        {
            public DelayingTransport(BufferManager bufferManager, TimeProvider timeProvider)
            {
                BufferManager = bufferManager;
                TimeProvider = timeProvider;
            }

            public TaskCompletionSource<bool> SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

            public int MaxFrameBodySize => 4096;

            public bool HasTransportFlowControl => true;

            public BufferManager BufferManager { get; }

            public TimeProvider TimeProvider { get; }

            public async ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                SendStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }

            public void OnProtocolFault(DataChannelFrameError error)
            {
            }
        }

        private FakeTimeProvider m_timeProvider = null!;
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;
        private LoopbackTransport m_serverTransport = null!;
        private LoopbackTransport m_clientTransport = null!;
        private DataChannelManager m_server = null!;
        private DataChannelManager m_client = null!;

        private static readonly uint[] SingleLiveChannelId = [1u];
    }
}
