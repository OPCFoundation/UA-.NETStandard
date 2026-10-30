// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

#if NET8_0_OR_GREATER
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Listens for capsule-based WebTransport CONNECT sessions using public .NET QUIC APIs.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public sealed class ManagedWebTransportUascListener : IAsyncDisposable
    {
        internal ManagedWebTransportUascListener(
            QuicListener listener,
            Uri endpoint,
            Func<Uri, CancellationToken, ValueTask<bool>> connectAuthorizer,
            WebTransportOriginPolicy originPolicy,
            string? origin,
            DataChannelApplicationBudget applicationBudget)
        {
            m_listener = listener;
            EndpointUrl = endpoint;
            m_connectAuthorizer = connectAuthorizer;
            m_originPolicy = originPolicy;
            m_origin = origin;
            m_applicationBudget = applicationBudget;
        }

        /// <summary>
        /// Gets the configured OPC UA endpoint used for CONNECT authorization and UACP HEL validation.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Accepts and authorizes one CONNECT session before exposing its UACP/UASC byte transport.
        /// </summary>
        public async ValueTask<ManagedWebTransportUascConnection> AcceptAsync(
            CancellationToken cancellationToken = default)
        {
            DataChannelApplicationBudget.DataChannelCarrierReservation? reservation =
                ManagedWebTransportUascTransport.ReserveCapacity(m_applicationBudget);
            QuicConnection? connection = null;
            ManagedHttp3ServerConnectionOwner? owner = null;
            QuicStream? requestStream = null;
            Http3CapsuleSession? session = null;
            try
            {
                connection = await m_listener.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false);
                if (connection.NegotiatedApplicationProtocol != s_http3Protocol)
                {
                    throw new AuthenticationException("The peer did not negotiate the exact h3 ALPN.");
                }

                owner = new ManagedHttp3ServerConnectionOwner(connection);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(EstablishmentTimeout);
                await owner.StartAsync(deadline.Token).ConfigureAwait(false);
                requestStream = await owner.AcceptRequestStreamAsync(deadline.Token).ConfigureAwait(false);
                Http3ExtendedConnectRequest request = await Http3CapsuleSession.ReadConnectRequestAsync(
                    requestStream, MaximumHeaderBytes, deadline.Token).ConfigureAwait(false);
                Uri httpsEndpoint = EndpointUrl;
                if (!string.Equals(request.Authority, httpsEndpoint.Authority, StringComparison.Ordinal) ||
                    !string.Equals(request.Path, httpsEndpoint.PathAndQuery, StringComparison.Ordinal))
                {
                    await Http3CapsuleSession.WriteConnectFailureAsync(
                        requestStream, 404, MaximumHeaderBytes, deadline.Token).ConfigureAwait(false);
                    throw new AuthenticationException("The WebTransport CONNECT target does not match this endpoint.");
                }
                if (!IsOriginAllowed(request.Origin, originPolicy: m_originPolicy, origin: m_origin))
                {
                    await Http3CapsuleSession.WriteConnectFailureAsync(
                        requestStream, 403, MaximumHeaderBytes, deadline.Token).ConfigureAwait(false);
                    throw new AuthenticationException("The WebTransport CONNECT Origin was not authorized.");
                }
                if (!await m_connectAuthorizer(httpsEndpoint, deadline.Token).ConfigureAwait(false))
                {
                    await Http3CapsuleSession.WriteConnectFailureAsync(
                        requestStream, 403, MaximumHeaderBytes, deadline.Token).ConfigureAwait(false);
                    throw new AuthenticationException("The WebTransport CONNECT request was not authorized.");
                }

                session = await Http3CapsuleSession.WriteConnectResponseAsync(
                    requestStream,
                    MaximumHeaderBytes,
                    MaximumFramePayloadBytes,
                    MaximumCapsulePayloadBytes,
                    deadline.Token,
                    owner).ConfigureAwait(false);
                owner = null;
                requestStream = null;
                WebTransportCapsuleByteStream stream = await WebTransportCapsuleByteStream.CreateAsync(
                    session, MaximumCapsulePayloadBytes, InitialReceiveWindowBytes, deadline.Token)
                    .ConfigureAwait(false);
                var accepted = new ManagedWebTransportUascConnection(
                    session, stream, EndpointUrl, isInitiator: false, reservation);
                reservation = null;
                return accepted;
            }
            catch
            {
                if (requestStream != null)
                {
                    await requestStream.DisposeAsync().ConfigureAwait(false);
                }
                if (session != null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                else if (owner == null && connection != null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                try
                {
                    if (owner != null)
                    {
                        await owner.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    reservation?.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_listener.DisposeAsync();
        }

        private const int MaximumHeaderBytes = 65536;
        private const int MaximumFramePayloadBytes = 1048576;
        private const int MaximumCapsulePayloadBytes = 1048576;
        private const int InitialReceiveWindowBytes = 1048576;
        private static readonly TimeSpan EstablishmentTimeout = TimeSpan.FromSeconds(15);
        private static readonly SslApplicationProtocol s_http3Protocol = new("h3");

        private readonly QuicListener m_listener;
        private readonly Func<Uri, CancellationToken, ValueTask<bool>> m_connectAuthorizer;
        private readonly WebTransportOriginPolicy m_originPolicy;
        private readonly string? m_origin;

        /// <summary>Application-wide capacity shared with Core flow admission.</summary>
        private readonly DataChannelApplicationBudget m_applicationBudget;

        private static bool IsOriginAllowed(
            string? requestOrigin,
            WebTransportOriginPolicy originPolicy,
            string? origin)
        {
            return originPolicy switch
            {
                WebTransportOriginPolicy.AbsentOnly => requestOrigin == null,
                WebTransportOriginPolicy.AbsentOrExact =>
                    requestOrigin == null || string.Equals(requestOrigin, origin, StringComparison.Ordinal),
                WebTransportOriginPolicy.RequiredExact =>
                    string.Equals(requestOrigin, origin, StringComparison.Ordinal),
                _ => false
            };
        }
    }

    /// <summary>
    /// Creates managed WebTransport listeners with explicit TLS and CONNECT authorization policy.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public sealed class ManagedWebTransportUascServer
    {
        /// <summary>
        /// Creates a server with its own accounting provider.
        /// </summary>
        public ManagedWebTransportUascServer()
            : this(new DataChannelApplicationBudget())
        {
        }

        /// <summary>
        /// Creates a server sharing the application's carrier and flow budget.
        /// </summary>
        public ManagedWebTransportUascServer(DataChannelApplicationBudget applicationBudget)
        {
            ArgumentNullException.ThrowIfNull(applicationBudget);
            m_applicationBudget = applicationBudget;
        }

        /// <summary>
        /// Binds an OPC UA endpoint to a local UDP address and authorizes each accepted CONNECT request.
        /// </summary>
        public async ValueTask<ManagedWebTransportUascListener> ListenAsync(
            Uri endpoint,
            IPEndPoint localEndPoint,
            SslServerAuthenticationOptions tls,
            Func<Uri, CancellationToken, ValueTask<bool>> connectAuthorizer,
            WebTransportOriginPolicy originPolicy = WebTransportOriginPolicy.AbsentOnly,
            string? origin = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(localEndPoint);
            ArgumentNullException.ThrowIfNull(tls);
            ArgumentNullException.ThrowIfNull(connectAuthorizer);
            WebTransportEndpoint.HttpsAddress(endpoint);
            if (!Enum.IsDefined(originPolicy) ||
                (originPolicy == WebTransportOriginPolicy.AbsentOnly && origin != null) ||
                (originPolicy != WebTransportOriginPolicy.AbsentOnly &&
                    !WebTransportEndpoint.IsCanonicalOrigin(origin)))
            {
                throw new ArgumentException("The WebTransport Origin policy is invalid.", nameof(origin));
            }
            ValidateTls(tls);
            if (!QuicListener.IsSupported)
            {
                throw new PlatformNotSupportedException("Managed QUIC is unavailable on this platform.");
            }

            QuicListener listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = localEndPoint,
                ListenBacklog = 16,
                ApplicationProtocols = [s_http3Protocol],
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = tls,
                    MaxInboundBidirectionalStreams = 1,
                    MaxInboundUnidirectionalStreams = MaximumPeerUnidirectionalStreams,
                    DefaultCloseErrorCode = Http3ErrorCode,
                    DefaultStreamErrorCode = Http3ErrorCode,
#if NET9_0_OR_GREATER
                    HandshakeTimeout = TimeSpan.FromSeconds(15),
#endif
                    IdleTimeout = s_idleTimeout
                })
            }, cancellationToken).ConfigureAwait(false);
            try
            {
                Uri boundEndpoint = WebTransportEndpoint.WithBoundPort(
                    endpoint, listener.LocalEndPoint.ToString() ?? throw new InvalidOperationException(
                        "The managed QUIC listener did not report its bound address."));
                return new ManagedWebTransportUascListener(
                    listener, boundEndpoint, connectAuthorizer, originPolicy, origin, m_applicationBudget);
            }
            catch
            {
                await listener.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static void ValidateTls(SslServerAuthenticationOptions tls)
        {
            if (tls.ApplicationProtocols is not { Count: 1 } ||
                tls.ApplicationProtocols[0] != s_http3Protocol ||
                tls.EnabledSslProtocols != SslProtocols.None)
            {
                throw new ArgumentException(
                    "HTTP/3 requires the exact h3 ALPN and operating-system-selected QUIC TLS.",
                    nameof(tls));
            }
        }

        private const long Http3ErrorCode = 0x102;
        private const int MaximumPeerUnidirectionalStreams = 72;
        private static readonly TimeSpan s_idleTimeout = TimeSpan.FromMinutes(1);
        private static readonly SslApplicationProtocol s_http3Protocol = new("h3");

        /// <summary>Application-wide accounting inherited by each listener.</summary>
        private readonly DataChannelApplicationBudget m_applicationBudget;
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    internal sealed class ManagedHttp3ServerConnectionOwner(QuicConnection connection) : IAsyncDisposable
    {
        internal async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            m_controlStream = await OpenCriticalStreamAsync(0, cancellationToken).ConfigureAwait(false);
            m_encoderStream = await OpenCriticalStreamAsync(2, cancellationToken).ConfigureAwait(false);
            m_decoderStream = await OpenCriticalStreamAsync(3, cancellationToken).ConfigureAwait(false);
            await WriteSettingsAsync(m_controlStream, cancellationToken).ConfigureAwait(false);
            m_acceptTask = AcceptPeerStreamsAsync(m_lifetime.Token);
            await m_ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        internal ValueTask<QuicStream> AcceptRequestStreamAsync(CancellationToken cancellationToken)
        {
            return m_requests.Reader.ReadAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Task? shutdownTask;
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    return;
                }
                m_disposed = 1;
                shutdownTask = m_connectionShutdownTask;
            }

            m_lifetime.Cancel();
            m_requests.Writer.TryComplete();
            await (shutdownTask ?? connection.DisposeAsync().AsTask()).ConfigureAwait(false);
            if (m_acceptTask != null)
            {
                try
                {
                    await m_acceptTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (m_lifetime.IsCancellationRequested)
                {
                }
            }
            Task[] peerTasks;
            lock (m_gate)
            {
                peerTasks = [.. m_peerTasks];
            }
            await Task.WhenAll(peerTasks).ConfigureAwait(false);
            foreach (QuicStream? stream in m_peerCriticalStreams)
            {
                if (stream != null)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
            if (m_controlStream != null)
            {
                await m_controlStream.DisposeAsync().ConfigureAwait(false);
            }
            if (m_encoderStream != null)
            {
                await m_encoderStream.DisposeAsync().ConfigureAwait(false);
            }
            if (m_decoderStream != null)
            {
                await m_decoderStream.DisposeAsync().ConfigureAwait(false);
            }
            m_lifetime.Dispose();
        }

        private async ValueTask<QuicStream> OpenCriticalStreamAsync(
            ulong streamType,
            CancellationToken cancellationToken)
        {
            QuicStream stream = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Unidirectional, cancellationToken).ConfigureAwait(false);
            byte[] prefix = new byte[8];
            if (!Http3VarInt.TryWrite(prefix, streamType, out int prefixLength))
            {
                throw new InvalidOperationException("The HTTP/3 critical stream type could not be encoded.");
            }
            await stream.WriteAsync(prefix.AsMemory(0, prefixLength), cancellationToken).ConfigureAwait(false);
            return stream;
        }

        private static async ValueTask WriteSettingsAsync(
            QuicStream controlStream,
            CancellationToken cancellationToken)
        {
            byte[] payload = new byte[8];
            if (!Http3Settings.TryWriteLocalPayload(
                Http3EndpointRole.Server, payload, out int payloadLength))
            {
                throw new InvalidOperationException("The local HTTP/3 SETTINGS payload could not be encoded.");
            }
            await Http3StreamCodec.WriteFrameAsync(
                controlStream, 0x4, payload.AsMemory(0, payloadLength), cancellationToken).ConfigureAwait(false);
        }

        private async Task AcceptPeerStreamsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    QuicStream stream = await connection.AcceptInboundStreamAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (stream.Type == QuicStreamType.Bidirectional)
                    {
                        if (stream.Id != 0 || !m_requests.Writer.TryWrite(stream))
                        {
                            stream.Abort(QuicAbortDirection.Both, Http3ErrorCode);
                            await stream.DisposeAsync().ConfigureAwait(false);
                            throw new InvalidDataException(
                                "Only the client's first bidirectional stream may carry the CONNECT request.");
                        }
                        continue;
                    }
                    await DispatchPeerStreamAsync(stream, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                FailConnection(exception);
            }
        }

        private async ValueTask DispatchPeerStreamAsync(QuicStream stream, CancellationToken cancellationToken)
        {
            if (stream.Type != QuicStreamType.Unidirectional)
            {
                throw new InvalidDataException("A peer stream has an unsupported HTTP/3 type.");
            }
            ulong streamType = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
            Http3CriticalStreamResult result;
            lock (m_gate)
            {
                result = m_registry.Register(checked((ulong)stream.Id), streamType);
            }
            switch (result)
            {
                case Http3CriticalStreamResult.CriticalStreamRegistered:
                    if (streamType == 0)
                    {
                        m_peerCriticalStreams[0] = stream;
                        TrackPeerTask(ReadControlStreamAsync(stream, cancellationToken));
                    }
                    else if (streamType == 2)
                    {
                        m_peerCriticalStreams[1] = stream;
                        TrackPeerTask(MonitorEmptyQpackStreamAsync(stream, cancellationToken));
                    }
                    else
                    {
                        m_peerCriticalStreams[2] = stream;
                        TrackPeerTask(MonitorEmptyQpackStreamAsync(stream, cancellationToken));
                    }
                    TryCompleteReady();
                    return;
                case Http3CriticalStreamResult.IgnoredExtensionStream:
                    TrackPeerTask(DrainExtensionStreamAsync(stream, cancellationToken));
                    return;
                case Http3CriticalStreamResult.UnsupportedPushStream:
                    stream.Abort(QuicAbortDirection.Both, Http3ErrorCode);
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw new InvalidDataException("HTTP/3 server push is not supported.");
                default:
                    stream.Abort(QuicAbortDirection.Both, Http3ErrorCode);
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw new InvalidDataException("The peer created an invalid or duplicate critical stream.");
            }
        }

        private async Task ReadControlStreamAsync(QuicStream stream, CancellationToken cancellationToken)
        {
            try
            {
                var state = new Http3ControlStreamState(Http3EndpointRole.Server);
                while (true)
                {
                    Http3Frame? frame = await Http3StreamCodec.ReadFrameAsync(
                        stream, MaximumControlFrameBytes, cancellationToken).ConfigureAwait(false) ??
                        throw new EndOfStreamException("The peer closed its HTTP/3 control stream.");
                    Http3ControlStreamFrameResult result = state.AcceptFrame(
                        frame.Value.Type, frame.Value.Payload);
                    if (result != Http3ControlStreamFrameResult.Accepted)
                    {
                        throw new InvalidDataException("The peer sent an invalid HTTP/3 control-stream frame.");
                    }
                    if (state.HasPeerSettings)
                    {
                        Interlocked.Exchange(ref m_peerSettingsReceived, 1);
                        TryCompleteReady();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                FailConnection(exception);
            }
        }

        private async Task MonitorEmptyQpackStreamAsync(QuicStream stream, CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[1];
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read != 0)
                {
                    throw new InvalidDataException(
                        "QPACK instructions are forbidden because this connection advertises zero capacity.");
                }
                throw new EndOfStreamException("The peer closed an HTTP/3 QPACK critical stream.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                FailConnection(exception);
            }
        }

        private async Task DrainExtensionStreamAsync(QuicStream stream, CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[ExtensionDiscardBufferBytes];
                int totalBytes = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }
                    totalBytes = checked(totalBytes + read);
                    if (totalBytes > MaximumExtensionDiscardBytes)
                    {
                        stream.Abort(QuicAbortDirection.Read, Http3ErrorCode);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                FailConnection(exception);
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async ValueTask<ulong> ReadVarIntAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            byte[] encoded = new byte[8];
            int firstByte = await stream.ReadAsync(encoded.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (firstByte == 0)
            {
                throw new EndOfStreamException("An HTTP/3 unidirectional stream has no type.");
            }
            int length = 1 << (encoded[0] >> 6);
            if (length > 1)
            {
                await stream.ReadExactlyAsync(encoded.AsMemory(1, length - 1), cancellationToken)
                    .ConfigureAwait(false);
            }
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(encoded, 0, length));
            if (!Http3VarInt.TryRead(ref reader, out ulong value))
            {
                throw new InvalidDataException("The HTTP/3 unidirectional stream type is malformed.");
            }
            return value;
        }

        private void TrackPeerTask(Task task)
        {
            lock (m_gate)
            {
                m_peerTasks.Add(task);
            }
        }

        private void FailConnection(Exception exception)
        {
            lock (m_gate)
            {
                if (m_disposed != 0)
                {
                    return;
                }
                m_ready.TrySetException(exception);
                m_requests.Writer.TryComplete(exception);
                m_connectionShutdownTask ??= connection.DisposeAsync().AsTask();
            }
            m_lifetime.Cancel();
        }

        private void TryCompleteReady()
        {
            lock (m_gate)
            {
                if (Volatile.Read(ref m_peerSettingsReceived) != 0 && m_registry.HasAllCriticalStreams)
                {
                    m_ready.TrySetResult(true);
                }
            }
        }

        private const long Http3ErrorCode = 0x102;
        private const int MaximumControlFrameBytes = 65536;
        private const int ExtensionDiscardBufferBytes = 4096;
        private const int MaximumExtensionDiscardBytes = 65536;
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly TaskCompletionSource<bool> m_ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Channel<QuicStream> m_requests = Channel.CreateBounded<QuicStream>(
            new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true });
        private readonly Http3CriticalStreamRegistry m_registry = new(Http3EndpointRole.Server);
        private readonly Lock m_gate = new();
        private readonly List<Task> m_peerTasks = [];
        private readonly QuicStream?[] m_peerCriticalStreams = new QuicStream[3];
        private QuicStream? m_controlStream;
        private QuicStream? m_encoderStream;
        private QuicStream? m_decoderStream;
        private Task? m_acceptTask;
        private Task? m_connectionShutdownTask;
        private int m_peerSettingsReceived;
        private int m_disposed;
    }
}
#endif
