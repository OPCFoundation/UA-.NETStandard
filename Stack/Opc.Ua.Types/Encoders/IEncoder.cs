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
        void EncodeMessage<T>(T message) where T : IEncodeable;

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
        /// Writes a byte string to the stream.
        /// </summary>
        void WriteByteString(string fieldName, ByteString value);

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
        /// <typeparam name="T">The type of the encodeable</typeparam>
        void WriteEncodeable<T>(string fieldName, T value)
            where T : IEncodeable;

        /// <summary>
        /// Writes an encodeable object to the stream as extension object.
        /// </summary>
        /// <typeparam name="T">The type of the encodeable</typeparam>
        void WriteEncodeableAsExtensionObject<T>(string fieldName, T value)
            where T : IEncodeable;

        /// <summary>
        /// Writes an enumerated value to the stream.
        /// </summary>
        /// <typeparam name="T">The type of the enumeration</typeparam>
        void WriteEnumerated<T>(string fieldName, T value)
            where T : Enum;

        /// <summary>
        /// Writes a boolean array to the stream.
        /// </summary>
        void WriteBooleanArray(string fieldName, ArrayOf<bool> values);

        /// <summary>
        /// Writes a sbyte array to the stream.
        /// </summary>
        void WriteSByteArray(string fieldName, ArrayOf<sbyte> values);

        /// <summary>
        /// Writes a byte array to the stream.
        /// </summary>
        void WriteByteArray(string fieldName, ArrayOf<byte> values);

        /// <summary>
        /// Writes a short array to the stream.
        /// </summary>
        void WriteInt16Array(string fieldName, ArrayOf<short> values);

        /// <summary>
        /// Writes a ushort array to the stream.
        /// </summary>
        void WriteUInt16Array(string fieldName, ArrayOf<ushort> values);

        /// <summary>
        /// Writes a int array to the stream.
        /// </summary>
        void WriteInt32Array(string fieldName, ArrayOf<int> values);

        /// <summary>
        /// Writes a uint array to the stream.
        /// </summary>
        void WriteUInt32Array(string fieldName, ArrayOf<uint> values);

        /// <summary>
        /// Writes a long array to the stream.
        /// </summary>
        void WriteInt64Array(string fieldName, ArrayOf<long> values);

        /// <summary>
        /// Writes a ulong array to the stream.
        /// </summary>
        void WriteUInt64Array(string fieldName, ArrayOf<ulong> values);

        /// <summary>
        /// Writes a float array to the stream.
        /// </summary>
        void WriteFloatArray(string fieldName, ArrayOf<float> values);

        /// <summary>
        /// Writes a double array to the stream.
        /// </summary>
        void WriteDoubleArray(string fieldName, ArrayOf<double> values);

        /// <summary>
        /// Writes a string array to the stream.
        /// </summary>
        void WriteStringArray(string fieldName, ArrayOf<string> values);

        /// <summary>
        /// Writes a UTC date/time array to the stream.
        /// </summary>
        void WriteDateTimeArray(string fieldName, ArrayOf<DateTime> values);

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        void WriteGuidArray(string fieldName, ArrayOf<Uuid> values);

        /// <summary>
        /// Writes a byte string array to the stream.
        /// </summary>
        void WriteByteStringArray(string fieldName, ArrayOf<ByteString> values);

        /// <summary>
        /// Writes a XmlElement array to the stream.
        /// </summary>
        void WriteXmlElementArray(string fieldName, ArrayOf<XmlElement> values);

        /// <summary>
        /// Writes a NodeId array to the stream.
        /// </summary>
        void WriteNodeIdArray(string fieldName, ArrayOf<NodeId> values);

        /// <summary>
        /// Writes an ExpandedNodeId array to the stream.
        /// </summary>
        void WriteExpandedNodeIdArray(string fieldName, ArrayOf<ExpandedNodeId> values);

        /// <summary>
        /// Writes a StatusCode array to the stream.
        /// </summary>
        void WriteStatusCodeArray(string fieldName, ArrayOf<StatusCode> values);

        /// <summary>
        /// Writes a DiagnosticInfo array to the stream.
        /// </summary>
        void WriteDiagnosticInfoArray(string fieldName, ArrayOf<DiagnosticInfo> values);

        /// <summary>
        /// Writes a QualifiedName array to the stream.
        /// </summary>
        void WriteQualifiedNameArray(string fieldName, ArrayOf<QualifiedName> values);

        /// <summary>
        /// Writes a LocalizedText array to the stream.
        /// </summary>
        void WriteLocalizedTextArray(string fieldName, ArrayOf<LocalizedText> values);

        /// <summary>
        /// Writes a Variant array to the stream.
        /// </summary>
        void WriteVariantArray(string fieldName, ArrayOf<Variant> values);

        /// <summary>
        /// Writes a DataValue array to the stream.
        /// </summary>
        void WriteDataValueArray(string fieldName, ArrayOf<DataValue> values);

        /// <summary>
        /// Writes an extension object array to the stream.
        /// </summary>
        void WriteExtensionObjectArray(string fieldName, ArrayOf<ExtensionObject> values);

        /// <summary>
        /// Writes an encodeable object array to the stream.
        /// </summary>
        /// <typeparam name="T">The type of the array elements</typeparam>
        void WriteEncodeableArray<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable;

        /// <summary>
        /// Writes an encodeable object array to the stream as extension objects.
        /// </summary>
        /// <typeparam name="T">The type of the array elements</typeparam>
        void WriteEncodeableArrayAsExtensionObjects<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable;

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        /// <typeparam name="T">The type of the array elements</typeparam>
        void WriteEnumeratedArray<T>(string fieldName, ArrayOf<T> values)
            where T : Enum;

        /// <summary>
        /// Writes just the value inside the variant. In essence
        /// invokes the apropriate Write method for the value contained
        /// in the variant If a field name is provided, the value is
        /// the value of said field or element. If not, the value is
        /// written as just the info contained in the Variant.
        /// </summary>
        /// <remarks>
        /// This replaces the previously available WriteArray method
        /// which has been removed because it could not be implemented in a
        /// type safe manner and did essentially the same here just for
        /// arrays while this method also handles scalar values.
        /// </remarks>
        void WriteVariantValue(string fieldName, Variant value);

        /// <summary>
        /// Encode the switch field for a union.
        /// </summary>
        /// <params name="switchField">The switch field </params>
        /// <params name="fieldName">Returns an alternate fieldName for the
        /// encoded union property if the encoder requires it, null otherwise.
        /// </params>
        void WriteSwitchField(uint switchField, out string fieldName);

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
