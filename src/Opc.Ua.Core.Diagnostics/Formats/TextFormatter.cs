// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
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
    /// Formats captured frames as human-readable text.
    /// </summary>
    public sealed class TextFormatter : ITraceFormatter
    {
        private readonly ILogger m_logger;

        /// <summary>
        /// Constructs a text formatter.
        /// </summary>
        public TextFormatter(ILogger? logger = null)
        {
            m_logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc/>
        public FormatKind Kind => FormatKind.Text;

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

            m_logger.FormattingCaptureAsText();
            StringBuilder builder = new();
            long count = 0;
            await foreach (CaptureFrame frame in source.ReadCapturedFramesAsync(maxFrames, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                ReadOnlySpan<byte> data = frame.Data.Span;
                string messageType = FrameFormatHelpers.GetMessageType(data) ?? "-";
                string channelId = FrameFormatHelpers.GetChannelId(data)?.ToString(CultureInfo.InvariantCulture) ?? "-";
                string tokenId = FrameFormatHelpers.GetTokenId(data)?.ToString(CultureInfo.InvariantCulture) ?? "-";

                builder.Append(FrameFormatHelpers.FormatTimestamp(frame.Timestamp))
                    .Append(' ')
                    .Append(FrameFormatHelpers.FormatDirection(frame.Direction))
                    .Append(' ')
                    .Append(frame.Data.Length.ToString(CultureInfo.InvariantCulture))
                    .Append("B ")
                    .Append(messageType)
                    .Append(" channel=")
                    .Append(channelId)
                    .Append(" token=")
                    .AppendLine(tokenId);
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

    }

    /// <summary>
    /// Source-generated log messages for <see cref="TextFormatter"/>.
    /// </summary>
    internal static partial class TextFormatterLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.TextFormatter + 0, Level = LogLevel.Debug,
            Message = "Formatting capture as text.")]
        public static partial void FormattingCaptureAsText(this ILogger logger);
    }

}
