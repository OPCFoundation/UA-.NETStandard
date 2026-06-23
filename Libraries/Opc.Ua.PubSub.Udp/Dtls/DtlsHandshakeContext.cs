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
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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
        /// <summary>
        /// Initializes a new <see cref="DtlsHandshakeContext"/> for the supplied profile,
        /// transport options, endpoint role and certificate validator.
        /// </summary>
        public DtlsHandshakeContext(
            DtlsProfile profile,
            DtlsTransportOptions options,
            ICertificateValidatorEx? certificateValidator,
            DtlsEndpointRole role,
            UdpEndpoint endpoint,
            TimeProvider timeProvider)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_certificateValidator = certificateValidator;
            m_role = role;
            m_endpoint = endpoint;
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <inheritdoc/>
        public DtlsProfile Profile { get; }

        /// <inheritdoc/>
        public async ValueTask OpenAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken = default)
        {
#if NET8_0_OR_GREATER
            if (channel is null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (m_role == DtlsEndpointRole.Client)
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

        /// <inheritdoc/>
        public ValueTask<ReadOnlyMemory<byte>> ProtectAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DtlsRecordProtection protection = m_writeProtection
                ?? throw new InvalidOperationException("DTLS application write keys are not installed.");
            return new ValueTask<ReadOnlyMemory<byte>>(protection.Seal(payload.Span));
        }

        /// <inheritdoc/>
        public ValueTask<ReadOnlyMemory<byte>> UnprotectAsync(
            ReadOnlyMemory<byte> record,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DtlsRecordProtection protection = m_readProtection
                ?? throw new InvalidOperationException("DTLS application read keys are not installed.");
            return new ValueTask<ReadOnlyMemory<byte>>(protection.Open(record.Span));
        }

        /// <inheritdoc/>
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
                IReadOnlyList<Certificate> peerChain =
                    DtlsCertificateAuthenticator.DecodeCertificate(certificateFrame.Fragment);
                try
                {
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
                }
                finally
                {
                    foreach (Certificate peerCertificate in peerChain)
                    {
                        peerCertificate.Dispose();
                    }
                }
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
                    CryptoUtils.ZeroMemory(expectedServerFinished);
                    CryptoUtils.ZeroMemory(actualServerFinished);
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
                CryptoUtils.ZeroMemory(clientHelloBody);
                CryptoUtils.ZeroMemory(sharedSecret);
            }
        }
        private async ValueTask AcceptAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken)
        {
            using Certificate localCertificate = GetLocalCertificate();
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
                    if (m_options.RequireHelloRetryRequestCookie &&
                        !cookieProtector.ValidateCookie(
                            remoteEndpoint,
                            [],
                            clientHello.Extensions.Cookie))
                    {
                        byte[] retryCookie = cookieProtector.CreateCookie(remoteEndpoint, []);
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
                    DtlsCertificateAuthenticator.EncodeCertificate([localCertificate]));
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
                    CryptoUtils.ZeroMemory(expectedClientFinished);
                    CryptoUtils.ZeroMemory(actualClientFinished);
                }

                InstallApplicationKeys(isClient: false);
            }
            finally
            {
                CryptoUtils.ZeroMemory(cookieKey);
                CryptoUtils.ZeroMemory(sharedSecret);
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
                m_options.InitialRetransmissionTimeout,
                m_options.MaxRetransmissionTimeout);
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

            if (!hello.Extensions.SupportedGroups.Contains(Profile.KeyExchangeCurve) ||
                !hello.Extensions.KeyShares.Any(k => k.Group == Profile.KeyExchangeCurve))
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
            IReadOnlyList<Certificate> peerChain,
            CancellationToken cancellationToken)
        {
            if (m_certificateValidator is null)
            {
                throw new DtlsHandshakeException(
                    "DTLS peer certificate validation requires an injected CertificateValidator.");
            }

            await DtlsCertificateAuthenticator.ValidatePeerCertificateAsync(
                m_certificateValidator,
                peerChain,
                cancellationToken).ConfigureAwait(false);
        }

        private Certificate GetLocalCertificate()
        {
            foreach (Certificate candidate in m_options.LocalCertificates)
            {
                if (candidate is null)
                {
                    continue;
                }

                using ECDsa? key = candidate.GetECDsaPrivateKey();
                if (key is null)
                {
                    continue;
                }

                if (MatchesCertificateCurve(key, Profile.CertificateCurve))
                {
                    return candidate.AddRef();
                }
            }

            throw new DtlsHandshakeException(
                "DTLS server authentication requires a configured local ECC certificate with an ECDSA private key " +
                "matching the negotiated profile certificate curve.");
        }

        private static bool MatchesCertificateCurve(ECDsa key, DtlsNamedCurve expected)
        {
            ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
            ECCurve curve = parameters.Curve;
            if (!curve.IsNamed)
            {
                return false;
            }

            string? oid = curve.Oid?.Value;
            string? friendlyName = curve.Oid?.FriendlyName;
            return expected switch
            {
                DtlsNamedCurve.NistP256 => MatchesCurveIdentifier(
                    oid, friendlyName, "1.2.840.10045.3.1.7", "nistP256", "ECDSA_P256", "secp256r1"),
                DtlsNamedCurve.NistP384 => MatchesCurveIdentifier(
                    oid, friendlyName, "1.3.132.0.34", "nistP384", "ECDSA_P384", "secp384r1"),
                DtlsNamedCurve.BrainpoolP256r1 => MatchesCurveIdentifier(
                    oid, friendlyName, "1.3.36.3.3.2.8.1.1.7", "brainpoolP256r1"),
                DtlsNamedCurve.BrainpoolP384r1 => MatchesCurveIdentifier(
                    oid, friendlyName, "1.3.36.3.3.2.8.1.1.11", "brainpoolP384r1"),
                _ => false
            };
        }

        private static bool MatchesCurveIdentifier(string? oid, string? friendlyName, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(oid, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(friendlyName, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
            return channel.RemoteEndpoint ?? new IPEndPoint(m_endpoint.Address, m_endpoint.Port);
        }
#endif

        private readonly DtlsTransportOptions m_options;
        private readonly ICertificateValidatorEx? m_certificateValidator;
        private readonly DtlsEndpointRole m_role;
        private readonly UdpEndpoint m_endpoint;
        private readonly TimeProvider m_timeProvider;
        private DtlsRecordProtection? m_writeProtection;
        private DtlsRecordProtection? m_readProtection;
        private DtlsHandshakeKeyingContext? m_keyingContext;
#if NET8_0_OR_GREATER
        private ushort m_nextSendSequence;
#endif
        private bool m_disposed;
    }
}
