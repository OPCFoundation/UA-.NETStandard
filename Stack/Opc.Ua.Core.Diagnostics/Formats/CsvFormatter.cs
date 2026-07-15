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
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Formats
{
    /// <summary>
    /// Formats captured frames as CSV.
    /// </summary>
    public sealed class CsvFormatter : ITraceFormatter
    {
        private readonly ILogger m_logger;

        /// <summary>
        /// Constructs a CSV formatter.
        /// </summary>
        public CsvFormatter(ILogger? logger = null)
        {
            m_logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc/>
        public FormatKind Kind => FormatKind.Csv;

        /// <inheritdoc/>
        public string MimeType => "text/csv";

        /// <inheritdoc/>
        public bool IsBinary => false;

        /// <inheritdoc/>
        public async ValueTask<FormatResult> FormatAsync(
            ICaptureSource source,
            long? maxFrames,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(source);

            m_logger.FormattingCaptureAsCsv();
            StringBuilder builder = new();
            builder.AppendLine("timestamp,direction,client,server,length,messageType,channelId,tokenId");
            long count = 0;
            await foreach (CaptureFrame frame in source.ReadCapturedFramesAsync(maxFrames, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                ReadOnlySpan<byte> data = frame.Data.Span;
                AppendCsv(builder, FrameFormatHelpers.FormatTimestamp(frame.Timestamp));
                builder.Append(',');
                AppendCsv(builder, frame.Direction.ToString());
                builder.Append(',');
                AppendCsv(builder, frame.ClientEndpoint);
                builder.Append(',');
                AppendCsv(builder, frame.ServerEndpoint);
                builder.Append(',')
                    .Append(frame.Data.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(',');
                AppendCsv(builder, FrameFormatHelpers.GetMessageType(data) ?? string.Empty);
                builder.Append(',')
                    .Append(FrameFormatHelpers.GetChannelId(data)?.ToString(CultureInfo.InvariantCulture))
                    .Append(',')
                    .AppendLine(FrameFormatHelpers.GetTokenId(data)?.ToString(CultureInfo.InvariantCulture));
                count++;
            }

            return new FormatResult
            {
                Kind = Kind,
                MimeType = MimeType,
                Bytes = Encoding.UTF8.GetBytes(builder.ToString()),
                FramesFormatted = count
            };
        }

        private static void AppendCsv(StringBuilder builder, string value)
        {
            if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');
            foreach (char ch in value)
            {
                if (ch == '"')
                {
                    builder.Append("\"\"");
                }
                else
                {
                    builder.Append(ch);
                }

            }
            builder.Append('"');
        }
    }

    internal static class FrameFormatHelpers
    {
        public static string FormatTimestamp(DateTimeOffset timestamp)
        {
            return timestamp
                .ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        public static string? GetMessageType(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
            {
                return null;
            }

            return Encoding.ASCII.GetString(data[..4]);
        }

        public static uint? GetChannelId(ReadOnlySpan<byte> data)
        {
            return data.Length >= 16 ? BinaryPrimitives.ReadUInt32LittleEndian(data[8..12]) : null;
        }

        public static uint? GetTokenId(ReadOnlySpan<byte> data)
        {
            return data.Length >= 16 ? BinaryPrimitives.ReadUInt32LittleEndian(data[12..16]) : null;
        }

        public static string FormatDirection(CaptureFrameDirection direction)
        {
            return direction switch
            {
                CaptureFrameDirection.ClientToServer => "C->S",
                CaptureFrameDirection.ServerToClient => "S->C",
                _ => "?"
            };
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="CsvFormatter"/>.
    /// </summary>
    internal static partial class CsvFormatterLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.CsvFormatter + 0, Level = LogLevel.Debug,
            Message = "Formatting capture as CSV.")]
        public static partial void FormattingCaptureAsCsv(this ILogger logger);
    }

}
