/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
    public sealed class DataChannelConformanceTests
    {
        [SetUp]
        public void SetUp()
        {
            m_timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("data-channel-conformance", 65536, m_telemetry);
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
        public async Task ConnectionLevelMaxDataChannelsCountsOnlyChannelsOpenAtOnce()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, maxDataChannels: 2);

            Assert.That(manager.TryAllocateChannelId(out uint firstId), Is.True);
            DataChannel first = RegisterOpen(manager, firstId);
            Assert.That(manager.TryAllocateChannelId(out uint secondId), Is.True);
            DataChannel second = RegisterOpen(manager, secondId);

            Assert.That(manager.TryAllocateChannelId(out uint exhausted), Is.False);
            Assert.That(exhausted, Is.Zero);

            first.Reset(StatusCodes.Good);
            await PumpSchedulerRoundAsync(manager).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(manager.ActiveChannelCount, Is.EqualTo(1));
                Assert.That(manager.Channels.Select(c => c.ChannelId), Is.EqualTo(new[] { secondId }));
                Assert.That(manager.TryAllocateChannelId(out uint thirdId), Is.True);
                Assert.That(thirdId, Is.GreaterThan(secondId));
            });
        }

        [Test]
        public async Task SequentialOpenCloseCyclesDoNotExhaustTheConnectionChannelLimit()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport, maxDataChannels: 16);
            uint previous = 0;

            for (int ii = 0; ii < 16; ii++)
            {
                Assert.That(manager.TryAllocateChannelId(out uint channelId), Is.True);
                Assert.That(channelId, Is.GreaterThan(previous));
                previous = channelId;

                DataChannel channel = RegisterOpen(manager, channelId);
                channel.Reset(StatusCodes.Good);
                await PumpSchedulerRoundAsync(manager).ConfigureAwait(false);
            }

            Assert.Multiple(() =>
            {
                Assert.That(manager.ActiveChannelCount, Is.Zero);
                Assert.That(manager.Channels, Is.Empty);
                Assert.That(manager.TryAllocateChannelId(out uint reopened), Is.True);
                Assert.That(reopened, Is.GreaterThan(previous));
            });
        }

        [Test]
        public async Task InlineUnknownFrameTypeEmitsReset()
        {
            await InlineDecodeChannelScopedFrameFaultsEmitReset(
                    7,
                    DataChannelFrameError.UnknownFrameType,
                    StatusCodes.BadDataChannelFrameTypeUnsupported)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task InlinePayloadOnNonDataFrameEmitsReset()
        {
            await InlineDecodeChannelScopedFrameFaultsEmitReset(
                    (byte)DataChannelFrameType.End,
                    DataChannelFrameError.PayloadOnNonDataFrame,
                    StatusCodes.BadDataChannelFrameInvalid)
                .ConfigureAwait(false);
        }

        private static async Task InlineDecodeChannelScopedFrameFaultsEmitReset(
            byte frameType,
            DataChannelFrameError expectedDecodeError,
            StatusCode expectedStatus)
        {
            using var channel = TestChannel.Create("data-channel-conformance-inline-reset");
            var transport = new CapturingByteTransport();
            channel.AttachTransport(transport);
            channel.Activate(SecureChannelId, TokenId);
            DataChannel sink = OpenSink(channel);
            await PumpSchedulerRoundAsync(channel.DataChannels!).ConfigureAwait(false);
            transport.ClearChunks();

            byte[] chunk = SpecVectors.Load("inline_data_first");
            chunk[SpecVectors.InlinePrefix + 4] = frameType;

            Assert.That(channel.DispatchStream(chunk), Is.False);
            await PumpSchedulerRoundAsync(channel.DataChannels!).ConfigureAwait(false);

            DataChannelFrame reset = await WaitForOutboundFrameAsync(transport, DataChannelFrameType.Reset)
                .ConfigureAwait(false);
            DataChannelFrame[] frames = DecodeOutboundFrames(transport);

            Assert.Multiple(() =>
            {
                Assert.That(channel.ProtocolFaults, Is.Empty);
                Assert.That(sink.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(sink.Status, Is.EqualTo(expectedStatus));
                Assert.That(frames.Count(f => f.FrameType == DataChannelFrameType.Reset), Is.EqualTo(1));
                Assert.That(reset.FrameType, Is.EqualTo(DataChannelFrameType.Reset));
                Assert.That(reset.ChannelId, Is.EqualTo(DataChannelId));
                Assert.That(reset.Status, Is.EqualTo(expectedStatus));
                Assert.That(expectedDecodeError.ToStatusCode(), Is.EqualTo(expectedStatus));
            });

            await channel.DataChannels!.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public void DecodeChannelScopedFrameFaultsPreserveTheWireChannelId()
        {
            byte[] unknown = BuildHeader(77, (DataChannelFrameType)7, 0, 1);
            byte[] endWithPayload = new byte[DataChannelConstants.StreamHeaderSize + 1];
            BuildHeader(88, DataChannelFrameType.End, 0, 1).CopyTo(endWithPayload, 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(
                        unknown,
                        0,
                        out DataChannelFrame unknownFrame,
                        out DataChannelFrameError unknownError),
                    Is.False);
                Assert.That(unknownError, Is.EqualTo(DataChannelFrameError.UnknownFrameType));
                Assert.That(unknownFrame.ChannelId, Is.EqualTo(77u));

                Assert.That(
                    DataChannelFrameCodec.TryDecode(
                        endWithPayload,
                        0,
                        out DataChannelFrame invalidFrame,
                        out DataChannelFrameError invalidError),
                    Is.False);
                Assert.That(invalidError, Is.EqualTo(DataChannelFrameError.PayloadOnNonDataFrame));
                Assert.That(invalidFrame.ChannelId, Is.EqualTo(88u));
            });
        }

        [Test]
        public void BufferedResetReplaysAsResetWhenPendingOpenCompletes()
        {
            var transport = new LoopbackTransport(m_bufferManager, m_timeProvider);
            DataChannelManager manager = CreateManager(transport);
            DataChannel channel = manager.Register(
                55,
                new NodeId(1u),
                DefaultSettings(),
                isSource: false);

            manager.HandleFrame(DataChannelFrame.Reset(
                channel.ChannelId,
                1,
                StatusCodes.BadDataChannelFrameInvalid));
            manager.MarkOpen(channel.ChannelId);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    channel.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadDataChannelFrameInvalid));
                Assert.That(transport.Faults, Is.Empty);
            });
        }

        [Test]
        public async Task UnknownChannelBufferIsBoundedByEncodedFrameBytes()
        {
            var transport = new LoopbackTransport(
                m_bufferManager,
                m_timeProvider,
                maxFrameBodySize: DataChannelConstants.StreamHeaderSize + 8);
            DataChannelManager manager = CreateManager(transport);
            DataChannel pending = manager.Register(
                77,
                new NodeId(1u),
                DefaultSettings(),
                isSource: false);
            manager.Remove(pending.ChannelId);

            // DATA rather than PING: a frame carrying an 8 byte payload encodes
            // to exactly the same size as a PING, so the bound arithmetic is
            // unchanged, but the count of frames that survived the buffer is
            // then observable directly. Counting the PONGs a replayed PING
            // burst produces would measure the §5.11 ping rate limit instead of
            // the buffer bound this test is about.
            byte[] payload = new byte[8];

            for (uint sequence = 1; sequence <= 5; sequence++)
            {
                manager.HandleFrame(DataChannelFrame.Data(
                    pending.ChannelId,
                    sequence,
                    DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                    payload));
            }

            DataChannel channel = manager.Register(
                pending.ChannelId,
                new NodeId(1u),
                DefaultSettings(),
                isSource: false);
            manager.MarkOpen(channel.ChannelId);
            await PumpSchedulerRoundAsync(manager).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.GetDiagnostics().FramesReceived, Is.EqualTo(4ul));
                Assert.That(transport.Faults, Is.Empty);
            });
        }

        [Test]
        public void MaxBitrateIsRevisedAgainstTheSourceLimit()
        {
            var requested = new DataChannelParametersDataType { MaxBitrate = 0 };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    new DataChannelSourceCapabilities
                    {
                        Direction = DataChannelDirection.SourceToSink,
                        SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                        MaxFrameSize = 4096,
                        MaxBitrate = 64_000
                    },
                    new DataChannelServerCapabilities
                    {
                        MaxFrameSize = 4096,
                        SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered]
                    },
                    4096,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(revised.MaxBitrate, Is.EqualTo(64_000u));
        }

        private DataChannelManager CreateManager(
            IDataChannelTransport transport,
            ushort maxDataChannels = 16)
        {
            var manager = new DataChannelManager(
                transport,
                isServer: true,
                m_telemetry,
                maxDataChannels);
            m_managers.Add(manager);
            return manager;
        }

        private static DataChannel RegisterOpen(DataChannelManager manager, uint channelId)
        {
            DataChannel channel = manager.Register(
                channelId,
                new NodeId(1u),
                DefaultSettings(),
                isSource: true);
            manager.MarkOpen(channelId);
            return channel;
        }

        private static DataChannelSettings DefaultSettings()
        {
            return new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = 4096,
                InitialCredit = 65536
            };
        }

        private static async Task PumpSchedulerRoundAsync(DataChannelManager manager)
        {
            MethodInfo method = typeof(DataChannelManager).GetMethod(
                "RunRoundAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var ready = new List<DataChannel>();
            var result = (ValueTask)method.Invoke(manager, [ready, CancellationToken.None])!;
            await result.ConfigureAwait(false);

            // A channel scoped fault on a channel the round does not visit
            // queues its RESET on the connection control queue, which the
            // round itself does not drain. Without this the frame is still
            // queued when the assertion runs, and whether it has been written
            // becomes a matter of timing rather than of behaviour.
            MethodInfo drain = typeof(DataChannelManager).GetMethod(
                "DrainControlQueueAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var drained = (ValueTask)drain.Invoke(manager, [CancellationToken.None])!;
            await drained.ConfigureAwait(false);
        }

        private static DataChannel OpenSink(TestChannel channel)
        {
            DataChannelManager manager = channel.EnableDataChannels(
                isServer: false,
                NUnitTelemetryContext.Create());
            channel.TrackProtocolFaults();

            DataChannel dataChannel = manager.Register(
                DataChannelId,
                new NodeId(1u),
                DefaultSettings(),
                isSource: false);

            manager.MarkOpen(DataChannelId);
            return dataChannel;
        }

        private static DataChannelFrame[] DecodeOutboundFrames(CapturingByteTransport transport)
        {
            byte[][] chunks = transport.SnapshotChunks();
            Assert.That(chunks, Has.Length.GreaterThan(0));
            return [.. chunks.Select(chunk =>
            {
                Assert.That(
                    DataChannelFrameCodec.TryDecode(
                        SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                        0,
                        out DataChannelFrame frame,
                        out DataChannelFrameError error),
                    Is.True,
                    error.ToString());
                return frame;
            })];
        }

        private static async Task<DataChannelFrame> WaitForOutboundFrameAsync(
            CapturingByteTransport transport,
            DataChannelFrameType frameType)
        {
            var stopwatch = Stopwatch.StartNew();
            DataChannelFrame[] frames = [];

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                byte[][] chunks = transport.SnapshotChunks();
                frames = [.. chunks.Select(chunk =>
                {
                    Assert.That(
                        DataChannelFrameCodec.TryDecode(
                            SpecVectors.Body(chunk, SpecVectors.InlinePrefix),
                            0,
                            out DataChannelFrame frame,
                            out DataChannelFrameError error),
                        Is.True,
                        error.ToString());
                    return frame;
                })];

                DataChannelFrame[] matches = [.. frames.Where(frame => frame.FrameType == frameType)];

                if (matches.Length > 0)
                {
                    return matches[0];
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.Fail(
                $"Timed out waiting for outbound {frameType} frame. " +
                $"Decoded frames: {string.Join(", ", frames.Select(frame => frame.FrameType))}.");
            return default;
        }

        private static byte[] BuildHeader(
            uint channelId,
            DataChannelFrameType frameType,
            byte flags,
            uint frameSequenceNumber)
        {
            byte[] body = new byte[DataChannelConstants.StreamHeaderSize];
            BitConverter.GetBytes(channelId).CopyTo(body, 0);
            body[4] = (byte)frameType;
            body[5] = flags;
            BitConverter.GetBytes(frameSequenceNumber).CopyTo(body, 8);
            return body;
        }

        private sealed class TestChannel : UaSCUaBinaryChannel
        {
            private TestChannel(
                string contextId,
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry)
                : base(
                    contextId,
                    bufferManager,
                    quotas,
                    serverCertificates: null,
                    endpoints: null,
                    securityMode: MessageSecurityMode.None,
                    securityPolicyUri: SecurityPolicies.None,
                    telemetry: telemetry)
            {
            }

            public List<DataChannelFrameError> ProtocolFaults => m_protocolFaults;

            public static TestChannel Create(string contextId)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                return new TestChannel(
                    contextId,
                    new BufferManager(contextId, TcpMessageLimits.DefaultMaxBufferSize, telemetry),
                    new ChannelQuotas(ServiceMessageContext.CreateEmpty(telemetry)),
                    telemetry);
            }

            public void Activate(uint channelId, uint tokenId)
            {
                ChannelId = channelId;
                ChannelToken token = CreateToken();
                token.TokenId = tokenId;
                ActivateToken(token);
            }

            public bool DispatchStream(byte[] chunk)
            {
                return ProcessDataChannelMessage(
                    BitConverter.ToUInt32(chunk, 0),
                    new ArraySegment<byte>(chunk),
                    isRequest: true);
            }

            public void AttachTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            /// <summary>
            /// Records the typed framing faults the channel raises. A transport
            /// error alone does not carry which rule was broken.
            /// </summary>
            public void TrackProtocolFaults()
            {
                DataChannelProtocolFault += (_, error) => m_protocolFaults.Add(error);
            }

            private readonly List<DataChannelFrameError> m_protocolFaults = [];
        }

        private sealed class CapturingByteTransport : IUaSCByteTransport
        {
            public EndPoint? LocalEndpoint => null;

            public EndPoint? RemoteEndpoint => null;

            public TransportChannelFeatures Features => TransportChannelFeatures.None;

            public string Implementation => "test";

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                lock (m_lock)
                {
                    m_chunks.Add(chunk.ToArray());
                }

                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                byte[] chunk = new byte[buffers.Sum(segment => segment.Count)];
                int offset = 0;

                foreach (ArraySegment<byte> segment in buffers)
                {
                    segment.AsSpan().CopyTo(chunk.AsSpan(offset, segment.Count));
                    offset += segment.Count;
                }

                lock (m_lock)
                {
                    m_chunks.Add(chunk);
                }

                return default;
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public void Close()
            {
            }

            public void ClearChunks()
            {
                lock (m_lock)
                {
                    m_chunks.Clear();
                }
            }

            public byte[][] SnapshotChunks()
            {
                lock (m_lock)
                {
                    return [.. m_chunks];
                }
            }

            private readonly List<byte[]> m_chunks = [];
            private readonly Lock m_lock = new();
        }

        private const uint SecureChannelId = 0x0000A17C;
        private const uint TokenId = 7;
        private const uint DataChannelId = 1;

        private FakeTimeProvider m_timeProvider = null!;
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;
        private readonly List<DataChannelManager> m_managers = [];
    }
}
