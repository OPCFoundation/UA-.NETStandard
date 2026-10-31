/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Well-known identifiers of the Arrow DataEncoding companion namespace - the Part 14
    /// configuration model additions for the Arrow message mapping (§6.5).
    /// </summary>
    /// <remarks>
    /// NodeIds are provisional; the OPC Foundation assigns the final identifiers when the namespace
    /// is registered. Configuration only: no DataTypeEncoding Object is added for
    /// <c>Default Arrow</c>.
    /// </remarks>
    public static class ArrowWellKnown
    {
        /// <summary>The Arrow DataEncoding companion namespace URI.</summary>
        public const string ArrowNamespaceUri = "http://opcfoundation.org/UA/Arrow/";

        /// <summary>Provisional NodeId of the <c>ArrowIpcFormatEnum</c> DataType.</summary>
        public const uint ArrowIpcFormatEnum = 65000;

        /// <summary>Provisional NodeId of the <c>ArrowDeltaFrameModeEnum</c> DataType.</summary>
        public const uint ArrowDeltaFrameModeEnum = 65001;

        /// <summary>Provisional NodeId of the <c>ArrowCompressionEnum</c> DataType.</summary>
        public const uint ArrowCompressionEnum = 65002;

        /// <summary>Provisional NodeId of the <c>ArrowWriterGroupMessageDataType</c> DataType.</summary>
        public const uint ArrowWriterGroupMessageDataType = 65010;

        /// <summary>Provisional NodeId of the <c>ArrowDataSetWriterMessageDataType</c> DataType.</summary>
        public const uint ArrowDataSetWriterMessageDataType = 65011;

        /// <summary>Provisional NodeId of the <c>ArrowDataSetReaderMessageDataType</c> DataType.</summary>
        public const uint ArrowDataSetReaderMessageDataType = 65012;

        /// <summary>Provisional NodeId of the <c>ArrowWriterGroupMessageType</c> ObjectType.</summary>
        public const uint ArrowWriterGroupMessageType = 65020;

        /// <summary>Provisional NodeId of the <c>ArrowDataSetWriterMessageType</c> ObjectType.</summary>
        public const uint ArrowDataSetWriterMessageType = 65021;

        /// <summary>Provisional NodeId of the <c>ArrowDataSetReaderMessageType</c> ObjectType.</summary>
        public const uint ArrowDataSetReaderMessageType = 65022;
    }

    /// <summary>
    /// The Arrow IPC payload framing as numbered by the configuration model (§6.5).
    /// </summary>
    /// <remarks>
    /// These are framing options over the identical canonical column layout, not encoding variants.
    /// <para>
    /// The numbering here is the specification's and deliberately differs from the internal
    /// <c>ArrowIpcFraming</c> enum, whose members are ordered differently. The two must therefore
    /// always be converted with <c>ArrowMessageSettings.ToFraming</c> and never by casting the
    /// underlying integer, which would silently select the wrong framing.
    /// </para>
    /// </remarks>
    public enum ArrowIpcFormat
    {
        /// <summary>A bare RecordBatch; the schema is resolved out of band by SchemaId. Default.</summary>
        Batch = 0,

        /// <summary>A self-contained Arrow IPC stream that embeds the Schema message.</summary>
        Stream = 1,

        /// <summary>An Arrow IPC file: the stream contents plus a random-access footer index.</summary>
        File = 2
    }

    /// <summary>
    /// How a delta or sparse frame is represented (§6.2).
    /// </summary>
    public enum ArrowDeltaFrameMode
    {
        /// <summary>
        /// Keep the full column set and mark absent keys as null cells, so sparse and full frames
        /// share one schema and therefore one SchemaId. Default.
        /// </summary>
        NullableColumns = 0,

        /// <summary>
        /// Drop unchanged columns. This changes the column set and therefore the SchemaId, so it is
        /// the explicit schema-changing option.
        /// </summary>
        SelectedColumns = 1
    }

    /// <summary>
    /// The Arrow IPC body compression codec (§6.1).
    /// </summary>
    public enum ArrowCompression
    {
        /// <summary>No compression.</summary>
        None = 0,

        /// <summary>LZ4 frame compression.</summary>
        Lz4Frame = 1,

        /// <summary>Zstandard compression.</summary>
        Zstd = 2
    }

    /// <summary>
    /// WriterGroup MessageSettings for the Arrow message mapping (§6.5), carrying the mapping
    /// parameters of §6.1.
    /// </summary>
    public sealed record ArrowWriterGroupMessageSettings
    {
        /// <summary>Payload framing (Table 6.2-1).</summary>
        public ArrowIpcFormat ArrowIpcFormat { get; init; } = ArrowIpcFormat.Batch;

        /// <summary>Batching target for rows per RecordBatch.</summary>
        public uint MaxRowsPerRecordBatch { get; init; }

        /// <summary>Whether NetworkMessage header fields are carried as schema metadata (§6.6).</summary>
        public bool IncludeSchemaMetadata { get; init; }

        /// <summary>Delta and sparse frame representation (§6.2).</summary>
        public ArrowDeltaFrameMode DeltaFrameMode { get; init; } = ArrowDeltaFrameMode.NullableColumns;

        /// <summary>Arrow IPC body compression codec.</summary>
        public ArrowCompression Compression { get; init; } = ArrowCompression.None;

        /// <summary>
        /// Optional URI of the schema registry or catalog serving this group's schemas.
        /// </summary>
        public string? ArrowSchemaUri { get; init; }
    }

    /// <summary>
    /// DataSetWriter MessageSettings for the Arrow message mapping (§6.5).
    /// </summary>
    public sealed record ArrowDataSetWriterMessageSettings
    {
        /// <summary>
        /// SchemaId of the DataSet schema currently published by this writer (§4.3), in hexadecimal.
        /// </summary>
        public string? ArrowSchemaId { get; init; }

        /// <summary>
        /// Selects the RawData, Variant or DataValue column representation (§6.2).
        /// </summary>
        public DataSetFieldContentMask DataSetFieldContentMask { get; init; }
    }

    /// <summary>
    /// DataSetReader MessageSettings for the Arrow message mapping (§6.5).
    /// </summary>
    public sealed record ArrowDataSetReaderMessageSettings
    {
        /// <summary>Framing the reader expects.</summary>
        public ArrowIpcFormat ArrowIpcFormat { get; init; } = ArrowIpcFormat.Batch;

        /// <summary>
        /// Optional URI from which the reader resolves schemas by SchemaId (§6.9.2.6).
        /// </summary>
        public string? ArrowSchemaUri { get; init; }
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Converts between the configuration model's Arrow settings and the encoder's internal
    /// framing selector.
    /// </summary>
    /// <remarks>
    /// Guarded to the frameworks that build the Arrow encoder, because
    /// <see cref="ArrowIpcFraming"/> only exists there. The configuration types above are
    /// deliberately not guarded: a configuration tool must be able to read and write an Arrow
    /// WriterGroup on any target, even one that cannot itself encode Arrow.
    /// </remarks>
    public static class ArrowMessageSettings
    {
        /// <summary>
        /// Maps a configured <see cref="ArrowIpcFormat"/> onto the encoder's internal framing.
        /// </summary>
        /// <param name="format">The configured payload framing.</param>
        /// <returns>The internal framing selector.</returns>
        /// <exception cref="NotSupportedException">
        /// Thrown for <see cref="ArrowIpcFormat.File"/>, which the specification defines for
        /// bounded seekable payloads but this encoder does not emit.
        /// </exception>
        /// <remarks>
        /// This mapping is written out rather than cast because the two enumerations are numbered
        /// differently: the configuration model uses Batch = 0 and Stream = 1 while
        /// <see cref="ArrowIpcFraming"/> declares Stream first. Casting the underlying value would
        /// compile, run, and select exactly the wrong framing.
        /// </remarks>
        public static ArrowIpcFraming ToFraming(ArrowIpcFormat format)
        {
            return format switch
            {
                ArrowIpcFormat.Batch => ArrowIpcFraming.Batch,
                ArrowIpcFormat.Stream => ArrowIpcFraming.Stream,
                _ => throw new NotSupportedException(
                    "The Arrow IPC file framing is not emitted by this encoder.")
            };
        }

        /// <summary>
        /// Maps the encoder's internal framing back onto the configuration model value.
        /// </summary>
        /// <param name="framing">The internal framing selector.</param>
        /// <returns>The configured payload framing.</returns>
        public static ArrowIpcFormat FromFraming(ArrowIpcFraming framing)
        {
            return framing == ArrowIpcFraming.Stream
                ? ArrowIpcFormat.Stream
                : ArrowIpcFormat.Batch;
        }
    }
#endif
}
