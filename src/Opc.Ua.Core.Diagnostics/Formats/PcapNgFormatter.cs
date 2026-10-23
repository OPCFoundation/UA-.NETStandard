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
using System.IO;
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
    /// Rewrites captured frames as a pcapng byte stream.
    /// </summary>
    public sealed class PcapNgFormatter : ITraceFormatter
    {
        private readonly ILogger m_logger;

        /// <summary>
        /// Constructs a pcapng formatter.
        /// </summary>
        public PcapNgFormatter(ILogger? logger = null)
        {
            m_logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc/>
        public FormatKind Kind => FormatKind.PcapNg;

        /// <inheritdoc/>
        public string MimeType => "application/x-pcapng";

        /// <inheritdoc/>
        public bool IsBinary => true;

        /// <inheritdoc/>
        public async ValueTask<FormatResult> FormatAsync(
            ICaptureSource source,
            long? maxFrames,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (source.GetRawPcapFilePath() is null && !source.SupportedFormats.Contains(FormatKind.PcapNg))
            {
                throw new PcapDiagnosticsException(
                    "Source does not produce link-layer frames; pcapng is not applicable. " +
                    "Use json|csv|text|service-timeline.");
            }

            m_logger.FormattingCaptureAsPcapng();
            string? path = source.GetRawPcapFilePath();
            if (path is not null)
            {
                CaptureFileValidator.RejectKeyLogPath(path, source.GetKeyLogFilePath());
                (FormatKind kind, _) = await CaptureFileValidator.ValidateFileAsync(path, ct)
                    .ConfigureAwait(false);
                if (kind == FormatKind.PcapNg)
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                    using MemoryStream rawStream = new(bytes, writable: false);
                    (FormatKind snapshotKind, long frameCount) = await CaptureFileValidator.ValidateAsync(rawStream, ct)
                        .ConfigureAwait(false);
                    if (snapshotKind != FormatKind.PcapNg)
                    {
                        throw new PcapDiagnosticsException("The capture format changed during export.");
                    }
                    if (maxFrames is long frameLimit && frameCount > Math.Max(0, frameLimit))
                    {
                        bytes = LimitFrames(bytes, frameLimit);
                        using MemoryStream limitedStream = new(bytes, writable: false);
                        (FormatKind limitedKind, long limitedFrameCount) = await CaptureFileValidator.ValidateAsync(
                            limitedStream, ct).ConfigureAwait(false);
                        if (limitedKind != FormatKind.PcapNg)
                        {
                            throw new PcapDiagnosticsException("The limited capture is not valid PCAPNG.");
                        }
                        frameCount = limitedFrameCount;
                    }

                    return new FormatResult
                    {
                        Kind = Kind,
                        MimeType = MimeType,
                        Bytes = bytes,
                        FramesFormatted = frameCount
                    };
                }

            }

            MemoryStream stream = new();
            PcapNgFileWriter writer = new(stream, PcapFileWriter.LinkTypeNull);
            long count = 0;
            try
            {
                await foreach (CaptureFrame frame in source.ReadCapturedFramesAsync(maxFrames, ct)
                    .WithCancellation(ct)
                    .ConfigureAwait(false))
                {
                    await writer.WriteAsync(frame.Timestamp, frame.Data, ct).ConfigureAwait(false);
                    count++;
                }

            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            return new FormatResult
            {
                Kind = Kind,
                MimeType = MimeType,
                Bytes = stream.ToArray(),
                FramesFormatted = count
            };
        }

        private static byte[] LimitFrames(byte[] bytes, long maxFrames)
        {
            long limit = Math.Max(0, maxFrames);
            long frameCount = 0;
            int sectionStart = -1;
            int offset = 0;
            bool littleEndian = true;
            while (offset < bytes.Length)
            {
                ReadOnlySpan<byte> block = bytes.AsSpan(offset);
                bool sectionHeader = BinaryPrimitives.ReadUInt32LittleEndian(block) == 0x0A0D0D0A;
                if (sectionHeader)
                {
                    uint byteOrderMagic = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(8, 4));
                    littleEndian = byteOrderMagic switch
                    {
                        0x1A2B3C4D => true,
                        0x4D3C2B1A => false,
                        _ => throw new PcapDiagnosticsException("Invalid PCAPNG byte-order magic.")
                    };
                    sectionStart = offset;
                }

                uint blockType = littleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(block)
                    : BinaryPrimitives.ReadUInt32BigEndian(block);
                uint blockLength = littleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(4, 4))
                    : BinaryPrimitives.ReadUInt32BigEndian(block.Slice(4, 4));
                bool packetBlock = blockType is 2 or 3 or 6;
                if (packetBlock && frameCount >= limit)
                {
                    if (sectionStart < 0)
                    {
                        throw new PcapDiagnosticsException("The PCAPNG section header is missing.");
                    }
                    byte[] limitedBytes = bytes.AsSpan(0, offset).ToArray();
                    limitedBytes.AsSpan(sectionStart + 16, 8).Fill(byte.MaxValue);
                    return limitedBytes;
                }
                if (packetBlock)
                {
                    frameCount++;
                }
                offset += checked((int)blockLength);
            }

            return bytes;
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PcapNgFormatter"/>.
    /// </summary>
    internal static partial class PcapNgFormatterLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.PcapNgFormatter + 0, Level = LogLevel.Debug,
            Message = "Formatting capture as pcapng.")]
        public static partial void FormattingCaptureAsPcapng(this ILogger logger);
    }

}
