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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;
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
    }
}
