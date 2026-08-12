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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Accepts QUIC connections and turns each one into an OPC UA
    /// SecureChannel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first inbound bidirectional stream of a connection carries the
    /// UACP and Secure Conversation conversation byte for byte as it
    /// appears over opc.tcp, so the accepted connection is wrapped in a
    /// <see cref="QuicMultiplexedTransport"/> and attached to an ordinary
    /// <see cref="TcpServerChannel"/>. Chunking, security, token renewal
    /// and request dispatch are the existing implementation, untouched.
    /// </para>
    /// <para>
    /// Losing the control stream is losing the SecureChannel: the channel
    /// is torn down and every data channel on it is aborted.
    /// </para>
    /// </remarks>
    public sealed class QuicTransportListener :
        ITransportListener,
        ITcpChannelListener,
        ITransportListenerCertificateRotation
    {
        /// <summary>
        /// Creates a listener.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="bufferManagerFactory">Factory used to create the
        /// channel buffer manager.</param>
        public QuicTransportListener(
            ITelemetryContext telemetry,
            IBufferManagerFactory? bufferManagerFactory = null)
        {
            Telemetry = telemetry;
            m_bufferManagerFactory = bufferManagerFactory ?? DefaultBufferManagerFactory.Instance;
            m_logger = telemetry.CreateLogger<QuicTransportListener>();
        }

        /// <summary>
        /// The telemetry context the listener and its channels use.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcQuic;

        /// <inheritdoc/>
        public string ListenerId { get; private set; } = default!;

        /// <inheritdoc/>
        public Uri EndpointUrl { get; private set; } = null!;

        /// <inheritdoc/>
        public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

        /// <inheritdoc/>
        public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

        /// <inheritdoc/>
        public async ValueTask OpenAsync(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback,
            CancellationToken ct = default)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!QuicListener.IsSupported)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "QUIC is unavailable on this platform, so an opc.quic endpoint cannot be opened.");
            }

            ListenerId = Guid.NewGuid().ToString();
            EndpointUrl = baseAddress ?? throw new ArgumentNullException(nameof(baseAddress));
            m_descriptions = settings.Descriptions ?? [];
            m_callback = callback;

            EndpointConfiguration? configuration = settings.Configuration;
            var messageContext = new ServiceMessageContext(Telemetry, settings.Factory!)
            {
                NamespaceUris = settings.NamespaceUris!,
                ServerUris = new StringTable()
            };

            m_quotas = new ChannelQuotas(messageContext);

            if (configuration != null)
            {
                m_quotas.MaxBufferSize = configuration.MaxBufferSize;
                m_quotas.MaxMessageSize =
                    TcpMessageLimits.AlignRoundMaxMessageSize(configuration.MaxMessageSize);
                m_quotas.ChannelLifetime = configuration.ChannelLifetime;
                m_quotas.SecurityTokenLifetime = configuration.SecurityTokenLifetime;
                messageContext.MaxArrayLength = configuration.MaxArrayLength;
                messageContext.MaxByteStringLength = configuration.MaxByteStringLength;
                messageContext.MaxMessageSize = m_quotas.MaxMessageSize;
                messageContext.MaxStringLength = configuration.MaxStringLength;
                messageContext.MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels;
                messageContext.MaxDecoderRecoveries = configuration.MaxDecoderRecoveries;
            }

            m_quotas.CertificateValidator = settings.CertificateValidator;
            m_serverCertificates = settings.ServerCertificates!;
            m_bufferManager = new BufferManager(
                m_bufferManagerFactory.Create("QuicServer", m_quotas.MaxBufferSize, Telemetry));

            int port = baseAddress.Port >= 0
                ? baseAddress.Port
                : DataChannelConstants.QuicDefaultPort;

            // The Server's TLS certificate shall be the Server's
            // Application Instance Certificate, or shall carry the same
            // subjectPublicKeyInfo, because that is what the key equality
            // check of Part 6 errata 7.6.1 compares against.
            X509Certificate2 tlsCertificate = ResolveTlsCertificate();
            lock (m_certificateActivationLock)
            {
                m_tlsCertificate = tlsCertificate;
            }

            var listenerOptions = new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, port),
                ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                ConnectionOptionsCallback = async (connection, _, _) =>
                {
                    X509Certificate2 serverCertificate;
                    long activationEpoch;

                    lock (m_certificateActivationLock)
                    {
                        serverCertificate = m_tlsCertificate
                            ?? throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "The server has no TLS certificate to present.");
                        activationEpoch = m_certificateEpoch;
                    }

                    TimeSpan handshakeTimeout = PendingAdmissionHandshakeTimeout;
                    var admission = new QuicAdmissionSnapshot(
                        activationEpoch,
                        serverCertificate,
                        TimestampAfter(handshakeTimeout));
                    RemoveExpiredPendingAdmissions();
                    m_pendingConnectionEpochs[connection] = admission;
                    _ = ExpirePendingAdmissionAsync(connection, admission, handshakeTimeout).AsTask();

                    Func<QuicConnection, long, ValueTask>? pause =
                        AdmissionCallbackPauseForTesting;
                    if (pause != null)
                    {
                        await pause(connection, activationEpoch).ConfigureAwait(false);
                    }

                    return new QuicServerConnectionOptions
                    {
                        DefaultStreamErrorCode = 0x0A,
                        DefaultCloseErrorCode = 0x0B,
                        MaxInboundBidirectionalStreams = MaxInboundStreams,
                        MaxInboundUnidirectionalStreams = MaxInboundStreams,
#if NET9_0_OR_GREATER
                        // QuicServerConnectionOptions.HandshakeTimeout is .NET 9+.
                        // On net8.0 the platform default applies instead; the
                        // pending admission still expires on this listener's own
                        // clock through ExpirePendingAdmissionAsync, so a peer
                        // that stalls the handshake cannot hold an admission
                        // slot open there either.
                        HandshakeTimeout = handshakeTimeout,
#endif
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                            ServerCertificate = serverCertificate,
                            ClientCertificateRequired = true,
                            RemoteCertificateValidationCallback = ValidatePeerCertificate,
                            AllowTlsResume = false
                        }
                    };
                }
            };

            m_listener = await QuicListener
                .ListenAsync(listenerOptions, ct)
                .ConfigureAwait(false);

            if (port == 0 && m_listener.LocalEndPoint is IPEndPoint actual)
            {
                port = actual.Port;
                EndpointUrl = ReplacePort(baseAddress, actual.Port);
            }

            var stop = new CancellationTokenSource();
            m_stop = stop;
            m_acceptLoop = Task.Run(() => RunAcceptLoopAsync(stop.Token), CancellationToken.None);

            m_logger.QuicListenerOpened(EndpointUrl, port);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken ct = default)
        {
            CancellationTokenSource? stop = Interlocked.Exchange(ref m_stop, null);

            if (stop != null)
            {
                await stop.CancelAsync().ConfigureAwait(false);
            }

            QuicListener? listener = Interlocked.Exchange(ref m_listener, null);

            if (listener != null)
            {
                await listener.DisposeAsync().ConfigureAwait(false);
            }

            Task? acceptLoop = Interlocked.Exchange(ref m_acceptLoop, null);

            if (acceptLoop != null)
            {
                try
                {
                    await acceptLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
            }

            foreach (TcpListenerChannel channel in m_channels.Values.ToArray())
            {
                channel.Dispose();
            }

            m_channels.Clear();
            m_connectionBindings.Clear();
            m_pendingConnectionEpochs.Clear();
            stop?.Dispose();

            X509Certificate2? tlsCertificate = Interlocked.Exchange(ref m_tlsCertificate, null);
            tlsCertificate?.Dispose();

            lock (m_retiredTlsCertificates)
            {
                foreach (X509Certificate2 retired in m_retiredTlsCertificates)
                {
                    retired.Dispose();
                }

                m_retiredTlsCertificates.Clear();
            }
        }

        /// <summary>
        /// Releases the cancellation source that bounds the accept loop.
        /// </summary>
        public void Dispose()
        {
            m_stop?.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await CloseAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public void CertificateUpdate(
            ICertificateValidatorEx validator,
            ICertificateRegistry serverCertificates)
        {
            if (m_quotas != null)
            {
                m_quotas.CertificateValidator = validator;
            }

            m_serverCertificates = serverCertificates;

            // The endpoint descriptions advertise the certificate a client
            // should expect, so they shall move with it.
            if (m_descriptions != null)
            {
                foreach (EndpointDescription description in m_descriptions)
                {
                    if (description.ServerCertificate.IsEmpty)
                    {
                        continue;
                    }

                    using CertificateEntry? entry = serverCertificates
                        .AcquireApplicationCertificateBySecurityPolicy(
                            description.SecurityPolicyUri ?? SecurityPolicies.Basic256Sha256);

                    if (entry?.Certificate == null)
                    {
                        continue;
                    }

                    description.ServerCertificate = serverCertificates.SendCertificateChain
                        ? entry.GetEncodedChainBlob().ToByteString()
                        : entry.Certificate.RawData.ToByteString();
                }
            }

            // Only replace the TLS certificate once the listener is running;
            // OpenAsync resolves it for itself.
            if (m_listener == null)
            {
                return;
            }

            X509Certificate2 rotated = ResolveTlsCertificate();
            X509Certificate2? retired;
            bool keyChanged = false;
            long activatedEpoch = 0;

            lock (m_certificateActivationLock)
            {
                retired = m_tlsCertificate;
                keyChanged = retired != null && !SamePublicKey(retired, rotated);
                if (keyChanged)
                {
                    activatedEpoch = ++m_certificateEpoch;
                }

                m_tlsCertificate = rotated;
            }

            // A handshake already in flight still holds the outgoing
            // certificate, so retire rather than dispose it and release the
            // whole set when the listener closes. Rotation is rare enough
            // that the set stays small.
            if (retired != null)
            {
                lock (m_retiredTlsCertificates)
                {
                    m_retiredTlsCertificates.Add(retired);
                }

                if (keyChanged)
                {
                    _ = CloseChannelsForSupersededEpochAsync(
                        activatedEpoch,
                        StatusCodes.BadSecurityChecksFailed,
                        CancellationToken.None).AsTask();
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateChannelLastActiveTime(string globalChannelId)
        {
            foreach (TcpListenerChannel channel in m_channels.Values)
            {
                if (string.Equals(channel.GlobalChannelId, globalChannelId, StringComparison.Ordinal))
                {
                    channel.UpdateLastActiveTime();
                    return;
                }
            }
        }

        /// <inheritdoc/>
        public bool ReconnectToExistingChannel(
            IUaSCByteTransport transport,
            uint requestId,
            uint sequenceNumber,
            uint channelId,
            Certificate clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request)
        {
            // A QUIC connection survives the client's address changing, so
            // the reconnect-to-existing-channel path that exists to
            // recover a broken TCP socket has no counterpart here
            // (Part 6 errata 7.7).
            throw ServiceResultException.Create(
                StatusCodes.BadTcpSecureChannelUnknown,
                "opc.quic recovers a path change through connection migration, not reconnect.");
        }

#pragma warning disable CS0618 // Obsolete: retained for interface compatibility.
        /// <inheritdoc/>
        public Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl)
        {
            return TransferListenerChannelAsync(channelId, serverUri, endpointUrl);
        }
#pragma warning restore CS0618

        /// <inheritdoc/>
        public async Task<bool> TransferListenerChannelAsync(
            uint channelId,
            string serverUri,
            Uri endpointUrl)
        {
            if (!m_channels.TryGetValue(channelId, out TcpListenerChannel? channel))
            {
                return false;
            }

            ConnectionWaitingHandlerAsync? handler = ConnectionWaiting;

            if (handler == null)
            {
                return false;
            }

            IUaSCByteTransport? transport = await channel.DetachTransportAsync()
                .ConfigureAwait(false);

            if (transport == null)
            {
                return false;
            }

            var args = new TcpConnectionWaitingEventArgs(
                serverUri,
                endpointUrl,
                transport);

            await handler(this, args).ConfigureAwait(false);

            if (!args.Accepted)
            {
                // The caller rejected the handoff, so re-attach the
                // transport and keep the existing channel working.
                channel.Transport = transport;
                channel.StartReceiveLoop();
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public void ChannelClosed(uint channelId)
        {
            m_connectionBindings.TryRemove(channelId, out _);

            if (m_channels.TryRemove(channelId, out TcpListenerChannel? channel))
            {
                ConnectionStatusChanged?.Invoke(
                    this,
                    new ConnectionStatusEventArgs(EndpointUrl, ServiceResult.Good, closed: true));

                channel.Dispose();
            }
        }

        /// <inheritdoc/>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            _ = RunReverseConnectAsync(url, timeout);
        }

        /// <inheritdoc/>
        public TrustListIdentifier PeerCertificateTrustListScope => TrustListIdentifier.Peers;

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<string>> CloseChannelsForCertificateAsync(
            Certificate oldCertificate,
            CancellationToken ct = default)
        {
            return CloseChannelsForCertificateAsync(
                oldCertificate,
                StatusCodes.BadSecurityChecksFailed,
                ct);
        }

        /// <summary>
        /// Closes channels whose own bound certificate no longer validates.
        /// </summary>
        public async ValueTask<IReadOnlyList<string>> CloseChannelsForOwnCertificateAsync(
            Func<Certificate, CancellationToken, ValueTask<bool>> isOwnCertificateTrustedAsync,
            CancellationToken ct = default)
        {
            if (isOwnCertificateTrustedAsync == null)
            {
                throw new ArgumentNullException(nameof(isOwnCertificateTrustedAsync));
            }

            var closed = new List<string>();

            foreach (TcpListenerChannel channel in m_channels.Values.ToArray())
            {
                Certificate? own = null;
                byte[]? rawData = channel.ServerCertificate?.RawData;
                if ((rawData == null || rawData.Length == 0) &&
                    TryGetChannelId(channel, out uint ownChannelId) &&
                    m_connectionBindings.TryGetValue(
                        ownChannelId,
                        out QuicConnectionBinding? ownBinding))
                {
                    rawData = ownBinding.CertificateRawData;
                }

                if (rawData == null || rawData.Length == 0)
                {
                    continue;
                }

                try
                {
                    own = Certificate.FromRawData(rawData);
                    if (await isOwnCertificateTrustedAsync(own, ct).ConfigureAwait(false))
                    {
                        continue;
                    }
                }
                finally
                {
                    own?.Dispose();
                }

                string? globalChannelId = await CloseQuicChannelAsync(
                    channel,
                    StatusCodes.BadSecurityChecksFailed,
                    ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(globalChannelId))
                {
                    closed.Add(globalChannelId!);
                }
            }

            return closed;
        }

        private async ValueTask<IReadOnlyList<string>> CloseChannelsForCertificateAsync(
            Certificate oldCertificate,
            StatusCode closeErrorCode,
            CancellationToken ct = default)
        {
            if (oldCertificate == null)
            {
                throw new ArgumentNullException(nameof(oldCertificate));
            }

            string thumbprint = oldCertificate.Thumbprint;

            if (string.IsNullOrEmpty(thumbprint))
            {
                return [];
            }

            byte[] oldPublicKey = oldCertificate.AsX509Certificate2().GetPublicKey();

            // A re-issue that keeps the same key is transparent under
            // Part 6 errata 7.6.2: the binding of 7.6.1 is by
            // subjectPublicKeyInfo, so an established connection remains
            // consistent and shall not be torn down. Only a change of key
            // forces the connections bound to the old one to close, so
            // matching on the thumbprint alone would abort every live
            // media stream on an ordinary scheduled renewal.
            X509Certificate2? active;
            lock (m_certificateActivationLock)
            {
                active = m_tlsCertificate;
            }

            if (active != null && SamePublicKey(active, oldCertificate.AsX509Certificate2()))
            {
                return [];
            }

            return await CloseChannelsForCertificateAsync(
                thumbprint,
                oldPublicKey,
                closeErrorCode,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<string>> CloseChannelsForUntrustedPeersAsync(
            Func<Certificate, CancellationToken, ValueTask<bool>> isPeerTrustedAsync,
            CancellationToken ct = default)
        {
            if (isPeerTrustedAsync == null)
            {
                throw new ArgumentNullException(nameof(isPeerTrustedAsync));
            }

            var closed = new List<string>();

            foreach (TcpListenerChannel channel in m_channels.Values.ToArray())
            {
                using Certificate? peer = channel.SnapshotClientCertificateForRevalidation();

                if (peer == null)
                {
                    continue;
                }

                if (await isPeerTrustedAsync(peer, ct).ConfigureAwait(false))
                {
                    continue;
                }

                string? globalChannelId = await CloseQuicChannelAsync(
                    channel,
                    StatusCodes.BadCertificateUntrusted,
                    ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(globalChannelId))
                {
                    closed.Add(globalChannelId!);
                }
            }

            return closed;
        }

        private async Task RunAcceptLoopAsync(CancellationToken ct)
        {
            QuicListener? listener = m_listener;

            while (listener != null && !ct.IsCancellationRequested)
            {
                QuicConnection connection;

                try
                {
                    connection = await listener.AcceptConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (QuicException e)
                {
                    m_logger.QuicAcceptFailed(e);
                    continue;
                }
                catch (AuthenticationException e)
                {
                    m_logger.QuicAcceptFailed(e);
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = HandleConnectionAsync(connection, ct);
            }
        }

        private async Task HandleConnectionAsync(QuicConnection connection, CancellationToken ct)
        {
            QuicMultiplexedTransport? transport = null;
            uint channelId = 0;

            try
            {
                if (!TryTakeConnectionAdmission(connection, out QuicAdmissionSnapshot? admission) ||
                    admission == null ||
                    admission.ActivationEpoch != Volatile.Read(ref m_certificateEpoch))
                {
                    await CloseConnectionAsync(
                        connection,
                        StatusCodes.BadSecurityChecksFailed,
                        ct).ConfigureAwait(false);
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                // The first inbound bidirectional stream is the control
                // stream; everything beside it belongs to data channels.
                QuicStream control = await connection
                    .AcceptInboundStreamAsync(ct)
                    .ConfigureAwait(false);

                transport = new QuicMultiplexedTransport(
                    connection,
                    control,
                    m_bufferManager!,
                    m_quotas!.MaxBufferSize,
                    Telemetry);
                var boundTransport = new QuicPeerBindingTransport(
                    transport,
                    m_bufferManager!,
                    endpointDescription: null,
                    bindToOpenSecureChannelOnly: true);

                channelId = NextChannelId();

                var channel = new TcpServerChannel(
                    ListenerId,
                    this,
                    m_bufferManager!,
                    m_quotas!,
                    m_serverCertificates!,
                    m_descriptions!,
                    Telemetry);

                if (m_callback != null)
                {
                    channel.SetRequestReceivedCallback(
                        new TcpChannelRequestEventHandler(OnRequestReceived));
                }

                m_channels[channelId] = channel;
                m_connectionBindings[channelId] = new QuicConnectionBinding(
                    connection,
                    admission.ActivationEpoch,
                    admission.Certificate.RawData);

                // Ownership passes to the channel, which starts the
                // receive loop on the control stream.
                channel.Attach(channelId, boundTransport);
                transport = null;

                ConnectionStatusChanged?.Invoke(
                    this,
                    new ConnectionStatusEventArgs(EndpointUrl, ServiceResult.Good, closed: false));
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
#pragma warning disable CA1031 // One bad connection must not stop the listener.
            catch (Exception e)
#pragma warning restore CA1031
            {
                m_logger.QuicConnectionSetupFailed(e);
            }
            finally
            {
                if (transport != null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }

                if (transport != null && channelId != 0)
                {
                    m_connectionBindings.TryRemove(channelId, out _);
                }
            }
        }

        private async Task RunReverseConnectAsync(Uri url, int timeout)
        {
            try
            {
                // Under reverse connect the OPC UA Server holds the QUIC
                // client role: it opens the connection and sends RHE, and
                // the Client replies HEL on the same stream
                // (Part 6 errata 7.10).
                var options = new QuicClientOptions
                {
                    ClientCertificate = ResolveTlsCertificate(),
                    ServerCertificateValidation = ValidatePeerCertificate,
                    HandshakeTimeout = timeout > 0
                        ? TimeSpan.FromMilliseconds(timeout)
                        : TimeSpan.FromSeconds(30)
                };

                QuicConnection connection = await QuicConnectionBuilder
                    .ConnectAsync(url, options, CancellationToken.None)
                    .ConfigureAwait(false);

                QuicStream control = await connection
                    .OpenOutboundStreamAsync(QuicStreamType.Bidirectional, CancellationToken.None)
                    .ConfigureAwait(false);

                QuicMultiplexedTransport? transport = null;

                try
                {
                    transport = new QuicMultiplexedTransport(
                        connection,
                        control,
                        m_bufferManager!,
                        m_quotas!.MaxBufferSize,
                        Telemetry);
                    var boundTransport = new QuicPeerBindingTransport(
                        transport,
                        m_bufferManager!,
                        endpointDescription: null,
                        bindToOpenSecureChannelOnly: true);

                    uint channelId = NextChannelId();

                    var channel = new TcpReverseConnectChannel(
                        ListenerId,
                        this,
                        m_bufferManager!,
                        m_quotas!,
                        m_descriptions!,
                        Telemetry);

                    m_channels[channelId] = channel;

                    // Ownership passes to the channel.
                    channel.Attach(channelId, boundTransport);
                    transport = null;
                }
                finally
                {
                    if (transport != null)
                    {
                        await transport.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
#pragma warning disable CA1031 // A failed reverse connect must not fault the listener.
            catch (Exception e)
#pragma warning restore CA1031
            {
                m_logger.QuicReverseConnectFailed(e, url);
            }
        }

        private bool ValidatePeerCertificate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (certificate == null)
            {
                // §7.6.1 requires the TLS server to *request* a client
                // certificate on every connection, and separately forbids
                // accepting OpenDataChannel on a connection that completed
                // without one. Refusing the handshake here would collapse
                // those two obligations into one and make the Discovery
                // Services unreachable: GetEndpoints and FindServers run on
                // a SecurityPolicy None channel, which by construction has
                // no client certificate to present. The absence is recorded
                // and enforced where the specification puts it, at
                // OpenDataChannel.
                m_logger.QuicPeerCertificateMissing();
                return true;
            }

            if (m_quotas?.CertificateValidator == null)
            {
                return false;
            }

            try
            {
                var validationChain = new X509Certificate2Collection();
                if (chain?.ChainElements != null && chain.ChainElements.Count > 0)
                {
                    foreach (X509ChainElement element in chain.ChainElements)
                    {
                        validationChain.Add(element.Certificate);
                    }
                }
                else if (certificate is X509Certificate2 certificate2)
                {
                    validationChain.Add(certificate2);
                }
                else
                {
                    validationChain.Add(new X509Certificate2(certificate));
                }

                using var validationCollection = CertificateCollection.From(validationChain);
#pragma warning disable CA2025
                CertificateValidationResult result = m_quotas.CertificateValidator
                    .ValidateAsync(validationCollection, ct: default)
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore CA2025

                if (!result.IsValid)
                {
                    m_logger.QuicPeerCertificateRejected(
                        certificate.Subject,
                        result.StatusCode.ToString());
                }

                return result.IsValid;
            }
            catch (Exception e)
            {
                m_logger.QuicPeerCertificateRejected(certificate.Subject, e.Message);
                return false;
            }
        }

        private async void OnRequestReceived(
            TcpListenerChannel channel,
            uint requestId,
            IServiceRequest request)
        {
            try
            {
                if (m_callback == null)
                {
                    return;
                }

                var context = new SecureChannelContext(
                    channel.GlobalChannelId,
                    channel.EndpointDescription,
                    RequestEncoding.Binary,
                    channel.ClientCertificate?.RawData,
                    channel.ServerCertificate?.RawData,
                    channel.ChannelThumbprint);

                IServiceResponse response = await m_callback
                    .ProcessRequestAsync(context, request)
                    .ConfigureAwait(false);

                ((TcpServerChannel)channel).SendResponse(requestId, response);

                NotifyResponseDispatched(context);
            }
#pragma warning disable CA1031 // A failed request must not fault the listener.
            catch (Exception e)
#pragma warning restore CA1031
            {
                m_logger.QuicRequestFailed(e, requestId);
            }
        }

        private void NotifyResponseDispatched(SecureChannelContext context)
        {
            try
            {
                context.ResponseDispatched?.Invoke();
            }
#pragma warning disable CA1031 // A faulty callback must not fault the listener.
            catch (Exception e)
#pragma warning restore CA1031
            {
                m_logger.QuicResponseDispatchedFailed(e);
            }
        }

        private X509Certificate2 ResolveTlsCertificate()
        {
            // The Server presents its Application Instance Certificate as
            // the TLS certificate, which is what makes the key equality
            // check of Part 6 errata 7.6.1 satisfiable at all.
            string policy = m_descriptions is { Count: > 0 }
                ? m_descriptions[0].SecurityPolicyUri ?? SecurityPolicies.Basic256Sha256
                : SecurityPolicies.Basic256Sha256;

            using CertificateEntry? entry = m_serverCertificates
                ?.AcquireApplicationCertificateBySecurityPolicy(policy);

            return entry?.Certificate?.AsX509Certificate2()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The server has no Application Instance Certificate to present over TLS.");
        }

        private async ValueTask<IReadOnlyList<string>> CloseChannelsForCertificateAsync(
            string oldThumbprint,
            byte[] oldPublicKey,
            StatusCode closeErrorCode,
            CancellationToken ct)
        {
            var closed = new List<string>();

            foreach (TcpListenerChannel channel in m_channels.Values.ToArray())
            {
                byte[]? rawData = channel.ServerCertificate?.RawData;
                if ((rawData == null || rawData.Length == 0) &&
                    TryGetChannelId(channel, out uint channelId) &&
                    m_connectionBindings.TryGetValue(
                        channelId,
                        out QuicConnectionBinding? binding))
                {
                    rawData = binding.CertificateRawData;
                }

                if (rawData == null || rawData.Length == 0)
                {
                    continue;
                }

                using Certificate serverCertificate = Certificate.FromRawData(rawData);
                if (!CryptographicOperations.FixedTimeEquals(
                        serverCertificate.AsX509Certificate2().GetPublicKey(),
                        oldPublicKey))
                {
                    continue;
                }

                string? globalChannelId = await CloseQuicChannelAsync(
                    channel,
                    closeErrorCode,
                    ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(globalChannelId))
                {
                    closed.Add(globalChannelId!);
                }
            }

            return closed;
        }

        private async ValueTask<IReadOnlyList<string>> CloseChannelsForSupersededEpochAsync(
            long activatedEpoch,
            StatusCode statusCode,
            CancellationToken ct)
        {
            var closed = new List<string>();

            foreach (KeyValuePair<uint, TcpListenerChannel> item in m_channels.ToArray())
            {
                if (!m_connectionBindings.TryGetValue(
                        item.Key,
                        out QuicConnectionBinding? binding) ||
                    binding.ActivationEpoch >= activatedEpoch)
                {
                    continue;
                }

                string? globalChannelId = await CloseQuicChannelAsync(
                    item.Value,
                    statusCode,
                    ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(globalChannelId))
                {
                    closed.Add(globalChannelId!);
                }
            }

            return closed;
        }

        private async ValueTask<string?> CloseQuicChannelAsync(
            TcpListenerChannel channel,
            StatusCode statusCode,
            CancellationToken ct)
        {
            if (TryGetChannelId(channel, out uint channelId) &&
                m_connectionBindings.TryGetValue(channelId, out QuicConnectionBinding? binding))
            {
                await CloseConnectionAsync(binding.Connection, statusCode, ct).ConfigureAwait(false);
            }

            if (!TryGetChannelId(channel, out channelId))
            {
                return null;
            }

            string globalChannelId = channel.GlobalChannelId;
            m_connectionBindings.TryRemove(channelId, out _);
            if (m_channels.TryRemove(channelId, out TcpListenerChannel? removed))
            {
                ConnectionStatusChanged?.Invoke(
                    this,
                    new ConnectionStatusEventArgs(
                        EndpointUrl,
                        new ServiceResult(statusCode),
                        closed: true));

                removed.Dispose();
                return globalChannelId;
            }

            return null;
        }

        private bool TryGetChannelId(TcpListenerChannel channel, out uint channelId)
        {
            foreach (KeyValuePair<uint, TcpListenerChannel> item in m_channels)
            {
                if (ReferenceEquals(item.Value, channel))
                {
                    channelId = item.Key;
                    return true;
                }
            }

            channelId = 0;
            return false;
        }

        private static async ValueTask CloseConnectionAsync(
            QuicConnection connection,
            StatusCode statusCode,
            CancellationToken ct)
        {
            try
            {
                await connection.CloseAsync((long)statusCode.Code, ct).ConfigureAwait(false);
            }
            catch (QuicException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool TryTakeConnectionAdmission(
            QuicConnection connection,
            out QuicAdmissionSnapshot? admission)
        {
            RemoveExpiredPendingAdmissions();
            return m_pendingConnectionEpochs.TryRemove(connection, out admission);
        }

        private static bool SamePublicKey(X509Certificate2 left, X509Certificate2 right)
        {
            return CryptographicOperations.FixedTimeEquals(
                left.GetPublicKey(),
                right.GetPublicKey());
        }

        private static Uri ReplacePort(Uri uri, int port)
        {
            var builder = new UriBuilder(uri)
            {
                Port = port
            };
            return builder.Uri;
        }

        private uint NextChannelId()
        {
            return (uint)Interlocked.Increment(ref m_nextChannelId);
        }

        private static long TimestampAfter(TimeSpan timeout)
        {
            return Stopwatch.GetTimestamp() +
                (long)Math.Ceiling(timeout.TotalSeconds * Stopwatch.Frequency);
        }

        private static bool IsExpired(long expiresAtTimestamp, long now)
        {
            return expiresAtTimestamp <= now;
        }

        private void RemoveExpiredPendingAdmissions()
        {
            long now = Stopwatch.GetTimestamp();
            foreach (KeyValuePair<QuicConnection, QuicAdmissionSnapshot> item in
                m_pendingConnectionEpochs.ToArray())
            {
                if (IsExpired(item.Value.ExpiresAtTimestamp, now))
                {
                    ((ICollection<KeyValuePair<QuicConnection, QuicAdmissionSnapshot>>)
                        m_pendingConnectionEpochs).Remove(item);
                }
            }
        }

        private async ValueTask ExpirePendingAdmissionAsync(
            QuicConnection connection,
            QuicAdmissionSnapshot admission,
            TimeSpan handshakeTimeout)
        {
            await Task.Delay(handshakeTimeout).ConfigureAwait(false);
            ((ICollection<KeyValuePair<QuicConnection, QuicAdmissionSnapshot>>)
                m_pendingConnectionEpochs).Remove(
                    new KeyValuePair<QuicConnection, QuicAdmissionSnapshot>(
                        connection,
                        admission));
        }

        private const int MaxInboundStreams = 128;
        private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);

        internal Func<QuicConnection, long, ValueTask>? AdmissionCallbackPauseForTesting { get; set; }
        internal TimeSpan PendingAdmissionHandshakeTimeout { get; set; } = DefaultHandshakeTimeout;
        internal int PendingConnectionAdmissionCount
        {
            get
            {
                RemoveExpiredPendingAdmissions();
                return m_pendingConnectionEpochs.Count;
            }
        }

        private readonly ConcurrentDictionary<uint, TcpListenerChannel> m_channels = new();
        private readonly ConcurrentDictionary<uint, QuicConnectionBinding> m_connectionBindings = new();
        private readonly ConcurrentDictionary<QuicConnection, QuicAdmissionSnapshot>
            m_pendingConnectionEpochs = new();
        private readonly List<X509Certificate2> m_retiredTlsCertificates = [];
        private readonly Lock m_certificateActivationLock = new();
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification =
                "Disposed by CloseAsync via Interlocked.Exchange; retired " +
                "instances are moved to m_retiredTlsCertificates and disposed " +
                "from the same close path.")]
        private X509Certificate2? m_tlsCertificate;
        private readonly IBufferManagerFactory m_bufferManagerFactory;
        private readonly ILogger m_logger;
        private List<EndpointDescription>? m_descriptions;
        private ITransportListenerCallback? m_callback;
        private ICertificateRegistry? m_serverCertificates;
        private BufferManager? m_bufferManager;
        private ChannelQuotas? m_quotas;
        private QuicListener? m_listener;
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification =
                "Disposed by both Dispose and CloseAsync. CloseAsync takes the " +
                "instance with Interlocked.Exchange so a concurrent close cannot " +
                "cancel a source another thread is already disposing, which the " +
                "analyzer does not recognise as a disposal of the field.")]
        private CancellationTokenSource? m_stop;
        private Task? m_acceptLoop;
        private int m_nextChannelId;
        private long m_certificateEpoch;

        private sealed record QuicConnectionBinding(
            QuicConnection Connection,
            long ActivationEpoch,
            byte[] CertificateRawData);

        private sealed record QuicAdmissionSnapshot(
            long ActivationEpoch,
            X509Certificate2 Certificate,
            long ExpiresAtTimestamp);
    }

    /// <summary>
    /// Source-generated log messages for <see cref="QuicTransportListener"/>.
    /// </summary>
    internal static partial class QuicTransportListenerLog
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "opc.quic listener opened for {EndpointUrl} on UDP port {Port}.")]
        public static partial void QuicListenerOpened(
            this ILogger logger,
            Uri endpointUrl,
            int port);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
            Message = "opc.quic listener failed to accept a connection.")]
        public static partial void QuicAcceptFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
            Message = "opc.quic connection setup failed.")]
        public static partial void QuicConnectionSetupFailed(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
            Message = "opc.quic reverse connect to {Url} failed.")]
        public static partial void QuicReverseConnectFailed(
            this ILogger logger,
            Exception exception,
            Uri url);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
            Message = "opc.quic request {RequestId} failed.")]
        public static partial void QuicRequestFailed(
            this ILogger logger,
            Exception exception,
            uint requestId);

        [LoggerMessage(EventId = 6, Level = LogLevel.Debug,
            Message = "opc.quic peer presented no TLS certificate. The connection is accepted so the " +
                "Discovery Services remain reachable, but it cannot carry data channels.")]
        public static partial void QuicPeerCertificateMissing(this ILogger logger);

        [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
            Message = "opc.quic rejected the TLS certificate of peer {Subject}: {Reason}.")]
        public static partial void QuicPeerCertificateRejected(
            this ILogger logger,
            string subject,
            string reason);

        [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
            Message = "A response dispatch callback threw.")]
        public static partial void QuicResponseDispatchedFailed(
            this ILogger logger,
            Exception exception);
    }
}
