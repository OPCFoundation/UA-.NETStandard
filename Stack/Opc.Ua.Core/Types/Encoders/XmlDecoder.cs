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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Reads objects from a XML stream.
    /// </summary>
    public class XmlDecoder : IDecoder
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public XmlDecoder(XmlReader reader, IServiceMessageContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Initialize();
            m_context = context;
            m_nestingLevel = 0;
            m_reader = reader;
        }

        /// <summary>
        /// Initializes the object with an XML element to parse.
        /// </summary>
        public XmlDecoder(XmlElement element, IServiceMessageContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Initialize();
            m_reader = XmlReader.Create(new StringReader(element.OuterXml), Utils.DefaultXmlReaderSettings());
            m_context = context;
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Initializes the object with a XML reader.
        /// </summary>
        public XmlDecoder(Type systemType, XmlReader reader, IServiceMessageContext context)
        {
            Initialize();

            m_reader = reader;
            m_context = context;
            m_nestingLevel = 0;

            string ns = null;
            string name = null;

            if (systemType != null)
            {
                XmlQualifiedName typeName = EncodeableFactory.GetXmlName(systemType);
                ns = typeName.Namespace;
                name = typeName.Name;
            }

            if (ns == null)
            {
                m_reader.MoveToContent();
                ns = m_reader.NamespaceURI;
                name = m_reader.Name;
            }

            int index = name.IndexOf(':');

            if (index != -1)
            {
                name = name.Substring(index + 1);
            }

            PushNamespace(ns);
            BeginField(name, false);
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_reader = null;
            m_namespaces = new Stack<string>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes a string table from an XML stream.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="elementName">Name of the element.</param>
        /// <param name="stringTable">The string table.</param>
        /// <returns>True if the table was found. False otherwise.</returns>
        public bool LoadStringTable(string tableName, string elementName, StringTable stringTable)
        {
            PushNamespace(Namespaces.OpcUaXsd);

            try
            {
                if (!Peek(tableName))
                {
                    return false;
                }

                ReadStartElement();

                while (Peek(elementName))
                {
                    string namespaceUri = ReadString(elementName);
                    stringTable.Append(namespaceUri);
                }

                Skip(new XmlQualifiedName(tableName, Namespaces.OpcUaXsd));
                return true;
            }
            finally
            {
                PopNamespace();
            }
        }

        /// <summary>
        /// Closes the stream used for reading.
        /// </summary>
        public void Close()
        {
            m_reader.Close();
        }

        /// <summary>
        /// Closes the stream used for reading.
        /// </summary>
        public void Close(bool checkEof)
        {
            if (checkEof && m_reader.NodeType != XmlNodeType.None)
            {
                m_reader.ReadEndElement();
            }

            m_reader.Close();
        }

        /// <summary>
        /// Returns the qualified name for the next element in the stream.
        /// </summary>
        public XmlQualifiedName Peek(XmlNodeType nodeType)
        {
            m_reader.MoveToContent();

            if (nodeType != XmlNodeType.None && nodeType != m_reader.NodeType)
            {
                return null;
            }

            return new XmlQualifiedName(m_reader.LocalName, m_reader.NamespaceURI);
        }

        /// <summary>
        /// Returns true if the specified field is the next element to be extracted.
        /// </summary>
        public bool Peek(string fieldName)
        {
            m_reader.MoveToContent();

            if (XmlNodeType.Element != m_reader.NodeType)
            {
                return false;
            }

            if (fieldName != m_reader.LocalName)
            {
                return false;
            }

            return m_namespaces.Peek() == m_reader.NamespaceURI;
        }

        /// <summary>
        /// Returns the qualified name for the next element in the stream.
        /// </summary>
        public void ReadStartElement()
        {
            bool isEmpty = m_reader.IsEmptyElement;
            m_reader.ReadStartElement();

            if (!isEmpty)
            {
                m_reader.MoveToContent();
            }
        }

        /// <summary>
        /// Skips to the end of the specified element.
        /// </summary>
        /// <param name="qname">The qualified name of the element to skip.</param>
        public void Skip(XmlQualifiedName qname)
        {
            try
            {
                m_reader.MoveToContent();

                int depth = 1;

                while (depth > 0)
                {
                    if (m_reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (m_reader.LocalName == qname.Name && m_reader.NamespaceURI == qname.Namespace)
                        {
                            depth--;
                        }
                    }
                    else if (m_reader.NodeType == XmlNodeType.Element)
                    {
                        if (m_reader.LocalName == qname.Name && m_reader.NamespaceURI == qname.Namespace)
                        {
                            depth++;
                        }
                    }

                    m_reader.Skip();
                    m_reader.MoveToContent();
                }
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Skip {0} failed: {1}", qname.Name, xe.Message);
            }
        }

        /// <summary>
        /// Reads the contents of an Variant object.
        /// </summary>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public object ReadVariantContents(out TypeInfo typeInfo)
        {
            typeInfo = TypeInfo.Unknown;

            // skip whitespace.
            while (m_reader.NodeType != XmlNodeType.Element)
            {
                m_reader.Read();
            }

            try
            {
                m_namespaces.Push(Namespaces.OpcUaXsd);

                string typeName = m_reader.LocalName;

                // process array types.
                if (typeName.StartsWith("ListOf", StringComparison.Ordinal))
                {
                    switch (typeName.Substring("ListOf".Length))
                    {
                        case "Boolean":
                        {
                            typeInfo = TypeInfo.Arrays.Boolean;
                            BooleanCollection collection = ReadBooleanArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "SByte":
                        {
                            typeInfo = TypeInfo.Arrays.SByte;
                            SByteCollection collection = ReadSByteArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Byte":
                        {
                            typeInfo = TypeInfo.Arrays.Byte;
                            ByteCollection collection = ReadByteArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Int16":
                        {
                            typeInfo = TypeInfo.Arrays.Int16;
                            Int16Collection collection = ReadInt16Array(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "UInt16":
                        {
                            typeInfo = TypeInfo.Arrays.UInt16;
                            UInt16Collection collection = ReadUInt16Array(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Int32":
                        {
                            typeInfo = TypeInfo.Arrays.Int32;
                            Int32Collection collection = ReadInt32Array(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "UInt32":
                        {
                            typeInfo = TypeInfo.Arrays.UInt32;
                            UInt32Collection collection = ReadUInt32Array(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Int64":
                        {
                            typeInfo = TypeInfo.Arrays.Int64;
                            Int64Collection collection = ReadInt64Array(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "UInt64":
                        {
                            typeInfo = TypeInfo.Arrays.UInt64;
                            UInt64Collection collection = ReadUInt64Array(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Float":
                        {
                            typeInfo = TypeInfo.Arrays.Float;
                            FloatCollection collection = ReadFloatArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Double":
                        {
                            typeInfo = TypeInfo.Arrays.Double;
                            DoubleCollection collection = ReadDoubleArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "String":
                        {
                            typeInfo = TypeInfo.Arrays.String;
                            StringCollection collection = ReadStringArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "DateTime":
                        {
                            typeInfo = TypeInfo.Arrays.DateTime;
                            DateTimeCollection collection = ReadDateTimeArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Guid":
                        {
                            typeInfo = TypeInfo.Arrays.Guid;
                            UuidCollection collection = ReadGuidArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "ByteString":
                        {
                            typeInfo = TypeInfo.Arrays.ByteString;
                            ByteStringCollection collection = ReadByteStringArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "XmlElement":
                        {
                            typeInfo = TypeInfo.Arrays.XmlElement;
                            XmlElementCollection collection = ReadXmlElementArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "NodeId":
                        {
                            typeInfo = TypeInfo.Arrays.NodeId;
                            NodeIdCollection collection = ReadNodeIdArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "ExpandedNodeId":
                        {
                            typeInfo = TypeInfo.Arrays.ExpandedNodeId;
                            ExpandedNodeIdCollection collection = ReadExpandedNodeIdArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "StatusCode":
                        {
                            typeInfo = TypeInfo.Arrays.StatusCode;
                            StatusCodeCollection collection = ReadStatusCodeArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "DiagnosticInfo":
                        {
                            typeInfo = TypeInfo.Arrays.DiagnosticInfo;
                            DiagnosticInfoCollection collection = ReadDiagnosticInfoArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "QualifiedName":
                        {
                            typeInfo = TypeInfo.Arrays.QualifiedName;
                            QualifiedNameCollection collection = ReadQualifiedNameArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "LocalizedText":
                        {
                            typeInfo = TypeInfo.Arrays.LocalizedText;
                            LocalizedTextCollection collection = ReadLocalizedTextArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "ExtensionObject":
                        {
                            typeInfo = TypeInfo.Arrays.ExtensionObject;
                            ExtensionObjectCollection collection = ReadExtensionObjectArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "DataValue":
                        {
                            typeInfo = TypeInfo.Arrays.DataValue;
                            DataValueCollection collection = ReadDataValueArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                        case "Variant":
                        {
                            typeInfo = TypeInfo.Arrays.Variant;
                            VariantCollection collection = ReadVariantArray(typeName);
                            return collection != null ? collection.ToArray() : (object)null;
                        }
                    }
                }

                // process scalar types.
                else
                {
                    switch (typeName)
                    {
                        case "Null":
                        {
                            if (BeginField(typeName, true))
                            {
                                EndField(typeName);
                            }

                            return null;
                        }

                        case "Boolean": { typeInfo = TypeInfo.Scalars.Boolean; return ReadBoolean(typeName); }
                        case "SByte": { typeInfo = TypeInfo.Scalars.SByte; return ReadSByte(typeName); }
                        case "Byte": { typeInfo = TypeInfo.Scalars.Byte; return ReadByte(typeName); }
                        case "Int16": { typeInfo = TypeInfo.Scalars.Int16; return ReadInt16(typeName); }
                        case "UInt16": { typeInfo = TypeInfo.Scalars.UInt16; return ReadUInt16(typeName); }
                        case "Int32": { typeInfo = TypeInfo.Scalars.Int32; return ReadInt32(typeName); }
                        case "UInt32": { typeInfo = TypeInfo.Scalars.UInt32; return ReadUInt32(typeName); }
                        case "Int64": { typeInfo = TypeInfo.Scalars.Int64; return ReadInt64(typeName); }
                        case "UInt64": { typeInfo = TypeInfo.Scalars.UInt64; return ReadUInt64(typeName); }
                        case "Float": { typeInfo = TypeInfo.Scalars.Float; return ReadFloat(typeName); }
                        case "Double": { typeInfo = TypeInfo.Scalars.Double; return ReadDouble(typeName); }
                        case "String": { typeInfo = TypeInfo.Scalars.String; return ReadString(typeName); }
                        case "DateTime": { typeInfo = TypeInfo.Scalars.DateTime; return ReadDateTime(typeName); }
                        case "Guid": { typeInfo = TypeInfo.Scalars.Guid; return ReadGuid(typeName); }
                        case "ByteString": { typeInfo = TypeInfo.Scalars.ByteString; return ReadByteString(typeName); }
                        case "XmlElement": { typeInfo = TypeInfo.Scalars.XmlElement; return ReadXmlElement(typeName); }
                        case "NodeId": { typeInfo = TypeInfo.Scalars.NodeId; return ReadNodeId(typeName); }
                        case "ExpandedNodeId": { typeInfo = TypeInfo.Scalars.ExpandedNodeId; return ReadExpandedNodeId(typeName); }
                        case "StatusCode": { typeInfo = TypeInfo.Scalars.StatusCode; return ReadStatusCode(typeName); }
                        case "DiagnosticInfo": { typeInfo = TypeInfo.Scalars.DiagnosticInfo; return ReadDiagnosticInfo(typeName); }
                        case "QualifiedName": { typeInfo = TypeInfo.Scalars.QualifiedName; return ReadQualifiedName(typeName); }
                        case "LocalizedText": { typeInfo = TypeInfo.Scalars.LocalizedText; return ReadLocalizedText(typeName); }
                        case "ExtensionObject": { typeInfo = TypeInfo.Scalars.ExtensionObject; return ReadExtensionObject(typeName); }
                        case "DataValue": { typeInfo = TypeInfo.Scalars.DataValue; return ReadDataValue(typeName); }

                        case "Matrix":
                        {
                            Matrix matrix = ReadMatrix(typeName);
                            typeInfo = matrix.TypeInfo;
                            // return Array for a one dimensional Matrix
                            return typeInfo.ValueRank == ValueRanks.OneDimension ? matrix.Elements : matrix;
                        }
                    }
                }

                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Element '{1}:{0}' is not allowed in a Variant.", m_reader.LocalName, m_reader.NamespaceURI);
            }
            finally
            {
                m_namespaces.Pop();
            }
        }

        /// <summary>
        /// Reads the body extension object from the stream.
        /// </summary>
        public object ReadExtensionObjectBody(ExpandedNodeId typeId)
        {
            m_reader.MoveToContent();

            // check for binary encoded body.
            if (m_reader.LocalName == "ByteString" && m_reader.NamespaceURI == Namespaces.OpcUaXsd)
            {
                PushNamespace(Namespaces.OpcUaXsd);
                byte[] bytes = ReadByteString("ByteString");
                PopNamespace();

                return bytes;
            }

            // lookup type.
            Type systemType = m_context.Factory.GetSystemType(typeId);

            // decode known type.
            if (systemType != null)
            {
                PushNamespace(m_reader.NamespaceURI);
                var encodeable = ReadEncodeable(m_reader.LocalName, systemType, typeId);
                PopNamespace();

                return encodeable;
            }

            string xmlString;
            try
            {
                // return undecoded xml body.
                xmlString = m_reader.ReadOuterXml();
            }
            catch (ArgumentException ae) {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Failed to decode xml extension object body: {0}", ae.Message);
            }

            // check for empty body.
            XmlDocument document = new XmlDocument();

            using (StringReader stream = new StringReader(xmlString))
            using (XmlReader reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings()))
            {
                document.Load(reader);
            }

            return document.DocumentElement;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(m_reader);
                m_reader = null;
            }
        }
        #endregion

        #region IDecoder Members
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Xml;

        /// <summary>
        /// The message context associated with the decoder.
        /// </summary>
        public IServiceMessageContext Context => m_context;

        /// <summary>
        /// Pushes a namespace onto the namespace stack.
        /// </summary>
        public void PushNamespace(string namespaceUri)
        {
            m_namespaces.Push(namespaceUri);
        }

        /// <summary>
        /// Pops a namespace from the namespace stack.
        /// </summary>
        public void PopNamespace()
        {
            m_namespaces.Pop();
        }

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs referenced by the data being decoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being decoded.</param>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && m_context.NamespaceUris != null)
            {
                m_namespaceMappings = m_context.NamespaceUris.CreateMapping(namespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && m_context.ServerUris != null)
            {
                m_serverMappings = m_context.ServerUris.CreateMapping(serverUris, false);
            }
        }

        /// <summary>
        /// Decodes an object from a buffer.
        /// </summary>
        public IEncodeable DecodeMessage(Type expectedType)
        {
            if (expectedType == null) throw new ArgumentNullException(nameof(expectedType));

            XmlQualifiedName typeName = EncodeableFactory.GetXmlName(expectedType);
            string ns = typeName.Namespace;
            string name = typeName.Name;

            int index = name.IndexOf(':');

            if (index != -1)
            {
                name = name.Substring(index + 1);
            }

            PushNamespace(ns);

            // read the message.
            var encodeable = ReadEncodeable(name, expectedType);

            PopNamespace();

            return encodeable;
        }

        /// <summary>
        /// Reads a boolean from the stream.
        /// </summary>
        public bool ReadBoolean(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    bool value = SafeXmlConvert(fieldName, XmlConvert.ToBoolean, xml.ToLowerInvariant());
                    EndField(fieldName);
                    return value;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads a sbyte from the stream.
        /// </summary>
        public sbyte ReadSByte(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    sbyte value = SafeXmlConvert(fieldName, XmlConvert.ToSByte, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a byte from the stream.
        /// </summary>
        public byte ReadByte(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    byte value = SafeXmlConvert(fieldName, XmlConvert.ToByte, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a short from the stream.
        /// </summary>
        public short ReadInt16(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    short value = SafeXmlConvert(fieldName, XmlConvert.ToInt16, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a ushort from the stream.
        /// </summary>
        public ushort ReadUInt16(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    ushort value = SafeXmlConvert(fieldName, XmlConvert.ToUInt16, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads an int from the stream.
        /// </summary>
        public int ReadInt32(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int value = SafeXmlConvert(fieldName, XmlConvert.ToInt32, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a uint from the stream.
        /// </summary>
        public uint ReadUInt32(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    uint value = SafeXmlConvert(fieldName, XmlConvert.ToUInt32, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a long from the stream.
        /// </summary>
        public long ReadInt64(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    long value = SafeXmlConvert(fieldName, XmlConvert.ToInt64, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a ulong from the stream.
        /// </summary>
        public ulong ReadUInt64(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    ulong value = SafeXmlConvert(fieldName, XmlConvert.ToUInt64, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a float from the stream.
        /// </summary>
        public float ReadFloat(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    float value = SafeXmlConvert(fieldName, XmlConvert.ToSingle, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a double from the stream.
        /// </summary>
        public double ReadDouble(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    double value = SafeXmlConvert(fieldName, XmlConvert.ToDouble, xml);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Reads a string from the stream.
        /// </summary>
        public string ReadString(string fieldName)
        {
            bool isNil;
            if (BeginField(fieldName, true, out isNil))
            {
                string value = SafeReadString();

                if (value != null)
                {
                    value = value.Trim();
                }

                EndField(fieldName);
                return value;
            }

            return !isNil ? string.Empty : null;
        }

        /// <summary>
        /// Reads a UTC date/time from the stream.
        /// </summary>
        public DateTime ReadDateTime(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                // check the length.
                if (m_context.MaxStringLength > 0 && m_context.MaxStringLength < xml.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                if (!string.IsNullOrEmpty(xml))
                {
                    try
                    {
                        DateTime value = XmlConvert.ToDateTime(xml, XmlDateTimeSerializationMode.Utc);
                        EndField(fieldName);
                        return value;
                    }
                    catch (FormatException fe)
                    {
                        throw CreateBadDecodingError(fieldName, fe);
                    }
                }
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Reads a GUID from the stream.
        /// </summary>
        public Uuid ReadGuid(string fieldName)
        {
            Uuid value = new Uuid();

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                var guidString = ReadString("String");
                PopNamespace();

                try
                {
                    value.GuidString = guidString;
                }
                catch (FormatException fe)
                {
                    throw CreateBadDecodingError(fieldName, fe);
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        public byte[] ReadByteString(string fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                byte[] value;
                try
                {
                    string xml = m_reader.ReadContentAsString();

                    if (!string.IsNullOrEmpty(xml))
                    {
                        value = SafeConvertFromBase64String(xml);
                    }
                    else
                    {
                        value = Array.Empty<byte>();
                    }
                }
                catch (XmlException xe)
                {
                    throw CreateBadDecodingError(fieldName, xe);
                }
                catch (InvalidOperationException ioe)
                {
                    throw CreateBadDecodingError(fieldName, ioe);
                }

                // check the length.
                if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                EndField(fieldName);
                return value;
            }

            return !isNil ? Array.Empty<byte>() : null;
        }

        /// <summary>
        /// Extracts the XML from the reader.
        /// </summary>
        private void ExtractXml(StringBuilder builder)
        {
            builder.Append('<');
            builder.Append(m_reader.Prefix);
            builder.Append(':');
            builder.Append(m_reader.LocalName);

            if (m_reader.HasAttributes)
            {
                for (int ii = 0; ii < m_reader.AttributeCount; ii++)
                {
                    m_reader.MoveToAttribute(ii);

                    builder.Append(' ');
                    builder.Append(m_reader.Name);
                    builder.Append("='");
                    builder.Append(m_reader.Value);
                    builder.Append('\'');
                }

                m_reader.MoveToElement(); // Moves the reader back to the element node.
            }

            m_reader.MoveToContent();

            while (m_reader.NodeType != XmlNodeType.EndElement)
            {
                if (m_reader.IsStartElement())
                {
                    ExtractXml(builder);
                    continue;
                }

                builder.Append(m_reader.ReadContentAsString());
            }

            m_reader.ReadEndElement();
        }

        /// <summary>
        /// Reads an XmlElement from the stream.
        /// </summary>
        public XmlElement ReadXmlElement(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                if (MoveToElement(null))
                {
                    XmlDocument document = new XmlDocument();
                    XmlElement value = document.CreateElement(m_reader.Prefix, m_reader.LocalName, m_reader.NamespaceURI);
                    document.AppendChild(value);

                    if (m_reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            XmlAttribute attribute = document.CreateAttribute(m_reader.Name);
                            attribute.Value = m_reader.Value;
                            value.Attributes.Append(attribute);
                        }
                        while (m_reader.MoveToNextAttribute());

                        m_reader.MoveToContent();
                    }

                    value.InnerXml = m_reader.ReadInnerXml();

                    EndField(fieldName);
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads an NodeId from the stream.
        /// </summary>
        public NodeId ReadNodeId(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string identifierText = ReadString("Identifier");
                PopNamespace();

                NodeId value;
                try
                {
                    value = NodeId.Parse(identifierText);
                }
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadNodeIdInvalid)
                {
                    throw CreateBadDecodingError(fieldName, sre);
                }
                catch (ArgumentException ae)
                {
                    throw CreateBadDecodingError(fieldName, ae);
                }

                EndField(fieldName);

	            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex)
	            {
	                value.SetNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
	            }

                return value;
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Reads an ExpandedNodeId from the stream.
        /// </summary>
        public ExpandedNodeId ReadExpandedNodeId(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string identifierText = ReadString("Identifier");
                PopNamespace();

                ExpandedNodeId value;
                try
                {
                    value = ExpandedNodeId.Parse(identifierText);
                }
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadNodeIdInvalid)
                {
                    throw CreateBadDecodingError(fieldName, sre);
                }
                catch (ArgumentException ae)
                {
                    throw CreateBadDecodingError(fieldName, ae);
                }

                EndField(fieldName);

	            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex && !value.IsNull)
	            {
	                value.SetNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
	            }

	            if (m_serverMappings != null && m_serverMappings.Length > value.ServerIndex && !value.IsNull)
	            {
	                value.SetServerIndex(m_serverMappings[value.ServerIndex]);
	            }

            	return value;
            }

            return ExpandedNodeId.Null;
        }

        /// <summary>
        /// Reads an StatusCode from the stream.
        /// </summary>
        public StatusCode ReadStatusCode(string fieldName)
        {
            StatusCode value = new StatusCode();

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                value.Code = ReadUInt32("Code");
                PopNamespace();

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// </summary>
        public DiagnosticInfo ReadDiagnosticInfo(string fieldName)
        {
            DiagnosticInfo value = null;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                value = ReadDiagnosticInfo(0);
                PopNamespace();

                EndField(fieldName);
                return value;
            }

            return value;
        }

        /// <summary>
        /// Reads an QualifiedName from the stream.
        /// </summary>
        public QualifiedName ReadQualifiedName(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                ushort namespaceIndex = 0;

                if (BeginField("NamespaceIndex", true))
                {
                    namespaceIndex = ReadUInt16(null);
                    EndField("NamespaceIndex");
                }

                string name = null;

                if (BeginField("Name", true, out bool isNil))
                {
                    name = ReadString(null);
                    EndField("Name");
                }
                else if (!isNil)
                {
                    name = string.Empty;
                }

                PopNamespace();
                EndField(fieldName);

                if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                {
                    namespaceIndex = m_namespaceMappings[namespaceIndex];
                }

                return new QualifiedName(name, namespaceIndex);
            }

            return new QualifiedName();
        }

        /// <summary>
        /// Reads an LocalizedText from the stream.
        /// </summary>
        public LocalizedText ReadLocalizedText(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string text = null;
                string locale = null;

                if (BeginField("Locale", true, out bool isNil))
                {
                    locale = ReadString(null);
                    EndField("Locale");
                }
                else if (!isNil)
                {
                    locale = string.Empty;
                }

                if (BeginField("Text", true, out isNil))
                {
                    text = ReadString(null);
                    EndField("Text");
                }
                else if (!isNil)
                {
                    text = string.Empty;
                }

                LocalizedText value = new LocalizedText(locale, text);

                PopNamespace();

                EndField(fieldName);
                return value;
            }

            return LocalizedText.Null;
        }

        /// <summary>
        /// Reads an Variant from the stream.
        /// </summary>
        public Variant ReadVariant(string fieldName)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                Variant value = new Variant();

                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    if (BeginField("Value", true))
                    {
                        try
                        {
                            TypeInfo typeInfo = null;
                            object contents = ReadVariantContents(out typeInfo);
                            value = new Variant(contents, typeInfo);
                        }
                        catch (Exception ex) when (!(ex is ServiceResultException))
                        {
                            Utils.LogError(ex, "XmlDecoder: Error reading variant. {0}", ex.Message);
                            value = new Variant((StatusCode)StatusCodes.BadDecodingError);
                        }
                        EndField("Value");
                    }

                    PopNamespace();

                    EndField(fieldName);
                }

                return value;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Reads an DataValue from the stream.
        /// </summary>
        public DataValue ReadDataValue(string fieldName)
        {
            DataValue value = new DataValue();

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                value.WrappedValue = ReadVariant("Value");
                value.StatusCode = ReadStatusCode("StatusCode");
                value.SourceTimestamp = ReadDateTime("SourceTimestamp");
                value.SourcePicoseconds = ReadUInt16("SourcePicoseconds");
                value.ServerTimestamp = ReadDateTime("ServerTimestamp");
                value.ServerPicoseconds = ReadUInt16("ServerPicoseconds");

                PopNamespace();

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads an extension object from the stream.
        /// </summary>
        public ExtensionObject ReadExtensionObject(string fieldName)
        {
            bool isNil;

            if (!BeginField(fieldName, true, out isNil))
            {
                return isNil ? null : ExtensionObject.Null;
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // read local type id.
            NodeId typeId = ReadNodeId("TypeId");

            // convert to absolute type id.
            ExpandedNodeId absoluteId = NodeId.ToExpandedNodeId(typeId, m_context.NamespaceUris);

            if (!NodeId.IsNull(typeId) && NodeId.IsNull(absoluteId))
            {
                Utils.LogWarning(
                    "Cannot de-serialize extension objects if the NamespaceUri is not in the NamespaceTable: Type = {0}",
                    typeId);
            }

            // read body.
            if (!BeginField("Body", true))
            {
                // read end of extension object.
                EndField(fieldName);
                PopNamespace();

                return new ExtensionObject(absoluteId);
            }

            // read the body.
            object body = ReadExtensionObjectBody(absoluteId);

            // read end of body.
            EndField("Body");
            PopNamespace();

            // read end of extension object.
            EndField(fieldName);

            if (body is IEncodeable encodeable)
            {
                // Set the known TypeId for encodeables.
                absoluteId = encodeable.TypeId;
            }

            return new ExtensionObject(absoluteId, body);
        }

        /// <summary>
        /// Reads an encodeable object from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable object field name</param>
        /// <param name="systemType">The system type of the encodeable object to be read</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instance that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> object that was read from the stream.</returns>
        public IEncodeable ReadEncodeable(string fieldName, Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null) throw new ArgumentNullException(nameof(systemType));

            if (!(Activator.CreateInstance(systemType) is IEncodeable value))
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Type does not support IEncodeable interface: '{0}'", systemType.FullName);
            }

            if (encodeableTypeId != null)
            {
                // set type identifier for custom complex data types before decode.

                if (value is IComplexTypeInstance complexTypeInstance)
                {
                    complexTypeInstance.TypeId = encodeableTypeId;
                }
            }

            CheckAndIncrementNestingLevel();

            try
            {
                if (BeginField(fieldName, true))
                {
                    XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(value, this.Context);

                    PushNamespace(xmlName.Namespace);
                    value.Decode(this);
                    PopNamespace();

                    // skip to end of encodeable object.
                    m_reader.MoveToContent();

                    while (!(m_reader.NodeType == XmlNodeType.EndElement && m_reader.LocalName == fieldName && m_reader.NamespaceURI == m_namespaces.Peek()))
                    {
                        if (m_reader.NodeType == XmlNodeType.None)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                                "Unexpected end of stream decoding field '{0}' for type '{1}'.", fieldName, systemType.FullName);
                        }

                        m_reader.Skip();
                        m_reader.MoveToContent();
                    }

                    EndField(fieldName);
                }
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Error decoding field '{0}' for type '{1}': {2}", fieldName, systemType.Name, xe.Message);
            }
            finally
            {
                m_nestingLevel--;
            }

            return value;
        }

        /// <summary>
        ///  Reads an enumerated value from the stream.
        /// </summary>
        public Enum ReadEnumerated(string fieldName, Type enumType)
        {
            Enum value = (Enum)Enum.GetValues(enumType).GetValue(0);

            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int index = xml.LastIndexOf('_');

                    try
                    {
                        if (index != -1)
                        {
                            int numericValue = Convert.ToInt32(xml.Substring(index + 1), CultureInfo.InvariantCulture);
                            value = (Enum)Enum.ToObject(enumType, numericValue);
                        }
                        else
                        {
                            value = (Enum)Enum.Parse(enumType, xml, false);
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
                    {
                        throw CreateBadDecodingError(fieldName, ex);
                    }
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads a boolean array from the stream.
        /// </summary>
        public BooleanCollection ReadBooleanArray(string fieldName)
        {
            BooleanCollection values = new BooleanCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Boolean"))
                {
                    values.Add(ReadBoolean("Boolean"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a sbyte array from the stream.
        /// </summary>
        public SByteCollection ReadSByteArray(string fieldName)
        {
            SByteCollection values = new SByteCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("SByte"))
                {
                    values.Add(ReadSByte("SByte"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a byte array from the stream.
        /// </summary>
        public ByteCollection ReadByteArray(string fieldName)
        {
            ByteCollection values = new ByteCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Byte"))
                {
                    values.Add(ReadByte("Byte"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a short array from the stream.
        /// </summary>
        public Int16Collection ReadInt16Array(string fieldName)
        {
            Int16Collection values = new Int16Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int16"))
                {
                    values.Add(ReadInt16("Int16"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a ushort array from the stream.
        /// </summary>
        public UInt16Collection ReadUInt16Array(string fieldName)
        {
            UInt16Collection values = new UInt16Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt16"))
                {
                    values.Add(ReadUInt16("UInt16"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a int array from the stream.
        /// </summary>
        public Int32Collection ReadInt32Array(string fieldName)
        {
            Int32Collection values = new Int32Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int32"))
                {
                    values.Add(ReadInt32("Int32"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a uint array from the stream.
        /// </summary>
        public UInt32Collection ReadUInt32Array(string fieldName)
        {
            UInt32Collection values = new UInt32Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt32"))
                {
                    values.Add(ReadUInt32("UInt32"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a long array from the stream.
        /// </summary>
        public Int64Collection ReadInt64Array(string fieldName)
        {
            Int64Collection values = new Int64Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int64"))
                {
                    values.Add(ReadInt64("Int64"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a ulong array from the stream.
        /// </summary>
        public UInt64Collection ReadUInt64Array(string fieldName)
        {
            UInt64Collection values = new UInt64Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt64"))
                {
                    values.Add(ReadUInt64("UInt64"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a float array from the stream.
        /// </summary>
        public FloatCollection ReadFloatArray(string fieldName)
        {
            FloatCollection values = new FloatCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Float"))
                {
                    values.Add(ReadFloat("Float"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a double array from the stream.
        /// </summary>
        public DoubleCollection ReadDoubleArray(string fieldName)
        {
            DoubleCollection values = new DoubleCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Double"))
                {
                    values.Add(ReadDouble("Double"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a string array from the stream.
        /// </summary>
        public StringCollection ReadStringArray(string fieldName)
        {
            StringCollection values = new StringCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("String"))
                {
                    values.Add(ReadString("String"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a UTC date/time array from the stream.
        /// </summary>
        public DateTimeCollection ReadDateTimeArray(string fieldName)
        {
            DateTimeCollection values = new DateTimeCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DateTime"))
                {
                    values.Add(ReadDateTime("DateTime"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a GUID array from the stream.
        /// </summary>
        public UuidCollection ReadGuidArray(string fieldName)
        {
            UuidCollection values = new UuidCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Guid"))
                {
                    values.Add(ReadGuid("Guid"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads a byte string array from the stream.
        /// </summary>
        public ByteStringCollection ReadByteStringArray(string fieldName)
        {
            ByteStringCollection values = new ByteStringCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ByteString"))
                {
                    values.Add(ReadByteString("ByteString"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an XmlElement array from the stream.
        /// </summary>
        public XmlElementCollection ReadXmlElementArray(string fieldName)
        {
            XmlElementCollection values = new XmlElementCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("XmlElement"))
                {
                    values.Add(ReadXmlElement("XmlElement"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an NodeId array from the stream.
        /// </summary>
        public NodeIdCollection ReadNodeIdArray(string fieldName)
        {
            NodeIdCollection values = new NodeIdCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("NodeId"))
                {
                    values.Add(ReadNodeId("NodeId"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an ExpandedNodeId array from the stream.
        /// </summary>
        public ExpandedNodeIdCollection ReadExpandedNodeIdArray(string fieldName)
        {
            ExpandedNodeIdCollection values = new ExpandedNodeIdCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExpandedNodeId"))
                {
                    values.Add(ReadExpandedNodeId("ExpandedNodeId"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an StatusCode array from the stream.
        /// </summary>
        public StatusCodeCollection ReadStatusCodeArray(string fieldName)
        {
            StatusCodeCollection values = new StatusCodeCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("StatusCode"))
                {
                    values.Add(ReadStatusCode("StatusCode"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an DiagnosticInfo array from the stream.
        /// </summary>
        public DiagnosticInfoCollection ReadDiagnosticInfoArray(string fieldName)
        {
            DiagnosticInfoCollection values = new DiagnosticInfoCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DiagnosticInfo"))
                {
                    values.Add(ReadDiagnosticInfo("DiagnosticInfo"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an QualifiedName array from the stream.
        /// </summary>
        public QualifiedNameCollection ReadQualifiedNameArray(string fieldName)
        {
            QualifiedNameCollection values = new QualifiedNameCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("QualifiedName"))
                {
                    values.Add(ReadQualifiedName("QualifiedName"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an LocalizedText array from the stream.
        /// </summary>
        public LocalizedTextCollection ReadLocalizedTextArray(string fieldName)
        {
            LocalizedTextCollection values = new LocalizedTextCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("LocalizedText"))
                {
                    values.Add(ReadLocalizedText("LocalizedText"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an Variant array from the stream.
        /// </summary>
        public VariantCollection ReadVariantArray(string fieldName)
        {
            VariantCollection values = new VariantCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Variant"))
                {
                    values.Add(ReadVariant("Variant"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an DataValue array from the stream.
        /// </summary>
        public DataValueCollection ReadDataValueArray(string fieldName)
        {
            DataValueCollection values = new DataValueCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DataValue"))
                {
                    values.Add(ReadDataValue("DataValue"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an array of extension objects from the stream.
        /// </summary>
        public ExtensionObjectCollection ReadExtensionObjectArray(string fieldName)
        {
            ExtensionObjectCollection values = new ExtensionObjectCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExtensionObject"))
                {
                    values.Add(ReadExtensionObject("ExtensionObject"));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? null : values;
        }

        /// <summary>
        /// Reads an encodeable array from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable array field name</param>
        /// <param name="systemType">The system type of the encodeable objects to be read object</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instances that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> array that was read from the stream.</returns>
        public Array ReadEncodeableArray(string fieldName, Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null) throw new ArgumentNullException(nameof(systemType));
            IEncodeableCollection encodeables = new IEncodeableCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(systemType);
                PushNamespace(xmlName.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    encodeables.Add(ReadEncodeable(xmlName.Name, systemType, encodeableTypeId));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < encodeables.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);

                // convert to an array of the specified type.
                Array values = Array.CreateInstance(systemType, encodeables.Count);

                for (int ii = 0; ii < encodeables.Count; ii++)
                {
                    values.SetValue(encodeables[ii], ii);
                }

                return values;
            }

            return isNil ? null : Array.CreateInstance(systemType, 0);
        }

        /// <summary>
        /// Reads an enumerated value array from the stream.
        /// </summary>
        public Array ReadEnumeratedArray(string fieldName, Type enumType)
        {
            if (enumType == null) throw new ArgumentNullException(nameof(enumType));
            List<Enum> enums = new List<Enum>();


            if (BeginField(fieldName, true, out bool isNil))
            {
                XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(enumType);
                PushNamespace(xmlName.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    enums.Add(ReadEnumerated(xmlName.Name, enumType));
                }

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < enums.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();
                EndField(fieldName);

                Array values = Array.CreateInstance(enumType, enums.Count);

                for (int ii = 0; ii < enums.Count; ii++)
                {
                    values.SetValue(enums[ii], ii);
                }

                return values;
            }

            return isNil ? null : Array.CreateInstance(enumType, 0);
        }

        /// <inheritdoc/>
        public Array ReadArray(string fieldName, int valueRank, BuiltInType builtInType, Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (valueRank == ValueRanks.OneDimension)
            {
                /*One dimensional Array parameters are always encoded by wrapping the elements in a container element
                 * and inserting the container into the structure. The name of the container element should be the name of the parameter.
                 * The name of the element in the array shall be the type name.*/
                return ReadArrayElements(fieldName, builtInType, systemType, encodeableTypeId);
            }
            // read matrix/array.
            else if (valueRank > ValueRanks.OneDimension)
            {
                Array elements = null;
                Int32Collection dimensions = null;

                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);
                    // dimensions are written before elements when encoding multi dimensional array!! UA Specs
                    dimensions = ReadInt32Array("Dimensions");

                    elements = ReadArrayElements("Elements", builtInType, systemType, encodeableTypeId);

                    PopNamespace();

                    EndField(fieldName);
                }

                if (elements == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "The Matrix contains invalid elements");
                }

                Matrix matrix;
                if (dimensions != null && dimensions.Count > 0)
                {
                    matrix = new Matrix(elements, builtInType, dimensions.ToArray());
                }
                else
                {
                    matrix = new Matrix(elements, builtInType);
                }

                return matrix.ToArray();
            }

            throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                "Invalid ValueRank {0} for Array", valueRank);
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string fieldName)
        {
            fieldName = null;
            return ReadUInt32("SwitchField");
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks) => ReadUInt32("EncodingMask");
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfo nesting level.
        /// </summary>
        private DiagnosticInfo ReadDiagnosticInfo(int depth)
        {
            if (depth >= DiagnosticInfo.MaxInnerDepth)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of InnerDiagnosticInfo was exceeded");
            }

            CheckAndIncrementNestingLevel();

            try
            {
                DiagnosticInfo value = new DiagnosticInfo();
                bool hasDiagnosticInfo = false;

                if (BeginField("SymbolicId", true))
                {
                    value.SymbolicId = ReadInt32(null);
                    EndField("SymbolicId");
                    hasDiagnosticInfo = true;
                }

                if (BeginField("NamespaceUri", true))
                {
                    value.NamespaceUri = ReadInt32(null);
                    EndField("NamespaceUri");
                    hasDiagnosticInfo = true;
                }

                if (BeginField("Locale", true))
                {
                    value.Locale = ReadInt32(null);
                    EndField("Locale");
                    hasDiagnosticInfo = true;
                }

                if (BeginField("LocalizedText", true))
                {
                    value.LocalizedText = ReadInt32(null);
                    EndField("LocalizedText");
                    hasDiagnosticInfo = true;
                }

                value.AdditionalInfo = ReadString("AdditionalInfo");
                value.InnerStatusCode = ReadStatusCode("InnerStatusCode");

                hasDiagnosticInfo = hasDiagnosticInfo || value.AdditionalInfo != null || value.InnerStatusCode != StatusCodes.Good;

                if (BeginField("InnerDiagnosticInfo", true))
                {
                    value.InnerDiagnosticInfo = ReadDiagnosticInfo(depth + 1);
                    EndField("InnerDiagnosticInfo");
                    hasDiagnosticInfo = true;
                }

                return hasDiagnosticInfo ? value : null;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Reads a Matrix from the stream.
        /// </summary>
        private Matrix ReadMatrix(string fieldName)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                Array elements = null;
                Int32Collection dimensions = null;
                TypeInfo typeInfo = null;

                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    dimensions = ReadInt32Array("Dimensions");

                    if (BeginField("Elements", true))
                    {
                        typeInfo = MapElementTypeToTypeInfo(m_reader.LocalName);
                        elements = ReadArray(null, typeInfo.ValueRank, typeInfo.BuiltInType, null);
                        EndField("Elements");
                    }

                    PopNamespace();

                    EndField(fieldName);
                }

                if (elements == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "The Matrix contains invalid elements.");
                }

                if (dimensions != null && dimensions.Count > 0)
                {
                    int length = elements.Length;
                    var dimensionsArray = dimensions.ToArray();
                    (bool valid, int matrixLength) = Matrix.ValidateDimensions(dimensionsArray, length, Context.MaxArrayLength);

                    if (!valid || (matrixLength != length))
                    {
                        throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                            "ArrayDimensions length does not match with the ArrayLength in Variant object.");
                    }

                    return new Matrix(elements, typeInfo.BuiltInType, dimensionsArray);
                }

                return new Matrix(elements, typeInfo.BuiltInType);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Maps an element type name to its corresponding TypeInfo.Arrays value.
        /// </summary>
        /// <param name="elementTypeName">The name of the element type.</param>
        /// <returns>The corresponding TypeInfo.Arrays value.</returns>
        private TypeInfo MapElementTypeToTypeInfo(string elementTypeName)
        {
            return elementTypeName switch {
                "Boolean" => TypeInfo.Arrays.Boolean,
                "SByte" => TypeInfo.Arrays.SByte,
                "Byte" => TypeInfo.Arrays.Byte,
                "Int16" => TypeInfo.Arrays.Int16,
                "UInt16" => TypeInfo.Arrays.UInt16,
                "Int32" => TypeInfo.Arrays.Int32,
                "UInt32" => TypeInfo.Arrays.UInt32,
                "Int64" => TypeInfo.Arrays.Int64,
                "UInt64" => TypeInfo.Arrays.UInt64,
                "Float" => TypeInfo.Arrays.Float,
                "Double" => TypeInfo.Arrays.Double,
                "String" => TypeInfo.Arrays.String,
                "DateTime" => TypeInfo.Arrays.DateTime,
                "Guid" => TypeInfo.Arrays.Guid,
                "ByteString" => TypeInfo.Arrays.ByteString,
                "XmlElement" => TypeInfo.Arrays.XmlElement,
                "NodeId" => TypeInfo.Arrays.NodeId,
                "ExpandedNodeId" => TypeInfo.Arrays.ExpandedNodeId,
                "StatusCode" => TypeInfo.Arrays.StatusCode,
                "QualifiedName" => TypeInfo.Arrays.QualifiedName,
                "LocalizedText" => TypeInfo.Arrays.LocalizedText,
                "ExtensionObject" => TypeInfo.Arrays.ExtensionObject,
                "Variant" => TypeInfo.Arrays.Variant,
                "DataValue" => TypeInfo.Arrays.DataValue,
                "DiagnosticInfo" => TypeInfo.Arrays.DiagnosticInfo,
                _ => throw new ServiceResultException(StatusCodes.BadDecodingError, $"Unsupported element type: {elementTypeName}")
            };
        }

        /// <summary>
        /// Read array items from current ListOf element
        /// </summary>
        /// <param name="fieldName">provides the fieldName for the array</param>
        /// <param name="builtInType">provides the BuiltInType of the elements that are read</param>
        /// <param name="systemType">The system type of the elements to read.</param>
        /// <param name="encodeableTypeId">provides the type id of the encodeable element</param>
        private Array ReadArrayElements(string fieldName, BuiltInType builtInType, Type systemType, ExpandedNodeId encodeableTypeId)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                // skip whitespace.
                while (m_reader.NodeType != XmlNodeType.Element)
                {
                    m_reader.Read();
                }

                // process array types.

                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                    {
                        BooleanCollection collection = ReadBooleanArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.SByte:
                    {
                        SByteCollection collection = ReadSByteArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Byte:
                    {
                        ByteCollection collection = ReadByteArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Int16:
                    {
                        Int16Collection collection = ReadInt16Array(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.UInt16:
                    {
                        UInt16Collection collection = ReadUInt16Array(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Enumeration:
                    case BuiltInType.Int32:
                    {
                        Int32Collection collection = ReadInt32Array(fieldName);
                        if (collection != null)
                        {
                            if (builtInType == BuiltInType.Enumeration)
                            {
                                DetermineIEncodeableSystemType(ref systemType, encodeableTypeId);
                                if (systemType?.IsEnum == true)
                                {
                                    Array array = Array.CreateInstance(systemType, collection.Count);
                                    int ii = 0;
                                    foreach (var item in collection)
                                    {
                                        array.SetValue(Enum.ToObject(systemType, item), ii++);
                                    }
                                    return array;
                                }
                            }
                            return collection.ToArray();
                        }
                        return null;
                    }
                    case BuiltInType.UInt32:
                    {
                        UInt32Collection collection = ReadUInt32Array(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Int64:
                    {
                        Int64Collection collection = ReadInt64Array(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.UInt64:
                    {
                        UInt64Collection collection = ReadUInt64Array(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Float:
                    {
                        FloatCollection collection = ReadFloatArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Double:
                    {
                        DoubleCollection collection = ReadDoubleArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.String:
                    {
                        StringCollection collection = ReadStringArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.DateTime:
                    {
                        DateTimeCollection collection = ReadDateTimeArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Guid:
                    {
                        UuidCollection collection = ReadGuidArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.ByteString:
                    {
                        ByteStringCollection collection = ReadByteStringArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.XmlElement:
                    {
                        XmlElementCollection collection = ReadXmlElementArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.NodeId:
                    {
                        NodeIdCollection collection = ReadNodeIdArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.ExpandedNodeId:
                    {
                        ExpandedNodeIdCollection collection = ReadExpandedNodeIdArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.StatusCode:
                    {
                        StatusCodeCollection collection = ReadStatusCodeArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.DiagnosticInfo:
                    {
                        DiagnosticInfoCollection collection = ReadDiagnosticInfoArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.QualifiedName:
                    {
                        QualifiedNameCollection collection = ReadQualifiedNameArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.LocalizedText:
                    {
                        LocalizedTextCollection collection = ReadLocalizedTextArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.ExtensionObject:
                    {
                        ExtensionObjectCollection collection = ReadExtensionObjectArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.DataValue:
                    {
                        DataValueCollection collection = ReadDataValueArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    case BuiltInType.Variant:
                    {
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }

                        VariantCollection collection = ReadVariantArray(fieldName);
                        return collection != null ? collection.ToArray() : (Array)null;
                    }
                    default:
                    {
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Cannot decode unknown type in Array object with BuiltInType: {0}.",
                            builtInType);
                    }
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Reads a string from the stream.
        /// </summary>
        private string SafeReadString([CallerMemberName] string functionName = null)
        {
            string message;
            try
            {
                string value = m_reader.ReadContentAsString();

                // check the length.
                if (value != null)
                {
                    if (m_context.MaxStringLength > 0 && m_context.MaxStringLength < value.Length)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                            "ReadString in {0} exceeds MaxStringLength: {1} > {2}", functionName, value.Length, m_context.MaxStringLength);
                    }
                }

                return value;
            }
            catch (XmlException xe)
            {
                message = xe.Message;
            }
            catch (InvalidOperationException ioe)
            {
                message = ioe.Message;
            }
            throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                "Unable to read string of {0}: {1}", functionName, message);
        }

        private byte[] SafeConvertFromBase64String(string s)
        {
            try
            {
                return Convert.FromBase64String(s);
            }
            catch (FormatException fe)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Error decoding base64 string: {0}", fe.Message);
            }
        }

        /// <summary>
        /// Reads the start of field where the presences of the xsi:nil attribute is not significant.
        /// </summary>
        private bool BeginField(string fieldName, bool isOptional)
        {
            return BeginField(fieldName, isOptional, out _);
        }

        /// <summary>
        /// Reads the start of field.
        /// </summary>
        private bool BeginField(string fieldName, bool isOptional, out bool isNil)
        {
            try
            {
                isNil = false;

                // move to the next node.
                m_reader.MoveToContent();

                // allow caller to skip reading element tag if field name is not specified.
                if (string.IsNullOrEmpty(fieldName))
                {
                    return true;
                }

                // check if requested element is present.
                if (!m_reader.IsStartElement(fieldName, m_namespaces.Peek()))
                {
                    if (!isOptional)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                            "Encountered element: '{1}:{0}' when expecting element: '{2}:{3}'.", m_reader.LocalName, m_reader.NamespaceURI, fieldName, m_namespaces.Peek());
                    }

                    isNil = true;

                    // nothing more to read.
                    return false;
                }

                // check for empty or nil element.
                if (m_reader.HasAttributes)
                {
                    string nilValue = m_reader.GetAttribute("nil", Namespaces.XmlSchemaInstance);

                    if (!string.IsNullOrEmpty(nilValue))
                    {

                        if (SafeXmlConvert(fieldName, XmlConvert.ToBoolean, nilValue))
                        {
                            isNil = true;
                        }
                    }
                }

                bool isEmpty = m_reader.IsEmptyElement;

                m_reader.ReadStartElement();

                if (!isEmpty)
                {
                    m_reader.MoveToContent();

                    // check for an element with no children but not empty (due to whitespace).
                    if (m_reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (m_reader.LocalName == fieldName && m_reader.NamespaceURI == m_namespaces.Peek())
                        {
                            m_reader.ReadEndElement();
                            return false;
                        }
                    }
                }

                // caller must read contents of element.
                return !isNil && !isEmpty;
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Unable to read field {0}: {1}", fieldName, xe.Message);
            }
        }

        /// <summary>
        /// Reads the end of a field.
        /// </summary>
        private void EndField(string fieldName)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                try
                {
                    m_reader.MoveToContent();

                    if (m_reader.NodeType != XmlNodeType.EndElement || m_reader.LocalName != fieldName || m_reader.NamespaceURI != m_namespaces.Peek())
                    {
                        throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                            "Encountered end element: '{1}:{0}' when expecting element: '{3}:{2}'.", m_reader.LocalName, m_reader.NamespaceURI, fieldName, m_namespaces.Peek());
                    }

                    m_reader.ReadEndElement();
                }
                catch (XmlException xe)
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                        "Unable to read end field: {0}: {1}", fieldName, xe.Message);
                }
            }
        }

        /// <summary>
        /// Moves to the next start element.
        /// </summary>
        private bool MoveToElement(string elementName)
        {
            while (!m_reader.IsStartElement())
            {
                if (m_reader.NodeType == XmlNodeType.None || m_reader.NodeType == XmlNodeType.EndElement)
                {
                    return false;
                }

                m_reader.Read();
            }

            if (string.IsNullOrEmpty(elementName))
            {
                return true;
            }

            return (m_reader.LocalName == elementName && m_reader.NamespaceURI == m_namespaces.Peek());
        }

        /// <summary>
        /// Get the system type from the type factory if not specified by caller.
        /// </summary>
        /// <param name="systemType">The reference to the system type, or null</param>
        /// <param name="encodeableTypeId">The encodeable type id of the system type.</param>
        /// <returns>If the system type is assignable to <see cref="IEncodeable"/> </returns>
        private bool DetermineIEncodeableSystemType(ref Type systemType, ExpandedNodeId encodeableTypeId)
        {
            if (encodeableTypeId != null && systemType == null)
            {
                systemType = Context.Factory.GetSystemType(encodeableTypeId);
            }
            return typeof(IEncodeable).IsAssignableFrom(systemType);
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        private void CheckAndIncrementNestingLevel([CallerMemberName] string functionName = null)
        {
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} in function {1} was exceeded", m_context.MaxEncodingNestingLevels, functionName);
            }
            m_nestingLevel++;
        }

        /// <summary>
        /// Helper to create a BadDecodingError exception.
        /// </summary>
        private ServiceResultException CreateBadDecodingError(string fieldName, Exception ex, [CallerMemberName] string functionName = null)
        {
            return ServiceResultException.Create(StatusCodes.BadDecodingError,
                "Unable to read field {0} in function {1}: {2}", fieldName, functionName, ex.Message);
        }

        /// <summary>
        /// Wrapper for XmlConvert calls which catches the
        /// <see cref="FormatException"/> or <see cref="OverflowException"/>"
        /// and throws instead a <see cref="ServiceResultException"/> with
        /// StatusCode <see cref="StatusCodes.BadDecodingError"/>.
        /// </summary>
        private T SafeXmlConvert<T>(string fieldName, Func<string, T> converter, string xml, [CallerMemberName] string functionName = null)
        {
            try
            {
                return converter(xml);
            }
            catch (OverflowException ove)
            {
                throw CreateBadDecodingError(fieldName, ove, functionName);
            }
            catch (FormatException fe)
            {
                throw CreateBadDecodingError(fieldName, fe, functionName);
            }
        }
        #endregion

        #region Private Fields
        private XmlReader m_reader;
        private Stack<string> m_namespaces;
        private IServiceMessageContext m_context;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        #endregion
    }
}
