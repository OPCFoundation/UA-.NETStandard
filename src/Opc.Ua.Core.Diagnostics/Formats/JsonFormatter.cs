// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    /// Formats captured frames as JSON.
    /// </summary>
    public sealed class JsonFormatter : ITraceFormatter
    {
        private readonly ILogger m_logger;

        /// <summary>
        /// Constructs a JSON formatter.
        /// </summary>
        public JsonFormatter(ILogger? logger = null)
        {
            m_logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc/>
        public FormatKind Kind => FormatKind.Json;

        /// <inheritdoc/>
        public string MimeType => "application/json";

        /// <inheritdoc/>
        public bool IsBinary => false;

        /// <inheritdoc/>
        public async ValueTask<FormatResult> FormatAsync(
            ICaptureSource source,
            long? maxFrames,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(source);

            m_logger.FormattingCaptureAsJson();
            List<FrameJsonDto> frames = [];
            await foreach (CaptureFrame frame in source.ReadCapturedFramesAsync(maxFrames, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                frames.Add(FrameJsonDto.FromFrame(frame));
            }

            byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                frames,
                FrameJsonSerializerContext.Default.ListFrameJsonDto);
            return new FormatResult
            {
                Kind = Kind,
                MimeType = MimeType,
                Bytes = bytes,
                FramesFormatted = frames.Count
            };
        }
    }


    internal sealed record FrameJsonDto(
        string Timestamp,
        string Direction,
        string Client,
        string Server,
        int Length,
        string? MessageType,
        uint? ChannelId,
        uint? TokenId)
    {
        public static FrameJsonDto FromFrame(CaptureFrame frame)
        {
            return new FrameJsonDto(
                FrameFormatHelpers.FormatTimestamp(frame.Timestamp),
                frame.Direction.ToString(),
                frame.ClientEndpoint,
                frame.ServerEndpoint,
                frame.Data.Length,
                FrameFormatHelpers.GetMessageType(frame.Data.Span),
                FrameFormatHelpers.GetChannelId(frame.Data.Span),
                FrameFormatHelpers.GetTokenId(frame.Data.Span));
        }
    }

    [JsonSerializable(typeof(List<FrameJsonDto>))]
    internal sealed partial class FrameJsonSerializerContext : JsonSerializerContext;

    /// <summary>
    /// Source-generated log messages for <see cref="JsonFormatter"/>.
    /// </summary>
    internal static partial class JsonFormatterLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.JsonFormatter + 0, Level = LogLevel.Debug,
            Message = "Formatting capture as JSON.")]
        public static partial void FormattingCaptureAsJson(this ILogger logger);
    }

}
