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
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
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

            int port = baseAddress.Port > 0
                ? baseAddress.Port
                : DataChannelConstants.QuicDefaultPort;

            // The Server's TLS certificate shall be the Server's
            // Application Instance Certificate, or shall carry the same
            // subjectPublicKeyInfo, because that is what the key equality
            // check of Part 6 errata 7.6.1 compares against.
            X509Certificate2 tlsCertificate = ResolveTlsCertificate();
            m_tlsCertificate = tlsCertificate;

            var listenerOptions = new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, port),
                ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                    new QuicServerConnectionOptions
                    {
                        DefaultStreamErrorCode = 0x0A,
                        DefaultCloseErrorCode = 0x0B,
                        MaxInboundBidirectionalStreams = MaxInboundStreams,
                        MaxInboundUnidirectionalStreams = MaxInboundStreams,
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                            // Read the field rather than a captured local so a
                            // rotation performed by CertificateUpdate reaches
                            // every subsequent connection. Presenting a retired
                            // certificate over TLS while the UASC layer has
                            // moved to the new one would break the key equality
                            // check of Part 6 errata 7.6.1.
                            ServerCertificate = m_tlsCertificate,
                            ClientCertificateRequired = false
                        }
                    })
            };

            m_listener = await QuicListener
                .ListenAsync(listenerOptions, ct)
                .ConfigureAwait(false);

            m_stop = new CancellationTokenSource();
            m_acceptLoop = Task.Run(() => RunAcceptLoopAsync(m_stop.Token), CancellationToken.None);

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
            X509Certificate2? retired = Interlocked.Exchange(ref m_tlsCertificate, rotated);

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
            if (oldCertificate == null)
            {
                throw new ArgumentNullException(nameof(oldCertificate));
            }

            string thumbprint = oldCertificate.Thumbprint;

            if (string.IsNullOrEmpty(thumbprint))
            {
                return new ValueTask<IReadOnlyList<string>>([]);
            }

            var closed = new List<string>();

            foreach (TcpListenerChannel channel in m_channels.Values.ToArray())
            {
                if (channel.TryCloseForCertificateRotation(thumbprint, out string? globalChannelId) &&
                    !string.IsNullOrEmpty(globalChannelId))
                {
                    closed.Add(globalChannelId!);
                }
            }

            return new ValueTask<IReadOnlyList<string>>(closed);
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

                if (channel.CloseForUntrustedPeerCertificate(out string? globalChannelId) &&
                    !string.IsNullOrEmpty(globalChannelId))
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

            try
            {
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

                uint channelId = NextChannelId();

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

                // Ownership passes to the channel, which starts the
                // receive loop on the control stream.
                channel.Attach(channelId, transport);
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
                    channel.Attach(channelId, transport);
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
            }
#pragma warning disable CA1031 // A failed request must not fault the listener.
            catch (Exception e)
#pragma warning restore CA1031
            {
                m_logger.QuicRequestFailed(e, requestId);
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

        private uint NextChannelId()
        {
            return (uint)Interlocked.Increment(ref m_nextChannelId);
        }

        private const int MaxInboundStreams = 128;

        private readonly ConcurrentDictionary<uint, TcpListenerChannel> m_channels = new();
        private readonly List<X509Certificate2> m_retiredTlsCertificates = [];
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
    }
}
