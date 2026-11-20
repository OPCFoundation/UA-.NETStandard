// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Formats
{
    /// <summary>
    /// Formats the contents of a completed capture session into a single
    /// output blob (pcap, pcapng, json, csv, text, service-timeline).
    /// </summary>
    public interface ITraceFormatter
    {
        /// <summary>
        /// The format this implementation produces.
        /// </summary>
        FormatKind Kind { get; }

        /// <summary>
        /// MIME type to use when surfacing the bytes as an MCP resource.
        /// </summary>
        string MimeType { get; }

        /// <summary>
        /// Whether the produced bytes are binary (true) or text (false).
        /// Binary formats are returned as base64 MCP resources; text
        /// formats are returned inline.
        /// </summary>
        bool IsBinary { get; }

        /// <summary>
        /// Format the session into a single byte stream.
        /// </summary>
        ValueTask<FormatResult> FormatAsync(
            ICaptureSource source,
            long? maxFrames,
            CancellationToken ct);
    }

    /// <summary>
    /// Result of a single formatting operation.
    /// </summary>
    public sealed class FormatResult
    {
        /// <summary>
        /// The format produced.
        /// </summary>
        public required FormatKind Kind { get; init; }

        /// <summary>
        /// The MIME type.
        /// </summary>
        public required string MimeType { get; init; }

        /// <summary>
        /// The output bytes.
        /// </summary>
        public required byte[] Bytes { get; init; }

        /// <summary>
        /// Number of frames included in the output.
        /// </summary>
        public long FramesFormatted { get; init; }
    }

    /// <summary>
    /// Convenience helpers for <see cref="FormatKind"/>.
    /// </summary>
    public static class FormatKindExtensions
    {
        /// <summary>
        /// Attempts to parse a format name (case-insensitive). Accepts
        /// the canonical lower-case names: <c>pcap</c>, <c>pcapng</c>,
        /// <c>json</c>, <c>csv</c>, <c>text</c>, <c>service-timeline</c>.
        /// </summary>
        public static bool TryParse(this string? value, out FormatKind kind)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "pcap":
                    kind = FormatKind.Pcap;
                    return true;
                case "pcapng":
                    kind = FormatKind.PcapNg;
                    return true;
                case "json":
                    kind = FormatKind.Json;
                    return true;
                case "csv":
                    kind = FormatKind.Csv;
                    return true;
                case "text":
                    kind = FormatKind.Text;
                    return true;
                case "service-timeline":
                case "servicetimeline":
                case "timeline":
                    kind = FormatKind.ServiceTimeline;
                    return true;
                default:
                    kind = FormatKind.Pcap;
                    return false;
            }
        }

        /// <summary>
        /// Returns the canonical wire name for an output format.
        /// </summary>
        public static string ToWireName(this FormatKind kind)
        {
            return kind switch
            {
                FormatKind.Pcap => "pcap",
                FormatKind.PcapNg => "pcapng",
                FormatKind.Json => "json",
                FormatKind.Csv => "csv",
                FormatKind.Text => "text",
                FormatKind.ServiceTimeline => "service-timeline",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown output format.")
            };
        }

        /// <summary>
        /// The canonical format names used on the wire.
        /// </summary>
        public const string SupportedNames = "pcap, pcapng, json, csv, text, or service-timeline";
    }
}
