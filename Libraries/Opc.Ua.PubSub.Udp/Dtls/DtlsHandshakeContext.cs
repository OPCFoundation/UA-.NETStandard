/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// DTLS 1.3 handshake driver for Part 14 §7.3.2.4 unicast PubSub.
    /// </summary>
    internal sealed class DtlsHandshakeContext : IDtlsContext, IDisposable
    {
        public DtlsHandshakeContext(
            DtlsProfile profile,
            DtlsTransportOptions options,
            ICertificateValidatorEx? certificateValidator,
            DtlsEndpointRole role,
            UdpEndpoint endpoint,
            TimeProvider timeProvider)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            CertificateValidator = certificateValidator;
            Role = role;
            Endpoint = endpoint;
            TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public DtlsProfile Profile { get; }

        public async ValueTask OpenAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken = default)
        {
#if NET8_0_OR_GREATER
            if (channel is null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Role == DtlsEndpointRole.Client)
            {
                await ConnectAsync(channel, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await AcceptAsync(channel, cancellationToken).ConfigureAwait(false);
            }
#else
            _ = channel;
            m_writeProtection = null;
            m_readProtection = null;
            m_keyingContext = null;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("DTLS 1.3 ECDHE requires .NET 8 or later BCL primitives.");
#endif
        }

        public ValueTask<ReadOnlyMemory<byte>> ProtectAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DtlsRecordProtection protection = m_writeProtection
                ?? throw new InvalidOperationException("DTLS application write keys are not installed.");
            return new ValueTask<ReadOnlyMemory<byte>>(protection.Seal(payload.Span));
        }

        public ValueTask<ReadOnlyMemory<byte>> UnprotectAsync(
            ReadOnlyMemory<byte> record,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DtlsRecordProtection protection = m_readProtection
                ?? throw new InvalidOperationException("DTLS application read keys are not installed.");
            return new ValueTask<ReadOnlyMemory<byte>>(protection.Open(record.Span));
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_writeProtection?.Dispose();
            m_readProtection?.Dispose();
            m_keyingContext?.Dispose();
            m_disposed = true;
        }

#if NET8_0_OR_GREATER
        private async ValueTask ConnectAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken)
        {
            using DtlsEcdheKeyExchange ecdhe = new(Profile.KeyExchangeCurve);
            var transcript = new DtlsTranscriptHash(GetHashAlgorithm(Profile.CipherSuite));
            byte[] sessionId = CreateRandom(32);
            byte[] cookie = [];
            byte[] clientHelloBody = [];
            byte[] sharedSecret = [];
            try
            {
                while (true)
                {
                    clientHelloBody = BuildClientHello(sessionId, ecdhe.PublicKey, cookie);
                    byte[] clientHelloFrame = DtlsHandshakeCodec.EncodeFrame(
                        DtlsHandshakeType.ClientHello,
                        m_nextSendSequence++,
                        clientHelloBody);
                    await SendFlightAsync(channel, clientHelloFrame, cancellationToken).ConfigureAwait(false);
                    transcript.Append(clientHelloFrame);
                    DtlsHandshakeFrame firstFrame = await ReceiveFrameAsync(channel, cancellationToken)
                        .ConfigureAwait(false);
                    RequireMessage(firstFrame, DtlsHandshakeType.ServerHello);
                    DtlsServerHello serverHello = DtlsHandshakeCodec.DecodeServerHello(firstFrame.Fragment);
                    if (serverHello.Extensions.Cookie.Length > 0 && serverHello.Extensions.KeyShares.Count == 0)
                    {
                        cookie = serverHello.Extensions.Cookie;
                        transcript = new DtlsTranscriptHash(GetHashAlgorithm(Profile.CipherSuite));
                        continue;
                    }

                    ValidateServerHello(serverHello);
                    transcript.Append(ToCompleteFrame(firstFrame));
                    sharedSecret = ecdhe.DeriveSharedSecret(serverHello.Extensions.KeyShares[0].KeyExchange);
                    break;
                }

                byte[] serverHelloHash = transcript.GetHash();
                m_keyingContext = new DtlsHandshakeKeyingContext(Profile, sharedSecret, serverHelloHash, serverHelloHash);
                await ReceiveAndAppendAsync(channel, transcript, DtlsHandshakeType.EncryptedExtensions, cancellationToken)
                    .ConfigureAwait(false);
                DtlsHandshakeFrame certificateFrame = await ReceiveAndAppendAsync(
                    channel,
                    transcript,
                    DtlsHandshakeType.Certificate,
                    cancellationToken).ConfigureAwait(false);
                IReadOnlyList<X509Certificate2> peerChain =
                    DtlsCertificateAuthenticator.DecodeCertificate(certificateFrame.Fragment);
                await ValidatePeerCertificateAsync(peerChain, cancellationToken).ConfigureAwait(false);
                byte[] certificateVerifyTranscriptHash = transcript.GetHash();
                DtlsHandshakeFrame certificateVerifyFrame = await ReceiveFrameAsync(channel, cancellationToken)
                    .ConfigureAwait(false);
                RequireMessage(certificateVerifyFrame, DtlsHandshakeType.CertificateVerify);
                DtlsCertificateAuthenticator.VerifyCertificateVerify(
                    peerChain[0],
                    Profile.CipherSuite,
                    certificateVerifyTranscriptHash,
                    certificateVerifyFrame.Fragment,
                    isServer: true);
                transcript.Append(ToCompleteFrame(certificateVerifyFrame));
                byte[] finishedTranscriptHash = transcript.GetHash();
                DtlsHandshakeFrame serverFinishedFrame = await ReceiveFrameAsync(channel, cancellationToken)
                    .ConfigureAwait(false);
                RequireMessage(serverFinishedFrame, DtlsHandshakeType.Finished);
                byte[] expectedServerFinished = m_keyingContext.ComputeServerFinished(finishedTranscriptHash);
                byte[] actualServerFinished = DtlsHandshakeCodec.DecodeFinished(serverFinishedFrame.Fragment);
                try
                {
                    m_keyingContext.VerifyFinished(expectedServerFinished, actualServerFinished);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(expectedServerFinished);
                    CryptographicOperations.ZeroMemory(actualServerFinished);
                }

                transcript.Append(ToCompleteFrame(serverFinishedFrame));
                byte[] clientFinished = m_keyingContext.ComputeClientFinished(transcript.GetHash());
                byte[] clientFinishedFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.Finished,
                    m_nextSendSequence++,
                    DtlsHandshakeCodec.EncodeFinished(clientFinished));
                await SendFlightAsync(channel, clientFinishedFrame, cancellationToken).ConfigureAwait(false);
                InstallApplicationKeys(isClient: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clientHelloBody);
                CryptographicOperations.ZeroMemory(sharedSecret);
            }
        }
        private async ValueTask AcceptAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken)
        {
            X509Certificate2 localCertificate = GetLocalCertificate();
            using DtlsEcdheKeyExchange ecdhe = new(Profile.KeyExchangeCurve);
            var transcript = new DtlsTranscriptHash(GetHashAlgorithm(Profile.CipherSuite));
            byte[] cookieKey = CreateRandom(32);
            byte[] sharedSecret = [];
            try
            {
                DtlsClientHello clientHello;
                while (true)
                {
                    DtlsHandshakeFrame clientHelloFrame = await ReceiveFrameAsync(channel, cancellationToken)
                        .ConfigureAwait(false);
                    RequireMessage(clientHelloFrame, DtlsHandshakeType.ClientHello);
                    clientHello = DtlsHandshakeCodec.DecodeClientHello(clientHelloFrame.Fragment);
                    ValidateClientHello(clientHello);
                    using var cookieProtector = new DtlsHelloRetryCookieProtector(cookieKey);
                    IPEndPoint remoteEndpoint = GetCookieEndpoint(channel);
                    if (Options.RequireHelloRetryRequestCookie
                        && !cookieProtector.ValidateCookie(
                            remoteEndpoint,
                            ReadOnlySpan<byte>.Empty,
                            clientHello.Extensions.Cookie))
                    {
                        byte[] retryCookie = cookieProtector.CreateCookie(remoteEndpoint, ReadOnlySpan<byte>.Empty);
                        byte[] retryFrame = BuildHelloRetryRequest(clientHello.SessionId, retryCookie);
                        await SendFlightAsync(channel, retryFrame, cancellationToken).ConfigureAwait(false);
                        transcript = new DtlsTranscriptHash(GetHashAlgorithm(Profile.CipherSuite));
                        continue;
                    }

                    transcript.Append(ToCompleteFrame(clientHelloFrame));
                    DtlsKeyShareEntry clientKeyShare = clientHello.Extensions.KeyShares
                        .First(k => k.Group == Profile.KeyExchangeCurve);
                    sharedSecret = ecdhe.DeriveSharedSecret(clientKeyShare.KeyExchange);
                    byte[] serverHelloFrame = BuildServerHello(clientHello.SessionId, ecdhe.PublicKey);
                    await SendFlightAsync(channel, serverHelloFrame, cancellationToken).ConfigureAwait(false);
                    transcript.Append(serverHelloFrame);
                    byte[] serverHelloHash = transcript.GetHash();
                    m_keyingContext = new DtlsHandshakeKeyingContext(
                        Profile,
                        sharedSecret,
                        serverHelloHash,
                        serverHelloHash);
                    break;
                }

                byte[] encryptedExtensionsFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.EncryptedExtensions,
                    m_nextSendSequence++,
                    DtlsHandshakeCodec.EncodeEncryptedExtensions());
                await SendFlightAsync(channel, encryptedExtensionsFrame, cancellationToken).ConfigureAwait(false);
                transcript.Append(encryptedExtensionsFrame);
                byte[] certificateFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.Certificate,
                    m_nextSendSequence++,
                    DtlsCertificateAuthenticator.EncodeCertificate(GetCertificateChain(localCertificate)));
                await SendFlightAsync(channel, certificateFrame, cancellationToken).ConfigureAwait(false);
                transcript.Append(certificateFrame);
                byte[] certificateVerifyBody = DtlsCertificateAuthenticator.SignCertificateVerify(
                    localCertificate,
                    Profile.CipherSuite,
                    transcript.GetHash());
                byte[] certificateVerifyFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.CertificateVerify,
                    m_nextSendSequence++,
                    certificateVerifyBody);
                await SendFlightAsync(channel, certificateVerifyFrame, cancellationToken).ConfigureAwait(false);
                transcript.Append(certificateVerifyFrame);
                byte[] serverFinishedBody = DtlsHandshakeCodec.EncodeFinished(
                    m_keyingContext!.ComputeServerFinished(transcript.GetHash()));
                byte[] serverFinishedFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.Finished,
                    m_nextSendSequence++,
                    serverFinishedBody);
                await SendFlightAsync(channel, serverFinishedFrame, cancellationToken).ConfigureAwait(false);
                transcript.Append(serverFinishedFrame);
                DtlsHandshakeFrame clientFinishedFrame = await ReceiveFrameAsync(channel, cancellationToken)
                    .ConfigureAwait(false);
                RequireMessage(clientFinishedFrame, DtlsHandshakeType.Finished);
                byte[] expectedClientFinished = m_keyingContext.ComputeClientFinished(transcript.GetHash());
                byte[] actualClientFinished = DtlsHandshakeCodec.DecodeFinished(clientFinishedFrame.Fragment);
                try
                {
                    m_keyingContext.VerifyFinished(expectedClientFinished, actualClientFinished);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(expectedClientFinished);
                    CryptographicOperations.ZeroMemory(actualClientFinished);
                }

                InstallApplicationKeys(isClient: false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cookieKey);
                CryptographicOperations.ZeroMemory(sharedSecret);
            }
        }

        private byte[] BuildClientHello(byte[] sessionId, byte[] publicKey, byte[] cookie)
        {
            var hello = new DtlsClientHello(
                CreateRandom(32),
                sessionId,
                [Profile.CipherSuite],
                DtlsHelloExtensions.CreateDefault(
                    [Profile.KeyExchangeCurve],
                    [new DtlsKeyShareEntry(Profile.KeyExchangeCurve, publicKey)],
                    cookie));
            return DtlsHandshakeCodec.EncodeClientHello(hello);
        }

        private byte[] BuildServerHello(byte[] sessionId, byte[] publicKey)
        {
            var hello = new DtlsServerHello(
                CreateRandom(32),
                sessionId,
                Profile.CipherSuite,
                DtlsHelloExtensions.CreateDefault(
                    [Profile.KeyExchangeCurve],
                    [new DtlsKeyShareEntry(Profile.KeyExchangeCurve, publicKey)]));
            return DtlsHandshakeCodec.EncodeFrame(
                DtlsHandshakeType.ServerHello,
                m_nextSendSequence++,
                DtlsHandshakeCodec.EncodeServerHello(hello));
        }

        private byte[] BuildHelloRetryRequest(byte[] sessionId, byte[] cookie)
        {
            var retry = new DtlsServerHello(
                CreateRandom(32),
                sessionId,
                Profile.CipherSuite,
                DtlsHelloExtensions.CreateDefault([Profile.KeyExchangeCurve], [], cookie));
            return DtlsHandshakeCodec.EncodeFrame(
                DtlsHandshakeType.ServerHello,
                m_nextSendSequence++,
                DtlsHandshakeCodec.EncodeServerHello(retry));
        }

        private async ValueTask SendFlightAsync(
            IDtlsDatagramChannel channel,
            ReadOnlyMemory<byte> flight,
            CancellationToken cancellationToken)
        {
            var timer = new DtlsRetransmissionTimer(
                Options.InitialRetransmissionTimeout,
                Options.MaxRetransmissionTimeout);
            await channel.SendAsync(flight, cancellationToken).ConfigureAwait(false);
            _ = timer;
        }

        private static async ValueTask<DtlsHandshakeFrame> ReceiveFrameAsync(
            IDtlsDatagramChannel channel,
            CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> datagram = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            return DtlsHandshakeCodec.DecodeFrame(datagram.Span);
        }
        private static async ValueTask<DtlsHandshakeFrame> ReceiveAndAppendAsync(
            IDtlsDatagramChannel channel,
            DtlsTranscriptHash transcript,
            DtlsHandshakeType messageType,
            CancellationToken cancellationToken)
        {
            DtlsHandshakeFrame frame = await ReceiveFrameAsync(channel, cancellationToken).ConfigureAwait(false);
            RequireMessage(frame, messageType);
            if (messageType == DtlsHandshakeType.EncryptedExtensions)
            {
                DtlsHandshakeCodec.DecodeEncryptedExtensions(frame.Fragment);
            }

            transcript.Append(ToCompleteFrame(frame));
            return frame;
        }

        private static void RequireMessage(DtlsHandshakeFrame frame, DtlsHandshakeType messageType)
        {
            if (frame.MessageType != messageType)
            {
                throw new DtlsHandshakeException($"Unexpected DTLS handshake message {frame.MessageType}.");
            }
        }

        private void ValidateClientHello(DtlsClientHello hello)
        {
            if (!hello.CipherSuites.Contains(Profile.CipherSuite))
            {
                throw new DtlsHandshakeException("DTLS cipher suite downgrade is rejected.");
            }

            if (!hello.Extensions.SupportedGroups.Contains(Profile.KeyExchangeCurve)
                || !hello.Extensions.KeyShares.Any(k => k.Group == Profile.KeyExchangeCurve))
            {
                throw new DtlsHandshakeException("DTLS key_share group is unsupported by the selected profile.");
            }
        }

        private void ValidateServerHello(DtlsServerHello hello)
        {
            if (hello.CipherSuite != Profile.CipherSuite)
            {
                throw new DtlsHandshakeException("DTLS server selected an unexpected cipher suite; downgrade rejected.");
            }

            if (hello.Extensions.KeyShares.Count != 1 || hello.Extensions.KeyShares[0].Group != Profile.KeyExchangeCurve)
            {
                throw new DtlsHandshakeException("DTLS server selected an unsupported key_share group.");
            }
        }

        private async ValueTask ValidatePeerCertificateAsync(
            IReadOnlyList<X509Certificate2> peerChain,
            CancellationToken cancellationToken)
        {
            if (CertificateValidator is null)
            {
                throw new DtlsHandshakeException(
                    "DTLS peer certificate validation requires an injected CertificateValidator.");
            }

            await DtlsCertificateAuthenticator.ValidatePeerCertificateAsync(
                CertificateValidator,
                peerChain,
                cancellationToken).ConfigureAwait(false);
        }

        private X509Certificate2 GetLocalCertificate()
        {
            X509Certificate2? certificate = Options.LocalCertificate;
            if (certificate is null)
            {
                throw new DtlsHandshakeException("DTLS server authentication requires a configured local ECC certificate.");
            }

            using ECDsa? key = certificate.GetECDsaPrivateKey();
            if (key is null)
            {
                throw new DtlsHandshakeException("DTLS local certificate must be ECC and include an ECDSA private key.");
            }

            return certificate;
        }

        private X509Certificate2[] GetCertificateChain(X509Certificate2 localCertificate)
        {
            if (Options.LocalCertificateChain.Count == 0)
            {
                return [localCertificate];
            }

            return Options.LocalCertificateChain.ToArray();
        }

        private void InstallApplicationKeys(bool isClient)
        {
            DtlsHandshakeKeyingContext keyingContext = m_keyingContext
                ?? throw new InvalidOperationException("DTLS keying context was not created.");
            m_writeProtection = isClient
                ? keyingContext.CreateClientApplicationWriteProtection()
                : keyingContext.CreateServerApplicationWriteProtection();
            m_readProtection = isClient
                ? keyingContext.CreateServerApplicationWriteProtection()
                : keyingContext.CreateClientApplicationWriteProtection();
        }

        private static byte[] ToCompleteFrame(DtlsHandshakeFrame frame)
        {
            return DtlsHandshakeCodec.EncodeFrame(frame.MessageType, frame.MessageSequence, frame.Fragment);
        }

        private static byte[] CreateRandom(int length)
        {
            byte[] bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            return bytes;
        }

        private static HashAlgorithmName GetHashAlgorithm(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite is DtlsCipherSuite.TlsAes256GcmSha384 or DtlsCipherSuite.TlsSha384Sha384
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256;
        }

        private IPEndPoint GetCookieEndpoint(IDtlsDatagramChannel channel)
        {
            return channel.RemoteEndpoint ?? new IPEndPoint(Endpoint.Address, Endpoint.Port);
        }
#endif

        private DtlsTransportOptions Options { get; }

        private ICertificateValidatorEx? CertificateValidator { get; }

        private DtlsEndpointRole Role { get; }

        private UdpEndpoint Endpoint { get; }

        private TimeProvider TimeProvider { get; }

        private DtlsRecordProtection? m_writeProtection;
        private DtlsRecordProtection? m_readProtection;
        private DtlsHandshakeKeyingContext? m_keyingContext;
#if NET8_0_OR_GREATER
        private ushort m_nextSendSequence;
#endif
        private bool m_disposed;
    }
}
