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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Bindings;

namespace Opc.Ua.Bindings.Pcap.Tests.Bindings
{
    [TestFixture]
    public sealed class CapturingMessageSocketFactoryTests
    {
        [Test]
        public void CtorThrowsOnNullInner()
        {
            var registry = new ChannelCaptureRegistry();

            Assert.That(
                () => new CapturingMessageSocketFactory(inner: null!, registry),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("inner"));
        }

        [Test]
        public void CtorThrowsOnNullRegistry()
        {
            IMessageSocketFactory inner = Mock.Of<IMessageSocketFactory>();

            Assert.That(
                () => new CapturingMessageSocketFactory(inner, registry: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("registry"));
        }

        [Test]
        public void ImplementationStringAppendsPcapToInnerImplementation()
        {
            var innerMock = new Mock<IMessageSocketFactory>();
            innerMock.SetupGet(x => x.Implementation).Returns("tcp");
            var registry = new ChannelCaptureRegistry();

            var factory = new CapturingMessageSocketFactory(innerMock.Object, registry);

            Assert.That(factory.Implementation, Is.EqualTo("tcp+pcap"));
        }

        [Test]
        public void ImplementationReflectsInnerEvenWhenInnerIsEmptyString()
        {
            var innerMock = new Mock<IMessageSocketFactory>();
            innerMock.SetupGet(x => x.Implementation).Returns(string.Empty);
            var registry = new ChannelCaptureRegistry();

            var factory = new CapturingMessageSocketFactory(innerMock.Object, registry);

            Assert.That(factory.Implementation, Is.EqualTo("+pcap"));
        }

        [Test]
        public void CreateForwardsToInnerFactoryAndWrapsResult()
        {
            using var innerSocket = new RecordingMessageSocket();
            var innerFactoryMock = new Mock<IMessageSocketFactory>();
            innerFactoryMock
                .Setup(x => x.Create(It.IsAny<IMessageSink>(), It.IsAny<BufferManager>(), It.IsAny<int>()))
                .Returns(innerSocket);
            var registry = new ChannelCaptureRegistry();
            var factory = new CapturingMessageSocketFactory(
                innerFactoryMock.Object,
                registry,
                NullLoggerFactory.Instance);

            using var sink = new StubChannelSink(channelId: 0xDEADBEEF);
            var bufferManager = new BufferManager(
                "factory-tests",
                TcpMessageLimits.DefaultMaxBufferSize,
                TestTelemetryContext.Instance);
            IMessageSocket socket = factory.Create(sink, bufferManager, 8192);

            Assert.That(socket, Is.Not.Null);
            Assert.That(socket, Is.TypeOf<CapturingMessageSocket>(),
                "Factory must wrap the inner socket in a CapturingMessageSocket.");
            innerFactoryMock.Verify(
                x => x.Create(sink, bufferManager, 8192),
                Times.Once,
                "The capturing factory must invoke the inner factory exactly once with the same arguments.");
            socket.Dispose();
        }

        [Test]
        public void CreatedSocketTapsBytesOnSendWhenObserverInstalled()
        {
            using var innerSocket = new RecordingMessageSocket();
            var innerFactoryMock = new Mock<IMessageSocketFactory>();
            innerFactoryMock
                .Setup(x => x.Create(It.IsAny<IMessageSink>(), It.IsAny<BufferManager>(), It.IsAny<int>()))
                .Returns(innerSocket);
            var registry = new ChannelCaptureRegistry();
            var factory = new CapturingMessageSocketFactory(
                innerFactoryMock.Object,
                registry);
            using var sink = new StubChannelSink(channelId: 0xABCD);
            var bufferManager = new BufferManager(
                "factory-tests-tap",
                TcpMessageLimits.DefaultMaxBufferSize,
                TestTelemetryContext.Instance);
            using IMessageSocket socket = factory.Create(sink, bufferManager, 8192);

            var observer = new RecordingSink();
            registry.SetObserver(observer);
            using var args = new StubAsyncEventArgs([0x10, 0x20, 0x30]);
            socket.Send(args);

            Assert.That(innerSocket.SendCount, Is.EqualTo(1));
            Assert.That(observer.SentChunks, Has.Count.EqualTo(1));
            Assert.That(observer.SentChunks[0].ChannelId, Is.EqualTo(0xABCDu));
            Assert.That(observer.SentChunks[0].Bytes, Is.EqualTo(new byte[] { 0x10, 0x20, 0x30 }));
        }

        // ----- helpers -----

        private sealed class RecordingMessageSocket : IMessageSocket
        {
            public int SendCount { get; private set; }

            public int Handle => 0;
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;
            public TransportChannelFeatures MessageSocketFeatures => TransportChannelFeatures.None;

            public Task ConnectAsync(Uri endpointUrl, CancellationToken ct = default)
            {
                return Task.CompletedTask;
            }

            public void Close()
            {
            }

            public void Dispose()
            {
            }

            public void ReadNextMessage()
            {
            }

            public void ChangeSink(IMessageSink sink)
            {
            }

            public bool Send(IMessageSocketAsyncEventArgs args)
            {
                SendCount++;
                return true;
            }

            public IMessageSocketAsyncEventArgs MessageSocketEventArgs()
            {
                return new StubAsyncEventArgs([]);
            }
        }

        private sealed class StubChannelSink : UaSCUaBinaryChannel
        {
            public StubChannelSink(uint channelId)
                : base(
                    contextId: "factory-tests",
                    bufferManager: new BufferManager(
                        "factory-tests",
                        TcpMessageLimits.DefaultMaxBufferSize,
                        TestTelemetryContext.Instance),
                    quotas: new ChannelQuotas(
                        ServiceMessageContext.CreateEmpty(TestTelemetryContext.Instance)),
                    serverCertificate: null,
                    endpoints: null,
                    securityMode: MessageSecurityMode.None,
                    securityPolicyUri: SecurityPolicies.None,
                    telemetry: TestTelemetryContext.Instance)
            {
                ChannelId = channelId;
            }

            public override void OnMessageReceived(IMessageSocket source, ArraySegment<byte> message)
            {
            }
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
            public event EventHandler<IMessageSocketAsyncEventArgs>? Completed
            {
                add { }
                remove { }
            }

            public void SetBuffer(byte[] buffer, int offset, int count)
            {
                m_buffer = buffer;
                m_offset = offset;
                m_count = count;
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingSink : IFrameCaptureSink
        {
            public List<(uint ChannelId, byte[] Bytes)> SentChunks { get; } = [];

            public void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
            {
                SentChunks.Add((channelId, chunk.ToArray()));
            }

            public void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
            {
            }

            public void OnTokenActivated(uint channelId, ChannelToken currentToken, ChannelToken? previousToken)
            {
            }
        }
    }
}
