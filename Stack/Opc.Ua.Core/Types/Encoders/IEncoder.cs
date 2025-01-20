/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Defines functions used to encode objects in a stream.
    /// </summary>
    public interface IEncoder : IDisposable
    {
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        EncodingType EncodingType { get; }

        /// <summary>
        /// If the encoder is configured to produce a reversible encoding.
        /// </summary>
        /// <remarks>
        /// The BinaryEncoder and XmlEncoder in this library are reversible encoders.
        /// For a JsonEncoder, reversability depends on the encoding type.
        /// </remarks>
        bool UseReversibleEncoding { get; }

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        IServiceMessageContext Context { get; }

        /// <summary>
        /// Completes writing and returns the encoded length.
        /// </summary>
        int Close();

        /// <summary>
        /// Completes writing and returns the encoded text.
        /// </summary>
        string CloseAndReturnText();

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during encoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being encoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being encoded.</param>
        void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris);

        /// <summary>
        /// Pushes a namespace onto the namespace stack.
        /// </summary>
        void PushNamespace(string namespaceUri);

        /// <summary>
        /// Pops a namespace from the namespace stack.
        /// </summary>
        void PopNamespace();

        /// <summary>
        /// Encodes a message with its header.
        /// </summary>
        void EncodeMessage(IEncodeable message);

        /// <summary>
        /// Writes a boolean to the stream.
        /// </summary>
        void WriteBoolean(string fieldName, bool value);

        /// <summary>
        /// Writes a sbyte to the stream.
        /// </summary>
        void WriteSByte(string fieldName, sbyte value);

        /// <summary>
        /// Writes a byte to the stream.
        /// </summary>
        void WriteByte(string fieldName, byte value);

        /// <summary>
        /// Writes a short to the stream.
        /// </summary>
        void WriteInt16(string fieldName, short value);

        /// <summary>
        /// Writes a ushort to the stream.
        /// </summary>
        void WriteUInt16(string fieldName, ushort value);

        /// <summary>
        /// Writes an int to the stream.
        /// </summary>
        void WriteInt32(string fieldName, int value);

        /// <summary>
        /// Writes a uint to the stream.
        /// </summary>
        void WriteUInt32(string fieldName, uint value);

        /// <summary>
        /// Writes a long to the stream.
        /// </summary>
        void WriteInt64(string fieldName, long value);

        /// <summary>
        /// Writes a ulong to the stream.
        /// </summary>
        void WriteUInt64(string fieldName, ulong value);

        /// <summary>
        /// Writes a float to the stream.
        /// </summary>
        void WriteFloat(string fieldName, float value);

        /// <summary>
        /// Writes a double to the stream.
        /// </summary>
        void WriteDouble(string fieldName, double value);

        /// <summary>
        /// Writes a string to the stream.
        /// </summary>
        void WriteString(string fieldName, string value);

        /// <summary>
        /// Writes a UTC date/time to the stream.
        /// </summary>
        void WriteDateTime(string fieldName, DateTime value);

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        void WriteGuid(string fieldName, Uuid value);

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        void WriteGuid(string fieldName, Guid value);

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        void WriteByteString(string fieldName, byte[] value);

        /// <summary>
        /// Writes a byte string to the stream with a given index and count.
        /// </summary>
        void WriteByteString(string fieldName, byte[] value, int index, int count);

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        void WriteByteString(string fieldName, ReadOnlySpan<byte> value);
#endif
        /// <summary>
        /// Writes a XmlElement to the stream.
        /// </summary>
        void WriteXmlElement(string fieldName, XmlElement value);

        /// <summary>
        /// Writes a NodeId to the stream.
        /// </summary>
        void WriteNodeId(string fieldName, NodeId value);

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        void WriteExpandedNodeId(string fieldName, ExpandedNodeId value);

        /// <summary>
        /// Writes a StatusCode to the stream.
        /// </summary>
        void WriteStatusCode(string fieldName, StatusCode value);

        /// <summary>
        /// Writes a DiagnosticInfo to the stream.
        /// </summary>
        void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value);

        /// <summary>
        /// Writes a QualifiedName to the stream.
        /// </summary>
        void WriteQualifiedName(string fieldName, QualifiedName value);

        /// <summary>
        /// Writes a LocalizedText to the stream.
        /// </summary>
        void WriteLocalizedText(string fieldName, LocalizedText value);

        /// <summary>
        /// Writes a Variant to the stream.
        /// </summary>
        void WriteVariant(string fieldName, Variant value);

        /// <summary>
        /// Writes a DataValue to the stream.
        /// </summary>
        void WriteDataValue(string fieldName, DataValue value);

        /// <summary>
        /// Writes an ExtensionObject to the stream.
        /// </summary>
        void WriteExtensionObject(string fieldName, ExtensionObject value);

        /// <summary>
        /// Writes an encodeable object to the stream.
        /// </summary>
        void WriteEncodeable(string fieldName, IEncodeable value, Type systemType);

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        void WriteEnumerated(string fieldName, Enum value);

        /// <summary>
        /// Writes a boolean array to the stream.
        /// </summary>
        void WriteBooleanArray(string fieldName, IList<bool> values);

        /// <summary>
        /// Writes a sbyte array to the stream.
        /// </summary>
        void WriteSByteArray(string fieldName, IList<sbyte> values);

        /// <summary>
        /// Writes a byte array to the stream.
        /// </summary>
        void WriteByteArray(string fieldName, IList<byte> values);

        /// <summary>
        /// Writes a short array to the stream.
        /// </summary>
        void WriteInt16Array(string fieldName, IList<short> values);

        /// <summary>
        /// Writes a ushort array to the stream.
        /// </summary>
        void WriteUInt16Array(string fieldName, IList<ushort> values);

        /// <summary>
        /// Writes a int array to the stream.
        /// </summary>
        void WriteInt32Array(string fieldName, IList<int> values);

        /// <summary>
        /// Writes a uint array to the stream.
        /// </summary>
        void WriteUInt32Array(string fieldName, IList<uint> values);

        /// <summary>
        /// Writes a long array to the stream.
        /// </summary>
        void WriteInt64Array(string fieldName, IList<long> values);

        /// <summary>
        /// Writes a ulong array to the stream.
        /// </summary>
        void WriteUInt64Array(string fieldName, IList<ulong> values);

        /// <summary>
        /// Writes a float array to the stream.
        /// </summary>
        void WriteFloatArray(string fieldName, IList<float> values);

        /// <summary>
        /// Writes a double array to the stream.
        /// </summary>
        void WriteDoubleArray(string fieldName, IList<double> values);

        /// <summary>
        /// Writes a string array to the stream.
        /// </summary>
        void WriteStringArray(string fieldName, IList<string> values);

        /// <summary>
        /// Writes a UTC date/time array to the stream.
        /// </summary>
        void WriteDateTimeArray(string fieldName, IList<DateTime> values);

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        void WriteGuidArray(string fieldName, IList<Uuid> values);

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        void WriteGuidArray(string fieldName, IList<Guid> values);

        /// <summary>
        /// Writes a byte string array to the stream.
        /// </summary>
        void WriteByteStringArray(string fieldName, IList<byte[]> values);

        /// <summary>
        /// Writes a XmlElement array to the stream.
        /// </summary>
        void WriteXmlElementArray(string fieldName, IList<XmlElement> values);

        /// <summary>
        /// Writes a NodeId array to the stream.
        /// </summary>
        void WriteNodeIdArray(string fieldName, IList<NodeId> values);

        /// <summary>
        /// Writes an ExpandedNodeId array to the stream.
        /// </summary>
        void WriteExpandedNodeIdArray(string fieldName, IList<ExpandedNodeId> values);

        /// <summary>
        /// Writes a StatusCode array to the stream.
        /// </summary>
        void WriteStatusCodeArray(string fieldName, IList<StatusCode> values);

        /// <summary>
        /// Writes a DiagnosticInfo array to the stream.
        /// </summary>
        void WriteDiagnosticInfoArray(string fieldName, IList<DiagnosticInfo> values);

        /// <summary>
        /// Writes a QualifiedName array to the stream.
        /// </summary>
        void WriteQualifiedNameArray(string fieldName, IList<QualifiedName> values);

        /// <summary>
        /// Writes a LocalizedText array to the stream.
        /// </summary>
        void WriteLocalizedTextArray(string fieldName, IList<LocalizedText> values);

        /// <summary>
        /// Writes a Variant array to the stream.
        /// </summary>
        void WriteVariantArray(string fieldName, IList<Variant> values);

        /// <summary>
        /// Writes a DataValue array to the stream.
        /// </summary>
        void WriteDataValueArray(string fieldName, IList<DataValue> values);

        /// <summary>
        /// Writes an extension object array to the stream.
        /// </summary>
        void WriteExtensionObjectArray(string fieldName, IList<ExtensionObject> values);

        /// <summary>
        /// Writes an encodeable object array to the stream.
        /// </summary>
        void WriteEncodeableArray(string fieldName, IList<IEncodeable> values, System.Type systemType);

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        void WriteEnumeratedArray(string fieldName, Array values, Type systemType);

        /// <summary>
        /// Encode an array according to its valueRank and BuiltInType
        /// </summary>
        void WriteArray(string fieldName, object array, int valueRank, BuiltInType builtInType);

        /// <summary>
        /// Encode the switch field for a union.
        /// </summary>
        /// <params name="switchField">The switch field </params>
        /// <params name="alternateFieldName">Returns an alternate fieldName for the encoded union property if the encoder requires it, null otherwise.</params>
        void WriteSwitchField(uint switchField, out string alternateFieldName);

        /// <summary>
        /// Encode the encoding mask for a structure with optional fields.
        /// </summary>
        void WriteEncodingMask(uint encodingMask);
    }

    /// <summary>
    /// The type of encoding used by an encoder/decoder.
    /// </summary>
    public enum EncodingType
    {
        /// <summary>
        /// The UA Binary encoding.
        /// </summary>
        Binary,

        /// <summary>
        /// XML
        /// </summary>
        Xml,

        /// <summary>
        /// JSON
        /// </summary>
        Json
    }
}
