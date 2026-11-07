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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// WriterGroup MessageSettings for the Avro message mapping (§9.3).
    /// </summary>
    /// <remarks>
    /// A WriterGroup uses the Avro message mapping if and only if its <c>MessageSettings</c>
    /// carries this type, exactly as UADP and JSON are selected by their own
    /// <c>WriterGroupMessageDataType</c> subtypes. <c>TransportSettings</c> are unchanged; the
    /// mapping is identified on the wire by the content types of §8.6.
    /// </remarks>
    public sealed record AvroWriterGroupMessageSettings
    {
        /// <summary>Envelope fields to populate (§9.2).</summary>
        public AvroNetworkMessageContentMask NetworkMessageContentMask { get; init; }

        /// <summary>
        /// Optional URI of the schema registry or catalog serving this group's schemas (§8.4).
        /// </summary>
        public string? AvroSchemaUri { get; init; }

        /// <summary>
        /// Whether payloads are Avro object container files. False for PubSub network payloads by
        /// default; true only for transports that explicitly carry container files.
        /// </summary>
        public bool AvroUseObjectContainerFile { get; init; }
    }

    /// <summary>
    /// DataSetWriter MessageSettings for the Avro message mapping (§9.3).
    /// </summary>
    public sealed record AvroDataSetWriterMessageSettings
    {
        /// <summary>DataSetMessage header fields to populate (§9.2).</summary>
        public AvroDataSetMessageContentMask DataSetMessageContentMask { get; init; }

        /// <summary>
        /// SchemaId of the DataSet schema currently published by this writer (§6.3), in
        /// little-endian hexadecimal. It identifies the schema and does not replace the
        /// ConfigurationVersion: a publisher that grows a schema advances both (§8.4).
        /// </summary>
        public string? AvroSchemaId { get; init; }

        /// <summary>
        /// Optional copy of the little-endian CRC-64-AVRO SchemaId bytes for mismatch detection.
        /// </summary>
        public ByteString AvroSchemaHash { get; init; }

        /// <summary>Whether RawData fields may be emitted for this writer (§8.2).</summary>
        public bool AvroRawDataAllowed { get; init; }
    }

    /// <summary>
    /// DataSetReader MessageSettings for the Avro message mapping (§9.3).
    /// </summary>
    public sealed record AvroDataSetReaderMessageSettings
    {
        /// <summary>Envelope fields the reader expects.</summary>
        public AvroNetworkMessageContentMask NetworkMessageContentMask { get; init; }

        /// <summary>DataSetMessage header fields the reader expects.</summary>
        public AvroDataSetMessageContentMask DataSetMessageContentMask { get; init; }

        /// <summary>
        /// Optional URI from which the reader resolves schemas by SchemaId (§7).
        /// </summary>
        public string? AvroSchemaUri { get; init; }
    }
}
