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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for OPC UA packet capture sessions.
    /// </summary>
    [McpServerToolType]
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "MCP discovers tool types through reflection; TODO: remove if the analyzer recognizes MCP tools.")]
    internal sealed class PacketCaptureTools
    {
        private const int kMaxResponseBytes = 10 * 1024 * 1024;

        /// <summary>
        /// Lists local network interfaces that can be captured from.
        /// </summary>
        [McpServerTool(Name = "list_interfaces")]
        [Description("Lists local network interfaces that can be used as the 'interfaceName' parameter to " +
            "start_capture with source='nic'. Requires libpcap (Linux/macOS) or Npcap (Windows).")]
        public static IReadOnlyList<NetworkInterfaceInfo> ListInterfaces()
        {
            return NetworkInterfaceEnumerator.ListLocalInterfaces();
        }

        /// <summary>
        /// Starts an OPC UA packet capture session.
        /// </summary>
        [McpServerTool(Name = "start_capture")]
        [Description("Starts a new OPC UA packet capture session. source='nic' captures from a network interface " +
            "(supply 'interfaceName'); source='inproc-client' captures the MCP server's own OPC UA client traffic " +
            "with full key material; source='inproc-server' is reserved for hosted server scenarios; source='replay' " +
            "re-reads an existing pcap + keylog. The in-process tap only captures channels that already exist when " +
            "start_capture is called. Returns the session id which must be passed to stop_capture / get_capture / " +
            "summarize_capture / replay_pcap. Capture stops automatically when the configured size/duration limits " +
            "are reached.")]
        public static async Task<CaptureSessionInfo> StartCaptureAsync(
            CaptureSessionManager manager,
            OpcUaSessionManager sessions,
            [Description("The capture request, including the source name and source-specific parameters.")]
            StartCaptureRequest request,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(sessions);
            ArgumentNullException.ThrowIfNull(request);

            // Every ITransportChannel created via ClientChannelManager is
            // already capture-enabled by the Opc.Ua.Pcap binding,
            // so starting the session here is the only step needed to flip
            // recording on for both existing and future channels.
            CaptureSession session = await manager.StartAsync(request, ct).ConfigureAwait(false);
            return session.ToInfo();
        }

        /// <summary>
        /// Stops an OPC UA packet capture session.
        /// </summary>
        [McpServerTool(Name = "stop_capture")]
        [Description("Stops a running capture session and finalises the pcap and keylog on disk. Subsequent calls " +
            "to get_capture / summarize_capture are safe once this returns.")]
        public static async Task<CaptureSessionInfo> StopCaptureAsync(
            CaptureSessionManager manager,
            [Description("Session id returned by start_capture.")] string sessionId,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);

            CaptureSession session = await manager.StopAsync(sessionId, ct).ConfigureAwait(false);
            return session.ToInfo();
        }

        /// <summary>
        /// Lists packet capture sessions.
        /// </summary>
        [McpServerTool(Name = "list_captures")]
        [Description("Lists capture sessions known to the server. 'state' filter: 'active' (starting/running), " +
            "'completed' (completed/failed), or 'all' (default).")]
        public static IReadOnlyList<CaptureSessionInfo> ListCaptures(
            CaptureSessionManager manager,
            [Description("Optional filter: active | completed | all.")] string? state = null)
        {
            ArgumentNullException.ThrowIfNull(manager);

            string filter = state?.Trim().ToLowerInvariant() ?? "all";
            return [.. manager.List()
                .Where(session => MatchesFilter(session.State, filter))
                .Select(static session => session.ToInfo())];
        }

        /// <summary>
        /// Returns the captured trace in the requested format.
        /// </summary>
        [McpServerTool(Name = "get_capture")]
        [Description("Returns the captured trace in the requested format. Binary formats (pcap, pcapng) are " +
            "returned as embedded MCP resources; text formats (json, csv, text, service-timeline) are returned " +
            "inline. service-timeline returns an OPC UA-specific decoded view that requires both captured frames " +
            "AND key material. Default format = service-timeline.")]
        public static async Task<IList<ContentBlock>> GetCaptureAsync(
            CaptureSessionManager manager,
            TraceFormatterRegistry formatters,
            [Description("Session id.")] string sessionId,
            [Description("Output format: pcap | pcapng | json | csv | text | service-timeline.")] string format =
                "service-timeline",
            [Description("Maximum frames to format. Default = all.")] long? maxFrames = null,
            [Description("If true, format a snapshot of an active session without stopping it. Default false.")]
            bool allowPartial = false,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(formatters);

            CaptureSession session = manager.Get(sessionId);
            await using ConfiguredAsyncDisposable sessionLock = (await session.AcquireAsync(ct).ConfigureAwait(false)).ConfigureAwait(false);
            ValidateReadable(session, allowPartial);

            FormatKind kind = ParseFormat(format);
            ITraceFormatter formatter = formatters.Get(kind);
            FormatResult result = await formatter.FormatAsync(session.Source, maxFrames, ct).ConfigureAwait(false);
            return BuildContentBlocks(session.Id, result, formatter);
        }

        /// <summary>
        /// Captures for a bounded duration and returns the formatted trace.
        /// </summary>
        [McpServerTool(Name = "capture_now")]
        [Description("Convenience: starts a capture, waits for durationSeconds (capped at 60s), stops, then returns " +
            "the formatted trace in one call. Cleanup is guaranteed even on cancellation.")]
        public static async Task<IList<ContentBlock>> CaptureNowAsync(
            CaptureSessionManager manager,
            OpcUaSessionManager sessions,
            TraceFormatterRegistry formatters,
            [Description("Capture configuration, format, and duration.")] CaptureNowRequest request,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(sessions);
            ArgumentNullException.ThrowIfNull(formatters);
            ArgumentNullException.ThrowIfNull(request);

            CaptureSession? session = null;
            try
            {
                // The decorator binding takes care of wrapping every
                // channel; we just need to start the session to publish
                // ourselves as the active observer.
                session = await manager.StartAsync(request.Start, ct).ConfigureAwait(false);

                int durationSeconds = Math.Clamp(request.DurationSeconds, 0, 60);
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), ct).ConfigureAwait(false);
                await session.StopAsync(ct).ConfigureAwait(false);

                ITraceFormatter formatter = formatters.Get(request.Format);
                await using ConfiguredAsyncDisposable sessionLock = (await session.AcquireAsync(ct).ConfigureAwait(false)).ConfigureAwait(false);
                FormatResult result = await formatter.FormatAsync(session.Source, null, ct).ConfigureAwait(false);
                return BuildContentBlocks(session.Id, result, formatter);
            }
            finally
            {
                if (session is not null && session.State is CaptureSessionState.Running or CaptureSessionState.Starting)
                {
                    await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static IList<ContentBlock> BuildContentBlocks(
            string sessionId,
            FormatResult result,
            ITraceFormatter formatter)
        {
            if (result.Bytes.LongLength > kMaxResponseBytes)
            {
                throw new PcapDiagnosticsException(
                    $"Formatted capture is {result.Bytes.LongLength} bytes, which exceeds the 10 MB MCP response cap. " +
                    "Request fewer frames with maxFrames or use a binary capture artifact directly.");
            }

            if (!formatter.IsBinary)
            {
                return
                [
                    new TextContentBlock
                    {
                        Text = Encoding.UTF8.GetString(result.Bytes)
                    }
                ];
            }

            string uri = $"opcua-pcap://capture/{Uri.EscapeDataString(sessionId)}/{result.Kind.ToString().ToLowerInvariant()}";
            return
            [
                new EmbeddedResourceBlock
                {
                    Resource = new BlobResourceContents
                    {
                        Uri = uri,
                        MimeType = result.MimeType,
                        Blob = result.Bytes
                    }
                }
            ];
        }

        private static FormatKind ParseFormat(string format)
        {
            if (!FormatKindExtensions.TryParse(format, out FormatKind kind))
            {
                throw new PcapDiagnosticsException(
                    $"Unsupported format '{format}'. Use pcap, pcapng, json, csv, text, or service-timeline.");
            }

            return kind;
        }

        private static bool MatchesFilter(CaptureSessionState state, string filter)
        {
            return filter switch
            {
                "active" => state is CaptureSessionState.Starting or CaptureSessionState.Running,
                "completed" => state is CaptureSessionState.Completed or CaptureSessionState.Failed,
                "all" or "" => true,
                _ => throw new PcapDiagnosticsException(
                    $"Unsupported state filter '{filter}'. Use active, completed, or all.")
            };
        }

        private static void ValidateReadable(CaptureSession session, bool allowPartial)
        {
            if (allowPartial)
            {
                return;
            }

            if (session.State is CaptureSessionState.Completed or CaptureSessionState.Failed)
            {
                return;
            }

            throw new PcapDiagnosticsException(
                $"Capture session '{session.Id}' is {session.State}; stop it first or pass allowPartial=true.");
        }
    }
}
