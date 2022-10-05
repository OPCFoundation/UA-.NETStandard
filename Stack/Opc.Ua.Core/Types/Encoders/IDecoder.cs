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
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Defines functions used to dencode objects from a stream.
    /// </summary>
    public interface IDecoder
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
        /// Initializes the tables used to map namespace and server uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being decoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being decoded.</param>
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
        byte[] ReadByteString(string fieldName);

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
        /// <param name="fieldName">The encodeable object field name</param>
        /// <param name="systemType">The system type of the encopdeable object to be read</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instance that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> object that was read from the stream.</returns>
        IEncodeable ReadEncodeable(string fieldName, Type systemType, ExpandedNodeId encodeableTypeId = null);

        /// <summary>
        ///  Reads an enumerated value from the stream.
        /// </summary>
        Enum ReadEnumerated(string fieldName, Type enumType);

        /// <summary>
        /// Reads a boolean array from the stream.
        /// </summary>
        BooleanCollection ReadBooleanArray(string fieldName);

        /// <summary>
        /// Reads a sbyte array from the stream.
        /// </summary>
        SByteCollection ReadSByteArray(string fieldName);

        /// <summary>
        /// Reads a sbyte array from the stream.
        /// </summary>
        ByteCollection ReadByteArray(string fieldName);

        /// <summary>
        /// Reads a short array from the stream.
        /// </summary>
        Int16Collection ReadInt16Array(string fieldName);

        /// <summary>
        /// Reads a ushort array from the stream.
        /// </summary>
        UInt16Collection ReadUInt16Array(string fieldName);

        /// <summary>
        /// Reads a int array from the stream.
        /// </summary>
        Int32Collection ReadInt32Array(string fieldName);

        /// <summary>
        /// Reads a uint array from the stream.
        /// </summary>
        UInt32Collection ReadUInt32Array(string fieldName);

        /// <summary>
        /// Reads a long array from the stream.
        /// </summary>
        Int64Collection ReadInt64Array(string fieldName);

        /// <summary>
        /// Reads a ulong array from the stream.
        /// </summary>
        UInt64Collection ReadUInt64Array(string fieldName);

        /// <summary>
        /// Reads a float array from the stream.
        /// </summary>
        FloatCollection ReadFloatArray(string fieldName);

        /// <summary>
        /// Reads a double array from the stream.
        /// </summary>
        DoubleCollection ReadDoubleArray(string fieldName);

        /// <summary>
        /// Reads a string array from the stream.
        /// </summary>
        StringCollection ReadStringArray(string fieldName);

        /// <summary>
        /// Reads a UTC date/time array from the stream.
        /// </summary>
        DateTimeCollection ReadDateTimeArray(string fieldName);

        /// <summary>
        /// Reads a GUID array from the stream.
        /// </summary>
        UuidCollection ReadGuidArray(string fieldName);

        /// <summary>
        /// Reads a byte string array from the stream.
        /// </summary>
        ByteStringCollection ReadByteStringArray(string fieldName);

        /// <summary>
        /// Reads an XmlElement array from the stream.
        /// </summary>
        XmlElementCollection ReadXmlElementArray(string fieldName);

        /// <summary>
        /// Reads an NodeId array from the stream.
        /// </summary>
        NodeIdCollection ReadNodeIdArray(string fieldName);

        /// <summary>
        /// Reads an ExpandedNodeId array from the stream.
        /// </summary>
        ExpandedNodeIdCollection ReadExpandedNodeIdArray(string fieldName);

        /// <summary>
        /// Reads an StatusCode array from the stream.
        /// </summary>
        StatusCodeCollection ReadStatusCodeArray(string fieldName);

        /// <summary>
        /// Reads an DiagnosticInfo array from the stream.
        /// </summary>
        DiagnosticInfoCollection ReadDiagnosticInfoArray(string fieldName);

        /// <summary>
        /// Reads an QualifiedName array from the stream.
        /// </summary>
        QualifiedNameCollection ReadQualifiedNameArray(string fieldName);

        /// <summary>
        /// Reads an LocalizedText array from the stream.
        /// </summary>
        LocalizedTextCollection ReadLocalizedTextArray(string fieldName);

        /// <summary>
        /// Reads an Variant array from the stream.
        /// </summary>
        VariantCollection ReadVariantArray(string fieldName);

        /// <summary>
        /// Reads an DataValue array from the stream.
        /// </summary>
        DataValueCollection ReadDataValueArray(string fieldName);

        /// <summary>
        /// Reads an extension object array from the stream.
        /// </summary>
        ExtensionObjectCollection ReadExtensionObjectArray(string fieldName);

        /// <summary>
        /// Reads an encodeable array from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable array field name</param>
        /// <param name="systemType">The system type of the encopdeable objects to be read object</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instances that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> array that was read from the stream.</returns>
        Array ReadEncodeableArray(string fieldName, Type systemType, ExpandedNodeId encodeableTypeId = null);

        /// <summary>
        /// Reads an enumerated value array from the stream.
        /// </summary>
        Array ReadEnumeratedArray(string fieldName, Type enumType);

        /// <summary>
        /// Reads an array with the specified valueRank and the specified BuiltInType.
        /// </summary>
        /// <param name="fieldName">The array field name.</param>
        /// <param name="valueRank">The value rank of the array.</param>
        /// <param name="builtInType">The builtInType of the array elements.</param>
        /// <param name="systemType">The system type of an encodeable or enum element of the array.</param>
        /// <param name="encodeableTypeId">The type id of an encodeable or enum element of the array.</param>
        /// <returns>An array of the specified builtInType, systemType or encodeableTypeId.</returns>
        Array ReadArray(string fieldName, int valueRank, BuiltInType builtInType,
            Type systemType = null, ExpandedNodeId encodeableTypeId = null);
    }
}
