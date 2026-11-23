// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;

namespace Opc.Ua.Pcap.Tests.Bindings
{
    [TestFixture]
    public sealed class CapturingByteTransportFactoryTests
    {
        [Test]
        public void ConstructorRejectsNulls()
        {
            var registry = new ChannelCaptureRegistry();
            Assert.That(
                () => new CapturingByteTransportFactory(null!, registry),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new CapturingByteTransportFactory(new RecordingFactory(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CreateWrapsInnerTransportInCapturingDecorator()
        {
            var registry = new ChannelCaptureRegistry();
            var inner = new RecordingFactory();
            var factory = new CapturingByteTransportFactory(inner, registry);
            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();
            var buffers = new BufferManager("test", 8192, telemetry);

            IUaSCByteTransport transport = factory.Create(buffers, 8192, telemetry);
            try
            {
                Assert.That(transport, Is.InstanceOf<CapturingByteTransport>());
                Assert.That(inner.CreateCount, Is.EqualTo(1));
            }
            finally
            {
                (transport as IDisposable)?.Dispose();
            }
        }

        [Test]
        public async Task CreateAssignsDistinctSyntheticFlowIdsToWrappedTransportsAsync()
        {
            var registry = new ChannelCaptureRegistry();
            var sink = new RecordingFrameCaptureSink();
            registry.SetObserver(sink);
            var factory = new CapturingByteTransportFactory(
                new RecordingFactory(),
                registry);
            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();
            var buffers = new BufferManager("test", 8192, telemetry);

            IUaSCByteTransport first = factory.Create(buffers, 8192, telemetry);
            IUaSCByteTransport second = factory.Create(buffers, 8192, telemetry);
            try
            {
                await first.SendChunkAsync(new byte[] { 1 }, CancellationToken.None)
                    .ConfigureAwait(false);
                await second.SendChunkAsync(new byte[] { 2 }, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(sink.SentChunks, Has.Count.EqualTo(2));
                Assert.That(sink.SentChunks[0].ChannelId, Is.Not.Zero);
                Assert.That(sink.SentChunks[1].ChannelId, Is.Not.Zero);
                Assert.That(
                    sink.SentChunks[0].ChannelId,
                    Is.Not.EqualTo(sink.SentChunks[1].ChannelId));
            }
            finally
            {
                (first as IDisposable)?.Dispose();
                (second as IDisposable)?.Dispose();
            }
        }

        [Test]
        public void ImplementationStringIncludesPcapSuffix()
        {
            var factory = new CapturingByteTransportFactory(
                new RecordingFactory(),
                new ChannelCaptureRegistry());
            Assert.That(factory.Implementation, Is.EqualTo("UA-TEST+pcap"));
        }

        private sealed class RecordingFactory : IUaSCByteTransportFactory
        {
            public int CreateCount { get; private set; }

            public string Implementation => "UA-TEST";

            public IUaSCByteTransport Create(
                BufferManager bufferManager,
                int receiveBufferSize,
                ITelemetryContext telemetry)
            {
                CreateCount++;
                return new NoopTransport();
            }
        }

        private sealed class NoopTransport : IUaSCByteTransport, IDisposable
        {
            public string Implementation => "UA-NOOP";
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return default;
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return new(new ArraySegment<byte>([]));
            }

            public void Close()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingFrameCaptureSink : IFrameCaptureSink
        {
            public System.Collections.Generic.List<(uint ChannelId, byte[] Bytes)> SentChunks { get; } = [];

            public void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
            {
                SentChunks.Add((channelId, chunk.ToArray()));
            }

            public void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
            {
            }

            public void OnTokenActivated(
                uint channelId,
                ChannelToken currentToken,
                ChannelToken? previousToken)
            {
            }
        }
    }
}
