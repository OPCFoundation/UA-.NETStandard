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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Stress.Tests.Channels.Fakes
{
    /// <summary>
    /// TCP forwarding proxy for channel-manager stress tests.
    /// </summary>
    public sealed class TcpChaosProxy : IAsyncDisposable
    {
        private TcpChaosProxy(Uri upstreamUrl, int localPort, ITelemetryContext? telemetry)
        {
            UpstreamUrl = upstreamUrl;
            m_logger = telemetry.CreateLogger<TcpChaosProxy>();
            m_listener = StartListener(localPort);
            m_localPort = ((IPEndPoint)m_listener.LocalEndpoint).Port;
            LocalUrl = new UriBuilder(Utils.UriSchemeOpcTcp, "localhost", m_localPort).Uri;
            m_acceptTask = AcceptLoopAsync();
        }

        /// <summary>
        /// Local URL clients should connect to (opc.tcp://localhost:{port}).
        /// </summary>
        public Uri LocalUrl { get; }

        /// <summary>
        /// Upstream URL the proxy forwards to (the real server).
        /// </summary>
        public Uri UpstreamUrl { get; }

        /// <summary>
        /// Number of currently-connected client sockets.
        /// </summary>
        public int ActiveConnections => m_connections.Count;

        /// <summary>
        /// Total bytes forwarded (cumulative both directions).
        /// </summary>
        public long BytesForwarded => Interlocked.Read(ref m_bytesForwarded);

        /// <summary>
        /// Total client connections accepted since start.
        /// </summary>
        public long TotalAccepted => Interlocked.Read(ref m_totalAccepted);

        /// <summary>
        /// Whether the proxy is currently in "accept blocked" mode.
        /// </summary>
        public bool AcceptBlocked => Volatile.Read(ref m_acceptBlocked) != 0;

        /// <summary>
        /// Drain reads in both directions while keeping each connection open.
        /// </summary>
        public bool StallForwarding
        {
            get => Volatile.Read(ref m_stallForwarding) != 0;
            set => Volatile.Write(ref m_stallForwarding, value ? 1 : 0);
        }

        /// <summary>
        /// Configure client-to-server throttling in bytes/sec; 0 = unlimited.
        /// </summary>
        public int ClientToServerBytesPerSec
        {
            get => Volatile.Read(ref m_clientToServerBytesPerSec);
            set => SetNonNegative(ref m_clientToServerBytesPerSec, value);
        }

        /// <summary>
        /// Configure server-to-client throttling in bytes/sec; 0 = unlimited.
        /// </summary>
        public int ServerToClientBytesPerSec
        {
            get => Volatile.Read(ref m_serverToClientBytesPerSec);
            set => SetNonNegative(ref m_serverToClientBytesPerSec, value);
        }

        /// <summary>
        /// Start listening; returns when the proxy is ready to accept.
        /// </summary>
        /// <param name="upstreamUrl">opc.tcp:// URL of the real server.</param>
        /// <param name="localPort">0 to auto-pick.</param>
        /// <param name="telemetry">Optional telemetry context.</param>
        /// <returns>The started proxy.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="upstreamUrl"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static Task<TcpChaosProxy> StartAsync(
            Uri upstreamUrl,
            int localPort = 0,
            ITelemetryContext? telemetry = null)
        {
            if (upstreamUrl == null)
            {
                throw new ArgumentNullException(nameof(upstreamUrl));
            }

            if (upstreamUrl.Port <= 0)
            {
                throw new ArgumentException("The upstream URL must include a TCP port.", nameof(upstreamUrl));
            }

            if (localPort is < 0 or > IPEndPoint.MaxPort)
            {
                throw new ArgumentOutOfRangeException(nameof(localPort));
            }

            return Task.FromResult(new TcpChaosProxy(upstreamUrl, localPort, telemetry));
        }

        /// <summary>
        /// Disconnect every currently-established client connection abruptly.
        /// </summary>
        /// <returns>A task that completes when current connection tasks have observed the drop.</returns>
        public async Task DropAllConnectionsAsync()
        {
            var tasks = new List<Task>();
            foreach (ConnectionState connection in m_connections.Values)
            {
                if (connection.RunTask != null)
                {
                    tasks.Add(connection.RunTask);
                }

                connection.Abort();
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stop accepting new connections for the supplied duration.
        /// </summary>
        /// <param name="duration">The duration to block accepts.</param>
        /// <returns>A task that completes when accepting is unblocked.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public Task BlockAcceptAsync(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            ThrowIfDisposed();
            if (duration == TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            long version = Interlocked.Increment(ref m_acceptBlockVersion);
            Volatile.Write(ref m_acceptBlocked, 1);
            DrainAcceptSignal();
            StopCurrentListener();
            return UnblockAcceptAsync(version, duration);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            await m_cts.CancelAsync().ConfigureAwait(false);
            Volatile.Write(ref m_acceptBlocked, 0);
            StopCurrentListener();
            SignalAcceptLoop();
            await DropAllConnectionsAsync().ConfigureAwait(false);
            await IgnoreExpectedAsync(m_acceptTask).ConfigureAwait(false);
            m_acceptSignal.Dispose();
            m_cts.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            CancellationToken ct = m_cts.Token;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await WaitForAcceptAsync(ct).ConfigureAwait(false);
#pragma warning disable CA2000 // ConnectionState owns the accepted socket lifetime.
                    TcpClient client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
#pragma warning restore CA2000
                    client.NoDelay = true;
                    long id = Interlocked.Increment(ref m_nextConnectionId);
                    var connection = new ConnectionState(id, client);
                    Interlocked.Increment(ref m_totalAccepted);
                    m_connections[id] = connection;
                    connection.RunTask = RunConnectionAsync(connection);
                }
                catch (Exception ex) when (IsExpectedAcceptStop(ex, ct))
                {
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    m_logger.LogWarning(ex, "TCP chaos proxy accept loop failed.");
                    await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
                }
            }
        }

        private async Task RunConnectionAsync(ConnectionState connection)
        {
            try
            {
#pragma warning disable CA2000 // ConnectionState owns and aborts the upstream socket.
                var upstream = new TcpClient { NoDelay = true };
#pragma warning restore CA2000
                connection.Upstream = upstream;
                await upstream.ConnectAsync(UpstreamUrl.DnsSafeHost, UpstreamUrl.Port).ConfigureAwait(false);
                Task clientToServer = ForwardAsync(connection, connection.Client, upstream, true, m_cts.Token);
                Task serverToClient = ForwardAsync(connection, upstream, connection.Client, false, m_cts.Token);
                await Task.WhenAny(clientToServer, serverToClient).ConfigureAwait(false);
                connection.Abort();
                await IgnoreExpectedAsync(Task.WhenAll(clientToServer, serverToClient)).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                (m_cts.IsCancellationRequested || connection.IsClosed) &&
                IsExpectedSocketClose(ex))
            {
                // Expected ONLY while the connection is being torn down
                // (DropAllConnections aborts the sockets, or the proxy is
                // disposed): an in-flight upstream ConnectAsync or a forward
                // loop then throws OperationAborted / a socket close, which we
                // swallow so the awaited RunTask completes instead of faulting
                // the test. A socket/IO failure that occurs while the proxy is
                // still live and this connection was not aborted (e.g. a real
                // upstream "connection refused") is NOT swallowed here - it
                // falls through to the log branch below so it stays diagnosable.
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "TCP chaos proxy connection {ConnectionId} closed.", connection.Id);
            }
            finally
            {
                m_connections.TryRemove(connection.Id, out _);
                connection.Abort();
            }
        }

        private async Task ForwardAsync(
            ConnectionState connection,
            TcpClient source,
            TcpClient destination,
            bool clientToServer,
            CancellationToken ct)
        {
            byte[] buffer = new byte[BufferSize];
            NetworkStream sourceStream = source.GetStream();
            NetworkStream destinationStream = destination.GetStream();
            while (!ct.IsCancellationRequested && !connection.IsClosed)
            {
                int read = await ReadAsync(sourceStream, buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (StallForwarding)
                {
                    continue;
                }

                int limit = clientToServer ? ClientToServerBytesPerSec : ServerToClientBytesPerSec;
                await ThrottleAsync(read, limit, ct).ConfigureAwait(false);
                await WriteAsync(destinationStream, buffer, 0, read, ct).ConfigureAwait(false);
                Interlocked.Add(ref m_bytesForwarded, read);
            }
        }

        private async Task WaitForAcceptAsync(CancellationToken ct)
        {
            while (AcceptBlocked && !ct.IsCancellationRequested)
            {
                await m_acceptSignal.WaitAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task UnblockAcceptAsync(long version, TimeSpan duration)
        {
            try
            {
                await Task.Delay(duration, m_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (Interlocked.Read(ref m_acceptBlockVersion) == version && !IsDisposed)
            {
                m_listener = StartListener(m_localPort);
                Volatile.Write(ref m_acceptBlocked, 0);
                SignalAcceptLoop();
            }
        }

        private void StopCurrentListener()
        {
            TcpListener listener = m_listener;
            try
            {
                listener.Stop();
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
#if NETCOREAPP
            listener.Dispose();
#endif
        }

        private void SignalAcceptLoop()
        {
            try
            {
                m_acceptSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void DrainAcceptSignal()
        {
            while (m_acceptSignal.Wait(0))
            {
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(TcpChaosProxy));
            }
        }

        private static async Task IgnoreExpectedAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex) when (IsExpectedSocketClose(ex))
            {
            }
        }

        private static async Task ThrottleAsync(int byteCount, int bytesPerSecond, CancellationToken ct)
        {
            if (bytesPerSecond > 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Ceiling(byteCount * 1000d / bytesPerSecond)),
                    ct).ConfigureAwait(false);
            }
        }

        private static TcpListener StartListener(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return listener;
        }

        private static ValueTask<int> ReadAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
            return stream.ReadAsync(buffer.AsMemory(offset, count), ct);
#else
            return new ValueTask<int>(stream.ReadAsync(buffer, offset, count, ct));
#endif
        }

        private static ValueTask WriteAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
            return stream.WriteAsync(buffer.AsMemory(offset, count), ct);
#else
            return new ValueTask(stream.WriteAsync(buffer, offset, count, ct));
#endif
        }

        private static void SetNonNegative(ref int field, int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Volatile.Write(ref field, value);
        }

        private static bool IsExpectedAcceptStop(Exception ex, CancellationToken ct)
        {
            return ct.IsCancellationRequested ||
                ex is ObjectDisposedException ||
                ex is InvalidOperationException ||
                (ex is SocketException socketException &&
                    (socketException.SocketErrorCode == SocketError.Interrupted ||
                        socketException.SocketErrorCode == SocketError.OperationAborted ||
                        socketException.SocketErrorCode == SocketError.InvalidArgument));
        }

        private static bool IsExpectedSocketClose(Exception ex)
        {
            return ex is IOException or ObjectDisposedException or
                SocketException or OperationCanceledException;
        }

        private bool IsDisposed => Volatile.Read(ref m_disposed) != 0;

        private sealed class ConnectionState
        {
            public ConnectionState(long id, TcpClient client)
            {
                Id = id;
                Client = client;
            }

            public long Id { get; }
            public TcpClient Client { get; }
            public TcpClient? Upstream { get; set; }
            public Task? RunTask { get; set; }
            public bool IsClosed => Volatile.Read(ref m_closed) != 0;

            public void Abort()
            {
                if (Interlocked.Exchange(ref m_closed, 1) == 0)
                {
                    Abort(Client);
                    Abort(Upstream);
                }
            }

            private static void Abort(TcpClient? client)
            {
                if (client == null)
                {
                    return;
                }

                try
                {
                    client.LingerState = new LingerOption(true, 0);
                    client.Client.Close(0);
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }

                client.Dispose();
            }

            private int m_closed;
        }

        private const int BufferSize = 16 * 1024;
        private readonly ConcurrentDictionary<long, ConnectionState> m_connections = new();
        private readonly CancellationTokenSource m_cts = new();
        private readonly ILogger<TcpChaosProxy> m_logger;
        private readonly int m_localPort;
        private readonly SemaphoreSlim m_acceptSignal = new(0, 1);
        private readonly Task m_acceptTask;
        private long m_acceptBlockVersion;
        private long m_bytesForwarded;
        private long m_nextConnectionId;
        private long m_totalAccepted;
        private int m_acceptBlocked;
        private int m_clientToServerBytesPerSec;
        private int m_disposed;
        private int m_serverToClientBytesPerSec;
        private int m_stallForwarding;
        private TcpListener m_listener;
    }
}
