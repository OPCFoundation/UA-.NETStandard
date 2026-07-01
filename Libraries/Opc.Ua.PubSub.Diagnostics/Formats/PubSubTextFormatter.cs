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

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Formats PubSub dissection results as a human-readable timeline.
    /// </summary>
    public sealed class PubSubTextFormatter
    {
        /// <summary>
        /// MIME type produced by the formatter.
        /// </summary>
        public string MimeType => "text/plain";

        /// <summary>
        /// Formats captured frames as a text timeline.
        /// </summary>
        /// <param name="frames">Captured frames.</param>
        /// <param name="dissector">Offline dissector.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Text output.</returns>
        public async ValueTask<string> FormatAsync(
            IAsyncEnumerable<PubSubCaptureFrame> frames,
            PubSubOfflineDissector? dissector = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            dissector ??= new PubSubOfflineDissector();
            StringBuilder builder = new();
            await foreach (PubSubCaptureFrame frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                PubSubDissectionResult result = await dissector.DissectAsync(frame, cancellationToken)
                    .ConfigureAwait(false);
                AppendResult(builder, result);
            }
            return builder.ToString();
        }

        private static void AppendResult(StringBuilder builder, PubSubDissectionResult result)
        {
            _ = builder.Append(result.Timestamp.ToString("O"))
                .Append(' ')
                .Append(result.Direction)
                .Append(' ')
                .Append(result.MessageType)
                .Append(' ')
                .Append(result.SecurityState)
                .Append(" publisher=")
                .Append(result.PublisherId)
                .Append(" writerGroup=")
                .Append(result.WriterGroupId?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" endpoint=")
                .Append(result.Endpoint ?? result.Topic ?? "-")
                .Append(" bytes=")
                .Append(result.PayloadLength);
            if (!string.IsNullOrEmpty(result.DiagnosticMessage))
            {
                _ = builder.Append(" note=\"").Append(result.DiagnosticMessage).Append('"');
            }
            _ = builder.AppendLine();

            foreach (PubSubDissectedDataSet dataSet in result.DataSets)
            {
                _ = builder.Append("  DataSetWriterId=")
                    .Append(dataSet.DataSetWriterId)
                    .Append(" sequence=")
                    .Append(dataSet.SequenceNumber)
                    .Append(" type=")
                    .Append(dataSet.MessageType)
                    .AppendLine();
                foreach (PubSubDissectedField field in dataSet.Fields)
                {
                    _ = builder.Append("    ")
                        .Append(string.IsNullOrEmpty(field.Name) ? "<unnamed>" : field.Name)
                        .Append(" = ")
                        .Append(field.Value)
                        .Append(" [")
                        .Append(field.StatusCode)
                        .Append(']')
                        .AppendLine();
                }
            }
        }
    }
}
