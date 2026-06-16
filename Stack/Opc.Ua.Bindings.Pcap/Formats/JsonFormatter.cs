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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Formats
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

            m_logger.LogDebug("Formatting capture as JSON.");
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
}
