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
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [NonParallelizable]
    public sealed class TcpReconnectKeyAgreementTests
    {
        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.RSA_DH_AesGcm)]
        public async Task RenewHandoffRetainsPrivateKeyAndChainsPreviousSecretAsync(string policyUri)
        {
            using var harness = new HandoffHarness(policyUri);
            await harness.OpenAsync().ConfigureAwait(false);
            ChannelToken previous = harness.Target.Token;
            Assert.That(previous.Secret, Is.Not.Null.And.Not.Empty);

            await harness.RenewAsync().ConfigureAwait(false);

            Assert.That(harness.HandoffError, Is.Null);
            Assert.That(harness.HandoffCount, Is.EqualTo(1));
            Assert.That(harness.Target.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(harness.Temporary.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(harness.NewTransport.IsClosed, Is.False);
            Assert.That(harness.OldTransport.IsClosed, Is.True);
            Assert.That(harness.Target.Token.PreviousSecret, Is.SameAs(previous.Secret));
            Assert.That(harness.Target.Token.TokenId, Is.Not.EqualTo(previous.TokenId));

            await harness.Peer.CompleteOpenAsync(
                await harness.NewTransport.ReadAsync(harness.CancellationToken).ConfigureAwait(false),
                renew: true).ConfigureAwait(false);

            harness.Target.RecomputeKeys();
            Assert.That(harness.Target.Token.Secret, Is.EqualTo(harness.Peer.Token.Secret));
            Assert.That(harness.Target.Token.Secret, Is.Not.EqualTo(previous.Secret));
            Assert.That(
                harness.HandedOffLocal!.GenerateSecret(harness.HandedOffRemote!, previous.Secret),
                Is.EqualTo(harness.Target.Token.Secret));
            await harness.AssertEncryptedReadAsync(harness.NewTransport).ConfigureAwait(false);
        }

        [TestCase(SecurityPolicies.ECC_nistP256, false)]
        [TestCase(SecurityPolicies.ECC_nistP256, true)]
        [TestCase(SecurityPolicies.RSA_DH_AesGcm, false)]
        [TestCase(SecurityPolicies.RSA_DH_AesGcm, true)]
        public async Task FailedReconnectClosesHandoffAndRethrowsToListenerAsync(string policyUri, bool afterAdoption)
        {
            using var harness = new HandoffHarness(policyUri);
            await harness.OpenAsync().ConfigureAwait(false);
            harness.NewTransport.FailNegotiation = !afterAdoption;
            harness.Target.FailReceiveStart = afterAdoption;

            await harness.RenewAsync().ConfigureAwait(false);

            Assert.That(harness.HandoffError, Is.SameAs(afterAdoption
                ? harness.Target.ReceiveError
                : harness.NewTransport.NegotiationError));
            Assert.That(harness.NewTransport.IsClosed, Is.True);
            Assert.That(harness.OldTransport.IsClosed, Is.True);
            Assert.That(harness.Target.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(harness.Temporary.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(harness.NewTransport.SentCount, Is.Zero);
            Assert.That(() => harness.HandedOffToken!.TakeNonces(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(harness.FailureAuditSubject, Is.EqualTo("CN=ReconnectClient"));
        }

        [Test]
        public async Task FailedEndpointReadClosesTheUnadoptedHandoffAsync()
        {
            using var harness = new HandoffHarness(SecurityPolicies.ECC_nistP256);
            await harness.OpenAsync().ConfigureAwait(false);
            harness.NewTransport.FailEndpointRead = true;

            await harness.RenewAsync().ConfigureAwait(false);

            Assert.That(harness.HandoffCount, Is.Zero);
            Assert.That(harness.NewTransport.IsClosed, Is.True);
            Assert.That(harness.Temporary.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(harness.Target.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
            Assert.That(harness.FailureAuditSubject, Is.EqualTo("CN=ReconnectClient"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedHandoffReleasesUnadoptedEccNoncesAsync(bool throwFromListener)
        {
            using var harness = new HandoffHarness(SecurityPolicies.ECC_nistP256);
            await harness.OpenAsync().ConfigureAwait(false);
            ChannelToken previous = harness.Target.Token;
            harness.RejectHandoff = true;
            harness.ThrowFromListener = throwFromListener;

            await harness.RenewAsync().ConfigureAwait(false);

            Assert.That(harness.HandoffCount, Is.EqualTo(1));
            Assert.That(harness.NewTransport.IsClosed, Is.True);
            Assert.That(harness.NewTransport.SentCount, Is.Zero);
            Assert.That(harness.Temporary.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(harness.Target.Token, Is.SameAs(previous));
            Assert.That(harness.Target.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
            Assert.That(() => harness.HandedOffToken!.TakeNonces(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(harness.HandedOffLocal!.GenerateSecret(harness.HandedOffRemote!, null), Is.Null);
            harness.Target.RecomputeKeys();
            Assert.That(previous.Secret, Is.EqualTo(harness.Peer.Token.Secret));
        }

        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.RSA_DH_AesGcm)]
        public async Task ReconnectRejectsPublicOnlyNonceMaterialAsync(string policyUri)
        {
            using var harness = new HandoffHarness(policyUri);
            await harness.OpenAsync().ConfigureAwait(false);
            harness.DiscardHandoffNonces = true;

            await harness.RenewAsync().ConfigureAwait(false);

            Assert.That(harness.HandoffError, Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNonceInvalid));
            Assert.That(harness.HandedOffToken!.ServerNonce, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.HandedOffToken.ClientNonce, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.NewTransport.IsClosed, Is.True);
            Assert.That(harness.Target.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(harness.Temporary.CurrentState, Is.EqualTo(TcpChannelState.Closed));
        }

        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.RSA_DH_AesGcm)]
        public async Task RetainedChannelRejectsReplayedReconnectSequenceAsync(string policyUri)
        {
            using var harness = new HandoffHarness(policyUri);
            await harness.OpenAsync().ConfigureAwait(false);
            ChannelToken previous = harness.Target.Token;
            harness.Target.MarkSequenceReceived(2);

            await harness.RenewAsync().ConfigureAwait(false);

            Assert.That(harness.HandoffError, Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadSequenceNumberInvalid));
            Assert.That(harness.Target.Token, Is.SameAs(previous));
            Assert.That(harness.NewTransport.IsClosed, Is.True);
            Assert.That(harness.Temporary.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(() => harness.HandedOffToken!.TakeNonces(), Throws.TypeOf<ObjectDisposedException>());
        }

        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.RSA_DH_AesGcm)]
        public void ChannelTokenTransfersOriginalNonceObjectsOnce(string policyUri)
        {
            SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(policyUri)!;
            using Nonce local = Nonce.CreateNonce(policy);
            using Nonce remote = Nonce.CreateNonce(policy);
            using var token = new ChannelToken();
            token.SetNonces(local, remote);

            (Nonce transferredLocal, Nonce transferredRemote) = token.TakeNonces();
            Assert.That(() => token.TakeNonces(), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNonceInvalid));
            token.Dispose();

            Assert.That(transferredLocal, Is.SameAs(local));
            Assert.That(transferredRemote, Is.SameAs(remote));
            Assert.That(() => token.TakeNonces(), Throws.TypeOf<ObjectDisposedException>());
            byte[]? secret = transferredLocal.GenerateSecret(transferredRemote, null);
            Assert.That(secret, Is.Not.Null.And.Not.Empty);
            Assert.That(secret, Is.EqualTo(remote.GenerateSecret(local, null)));
        }

        private sealed class HandoffHarness : IDisposable
        {
            public HandoffHarness(string policyUri)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var context = ServiceMessageContext.Create(telemetry);
                var buffers = new BufferManager("reconnect-keys", 65536, telemetry);
                var validator = new Mock<ICertificateValidatorEx>();
                validator.Setup(value => value.ValidateAsync(
                        It.IsAny<CertificateCollection>(),
                        It.IsAny<TrustListIdentifier?>(),
                        It.IsAny<Opc.Ua.Security.Certificates.CertificateValidationOptions?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(CertificateValidationResult.Success));
                var quotas = new ChannelQuotas(context)
                {
                    MaxBufferSize = 65536,
                    CertificateValidator = validator.Object
                };
                SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(policyUri)
                    ?? throw new AssertionException("The requested key-agreement policy must be supported.");
                m_serverCertificate = CreateCertificate("CN=ReconnectServer", policy);
                m_clientCertificate = CreateCertificate("CN=ReconnectClient", policy);
                var registry = new Mock<ICertificateRegistry>();
                registry.Setup(value => value.AcquireApplicationCertificateBySecurityPolicy(policyUri))
                    .Returns(() => new CertificateEntry(
                        m_serverCertificate, m_chain, policy.SupportedCertificateTypes[0]));
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = policyUri,
                    TransportProfileUri = Profiles.UaTcpTransport
                };
                var listener = new Mock<ITcpChannelListener>();
                listener.SetupGet(value => value.EndpointUrl).Returns(new Uri(endpoint.EndpointUrl));
                Target = new HandoffChannel(listener.Object, buffers, quotas, registry.Object, endpoint, telemetry);
                Temporary = new HandoffChannel(listener.Object, buffers, quotas, registry.Object, endpoint, telemetry);
                Peer = new WirePeer(buffers, quotas, m_serverCertificate, m_clientCertificate, endpoint, telemetry);
                Target.Attach(1, OldTransport);
                Target.CurrentState = TcpChannelState.Opening;
                Temporary.Attach(2, NewTransport);
                Temporary.CurrentState = TcpChannelState.Opening;
                listener.Setup(value => value.ChannelClosed(1)).Callback(Target.Dispose);
                listener.Setup(value => value.ChannelClosed(2)).Callback(Temporary.Dispose);
                Temporary.SetReportOpenSecureChannelAuditCallback((_, _, certificate, exception) =>
                {
                    if (exception != null)
                    {
                        FailureAuditSubject = certificate?.Subject;
                    }
                });
                listener.Setup(value => value.ReconnectToExistingChannel(
                        Temporary, NewTransport, It.IsAny<uint>(), It.IsAny<uint>(), 1,
                        It.IsAny<Certificate>(), It.IsAny<ChannelToken>(), It.IsAny<OpenSecureChannelRequest>()))
                    .Returns<TcpListenerChannel, IUaSCByteTransport, uint, uint, uint,
                        Certificate, ChannelToken, OpenSecureChannelRequest>(
                        (_, transport, requestId, sequence, _, certificate, token, request) =>
                        {
                            HandoffCount++;
                            HandedOffToken = token;
                            (Nonce local, Nonce remote) = token.TakeNonces();
                            HandedOffLocal = local;
                            HandedOffRemote = remote;
                            if (DiscardHandoffNonces)
                            {
                                local.Dispose();
                                remote.Dispose();
                            }
                            else
                            {
                                token.SetNonces(local, remote);
                            }
                            try
                            {
                                if (RejectHandoff)
                                {
                                    if (ThrowFromListener)
                                    {
                                        throw new ServiceResultException(StatusCodes.BadTcpSecureChannelUnknown);
                                    }
                                    return false;
                                }
                                Target.Reconnect(transport, requestId, sequence, certificate, token, request);
                                return true;
                            }
                            catch (Exception exception)
                            {
                                HandoffError = exception;
                                throw;
                            }
                        });
                Target.SetRequestReceivedCallback((channel, requestId, request) =>
                {
                    Assert.That(request, Is.TypeOf<ReadRequest>());
                    Assert.That(channel, Is.SameAs(Target));
                    Target.SendResponse(requestId, new ReadResponse
                    {
                        Results = [new DataValue(new Variant(123))]
                    });
                });
            }

            public HandoffChannel Target { get; }
            public HandoffChannel Temporary { get; }
            public WirePeer Peer { get; }
            public RecordingTransport OldTransport { get; } = new();
            public RecordingTransport NewTransport { get; } = new();
            public CancellationToken CancellationToken => m_timeout.Token;
            public int HandoffCount { get; private set; }
            public Exception? HandoffError { get; private set; }
            public string? FailureAuditSubject { get; private set; }
            public ChannelToken? HandedOffToken { get; private set; }
            public Nonce? HandedOffLocal { get; private set; }
            public Nonce? HandedOffRemote { get; private set; }
            public bool RejectHandoff { get; set; }
            public bool ThrowFromListener { get; set; }
            public bool DiscardHandoffNonces { get; set; }

            public async Task OpenAsync()
            {
                await Target.FeedAsync(await Peer.CreateOpenAsync(false).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(Target.CurrentState, Is.EqualTo(TcpChannelState.Open));
                await Peer.CompleteOpenAsync(
                    await OldTransport.ReadAsync(CancellationToken).ConfigureAwait(false),
                    false).ConfigureAwait(false);
                Assert.That(Target.Token.Secret, Is.EqualTo(Peer.Token.Secret));
                await AssertEncryptedReadAsync(OldTransport).ConfigureAwait(false);
            }

            public async Task RenewAsync()
            {
                Target.CurrentState = TcpChannelState.Faulted;
                ArraySegment<byte> chunk = await Peer.CreateOpenAsync(true).ConfigureAwait(false);
                await Temporary.FeedAsync(chunk).ConfigureAwait(false);
            }

            public async Task AssertEncryptedReadAsync(RecordingTransport transport)
            {
                await Target.FeedAsync(Peer.CreateRead()).ConfigureAwait(false);
                ReadResponse response = Peer.ReadResponse(
                    await transport.ReadAsync(CancellationToken).ConfigureAwait(false));
                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(response.Results[0].WrappedValue, Is.EqualTo(new Variant(123)));
            }

            public void Dispose()
            {
                Temporary.Dispose();
                Target.Dispose();
                Peer.Dispose();
                m_serverCertificate.Dispose();
                m_clientCertificate.Dispose();
                m_chain.Dispose();
                m_timeout.Dispose();
            }

            private static Certificate CreateCertificate(string subject, SecurityPolicyInfo policy)
            {
                ICertificateBuilder builder = DefaultCertificateFactory.Instance.CreateCertificate(subject);
                return policy.CertificateKeyFamily == CertificateKeyFamily.ECC
                    ? builder.SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa()
                    : builder.CreateForRSA();
            }

            private readonly Certificate m_serverCertificate;
            private readonly Certificate m_clientCertificate;
            private readonly CertificateCollection m_chain = [];
            private readonly CancellationTokenSource m_timeout = new(TimeSpan.FromSeconds(15));
        }

        private sealed class HandoffChannel : TcpServerChannel
        {
            public HandoffChannel(
                ITcpChannelListener listener,
                BufferManager buffers,
                ChannelQuotas quotas,
                ICertificateRegistry registry,
                EndpointDescription endpoint,
                ITelemetryContext telemetry)
                : base("handoff", listener, buffers, quotas, registry, [endpoint], telemetry, new FakeTimeProvider())
            {
            }

            public TcpChannelState CurrentState
            {
                get => State;
                set => State = value;
            }

            public ChannelToken Token => CurrentToken!;
            public bool FailReceiveStart { get; set; }
            public InvalidOperationException ReceiveError { get; } = new("Injected receive-loop failure.");

            public ValueTask FeedAsync(ArraySegment<byte> chunk)
            {
                return OnChunkReceivedAsync(chunk, CancellationToken.None);
            }

            public void RecomputeKeys()
            {
                Token.ClientHmac?.Dispose();
                Token.ServerHmac?.Dispose();
                ComputeKeys(Token);
            }

            public void MarkSequenceReceived(uint sequenceNumber)
            {
                Assert.That(VerifySequenceNumber(sequenceNumber, "ReceivedBeforeReconnect"), Is.True);
            }

            protected internal override void StartReceiveLoop()
            {
                if (FailReceiveStart)
                {
                    throw ReceiveError;
                }
            }
        }

        private sealed class WirePeer : UaSCUaBinaryChannel
        {
            public WirePeer(
                BufferManager buffers,
                ChannelQuotas quotas,
                Certificate server,
                Certificate client,
                EndpointDescription endpoint,
                ITelemetryContext telemetry)
                : base("peer", buffers, quotas, server.AddRef(), [endpoint],
                    MessageSecurityMode.SignAndEncrypt, endpoint.SecurityPolicyUri, telemetry, new FakeTimeProvider())
            {
                ClientCertificate = client.AddRef();
            }

            public ChannelToken Token => CurrentToken!;

            public async Task<ArraySegment<byte>> CreateOpenAsync(bool renew)
            {
                m_pendingToken = CreateToken();
                m_pendingToken.ClientNonce = CreateNonce(ClientCertificate);
                m_pendingToken.PreviousSecret = renew ? CurrentToken?.Secret : null;
                var request = new OpenSecureChannelRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 77 },
                    RequestType = renew ? SecurityTokenRequestType.Renew : SecurityTokenRequestType.Issue,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    ClientNonce = m_pendingToken.ClientNonce.ToByteString(),
                    RequestedLifetime = 60000
                };
                byte[] body = BinaryEncoder.EncodeMessage(request, Quotas.MessageContext);
                AsymmetricWriteResult written = await WriteAsymmetricMessageAsync(
                    TcpMessageType.Open, 77, ClientCertificate, null, ServerCertificate,
                    new ArraySegment<byte>(body), null, CancellationToken.None).ConfigureAwait(false);
                m_requestSignature = !renew && SecurityPolicy!.SecureChannelEnhancements ? written.Signature : null;
                Assert.That(written.Chunks, Has.Count.EqualTo(1));
                return written.Chunks[0];
            }

            public async Task CompleteOpenAsync(ByteString chunk, bool renew)
            {
                Certificate? sender = null;
                AsymmetricMessage message = await ReadAsymmetricMessageAsync(
                    new ArraySegment<byte>(chunk.ToArray()), ClientCertificate,
                    renew ? null : m_requestSignature,
                    certificate => sender = certificate, CancellationToken.None).ConfigureAwait(false);
                try
                {
                    Assert.That(VerifySequenceNumber(message.SequenceNumber, "OpenResponse"), Is.True);
                    using var decoder = new BinaryDecoder(message.Body, Quotas.MessageContext);
                    OpenSecureChannelResponse response = decoder.DecodeMessage<OpenSecureChannelResponse>();
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(response.SecurityToken.ChannelId, Is.EqualTo(1));
                    m_pendingToken!.ServerNonce = response.ServerNonce.ToArray();
                    m_pendingToken.TokenId = response.SecurityToken.TokenId;
                    m_pendingToken.ChannelId = ChannelId = response.SecurityToken.ChannelId;
                    Assert.That(ValidateNonce(ServerCertificate, m_pendingToken.ServerNonce), Is.True);
                    ActivateToken(m_pendingToken);
                    m_pendingToken = null;
                }
                finally
                {
                    sender?.Dispose();
                    ReturnDecryptedBuffer(message.Body);
                }
            }

            public ArraySegment<byte> CreateRead()
            {
                BufferCollection chunks = WriteSymmetricMessage(
                    TcpMessageType.Message, 78, Token, new ReadRequest(), true, out bool exceeded);
                Assert.That(exceeded, Is.False);
                Assert.That(chunks, Has.Count.EqualTo(1));
                return chunks[0];
            }

            public ReadResponse ReadResponse(ByteString chunk)
            {
                ArraySegment<byte> body = ReadSymmetricMessage(
                    new ArraySegment<byte>(chunk.ToArray()), false,
                    out _, out uint requestId, out uint sequenceNumber);
                Assert.That(VerifySequenceNumber(sequenceNumber, "ReadResponse"), Is.True);
                Assert.That(requestId, Is.EqualTo(78));
                using var decoder = new BinaryDecoder(body, Quotas.MessageContext);
                return decoder.DecodeMessage<ReadResponse>();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_pendingToken?.Dispose();
                    m_pendingToken = null;
                }
                base.Dispose(disposing);
            }

            private ChannelToken? m_pendingToken;
            private byte[]? m_requestSignature;
        }

        private sealed class RecordingTransport : IUaSCByteTransport, IUaSCByteTransportLimits
        {
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => FailEndpointRead
                ? throw new InvalidOperationException("Injected endpoint failure.")
                : null;
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public string Implementation => "ReconnectTest";
            public bool IsClosed => Volatile.Read(ref m_closed) != 0;
            public int SentCount => Volatile.Read(ref m_sentCount);
            public bool FailNegotiation { get; set; }
            public bool FailEndpointRead { get; set; }
            public InvalidOperationException NegotiationError { get; } = new("Injected handoff failure.");

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                Interlocked.Increment(ref m_sentCount);
                return m_sent.Writer.WriteAsync(chunk.ToArray().ToByteString(), ct);
            }

            public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                using var stream = new MemoryStream();
                foreach (ArraySegment<byte> buffer in buffers)
                {
                    stream.Write(buffer.Array!, buffer.Offset, buffer.Count);
                }
                await SendChunkAsync(stream.ToArray(), ct).ConfigureAwait(false);
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<ByteString> ReadAsync(CancellationToken ct)
            {
                return m_sent.Reader.ReadAsync(ct);
            }

            public void SetReceiveBufferSize(int receiveBufferSize)
            {
                if (FailNegotiation)
                {
                    throw NegotiationError;
                }
            }

            public void Close()
            {
                Interlocked.Exchange(ref m_closed, 1);
                m_sent.Writer.TryComplete();
            }

            private readonly Channel<ByteString> m_sent = Channel.CreateUnbounded<ByteString>();
            private int m_closed;
            private int m_sentCount;
        }
    }
}
