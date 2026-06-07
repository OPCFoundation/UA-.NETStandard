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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Pcap.Bindings;

namespace Opc.Ua.Bindings.Pcap.Tests.Bindings
{
    [TestFixture]
    public sealed class CapturingMessageSocketTests
    {
        [Test]
        public void NotInstalledObserverIsNullAndHotPathReturnsTrue()
        {
            var registry = new ChannelCaptureRegistry();
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public void SendForwardsToInnerAndTapsBytesWhenObserverIsRegistered()
        {
            var registry = new ChannelCaptureRegistry();
            var inner = new RecordingMessageSocket();
            var sink = new RecordingFrameCaptureSink();
            using var socket = new CapturingMessageSocket(
                inner,
                registry,
                new TestSink(channelId: 0x4242));

            using var args = new StubAsyncEventArgs(new byte[] { 1, 2, 3, 4 });

            // No observer installed: forwarded but not tapped.
            socket.Send(args);
            Assert.That(inner.SendCount, Is.EqualTo(1));
            Assert.That(sink.SentChunks, Is.Empty);

            // Install observer; subsequent sends must tap.
            registry.SetObserver(sink);
            using var args2 = new StubAsyncEventArgs(new byte[] { 5, 6, 7 });
            socket.Send(args2);
            Assert.That(inner.SendCount, Is.EqualTo(2));
            Assert.That(sink.SentChunks, Has.Count.EqualTo(1));
            Assert.That(sink.SentChunks[0].ChannelId, Is.EqualTo(0x4242u));
            Assert.That(sink.SentChunks[0].Bytes, Is.EqualTo(new byte[] { 5, 6, 7 }));
        }

        [Test]
        public void IncomingChunksFromInnerAreTappedThroughWrappedSink()
        {
            var registry = new ChannelCaptureRegistry();
            var inner = new RecordingMessageSocket();
            var sink = new RecordingFrameCaptureSink();
            var originalSink = new TestSink(channelId: 0x99);
            using var socket = new CapturingMessageSocket(inner, registry, originalSink);
            registry.SetObserver(sink);

            // The inner socket records the sink the wrapper installed
            // via ChangeSink. Drive that sink to emulate an incoming
            // chunk.
            var payload = new byte[] { 0xAA, 0xBB };
            inner.LastSink!.OnMessageReceived(socket, new ArraySegment<byte>(payload));

            Assert.That(originalSink.Received, Has.Count.EqualTo(1));
            Assert.That(sink.ReceivedChunks, Has.Count.EqualTo(1));
            Assert.That(sink.ReceivedChunks[0].ChannelId, Is.EqualTo(0x99u));
            Assert.That(sink.ReceivedChunks[0].Bytes, Is.EqualTo(payload));
        }

        [Test]
        public void ObserverExceptionsDoNotBreakSendOrReceive()
        {
            var registry = new ChannelCaptureRegistry();
            var inner = new RecordingMessageSocket();
            var sink = new ThrowingFrameCaptureSink();
            using var socket = new CapturingMessageSocket(
                inner,
                registry,
                new TestSink(channelId: 1));
            registry.SetObserver(sink);

            Assert.DoesNotThrow(() =>
            {
                using var args = new StubAsyncEventArgs(new byte[] { 1, 2, 3 });
                socket.Send(args);
            });
            Assert.That(inner.SendCount, Is.EqualTo(1));

            Assert.DoesNotThrow(() =>
                inner.LastSink!.OnMessageReceived(socket, new ArraySegment<byte>(new byte[] { 4 })));
        }

        private sealed class RecordingMessageSocket : IMessageSocket
        {
            public int SendCount { get; private set; }
            public IMessageSink? LastSink { get; private set; }
            public int Handle => 0;
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;
            public TransportChannelFeatures MessageSocketFeatures => TransportChannelFeatures.None;
            public Task ConnectAsync(Uri endpointUrl, CancellationToken ct = default) => Task.CompletedTask;
            public void Close() { }
            public void Dispose() { }
            public void ReadNextMessage() { }
            public void ChangeSink(IMessageSink sink) => LastSink = sink;
            public bool Send(IMessageSocketAsyncEventArgs args) { SendCount++; return true; }
            public IMessageSocketAsyncEventArgs MessageSocketEventArgs() => new StubAsyncEventArgs(Array.Empty<byte>());
        }

        private sealed class TestSink : UaSCUaBinaryChannel
        {
            public TestSink(uint channelId)
                : base(
                    contextId: "test",
                    bufferManager: new BufferManager(
                        "test",
                        Opc.Ua.Bindings.TcpMessageLimits.DefaultMaxBufferSize,
                        DummyTelemetry.Instance),
                    quotas: new ChannelQuotas(ServiceMessageContext.CreateEmpty(DummyTelemetry.Instance)),
                    serverCertificate: null,
                    endpoints: null,
                    securityMode: MessageSecurityMode.None,
                    securityPolicyUri: SecurityPolicies.None,
                    telemetry: DummyTelemetry.Instance)
            {
                ChannelId = channelId;
            }

            public List<byte[]> Received { get; } = new();

            public override void OnMessageReceived(IMessageSocket source, ArraySegment<byte> message)
            {
                Received.Add(message.ToArray());
            }
        }

        private sealed class RecordingFrameCaptureSink : IFrameCaptureSink
        {
            public List<(uint ChannelId, byte[] Bytes)> SentChunks { get; } = new();
            public List<(uint ChannelId, byte[] Bytes)> ReceivedChunks { get; } = new();
            public List<(uint ChannelId, uint TokenId)> Tokens { get; } = new();

            public void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
                => SentChunks.Add((channelId, chunk.ToArray()));

            public void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
                => ReceivedChunks.Add((channelId, chunk.ToArray()));

            public void OnTokenActivated(
                uint channelId,
                ChannelToken currentToken,
                ChannelToken? previousToken)
                => Tokens.Add((channelId, currentToken.TokenId));
        }

        private sealed class ThrowingFrameCaptureSink : IFrameCaptureSink
        {
            public void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
                => throw new InvalidOperationException("boom-send");

            public void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
                => throw new InvalidOperationException("boom-recv");

            public void OnTokenActivated(
                uint channelId,
                ChannelToken currentToken,
                ChannelToken? previousToken)
                => throw new InvalidOperationException("boom-token");
        }

        private sealed class StubAsyncEventArgs : IMessageSocketAsyncEventArgs
        {
            private byte[]? m_buffer;
            private int m_offset;
            private int m_count;
            public StubAsyncEventArgs(byte[] buffer)
            {
                m_buffer = buffer;
                m_offset = 0;
                m_count = buffer.Length;
            }
            public byte[]? Buffer => m_buffer;
            public BufferCollection? BufferList { get; set; }
            public int BytesTransferred => m_count;
            public int Offset => m_offset;
            public int Count => m_count;
            public bool IsSocketError => false;
            public string SocketErrorString => string.Empty;
            public object? UserToken { get; set; }
            public event EventHandler<IMessageSocketAsyncEventArgs>? Completed { add { } remove { } }
            public void SetBuffer(byte[] buffer, int offset, int count)
            {
                m_buffer = buffer;
                m_offset = offset;
                m_count = count;
            }
            public void Dispose() { }
        }

        private sealed class DummyTelemetry : ITelemetryContext
        {
            public static DummyTelemetry Instance { get; } = new();
            public Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory
                => Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
            public System.Diagnostics.ActivitySource ActivitySource { get; } = new("test");
            public System.Diagnostics.Metrics.Meter CreateMeter() => new("test");
        }
    }
}
