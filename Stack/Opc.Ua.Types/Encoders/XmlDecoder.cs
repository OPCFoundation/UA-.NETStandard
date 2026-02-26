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
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Reads objects from a XML stream.
    /// </summary>
    public class XmlDecoder : IDecoder
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public XmlDecoder(XmlReader reader, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlDecoder>();
            m_nestingLevel = 0;
            m_reader = reader;
        }

        /// <summary>
        /// Initializes the object with an XML element to parse.
        /// </summary>
        public XmlDecoder(XmlElement element, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlDecoder>();
            m_reader = XmlReader.Create(
                new StringReader(element.OuterXml),
                CoreUtils.DefaultXmlReaderSettings());
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Initializes the object with an XML element to parse.
        /// </summary>
        public XmlDecoder(System.Xml.XmlElement element, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlDecoder>();
            m_reader = XmlReader.Create(
                new StringReader(element.OuterXml),
                CoreUtils.DefaultXmlReaderSettings());
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Initializes the object with a XML reader.
        /// </summary>
        public XmlDecoder(Type systemType, XmlReader reader, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlDecoder>();
            m_reader = reader;
            m_nestingLevel = 0;

            string ns = null;
            string name = null;

            if (systemType != null)
            {
                XmlQualifiedName typeName = TypeInfo.GetXmlName(systemType);
                ns = typeName.Namespace;
                name = typeName.Name;
            }

            if (ns == null)
            {
                m_reader.MoveToContent();
                ns = m_reader.NamespaceURI;
                name = m_reader.Name;
            }

            int index = name.IndexOf(':', StringComparison.Ordinal);

            if (index != -1)
            {
                name = name[(index + 1)..];
            }

            PushNamespace(ns);
            BeginField(name, false);
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
        /// Skips to the end of the specified element. Assumes we are already in
        /// the element to skip. Will skip all nested elements with the same name
        /// as well.
        /// </summary>
        /// <param name="qname">The qualified name of the element to skip.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void Skip(XmlQualifiedName qname)
        {
            try
            {
                m_reader.MoveToContent();

                int depth = 1;

                // Skip to end passing all nested elements with the same name.
                while (depth > 0 && m_reader.NodeType != XmlNodeType.None)
                {
                    if (m_reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (m_reader.LocalName == qname.Name &&
                            m_reader.NamespaceURI == qname.Namespace)
                        {
                            depth--;
                        }
                    }
                    else if (m_reader.NodeType == XmlNodeType.Element)
                    {
                        if (m_reader.LocalName == qname.Name &&
                            m_reader.NamespaceURI == qname.Namespace)
                        {
                            // depth++;
                            // Handled by skip, skipping the entire tree
                        }
                    }

                    m_reader.Skip();
                    m_reader.MoveToContent();
                }
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Skip {0} failed: {1}",
                    qname.Name,
                    xe.Message);
            }
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string fieldName, TypeInfo typeInfo)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                Variant value = Variant.Null;

                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    value = ReadVariantValue();

                    // Allow reading with unknown type info
                    if (!typeInfo.IsUnknown && value.TypeInfo != typeInfo)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Error reading value as variant. Type mismatch: Expected {0} != Actual {1}",
                            typeInfo, value.TypeInfo);
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

        /// <inheritdoc/>
        public Variant ReadVariantValue()
        {
            // skip whitespace.
            while (m_reader.NodeType != XmlNodeType.Element)
            {
                m_reader.Read();
            }

            try
            {
                m_namespaces.Push(Namespaces.OpcUaXsd);

                string typeName = m_reader.LocalName;

                if (!typeName.StartsWith("ListOf", StringComparison.Ordinal))
                {
                    // process scalar types.
                    switch (typeName)
                    {
                        case "Null":
                            if (BeginField(typeName, true))
                            {
                                EndField(typeName);
                            }
                            return Variant.Null;
                        case "Boolean":
                            return ReadBoolean(typeName);
                        case "SByte":
                            return ReadSByte(typeName);
                        case "Byte":
                            return ReadByte(typeName);
                        case "Int16":
                            return ReadInt16(typeName);
                        case "UInt16":
                            return ReadUInt16(typeName);
                        case "Int32":
                            return ReadInt32(typeName);
                        case "UInt32":
                            return ReadUInt32(typeName);
                        case "Int64":
                            return ReadInt64(typeName);
                        case "UInt64":
                            return ReadUInt64(typeName);
                        case "Float":
                            return ReadFloat(typeName);
                        case "Double":
                            return ReadDouble(typeName);
                        case "String":
                            return ReadString(typeName);
                        case "DateTime":
                            return ReadDateTime(typeName);
                        case "Guid":
                            return ReadGuid(typeName);
                        case "ByteString":
                            return ReadByteString(typeName);
                        case "XmlElement":
                            return ReadXmlElement(typeName);
                        case "NodeId":
                            return ReadNodeId(typeName);
                        case "ExpandedNodeId":
                            return ReadExpandedNodeId(typeName);
                        case "StatusCode":
                            return ReadStatusCode(typeName);
                        case "QualifiedName":
                            return ReadQualifiedName(typeName);
                        case "LocalizedText":
                            return ReadLocalizedText(typeName);
                        case "ExtensionObject":
                            return ReadExtensionObject(typeName);
                        case "DataValue":
                            return ReadDataValue(typeName);
                        case "Matrix":
                            return ReadMatrix(typeName);
                        default:
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Element '{1}:{0}' is not allowed in a Variant.",
                                m_reader.LocalName,
                                m_reader.NamespaceURI);
                    }
                }
                else
                {
                    // process array types.
                    switch (typeName["ListOf".Length..])
                    {
                        case "Boolean":
                            return Variant.From(ReadBooleanArray(typeName));
                        case "SByte":
                            return Variant.From(ReadSByteArray(typeName));
                        case "Byte":
                            return Variant.From(ReadByteArray(typeName));
                        case "Int16":
                            return Variant.From(ReadInt16Array(typeName));
                        case "UInt16":
                            return Variant.From(ReadUInt16Array(typeName));
                        case "Int32":
                            return Variant.From(ReadInt32Array(typeName));
                        case "UInt32":
                            return Variant.From(ReadUInt32Array(typeName));
                        case "Int64":
                            return Variant.From(ReadInt64Array(typeName));
                        case "UInt64":
                            return Variant.From(ReadUInt64Array(typeName));
                        case "Float":
                            return Variant.From(ReadFloatArray(typeName));
                        case "Double":
                            return Variant.From(ReadDoubleArray(typeName));
                        case "String":
                            return Variant.From(ReadStringArray(typeName));
                        case "DateTime":
                            return Variant.From(ReadDateTimeArray(typeName));
                        case "Guid":
                            return Variant.From(ReadGuidArray(typeName));
                        case "ByteString":
                            return Variant.From(ReadByteStringArray(typeName));
                        case "XmlElement":
                            return Variant.From(ReadXmlElementArray(typeName));
                        case "NodeId":
                            return Variant.From(ReadNodeIdArray(typeName));
                        case "ExpandedNodeId":
                            return Variant.From(ReadExpandedNodeIdArray(typeName));
                        case "StatusCode":
                            return Variant.From(ReadStatusCodeArray(typeName));
                        case "QualifiedName":
                            return Variant.From(ReadQualifiedNameArray(typeName));
                        case "LocalizedText":
                            return Variant.From(ReadLocalizedTextArray(typeName));
                        case "ExtensionObject":
                            return Variant.From(ReadExtensionObjectArray(typeName));
                        case "DataValue":
                            return Variant.From(ReadDataValueArray(typeName));
                        case "Variant":
                            return Variant.From(ReadVariantArray(typeName));
                        default:
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Element '{1}:{0}' is not allowed in a Variant.",
                                m_reader.LocalName,
                                m_reader.NamespaceURI);
                    }
                }
            }
            finally
            {
                m_namespaces.Pop();
            }
        }

        /// <summary>
        /// Reads the body extension object from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ExtensionObject ReadExtensionObjectBody(ExpandedNodeId typeId)
        {
            m_reader.MoveToContent();

            // check for binary encoded body.
            if (m_reader.LocalName == "ByteString" && m_reader.NamespaceURI == Namespaces.OpcUaXsd)
            {
                PushNamespace(Namespaces.OpcUaXsd);
                ByteString bytes = ReadByteString("ByteString");
                PopNamespace();

                return new ExtensionObject(typeId, bytes);
            }

            // lookup type.
            Type systemType = Context.Factory.GetSystemType(typeId);

            // decode known type.
            if (systemType != null)
            {
                PushNamespace(m_reader.NamespaceURI);
                IEncodeable encodeable = ReadEncodeable(m_reader.LocalName, systemType, typeId);
                PopNamespace();
                return new ExtensionObject(encodeable);
            }

            try
            {
                var xmlElement = XmlElement.From(m_reader.ReadOuterXml());
                if (!xmlElement.IsValid)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Invalid xml in extension object body: {0}",
                        xmlElement);
                }
                return new ExtensionObject(typeId, xmlElement);
            }
            catch (Exception ae)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Failed to decode xml extension object body: {0}",
                    ae.Message);
            }
        }

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
                CoreUtils.SilentDispose(m_reader);
                m_reader = null;
            }
        }

        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Xml;

        /// <summary>
        /// The message context associated with the decoder.
        /// </summary>
        public IServiceMessageContext Context { get; }

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

            if (namespaceUris != null && Context.NamespaceUris != null)
            {
                m_namespaceMappings = Context.NamespaceUris.CreateMapping(namespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && Context.ServerUris != null)
            {
                m_serverMappings = Context.ServerUris.CreateMapping(serverUris, false);
            }
        }

        /// <summary>
        /// Decodes an object from a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="expectedType"/> is <c>null</c>.</exception>
        public IEncodeable DecodeMessage(Type expectedType)
        {
            if (expectedType == null)
            {
                throw new ArgumentNullException(nameof(expectedType));
            }

            XmlQualifiedName typeName = TypeInfo.GetXmlName(expectedType);
            string ns = typeName.Namespace;
            string name = typeName.Name;

            int index = name.IndexOf(':', StringComparison.Ordinal);

            if (index != -1)
            {
                name = name[(index + 1)..];
            }

            PushNamespace(ns);

            // read the message.
            IEncodeable encodeable = ReadEncodeable(name, expectedType);

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
                    bool value = SafeXmlConvert(
                        fieldName,
                        XmlConvert.ToBoolean,
                        xml.ToLowerInvariant());
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
            if (BeginField(fieldName, true, out bool isNil))
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
        /// <exception cref="ServiceResultException"></exception>
        public DateTime ReadDateTime(string fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string xml = SafeReadString();

                // check the length.
                if (Context.MaxStringLength > 0 && Context.MaxStringLength < xml.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                if (!string.IsNullOrEmpty(xml))
                {
                    try
                    {
                        var value = XmlConvert.ToDateTime(xml, XmlDateTimeSerializationMode.Utc);
                        EndField(fieldName);
                        return value;
                    }
                    catch (FormatException fe)
                    {
                        throw CreateBadDecodingError(fieldName, fe, value: xml);
                    }
                }
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Reads a GUID from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Uuid ReadGuid(string fieldName)
        {
            Uuid value = Uuid.Empty;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string guidString = ReadString("String");
                PopNamespace();

                try
                {
                    value = Uuid.Parse(guidString);
                }
                catch (FormatException fe)
                {
                    throw CreateBadDecodingError(fieldName, fe, value: guidString);
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ByteString ReadByteString(string fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                ByteString value;
                try
                {
                    string xml = m_reader.ReadContentAsString();

                    if (!string.IsNullOrEmpty(xml))
                    {
                        value = ByteString.From(SafeConvertFromBase64String(xml));
                    }
                    else
                    {
                        value = ByteString.Empty;
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
                if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                EndField(fieldName);
                return value;
            }

            return default;
        }

        /// <summary>
        /// Reads an XmlElement from the stream.
        /// </summary>
        public XmlElement ReadXmlElement(string fieldName)
        {
            if (BeginField(fieldName, true) && MoveToElement(null))
            {
                var document = new XmlDocument();
                System.Xml.XmlElement value = document.CreateElement(
                    m_reader.Prefix,
                    m_reader.LocalName,
                    m_reader.NamespaceURI);
                document.AppendChild(value);

                if (m_reader.MoveToFirstAttribute())
                {
                    do
                    {
                        XmlAttribute attribute = document.CreateAttribute(m_reader.Name);
                        attribute.Value = m_reader.Value;
                        value.Attributes.Append(attribute);
                    } while (m_reader.MoveToNextAttribute());

                    m_reader.MoveToContent();
                }

                value.InnerXml = m_reader.ReadInnerXml();

                EndField(fieldName);
                return (XmlElement)value;
            }

            return default;
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
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes
                    .BadNodeIdInvalid)
                {
                    throw CreateBadDecodingError(fieldName, sre, value: identifierText);
                }
                catch (ArgumentException ae)
                {
                    throw CreateBadDecodingError(fieldName, ae, value: identifierText);
                }

                EndField(fieldName);

                if (m_namespaceMappings != null &&
                    m_namespaceMappings.Length > value.NamespaceIndex)
                {
                    return value.WithNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
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
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes
                    .BadNodeIdInvalid)
                {
                    throw CreateBadDecodingError(fieldName, sre, value: identifierText);
                }
                catch (ArgumentException ae)
                {
                    throw CreateBadDecodingError(fieldName, ae, value: identifierText);
                }

                EndField(fieldName);

                if (m_namespaceMappings != null &&
                    m_namespaceMappings.Length > value.NamespaceIndex &&
                    !value.IsNull)
                {
                    value = value.WithNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
                }

                if (m_serverMappings != null &&
                    m_serverMappings.Length > value.ServerIndex &&
                    !value.IsNull)
                {
                    value = value.WithServerIndex(m_serverMappings[value.ServerIndex]);
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
            StatusCode value;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                value = ReadUInt32("Code");
                PopNamespace();

                EndField(fieldName);
            }
            else
            {
                value = StatusCodes.Good;
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

            return default;
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

                var value = new LocalizedText(locale, text);

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
                Variant value = Variant.Null;

                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    if (BeginField("Value", true))
                    {
                        try
                        {
                            value = ReadVariantValue();
                        }
                        catch (Exception ex) when (ex is not ServiceResultException)
                        {
                            m_logger.LogError(ex, "XmlDecoder: Error reading variant.");
                            value = new Variant(StatusCodes.BadDecodingError);
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
            var value = new DataValue();

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
            if (!BeginField(fieldName, true))
            {
                return ExtensionObject.Null;
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // read local type id.
            NodeId typeId = ReadNodeId("TypeId");

            // convert to absolute type id.
            var absoluteId = NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris);

            if (!typeId.IsNull && absoluteId.IsNull)
            {
                m_logger.LogWarning(
                    "Cannot de-serialize extension objects if the NamespaceUri is not in the NamespaceTable: Type = {Type}",
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
            ExtensionObject result = ReadExtensionObjectBody(absoluteId);

            // read end of body.
            EndField("Body");
            PopNamespace();

            // read end of extension object.
            EndField(fieldName);

            return result;
        }

        /// <summary>
        /// Reads an encodeable object from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable object field name</param>
        /// <param name="systemType">The system type of the encodeable object to be read</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instance that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> object that was read from the stream.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="systemType"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public IEncodeable ReadEncodeable(
            string fieldName,
            Type systemType,
            ExpandedNodeId encodeableTypeId = default)
        {
            if (systemType == null)
            {
                throw new ArgumentNullException(nameof(systemType));
            }

            if (Activator.CreateInstance(systemType) is not IEncodeable value)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Type does not support IEncodeable interface: '{0}'",
                    systemType.FullName);
            }

            if (!encodeableTypeId.IsNull)
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
                    XmlQualifiedName xmlName = TypeInfo.GetXmlName(value, Context);

                    PushNamespace(xmlName.Namespace);
                    value.Decode(this);
                    PopNamespace();

                    // skip to end of encodeable object.
                    m_reader.MoveToContent();

                    while (
                        !(
                            m_reader.NodeType == XmlNodeType.EndElement &&
                            m_reader.LocalName == fieldName &&
                            m_reader.NamespaceURI == m_namespaces.Peek()))
                    {
                        if (m_reader.NodeType == XmlNodeType.None)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Unexpected end of stream decoding field '{0}' for type '{1}'.",
                                fieldName,
                                systemType.FullName);
                        }

                        m_reader.Skip();
                        m_reader.MoveToContent();
                    }

                    EndField(fieldName);
                }
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Error decoding field '{0}' for type '{1}': {2}",
                    fieldName,
                    systemType.Name,
                    xe.Message);
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
        /// <exception cref="ServiceResultException"></exception>
        public Enum ReadEnumerated(string fieldName, Type enumType)
        {
            var value = (Enum)Enum.GetValues(enumType).GetValue(0);

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
                            int numericValue = Convert.ToInt32(
                                xml[(index + 1)..],
                                CultureInfo.InvariantCulture);
                            value = (Enum)Enum.ToObject(enumType, numericValue);
                        }
                        else
                        {
                            value = (Enum)Enum.Parse(enumType, xml, false);
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
                    {
                        throw CreateBadDecodingError(fieldName, ex, value: xml);
                    }
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Reads a boolean array from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public BooleanCollection ReadBooleanArray(string fieldName)
        {
            var values = new BooleanCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Boolean"))
                {
                    values.Add(ReadBoolean("Boolean"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public SByteCollection ReadSByteArray(string fieldName)
        {
            var values = new SByteCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("SByte"))
                {
                    values.Add(ReadSByte("SByte"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public ByteCollection ReadByteArray(string fieldName)
        {
            var values = new ByteCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Byte"))
                {
                    values.Add(ReadByte("Byte"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public Int16Collection ReadInt16Array(string fieldName)
        {
            var values = new Int16Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int16"))
                {
                    values.Add(ReadInt16("Int16"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public UInt16Collection ReadUInt16Array(string fieldName)
        {
            var values = new UInt16Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt16"))
                {
                    values.Add(ReadUInt16("UInt16"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public Int32Collection ReadInt32Array(string fieldName)
        {
            var values = new Int32Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int32"))
                {
                    values.Add(ReadInt32("Int32"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public UInt32Collection ReadUInt32Array(string fieldName)
        {
            var values = new UInt32Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt32"))
                {
                    values.Add(ReadUInt32("UInt32"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public Int64Collection ReadInt64Array(string fieldName)
        {
            var values = new Int64Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int64"))
                {
                    values.Add(ReadInt64("Int64"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public UInt64Collection ReadUInt64Array(string fieldName)
        {
            var values = new UInt64Collection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt64"))
                {
                    values.Add(ReadUInt64("UInt64"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public FloatCollection ReadFloatArray(string fieldName)
        {
            var values = new FloatCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Float"))
                {
                    values.Add(ReadFloat("Float"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public DoubleCollection ReadDoubleArray(string fieldName)
        {
            var values = new DoubleCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Double"))
                {
                    values.Add(ReadDouble("Double"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public StringCollection ReadStringArray(string fieldName)
        {
            var values = new StringCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("String"))
                {
                    values.Add(ReadString("String"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public DateTimeCollection ReadDateTimeArray(string fieldName)
        {
            var values = new DateTimeCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DateTime"))
                {
                    values.Add(ReadDateTime("DateTime"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public UuidCollection ReadGuidArray(string fieldName)
        {
            var values = new UuidCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Guid"))
                {
                    values.Add(ReadGuid("Guid"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public ByteStringCollection ReadByteStringArray(string fieldName)
        {
            var values = new ByteStringCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ByteString"))
                {
                    values.Add(ReadByteString("ByteString"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public XmlElementCollection ReadXmlElementArray(string fieldName)
        {
            var values = new XmlElementCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("XmlElement"))
                {
                    values.Add(ReadXmlElement("XmlElement"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public NodeIdCollection ReadNodeIdArray(string fieldName)
        {
            var values = new NodeIdCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("NodeId"))
                {
                    values.Add(ReadNodeId("NodeId"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public ExpandedNodeIdCollection ReadExpandedNodeIdArray(string fieldName)
        {
            var values = new ExpandedNodeIdCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExpandedNodeId"))
                {
                    values.Add(ReadExpandedNodeId("ExpandedNodeId"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public StatusCodeCollection ReadStatusCodeArray(string fieldName)
        {
            var values = new StatusCodeCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("StatusCode"))
                {
                    values.Add(ReadStatusCode("StatusCode"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public DiagnosticInfoCollection ReadDiagnosticInfoArray(string fieldName)
        {
            var values = new DiagnosticInfoCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DiagnosticInfo"))
                {
                    values.Add(ReadDiagnosticInfo("DiagnosticInfo"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public QualifiedNameCollection ReadQualifiedNameArray(string fieldName)
        {
            var values = new QualifiedNameCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("QualifiedName"))
                {
                    values.Add(ReadQualifiedName("QualifiedName"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public LocalizedTextCollection ReadLocalizedTextArray(string fieldName)
        {
            var values = new LocalizedTextCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("LocalizedText"))
                {
                    values.Add(ReadLocalizedText("LocalizedText"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public VariantCollection ReadVariantArray(string fieldName)
        {
            var values = new VariantCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Variant"))
                {
                    values.Add(ReadVariant("Variant"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public DataValueCollection ReadDataValueArray(string fieldName)
        {
            var values = new DataValueCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DataValue"))
                {
                    values.Add(ReadDataValue("DataValue"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ServiceResultException"></exception>
        public ExtensionObjectCollection ReadExtensionObjectArray(string fieldName)
        {
            var values = new ExtensionObjectCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExtensionObject"))
                {
                    values.Add(ReadExtensionObject("ExtensionObject"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
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
        /// <exception cref="ArgumentNullException"><paramref name="systemType"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public Array ReadEncodeableArray(
            string fieldName,
            Type systemType,
            ExpandedNodeId encodeableTypeId = default)
        {
            if (systemType == null)
            {
                throw new ArgumentNullException(nameof(systemType));
            }

            var encodeables = new IEncodeableCollection();

            if (BeginField(fieldName, true, out bool isNil))
            {
                XmlQualifiedName xmlName = TypeInfo.GetXmlName(systemType);
                PushNamespace(xmlName.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    encodeables.Add(ReadEncodeable(xmlName.Name, systemType, encodeableTypeId));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < encodeables.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);

                // convert to an array of the specified type.
                var values = Array.CreateInstance(systemType, encodeables.Count);

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
        /// <exception cref="ArgumentNullException"><paramref name="enumType"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public Array ReadEnumeratedArray(string fieldName, Type enumType)
        {
            if (enumType == null)
            {
                throw new ArgumentNullException(nameof(enumType));
            }

            var enums = new List<Enum>();

            if (BeginField(fieldName, true, out bool isNil))
            {
                XmlQualifiedName xmlName = TypeInfo.GetXmlName(enumType);
                PushNamespace(xmlName.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    enums.Add(ReadEnumerated(xmlName.Name, enumType));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < enums.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();
                EndField(fieldName);

                var values = Array.CreateInstance(enumType, enums.Count);

                for (int ii = 0; ii < enums.Count; ii++)
                {
                    values.SetValue(enums[ii], ii);
                }

                return values;
            }

            return isNil ? null : Array.CreateInstance(enumType, 0);
        }

        /// <inheritdoc/>
        public Array ReadArray(
            string fieldName,
            int valueRank,
            BuiltInType builtInType,
            Type systemType,
            ExpandedNodeId encodeableTypeId = default)
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

                    elements = ReadArrayElements(
                        "Elements",
                        builtInType,
                        systemType,
                        encodeableTypeId);

                    PopNamespace();

                    EndField(fieldName);
                }

                if (elements == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "The Matrix contains invalid elements");
                }

                Matrix matrix;
                if (dimensions != null && dimensions.Count > 0)
                {
                    matrix = new Matrix(elements, builtInType, [.. dimensions]);
                }
                else
                {
                    matrix = new Matrix(elements, builtInType);
                }

                return matrix.ToArray();
            }

            throw ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Invalid ValueRank {0} for Array",
                valueRank);
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string fieldName)
        {
            fieldName = null;
            return ReadUInt32("SwitchField");
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            return ReadUInt32("EncodingMask");
        }

        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfo nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
                var value = new DiagnosticInfo();
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

                hasDiagnosticInfo =
                    hasDiagnosticInfo ||
                    value.AdditionalInfo != null ||
                    value.InnerStatusCode != StatusCodes.Good;

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
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadMatrix(string fieldName)
        {
            CheckAndIncrementNestingLevel();

            Variant value = default;
            try
            {
                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    var dimensions = ReadInt32Array("Dimensions").ToArray();
                    if (BeginField("Elements", true))
                    {
                        value = ReadMatrix(dimensions);
                        EndField("Elements");
                    }

                    PopNamespace();

                    EndField(fieldName);
                }
            }
            finally
            {
                m_nestingLevel--;
            }
            return value;

            Variant ReadMatrix(int[] dimensions)
            {
                var typeName = m_reader.LocalName;
                switch (typeName)
                {
                    case "Boolean":
                        return Variant.From(ReadBooleanArray(null).ToMatrixOf(dimensions));
                    case "SByte":
                        return Variant.From(ReadSByteArray(null).ToMatrixOf(dimensions));
                    case "Byte":
                        return Variant.From(ReadByteArray(null).ToMatrixOf(dimensions));
                    case "Int16":
                        return Variant.From(ReadInt16Array(null).ToMatrixOf(dimensions));
                    case "UInt16":
                        return Variant.From(ReadUInt16Array(null).ToMatrixOf(dimensions));
                    case "Int32":
                        return Variant.From(ReadInt32Array(null).ToMatrixOf(dimensions));
                    case "UInt32":
                        return Variant.From(ReadUInt32Array(null).ToMatrixOf(dimensions));
                    case "Int64":
                        return Variant.From(ReadInt64Array(null).ToMatrixOf(dimensions));
                    case "UInt64":
                        return Variant.From(ReadUInt64Array(null).ToMatrixOf(dimensions));
                    case "Float":
                        return Variant.From(ReadFloatArray(null).ToMatrixOf(dimensions));
                    case "Double":
                        return Variant.From(ReadDoubleArray(null).ToMatrixOf(dimensions));
                    case "String":
                        return Variant.From(ReadStringArray(null).ToMatrixOf(dimensions));
                    case "DateTime":
                        return Variant.From(ReadDateTimeArray(null).ToMatrixOf(dimensions));
                    case "Guid":
                        return Variant.From(ReadGuidArray(null).ToMatrixOf(dimensions));
                    case "ByteString":
                        return Variant.From(ReadByteStringArray(null).ToMatrixOf(dimensions));
                    case "XmlElement":
                        return Variant.From(ReadXmlElementArray(null).ToMatrixOf(dimensions));
                    case "NodeId":
                        return Variant.From(ReadNodeIdArray(null).ToMatrixOf(dimensions));
                    case "ExpandedNodeId":
                        return Variant.From(ReadExpandedNodeIdArray(null).ToMatrixOf(dimensions));
                    case "StatusCode":
                        return Variant.From(ReadStatusCodeArray(null).ToMatrixOf(dimensions));
                    case "QualifiedName":
                        return Variant.From(ReadQualifiedNameArray(null).ToMatrixOf(dimensions));
                    case "LocalizedText":
                        return Variant.From(ReadLocalizedTextArray(null).ToMatrixOf(dimensions));
                    case "ExtensionObject":
                        return Variant.From(ReadExtensionObjectArray(null).ToMatrixOf(dimensions));
                    case "DataValue":
                        return Variant.From(ReadDataValueArray(null).ToMatrixOf(dimensions));
                    case "Variant":
                        return Variant.From(ReadVariantArray(null).ToMatrixOf(dimensions));
                    default:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Element '{1}:{0}' is not allowed in a Variant.",
                            m_reader.LocalName,
                            m_reader.NamespaceURI);
                }
            }
        }

        /// <summary>
        /// Read array items from current ListOf element
        /// </summary>
        /// <param name="fieldName">provides the fieldName for the array</param>
        /// <param name="builtInType">provides the BuiltInType of the elements that are read</param>
        /// <param name="systemType">The system type of the elements to read.</param>
        /// <param name="encodeableTypeId">provides the type id of the encodeable element</param>
        /// <exception cref="ServiceResultException"></exception>
        private Array ReadArrayElements(
            string fieldName,
            BuiltInType builtInType,
            Type systemType,
            ExpandedNodeId encodeableTypeId)
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
                        return collection?.ToArray();
                    }
                    case BuiltInType.SByte:
                    {
                        SByteCollection collection = ReadSByteArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.Byte:
                    {
                        ByteCollection collection = ReadByteArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.Int16:
                    {
                        Int16Collection collection = ReadInt16Array(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.UInt16:
                    {
                        UInt16Collection collection = ReadUInt16Array(fieldName);
                        return collection?.ToArray();
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
                                    var array = Array.CreateInstance(systemType, collection.Count);
                                    int ii = 0;
                                    foreach (int item in collection)
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
                        return collection?.ToArray();
                    }
                    case BuiltInType.Int64:
                    {
                        Int64Collection collection = ReadInt64Array(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.UInt64:
                    {
                        UInt64Collection collection = ReadUInt64Array(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.Float:
                    {
                        FloatCollection collection = ReadFloatArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.Double:
                    {
                        DoubleCollection collection = ReadDoubleArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.String:
                    {
                        StringCollection collection = ReadStringArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.DateTime:
                    {
                        DateTimeCollection collection = ReadDateTimeArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.Guid:
                    {
                        UuidCollection collection = ReadGuidArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.ByteString:
                    {
                        ByteStringCollection collection = ReadByteStringArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.XmlElement:
                    {
                        XmlElementCollection collection = ReadXmlElementArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.NodeId:
                    {
                        NodeIdCollection collection = ReadNodeIdArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.ExpandedNodeId:
                    {
                        ExpandedNodeIdCollection collection = ReadExpandedNodeIdArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.StatusCode:
                    {
                        StatusCodeCollection collection = ReadStatusCodeArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.DiagnosticInfo:
                    {
                        DiagnosticInfoCollection collection = ReadDiagnosticInfoArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.QualifiedName:
                    {
                        QualifiedNameCollection collection = ReadQualifiedNameArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.LocalizedText:
                    {
                        LocalizedTextCollection collection = ReadLocalizedTextArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.ExtensionObject:
                    {
                        ExtensionObjectCollection collection = ReadExtensionObjectArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.DataValue:
                    {
                        DataValueCollection collection = ReadDataValueArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.Variant:
                    {
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }

                        VariantCollection collection = ReadVariantArray(fieldName);
                        return collection?.ToArray();
                    }
                    case BuiltInType.Null:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Cannot decode unknown type in Array object with BuiltInType: {0}.",
                            builtInType);
                    default:
                        throw ServiceResultException.Unexpected($"Unexpected BuiltInType {builtInType}");
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
        /// <exception cref="ServiceResultException"></exception>
        private string SafeReadString([CallerMemberName] string functionName = null)
        {
            string message;
            try
            {
                string value = m_reader.ReadContentAsString();

                // check the length.
                if (value != null &&
                    Context.MaxStringLength > 0 &&
                    Context.MaxStringLength < value.Length)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "ReadString in {0} exceeds MaxStringLength: {1} > {2}",
                        functionName,
                        value.Length,
                        Context.MaxStringLength);
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
            throw ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Unable to read string of {0}: {1}",
                functionName,
                message);
        }

        private static byte[] SafeConvertFromBase64String(string s)
        {
            try
            {
                return Convert.FromBase64String(s);
            }
            catch (FormatException fe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Error decoding base64 string: {0}",
                    fe.Message);
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
        /// <exception cref="ServiceResultException"></exception>
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
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Encountered element: '{1}:{0}' when expecting element: '{2}:{3}'.",
                            m_reader.LocalName,
                            m_reader.NamespaceURI,
                            fieldName,
                            m_namespaces.Peek());
                    }

                    isNil = true;

                    // nothing more to read.
                    return false;
                }

                // check for empty or nil element.
                if (m_reader.HasAttributes)
                {
                    string nilValue = m_reader.GetAttribute("nil", Namespaces.XmlSchemaInstance);

                    if (!string.IsNullOrEmpty(nilValue) &&
                        SafeXmlConvert(fieldName, XmlConvert.ToBoolean, nilValue))
                    {
                        isNil = true;
                    }
                }

                bool isEmpty = m_reader.IsEmptyElement;

                m_reader.ReadStartElement();

                if (!isEmpty)
                {
                    m_reader.MoveToContent();

                    // check for an element with no children but not empty (due to whitespace).
                    if (m_reader.NodeType == XmlNodeType.EndElement &&
                        m_reader.LocalName == fieldName &&
                        m_reader.NamespaceURI == m_namespaces.Peek())
                    {
                        m_reader.ReadEndElement();
                        return false;
                    }
                }

                // caller must read contents of element.
                return !isNil && !isEmpty;
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unable to read field {0}: {1}",
                    fieldName,
                    xe.Message);
            }
        }

        /// <summary>
        /// Reads the end of a field.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void EndField(string fieldName)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                try
                {
                    m_reader.MoveToContent();

                    if (m_reader.NodeType != XmlNodeType.EndElement ||
                        m_reader.LocalName != fieldName ||
                        m_reader.NamespaceURI != m_namespaces.Peek())
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Encountered end element: '{1}:{0}' when expecting element: '{3}:{2}'.",
                            m_reader.LocalName,
                            m_reader.NamespaceURI,
                            fieldName,
                            m_namespaces.Peek());
                    }

                    m_reader.ReadEndElement();
                }
                catch (XmlException xe)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unable to read end field: {0}: {1}",
                        fieldName,
                        xe.Message);
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
                if (m_reader.NodeType is XmlNodeType.None or XmlNodeType.EndElement)
                {
                    return false;
                }

                m_reader.Read();
            }

            if (string.IsNullOrEmpty(elementName))
            {
                return true;
            }

            return m_reader.LocalName == elementName &&
                m_reader.NamespaceURI == m_namespaces.Peek();
        }

        /// <summary>
        /// Get the system type from the type factory if not specified by caller.
        /// </summary>
        /// <param name="systemType">The reference to the system type, or null</param>
        /// <param name="encodeableTypeId">The encodeable type id of the system type.</param>
        /// <returns>If the system type is assignable to <see cref="IEncodeable"/> </returns>
        private bool DetermineIEncodeableSystemType(
            ref Type systemType,
            ExpandedNodeId encodeableTypeId)
        {
            if (!encodeableTypeId.IsNull && systemType == null)
            {
                systemType = Context.Factory.GetSystemType(encodeableTypeId);
            }
            return typeof(IEncodeable).IsAssignableFrom(systemType);
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckAndIncrementNestingLevel([CallerMemberName] string functionName = null)
        {
            if (m_nestingLevel > Context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} in function {1} was exceeded",
                    Context.MaxEncodingNestingLevels,
                    functionName);
            }
            m_nestingLevel++;
        }

        /// <summary>
        /// Helper to create a BadDecodingError exception.
        /// </summary>
        private static ServiceResultException CreateBadDecodingError(
            string fieldName,
            Exception ex,
            [CallerMemberName] string functionName = null,
            string value = null)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unable to read field {0} in function {1}: {2}. Value: '{3}'",
                    fieldName,
                    functionName,
                    ex.Message,
                    value);
            }
            return ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Unable to read field {0} in function {1}: {2}",
                fieldName,
                functionName,
                ex.Message);
        }

        /// <summary>
        /// Wrapper for XmlConvert calls which catches the
        /// <see cref="FormatException"/> or <see cref="OverflowException"/>"
        /// and throws instead a <see cref="ServiceResultException"/> with
        /// StatusCode <see cref="StatusCodes.BadDecodingError"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private static T SafeXmlConvert<T>(
            string fieldName,
            Func<string, T> converter,
            string xml,
            [CallerMemberName] string functionName = null)
        {
            try
            {
                return converter(xml);
            }
            catch (OverflowException ove)
            {
                throw CreateBadDecodingError(fieldName, ove, functionName: functionName, value: xml);
            }
            catch (FormatException fe)
            {
                throw CreateBadDecodingError(fieldName, fe, functionName: functionName, value: xml);
            }
        }

        private readonly ILogger m_logger;
        private XmlReader m_reader;
        private readonly Stack<string> m_namespaces = [];
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
    }
}
