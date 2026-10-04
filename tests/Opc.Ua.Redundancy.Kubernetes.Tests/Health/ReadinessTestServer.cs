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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// A <see cref="KubernetesReadinessServer"/> that a test started on a loopback
    /// port of its own, together with that port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A port read from a <see cref="TcpListener"/> probe is only free at the
    /// instant it is queried: the probe releases it again before the readiness
    /// server registers its URL prefixes, so tests that run in parallel can draw
    /// the same port. Two readiness servers on one port register the same prefix,
    /// for example <c>http://localhost:{port}/readyz/</c>, and the second
    /// <see cref="HttpListener.Start"/> fails because the prefix "conflicts with an
    /// existing registration on the machine" - <c>ERROR_ALREADY_EXISTS</c> (183)
    /// from http.sys on Windows, the same message from the managed listener
    /// elsewhere. On Windows the probe cannot rule this out either, because an
    /// http.sys registration is an entry in the URL namespace of the kernel driver
    /// and not a user-mode socket bound to the probed loopback address.
    /// </para>
    /// <para>
    /// <see cref="GetUniqueFreePort"/> therefore never hands out a port twice in a
    /// test run, which rules out collisions between the tests themselves. A port
    /// that another process takes between the probe and the start is handled by
    /// <see cref="StartAsync"/>, which moves to a fresh port a bounded number of
    /// times.
    /// </para>
    /// </remarks>
    internal sealed class ReadinessTestServer : IAsyncDisposable
    {
        private ReadinessTestServer(KubernetesReadinessServer server, int port)
        {
            Server = server;
            Port = port;
        }

        /// <summary>
        /// The started readiness server.
        /// </summary>
        public KubernetesReadinessServer Server { get; }

        /// <summary>
        /// The loopback port the server listens on.
        /// </summary>
        public int Port { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return Server.DisposeAsync();
        }

        /// <summary>
        /// Creates a readiness server on a port from <see cref="GetUniqueFreePort"/>
        /// and starts it, moving to a fresh port when another process took the
        /// probed port before the start.
        /// </summary>
        /// <param name="createServer">Creates the server for the given port.</param>
        /// <param name="startServer">Starts the server; <see cref="KubernetesReadinessServer.Start"/>
        /// when omitted.</param>
        /// <returns>The started server and its port.</returns>
        public static async Task<ReadinessTestServer> StartAsync(
            Func<int, KubernetesReadinessServer> createServer,
            Func<KubernetesReadinessServer, ValueTask>? startServer = null)
        {
            for (int attempt = 1; ; attempt++)
            {
                int port = GetUniqueFreePort();
                KubernetesReadinessServer server = createServer(port);
                try
                {
                    if (startServer == null)
                    {
                        server.Start();
                    }
                    else
                    {
                        await startServer(server).ConfigureAwait(false);
                    }
                    return new ReadinessTestServer(server, port);
                }
                catch (Exception ex)
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                    if (ex is not HttpListenerException || attempt >= kMaxStartAttempts)
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Returns a loopback port that is free right now and that was not handed
        /// out before in this test run.
        /// </summary>
        /// <returns>The port.</returns>
        /// <exception cref="InvalidOperationException">No such port was found.</exception>
        public static int GetUniqueFreePort()
        {
            // Every probe stays open until a port is found that was not handed
            // out before, so the next probe cannot be given the same port again.
            var probes = new List<TcpListener>();
            try
            {
                while (probes.Count < kMaxProbes)
                {
                    var probe = new TcpListener(IPAddress.Loopback, 0);
                    probes.Add(probe);
                    probe.Start();
                    int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                    if (s_handedOutPorts.TryAdd(port, true))
                    {
                        return port;
                    }
                }
            }
            finally
            {
                foreach (TcpListener probe in probes)
                {
                    probe.Dispose();
                }
            }
            throw new InvalidOperationException(
                "No loopback port was found that was not handed out before in this test run.");
        }

        private const int kMaxStartAttempts = 5;
        private const int kMaxProbes = 64;
        private static readonly ConcurrentDictionary<int, bool> s_handedOutPorts = new();
    }
}
