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

namespace Opc.Ua
{
    /// <summary>
    /// Defines functions used to decode objects from a stream.
    /// </summary>
    public interface IDecoder : IDisposable
    {
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        EncodingType EncodingType { get; }

        /// <summary>
        /// The message context associated with the decoder.
        /// </summary>
        IServiceMessageContext Context { get; }

        /// <summary>
        /// Closes the stream used for reading.
        /// </summary>
        void Close();

        /// <summary>
        /// Initializes the tables used to map namespace and server
        /// uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced
        /// by the data being decoded.</param>
        /// <param name="serverUris">The server URIs referenced by the
        /// data being decoded.</param>
        void SetMappingTables(
            NamespaceTable namespaceUris, StringTable serverUris);

        /// <summary>
        /// Decodes a message of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of the message to read</typeparam>
        T DecodeMessage<T>() where T : IEncodeable;

        /// <summary>
        /// Pushes a namespace onto the namespace stack.
        /// </summary>
        void PushNamespace(string namespaceUri);

        /// <summary>
        /// Pops a namespace from the namespace stack.
        /// </summary>
        void PopNamespace();

        /// <summary>
        /// Reads a boolean from the stream.
        /// </summary>
        bool ReadBoolean(string fieldName);

        /// <summary>
        /// Reads a sbyte from the stream.
        /// </summary>
        sbyte ReadSByte(string fieldName);

        /// <summary>
        /// Reads a byte from the stream.
        /// </summary>
        byte ReadByte(string fieldName);

        /// <summary>
        /// Reads a short from the stream.
        /// </summary>
        short ReadInt16(string fieldName);

        /// <summary>
        /// Reads a ushort from the stream.
        /// </summary>
        ushort ReadUInt16(string fieldName);

        /// <summary>
        /// Reads an int from the stream.
        /// </summary>
        int ReadInt32(string fieldName);

        /// <summary>
        /// Reads a uint from the stream.
        /// </summary>
        uint ReadUInt32(string fieldName);

        /// <summary>
        /// Reads a long from the stream.
        /// </summary>
        long ReadInt64(string fieldName);

        /// <summary>
        /// Reads a ulong from the stream.
        /// </summary>
        ulong ReadUInt64(string fieldName);

        /// <summary>
        /// Reads a float from the stream.
        /// </summary>
        float ReadFloat(string fieldName);

        /// <summary>
        /// Reads a double from the stream.
        /// </summary>
        double ReadDouble(string fieldName);

        /// <summary>
        /// Reads a string from the stream.
        /// </summary>
        string ReadString(string fieldName);

        /// <summary>
        /// Reads a UTC date/time from the stream.
        /// </summary>
        DateTime ReadDateTime(string fieldName);

        /// <summary>
        /// Reads a GUID from the stream.
        /// </summary>
        Uuid ReadGuid(string fieldName);

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        ByteString ReadByteString(string fieldName);

        /// <summary>
        /// Reads an XmlElement from the stream.
        /// </summary>
        XmlElement ReadXmlElement(string fieldName);

        /// <summary>
        /// Reads an NodeId from the stream.
        /// </summary>
        NodeId ReadNodeId(string fieldName);

        /// <summary>
        /// Reads an ExpandedNodeId from the stream.
        /// </summary>
        ExpandedNodeId ReadExpandedNodeId(string fieldName);

        /// <summary>
        /// Reads an StatusCode from the stream.
        /// </summary>
        StatusCode ReadStatusCode(string fieldName);

        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// </summary>
        DiagnosticInfo ReadDiagnosticInfo(string fieldName);

        /// <summary>
        /// Reads an QualifiedName from the stream.
        /// </summary>
        QualifiedName ReadQualifiedName(string fieldName);

        /// <summary>
        /// Reads an LocalizedText from the stream.
        /// </summary>
        LocalizedText ReadLocalizedText(string fieldName);

        /// <summary>
        /// Reads an Variant from the stream.
        /// </summary>
        Variant ReadVariant(string fieldName);

        /// <summary>
        /// Reads an DataValue from the stream.
        /// </summary>
        DataValue ReadDataValue(string fieldName);

        /// <summary>
        /// Reads an ExtensionObject from the stream.
        /// </summary>
        ExtensionObject ReadExtensionObject(string fieldName);

        /// <summary>
        /// Reads an encodeable object from the stream.
        /// </summary>
        /// <typeparam name="T">The type of the encodeable object to be read
        /// </typeparam>
        /// <param name="fieldName">The encodeable object field name</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/>
        /// instance that will be read.</param>
        /// <returns>A type of type <see cref="IEncodeable"/> that was read
        /// from the stream.</returns>
        T ReadEncodeable<T>(string fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable;

        /// <summary>
        /// Reads an encodeable object from the stream.
        /// </summary>
        /// <typeparam name="T">The type of the encodeable object to be read
        /// </typeparam>
        /// <param name="fieldName">The encodeable object field name</param>
        /// <returns>A type of type <see cref="IEncodeable"/> that was read
        /// from the stream.</returns>
        T ReadEncodeable<T>(string fieldName) where T : IEncodeable, new();

        /// <summary>
        /// Reads an enumerated value from the stream.
        /// </summary>
        /// <typeparam name="T">The type of the enum to be read</typeparam>
        T ReadEnumerated<T>(string fieldName) where T : struct, Enum;

        /// <summary>
        /// Reads a boolean array from the stream.
        /// </summary>
        ArrayOf<bool> ReadBooleanArray(string fieldName);

        /// <summary>
        /// Reads a sbyte array from the stream.
        /// </summary>
        ArrayOf<sbyte> ReadSByteArray(string fieldName);

        /// <summary>
        /// Reads a byte array from the stream.
        /// </summary>
        ArrayOf<byte> ReadByteArray(string fieldName);

        /// <summary>
        /// Reads a short array from the stream.
        /// </summary>
        ArrayOf<short> ReadInt16Array(string fieldName);

        /// <summary>
        /// Reads a ushort array from the stream.
        /// </summary>
        ArrayOf<ushort> ReadUInt16Array(string fieldName);

        /// <summary>
        /// Reads a int array from the stream.
        /// </summary>
        ArrayOf<int> ReadInt32Array(string fieldName);

        /// <summary>
        /// Reads a uint array from the stream.
        /// </summary>
        ArrayOf<uint> ReadUInt32Array(string fieldName);

        /// <summary>
        /// Reads a long array from the stream.
        /// </summary>
        ArrayOf<long> ReadInt64Array(string fieldName);

        /// <summary>
        /// Reads a ulong array from the stream.
        /// </summary>
        ArrayOf<ulong> ReadUInt64Array(string fieldName);

        /// <summary>
        /// Reads a float array from the stream.
        /// </summary>
        ArrayOf<float> ReadFloatArray(string fieldName);

        /// <summary>
        /// Reads a double array from the stream.
        /// </summary>
        ArrayOf<double> ReadDoubleArray(string fieldName);

        /// <summary>
        /// Reads a string array from the stream.
        /// </summary>
        ArrayOf<string> ReadStringArray(string fieldName);

        /// <summary>
        /// Reads a UTC date/time array from the stream.
        /// </summary>
        ArrayOf<DateTime> ReadDateTimeArray(string fieldName);

        /// <summary>
        /// Reads a GUID array from the stream.
        /// </summary>
        ArrayOf<Uuid> ReadGuidArray(string fieldName);

        /// <summary>
        /// Reads a byte string array from the stream.
        /// </summary>
        ArrayOf<ByteString> ReadByteStringArray(string fieldName);

        /// <summary>
        /// Reads an XmlElement array from the stream.
        /// </summary>
        ArrayOf<XmlElement> ReadXmlElementArray(string fieldName);

        /// <summary>
        /// Reads an NodeId array from the stream.
        /// </summary>
        ArrayOf<NodeId> ReadNodeIdArray(string fieldName);

        /// <summary>
        /// Reads an ExpandedNodeId array from the stream.
        /// </summary>
        ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string fieldName);

        /// <summary>
        /// Reads an StatusCode array from the stream.
        /// </summary>
        ArrayOf<StatusCode> ReadStatusCodeArray(string fieldName);

        /// <summary>
        /// Reads an DiagnosticInfo array from the stream.
        /// </summary>
        ArrayOf<DiagnosticInfo> ReadDiagnosticInfoArray(string fieldName);

        /// <summary>
        /// Reads an QualifiedName array from the stream.
        /// </summary>
        ArrayOf<QualifiedName> ReadQualifiedNameArray(string fieldName);

        /// <summary>
        /// Reads an LocalizedText array from the stream.
        /// </summary>
        ArrayOf<LocalizedText> ReadLocalizedTextArray(string fieldName);

        /// <summary>
        /// Reads an Variant array from the stream.
        /// </summary>
        ArrayOf<Variant> ReadVariantArray(string fieldName);

        /// <summary>
        /// Reads an DataValue array from the stream.
        /// </summary>
        ArrayOf<DataValue> ReadDataValueArray(string fieldName);

        /// <summary>
        /// Reads an extension object array from the stream.
        /// </summary>
        ArrayOf<ExtensionObject> ReadExtensionObjectArray(string fieldName);

        /// <summary>
        /// Reads an encodeable array from the stream.
        /// </summary>
        /// <typeparam name="T">The type of the encodeable objects to be read
        /// </typeparam>
        /// <param name="fieldName">The encodeable array field name</param>
        /// <returns>An array of types of type <see cref="IEncodeable"/></returns>
        ArrayOf<T> ReadEncodeableArray<T>(string fieldName) where T : IEncodeable, new();

        /// <summary>
        /// Reads an encodeable array from the stream.
        /// </summary>
        /// <typeparam name="T">The type of the encodeable objects to be read
        /// </typeparam>
        /// <param name="fieldName">The encodeable array field name</param>
        /// <param name="encodeableTypeId">The TypeId for the
        /// <see cref="IEncodeable"/> instances that will be read.</param>
        /// <returns>An array of types of type <see cref="IEncodeable"/></returns>
        ArrayOf<T> ReadEncodeableArray<T>(
            string fieldName,
            ExpandedNodeId encodeableTypeId)
            where T : IEncodeable;

        /// <summary>
        /// Reads an enumerated value array from the stream.
        /// </summary>
        /// <typeparam name="T">The type of the enum to be read</typeparam>
        ArrayOf<T> ReadEnumeratedArray<T>(string fieldName) where T : struct, Enum;

        /// <summary>
        /// Reads a variant value from the stream with the specified TypeInfo.
        /// If type info is <see cref="TypeInfo.Unknown"/> the value is read
        /// as <see cref="ReadVariant(string)"/>.
        /// </summary>
        /// <remarks>
        /// Replaced the former untyped ReadArray method to read any value
        /// corresponding to the type information.
        /// </remarks>
        /// <param name="fieldName">The field name.</param>
        /// <param name="typeInfo">The type info deciding the Variant</param>
        /// <returns></returns>
        Variant ReadVariantValue(string fieldName, TypeInfo typeInfo);

        /// <summary>
        /// Decode the switch field for a union.
        /// </summary>
        /// <param name="switches">The list of field names in the order of
        /// the union selector.</param>
        /// <param name="fieldName">Returns an alternate fieldName for the
        /// encoded union property if the encoder requires it, null otherwise.</param>
        uint ReadSwitchField(IList<string> switches, out string fieldName);

        /// <summary>
        /// Decode the encoding mask for a structure with optional fields.
        /// </summary>
        /// <param name="masks">The list of field names in the order of the
        /// bits in the optional fields mask.</param>
        uint ReadEncodingMask(IList<string> masks);
    }
}
