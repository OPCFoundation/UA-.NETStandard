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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Dissection;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Formats
{
    /// <summary>
    /// Formats decoded OPC UA service calls as a text timeline.
    /// </summary>
    public sealed class ServiceTimelineFormatter : ITraceFormatter
    {
        private readonly ILogger m_logger = NullLogger.Instance;

        /// <summary>
        /// Constructs a service-timeline formatter.
        /// </summary>
        public ServiceTimelineFormatter()
        {
        }

        /// <inheritdoc/>
        public FormatKind Kind => FormatKind.ServiceTimeline;

        /// <inheritdoc/>
        public string MimeType => "text/plain";

        /// <inheritdoc/>
        public bool IsBinary => false;

        /// <inheritdoc/>
        public async ValueTask<FormatResult> FormatAsync(
            ICaptureSource source,
            long? maxFrames,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(source);

            List<ChannelKeyMaterial> keyMaterials = [];
            await foreach (ChannelKeyMaterial material in source.ReadKeyMaterialAsync(ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                keyMaterials.Add(material);
            }

            if (keyMaterials.Count == 0)
            {
                throw new PcapDiagnosticsException(
                    "Service timeline requires both captured frames and key material from the source.");
            }
            m_logger.FormattingCaptureAsServiceTimeline();
            m_logger.FormattingCaptureAsServiceTimeline();
            var reassembler = new ServiceCallReassembler();
            foreach (ChannelKeyMaterial material in keyMaterials)
            {
                reassembler.LoadKeyMaterial(material);
            }

            long frameCount = 0;
            await foreach (CaptureFrame frame in source.ReadCapturedFramesAsync(maxFrames, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                reassembler.Push(frame);
                frameCount++;
            }

            if (frameCount == 0)
            {
                throw new PcapDiagnosticsException(
                    "Service timeline requires both captured frames and key material from the source.");
            }

            IReadOnlyList<DecodedServiceCall> calls = reassembler.DrainCompleted();
            return new FormatResult
            {
                Kind = Kind,
                MimeType = MimeType,
                Bytes = Encoding.UTF8.GetBytes(FormatCalls(calls)),
                FramesFormatted = frameCount
            };
        }

        private static string FormatCalls(IReadOnlyList<DecodedServiceCall> calls)
        {
            List<TimelineRow> rows = [];
            foreach (DecodedServiceCall call in calls)
            {
                rows.Add(TimelineRow.CreateRequest(call));
                if (call.ResponseTimestamp.HasValue)
                {
                    rows.Add(TimelineRow.CreateResponse(call));
                }

            }

            rows.Sort(static (left, right) => left.Timestamp.CompareTo(right.Timestamp));

            StringBuilder builder = new();
            builder.AppendLine(
                "Timestamp                     Channel  Token  Request   Service                       " +
                "Status        Latency    Summary");
            foreach (TimelineRow row in rows)
            {
                builder.Append(FrameFormatHelpers.FormatTimestamp(row.Timestamp).PadRight(30))
                    .Append(row.Channel.PadRight(9))
                    .Append(row.Token.PadRight(7))
                    .Append(row.Request.PadRight(10))
                    .Append(row.Service.PadRight(30))
                    .Append(row.Status.PadRight(14))
                    .Append(row.Latency.PadRight(11))
                    .AppendLine(row.Summary);
            }

            return builder.ToString();
        }

        private sealed record TimelineRow(
            DateTimeOffset Timestamp,
            string Channel,
            string Token,
            string Request,
            string Service,
            string Status,
            string Latency,
            string Summary)
        {
            public static TimelineRow CreateRequest(DecodedServiceCall call)
            {
                return new TimelineRow(
                    call.RequestTimestamp,
                    FormatChannel(call.ChannelId),
                    call.TokenId.ToString(CultureInfo.InvariantCulture),
                    call.RequestId.ToString(CultureInfo.InvariantCulture),
                    call.RequestName ?? "-",
                    "-",
                    "-",
                    call.RequestSummary ?? string.Empty);
            }

            public static TimelineRow CreateResponse(DecodedServiceCall call)
            {
                return new TimelineRow(
                    call.ResponseTimestamp.GetValueOrDefault(),
                    FormatChannel(call.ChannelId),
                    call.TokenId.ToString(CultureInfo.InvariantCulture),
                    call.RequestId.ToString(CultureInfo.InvariantCulture),
                    call.ResponseName ?? "-",
                    call.ResponseStatus?.ToString() ?? "-",
                    FormatLatency(call.Latency),
                    call.ResponseSummary ?? string.Empty);
            }

            private static string FormatChannel(uint channelId)
            {
                return "0x" + channelId.ToString("X4", CultureInfo.InvariantCulture);
            }

            private static string FormatLatency(TimeSpan? latency)
            {
                if (!latency.HasValue)
                {
                    return "-";
                }

                return latency.Value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms";
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ServiceTimelineFormatter"/>.
    /// </summary>
    internal static partial class ServiceTimelineFormatterLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.ServiceTimelineFormatter + 0, Level = LogLevel.Debug,
            Message = "Formatting capture as service timeline.")]
        public static partial void FormattingCaptureAsServiceTimeline(this ILogger logger);
    }

}
