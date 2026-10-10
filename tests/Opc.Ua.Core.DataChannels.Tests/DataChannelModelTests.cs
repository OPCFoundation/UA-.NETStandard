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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Verifies the data channel AddressSpace projection helpers.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelModelTests
    {
        [Test]
        public void BuildCapabilitiesCopiesServerLimitsAndClampsActiveChannels()
        {
            DataChannelDeliveryMode[] modes =
            [
                DataChannelDeliveryMode.ReliableOrdered,
                DataChannelDeliveryMode.Unreliable
            ];
            string[] profiles =
            [
                Profiles.UaTcpTransport,
                Profiles.HttpsBinaryTransport
            ];
            var capabilities = new DataChannelServerCapabilities
            {
                MaxDataChannels = 32,
                MaxFrameSize = 131072,
                SupportedDeliveryModes = modes,
                SupportedTransportProfileUris = profiles,
                MaxCreditPerChannel = 4 * 1024 * 1024,
                MaxTotalBitrate = 10_000_000,
                SupportsUnreliableDatagrams = true,
                AllowInsecureDataChannels = true
            };

            DataChannelCapabilitiesValues values = DataChannelModel.BuildCapabilities(
                capabilities,
                activeChannelCount: ushort.MaxValue + 100);

            modes[0] = DataChannelDeliveryMode.ReliableUnordered;
            profiles[0] = "tampered";

            Assert.Multiple(() =>
            {
                Assert.That(values.MaxDataChannels, Is.EqualTo((ushort)32));
                Assert.That(values.MaxFrameSize, Is.EqualTo(131072u));
                Assert.That(
                    values.SupportedDeliveryModes,
                    Is.EqualTo(new[]
                    {
                        DataChannelDeliveryMode.ReliableOrdered,
                        DataChannelDeliveryMode.Unreliable
                    }));
                Assert.That(
                    values.SupportedTransportProfileUris,
                    Is.EqualTo(new[] { Profiles.UaTcpTransport, Profiles.HttpsBinaryTransport }));
                Assert.That(values.MaxCreditPerChannel, Is.EqualTo(4u * 1024u * 1024u));
                Assert.That(values.MaxTotalBitrate, Is.EqualTo(10_000_000u));
                Assert.That(values.SupportsUnreliableDatagrams, Is.True);
                Assert.That(values.AllowInsecureDataChannels, Is.True);
                Assert.That(values.ActiveChannelCount, Is.EqualTo(ushort.MaxValue));
            });
        }

        [Test]
        public void BuildCapabilitiesReportsActiveChannelsAsChannelsOpenAndClose()
        {
            using ModelHarness harness = new();
            var capabilities = new DataChannelServerCapabilities { MaxDataChannels = 4 };
            var sourceNodeId = new NodeId(9001u);

            DataChannelCapabilitiesValues empty = DataChannelModel.BuildCapabilities(
                capabilities,
                harness.Manager.ActiveChannelCount);

            harness.RegisterOpenChannel(1, sourceNodeId, new DataChannelSettings(), 0);
            harness.RegisterOpenChannel(2, sourceNodeId, new DataChannelSettings(), 0);
            DataChannelCapabilitiesValues opened = DataChannelModel.BuildCapabilities(
                capabilities,
                harness.Manager.ActiveChannelCount);

            harness.Manager.Remove(1);
            DataChannelCapabilitiesValues afterClose = DataChannelModel.BuildCapabilities(
                capabilities,
                harness.Manager.ActiveChannelCount);

            Assert.Multiple(() =>
            {
                Assert.That(empty.ActiveChannelCount, Is.Zero);
                Assert.That(opened.ActiveChannelCount, Is.EqualTo((ushort)2));
                Assert.That(afterClose.ActiveChannelCount, Is.EqualTo((ushort)1));
            });
        }

        [Test]
        public void BuildCapabilitiesForQuicBindingDoesNotAdvertiseUnreliableDatagrams()
        {
            var capabilities = new DataChannelServerCapabilities
            {
                SupportedDeliveryModes =
                [
                    DataChannelDeliveryMode.ReliableOrdered,
                    DataChannelDeliveryMode.ReliableUnordered
                ],
                SupportedTransportProfileUris = [DataChannelConstants.QuicTransportProfileUri],
                SupportsUnreliableDatagrams = false
            };

            DataChannelCapabilitiesValues values = DataChannelModel.BuildCapabilities(
                capabilities,
                activeChannelCount: 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    values.SupportedTransportProfileUris,
                    Is.EqualTo(new[] { DataChannelConstants.QuicTransportProfileUri }));
                Assert.That(values.SupportsUnreliableDatagrams, Is.False);
                Assert.That(
                    values.SupportedDeliveryModes,
                    Does.Not.Contain(DataChannelDeliveryMode.Unreliable));
                Assert.That(
                    values.SupportedDeliveryModes,
                    Does.Not.Contain(DataChannelDeliveryMode.PartiallyReliable));
            });
        }

        [Test]
        public void BuildCapabilitiesRejectsAbsentNoSupportCapabilities()
        {
            Assert.That(
                () => DataChannelModel.BuildCapabilities(null!, 0),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("capabilities"));
        }

        [Test]
        public async Task BuildDiagnosticsUpdatesAsTrafficFlows()
        {
            using LoopbackModelHarness harness = new();
            NodeId sourceNodeId = new(3001u);
            DataChannel source = harness.OpenPair(sourceNodeId, DataChannelDirection.SourceToSink);
            DataChannel sink = harness.Client.Channels[0];
            byte[] payload = [1, 2, 3, 4, 5];

            source.Write(
                payload,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd);
            using DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                .ConfigureAwait(false);

            var sourceDiagnostics = DataChannelModel.BuildDiagnostics(harness.Server, sourceNodeId);
            var sinkDiagnostics = DataChannelModel.BuildDiagnostics(harness.Client, sourceNodeId);

            Assert.Multiple(() =>
            {
                Assert.That(message, Is.Not.Null);
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(payload));
                Assert.That(sourceDiagnostics, Has.Count.EqualTo(1));
                Assert.That(sourceDiagnostics[0].ChannelId, Is.EqualTo(source.ChannelId));
                Assert.That(sourceDiagnostics[0].FramesSent, Is.EqualTo(1ul));
                Assert.That(sourceDiagnostics[0].BytesSent, Is.EqualTo((ulong)payload.Length));
                Assert.That(sourceDiagnostics[0].FramesReceived, Is.Zero);
                Assert.That(sinkDiagnostics, Has.Count.EqualTo(1));
                Assert.That(sinkDiagnostics[0].ChannelId, Is.EqualTo(sink.ChannelId));
                Assert.That(sinkDiagnostics[0].FramesReceived, Is.EqualTo(1ul));
                Assert.That(sinkDiagnostics[0].BytesReceived, Is.EqualTo((ulong)payload.Length));
                Assert.That(sinkDiagnostics[0].FramesSent, Is.Zero);
            });
        }

        [Test]
        public void BuildChannelStatusFiltersChannelsBySourceNode()
        {
            using ModelHarness harness = new();
            NodeId selectedSource = new(1001u);
            NodeId otherSource = new(1002u);

            harness.RegisterOpenChannel(
                7,
                selectedSource,
                new DataChannelSettings
                {
                    Direction = DataChannelDirection.Bidirectional,
                    ContentType = "application/json",
                    MaxFrameSize = 8192,
                    InitialCredit = 16384,
                    Priority = 4
                },
                transportChannelId: 1234);
            harness.RegisterOpenChannel(
                8,
                otherSource,
                new DataChannelSettings { ContentType = "application/octet-stream" },
                transportChannelId: 5678);

            var status = DataChannelModel.BuildChannelStatus(harness.Manager, selectedSource);

            Assert.That(status, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(status[0].ChannelId, Is.EqualTo(7u));
                Assert.That(status[0].SourceNodeId, Is.EqualTo(selectedSource));
                Assert.That(status[0].State, Is.EqualTo(DataChannelState.Open));
                Assert.That(status[0].Parameters.Direction, Is.EqualTo(DataChannelDirection.Bidirectional));
                Assert.That(status[0].Parameters.ContentType, Is.EqualTo("application/json"));
                Assert.That(status[0].Parameters.MaxFrameSize, Is.EqualTo(8192u));
                Assert.That(status[0].Parameters.InitialCredit, Is.EqualTo(16384u));
                Assert.That(status[0].Parameters.Priority, Is.EqualTo((byte)4));
                Assert.That(status[0].TransportChannelId, Is.EqualTo(1234ul));
                Assert.That(status[0].StartTime, Is.Not.EqualTo(default(DateTime)));
            });
        }

        [Test]
        public void BuildChannelStatusRejectsNullManager()
        {
            Assert.That(
                () => DataChannelModel.BuildChannelStatus(null!, new NodeId(1u)),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("manager"));
        }

        [Test]
        public void BuildDiagnosticsFiltersChannelsBySourceNode()
        {
            using ModelHarness harness = new();
            NodeId selectedSource = new(2001u);
            NodeId otherSource = new(2002u);

            harness.RegisterOpenChannel(11, selectedSource, new DataChannelSettings(), 111);
            harness.RegisterOpenChannel(12, selectedSource, new DataChannelSettings(), 222);
            harness.RegisterOpenChannel(13, otherSource, new DataChannelSettings(), 333);

            var diagnostics = DataChannelModel.BuildDiagnostics(harness.Manager, selectedSource);

            Assert.That(diagnostics, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(diagnostics[0].ChannelId, Is.EqualTo(11u));
                Assert.That(diagnostics[0].FramesSent, Is.Zero);
                Assert.That(diagnostics[0].FramesReceived, Is.Zero);
                Assert.That(diagnostics[0].BytesSent, Is.Zero);
                Assert.That(diagnostics[0].BytesReceived, Is.Zero);
                Assert.That(diagnostics[0].CreditStalls, Is.Zero);
                Assert.That(diagnostics[0].LastGapSequenceNumber, Is.Zero);
                Assert.That(diagnostics[1].ChannelId, Is.EqualTo(12u));
                Assert.That(diagnostics[1].FramesDiscarded, Is.Zero);
            });
        }

        [Test]
        public void BuildDiagnosticsRejectsNullManager()
        {
            Assert.That(
                () => DataChannelModel.BuildDiagnostics(null!, new NodeId(1u)),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("manager"));
        }

        [Test]
        public void BuildStateChangeEventCopiesTransitionValues()
        {
            var arguments = new DataChannelStateChangedEventArgs(
                99,
                DataChannelState.Faulted,
                StatusCodes.BadTimeout);

            DataChannelStateChangeValues values = DataChannelModel.BuildStateChangeEvent(arguments);

            Assert.Multiple(() =>
            {
                Assert.That(values.ChannelId, Is.EqualTo(99u));
                Assert.That(values.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(values.Status, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
            });
        }

        [Test]
        public void BuildStateChangeEventRejectsNullArguments()
        {
            Assert.That(
                () => DataChannelModel.BuildStateChangeEvent(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("arguments"));
        }

        private sealed class ModelHarness : IDisposable
        {
            public ModelHarness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var bufferManager = new BufferManager("model", 65536, telemetry);
                var transport = new LoopbackTransport(bufferManager, TimeProvider.System);
                Manager = new DataChannelManager(transport, true, telemetry);
            }

            public DataChannelManager Manager { get; }

            public void RegisterOpenChannel(
                uint channelId,
                NodeId sourceNodeId,
                DataChannelSettings settings,
                ulong transportChannelId)
            {
                Manager.Register(channelId, sourceNodeId, settings, isSource: true, transportChannelId);
                Manager.MarkOpen(channelId);
            }

            public void Dispose()
            {
                Manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private sealed class LoopbackModelHarness : IDisposable
        {
            public LoopbackModelHarness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                BufferManager = new BufferManager("model-loopback", 65536, telemetry);
                ServerTransport = new LoopbackTransport(BufferManager, TimeProvider.System);
                ClientTransport = new LoopbackTransport(BufferManager, TimeProvider.System);
                Server = new DataChannelManager(ServerTransport, true, telemetry);
                Client = new DataChannelManager(ClientTransport, false, telemetry);
                ServerTransport.Peer = Client;
                ClientTransport.Peer = Server;
            }

            public DataChannelManager Server { get; }

            public DataChannelManager Client { get; }

            private BufferManager BufferManager { get; }

            private LoopbackTransport ServerTransport { get; }

            private LoopbackTransport ClientTransport { get; }

            public DataChannel OpenPair(
                NodeId sourceNodeId,
                DataChannelDirection direction,
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

                Assert.That(Server.TryAllocateChannelId(out uint channelId), Is.True);

                DataChannel server = Server.Register(
                    channelId,
                    sourceNodeId,
                    settings,
                    isSource: true);

                Client.Register(
                    channelId,
                    sourceNodeId,
                    settings,
                    isSource: false);

                Server.MarkOpen(channelId);
                Client.MarkOpen(channelId);

                return server;
            }

            public void Dispose()
            {
                Client.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
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
    }
}
