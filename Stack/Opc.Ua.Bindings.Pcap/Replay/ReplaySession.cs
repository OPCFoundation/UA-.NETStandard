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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Pcap.Capture;

namespace Opc.Ua.Bindings.Pcap.Replay
{
    /// <summary>
    /// Replay operating mode.
    /// </summary>
    public enum ReplayMode
    {
        /// <summary>
        /// Listen locally and replay captured server bytes to a connecting client.
        /// </summary>
        MockServer = 0,

        /// <summary>
        /// Re-issue supported captured client requests to a live target endpoint.
        /// </summary>
        MockClient = 1
    }

    /// <summary>
    /// Unified replay session wrapper for mock-server and mock-client replay.
    /// </summary>
    public sealed class ReplaySession : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a mock-server replay session.
        /// </summary>
        public ReplaySession(
            string id,
            MockServerReplay mockServer,
            string listenScheme,
            int? listenPort)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(mockServer);
            ArgumentException.ThrowIfNullOrWhiteSpace(listenScheme);
            ValidateListenPort(listenPort);

            Id = id;
            Mode = ReplayMode.MockServer;
            m_mockServer = mockServer;
            m_listenScheme = listenScheme;
            m_listenPort = listenPort;
            StartedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Constructs a mock-client replay session.
        /// </summary>
        public ReplaySession(
            string id,
            MockClientReplay mockClient,
            string targetEndpointUrl)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(mockClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetEndpointUrl);

            Id = id;
            Mode = ReplayMode.MockClient;
            TargetEndpointUrl = targetEndpointUrl;
            m_mockClient = mockClient;
            StartedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Session id.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Replay mode.
        /// </summary>
        public ReplayMode Mode { get; }

        /// <summary>
        /// Listen URI for mock-server mode after start.
        /// </summary>
        public Uri? ListenUri => m_mockServer?.ListenUri;

        /// <summary>
        /// Target endpoint URL for mock-client mode.
        /// </summary>
        public string? TargetEndpointUrl { get; }

        /// <summary>
        /// UTC time at which the session was created.
        /// </summary>
        public DateTimeOffset StartedAt { get; }

        /// <summary>
        /// Whether the replay session is currently running.
        /// </summary>
        public bool IsRunning => Volatile.Read(ref m_isRunning) == 1;

        /// <summary>
        /// Mock-client result after a mock-client run completes.
        /// </summary>
        public MockReplayResult? Result { get; private set; }

        /// <summary>
        /// Starts the wrapped replay.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException"></exception>
        public async ValueTask StartAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref m_isRunning, 1, 0) != 0)
            {
                throw new PcapDiagnosticsException($"Replay session '{Id}' is already running.");
            }

            try
            {
                if (m_mockServer is not null)
                {
                    await m_mockServer.StartAsync(m_listenScheme, m_listenPort, ct).ConfigureAwait(false);
                    return;
                }

                if (m_mockClient is not null)
                {
                    Result = await m_mockClient.RunAsync(ct).ConfigureAwait(false);
                    Interlocked.Exchange(ref m_isRunning, 0);
                }
            }
            catch
            {
                Interlocked.Exchange(ref m_isRunning, 0);
                throw;
            }
        }

        /// <summary>
        /// Stops the wrapped replay.
        /// </summary>
        public async ValueTask StopAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (m_mockServer is not null)
            {
                await m_mockServer.StopAsync(ct).ConfigureAwait(false);
            }

            Interlocked.Exchange(ref m_isRunning, 0);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            if (m_mockServer is not null)
            {
                await m_mockServer.DisposeAsync().ConfigureAwait(false);
            }

            if (m_mockClient is not null)
            {
                await m_mockClient.DisposeAsync().ConfigureAwait(false);
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Validates a TCP listen port. Accepts <c>null</c> (caller wants
        /// the OS to pick an ephemeral port) and any value in
        /// <c>[1024, 65535]</c>. Rejects privileged ports (0-1023),
        /// negative values, and values above 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="listenPort"/> is outside the allowed
        /// range.
        /// </exception>
        private static void ValidateListenPort(int? listenPort)
        {
            if (listenPort is null)
            {
                return;
            }

            if (listenPort.Value < 1024 || listenPort.Value > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(listenPort),
                    listenPort.Value,
                    "Replay listenPort must be null (use ephemeral) or in the range [1024, 65535]. " +
                    "Privileged ports (0-1023) are not permitted to avoid requiring elevated privileges " +
                    "and to defend against port-squatting.");
            }
        }

        private readonly MockServerReplay? m_mockServer;
        private readonly MockClientReplay? m_mockClient;
        private readonly string m_listenScheme = "opc.tcp";
        private readonly int? m_listenPort;
        private int m_isRunning;
    }
}
