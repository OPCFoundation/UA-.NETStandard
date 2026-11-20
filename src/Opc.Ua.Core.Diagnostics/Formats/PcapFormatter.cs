/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
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
    /// Pass-through formatter for sources that already produce a libpcap file.
    /// </summary>
    public sealed class PcapFormatter : ITraceFormatter
    {
        private readonly ILogger m_logger;

        /// <summary>
        /// Constructs a libpcap formatter.
        /// </summary>
        public PcapFormatter(ILogger? logger = null)
        {
            m_logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc/>
        public FormatKind Kind => FormatKind.Pcap;

        /// <inheritdoc/>
        public string MimeType => "application/vnd.tcpdump.pcap";

        /// <inheritdoc/>
        public bool IsBinary => true;

        /// <inheritdoc/>
        public async ValueTask<FormatResult> FormatAsync(
            ICaptureSource source,
            long? maxFrames,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(source);
            _ = maxFrames;

            m_logger.FormattingCaptureAsLibpcap();
            string? path = source.GetRawPcapFilePath();
            if (path is null || !File.Exists(path))
            {
                throw new PcapDiagnosticsException(
                    "Source does not produce a libpcap file. Try format=json|csv|text|service-timeline.");
            }

            CaptureFileValidator.RejectKeyLogPath(path, source.GetKeyLogFilePath());
            byte[] bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            using MemoryStream stream = new(bytes, writable: false);
            (FormatKind kind, long frameCount) = await CaptureFileValidator.ValidateAsync(stream, ct)
                .ConfigureAwait(false);
            return new FormatResult
            {
                Kind = kind,
                MimeType = kind == FormatKind.PcapNg ? "application/x-pcapng" : MimeType,
                Bytes = bytes,
                FramesFormatted = frameCount
            };
        }

    }

    /// <summary>
    /// Source-generated log messages for <see cref="PcapFormatter"/>.
    /// </summary>
    internal static partial class PcapFormatterLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.PcapFormatter + 0, Level = LogLevel.Debug,
            Message = "Formatting capture as libpcap.")]
        public static partial void FormattingCaptureAsLibpcap(this ILogger logger);
    }

}
