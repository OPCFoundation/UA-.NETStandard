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
    /// Well-known identifiers of the Avro DataEncoding companion namespace - the Part 14
    /// configuration model additions for the Avro message mapping (§9).
    /// </summary>
    /// <remarks>
    /// NodeIds are provisional; the OPC Foundation assigns the final identifiers when the namespace
    /// is registered. The model adds <em>configuration</em> only: there is no DataTypeEncoding
    /// Object and no <c>HasEncoding</c> reference for <c>Default Avro</c>, because §4.2 states the
    /// encoding has no AddressSpace representation and is named only for symmetry with
    /// <c>Default Binary</c>, <c>Default XML</c> and <c>Default JSON</c>.
    /// </remarks>
    public static class AvroWellKnown
    {
        /// <summary>The Avro DataEncoding companion namespace URI.</summary>
        public const string AvroNamespaceUri = "http://opcfoundation.org/UA/Avro/";

        /// <summary>Provisional NodeId of the <c>AvroNetworkMessageContentMask</c> DataType.</summary>
        public const uint AvroNetworkMessageContentMask = 64000;

        /// <summary>Provisional NodeId of the <c>AvroDataSetMessageContentMask</c> DataType.</summary>
        public const uint AvroDataSetMessageContentMask = 64001;

        /// <summary>Provisional NodeId of the <c>AvroWriterGroupMessageDataType</c> DataType.</summary>
        public const uint AvroWriterGroupMessageDataType = 64010;

        /// <summary>Provisional NodeId of the <c>AvroDataSetWriterMessageDataType</c> DataType.</summary>
        public const uint AvroDataSetWriterMessageDataType = 64011;

        /// <summary>Provisional NodeId of the <c>AvroDataSetReaderMessageDataType</c> DataType.</summary>
        public const uint AvroDataSetReaderMessageDataType = 64012;

        /// <summary>Provisional NodeId of the <c>AvroWriterGroupMessageType</c> ObjectType.</summary>
        public const uint AvroWriterGroupMessageType = 64020;

        /// <summary>Provisional NodeId of the <c>AvroDataSetWriterMessageType</c> ObjectType.</summary>
        public const uint AvroDataSetWriterMessageType = 64021;

        /// <summary>Provisional NodeId of the <c>AvroDataSetReaderMessageType</c> ObjectType.</summary>
        public const uint AvroDataSetReaderMessageType = 64022;
    }

    /// <summary>
    /// The Avro NetworkMessage content mask (§9.2). Selects which fields of the fixed envelope are
    /// populated; unselected fields stay present in the envelope schema and are null on the wire,
    /// which is what keeps the envelope schema stable across content-mask changes.
    /// </summary>
    [Flags]
    public enum AvroNetworkMessageContentMask : uint
    {
        /// <summary>No optional envelope field is populated.</summary>
        None = 0,

        /// <summary>The envelope header fields are populated.</summary>
        NetworkMessageHeader = 1,

        /// <summary>Each payload entry carries its DataSetMessage header.</summary>
        DataSetMessageHeader = 2,

        /// <summary>The payload carries exactly one DataSetMessage.</summary>
        SingleDataSetMessage = 4,

        /// <summary>The PublisherId is populated.</summary>
        PublisherId = 8,

        /// <summary>The DataSetClassId is populated.</summary>
        DataSetClassId = 16,

        /// <summary>The GroupHeader group of fields is populated.</summary>
        GroupHeader = 32,

        /// <summary>The WriterGroupId is populated.</summary>
        WriterGroupId = 64,

        /// <summary>The GroupVersion is populated.</summary>
        GroupVersion = 128,

        /// <summary>The NetworkMessageNumber is populated.</summary>
        NetworkMessageNumber = 256,

        /// <summary>The SequenceNumber is populated.</summary>
        SequenceNumber = 512,

        /// <summary>The Timestamp is populated.</summary>
        Timestamp = 1024,

        /// <summary>The PicoSeconds field is populated.</summary>
        PicoSeconds = 2048,

        /// <summary>Promoted fields are populated (§8.1).</summary>
        PromotedFields = 4096
    }

    /// <summary>
    /// The Avro DataSetMessage content mask (§9.2).
    /// </summary>
    [Flags]
    public enum AvroDataSetMessageContentMask : uint
    {
        /// <summary>No optional header field is populated.</summary>
        None = 0,

        /// <summary>The DataSetWriterId is populated. Always required (§8.2).</summary>
        DataSetWriterId = 1,

        /// <summary>The DataSetMessage type is populated. Always required (§8.2).</summary>
        MessageType = 2,

        /// <summary>The ConfigurationVersion major part is populated.</summary>
        MajorVersion = 4,

        /// <summary>The ConfigurationVersion minor part is populated.</summary>
        MinorVersion = 8,

        /// <summary>The SequenceNumber is populated.</summary>
        SequenceNumber = 16,

        /// <summary>The Timestamp is populated.</summary>
        Timestamp = 32,

        /// <summary>The PicoSeconds field is populated.</summary>
        PicoSeconds = 64,

        /// <summary>The Status is populated.</summary>
        Status = 128,

        /// <summary>The DataSet SchemaId is carried in the DataSetMessage header (§8.4).</summary>
        SchemaId = 256
    }
}
