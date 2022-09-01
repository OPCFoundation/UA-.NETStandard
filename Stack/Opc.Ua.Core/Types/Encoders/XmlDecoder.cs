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
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Reads objects from a XML stream.
    /// </summary>
    public class XmlDecoder : IDecoder, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public XmlDecoder(IServiceMessageContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Initialize();
            m_context = context;
            m_nestingLevel = 0;
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
        public XmlDecoder(System.Type systemType, XmlReader reader, IServiceMessageContext context)
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
        /// Initializes the tables used to map namespace and server uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being decoded.</param>
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
            m_reader.Dispose();
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

            m_reader.Dispose();
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

            if (m_namespaces.Peek() != m_reader.NamespaceURI)
            {
                return false;
            }

            return true;
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
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "SByte":
                        {
                            typeInfo = TypeInfo.Arrays.SByte;
                            SByteCollection collection = ReadSByteArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Byte":
                        {
                            typeInfo = TypeInfo.Arrays.Byte;
                            ByteCollection collection = ReadByteArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Int16":
                        {
                            typeInfo = TypeInfo.Arrays.Int16;
                            Int16Collection collection = ReadInt16Array(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "UInt16":
                        {
                            typeInfo = TypeInfo.Arrays.UInt16;
                            UInt16Collection collection = ReadUInt16Array(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Int32":
                        {
                            typeInfo = TypeInfo.Arrays.Int32;
                            Int32Collection collection = ReadInt32Array(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "UInt32":
                        {
                            typeInfo = TypeInfo.Arrays.UInt32;
                            UInt32Collection collection = ReadUInt32Array(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Int64":
                        {
                            typeInfo = TypeInfo.Arrays.Int64;
                            Int64Collection collection = ReadInt64Array(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "UInt64":
                        {
                            typeInfo = TypeInfo.Arrays.UInt64;
                            UInt64Collection collection = ReadUInt64Array(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Float":
                        {
                            typeInfo = TypeInfo.Arrays.Float;
                            FloatCollection collection = ReadFloatArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Double":
                        {
                            typeInfo = TypeInfo.Arrays.Double;
                            DoubleCollection collection = ReadDoubleArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "String":
                        {
                            typeInfo = TypeInfo.Arrays.String;
                            StringCollection collection = ReadStringArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "DateTime":
                        {
                            typeInfo = TypeInfo.Arrays.DateTime;
                            DateTimeCollection collection = ReadDateTimeArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Guid":
                        {
                            typeInfo = TypeInfo.Arrays.Guid;
                            UuidCollection collection = ReadGuidArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "ByteString":
                        {
                            typeInfo = TypeInfo.Arrays.ByteString;
                            ByteStringCollection collection = ReadByteStringArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "XmlElement":
                        {
                            typeInfo = TypeInfo.Arrays.XmlElement;
                            XmlElementCollection collection = ReadXmlElementArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "NodeId":
                        {
                            typeInfo = TypeInfo.Arrays.NodeId;
                            NodeIdCollection collection = ReadNodeIdArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "ExpandedNodeId":
                        {
                            typeInfo = TypeInfo.Arrays.ExpandedNodeId;
                            ExpandedNodeIdCollection collection = ReadExpandedNodeIdArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "StatusCode":
                        {
                            typeInfo = TypeInfo.Arrays.StatusCode;
                            StatusCodeCollection collection = ReadStatusCodeArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "DiagnosticInfo":
                        {
                            typeInfo = TypeInfo.Arrays.DiagnosticInfo;
                            DiagnosticInfoCollection collection = ReadDiagnosticInfoArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "QualifiedName":
                        {
                            typeInfo = TypeInfo.Arrays.QualifiedName;
                            QualifiedNameCollection collection = ReadQualifiedNameArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "LocalizedText":
                        {
                            typeInfo = TypeInfo.Arrays.LocalizedText;
                            LocalizedTextCollection collection = ReadLocalizedTextArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "ExtensionObject":
                        {
                            typeInfo = TypeInfo.Arrays.ExtensionObject;
                            ExtensionObjectCollection collection = ReadExtensionObjectArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "DataValue":
                        {
                            typeInfo = TypeInfo.Arrays.DataValue;
                            DataValueCollection collection = ReadDataValueArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
                        }
                        case "Variant":
                        {
                            typeInfo = TypeInfo.Arrays.Variant;
                            VariantCollection collection = ReadVariantArray(typeName);
                            if (collection != null) return collection.ToArray();
                            return null;
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
                            return matrix;
                        }
                    }
                }

                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    Utils.Format("Element '{1}:{0}' is not allowed in an Variant.", m_reader.LocalName, m_reader.NamespaceURI));
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

            // check for empty body.
            XmlDocument document = new XmlDocument();

            // return undecoded xml body.
            var xmlString = m_reader.ReadOuterXml();

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
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_reader != null)
                {
                    m_reader.Dispose();
                }
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
        /// Reads a boolean from the stream.
        /// </summary>
        public bool ReadBoolean(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    bool value = XmlConvert.ToBoolean(xml.ToLowerInvariant());
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    sbyte value = XmlConvert.ToSByte(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    byte value = XmlConvert.ToByte(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    short value = XmlConvert.ToInt16(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    ushort value = XmlConvert.ToUInt16(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    int value = XmlConvert.ToInt32(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    uint value = XmlConvert.ToUInt32(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    long value = XmlConvert.ToInt64(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    ulong value = XmlConvert.ToUInt64(xml);
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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    float value = 0;

                    if (xml.Length == 3)
                    {
                        if (xml == "NaN")
                        {
                            value = Single.NaN;
                        }

                        if (xml == "INF")
                        {
                            value = Single.PositiveInfinity;
                        }
                    }

                    if (xml.Length == 4)
                    {
                        if (xml == "-INF")
                        {
                            value = Single.NegativeInfinity;
                        }
                    }

                    if (value == 0)
                    {
                        value = XmlConvert.ToSingle(xml);
                    }

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
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    double value = 0;

                    if (xml.Length == 3)
                    {
                        if (xml == "NaN")
                        {
                            value = Single.NaN;
                        }

                        if (xml == "INF")
                        {
                            value = Single.PositiveInfinity;
                        }
                    }

                    if (xml.Length == 4)
                    {
                        if (xml == "-INF")
                        {
                            value = Single.NegativeInfinity;
                        }
                    }

                    if (value == 0)
                    {
                        value = XmlConvert.ToDouble(xml);
                    }

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
            bool isNil = false;

            if (BeginField(fieldName, true, out isNil))
            {
                string value = ReadString();

                if (value != null)
                {
                    value = value.Trim();
                }

                EndField(fieldName);
                return value;
            }

            if (!isNil)
            {
                return String.Empty;
            }

            return null;
        }

        /// <summary>
        /// Reads a UTC date/time from the stream.
        /// </summary>
        public DateTime ReadDateTime(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = ReadString();

                // check the length.
                if (m_context.MaxStringLength > 0 && m_context.MaxStringLength < xml.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                if (!String.IsNullOrEmpty(xml))
                {
                    DateTime value = XmlConvert.ToDateTime(xml, XmlDateTimeSerializationMode.Utc);
                    EndField(fieldName);
                    return value;
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
                value.GuidString = ReadString("String");
                PopNamespace();

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        public byte[] ReadByteString(string fieldName)
        {
            bool isNil = false;

            if (BeginField(fieldName, true, out isNil))
            {
                byte[] value = null;

                string xml = m_reader.ReadContentAsString();

                if (!String.IsNullOrEmpty(xml))
                {
                    value = Convert.FromBase64String(xml);
                }
                else
                {
                    value = Array.Empty<byte>();
                }

                // check the length.
                if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                EndField(fieldName);
                return value;
            }

            if (!isNil)
            {
                return Array.Empty<byte>();
            }

            return null;
        }

        /// <summary>
        /// Exracts the XML from the reader.
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
            NodeId value = new NodeId();

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                value.IdentifierText = ReadString("Identifier");
                PopNamespace();

                EndField(fieldName);
            }

            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex)
            {
                value.SetNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
            }

            return value;
        }

        /// <summary>
        /// Reads an ExpandedNodeId from the stream.
        /// </summary>
        public ExpandedNodeId ReadExpandedNodeId(string fieldName)
        {
            ExpandedNodeId value = new ExpandedNodeId();

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                value.IdentifierText = ReadString("Identifier");
                PopNamespace();

                EndField(fieldName);
            }

            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex)
            {
                value.SetNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
            }

            if (m_serverMappings != null && m_serverMappings.Length > value.ServerIndex)
            {
                value.SetServerIndex(m_serverMappings[value.ServerIndex]);
            }

            return value;
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
                value = ReadDiagnosticInfo();
                PopNamespace();

                EndField(fieldName);
                return value;
            }

            return value;
        }

        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// </summary>
        public DiagnosticInfo ReadDiagnosticInfo()
        {
            // check the nesting level for avoiding a stack overflow.
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }

            m_nestingLevel++;

            DiagnosticInfo value = new DiagnosticInfo();

            if (BeginField("SymbolicId", true))
            {
                value.SymbolicId = ReadInt32(null);
                EndField("SymbolicId");
            }

            if (BeginField("NamespaceUri", true))
            {
                value.NamespaceUri = ReadInt32(null);
                EndField("NamespaceUri");
            }

            if (BeginField("Locale", true))
            {
                value.Locale = ReadInt32(null);
                EndField("Locale");
            }

            if (BeginField("LocalizedText", true))
            {
                value.LocalizedText = ReadInt32(null);
                EndField("LocalizedText");
            }

            value.AdditionalInfo = ReadString("AdditionalInfo");
            value.InnerStatusCode = ReadStatusCode("InnerStatusCode");

            if (BeginField("InnerDiagnosticInfo", true))
            {
                value.InnerDiagnosticInfo = ReadDiagnosticInfo();
                EndField("InnerDiagnosticInfo");
            }

            m_nestingLevel--;

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

                bool isNil = false;
                string name = null;

                if (BeginField("Name", true, out isNil))
                {
                    name = ReadString(null);
                    EndField("Name");
                }
                else if (!isNil)
                {
                    name = String.Empty;
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

                bool isNil = false;
                string text = null;
                string locale = null;

                if (BeginField("Locale", true, out isNil))
                {
                    locale = ReadString(null);
                    EndField("Locale");
                }
                else if (!isNil)
                {
                    locale = String.Empty;
                }

                if (BeginField("Text", true, out isNil))
                {
                    text = ReadString(null);
                    EndField("Text");
                }
                else if (!isNil)
                {
                    text = String.Empty;
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
            // check the nesting level for avoiding a stack overflow.
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }

            m_nestingLevel++;

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
                    catch (Exception ex)
                    {
                        Utils.LogError(ex, "XmlDecoder: Error reading variant.");
                        value = new Variant(StatusCodes.BadDecodingError);
                    }
                    EndField("Value");
                }

                PopNamespace();

                EndField(fieldName);
            }

            m_nestingLevel--;

            return value;
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
                if (isNil)
                {
                    return null;
                }

                return ExtensionObject.Null;
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // read local type id.
            NodeId typeId = ReadNodeId("TypeId");

            // convert to absolute type id.
            ExpandedNodeId absoluteId = NodeId.ToExpandedNodeId(typeId, m_context.NamespaceUris);

            if (!NodeId.IsNull(typeId) && NodeId.IsNull(absoluteId))
            {
                Utils.LogWarning(
                    "Cannot de-serialized extension objects if the NamespaceUri is not in the NamespaceTable: Type = {0}",
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

            IEncodeable encodeable = body as IEncodeable;
            if (encodeable != null)
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
        /// <param name="systemType">The system type of the encopdeable object to be read</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instance that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> object that was read from the stream.</returns>
        public IEncodeable ReadEncodeable(string fieldName, System.Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null) throw new ArgumentNullException(nameof(systemType));

            IEncodeable value = Activator.CreateInstance(systemType) as IEncodeable;

            if (value == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    Utils.Format("Type does not support IEncodeable interface: '{0}'", systemType.FullName));
            }

            if (encodeableTypeId != null)
            {
                // set type identifier for custom complex data types before decode.
                IComplexTypeInstance complexTypeInstance = value as IComplexTypeInstance;

                if (complexTypeInstance != null)
                {
                    complexTypeInstance.TypeId = encodeableTypeId;
                }
            }

            // check the nesting level for avoiding a stack overflow.
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }

            m_nestingLevel++;

            if (BeginField(fieldName, true))
            {
                XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(systemType);

                PushNamespace(xmlName.Namespace);
                value.Decode(this);
                PopNamespace();

                // skip to end of encodeable object.
                m_reader.MoveToContent();

                while (!(m_reader.NodeType == XmlNodeType.EndElement && m_reader.LocalName == fieldName && m_reader.NamespaceURI == m_namespaces.Peek()))
                {
                    if (m_reader.NodeType == XmlNodeType.None)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            Utils.Format("Unexpected end of stream decoding field '{0}' for type '{1}'.", fieldName, systemType.FullName));
                    }

                    m_reader.Skip();
                    m_reader.MoveToContent();
                }

                EndField(fieldName);
            }

            m_nestingLevel--;

            return value;
        }

        /// <summary>
        ///  Reads an enumerated value from the stream.
        /// </summary>
        public Enum ReadEnumerated(string fieldName, System.Type enumType)
        {
            Enum value = (Enum)Enum.GetValues(enumType).GetValue(0);

            if (BeginField(fieldName, true))
            {
                string xml = ReadString();

                if (!String.IsNullOrEmpty(xml))
                {
                    int index = xml.LastIndexOf('_');

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

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads a boolean array from the stream.
        /// </summary>
        public BooleanCollection ReadBooleanArray(string fieldName)
        {
            bool isNil = false;

            BooleanCollection values = new BooleanCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a sbyte array from the stream.
        /// </summary>
        public SByteCollection ReadSByteArray(string fieldName)
        {
            bool isNil = false;

            SByteCollection values = new SByteCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a byte array from the stream.
        /// </summary>
        public ByteCollection ReadByteArray(string fieldName)
        {
            bool isNil = false;

            ByteCollection values = new ByteCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a short array from the stream.
        /// </summary>
        public Int16Collection ReadInt16Array(string fieldName)
        {
            bool isNil = false;

            Int16Collection values = new Int16Collection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a ushort array from the stream.
        /// </summary>
        public UInt16Collection ReadUInt16Array(string fieldName)
        {
            bool isNil = false;

            UInt16Collection values = new UInt16Collection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a int array from the stream.
        /// </summary>
        public Int32Collection ReadInt32Array(string fieldName)
        {
            bool isNil = false;

            Int32Collection values = new Int32Collection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a uint array from the stream.
        /// </summary>
        public UInt32Collection ReadUInt32Array(string fieldName)
        {
            bool isNil = false;

            UInt32Collection values = new UInt32Collection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a long array from the stream.
        /// </summary>
        public Int64Collection ReadInt64Array(string fieldName)
        {
            bool isNil = false;

            Int64Collection values = new Int64Collection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a ulong array from the stream.
        /// </summary>
        public UInt64Collection ReadUInt64Array(string fieldName)
        {
            bool isNil = false;

            UInt64Collection values = new UInt64Collection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a float array from the stream.
        /// </summary>
        public FloatCollection ReadFloatArray(string fieldName)
        {
            bool isNil = false;

            FloatCollection values = new FloatCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a double array from the stream.
        /// </summary>
        public DoubleCollection ReadDoubleArray(string fieldName)
        {
            bool isNil = false;

            DoubleCollection values = new DoubleCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a string array from the stream.
        /// </summary>
        public StringCollection ReadStringArray(string fieldName)
        {
            bool isNil = false;

            StringCollection values = new StringCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a UTC date/time array from the stream.
        /// </summary>
        public DateTimeCollection ReadDateTimeArray(string fieldName)
        {
            bool isNil = false;

            DateTimeCollection values = new DateTimeCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a GUID array from the stream.
        /// </summary>
        public UuidCollection ReadGuidArray(string fieldName)
        {
            bool isNil = false;

            UuidCollection values = new UuidCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads a byte string array from the stream.
        /// </summary>
        public ByteStringCollection ReadByteStringArray(string fieldName)
        {
            bool isNil = false;

            ByteStringCollection values = new ByteStringCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an XmlElement array from the stream.
        /// </summary>
        public XmlElementCollection ReadXmlElementArray(string fieldName)
        {
            bool isNil = false;

            XmlElementCollection values = new XmlElementCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an NodeId array from the stream.
        /// </summary>
        public NodeIdCollection ReadNodeIdArray(string fieldName)
        {
            bool isNil = false;

            NodeIdCollection values = new NodeIdCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an ExpandedNodeId array from the stream.
        /// </summary>
        public ExpandedNodeIdCollection ReadExpandedNodeIdArray(string fieldName)
        {
            bool isNil = false;

            ExpandedNodeIdCollection values = new ExpandedNodeIdCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an StatusCode array from the stream.
        /// </summary>
        public StatusCodeCollection ReadStatusCodeArray(string fieldName)
        {
            bool isNil = false;

            StatusCodeCollection values = new StatusCodeCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an DiagnosticInfo array from the stream.
        /// </summary>
        public DiagnosticInfoCollection ReadDiagnosticInfoArray(string fieldName)
        {
            bool isNil = false;

            DiagnosticInfoCollection values = new DiagnosticInfoCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an QualifiedName array from the stream.
        /// </summary>
        public QualifiedNameCollection ReadQualifiedNameArray(string fieldName)
        {
            bool isNil = false;

            QualifiedNameCollection values = new QualifiedNameCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an LocalizedText array from the stream.
        /// </summary>
        public LocalizedTextCollection ReadLocalizedTextArray(string fieldName)
        {
            bool isNil = false;

            LocalizedTextCollection values = new LocalizedTextCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an Variant array from the stream.
        /// </summary>
        public VariantCollection ReadVariantArray(string fieldName)
        {
            bool isNil = false;

            VariantCollection values = new VariantCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an DataValue array from the stream.
        /// </summary>
        public DataValueCollection ReadDataValueArray(string fieldName)
        {
            bool isNil = false;

            DataValueCollection values = new DataValueCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an array of extension objects from the stream.
        /// </summary>
        public ExtensionObjectCollection ReadExtensionObjectArray(string fieldName)
        {
            bool isNil = false;

            ExtensionObjectCollection values = new ExtensionObjectCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return values;
        }

        /// <summary>
        /// Reads an encodeable array from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable array field name</param>
        /// <param name="systemType">The system type of the encopdeable objects to be read object</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instances that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> array that was read from the stream.</returns>
        public Array ReadEncodeableArray(string fieldName, System.Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null) throw new ArgumentNullException(nameof(systemType));

            bool isNil = false;

            IEncodeableCollection encodeables = new IEncodeableCollection();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return Array.CreateInstance(systemType, 0);
        }

        /// <summary>
        /// Reads an enumerated value array from the stream.
        /// </summary>
        public Array ReadEnumeratedArray(string fieldName, System.Type enumType)
        {
            if (enumType == null) throw new ArgumentNullException(nameof(enumType));

            bool isNil = false;

            List<Enum> enums = new List<Enum>();

            if (BeginField(fieldName, true, out isNil))
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

            if (isNil)
            {
                return null;
            }

            return Array.CreateInstance(enumType, 0);
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
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads an Matrix from the stream.
        /// </summary>
        private Matrix ReadMatrix(string fieldName)
        {
            // check the nesting level for avoiding a stack overflow.
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }

            m_nestingLevel++;

            Array elements = null;
            Int32Collection dimensions = null;
            TypeInfo typeInfo = null;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (BeginField("Elements", true))
                {
                    object contents = ReadVariantContents(out typeInfo);
                    elements = contents as Array;
                    EndField("Elements");
                }

                dimensions = ReadInt32Array("Dimensions");

                PopNamespace();

                EndField(fieldName);
            }

            m_nestingLevel--;

            if (elements == null)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The Matrix contains invalid elements");
            }

            if (dimensions != null && dimensions.Count > 0)
            {
                return new Matrix(elements, typeInfo.BuiltInType, dimensions.ToArray());
            }

            return new Matrix(elements, typeInfo.BuiltInType);
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
            // check the nesting level for avoiding a stack overflow.
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }

            m_nestingLevel++;


            // skip whitespace.
            while (m_reader.NodeType != XmlNodeType.Element)
            {
                m_reader.Read();
            }

            try
            {
                m_namespaces.Push(Namespaces.OpcUaXsd);

                // process array types.

                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                    {
                        BooleanCollection collection = ReadBooleanArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.SByte:
                    {
                        SByteCollection collection = ReadSByteArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.Byte:
                    {
                        ByteCollection collection = ReadByteArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.Int16:
                    {
                        Int16Collection collection = ReadInt16Array(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.UInt16:
                    {
                        UInt16Collection collection = ReadUInt16Array(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
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
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.Int64:
                    {
                        Int64Collection collection = ReadInt64Array(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.UInt64:
                    {
                        UInt64Collection collection = ReadUInt64Array(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.Float:
                    {
                        FloatCollection collection = ReadFloatArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.Double:
                    {
                        DoubleCollection collection = ReadDoubleArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.String:
                    {
                        StringCollection collection = ReadStringArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.DateTime:
                    {
                        DateTimeCollection collection = ReadDateTimeArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.Guid:
                    {
                        UuidCollection collection = ReadGuidArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.ByteString:
                    {
                        ByteStringCollection collection = ReadByteStringArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.XmlElement:
                    {
                        XmlElementCollection collection = ReadXmlElementArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.NodeId:
                    {
                        NodeIdCollection collection = ReadNodeIdArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.ExpandedNodeId:
                    {
                        ExpandedNodeIdCollection collection = ReadExpandedNodeIdArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.StatusCode:
                    {
                        StatusCodeCollection collection = ReadStatusCodeArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.DiagnosticInfo:
                    {
                        DiagnosticInfoCollection collection = ReadDiagnosticInfoArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.QualifiedName:
                    {
                        QualifiedNameCollection collection = ReadQualifiedNameArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.LocalizedText:
                    {
                        LocalizedTextCollection collection = ReadLocalizedTextArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.ExtensionObject:
                    {
                        ExtensionObjectCollection collection = ReadExtensionObjectArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.DataValue:
                    {
                        DataValueCollection collection = ReadDataValueArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
                    }
                    case BuiltInType.Variant:
                    {
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }

                        VariantCollection collection = ReadVariantArray(fieldName);
                        if (collection != null) return collection.ToArray();
                        return null;
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
                m_namespaces.Pop();
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Reads a string from the stream.
        /// </summary>
        private string ReadString()
        {
            string value = m_reader.ReadContentAsString();

            // check the length.
            if (value != null)
            {
                if (m_context.MaxStringLength > 0 && m_context.MaxStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
            }

            return value;
        }

        /// <summary>
        /// Reads the start of filed where the presences of the xsi:nil attribute is not significant.
        /// </summary>
        private bool BeginField(string fieldName, bool isOptional)
        {
            bool isNil = false;
            return BeginField(fieldName, isOptional, out isNil);
        }

        /// <summary>
        /// Reads the start of field.
        /// </summary>
        private bool BeginField(string fieldName, bool isOptional, out bool isNil)
        {
            isNil = false;

            // move to the next node.
            m_reader.MoveToContent();

            // allow caller to skip reading element tag if field name is not specified.
            if (String.IsNullOrEmpty(fieldName))
            {
                return true;
            }

            // check if requested element is present.
            if (!m_reader.IsStartElement(fieldName, m_namespaces.Peek()))
            {
                if (!isOptional)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        Utils.Format("Encountered element: '{1}:{0}' when expecting element: '{2}:{3}'.", m_reader.LocalName, m_reader.NamespaceURI, fieldName, m_namespaces.Peek()));
                }

                isNil = true;

                // nothing more to read.
                return false;
            }

            // check for empty or nil element.
            if (m_reader.HasAttributes)
            {
                string nilValue = m_reader.GetAttribute("nil", Namespaces.XmlSchemaInstance);

                if (!String.IsNullOrEmpty(nilValue))
                {
                    if (XmlConvert.ToBoolean(nilValue))
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

        /// <summary>
        /// Reads the end of a field.
        /// </summary>
        private void EndField(string fieldName)
        {
            if (!String.IsNullOrEmpty(fieldName))
            {
                m_reader.MoveToContent();

                if (m_reader.NodeType != XmlNodeType.EndElement || m_reader.LocalName != fieldName || m_reader.NamespaceURI != m_namespaces.Peek())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        Utils.Format("Encountered end element: '{1}:{0}' when expecting element: '{3}:{2}'.", m_reader.LocalName, m_reader.NamespaceURI, fieldName, m_namespaces.Peek()));
                }

                m_reader.ReadEndElement();
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

            if (String.IsNullOrEmpty(elementName))
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
