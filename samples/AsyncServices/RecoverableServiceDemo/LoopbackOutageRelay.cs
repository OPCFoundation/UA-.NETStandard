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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Interrupts real loopback TCP connections without decoding or fabricating OPC UA messages.
    /// </summary>
    internal sealed class LoopbackOutageRelay : IAsyncDisposable
    {
        /// <summary>
        /// Starts a loopback-only listener forwarding to the local sample Server.
        /// </summary>
        public LoopbackOutageRelay(int serverPort, ITelemetryContext telemetry)
        {
            m_serverPort = serverPort;
            m_logger = telemetry.CreateLogger<LoopbackOutageRelay>();
            m_listener.Start();
            EndpointUrl = new Uri($"opc.tcp://127.0.0.1:{((IPEndPoint)m_listener.LocalEndpoint).Port}/RecoverableDemo");
            m_accept = AcceptAsync(m_shutdown.Token);
        }

        /// <summary>
        /// Gets the local endpoint exposed to the sample Client.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Gets the number of established connections closed by the demonstrated outage.
        /// </summary>
        public int InterruptedConnections { get; private set; }

        /// <summary>
        /// Closes existing sockets and rejects new ones until the connection is restored.
        /// </summary>
        public void Interrupt()
        {
            lock (m_gate)
            {
                m_offline = true;
                InterruptedConnections += m_clients.Count;
                foreach (TcpClient client in m_clients)
                {
                    client.Dispose();
                }
            }
        }

        /// <summary>
        /// Allows new native connections while preserving the running Server and its Sessions.
        /// </summary>
        public void Restore()
        {
            lock (m_gate)
            {
                m_offline = false;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_shutdown.CancelAsync().ConfigureAwait(false);
            m_listener.Stop();
            Interrupt();
            try
            {
                await m_accept.ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await Task.WhenAll(m_connections).ConfigureAwait(false);
                }
                finally
                {
                    m_listener.Dispose();
                    m_shutdown.Dispose();
                }
            }
        }

        private async Task AcceptAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    TcpClient client = await m_listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    lock (m_gate)
                    {
                        if (m_offline)
                        {
                            client.Dispose();
                        }
                        else
                        {
                            m_clients.Add(client);
                            // ForwardAsync owns the socket; DisposeAsync joins it. TODO: Model this transfer in CA2025.
#pragma warning disable CA2025
                            m_connections.Add(ForwardAsync(client, ct));
#pragma warning restore CA2025
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The owned accept loop ends during disposal.
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                // Stopping the listener also interrupts a pending accept.
            }
        }

        private async Task ForwardAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var server = new TcpClient())
            using (var forwarding = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                try
                {
                    await server.ConnectAsync(IPAddress.Loopback, m_serverPort, ct).ConfigureAwait(false);
                    NetworkStream clientStream = client.GetStream();
                    NetworkStream serverStream = server.GetStream();
                    await Task.WhenAll(
                        ForwardDirectionAsync(clientStream, serverStream, forwarding),
                        ForwardDirectionAsync(serverStream, clientStream, forwarding)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (forwarding.IsCancellationRequested)
                {
                    // A completed direction or relay disposal stops the other direction.
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    m_logger.ConnectionClosed(ex);
                }
                finally
                {
                    lock (m_gate)
                    {
                        m_clients.Remove(client);
                    }
                }
            }
        }

        private static async Task ForwardDirectionAsync(
            NetworkStream source, NetworkStream destination, CancellationTokenSource forwarding)
        {
            try
            {
                await source.CopyToAsync(destination, forwarding.Token).ConfigureAwait(false);
            }
            finally
            {
                await forwarding.CancelAsync().ConfigureAwait(false);
            }
        }

        private readonly int m_serverPort;
        private readonly ILogger m_logger;
        private readonly TcpListener m_listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly Lock m_gate = new();
        private readonly HashSet<TcpClient> m_clients = [];
        private readonly List<Task> m_connections = [];
        private readonly Task m_accept;
        private bool m_offline;
    }

    /// <summary>
    /// Diagnostic messages for the deliberately interrupted relay.
    /// </summary>
    internal static partial class LoopbackOutageRelayLog
    {
        /// <summary>
        /// Records a native connection ending during forwarding or the deliberate outage.
        /// </summary>
        [LoggerMessage(EventId = RecoverableServiceDemoEventIds.ConnectionClosed,
            Level = LogLevel.Debug, Message = "Loopback relay connection closed.")]
        public static partial void ConnectionClosed(this ILogger logger, Exception exception);
    }

    /// <summary>
    /// Stable logging identifiers for the sample.
    /// </summary>
    internal static class RecoverableServiceDemoEventIds
    {
        /// <summary>A native relay socket closed.</summary>
        public const int ConnectionClosed = 1;
    }
}
