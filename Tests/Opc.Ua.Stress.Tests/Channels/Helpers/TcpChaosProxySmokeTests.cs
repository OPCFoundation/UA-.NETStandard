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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Smoke tests for the TCP chaos proxy helper.
    /// </summary>
    [TestFixture]
    [Category("TcpChaosProxy")]
    [Parallelizable]
    public class TcpChaosProxySmokeTests
    {
        /// <summary>
        /// Verifies byte forwarding through the proxy.
        /// </summary>
        [Test]
        public async Task StartAndConnectAsync()
        {
            using var upstream = new EchoUpstream();
            Task echoTask = upstream.AcceptAndEchoOneClientAsync();
            TcpChaosProxy proxy = await TcpChaosProxy
                .StartAsync(upstream.Url)
                .ConfigureAwait(false);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", proxy.LocalUrl.Port).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();
                byte[] payload = CreatePayload(100);
                byte[] response = new byte[payload.Length];

                await WriteAsync(stream, payload, 0, payload.Length, CancellationToken.None)
                    .ConfigureAwait(false);
                await ReadExactlyAsync(stream, response, response.Length).ConfigureAwait(false);

                Assert.That(response, Is.EqualTo(payload));
                Assert.That(proxy.TotalAccepted, Is.EqualTo(1));
                Assert.That(proxy.BytesForwarded, Is.GreaterThanOrEqualTo(payload.Length * 2L));
            }
            finally
            {
                await proxy.DisposeAsync().ConfigureAwait(false);
                upstream.Stop();
                await echoTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies abrupt disconnection of active sockets.
        /// </summary>
        [Test]
        public async Task DropAllConnectionsTerminatesActiveAsync()
        {
            using var upstream = new EchoUpstream();
            Task echoTask = upstream.AcceptAndEchoOneClientAsync();
            TcpChaosProxy proxy = await TcpChaosProxy
                .StartAsync(upstream.Url)
                .ConfigureAwait(false);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", proxy.LocalUrl.Port).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();
                byte[] payload = CreatePayload(1);
                byte[] response = new byte[payload.Length];
                await WriteAsync(stream, payload, 0, payload.Length, CancellationToken.None)
                    .ConfigureAwait(false);
                await ReadExactlyAsync(stream, response, response.Length).ConfigureAwait(false);
                Assert.That(proxy.ActiveConnections, Is.EqualTo(1));

                await proxy.DropAllConnectionsAsync().ConfigureAwait(false);
                Exception? exception = await ReadFailureAsync(stream).ConfigureAwait(false);

                Assert.That(exception, Is.Not.Null);
                Assert.That(IsConnectionReset(exception!), Is.True, exception!.ToString());
            }
            finally
            {
                await proxy.DisposeAsync().ConfigureAwait(false);
                upstream.Stop();
                await echoTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies accept blocking delays a new connection attempt.
        /// </summary>
        [Test]
        public async Task BlockAcceptDelaysNewConnectionAsync()
        {
            using var upstream = new EchoUpstream();
            Task echoTask = upstream.AcceptAndEchoOneClientAsync();
            TcpChaosProxy proxy = await TcpChaosProxy
                .StartAsync(upstream.Url)
                .ConfigureAwait(false);
            try
            {
                var blockDuration = TimeSpan.FromMilliseconds(500);
                Task blockTask = proxy.BlockAcceptAsync(blockDuration);
                var stopwatch = Stopwatch.StartNew();
                using TcpClient client = await ConnectWithRetryAsync(
                    proxy.LocalUrl.Port,
                    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                stopwatch.Stop();
                await blockTask.ConfigureAwait(false);

                Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400)));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
                Assert.That(client.Connected, Is.True);
            }
            finally
            {
                await proxy.DisposeAsync().ConfigureAwait(false);
                upstream.Stop();
                await echoTask.ConfigureAwait(false);
            }
        }

        private static byte[] CreatePayload(int length)
        {
            var payload = new byte[length];
            for (int ii = 0; ii < payload.Length; ii++)
            {
                payload[ii] = (byte)ii;
            }

            return payload;
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int bytesRead = await ReadAsync(
                    stream,
                    buffer,
                    offset,
                    count - offset,
                    CancellationToken.None).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new IOException("The stream ended before the expected bytes were read.");
                }

                offset += bytesRead;
            }
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

        private static async Task<Exception?> ReadFailureAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            Task<int> readTask = ReadAsync(
                stream,
                buffer,
                0,
                buffer.Length,
                CancellationToken.None).AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
            if (!ReferenceEquals(completedTask, readTask))
            {
                return new TimeoutException("Read did not observe the dropped connection.");
            }

            try
            {
                int bytesRead = await readTask.ConfigureAwait(false);
                return bytesRead == 0
                    ? new IOException("The connection closed gracefully instead of being reset.")
                    : new IOException("The connection unexpectedly returned data after being dropped.");
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static bool IsConnectionReset(Exception exception)
        {
            if (exception is IOException { InnerException: SocketException socketException })
            {
                return socketException.SocketErrorCode == SocketError.ConnectionReset;
            }

            return exception is SocketException ex && ex.SocketErrorCode == SocketError.ConnectionReset;
        }

        private static async Task<TcpClient> ConnectWithRetryAsync(int port, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
                    return client;
                }
                catch (SocketException)
                {
                    client.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
                }
            }

            throw new TimeoutException("Timed out waiting for the proxy to accept a connection.");
        }

        private sealed class EchoUpstream : IDisposable
        {
            public EchoUpstream()
            {
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                var endPoint = (IPEndPoint)m_listener.LocalEndpoint;
                Url = new UriBuilder(Utils.UriSchemeOpcTcp, "localhost", endPoint.Port).Uri;
            }

            public Uri Url { get; }

            public async Task AcceptAndEchoOneClientAsync()
            {
                try
                {
                    using TcpClient client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    NetworkStream stream = client.GetStream();
                    var buffer = new byte[1024];
                    while (true)
                    {
                        int bytesRead = await ReadAsync(
                            stream,
                            buffer,
                            0,
                            buffer.Length,
                            m_cts.Token).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        await WriteAsync(stream, buffer, 0, bytesRead, m_cts.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SocketException)
                {
                }
                catch (IOException)
                {
                }
            }

            public void Stop()
            {
                if (Interlocked.Exchange(ref m_stopped, 1) != 0)
                {
                    return;
                }

                m_cts.Cancel();
                m_listener.Stop();
#if NETCOREAPP
                m_listener.Dispose();
#endif
            }

            public void Dispose()
            {
                Stop();
                m_cts.Dispose();
            }

            private readonly CancellationTokenSource m_cts = new();
            private readonly TcpListener m_listener;
            private int m_stopped;
        }
    }
}
