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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Opc.Ua.Pcap.Audit;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for replaying OPC UA packet captures.
    /// </summary>
    [McpServerToolType]
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "MCP discovers tool types through reflection; TODO: remove if the analyzer recognizes MCP tools.")]
    internal sealed class PacketReplayTools
    {
        /// <summary>
        /// Starts replaying a pcap capture.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="PcapDiagnosticsException"></exception>
        [McpServerTool(Name = "replay_pcap")]
        [Description("Replays a captured pcap. mode='mock-server' opens an OPC UA listener that replays the " +
            "captured server bytes to a connecting client; mode='mock-client' walks the captured request stream " +
            "and re-issues each request against a live target endpoint. Returns the replay session id which must " +
            "be passed to stop_replay.")]
        public static async Task<ReplaySessionInfo> ReplayPcapAsync(
            IServiceProvider services,
            [Description("Path to the pcap file.")] string pcapPath,
            [Description("Path to the uakeys file (required for mock-client; optional for mock-server).")]
            string? keyLogPath,
            [Description("Replay mode: mock-server | mock-client.")] string mode = "mock-server",
            [Description("For mock-client: target endpoint URL (e.g. opc.tcp://target:62541).")]
            string? targetEndpointUrl = null,
            [Description("Speed multiplier (0.5 = half speed; default 1.0).")]
            double speed = 1.0,
            [Description("For mock-server: optional explicit port.")] int? listenPort = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0d)
            {
                throw new ArgumentException(
                    $"replay_pcap speed {speed} is not a finite positive number. " +
                    "Provide a value > 0 (e.g., 1.0 for real-time, 2.0 for 2× faster).",
                    nameof(speed));
            }

            ReplayMode replayMode = ParseReplayMode(mode);
            PcapOptions pcapOptions = services.GetService<PcapOptions>() ?? new PcapOptions();
            if (replayMode == ReplayMode.MockClient && !pcapOptions.AllowMockClientReplay)
            {
                throw new PcapDiagnosticsException(
                    "Mock-client replay is disabled. " +
                    "Configure PcapOptions.AllowMockClientReplay and " +
                    "AllowedReplayEndpoints in the host's DI setup to enable.");
            }

            ReplaySessionManager manager = GetReplayManager(services);
            await AuditAsync(
                services,
                new PcapAuditEvent(
                    PcapAuditEventKind.StartReplay,
                    DateTimeOffset.UtcNow,
                    sessionId: null,
                    resourcePath: pcapPath,
                    remoteEndpoint: targetEndpointUrl,
                    properties: new Dictionary<string, string>
                    {
                        ["Mode"] = replayMode.ToString(),
                        ["Speed"] = speed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }),
                ct).ConfigureAwait(false);
            ReplaySession session = await manager.StartAsync(
                new StartReplayRequest
                {
                    PcapFilePath = pcapPath,
                    KeyLogFilePath = keyLogPath,
                    Mode = replayMode,
                    TargetEndpointUrl = targetEndpointUrl,
                    Speed = speed,
                    ListenPort = listenPort
                },
                ct).ConfigureAwait(false);

            return CreateInfo(session);
        }

        /// <summary>
        /// Stops a replay session.
        /// </summary>
        [McpServerTool(Name = "stop_replay")]
        [Description("Stops a running replay session.")]
        public static async Task<ReplaySessionInfo> StopReplayAsync(
            IServiceProvider services,
            [Description("Replay session id.")] string sessionId,
            CancellationToken ct)
        {
            ReplaySessionManager manager = GetReplayManager(services);
            await AuditAsync(
                services,
                new PcapAuditEvent(
                    PcapAuditEventKind.StopReplay,
                    DateTimeOffset.UtcNow,
                    sessionId,
                    resourcePath: null,
                    remoteEndpoint: null,
                    properties: null),
                ct).ConfigureAwait(false);
            await manager.StopAsync(sessionId, ct).ConfigureAwait(false);
            return CreateInfo(manager.Get(sessionId));
        }

        /// <summary>
        /// Lists known replay sessions.
        /// </summary>
        [McpServerTool(Name = "list_replays")]
        [Description("Lists every replay session known to the server.")]
        public static IReadOnlyList<ReplaySessionInfo> ListReplays(IServiceProvider services)
        {
            ReplaySessionManager manager = GetReplayManager(services);
            return [.. manager.List().Select(CreateInfo)];
        }

        private static ReplaySessionManager GetReplayManager(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // TODO: Remove this guard once replay registration is mandatory for every host using these tools.
            return services.GetService<ReplaySessionManager>()
                ?? throw new NotSupportedException(
                    "Replay support is not yet wired - replay agent has not completed.");
        }

        private static ValueTask AuditAsync(
            IServiceProvider services,
            PcapAuditEvent auditEvent,
            CancellationToken ct)
        {
            IPcapAuditSink? auditSink = services.GetService<IPcapAuditSink>();
            if (auditSink is null)
            {
                return ValueTask.CompletedTask;
            }

            return auditSink.OnEventAsync(auditEvent, ct);
        }

        private static ReplayMode ParseReplayMode(string mode)
        {
            return mode.Trim().ToLowerInvariant() switch
            {
                "mock-server" or "mockserver" => ReplayMode.MockServer,
                "mock-client" or "mockclient" => ReplayMode.MockClient,
                _ => throw new PcapDiagnosticsException(
                    $"Unsupported replay mode '{mode}'. Use mock-server or mock-client.")
            };
        }

        private static ReplaySessionInfo CreateInfo(ReplaySession session)
        {
            return new ReplaySessionInfo
            {
                SessionId = session.Id,
                Mode = session.Mode.ToString(),
                ListenUri = session.ListenUri?.ToString(),
                TargetEndpointUrl = session.TargetEndpointUrl,
                StartedAt = session.StartedAt,
                IsRunning = session.IsRunning,
                ResultSummary = session.Result?.ToString()
            };
        }
    }

    /// <summary>
    /// Status and metadata for a replay session.
    /// </summary>
    public sealed class ReplaySessionInfo
    {
        /// <summary>
        /// Gets the replay session id.
        /// </summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the replay mode.
        /// </summary>
        public string Mode { get; init; } = string.Empty;

        /// <summary>
        /// Gets the mock-server listen URI, when available.
        /// </summary>
        public string? ListenUri { get; init; }

        /// <summary>
        /// Gets the mock-client target endpoint URL, when available.
        /// </summary>
        public string? TargetEndpointUrl { get; init; }

        /// <summary>
        /// Gets the replay start time.
        /// </summary>
        public DateTimeOffset StartedAt { get; init; }

        /// <summary>
        /// Gets whether the replay session is currently running.
        /// </summary>
        public bool IsRunning { get; init; }

        /// <summary>
        /// Gets a text summary of replay results, when available.
        /// </summary>
        public string? ResultSummary { get; init; }
    }
}
