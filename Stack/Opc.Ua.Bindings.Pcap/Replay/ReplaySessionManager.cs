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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Capture.Sources;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Replay
{
    /// <summary>
    /// Singleton manager for active replay sessions.
    /// </summary>
    public sealed class ReplaySessionManager : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a replay session manager.
        /// </summary>
        public ReplaySessionManager(ILoggerFactory? loggerFactory = null)
        {
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        }

        /// <summary>
        /// Creates and starts a replay session.
        /// </summary>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to ReplaySession; TODO: add analyzer-recognized ownership helpers.")]
        public async ValueTask<ReplaySession> StartAsync(StartReplayRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PcapFilePath);

            string id = Guid.NewGuid().ToString("N");
            ReplayCaptureSource source = new(m_loggerFactory);
            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = request.PcapFilePath,
                    KeyLogFilePath = request.KeyLogFilePath
                },
                ct).ConfigureAwait(false);

            ReplaySession session = CreateSession(id, request, source);
            try
            {
                await session.StartAsync(ct).ConfigureAwait(false);
                m_sessions[id] = session;
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Looks up a replay session by id.
        /// </summary>
        public ReplaySession Get(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (!m_sessions.TryGetValue(id, out ReplaySession? session))
            {
                throw new PcapDiagnosticsException($"Replay session '{id}' was not found.");
            }

            return session;
        }

        /// <summary>
        /// Lists known replay sessions.
        /// </summary>
        public IReadOnlyList<ReplaySession> List()
        {
            return [.. m_sessions.Values];
        }

        /// <summary>
        /// Stops a replay session by id.
        /// </summary>
        public async ValueTask StopAsync(string id, CancellationToken ct)
        {
            ReplaySession session = Get(id);
            await session.StopAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (ReplaySession session in m_sessions.Values)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            m_sessions.Clear();
            GC.SuppressFinalize(this);
        }

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to ReplaySession; TODO: add analyzer-recognized ownership helpers.")]
        private ReplaySession CreateSession(
            string id,
            StartReplayRequest request,
            ReplayCaptureSource source)
        {
            switch (request.Mode)
            {
                case ReplayMode.MockServer:
                    var server = new MockServerReplay(source, m_loggerFactory)
                    {
                        Speed = request.Speed
                    };
                    return new ReplaySession(
                        id,
                        server,
                        request.ListenScheme ?? "opc.tcp",
                        request.ListenPort);
                case ReplayMode.MockClient:
                    if (string.IsNullOrWhiteSpace(request.TargetEndpointUrl))
                    {
                        throw new PcapDiagnosticsException("Mock-client replay requires 'targetEndpointUrl'.");
                    }

                    var client = new MockClientReplay(source, request.TargetEndpointUrl, m_loggerFactory)
                    {
                        Speed = request.Speed
                    };
                    return new ReplaySession(id, client, request.TargetEndpointUrl);
                default:
                    throw new PcapDiagnosticsException($"Unsupported replay mode '{request.Mode}'.");
            }
        }

        private readonly ConcurrentDictionary<string, ReplaySession> m_sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILoggerFactory m_loggerFactory;
    }
}
