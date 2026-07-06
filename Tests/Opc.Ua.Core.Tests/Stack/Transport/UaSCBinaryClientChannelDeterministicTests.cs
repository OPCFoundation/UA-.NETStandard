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
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Socket-free, timer-free deterministic unit tests for
    /// <see cref="UaSCUaBinaryClientChannel"/>. Every test drives the channel
    /// with crafted in-memory chunks and a fake byte transport; a
    /// <see cref="FakeTimeProvider"/> neutralizes reconnect/handshake timers so
    /// no wall-clock, socket, or accept-loop behaviour is ever exercised.
    /// </summary>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class UaSCBinaryClientChannelDeterministicTests
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
            // buffer (quotas.MaxBufferSize) or SendHelloMessage's TakeBuffer throws.
            m_buffers = new BufferManager(
                "client-det-test", TcpMessageLimits.MaxBufferSize, m_telemetry);
            m_context = ServiceMessageContext.Create(m_telemetry);
            m_quotas = new ChannelQuotas(m_context);
        }

        [Test]
        public void ConstructorWithNullEndpointThrowsArgumentException()
        {
            var factory = new RecordingByteTransportFactory();

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => _ = new TestClientChannel(
                    m_buffers, factory, m_quotas, null, null, m_telemetry, new FakeTimeProvider()))!;
            Assert.That(ex.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void ConstructorWithNullEndpointUrlThrowsArgumentException()
        {
            var factory = new RecordingByteTransportFactory();
            var endpoint = new EndpointDescription { EndpointUrl = null };

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => _ = new TestClientChannel(
                    m_buffers, factory, m_quotas, null, endpoint, m_telemetry, new FakeTimeProvider()))!;
            Assert.That(ex.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void ConstructorWithSecurityButNoClientCertificateThrowsArgumentNull()
        {
            var factory = new RecordingByteTransportFactory();
            EndpointDescription endpoint = BuildEndpoint(
                MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => _ = new TestClientChannel(
                    m_buffers, factory, m_quotas, null, endpoint, m_telemetry, new FakeTimeProvider()))!;
            Assert.That(ex.ParamName, Is.EqualTo("clientCertificate"));
        }

        [Test]
        public void ConstructorWithOversizedClientCertificateThrowsArgumentException()
        {
            var factory = new RecordingByteTransportFactory();
            EndpointDescription endpoint = BuildEndpoint(
                MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256);
            Certificate oversized = CreateOversizedCertificate();

            Assert.That(
                oversized.RawData,
                Has.Length.GreaterThan(TcpMessageLimits.MaxCertificateSize),
                "test certificate must exceed the DER size limit");

            try
            {
                ArgumentException ex = Assert.Throws<ArgumentException>(
                    () => _ = new TestClientChannel(
                        m_buffers, factory, m_quotas, oversized, endpoint, m_telemetry,
                        new FakeTimeProvider()))!;
                Assert.That(ex.ParamName, Is.EqualTo("clientCertificate"));
                Assert.That(ex.Message, Does.Contain("7500"));
            }
            finally
            {
                oversized.Dispose();
            }
        }

        [Test]
        public void ConstructorUnsecuredDisposesSuppliedClientCertificate()
        {
            var factory = new RecordingByteTransportFactory();
            EndpointDescription endpoint = BuildEndpoint(
                MessageSecurityMode.None, SecurityPolicies.None);
            Certificate certificate = CreateSmallCertificate();

            using (var channel = new TestClientChannel(
                m_buffers, factory, m_quotas, certificate, endpoint, m_telemetry,
                new FakeTimeProvider()))
            {
                Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            }

            // The unsecured ctor path takes ownership and releases the handle,
            // so acquiring another reference must fail on the released core.
            Assert.That(() => certificate.AddRef(), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void ReadErrorMessageBodyMapsStatusCodeAndReason()
        {
            byte[] body = BuildErrorBody((uint)StatusCodes.BadCertificateTimeInvalid, "expired");
            using var decoder = new BinaryDecoder(body, m_context);

            ServiceResult result = TestClientChannel.CallReadErrorMessageBody(decoder);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadCertificateTimeInvalid));
            Assert.That(result.ToString(), Does.Contain("expired"));
        }

        [Test]
        public void WriteThenReadErrorMessageBodyRoundTripsStatusCode()
        {
            var error = new ServiceResult(StatusCodes.BadTcpEndpointUrlInvalid);
            byte[] buffer = new byte[512];
            int size;
            using (var stream = new MemoryStream(buffer, 0, buffer.Length))
            using (var encoder = new BinaryEncoder(stream, m_context, false))
            {
                TestClientChannel.CallWriteErrorMessageBody(encoder, error);
                size = encoder.Close();
            }

            using var decoder = new BinaryDecoder(TrimTo(buffer, size), m_context);
            ServiceResult roundTrip = TestClientChannel.CallReadErrorMessageBody(decoder);

            Assert.That(
                roundTrip.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadTcpEndpointUrlInvalid));
        }

        [Test]
        public void VerifyMessageTypeWithWrongTypeThrowsBadTcpMessageTypeInvalid()
        {
            byte[] header = BuildTypeAndSize(TcpMessageType.Error, 8);
            using var decoder = new BinaryDecoder(header, m_context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => TestClientChannel.CallVerifyMessageTypeAndSize(
                    decoder, TcpMessageType.Acknowledge, header.Length))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
        }

        [Test]
        public void VerifyMessageSizeLargerThanBufferThrowsBadTcpMessageTooLarge()
        {
            byte[] header = BuildTypeAndSize(TcpMessageType.Acknowledge, 1000);
            using var decoder = new BinaryDecoder(header, m_context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => TestClientChannel.CallVerifyMessageTypeAndSize(
                    decoder, TcpMessageType.Acknowledge, 8))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTooLarge));
        }

        [TestCase(200000u, 8192u, TestName = "SendBufferSizeTooLarge")]
        [TestCase(8192u, 200000u, TestName = "ReceiveBufferSizeTooLarge")]
        [TestCase(8192u, 1024u, TestName = "ReceiveBufferSizeTooSmall")]
        [TestCase(1024u, 8192u, TestName = "SendBufferSizeTooSmall")]
        public async Task ConnectAsyncFaultsOnInvalidAcknowledgeBufferSizesAsync(
            uint sendBufferSize, uint receiveBufferSize)
        {
            ServiceResultException ex = await RunHandshakeToFaultAsync(
                channel => channel.FeedIncomingMessage(
                    TcpMessageType.Acknowledge,
                    new ArraySegment<byte>(BuildAcknowledge(sendBufferSize, receiveBufferSize))))
                .ConfigureAwait(false);

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpNotEnoughResources));
        }

        [Test]
        public Task ConnectAsyncMapsCertificateUntrustedErrorAsync()
        {
            return AssertConnectErrorMapsAsync((uint)StatusCodes.BadCertificateUntrusted);
        }

        [Test]
        public Task ConnectAsyncMapsIdentityTokenRejectedErrorAsync()
        {
            return AssertConnectErrorMapsAsync((uint)StatusCodes.BadIdentityTokenRejected);
        }

        private async Task AssertConnectErrorMapsAsync(uint wireStatus)
        {
            ServiceResultException ex = await RunHandshakeToFaultAsync(
                channel => channel.FeedIncomingMessage(
                    TcpMessageType.Error, new ArraySegment<byte>(BuildErrorChunk(wireStatus))))
                .ConfigureAwait(false);

            Assert.That(ex.StatusCode, Is.EqualTo(wireStatus));
        }

        [Test]
        public async Task ConnectAsyncFaultsOnOpenResponseWhileConnectingAsync()
        {
            ServiceResultException ex = await RunHandshakeToFaultAsync(
                channel => channel.FeedIncomingMessage(
                    TcpMessageType.Open,
                    new ArraySegment<byte>(BuildChunk(TcpMessageType.Open, _ => { }))))
                .ConfigureAwait(false);

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
        }

        [Test]
        public async Task ConnectAsyncFaultsOnUnknownMessageTypeAsync()
        {
            ServiceResultException ex = await RunHandshakeToFaultAsync(
                channel => channel.FeedIncomingMessage(
                    0x00FFFFFFu, new ArraySegment<byte>(Array.Empty<byte>())))
                .ConfigureAwait(false);

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
        }

        [Test]
        public void ProcessErrorMessageWithoutHandshakeShutsDownChannel()
        {
            var timeProvider = new FakeTimeProvider();
            using var channel = new TestClientChannel(
                m_buffers,
                new RecordingByteTransportFactory(),
                m_quotas,
                null,
                BuildEndpoint(MessageSecurityMode.None, SecurityPolicies.None),
                m_telemetry,
                timeProvider);
            // No handshake operation is pending, so ProcessErrorMessage routes to
            // ForceReconnect; from the Closing state this shuts the channel down
            // deterministically (no reconnect timer is scheduled).
            channel.CurrentState = TcpChannelState.Closing;

            channel.FeedError(
                new ArraySegment<byte>(BuildErrorChunk((uint)StatusCodes.BadServerHalted)));

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
        }

        [Test]
        public void DoMessageLimitsExceededShutsDownChannel()
        {
            var timeProvider = new FakeTimeProvider();
            using var channel = new TestClientChannel(
                m_buffers,
                new RecordingByteTransportFactory(),
                m_quotas,
                null,
                BuildEndpoint(MessageSecurityMode.None, SecurityPolicies.None),
                m_telemetry,
                timeProvider);
            channel.CurrentState = TcpChannelState.Opening;

            channel.ForceMessageLimitsExceeded();

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
        }

        private async Task<ServiceResultException> RunHandshakeToFaultAsync(
            Action<TestClientChannel> feed)
        {
            var timeProvider = new FakeTimeProvider();
            var transport = new RecordingByteTransport();
            var factory = new RecordingByteTransportFactory(transport);
            EndpointDescription endpoint = BuildEndpoint(
                MessageSecurityMode.None, SecurityPolicies.None);

            using var channel = new TestClientChannel(
                m_buffers, factory, m_quotas, null, endpoint, m_telemetry, timeProvider);
            channel.SetupReverseTransport(transport);

            var url = new Uri("opc.tcp://localhost:4840");
            Task connectTask = channel
                .ConnectAsync(url, 60000, CancellationToken.None).AsTask();

            // The reverse handshake sends Hello synchronously and parks in
            // EndAsync with the channel in the Connecting state.
            Assert.That(
                await CompletesWithinAsync(transport.FirstSendTask, 30).ConfigureAwait(false),
                Is.True,
                "channel never sent the Hello message");
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Connecting));

            feed(channel);

            Assert.That(
                await CompletesWithinAsync(connectTask, 30).ConfigureAwait(false),
                Is.True,
                "ConnectAsync did not fault within the timeout");

            ServiceResultException? caught = null;
            try
            {
                await connectTask.ConfigureAwait(false);
            }
            catch (ServiceResultException e)
            {
                caught = e;
            }

            Assert.That(caught, Is.Not.Null, "ConnectAsync was expected to fault");
            return caught!;
        }

        private static async Task<bool> CompletesWithinAsync(Task task, int seconds)
        {
            Task completed = await Task
                .WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)))
                .ConfigureAwait(false);
            return ReferenceEquals(completed, task);
        }

        private static EndpointDescription BuildEndpoint(
            MessageSecurityMode securityMode, string securityPolicyUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri,
                Server = new ApplicationDescription { ApplicationUri = "urn:test:client" }
            };
        }

        private byte[] BuildAcknowledge(uint sendBufferSize, uint receiveBufferSize)
        {
            return BuildChunk(TcpMessageType.Acknowledge, encoder =>
            {
                encoder.WriteUInt32(null, 0); // protocol version
                encoder.WriteUInt32(null, sendBufferSize);
                encoder.WriteUInt32(null, receiveBufferSize);
                encoder.WriteUInt32(null, 0); // max message size
                encoder.WriteUInt32(null, 0); // max chunk count
            });
        }

        private byte[] BuildErrorChunk(uint statusCode)
        {
            return BuildChunk(TcpMessageType.Error, encoder =>
            {
                encoder.WriteUInt32(null, statusCode);
                encoder.WriteInt32(null, -1); // no reason string
            });
        }

        private byte[] BuildErrorBody(uint statusCode, string reason)
        {
            byte[] buffer = new byte[512];
            int size;
            using (var stream = new MemoryStream(buffer, 0, buffer.Length))
            using (var encoder = new BinaryEncoder(stream, m_context, false))
            {
                encoder.WriteUInt32(null, statusCode);
                byte[] bytes = Encoding.UTF8.GetBytes(reason);
                encoder.WriteInt32(null, bytes.Length);
                foreach (byte b in bytes)
                {
                    encoder.WriteByte(null, b);
                }
                size = encoder.Close();
            }
            return TrimTo(buffer, size);
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
            byte[] chunk = TrimTo(buffer, size);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)size);
            return chunk;
        }

        private static byte[] BuildTypeAndSize(uint messageType, int messageSize)
        {
            byte[] header = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), messageType);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), messageSize);
            return header;
        }

        private static byte[] TrimTo(byte[] buffer, int size)
        {
            byte[] result = new byte[size];
            Array.Copy(buffer, result, size);
            return result;
        }

        private static Certificate CreateSmallCertificate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=HyperTestClientSmall", ecdsa, HashAlgorithmName.SHA256);
            X509Certificate2 x509 = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return Certificate.From(x509);
        }

        private static Certificate CreateOversizedCertificate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=HyperTestClientLarge", ecdsa, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(
                new X509Extension(new Oid("1.3.6.1.4.1.311.99999.1"), new byte[8000], false));
            X509Certificate2 x509 = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return Certificate.From(x509);
        }

        private sealed class TestClientChannel : UaSCUaBinaryClientChannel
        {
            public TestClientChannel(
                BufferManager bufferManager,
                IUaSCByteTransportFactory transportFactory,
                ChannelQuotas quotas,
                Certificate? clientCertificate,
                EndpointDescription? endpoint,
                ITelemetryContext telemetry,
                TimeProvider? timeProvider)
                : base(
                    "client-det",
                    bufferManager,
                    transportFactory,
                    quotas,
                    clientCertificate,
                    null,
                    null,
                    endpoint,
                    telemetry,
                    timeProvider)
            {
            }

            public TcpChannelState CurrentState
            {
                get => State;
                set => State = value;
            }

            public void SetupReverseTransport(IUaSCByteTransport transport)
            {
                ReverseSocket = true;
                Transport = transport;
            }

            public bool FeedIncomingMessage(uint messageType, ArraySegment<byte> chunk)
            {
                return HandleIncomingMessage(messageType, chunk);
            }

            public bool FeedError(ArraySegment<byte> chunk)
            {
                return ProcessErrorMessage(chunk);
            }

            public void ForceMessageLimitsExceeded()
            {
                DoMessageLimitsExceeded();
            }

            public static ServiceResult CallReadErrorMessageBody(BinaryDecoder decoder)
            {
                return ReadErrorMessageBody(decoder);
            }

            public static void CallWriteErrorMessageBody(BinaryEncoder encoder, ServiceResult error)
            {
                WriteErrorMessageBody(encoder, error);
            }

            public static void CallVerifyMessageTypeAndSize(
                IDecoder decoder, uint expectedMessageType, int count)
            {
                ReadAndVerifyMessageTypeAndSize(decoder, expectedMessageType, count);
            }
        }

        private sealed class RecordingByteTransportFactory : IUaSCByteTransportFactory
        {
            private readonly RecordingByteTransport m_transport;

            public RecordingByteTransportFactory()
                : this(new RecordingByteTransport())
            {
            }

            public RecordingByteTransportFactory(RecordingByteTransport transport)
            {
                m_transport = transport;
            }

            public string Implementation => "UA-FAKE";

            public IUaSCByteTransport Create(
                BufferManager bufferManager, int receiveBufferSize, ITelemetryContext telemetry)
            {
                return m_transport;
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
