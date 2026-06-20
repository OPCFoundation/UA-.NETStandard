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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Pcap.Audit;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Replay
{
    /// <summary>
    /// Singleton manager for active replay sessions.
    /// </summary>
    public sealed class ReplaySessionManager : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a replay session manager.
        /// </summary>
        public ReplaySessionManager(
            ILoggerFactory? loggerFactory = null,
            IPcapAuditSink? auditSink = null,
            PcapOptions? options = null)
        {
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            m_auditSink = auditSink;
            m_options = options ?? new PcapOptions();
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
            ValidateSpeed(request.Speed);

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
                await AuditAsync(
                    PcapAuditEventKind.StartReplay,
                    id,
                    request.PcapFilePath,
                    session.TargetEndpointUrl ?? session.ListenUri?.ToString(),
                    properties: CreateStartReplayProperties(request),
                    ct).ConfigureAwait(false);
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
        /// <exception cref="PcapDiagnosticsException"></exception>
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
            await AuditAsync(
                PcapAuditEventKind.StopReplay,
                session.Id,
                resourcePath: null,
                remoteEndpoint: session.TargetEndpointUrl ?? session.ListenUri?.ToString(),
                properties: CreateStopReplayProperties(session),
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (ReplaySession session in m_sessions.Values)
            {
                bool shouldAuditStop = session.IsRunning;
                await session.DisposeAsync().ConfigureAwait(false);
                if (shouldAuditStop)
                {
                    await AuditAsync(
                        PcapAuditEventKind.StopReplay,
                        session.Id,
                        resourcePath: null,
                        remoteEndpoint: session.TargetEndpointUrl ?? session.ListenUri?.ToString(),
                        properties: CreateStopReplayProperties(session),
                        CancellationToken.None).ConfigureAwait(false);
                }
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

                    var client = new MockClientReplay(source, request.TargetEndpointUrl, m_options, m_loggerFactory)
                    {
                        Speed = request.Speed
                    };
                    return new ReplaySession(id, client, request.TargetEndpointUrl);
                default:
                    throw new PcapDiagnosticsException($"Unsupported replay mode '{request.Mode}'.");
            }
        }

        private ValueTask AuditAsync(
            PcapAuditEventKind kind,
            string sessionId,
            string? resourcePath,
            string? remoteEndpoint,
            IReadOnlyDictionary<string, string>? properties,
            CancellationToken ct)
        {
            if (m_auditSink is null)
            {
                return ValueTask.CompletedTask;
            }

            return m_auditSink.OnEventAsync(
                new PcapAuditEvent(
                    kind,
                    DateTimeOffset.UtcNow,
                    sessionId,
                    resourcePath,
                    remoteEndpoint,
                    properties),
                ct);
        }

        private static Dictionary<string, string> CreateStartReplayProperties(StartReplayRequest request)
        {
            var properties = new Dictionary<string, string>
            {
                ["Mode"] = request.Mode.ToString(),
                ["Speed"] = request.Speed.ToString(CultureInfo.InvariantCulture)
            };

            AddIfNotEmpty(properties, "KeyLogFilePath", request.KeyLogFilePath);
            if (request.ListenPort.HasValue)
            {
                properties["ListenPort"] = request.ListenPort.Value.ToString(
                    CultureInfo.InvariantCulture);
            }

            return properties;
        }

        private static Dictionary<string, string> CreateStopReplayProperties(ReplaySession session)
        {
            return new Dictionary<string, string>
            {
                ["Mode"] = session.Mode.ToString(),
                ["IsRunning"] = session.IsRunning.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static void AddIfNotEmpty(Dictionary<string, string> properties, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties[name] = value;
            }
        }

        private static void ValidateSpeed(double speed)
        {
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0d)
            {
                throw new ArgumentException(
                    $"Replay speed {speed} is not a finite positive number. " +
                    "Provide a value > 0 (e.g., 1.0 for real-time, 2.0 for 2× faster).",
                    nameof(speed));
            }
        }

        private readonly ConcurrentDictionary<string, ReplaySession> m_sessions = new(
            StringComparer.OrdinalIgnoreCase);
        private readonly ILoggerFactory m_loggerFactory;
        private readonly IPcapAuditSink? m_auditSink;
        private readonly PcapOptions m_options;
    }
}
