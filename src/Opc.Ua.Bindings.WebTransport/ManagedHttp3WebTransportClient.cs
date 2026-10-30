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
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Establishes a capsule-based WebTransport HTTP/3 session over the public .NET QUIC API.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    internal static class ManagedHttp3WebTransportClient
    {
        /// <summary>
        /// Establishes one dedicated HTTP/3 connection and its exact WebTransport CONNECT session.
        /// </summary>
        internal static async ValueTask<Http3CapsuleSession> ConnectAsync(
            Uri endpoint,
            SslClientAuthenticationOptions tls,
            string? origin,
            int maximumHeaderBytes,
            int maximumFramePayloadBytes,
            int maximumCapsulePayloadBytes,
            TimeSpan establishmentTimeout,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(tls);
            WebTransportEndpoint.HttpsAddress(endpoint);
            if (endpoint.Port == 0)
            {
                throw new ArgumentException("An explicit HTTPS port is required.", nameof(endpoint));
            }
            ValidateTls(endpoint, tls);
            ValidateLimits(
                maximumHeaderBytes, maximumFramePayloadBytes, maximumCapsulePayloadBytes, establishmentTimeout);
            if (!QuicConnection.IsSupported)
            {
                throw new PlatformNotSupportedException("Managed QUIC is unavailable on this platform.");
            }

            Uri httpsEndpoint = endpoint;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(establishmentTimeout);
            QuicConnection connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
            {
                RemoteEndPoint = new DnsEndPoint(httpsEndpoint.DnsSafeHost, httpsEndpoint.Port),
                ClientAuthenticationOptions = tls,
                MaxInboundBidirectionalStreams = 0,
                MaxInboundUnidirectionalStreams = MaximumPeerUnidirectionalStreams,
                DefaultCloseErrorCode = Http3ErrorCode,
                DefaultStreamErrorCode = Http3ErrorCode,
                IdleTimeout = s_idleTimeout
            }, deadline.Token).ConfigureAwait(false);

            ManagedHttp3ConnectionOwner? owner = new(connection);
            try
            {
                await owner.StartAsync(deadline.Token).ConfigureAwait(false);
                QuicStream requestStream = await connection.OpenOutboundStreamAsync(
                    QuicStreamType.Bidirectional, deadline.Token).ConfigureAwait(false);
                await Http3CapsuleSession.WriteConnectRequestAsync(
                    requestStream,
                    httpsEndpoint.Authority,
                    httpsEndpoint.PathAndQuery,
                    origin,
                    maximumHeaderBytes,
                    deadline.Token).ConfigureAwait(false);
                Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                    requestStream,
                    maximumHeaderBytes,
                    maximumFramePayloadBytes,
                    maximumCapsulePayloadBytes,
                    deadline.Token,
                    owner).ConfigureAwait(false);
                owner = null;
                return session;
            }
            finally
            {
                if (owner != null)
                {
                    await owner.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static void ValidateTls(Uri endpoint, SslClientAuthenticationOptions tls)
        {
            if (tls.ApplicationProtocols is not { Count: 1 } ||
                tls.ApplicationProtocols[0] != s_http3Protocol ||
                tls.EnabledSslProtocols != SslProtocols.None ||
                !string.Equals(tls.TargetHost, endpoint.DnsSafeHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "HTTP/3 requires the exact h3 ALPN, operating-system-selected TLS, and endpoint TargetHost.",
                    nameof(tls));
            }
        }

        private static void ValidateLimits(
            int maximumHeaderBytes,
            int maximumFramePayloadBytes,
            int maximumCapsulePayloadBytes,
            TimeSpan establishmentTimeout)
        {
            if (maximumHeaderBytes is < 1 or > MaximumHeaderBytes ||
                maximumFramePayloadBytes is < 1 or > MaximumFramePayloadBytes ||
                maximumCapsulePayloadBytes is < 1 or > MaximumCapsulePayloadBytes ||
                establishmentTimeout.TotalMilliseconds is < 1 or > 60000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumHeaderBytes), "HTTP/3 limits and establishment timeout must be finite.");
            }
        }

        private const long Http3ErrorCode = 0x102;
        private const int MaximumHeaderBytes = 65536;
        private const int MaximumFramePayloadBytes = 1048576;
        private const int MaximumCapsulePayloadBytes = 1048576;
        private const int MaximumPeerUnidirectionalStreams = 72;
        private static readonly TimeSpan s_idleTimeout = TimeSpan.FromMinutes(1);
        private static readonly SslApplicationProtocol s_http3Protocol = new("h3");

        private sealed class ManagedHttp3ConnectionOwner(QuicConnection connection) : IAsyncDisposable
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
                    peerTasks = m_peerTasks.ToArray();
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
                    Http3EndpointRole.Client, payload, out int payloadLength))
                {
                    throw new InvalidOperationException("The local HTTP/3 SETTINGS payload could not be encoded.");
                }
                await Http3StreamCodec.WriteFrameAsync(
                    controlStream, 0x4, payload.AsMemory(0, payloadLength), cancellationToken)
                    .ConfigureAwait(false);
            }

            private async Task AcceptPeerStreamsAsync(CancellationToken cancellationToken)
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        QuicStream stream = await connection.AcceptInboundStreamAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (stream.Type != QuicStreamType.Unidirectional)
                        {
                            stream.Abort(QuicAbortDirection.Both, Http3ErrorCode);
                            await stream.DisposeAsync().ConfigureAwait(false);
                            throw new InvalidDataException("A server-initiated HTTP/3 bidirectional stream is invalid.");
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

            private async ValueTask DispatchPeerStreamAsync(
                QuicStream stream,
                CancellationToken cancellationToken)
            {
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
                    var state = new Http3ControlStreamState(Http3EndpointRole.Client);
                    while (true)
                    {
                        Http3Frame? frame = await Http3StreamCodec.ReadFrameAsync(
                            stream, MaximumControlFrameBytes, cancellationToken).ConfigureAwait(false);
                        if (frame == null)
                        {
                            throw new EndOfStreamException("The peer closed its HTTP/3 control stream.");
                        }

                        Http3ControlStreamFrameResult result = state.AcceptFrame(
                            frame.Value.Type, frame.Value.Payload);
                        if (result != Http3ControlStreamFrameResult.Accepted)
                        {
                            throw new InvalidDataException("The peer sent an invalid HTTP/3 control-stream frame.");
                        }
                        if (state.HasPeerSettings)
                        {
                            if (!state.PeerSettings.PeerEnablesConnectProtocol)
                            {
                                throw new AuthenticationException(
                                    "The peer did not enable Extended CONNECT for WebTransport.");
                            }
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
                    try
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        FailConnection(exception);
                    }
                }
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
                    m_connectionShutdownTask ??= connection.DisposeAsync().AsTask();
                }
                m_lifetime.Cancel();
            }

            private static async ValueTask<ulong> ReadVarIntAsync(
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

            private const int MaximumControlFrameBytes = 65536;
            private const int ExtensionDiscardBufferBytes = 4096;
            private const int MaximumExtensionDiscardBytes = 65536;
            private readonly CancellationTokenSource m_lifetime = new();
            private readonly TaskCompletionSource<bool> m_ready =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Http3CriticalStreamRegistry m_registry = new(Http3EndpointRole.Client);
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
}
#endif
