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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Bindings;
using Opc.Ua.Core.Diagnostics.Audit;
using Opc.Ua.Core.Diagnostics.Capture;
using Opc.Ua.Core.Diagnostics.Capture.Sources;
using Opc.Ua.Core.Diagnostics.DependencyInjection;
using Opc.Ua.Core.Diagnostics.Dissection;
using Opc.Ua.Core.Diagnostics.Formats;
using Opc.Ua.Core.Diagnostics.KeyLog;
using Opc.Ua.Core.Diagnostics.Models;

using OpcUaMcpServerOptions = Opc.Ua.Mcp.McpServerOptions;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for decoding OPC UA packet captures.
    /// </summary>
    [McpServerToolType]
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "MCP discovers tool types through reflection; TODO: remove if the analyzer recognizes MCP tools.")]
    internal sealed class PacketDecodeTools
    {
        private const int kMaxResponseBytes = 10 * 1024 * 1024;
        private const string kTextKeyLogFileName = "keys.uakeys.txt";

        /// <summary>
        /// Lists active OPC UA secure channels known to the MCP server.
        /// </summary>
        [McpServerTool(Name = "list_active_channels")]
        [Description("Lists every OPC UA secure channel currently open in the MCP server's session pool, with " +
            "channel id, token id, security policy, security mode, and remote endpoint. Useful for confirming the " +
            "in-process client tap has visibility before starting a capture.")]
        public static IReadOnlyList<ActiveChannelInfo> ListActiveChannels(OpcUaSessionManager sessions)
        {
            ArgumentNullException.ThrowIfNull(sessions);

            return [.. sessions.GetAllSessions()
                .Where(static info => info.IsConnected)
                .Select(CreateActiveChannelInfo)];
        }

        /// <summary>
        /// Dumps captured key material for a capture session.
        /// </summary>
        [McpServerTool(Name = "dump_keys")]
        [Description("Returns the captured key material for a session as text. format='json' (default) returns " +
            "the .uakeys.json file; format='text' returns the Wireshark-style .uakeys.txt file. Treat the output " +
            "as a secret - it grants decryption of all captured traffic.")]
        public static async Task<IList<ContentBlock>> DumpKeysAsync(
            IServiceProvider services,
            CaptureSessionManager manager,
            [Description("Session id.")] string sessionId,
            [Description("Output format: json | text.")] string format = "json",
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manager);

            CaptureSession session = manager.Get(sessionId);
            string path = ResolveKeyLogPath(session, format);
            await AuditAsync(
                services,
                new PcapAuditEvent(
                    PcapAuditEventKind.DumpKeys,
                    DateTimeOffset.UtcNow,
                    session.Id,
                    path,
                    remoteEndpoint: null,
                    properties: new Dictionary<string, string>
                    {
                        ["Format"] = format
                    }),
                ct).ConfigureAwait(false);
            string text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return CreateText(text);
        }

        /// <summary>
        /// Decodes an existing pcap file with an OPC UA keylog.
        /// </summary>
        [McpServerTool(Name = "decode_pcap_with_keys")]
        [Description("Offline pipeline: re-reads an existing pcap plus a uakeys file from disk, decrypts every " +
            "chunk using the captured keys, and returns a decoded view. format='service-timeline' (default) returns " +
            "a human-readable timeline of service calls; format='json' returns one JSON object per decoded service " +
            "call. The pcap and keys do NOT need to come from the same MCP capture session.")]
        public static async Task<IList<ContentBlock>> DecodePcapWithKeysAsync(
            IServiceProvider services,
            TraceFormatterRegistry formatters,
            [Description("Absolute path to the pcap file.")] string pcapPath,
            [Description("Absolute path to the uakeys file.")] string keyLogPath,
            [Description("Output format: service-timeline | json | text.")] string format = "service-timeline",
            [Description("Maximum frames to process. Default = all.")] long? maxFrames = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(formatters);
            ArgumentException.ThrowIfNullOrWhiteSpace(pcapPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(keyLogPath);

            await AuditAsync(
                services,
                new PcapAuditEvent(
                    PcapAuditEventKind.DecodePcapWithKeys,
                    DateTimeOffset.UtcNow,
                    sessionId: null,
                    resourcePath: pcapPath,
                    remoteEndpoint: null,
                    properties: new Dictionary<string, string>
                    {
                        ["KeyLogPath"] = keyLogPath,
                        ["Format"] = format
                    }),
                ct).ConfigureAwait(false);
            string allowedRoot = GetDecodeAllowedRoot(services);
            pcapPath = ResolveAndValidateDecodePath(pcapPath, allowedRoot);
            keyLogPath = ResolveAndValidateDecodePath(keyLogPath, allowedRoot);

            ReplayCaptureSource source = await CreateReplaySourceAsync(pcapPath, keyLogPath, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sourceDispose = source.ConfigureAwait(false);
            FormatKind kind = ParseDecodeFormat(format);
            if (kind == FormatKind.Json)
            {
                IReadOnlyList<DecodedServiceCall> calls = await DecodeServiceCallsAsync(source, maxFrames, ct)
                    .ConfigureAwait(false);
                return CreateText(FormatDecodedCallsAsJson(calls));
            }

            ITraceFormatter formatter = formatters.Get(kind);
            FormatResult result = await formatter.FormatAsync(source, maxFrames, ct).ConfigureAwait(false);
            return CreateFormattedBlocks(result, formatter);
        }

        /// <summary>
        /// Summarizes service calls observed in a capture.
        /// </summary>
        [McpServerTool(Name = "summarize_service_calls")]
        [Description("Returns aggregate statistics for the OPC UA service calls observed in a capture: call counts " +
            "per service name, average latency, error rate.")]
        public static async Task<ServiceCallSummary> SummarizeServiceCallsAsync(
            IServiceProvider services,
            CaptureSessionManager manager,
            [Description("Session id (or empty string to use pcapPath + keyLogPath instead).")]
            string sessionId,
            [Description("Optional: absolute path to a pcap file (used when sessionId is empty).")]
            string? pcapPath = null,
            [Description("Optional: absolute path to a uakeys file (used with pcapPath).")]
            string? keyLogPath = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manager);

            IReadOnlyList<DecodedServiceCall> calls;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pcapPath);
                ArgumentException.ThrowIfNullOrWhiteSpace(keyLogPath);

                string allowedRoot = GetDecodeAllowedRoot(services);
                pcapPath = ResolveAndValidateDecodePath(pcapPath, allowedRoot);
                keyLogPath = ResolveAndValidateDecodePath(keyLogPath, allowedRoot);

                ReplayCaptureSource source = await CreateReplaySourceAsync(pcapPath, keyLogPath, ct)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable sourceDispose = source.ConfigureAwait(false);
                calls = await DecodeServiceCallsAsync(source, null, ct).ConfigureAwait(false);
            }
            else
            {
                CaptureSession session = manager.Get(sessionId);
                await using ConfiguredAsyncDisposable sessionLock = (await session.AcquireAsync(ct).ConfigureAwait(false))
                    .ConfigureAwait(false);
                calls = await DecodeServiceCallsAsync(session.Source, null, ct).ConfigureAwait(false);
            }

            return CreateSummary(calls);
        }

        /// <summary>
        /// Resolves and validates a file path supplied to a packet-decode
        /// MCP tool. The path must resolve to a file underneath the
        /// per-user packet-capture base folder (defaults to
        /// %LocalAppData%/OPCFoundation/opcua-pcap). Defends against
        /// arbitrary-file-read via path-traversal in the
        /// <c>pcapPath</c> or <c>keyLogPath</c> arguments.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="filePath"/> resolves outside
        /// the allowed root.
        /// </exception>
        internal static string ResolveAndValidateDecodePath(string filePath, string allowedRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);

            string fullPath = Path.GetFullPath(filePath, allowedRoot);
            string fullRoot = Path.GetFullPath(allowedRoot);

            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Decode path '{filePath}' resolves to '{fullPath}' which is " +
                    $"outside the allowed root '{allowedRoot}'.",
                    nameof(filePath));
            }

            return fullPath;
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

        private static string GetDecodeAllowedRoot(IServiceProvider services)
        {
            OpcUaMcpServerOptions? mcpOptions = services.GetService<OpcUaMcpServerOptions>();
            if (mcpOptions is not null &&
                !string.IsNullOrWhiteSpace(mcpOptions.PcapBaseFolder))
            {
                return Path.GetFullPath(mcpOptions.PcapBaseFolder!);
            }

            PcapOptions? options = services.GetService<PcapOptions>();
            return options?.BaseFolder ??
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OPCFoundation",
                    "opcua-pcap");
        }

        private static ActiveChannelInfo CreateActiveChannelInfo(OpcUaSessionManager.SessionInfo info)
        {
            ITransportChannel? channel = info.Session.TransportChannel;
            ChannelToken? token = channel is ISecureChannel secureChannel ? secureChannel.CurrentToken : null;
            EndpointDescription endpoint = channel?.EndpointDescription ?? info.Endpoint;

            return new ActiveChannelInfo
            {
                SessionName = info.Name,
                EndpointUrl = endpoint.EndpointUrl ?? string.Empty,
                ChannelId = token?.ChannelId ?? 0,
                TokenId = token?.TokenId ?? 0,
                SecurityPolicyUri = endpoint.SecurityPolicyUri ?? string.Empty,
                SecurityMode = endpoint.SecurityMode.ToString()
            };
        }

        private static async ValueTask<ReplayCaptureSource> CreateReplaySourceAsync(
            string pcapPath,
            string keyLogPath,
            CancellationToken ct)
        {
            var source = new ReplayCaptureSource();
            try
            {
                await source.StartAsync(
                    new StartCaptureRequest
                    {
                        Source = CaptureSourceKind.Replay,
                        PcapFilePath = pcapPath,
                        KeyLogFilePath = keyLogPath
                    },
                    ct).ConfigureAwait(false);
                return source;
            }
            catch
            {
                await source.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static async ValueTask<IReadOnlyList<DecodedServiceCall>> DecodeServiceCallsAsync(
            ICaptureSource source,
            long? maxFrames,
            CancellationToken ct)
        {
            var reassembler = new ServiceCallReassembler();
            await foreach (ChannelKeyMaterial material in source.ReadKeyMaterialAsync(ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                reassembler.LoadKeyMaterial(material);
            }

            IReadOnlyList<DecodedServiceCall> calls = await reassembler
                .ProcessAllAsync(source.ReadCapturedFramesAsync(maxFrames, ct), ct)
                .ConfigureAwait(false);
            return calls;
        }

        private static ServiceCallSummary CreateSummary(IReadOnlyList<DecodedServiceCall> calls)
        {
            var perService = calls
                .GroupBy(static call => call.RequestName ?? "Unknown")
                .ToDictionary(static group => group.Key, CreateServiceCallStat, StringComparer.OrdinalIgnoreCase);
            List<double> latencies = [.. calls.Select(static call => call.Latency?.TotalMilliseconds ?? 0D)];
            int errors = calls.Count(IsError);

            return new ServiceCallSummary
            {
                TotalCalls = calls.Count,
                Errors = errors,
                AverageLatencyMs = Average(latencies),
                PerService = perService
            };
        }

        private static ServiceCallStat CreateServiceCallStat(IGrouping<string, DecodedServiceCall> group)
        {
            List<double> latencies = [.. group.Select(static call => call.Latency?.TotalMilliseconds ?? 0D)];
            latencies.Sort();

            return new ServiceCallStat
            {
                Count = latencies.Count,
                Errors = group.Count(IsError),
                AverageLatencyMs = Average(latencies),
                P95LatencyMs = Percentile(latencies, 0.95D)
            };
        }

        private static bool IsError(DecodedServiceCall call)
        {
            return call.ResponseStatus.HasValue && StatusCode.IsBad(call.ResponseStatus.Value);
        }

        private static double Average(List<double> values)
        {
            return values.Count == 0 ? 0D : values.Average();
        }

        private static double Percentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
            {
                return 0D;
            }

            int index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
            return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
        }

        private static FormatKind ParseDecodeFormat(string format)
        {
            if (!FormatKindExtensions.TryParse(format, out FormatKind kind) ||
                kind is not (FormatKind.ServiceTimeline or FormatKind.Json or FormatKind.Text))
            {
                throw new PcapDiagnosticsException(
                    $"Unsupported decode format '{format}'. Use service-timeline, json, or text.");
            }

            return kind;
        }

        private static string FormatDecodedCallsAsJson(IReadOnlyList<DecodedServiceCall> calls)
        {
            var builder = new StringBuilder();
            foreach (DecodedServiceCall call in calls)
            {
                builder.AppendLine(JsonSerializer.Serialize(call));
            }

            return builder.ToString();
        }

        private static string ResolveKeyLogPath(CaptureSession session, string format)
        {
            string normalized = format.Trim().ToLowerInvariant();
            if (normalized == "json")
            {
                return session.Source.GetKeyLogFilePath() ?? throw new PcapDiagnosticsException(
                    $"Capture session '{session.Id}' does not have a JSON keylog file.");
            }

            if (normalized == "text")
            {
                string path = Path.Combine(session.SessionFolder, kTextKeyLogFileName);
                if (File.Exists(path))
                {
                    return path;
                }

                throw new PcapDiagnosticsException(
                    $"Capture session '{session.Id}' does not have a text keylog file.");
            }

            throw new PcapDiagnosticsException($"Unsupported key format '{format}'. Use json or text.");
        }

        private static IList<ContentBlock> CreateText(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.LongLength > kMaxResponseBytes)
            {
                throw new PcapDiagnosticsException(
                    $"Decoded output is {bytes.LongLength} bytes, which exceeds the 10 MB MCP response cap.");
            }

            return
            [
                new TextContentBlock
                {
                    Text = text
                }
            ];
        }

        private static IList<ContentBlock> CreateFormattedBlocks(FormatResult result, ITraceFormatter formatter)
        {
            if (result.Bytes.LongLength > kMaxResponseBytes)
            {
                throw new PcapDiagnosticsException(
                    $"Decoded output is {result.Bytes.LongLength} bytes, which exceeds the 10 MB MCP response cap.");
            }

            if (!formatter.IsBinary)
            {
                return CreateText(Encoding.UTF8.GetString(result.Bytes));
            }

            return
            [
                new EmbeddedResourceBlock
                {
                    Resource = new BlobResourceContents
                    {
                        Uri = $"opcua-pcap://decode/{Guid.NewGuid():N}/{result.Kind.ToString().ToLowerInvariant()}",
                        MimeType = result.MimeType,
                        Blob = result.Bytes
                    }
                }
            ];
        }
    }

    /// <summary>
    /// Describes an active OPC UA secure channel in the MCP session pool.
    /// </summary>
    public sealed class ActiveChannelInfo
    {
        /// <summary>
        /// Gets the MCP session name.
        /// </summary>
        public string SessionName { get; init; } = string.Empty;

        /// <summary>
        /// Gets the remote endpoint URL.
        /// </summary>
        public string EndpointUrl { get; init; } = string.Empty;

        /// <summary>
        /// Gets the OPC UA secure channel id.
        /// </summary>
        public uint ChannelId { get; init; }

        /// <summary>
        /// Gets the OPC UA secure token id.
        /// </summary>
        public uint TokenId { get; init; }

        /// <summary>
        /// Gets the security policy URI.
        /// </summary>
        public string SecurityPolicyUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets the message security mode.
        /// </summary>
        public string SecurityMode { get; init; } = string.Empty;
    }

    /// <summary>
    /// Aggregate service-call statistics for a capture.
    /// </summary>
    public sealed class ServiceCallSummary
    {
        /// <summary>
        /// Gets the total number of decoded calls.
        /// </summary>
        public int TotalCalls { get; init; }

        /// <summary>
        /// Gets the number of decoded calls with bad response status.
        /// </summary>
        public int Errors { get; init; }

        /// <summary>
        /// Gets the average latency in milliseconds.
        /// </summary>
        public double AverageLatencyMs { get; init; }

        /// <summary>
        /// Gets per-service statistics.
        /// </summary>
        public Dictionary<string, ServiceCallStat> PerService { get; init; } = [];
    }

    /// <summary>
    /// Aggregate statistics for one service name.
    /// </summary>
    public sealed class ServiceCallStat
    {
        /// <summary>
        /// Gets the call count.
        /// </summary>
        public int Count { get; init; }

        /// <summary>
        /// Gets the number of calls with bad response status.
        /// </summary>
        public int Errors { get; init; }

        /// <summary>
        /// Gets the average latency in milliseconds.
        /// </summary>
        public double AverageLatencyMs { get; init; }

        /// <summary>
        /// Gets the p95 latency in milliseconds.
        /// </summary>
        public double P95LatencyMs { get; init; }
    }
}
