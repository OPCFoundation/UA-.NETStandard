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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Pcap.Capture;
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

            m_logger.LogDebug("Formatting capture as libpcap.");
            string? path = source.GetRawPcapFilePath();
            if (path is null || !File.Exists(path))
            {
                throw new PcapDiagnosticsException(
                    "Source does not produce a libpcap file. Try format=json|csv|text|service-timeline.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return new FormatResult
            {
                Kind = Kind,
                MimeType = MimeType,
                Bytes = bytes,
                FramesFormatted = source.FrameCount
            };
        }
    }
}
