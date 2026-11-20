// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
