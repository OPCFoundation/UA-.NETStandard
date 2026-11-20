// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
