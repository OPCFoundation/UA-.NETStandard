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
    internal sealed class DtlsHandshakeContext : IDtlsContext
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
            if (channel is null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            using ITimer? handshakeTimeout = CreateTimeoutTimer(handshakeCts, ComputeHandshakeTimeout());
            try
            {
                if (m_role == DtlsEndpointRole.Client)
                {
                    await ConnectAsync(channel, handshakeCts.Token).ConfigureAwait(false);
                }
                else
                {
                    await AcceptAsync(channel, handshakeCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                const string message =
                    "DTLS handshake exceeded the overall handshake timeout before completion.";
                throw m_lastPeerFailure is { } peerFailure
                    ? new DtlsHandshakeException(message, peerFailure)
                    : new DtlsHandshakeException(message);
            }
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

        private async ValueTask ConnectAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken)
        {
            using DtlsEcdheKeyExchange ecdhe = new(Profile.KeyExchangeCurve);
            var transcript = new DtlsTranscriptHash(GetHashAlgorithm(Profile.CipherSuite));
            IPEndPoint serverPeer = GetPeerEndpoint(channel);
            byte[] sessionId = CreateRandom(32);
            byte[] cookie = [];
            byte[] clientHelloBody = [];
            byte[] sharedSecret = [];
            int helloRetryRequests = 0;
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
                    DtlsHandshakeFrame firstFrame = await ReceiveFrameAsync(
                        channel,
                        serverPeer,
                        cancellationToken)
                        .ConfigureAwait(false);
                    RequireMessage(firstFrame, DtlsHandshakeType.ServerHello);
                    DtlsServerHello serverHello = DtlsHandshakeCodec.DecodeServerHello(firstFrame.Fragment);
                    if (serverHello.Extensions.Cookie.Length > 0 && serverHello.Extensions.KeyShares.Count == 0)
                    {
                        if (++helloRetryRequests > 1)
                        {
                            throw new DtlsHandshakeException(
                                "DTLS server sent more than one HelloRetryRequest; RFC 8446 §4.1.4 permits only one.");
                        }

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
                m_keyingContext = new DtlsHandshakeKeyingContext(Profile, sharedSecret, serverHelloHash);
                await ReceiveAndAppendAsync(
                    channel,
                    transcript,
                    DtlsHandshakeType.EncryptedExtensions,
                    serverPeer,
                    cancellationToken)
                    .ConfigureAwait(false);
                DtlsHandshakeFrame certificateFrame = await ReceiveFrameAsync(
                    channel,
                    serverPeer,
                    cancellationToken)
                    .ConfigureAwait(false);
                bool clientCertificateRequested = false;
                if (certificateFrame.MessageType == DtlsHandshakeType.CertificateRequest)
                {
                    DtlsHandshakeCodec.DecodeCertificateRequest(certificateFrame.Fragment);
                    transcript.Append(ToCompleteFrame(certificateFrame));
                    clientCertificateRequested = true;
                    certificateFrame = await ReceiveFrameAsync(
                        channel,
                        serverPeer,
                        cancellationToken).ConfigureAwait(false);
                }

                RequireMessage(certificateFrame, DtlsHandshakeType.Certificate);
                transcript.Append(ToCompleteFrame(certificateFrame));
                using (CertificateCollection peerChain =
                    DtlsCertificateAuthenticator.DecodeCertificate(certificateFrame.Fragment))
                {
                    await ValidatePeerCertificateAsync(peerChain, cancellationToken).ConfigureAwait(false);
                    DtlsCertificateAuthenticator.ValidateServerIdentity(peerChain[0], m_endpoint);
                    byte[] certificateVerifyTranscriptHash = transcript.GetHash();
                    DtlsHandshakeFrame certificateVerifyFrame = await ReceiveFrameAsync(
                        channel,
                        serverPeer,
                        cancellationToken)
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
                byte[] finishedTranscriptHash = transcript.GetHash();
                DtlsHandshakeFrame serverFinishedFrame = await ReceiveFrameAsync(
                    channel,
                    serverPeer,
                    cancellationToken)
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
                m_keyingContext.InstallApplicationSecrets(transcript.GetHash());
                if (clientCertificateRequested)
                {
                    await SendClientAuthenticationAsync(channel, transcript, cancellationToken).ConfigureAwait(false);
                }

                byte[] clientFinished = m_keyingContext.ComputeClientFinished(transcript.GetHash());
                byte[] clientFinishedFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.Finished,
                    m_nextSendSequence++,
                    DtlsHandshakeCodec.EncodeFinished(clientFinished));
                await SendFlightAsync(channel, clientFinishedFrame, cancellationToken).ConfigureAwait(false);
                CryptoUtils.ZeroMemory(clientFinished);
                InstallApplicationKeys(isClient: true);
                SetAuthenticatedPeer(channel, serverPeer);
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
            byte[] cookieKey = CreateRandom(32);
            TimeSpan flightTimeout = ComputeFlightTimeout();
            try
            {
                // RFC 9147 §4.5.2: invalid datagrams are discarded and the
                // association is preserved. Everything a peer sends before the
                // handshake completes is unauthenticated, so neither a malformed
                // datagram nor a handshake attempt that fails or stalls may end
                // the accept: the server drops it and waits for the next
                // ClientHello until the overall handshake timeout expires.
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (DtlsClientHello clientHello, DtlsHandshakeFrame clientHelloFrame, IPEndPoint clientSource) =
                        await ReceiveClientHelloAsync(channel, cookieKey, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await CompleteAcceptWithinFlightTimeoutAsync(
                            channel,
                            localCertificate,
                            clientHello,
                            clientHelloFrame,
                            clientSource,
                            cookieKey,
                            flightTimeout,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex) when (
                        !cancellationToken.IsCancellationRequested && IsPeerHandshakeFailure(ex))
                    {
                        m_lastPeerFailure = ex;
                        ResetAcceptState();
                    }
                }
            }
            finally
            {
                CryptoUtils.ZeroMemory(cookieKey);
            }
        }

        /// <summary>
        /// Waits for a ClientHello that matches the profile and carries a valid
        /// HelloRetryRequest cookie. Datagrams that are not such a ClientHello are
        /// dropped; a ClientHello without a valid cookie is answered statelessly
        /// with a HelloRetryRequest.
        /// </summary>
        private async ValueTask<(DtlsClientHello Hello, DtlsHandshakeFrame Frame, IPEndPoint Source)>
            ReceiveClientHelloAsync(
                IDtlsDatagramChannel channel,
                byte[] cookieKey,
                CancellationToken cancellationToken)
        {
            while (true)
            {
                // A ClientHello that arrived while another peer held the
                // accept is served before new datagrams are read.
                if (m_pendingClientHellos.Count > 0)
                {
                    return m_pendingClientHellos.Dequeue();
                }

                DtlsDatagram datagram = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    (DtlsClientHello Hello, DtlsHandshakeFrame Frame, IPEndPoint Source)? admitted =
                        await TryAdmitClientHelloAsync(
                            channel,
                            datagram,
                            cookieKey,
                            m_nextSendSequence,
                            cancellationToken).ConfigureAwait(false);
                    if (admitted is null)
                    {
                        // Answered with a HelloRetryRequest.
                        m_nextSendSequence++;
                        continue;
                    }

                    return admitted.Value;
                }
                catch (Exception ex) when (
                    !cancellationToken.IsCancellationRequested && IsPeerHandshakeFailure(ex))
                {
                    m_lastPeerFailure = ex;
                }
            }
        }

        /// <summary>
        /// Decodes a datagram as a ClientHello for the profile. Returns the
        /// ClientHello when it carries a valid HelloRetryRequest cookie;
        /// otherwise answers it statelessly with a HelloRetryRequest and
        /// returns <see langword="null"/>. Throws when the datagram is not a
        /// ClientHello for the profile.
        /// </summary>
        private async ValueTask<(DtlsClientHello Hello, DtlsHandshakeFrame Frame, IPEndPoint Source)?>
            TryAdmitClientHelloAsync(
                IDtlsDatagramChannel channel,
                DtlsDatagram datagram,
                byte[] cookieKey,
                ushort retrySequence,
                CancellationToken cancellationToken)
        {
            DtlsHandshakeFrame clientHelloFrame = DtlsHandshakeCodec.DecodeFrame(datagram.Payload.Span);
            RequireMessage(clientHelloFrame, DtlsHandshakeType.ClientHello);
            DtlsClientHello clientHello = DtlsHandshakeCodec.DecodeClientHello(clientHelloFrame.Fragment);
            ValidateClientHello(clientHello);
            IPEndPoint clientSource = datagram.Source ?? GetPeerEndpoint(channel);
            using var cookieProtector = new DtlsHelloRetryCookieProtector(cookieKey);
            if (m_options.RequireHelloRetryRequestCookie &&
                !cookieProtector.ValidateCookie(
                    clientSource,
                    [],
                    clientHello.Extensions.Cookie))
            {
                byte[] retryCookie = cookieProtector.CreateCookie(clientSource, []);
                byte[] retryFrame = BuildHelloRetryRequest(clientHello.SessionId, retryCookie, retrySequence);
                await SendFlightAsync(channel, retryFrame, cancellationToken, clientSource)
                    .ConfigureAwait(false);
                return null;
            }

            return (clientHello, clientHelloFrame, clientSource);
        }

        /// <summary>
        /// Receives the next handshake message of the peer whose handshake is
        /// being completed. RFC 9147 §4.5.2: datagrams that cannot be decoded
        /// or are not the expected message are discarded without ending the
        /// attempt, because the source address of a datagram is not
        /// authenticated. A ClientHello of another peer is answered with a
        /// HelloRetryRequest or, once it carries a valid cookie, kept for the
        /// next attempt, so that a peer that stalls does not make the server
        /// lose the handshake of the others.
        /// </summary>
        private async ValueTask<DtlsHandshakeFrame> ReceiveAcceptFrameAsync(
            IDtlsDatagramChannel channel,
            IPEndPoint clientSource,
            DtlsHandshakeType messageType,
            byte[] cookieKey,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                DtlsDatagram datagram = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                IPEndPoint? source = datagram.Source ?? channel.RemoteEndpoint;
                try
                {
                    if (source is null || !clientSource.Equals(source))
                    {
                        (DtlsClientHello Hello, DtlsHandshakeFrame Frame, IPEndPoint Source)? competing =
                            await TryAdmitClientHelloAsync(
                                channel,
                                datagram,
                                cookieKey,
                                0,
                                cancellationToken).ConfigureAwait(false);
                        if (competing is not null)
                        {
                            EnqueuePendingClientHello(competing.Value);
                        }
                        continue;
                    }

                    DtlsHandshakeFrame frame = DtlsHandshakeCodec.DecodeFrame(datagram.Payload.Span);
                    if (frame.MessageType == messageType)
                    {
                        return frame;
                    }
                }
                catch (Exception ex) when (
                    !cancellationToken.IsCancellationRequested && IsPeerHandshakeFailure(ex))
                {
                    m_lastPeerFailure = ex;
                }
            }
        }

        /// <summary>
        /// Keeps a cookie-validated ClientHello of a peer that has to wait for
        /// the accept. One entry per source and a small fixed number of
        /// entries, so that waiting peers cannot grow the queue.
        /// </summary>
        private void EnqueuePendingClientHello(
            (DtlsClientHello Hello, DtlsHandshakeFrame Frame, IPEndPoint Source) clientHello)
        {
            int count = m_pendingClientHellos.Count;
            bool replaced = false;
            for (int i = 0; i < count; i++)
            {
                (DtlsClientHello Hello, DtlsHandshakeFrame Frame, IPEndPoint Source) pending =
                    m_pendingClientHellos.Dequeue();
                if (pending.Source.Equals(clientHello.Source))
                {
                    pending = clientHello;
                    replaced = true;
                }
                m_pendingClientHellos.Enqueue(pending);
            }
            if (!replaced && count < MaxPendingClientHellos)
            {
                m_pendingClientHellos.Enqueue(clientHello);
            }
        }

        private async ValueTask CompleteAcceptWithinFlightTimeoutAsync(
            IDtlsDatagramChannel channel,
            Certificate localCertificate,
            DtlsClientHello clientHello,
            DtlsHandshakeFrame clientHelloFrame,
            IPEndPoint clientSource,
            byte[] cookieKey,
            TimeSpan flightTimeout,
            CancellationToken cancellationToken)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using ITimer? attemptTimeout = CreateTimeoutTimer(attemptCts, flightTimeout);
            await CompleteAcceptAsync(
                channel,
                localCertificate,
                clientHello,
                clientHelloFrame,
                clientSource,
                cookieKey,
                attemptCts.Token).ConfigureAwait(false);
        }

        private async ValueTask CompleteAcceptAsync(
            IDtlsDatagramChannel channel,
            Certificate localCertificate,
            DtlsClientHello clientHello,
            DtlsHandshakeFrame clientHelloFrame,
            IPEndPoint clientSource,
            byte[] cookieKey,
            CancellationToken cancellationToken)
        {
            using DtlsEcdheKeyExchange ecdhe = new(Profile.KeyExchangeCurve);
            var transcript = new DtlsTranscriptHash(GetHashAlgorithm(Profile.CipherSuite));
            byte[] sharedSecret = [];
            try
            {
                transcript.Append(ToCompleteFrame(clientHelloFrame));
                DtlsKeyShareEntry clientKeyShare = clientHello.Extensions.KeyShares
                    .First(k => k.Group == Profile.KeyExchangeCurve);
                sharedSecret = ecdhe.DeriveSharedSecret(clientKeyShare.KeyExchange);
                byte[] serverHelloFrame = BuildServerHello(clientHello.SessionId, ecdhe.PublicKey);
                await SendFlightAsync(channel, serverHelloFrame, cancellationToken, clientSource)
                    .ConfigureAwait(false);
                transcript.Append(serverHelloFrame);
                byte[] serverHelloHash = transcript.GetHash();
                m_keyingContext = new DtlsHandshakeKeyingContext(
                    Profile,
                    sharedSecret,
                    serverHelloHash);

                byte[] encryptedExtensionsFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.EncryptedExtensions,
                    m_nextSendSequence++,
                    DtlsHandshakeCodec.EncodeEncryptedExtensions());
                await SendFlightAsync(channel, encryptedExtensionsFrame, cancellationToken, clientSource)
                    .ConfigureAwait(false);
                transcript.Append(encryptedExtensionsFrame);
                if (m_options.RequireClientCertificate)
                {
                    byte[] certificateRequestFrame = DtlsHandshakeCodec.EncodeFrame(
                        DtlsHandshakeType.CertificateRequest,
                        m_nextSendSequence++,
                        DtlsHandshakeCodec.EncodeCertificateRequest());
                    await SendFlightAsync(channel, certificateRequestFrame, cancellationToken, clientSource)
                        .ConfigureAwait(false);
                    transcript.Append(certificateRequestFrame);
                }

                byte[] certificateFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.Certificate,
                    m_nextSendSequence++,
                    DtlsCertificateAuthenticator.EncodeCertificate([localCertificate]));
                await SendFlightAsync(channel, certificateFrame, cancellationToken, clientSource)
                    .ConfigureAwait(false);
                transcript.Append(certificateFrame);
                byte[] certificateVerifyBody = DtlsCertificateAuthenticator.SignCertificateVerify(
                    localCertificate,
                    Profile.CipherSuite,
                    transcript.GetHash());
                byte[] certificateVerifyFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.CertificateVerify,
                    m_nextSendSequence++,
                    certificateVerifyBody);
                await SendFlightAsync(channel, certificateVerifyFrame, cancellationToken, clientSource)
                    .ConfigureAwait(false);
                transcript.Append(certificateVerifyFrame);
                byte[] serverFinishedBody = DtlsHandshakeCodec.EncodeFinished(
                    m_keyingContext.ComputeServerFinished(transcript.GetHash()));
                byte[] serverFinishedFrame = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.Finished,
                    m_nextSendSequence++,
                    serverFinishedBody);
                await SendFlightAsync(channel, serverFinishedFrame, cancellationToken, clientSource)
                    .ConfigureAwait(false);
                transcript.Append(serverFinishedFrame);
                m_keyingContext.InstallApplicationSecrets(transcript.GetHash());
                if (m_options.RequireClientCertificate)
                {
                    await ReceiveClientAuthenticationAsync(
                        channel,
                        transcript,
                        clientSource,
                        cookieKey,
                        cancellationToken)
                        .ConfigureAwait(false);
                }

                byte[] expectedClientFinished = m_keyingContext.ComputeClientFinished(transcript.GetHash());
                try
                {
                    // A Finished that does not verify is discarded like any
                    // other invalid datagram: it may be forged with the
                    // address of the peer, whose own Finished is still due.
                    while (true)
                    {
                        DtlsHandshakeFrame clientFinishedFrame = await ReceiveAcceptFrameAsync(
                            channel,
                            clientSource,
                            DtlsHandshakeType.Finished,
                            cookieKey,
                            cancellationToken)
                            .ConfigureAwait(false);
                        byte[] actualClientFinished = [];
                        try
                        {
                            actualClientFinished = DtlsHandshakeCodec.DecodeFinished(clientFinishedFrame.Fragment);
                            m_keyingContext.VerifyFinished(expectedClientFinished, actualClientFinished);
                            break;
                        }
                        catch (Exception ex) when (
                            !cancellationToken.IsCancellationRequested && IsPeerHandshakeFailure(ex))
                        {
                            m_lastPeerFailure = ex;
                        }
                        finally
                        {
                            CryptoUtils.ZeroMemory(actualClientFinished);
                        }
                    }
                }
                finally
                {
                    CryptoUtils.ZeroMemory(expectedClientFinished);
                }

                InstallApplicationKeys(isClient: false);
                SetAuthenticatedPeer(channel, clientSource);
            }
            finally
            {
                CryptoUtils.ZeroMemory(sharedSecret);
            }
        }

        /// <summary>
        /// Whether a failure during the accept was caused by what a peer sent (or
        /// failed to send) rather than by the local endpoint. Such a failure ends
        /// the handshake attempt of that peer only.
        /// </summary>
        private static bool IsPeerHandshakeFailure(Exception exception)
        {
            return exception
                is DtlsHandshakeException
                    or CryptographicException
                    or ServiceResultException
                    or ArgumentException
                    or FormatException
                    or System.Net.Sockets.SocketException
                    // The per-attempt flight timeout; the caller has already
                    // excluded cancellation of the accept itself.
                    or OperationCanceledException;
        }

        /// <summary>
        /// Discards the state of a failed handshake attempt so that the next
        /// ClientHello starts from a clean server state.
        /// </summary>
        private void ResetAcceptState()
        {
            m_keyingContext?.Dispose();
            m_keyingContext = null;
            m_nextSendSequence = 0;
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

        private byte[] BuildHelloRetryRequest(byte[] sessionId, byte[] cookie, ushort sequence)
        {
            var retry = new DtlsServerHello(
                CreateRandom(32),
                sessionId,
                Profile.CipherSuite,
                DtlsHelloExtensions.CreateDefault([Profile.KeyExchangeCurve], [], cookie));
            return DtlsHandshakeCodec.EncodeFrame(
                DtlsHandshakeType.ServerHello,
                sequence,
                DtlsHandshakeCodec.EncodeServerHello(retry));
        }

        private static async ValueTask SendFlightAsync(
            IDtlsDatagramChannel channel,
            ReadOnlyMemory<byte> flight,
            CancellationToken cancellationToken,
            IPEndPoint? destination = null)
        {
            await channel.SendAsync(flight, destination, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask<DtlsHandshakeFrame> ReceiveFrameAsync(
            IDtlsDatagramChannel channel,
            IPEndPoint? expectedSource,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                DtlsDatagram datagram = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                IPEndPoint? source = datagram.Source ?? channel.RemoteEndpoint;
                if (expectedSource is not null &&
                    (source is null || !expectedSource.Equals(source)))
                {
                    continue;
                }

                return DtlsHandshakeCodec.DecodeFrame(datagram.Payload.Span);
            }
        }

        private async ValueTask SendClientAuthenticationAsync(
            IDtlsDatagramChannel channel,
            DtlsTranscriptHash transcript,
            CancellationToken cancellationToken)
        {
            using Certificate localCertificate = GetLocalCertificate();
            byte[] certificateFrame = DtlsHandshakeCodec.EncodeFrame(
                DtlsHandshakeType.Certificate,
                m_nextSendSequence++,
                DtlsCertificateAuthenticator.EncodeCertificate([localCertificate]));
            await SendFlightAsync(channel, certificateFrame, cancellationToken).ConfigureAwait(false);
            transcript.Append(certificateFrame);
            byte[] certificateVerifyBody = DtlsCertificateAuthenticator.SignCertificateVerify(
                localCertificate,
                Profile.CipherSuite,
                transcript.GetHash(),
                isServer: false);
            byte[] certificateVerifyFrame = DtlsHandshakeCodec.EncodeFrame(
                DtlsHandshakeType.CertificateVerify,
                m_nextSendSequence++,
                certificateVerifyBody);
            await SendFlightAsync(channel, certificateVerifyFrame, cancellationToken).ConfigureAwait(false);
            transcript.Append(certificateVerifyFrame);
        }

        private async ValueTask ReceiveClientAuthenticationAsync(
            IDtlsDatagramChannel channel,
            DtlsTranscriptHash transcript,
            IPEndPoint clientSource,
            byte[] cookieKey,
            CancellationToken cancellationToken)
        {
            DtlsHandshakeFrame certificateFrame = await ReceiveAcceptFrameAsync(
                channel,
                clientSource,
                DtlsHandshakeType.Certificate,
                cookieKey,
                cancellationToken)
                .ConfigureAwait(false);
            transcript.Append(ToCompleteFrame(certificateFrame));
            using CertificateCollection peerChain =
                DtlsCertificateAuthenticator.DecodeCertificate(certificateFrame.Fragment);
            await ValidatePeerCertificateAsync(peerChain, cancellationToken).ConfigureAwait(false);
            byte[] certificateVerifyTranscriptHash = transcript.GetHash();
            DtlsHandshakeFrame certificateVerifyFrame = await ReceiveAcceptFrameAsync(
                channel,
                clientSource,
                DtlsHandshakeType.CertificateVerify,
                cookieKey,
                cancellationToken)
                .ConfigureAwait(false);
            DtlsCertificateAuthenticator.VerifyCertificateVerify(
                peerChain[0],
                Profile.CipherSuite,
                certificateVerifyTranscriptHash,
                certificateVerifyFrame.Fragment,
                isServer: false);
            transcript.Append(ToCompleteFrame(certificateVerifyFrame));
        }

        private ITimer? CreateTimeoutTimer(CancellationTokenSource handshakeCts, TimeSpan timeout)
        {
            if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                return m_timeProvider.CreateTimer(
                    static state =>
                    {
                        try
                        {
                            ((CancellationTokenSource)state!).Cancel();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    },
                    handshakeCts,
                    timeout,
                    Timeout.InfiniteTimeSpan);
            }

            return null;
        }

        private TimeSpan ComputeHandshakeTimeout()
        {
            TimeSpan initial = m_options.InitialRetransmissionTimeout;
            TimeSpan max = m_options.MaxRetransmissionTimeout;
            TimeSpan unit = max > initial ? max : initial;
            if (unit <= TimeSpan.Zero)
            {
                return Timeout.InfiniteTimeSpan;
            }

            return TimeSpan.FromTicks(unit.Ticks * HandshakeFlightBudget);
        }

        /// <summary>
        /// Time one peer is given to complete its handshake once the server has
        /// answered its ClientHello: one flight of the overall handshake budget, so
        /// a peer that stalls cannot hold the accept until the overall timeout.
        /// </summary>
        private TimeSpan ComputeFlightTimeout()
        {
            TimeSpan overall = ComputeHandshakeTimeout();
            return overall == Timeout.InfiniteTimeSpan
                ? overall
                : TimeSpan.FromTicks(overall.Ticks / HandshakeFlightBudget);
        }
        private static async ValueTask<DtlsHandshakeFrame> ReceiveAndAppendAsync(
            IDtlsDatagramChannel channel,
            DtlsTranscriptHash transcript,
            DtlsHandshakeType messageType,
            IPEndPoint? expectedSource,
            CancellationToken cancellationToken)
        {
            DtlsHandshakeFrame frame = await ReceiveFrameAsync(
                channel,
                expectedSource,
                cancellationToken).ConfigureAwait(false);
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
            CertificateCollection peerChain,
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
#if NET8_0_OR_GREATER
            RandomNumberGenerator.Fill(bytes);
#else
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
#endif
            return bytes;
        }

        private static HashAlgorithmName GetHashAlgorithm(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite is DtlsCipherSuite.TlsAes256GcmSha384 or DtlsCipherSuite.TlsSha384Sha384
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256;
        }

        private IPEndPoint GetPeerEndpoint(IDtlsDatagramChannel channel)
        {
            return channel.RemoteEndpoint ?? new IPEndPoint(m_endpoint.Address, m_endpoint.Port);
        }

        private static void SetAuthenticatedPeer(
            IDtlsDatagramChannel channel,
            IPEndPoint? peer)
        {
            if (peer is not null && channel is IDtlsAuthenticatedPeerChannel peerChannel)
            {
                peerChannel.SetAuthenticatedPeer(peer);
            }
        }

        private readonly DtlsTransportOptions m_options;
        private readonly ICertificateValidatorEx? m_certificateValidator;
        private readonly DtlsEndpointRole m_role;
        private readonly UdpEndpoint m_endpoint;
        private readonly TimeProvider m_timeProvider;
        private DtlsRecordProtection? m_writeProtection;
        private DtlsRecordProtection? m_readProtection;
        private DtlsHandshakeKeyingContext? m_keyingContext;
        private Exception? m_lastPeerFailure;
        private readonly Queue<(DtlsClientHello Hello, DtlsHandshakeFrame Frame, IPEndPoint Source)>
            m_pendingClientHellos = new();
        private const int MaxPendingClientHellos = 8;
        private const int HandshakeFlightBudget = 4;
        private ushort m_nextSendSequence;
        private bool m_disposed;
    }
}
