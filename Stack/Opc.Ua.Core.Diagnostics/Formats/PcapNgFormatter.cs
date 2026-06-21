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
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.Models;

using Opc.Ua.Bindings;

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

            m_logger.LogDebug("Formatting capture as pcapng.");
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
    }
}
