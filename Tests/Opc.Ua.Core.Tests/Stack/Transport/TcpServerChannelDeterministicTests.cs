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

#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Socket-free, timer-free deterministic unit tests for
    /// <see cref="TcpServerChannel"/>. Every test drives the channel with a
    /// mocked <see cref="ITcpChannelListener"/>, crafted in-memory chunks and a
    /// fake byte transport that records emitted bytes. A
    /// <see cref="FakeTimeProvider"/> neutralizes activity timers so no
    /// wall-clock, socket, or accept-loop behaviour is ever exercised.
    /// </summary>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class TcpServerChannelDeterministicTests
    {
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_buffers = null!;
        private ChannelQuotas m_quotas = null!;
        private ServiceMessageContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            // The buffer manager must accommodate the channel's negotiated send
            // buffer (quotas.MaxBufferSize) or the reverse-hello / error writes
            // throw from BufferManager.TakeBuffer.
            m_buffers = new BufferManager(
                "server-det-test", TcpMessageLimits.MaxBufferSize, m_telemetry);
            m_context = ServiceMessageContext.Create(m_telemetry);
            m_quotas = new ChannelQuotas(m_context);
        }

        [Test]
        public void ChannelNameReturnsTcpServerChannelLiteral()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using TestServerChannel channel = BuildChannel(listenerMock);

            Assert.That(channel.ChannelName, Is.EqualTo("TCPSERVERCHANNEL"));
        }

        [Test]
        public void DisposeReleasesClientCertificate()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            TestServerChannel channel = BuildChannel(listenerMock);
            Certificate certificate = CreateSmallCertificate();
            channel.SelectedClientCertificate = certificate;

            channel.Dispose();

            // Dispose nulls the reference and releases the underlying handle, so
            // a subsequent AddRef on the released core must fail.
            Assert.That(channel.SelectedClientCertificate, Is.Null);
            Assert.That(() => certificate.AddRef(), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void BeginReverseConnectWithNullTransportThrowsArgumentNull()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using TestServerChannel channel = BuildChannel(listenerMock);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => channel.BeginReverseConnect(
                    channelId: 1u,
                    endpointUrl: new Uri("opc.tcp://localhost:4840"),
                    transport: null!,
                    callback: null!,
                    callbackData: null!,
                    timeout: 0))!;
            Assert.That(ex.ParamName, Is.EqualTo("transport"));
        }

        [Test]
        public void EndReverseConnectWithInvalidResultThrowsArgumentException()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using TestServerChannel channel = BuildChannel(listenerMock);

            // Task implements IAsyncResult but is not a ReverseConnectAsyncResult.
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => channel.EndReverseConnect((IAsyncResult)Task.CompletedTask))!;
            Assert.That(ex.ParamName, Is.EqualTo("result"));
        }

        [Test]
        public void SendResponseWithNullResponseThrowsArgumentNull()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using TestServerChannel channel = BuildChannel(listenerMock);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => channel.SendResponse(1u, null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("response"));
        }

        [Test]
        public void SendResponseOnClosedChannelThrowsBadSecureChannelClosed()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.CurrentState = TcpChannelState.Closed;

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => channel.SendResponse(1u, Mock.Of<IServiceResponse>()))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadSecureChannelClosed));
        }

        [Test]
        public async Task ProcessHelloMessageWithOversizedEndpointUrlSendsErrorAndFaultsAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            var transport = new RecordingByteTransport();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.SetTransport(transport);
            channel.CurrentState = TcpChannelState.Connecting;

            channel.FeedIncomingMessage(
                TcpMessageType.Hello,
                new ArraySegment<byte>(BuildHelloWithUrlLength(5000)));

            Assert.That(
                await CompletesWithinAsync(transport.FirstSendTask, 30).ConfigureAwait(false),
                Is.True,
                "channel never emitted the error message");
            Assert.That(
                DecodeErrorStatusCode(transport.LastSent),
                Is.EqualTo((uint)StatusCodes.BadTcpEndpointUrlInvalid));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
            listenerMock.Verify(l => l.ChannelClosed(0u), Times.Once());
        }

        [Test]
        public async Task ProcessHelloMessageWhileNotConnectingSendsErrorAndFaultsAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            var transport = new RecordingByteTransport();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.SetTransport(transport);
            // A Hello is only valid in the Connecting state; Opening triggers a fault.
            channel.CurrentState = TcpChannelState.Opening;

            channel.FeedIncomingMessage(
                TcpMessageType.Hello, new ArraySegment<byte>(Array.Empty<byte>()));

            Assert.That(
                await CompletesWithinAsync(transport.FirstSendTask, 30).ConfigureAwait(false),
                Is.True,
                "channel never emitted the error message");
            Assert.That(
                DecodeErrorStatusCode(transport.LastSent),
                Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
            listenerMock.Verify(l => l.ChannelClosed(0u), Times.Once());
        }

        [Test]
        public async Task HandleIncomingMessageWithUnknownTypeSendsErrorAndFaultsAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            var transport = new RecordingByteTransport();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.SetTransport(transport);
            channel.CurrentState = TcpChannelState.Connecting;

            channel.FeedIncomingMessage(0x00FFFFFFu, new ArraySegment<byte>(Array.Empty<byte>()));

            Assert.That(
                await CompletesWithinAsync(transport.FirstSendTask, 30).ConfigureAwait(false),
                Is.True,
                "channel never emitted the error message");
            Assert.That(
                DecodeErrorStatusCode(transport.LastSent),
                Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
            listenerMock.Verify(l => l.ChannelClosed(0u), Times.Once());
        }

        [Test]
        public void ProcessOpenAndRequestAndCloseMessagesRoundTrip()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            var transport = new RecordingByteTransport();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.SetTransport(transport);
            channel.CurrentState = TcpChannelState.Opening;

            uint openRequestId = 12u;
            channel.FeedIncomingMessage(
                TcpMessageType.Open,
                new ArraySegment<byte>(channel.BuildOpenSecureChannelRequest(openRequestId)));

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(transport.SendCount, Is.EqualTo(1));

            channel.SetRequestReceivedCallback((listener, requestId, request) =>
            {
                channel.SendResponse(
                    requestId,
                    new GetEndpointsResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            RequestHandle = request.RequestHeader.RequestHandle,
                            ServiceResult = StatusCodes.Good
                        }
                    });
            });

            uint serviceRequestId = 33u;
            channel.FeedIncomingMessage(
                TcpMessageType.Message,
                new ArraySegment<byte>(channel.BuildSymmetricRequest(
                    serviceRequestId,
                    new GetEndpointsRequest
                    {
                        RequestHeader = new RequestHeader(),
                        EndpointUrl = "opc.tcp://localhost:4840"
                    })));

            Assert.That(transport.SendCount, Is.EqualTo(2));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));

            uint closeRequestId = 44u;
            channel.FeedIncomingMessage(
                TcpMessageType.Close,
                new ArraySegment<byte>(channel.BuildSymmetricRequest(
                    closeRequestId,
                    new CloseSecureChannelRequest
                    {
                        RequestHeader = new RequestHeader()
                    })));

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            listenerMock.Verify(l => l.ChannelClosed(0u), Times.Once());
        }

        [Test]
        public void DoMessageLimitsExceededClosesChannelAndNotifiesListener()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.CurrentState = TcpChannelState.Opening;

            channel.ForceMessageLimitsExceeded();

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            listenerMock.Verify(l => l.ChannelClosed(0u), Times.Once());
        }

        [Test]
        public async Task BeginReverseConnectWritesReverseHelloMessageAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            var transport = new RecordingByteTransport();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.PresetEndpointDescription(new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                Server = new ApplicationDescription { ApplicationUri = "urn:test:server" }
            });

            // The fake ConnectAsync completes synchronously so OnReverseConnectComplete
            // runs inline and writes the ReverseHello chunk through the transport.
            channel.BeginReverseConnect(
                channelId: 77u,
                endpointUrl: new Uri("opc.tcp://localhost:4840"),
                transport: transport,
                callback: null!,
                callbackData: null!,
                timeout: 0);

            Assert.That(
                await CompletesWithinAsync(transport.FirstSendTask, 30).ConfigureAwait(false),
                Is.True,
                "channel never emitted the reverse hello message");

            byte[] captured = transport.LastSent;
            Assert.That(
                BinaryPrimitives.ReadUInt32LittleEndian(captured.AsSpan(0)),
                Is.EqualTo(TcpMessageType.ReverseHello));
            Assert.That(
                BinaryPrimitives.ReadUInt32LittleEndian(captured.AsSpan(4)),
                Is.EqualTo((uint)captured.Length));

            ReverseHelloMessage decoded = TcpMessageParsers.ReadReverseHelloMessage(
                new ArraySegment<byte>(captured, 8, captured.Length - 8));
            Assert.That(decoded.ServerUri, Is.EqualTo("urn:test:server"));
            Assert.That(decoded.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Connecting));
        }

        [Test]
        public async Task SendServiceFaultWritesOpenMessageWithFaultStatusAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            var transport = new RecordingByteTransport();
            using TestServerChannel channel = BuildChannel(listenerMock);
            channel.SetTransport(transport);
            channel.CurrentState = TcpChannelState.Opening;

            channel.CallSendServiceFault(
                requestId: 42u,
                renew: false,
                fault: new ServiceResult(StatusCodes.BadCertificateUntrusted));

            Assert.That(
                await CompletesWithinAsync(transport.FirstSendTask, 30).ConfigureAwait(false),
                Is.True,
                "channel never emitted the service fault message");
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Opening),
                "SendServiceFault must not fault the channel on the happy path");

            byte[] captured = transport.LastSent;
            uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(captured.AsSpan(0));
            Assert.That(messageType & 0x00FFFFFFu, Is.EqualTo(TcpMessageType.Open));
            Assert.That(
                DecodeServiceFaultStatusCode(captured),
                Is.EqualTo((uint)StatusCodes.BadCertificateUntrusted));
        }

        private TestServerChannel BuildChannel(Mock<ITcpChannelListener> listenerMock)
        {
            return new TestServerChannel(
                contextId: "server-det",
                listener: listenerMock.Object,
                bufferManager: m_buffers,
                quotas: m_quotas,
                endpoints: new List<EndpointDescription>(),
                telemetry: m_telemetry,
                timeProvider: new FakeTimeProvider());
        }

        private static Mock<ITcpChannelListener> CreateListenerMock()
        {
            var mock = new Mock<ITcpChannelListener>();
            mock.Setup(l => l.EndpointUrl)
                .Returns(new Uri("opc.tcp://localhost:4840"));
            mock.Setup(l => l.ChannelClosed(It.IsAny<uint>()));
            mock.Setup(l => l.TransferListenerChannelAsync(
                    It.IsAny<uint>(),
                    It.IsAny<string>(),
                    It.IsAny<Uri>()))
                .Returns(Task.FromResult(false));
            return mock;
        }

        private static async Task<bool> CompletesWithinAsync(Task task, int seconds)
        {
            Task completed = await Task
                .WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)))
                .ConfigureAwait(false);
            return ReferenceEquals(completed, task);
        }

        private byte[] BuildHello(string endpointUrl)
        {
            return BuildChunk(TcpMessageType.Hello, encoder =>
            {
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 0);
                byte[] bytes = Encoding.UTF8.GetBytes(endpointUrl);
                encoder.WriteInt32(null, bytes.Length);
                foreach (byte value in bytes)
                {
                    encoder.WriteByte(null, value);
                }
            });
        }

        private byte[] BuildHelloWithUrlLength(int urlLength)
        {
            return BuildChunk(TcpMessageType.Hello, encoder =>
            {
                encoder.WriteUInt32(null, 0); // protocol version
                encoder.WriteUInt32(null, 8192); // send buffer size
                encoder.WriteUInt32(null, 8192); // receive buffer size
                encoder.WriteUInt32(null, 0); // max message size
                encoder.WriteUInt32(null, 0); // max chunk count
                encoder.WriteInt32(null, urlLength);
            });
        }

        private byte[] BuildChunk(uint messageType, Action<BinaryEncoder> writeBody)
        {
            byte[] buffer = new byte[256];
            int size;
            using (var stream = new MemoryStream(buffer, 0, buffer.Length))
            using (var encoder = new BinaryEncoder(stream, m_context, false))
            {
                encoder.WriteUInt32(null, messageType);
                encoder.WriteUInt32(null, 0); // size placeholder
                writeBody(encoder);
                size = encoder.Close();
            }
            byte[] chunk = new byte[size];
            Array.Copy(buffer, chunk, size);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)size);
            return chunk;
        }

        private uint ReadAsymmetricRequestId(byte[] chunk)
        {
            using var decoder = new BinaryDecoder(new ArraySegment<byte>(chunk, 8, chunk.Length - 8), m_context);
            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadString(null);
            _ = decoder.ReadByteString(null);
            _ = decoder.ReadByteString(null);
            _ = decoder.ReadUInt32(null);
            return decoder.ReadUInt32(null);
        }

        private uint ReadSymmetricRequestId(byte[] chunk)
        {
            using var decoder = new BinaryDecoder(new ArraySegment<byte>(chunk, 8, chunk.Length - 8), m_context);
            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32(null);
            return decoder.ReadUInt32(null);
        }

        private static uint DecodeErrorStatusCode(byte[] chunk)
        {
            ErrorMessage error = TcpMessageParsers.ReadErrorMessage(
                new ArraySegment<byte>(chunk, 8, chunk.Length - 8));
            return error.StatusCode;
        }

        private uint DecodeServiceFaultStatusCode(byte[] chunk)
        {
            using var decoder = new BinaryDecoder(
                new ArraySegment<byte>(chunk, 8, chunk.Length - 8), m_context);
            _ = decoder.ReadUInt32(null); // secure channel id
            _ = decoder.ReadString(null); // security policy uri
            _ = decoder.ReadByteString(null); // sender certificate
            _ = decoder.ReadByteString(null); // receiver certificate thumbprint
            _ = decoder.ReadUInt32(null); // sequence number
            _ = decoder.ReadUInt32(null); // request id
            ServiceFault fault = decoder.DecodeMessage<ServiceFault>();
            return fault.ResponseHeader.ServiceResult.Code;
        }

        private static Certificate CreateSmallCertificate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=HyperTestServerSmall", ecdsa, HashAlgorithmName.SHA256);
            X509Certificate2 x509 = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return Certificate.From(x509);
        }

        private sealed class TestServerChannel : TcpServerChannel
        {
            public TestServerChannel(
                string contextId,
                ITcpChannelListener listener,
                BufferManager bufferManager,
                ChannelQuotas quotas,
                List<EndpointDescription> endpoints,
                ITelemetryContext telemetry,
                TimeProvider? timeProvider)
                : base(
                    contextId,
                    listener,
                    bufferManager,
                    quotas,
                    null!,
                    endpoints,
                    telemetry,
                    timeProvider)
            {
            }

            public TcpChannelState CurrentState
            {
                get => State;
                set => State = value;
            }

            public ChannelToken? CurrentTokenSnapshot => CurrentToken;

            public Certificate? SelectedClientCertificate
            {
                get => ClientCertificate;
                set => ClientCertificate = value;
            }

            public void PresetEndpointDescription(EndpointDescription endpoint)
            {
                EndpointDescription = endpoint;
            }

            public void SetTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            public bool FeedIncomingMessage(uint messageType, ArraySegment<byte> chunk)
            {
                return HandleIncomingMessage(messageType, chunk);
            }

            public void ForceMessageLimitsExceeded()
            {
                DoMessageLimitsExceeded();
            }

            public void CallSendServiceFault(uint requestId, bool renew, ServiceResult fault)
            {
                SendServiceFault(requestId, renew, fault);
            }

            public byte[] BuildOpenSecureChannelRequest(uint requestId)
            {
                var request = new OpenSecureChannelRequest
                {
                    RequestHeader = new RequestHeader
                    {
                        RequestHandle = 7,
                        Timestamp = DateTime.UtcNow
                    },
                    RequestType = SecurityTokenRequestType.Issue,
                    SecurityMode = MessageSecurityMode.None,
                    ClientNonce = Array.Empty<byte>().ToByteString(),
                    RequestedLifetime = 60000
                };

                return BuildAsymmetricChunk(requestId, request);
            }

            public byte[] BuildSymmetricRequest(uint requestId, object request)
            {
                ChannelToken token = CurrentTokenSnapshot ?? throw new InvalidOperationException(
                    "Current token is not available.");

                bool limitsExceeded;
                BufferCollection buffers = WriteSymmetricMessage(
                    TcpMessageType.Message,
                    requestId,
                    token,
                    request,
                    true,
                    out limitsExceeded);

                if (limitsExceeded)
                {
                    throw new InvalidOperationException("Test request exceeded message limits.");
                }

                return FlattenBuffers(buffers);
            }

            private byte[] BuildAsymmetricChunk(uint requestId, IEncodeable body)
            {
                BufferCollection buffers = WriteAsymmetricMessage(
                    TcpMessageType.Open,
                    requestId,
                    null,
                    null,
                    null,
                    new ArraySegment<byte>(BinaryEncoder.EncodeMessage(body, Quotas.MessageContext)),
                    null,
                    out _);

                return FlattenBuffers(buffers);
            }

            private byte[] FlattenBuffers(BufferCollection buffers)
            {
                try
                {
                    int total = buffers.TotalSize;
                    byte[] flattened = new byte[total];
                    int offset = 0;

                    foreach (ArraySegment<byte> segment in buffers)
                    {
                        Buffer.BlockCopy(segment.Array!, segment.Offset, flattened, offset, segment.Count);
                        offset += segment.Count;
                    }

                    return flattened;
                }
                finally
                {
                    buffers.Release(BufferManager, nameof(TestServerChannel));
                }
            }
        }

        private sealed class RecordingByteTransport : IUaSCByteTransport
        {
            private readonly object m_lock = new();
            private readonly List<byte[]> m_sent = new();
            private readonly TaskCompletionSource<bool> m_closed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_firstSend =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public EndPoint? LocalEndpoint => null;

            public EndPoint? RemoteEndpoint => null;

            public TransportChannelFeatures Features => default;

            public string Implementation => "UA-FAKE";

            public Task FirstSendTask => m_firstSend.Task;

            public int SendCount
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_sent.Count;
                    }
                }
            }

            public byte[] LastSent
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_sent[m_sent.Count - 1];
                    }
                }
            }

            public byte[] GetSent(int index)
            {
                lock (m_lock)
                {
                    return m_sent[index];
                }
            }

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                Record(chunk.ToArray());
                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                int total = 0;
                foreach (ArraySegment<byte> segment in buffers)
                {
                    total += segment.Count;
                }

                byte[] flattened = new byte[total];
                int offset = 0;
                foreach (ArraySegment<byte> segment in buffers)
                {
                    Buffer.BlockCopy(segment.Array!, segment.Offset, flattened, offset, segment.Count);
                    offset += segment.Count;
                }

                Record(flattened);
                return default;
            }

            public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                var cancelled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true),
                    cancelled))
                {
                    await Task.WhenAny(m_closed.Task, cancelled.Task).ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed, "fake transport closed");
            }

            public void Close()
            {
                m_closed.TrySetResult(true);
            }

            private void Record(byte[] bytes)
            {
                lock (m_lock)
                {
                    m_sent.Add(bytes);
                }

                m_firstSend.TrySetResult(true);
            }
        }
    }
}
