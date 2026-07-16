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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Formats PubSub dissection results as JSON.
    /// </summary>
    public sealed class PubSubJsonFormatter
    {
        /// <summary>
        /// MIME type produced by the formatter.
        /// </summary>
        public string MimeType => "application/json";

        /// <summary>
        /// Formats captured frames as a JSON array of dissection results.
        /// </summary>
        /// <param name="frames">Captured frames.</param>
        /// <param name="dissector">Offline dissector.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>UTF-8 JSON bytes.</returns>
        public async ValueTask<byte[]> FormatAsync(
            IAsyncEnumerable<PubSubCaptureFrame> frames,
            PubSubOfflineDissector? dissector = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            dissector ??= new PubSubOfflineDissector();
            List<PubSubDissectionJsonDto> results = [];
            await foreach (PubSubCaptureFrame frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                PubSubDissectionResult result = await dissector.DissectAsync(frame, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(PubSubDissectionJsonDto.FromResult(result));
            }
            return JsonSerializer.SerializeToUtf8Bytes(
                results,
                PubSubJsonSerializerContext.Default.ListPubSubDissectionJsonDto);
        }
    }

    internal sealed record PubSubDissectionJsonDto(
        string Timestamp,
        string Direction,
        string TransportProfileUri,
        string? Endpoint,
        string? Topic,
        int PayloadLength,
        string MessageType,
        string SecurityState,
        string PublisherId,
        ushort? WriterGroupId,
        IReadOnlyList<ushort> DataSetWriterIds,
        uint? SecurityTokenId,
        bool IsDecoded,
        bool IsUndecodable,
        string? DiagnosticMessage,
        IReadOnlyList<PubSubDataSetJsonDto> DataSets)
    {
        public static PubSubDissectionJsonDto FromResult(PubSubDissectionResult result)
        {
            List<ushort> writerIds = [];
            foreach (ushort writerId in result.DataSetWriterIds)
            {
                writerIds.Add(writerId);
            }
            List<PubSubDataSetJsonDto> dataSets = [];
            foreach (PubSubDissectedDataSet dataSet in result.DataSets)
            {
                dataSets.Add(PubSubDataSetJsonDto.FromDataSet(dataSet));
            }
            return new PubSubDissectionJsonDto(
                result.Timestamp.ToString("O"),
                result.Direction.ToString(),
                result.TransportProfileUri,
                result.Endpoint,
                result.Topic,
                result.PayloadLength,
                result.MessageType.ToString(),
                result.SecurityState.ToString(),
                result.PublisherId.ToString(),
                result.WriterGroupId,
                writerIds,
                result.SecurityTokenId,
                result.IsDecoded,
                result.IsUndecodable,
                result.DiagnosticMessage,
                dataSets);
        }
    }

    internal sealed record PubSubDataSetJsonDto(
        ushort DataSetWriterId,
        uint SequenceNumber,
        string MessageType,
        string Status,
        IReadOnlyList<PubSubFieldJsonDto> Fields)
    {
        public static PubSubDataSetJsonDto FromDataSet(PubSubDissectedDataSet dataSet)
        {
            List<PubSubFieldJsonDto> fields = [];
            foreach (PubSubDissectedField field in dataSet.Fields)
            {
                fields.Add(PubSubFieldJsonDto.FromField(field));
            }
            return new PubSubDataSetJsonDto(
                dataSet.DataSetWriterId,
                dataSet.SequenceNumber,
                dataSet.MessageType.ToString(),
                dataSet.Status.ToString(),
                fields);
        }
    }

    internal sealed record PubSubFieldJsonDto(
        string Name,
        string Value,
        string StatusCode,
        string Encoding)
    {
        public static PubSubFieldJsonDto FromField(PubSubDissectedField field)
        {
            return new PubSubFieldJsonDto(
                field.Name,
                field.Value.ToString(),
                field.StatusCode.ToString(),
                field.Encoding.ToString());
        }
    }

    [JsonSerializable(typeof(List<PubSubDissectionJsonDto>))]
    internal sealed partial class PubSubJsonSerializerContext : JsonSerializerContext;
}
