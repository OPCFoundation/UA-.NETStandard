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

#nullable enable

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
    /// Reads objects from an XML DOM (XmlDocument) instead of a streaming XmlReader.
    /// </summary>
    public sealed class XmlParser : IDecoder
    {
        /// <summary>
        /// Initializes the object with an XML string to parse.
        /// </summary>
        public XmlParser(string xml, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlParser>();
            m_nestingLevel = 0;

            m_document = new XmlDocument();
            using (var reader = XmlReader.Create(
                new StringReader(xml),
                CoreUtils.DefaultXmlReaderSettings()))
            {
                m_document.Load(reader);
            }

            var rootContext = new ElementContext(null);
            if (m_document.DocumentElement != null)
            {
                rootContext.ChildElements.Add(m_document.DocumentElement);
            }
            m_contextStack.Push(rootContext);
        }

        /// <summary>
        /// Initializes the object with a stream to parse.
        /// </summary>
        public XmlParser(Stream stream, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlParser>();
            m_nestingLevel = 0;

            m_document = new XmlDocument();
            using (var reader = XmlReader.Create(
                stream,
                CoreUtils.DefaultXmlReaderSettings()))
            {
                m_document.Load(reader);
            }

            var rootContext = new ElementContext(null);
            if (m_document.DocumentElement != null)
            {
                rootContext.ChildElements.Add(m_document.DocumentElement);
            }
            m_contextStack.Push(rootContext);
        }

        /// <summary>
        /// Initializes the object with an OPC UA XmlElement to parse.
        /// </summary>
        public XmlParser(XmlElement element, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlParser>();
            m_nestingLevel = 0;

            m_document = new XmlDocument();
            using (var reader = XmlReader.Create(
                new StringReader(element.OuterXml ?? string.Empty),
                CoreUtils.DefaultXmlReaderSettings()))
            {
                m_document.Load(reader);
            }

            var rootContext = new ElementContext(null);
            if (m_document.DocumentElement != null)
            {
                rootContext.ChildElements.Add(m_document.DocumentElement);
            }
            m_contextStack.Push(rootContext);
        }

        /// <summary>
        /// Initializes the object with a System.Xml.XmlElement to parse.
        /// </summary>
        public XmlParser(System.Xml.XmlElement element, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlParser>();
            m_nestingLevel = 0;

            m_document = new XmlDocument();
            using (var reader = XmlReader.Create(
                new StringReader(element.OuterXml ?? string.Empty),
                CoreUtils.DefaultXmlReaderSettings()))
            {
                m_document.Load(reader);
            }

            var rootContext = new ElementContext(null);
            if (m_document.DocumentElement != null)
            {
                rootContext.ChildElements.Add(m_document.DocumentElement);
            }
            m_contextStack.Push(rootContext);
        }

        /// <summary>
        /// Initializes the object with a system type and XML string.
        /// The root element is entered automatically so children are immediately accessible.
        /// </summary>
        public XmlParser(Type systemType, Stream xml, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlParser>();
            m_nestingLevel = 0;
            m_document = new XmlDocument();
            using (var reader = XmlReader.Create(xml, CoreUtils.DefaultXmlReaderSettings()))
            {
                m_document.Load(reader);
            }
            PushWithSystemType(systemType);
        }

        /// <summary>
        /// Initializes the object with a system type and XML string.
        /// The root element is entered automatically so children are immediately accessible.
        /// </summary>
        public XmlParser(Type systemType, string xml, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlParser>();
            m_nestingLevel = 0;
            m_document = new XmlDocument();
            using (var reader = XmlReader.Create(
                new StringReader(xml),
                CoreUtils.DefaultXmlReaderSettings()))
            {
                m_document.Load(reader);
            }
            PushWithSystemType(systemType);
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
                    string? namespaceUri = ReadString(elementName);
                    if (!string.IsNullOrEmpty(namespaceUri))
                    {
                        stringTable.Append(namespaceUri!);
                    }
                }

                Skip(new XmlQualifiedName(tableName, Namespaces.OpcUaXsd));
                return true;
            }
            finally
            {
                PopNamespace();
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
        }

        /// <summary>
        /// Returns the qualified name for the next element in the stream.
        /// </summary>
        public XmlQualifiedName? Peek(XmlNodeType nodeType)
        {
            ElementContext context = m_contextStack.Peek();

            for (int i = context.Cursor; i < context.ChildElements.Count; i++)
            {
                if (!context.Consumed.Contains(i))
                {
                    if (nodeType is not XmlNodeType.None and not XmlNodeType.Element)
                    {
                        return null;
                    }

                    System.Xml.XmlElement child = context.ChildElements[i];
                    return new XmlQualifiedName(child.LocalName, child.NamespaceURI);
                }
            }

            // No unconsumed children - check current element.
            if (context.Element != null &&
                nodeType is XmlNodeType.None or XmlNodeType.Element)
            {
                return new XmlQualifiedName(
                    context.Element.LocalName,
                    context.Element.NamespaceURI);
            }

            return null;
        }

        /// <summary>
        /// Returns true if the specified field is the next element to be extracted.
        /// </summary>
        public bool Peek(string? fieldName)
        {
            ElementContext context = m_contextStack.Peek();
            string ns = m_namespaces.Peek();

            for (int i = context.Cursor; i < context.ChildElements.Count; i++)
            {
                if (!context.Consumed.Contains(i))
                {
                    return context.ChildElements[i].LocalName == fieldName &&
                        context.ChildElements[i].NamespaceURI == ns;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the start element: finds the next unconsumed child, marks it consumed,
        /// and pushes it as a new context.
        /// </summary>
        public void ReadStartElement()
        {
            ElementContext context = m_contextStack.Peek();

            for (int i = context.Cursor; i < context.ChildElements.Count; i++)
            {
                if (context.Consumed.Add(i))
                {
                    context.Cursor = i + 1;
                    m_contextStack.Push(new ElementContext(context.ChildElements[i]));
                    return;
                }
            }
        }

        /// <summary>
        /// Pops the current context from the stack (DOM equivalent of skipping to end of element).
        /// </summary>
        /// <param name="qname">The qualified name of the element to skip.</param>
        public void Skip(XmlQualifiedName qname)
        {
            m_contextStack.Pop();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_contextStack.Clear();
        }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Xml;

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri)
        {
            m_namespaces.Push(namespaceUri);
        }

        /// <inheritdoc/>
        public void PopNamespace()
        {
            m_namespaces.Pop();
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public T DecodeMessage<T>() where T : IEncodeable
        {
            XmlQualifiedName? typeName = Peek(XmlNodeType.Element);
            if (typeName == null ||
                !Context.Factory.TryGetType(typeName, out IType? type) ||
                type is not IEncodeableType activator)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode message '{0}'.",
                    typeof(T));
            }

            string name = typeName.Name;
            int index = name.IndexOf(':', StringComparison.Ordinal);
            if (index != -1)
            {
                name = name[(index + 1)..];
            }

            PushNamespace(typeName.Namespace);

            // read the message.
            T encodeable = ReadEncodeable(name, (T)activator.CreateInstance());

            PopNamespace();

            return encodeable;
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    bool value = SafeXmlConvert(fieldName, XmlConvert.ToBoolean, xml!.ToLowerInvariant());
                    EndField(fieldName);
                    return value;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    sbyte value = SafeXmlConvert(fieldName, XmlConvert.ToSByte, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public byte ReadByte(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    byte value = SafeXmlConvert(fieldName, XmlConvert.ToByte, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public short ReadInt16(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    short value = SafeXmlConvert(fieldName, XmlConvert.ToInt16, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    ushort value = SafeXmlConvert(fieldName, XmlConvert.ToUInt16, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public int ReadInt32(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int value = SafeXmlConvert(fieldName, XmlConvert.ToInt32, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    uint value = SafeXmlConvert(fieldName, XmlConvert.ToUInt32, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public long ReadInt64(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    long value = SafeXmlConvert(fieldName, XmlConvert.ToInt64, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    ulong value = SafeXmlConvert(fieldName, XmlConvert.ToUInt64, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public float ReadFloat(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    float value = SafeXmlConvert(fieldName, XmlConvert.ToSingle, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public double ReadDouble(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    double value = SafeXmlConvert(fieldName, XmlConvert.ToDouble, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public string? ReadString(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                string? value = SafeReadString();

                if (value != null)
                {
                    value = value.Trim();
                }

                EndField(fieldName);
                return value;
            }

            return !isNil ? string.Empty : null;
        }

        /// <inheritdoc/>
        public DateTimeUtc ReadDateTime(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                // check the length.
                if (Context.MaxStringLength > 0 && Context.MaxStringLength < xml!.Length)
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

            return DateTimeUtc.MinValue;
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string? fieldName)
        {
            Uuid value = Uuid.Empty;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? guidString = ReadString("String");
                PopNamespace();

                try
                {
                    value = Uuid.Parse(guidString ?? string.Empty);
                }
                catch (FormatException fe)
                {
                    throw CreateBadDecodingError(fieldName, fe, value: guidString);
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                string? xml = SafeReadString();

                ByteString value;

                if (!string.IsNullOrEmpty(xml))
                {
                    value = ByteString.From(SafeConvertFromBase64String(xml!));
                }
                else
                {
                    value = ByteString.Empty;
                }

                // check the length.
                if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                EndField(fieldName);
                return value;
            }

            return isNil ? default : ByteString.Empty;
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                if (MoveToElement(null!))
                {
                    ElementContext context = m_contextStack.Peek();
                    int childIdx = context.Cursor;

                    // Find the actual next unconsumed child.
                    for (int i = context.Cursor; i < context.ChildElements.Count; i++)
                    {
                        if (!context.Consumed.Contains(i))
                        {
                            childIdx = i;
                            break;
                        }
                    }

                    System.Xml.XmlElement found = context.ChildElements[childIdx];
                    context.Consumed.Add(childIdx);
                    context.Cursor = childIdx + 1;

                    var document = new XmlDocument();
                    var imported = (System.Xml.XmlElement)document.ImportNode(found, true);
                    document.AppendChild(imported);

                    EndField(fieldName);
                    return (XmlElement)imported;
                }

                EndField(fieldName);
            }

            return default;
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? identifierText = ReadString("Identifier");
                PopNamespace();

                NodeId value;
                try
                {
                    value = NodeId.Parse(identifierText ?? string.Empty);
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

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? identifierText = ReadString("Identifier");
                PopNamespace();

                ExpandedNodeId value;
                try
                {
                    value = ExpandedNodeId.Parse(identifierText ?? string.Empty);
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

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string? fieldName)
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

        /// <inheritdoc/>
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName)
        {
            DiagnosticInfo? value = null;

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

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string? fieldName)
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

                string? name = null;

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

                return new QualifiedName(name ?? string.Empty, namespaceIndex);
            }

            return default;
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? text = null;
                string? locale = null;

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

                var value = new LocalizedText(locale ?? string.Empty, text ?? string.Empty);

                PopNamespace();

                EndField(fieldName);
                return value;
            }

            return LocalizedText.Null;
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string? fieldName)
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
                            m_logger.LogError(ex, "XmlParser: Error reading variant.");
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

        /// <inheritdoc/>
        public DataValue? ReadDataValue(string? fieldName)
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

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string? fieldName)
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

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string fieldName) where T : IEncodeable, new()
        {
            return ReadEncodeable(fieldName, new T());
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (!Context.Factory.TryGetEncodeableType(
                encodeableTypeId,
                out IEncodeableType? activator))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode type '{0}'.",
                    encodeableTypeId);
            }

            var value = (T)activator.CreateInstance();
            return ReadEncodeable(fieldName, value);
        }

        /// <inheritdoc/>
        public T ReadEncodeableAsExtensionObject<T>(string fieldName)
            where T : IEncodeable
        {
            ExtensionObject extensionObject = ReadExtensionObject(fieldName);
#pragma warning disable CS8600 // out T may be null when false is returned
            if (extensionObject.TryGetEncodeable(out T value))
            {
                return value!;
            }
#pragma warning restore CS8600
            return default!;
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string fieldName) where T : struct, Enum
        {
            T value = default;

            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int index = xml!.LastIndexOf('_');

                    try
                    {
                        if (index != -1)
                        {
                            int numericValue = Convert.ToInt32(
                                xml![(index + 1)..],
                                CultureInfo.InvariantCulture);
                            value = EnumHelper.Int32ToEnum<T>(numericValue);
                        }
                        else
                        {
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
                            value = Enum.Parse<T>(xml!, false);
#else
                            value = (T)Enum.Parse(typeof(T), xml!, false);
#endif
                        }
                    }
                    catch (Exception ex) when (ex is
                        ArgumentException or
                        FormatException or
                        OverflowException)
                    {
                        throw CreateBadDecodingError(fieldName, ex, value: xml);
                    }
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <inheritdoc/>
        public EnumValue ReadEnumerated(string? fieldName)
        {
            EnumValue value = default;

            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int index = xml!.LastIndexOf('_');

                    try
                    {
                        if (index != -1)
                        {
                            int numericValue = Convert.ToInt32(
                                xml![(index + 1)..],
                                CultureInfo.InvariantCulture);
                            value = new EnumValue(numericValue, xml[..index]);
                        }
                        else if (int.TryParse(xml, out int numeric))
                        {
                            value = (EnumValue)numeric;
                        }
                        else
                        {
                            value = new EnumValue(0, xml!);
                        }
                    }
                    catch (Exception ex) when (ex is
                        ArgumentException or
                        FormatException or
                        OverflowException)
                    {
                        throw CreateBadDecodingError(fieldName, ex, value: xml);
                    }
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<bool>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<sbyte>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<byte>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<short>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ushort>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<int>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<uint>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<long>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ulong>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<float>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<double>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<string?>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<DateTimeUtc>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<Uuid>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ByteString>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ByteString"))
                {
                    values.Add(ReadByteString("ByteString"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxByteStringLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<XmlElement>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<NodeId>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ExpandedNodeId>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<StatusCode>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<DiagnosticInfo?>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<QualifiedName>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<LocalizedText>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<Variant>();
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

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue?> ReadDataValueArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<DataValue?>();
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
                return values.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ExtensionObject>();
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
                return values.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArrayAsExtensionObjects<T>(string fieldName)
            where T : IEncodeable
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<T>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExtensionObject"))
                {
                    values.Add(ReadEncodeableAsExtensionObject<T>("ExtensionObject"));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return values.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string fieldName)
            where T : IEncodeable, new()
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var encodeables = new List<T>();
                XmlQualifiedName? xmlName = TypeInfo.GetXmlName(typeof(T));
                PushNamespace(xmlName!.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    encodeables.Add(ReadEncodeable<T>(xmlName.Name));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < encodeables.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();

                EndField(fieldName);
                return encodeables.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(
            string fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var encodeables = new List<T>();
                XmlQualifiedName? xmlName = TypeInfo.GetXmlName(typeof(T));
                PushNamespace(xmlName!.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    encodeables.Add(ReadEncodeable<T>(xmlName.Name, encodeableTypeId));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < encodeables.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();
                EndField(fieldName);
                return encodeables.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public MatrixOf<T> ReadEncodeableMatrix<T>(
            string fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();
            MatrixOf<T> value = default;
            try
            {
                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    int[] dimensions = ReadInt32Array("Dimensions").ToArray() ?? [];
                    if (BeginField("Elements", true))
                    {
                        value = ReadEncodeableArray<T>(null!, encodeableTypeId)
                            .ToMatrix(dimensions);
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
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string fieldName) where T : struct, Enum
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var enums = new List<T>();
                XmlQualifiedName? xmlName = TypeInfo.GetXmlName(typeof(T));
                PushNamespace(xmlName!.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    enums.Add(ReadEnumerated<T>(xmlName.Name));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < enums.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();
                EndField(fieldName);
                return enums.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var enums = new List<EnumValue>();

                if (!MoveToElement(null!))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unable to read field {0} in function {1}: The enumerated array does not contain any elements.",
                        fieldName ?? string.Empty,
                        nameof(ReadEnumeratedArray));
                }

                XmlQualifiedName? xmlName = Peek(XmlNodeType.Element);
                PushNamespace(xmlName!.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    enums.Add(ReadEnumerated(xmlName.Name));
                }

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < enums.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PopNamespace();
                EndField(fieldName);
                return enums.ToArrayOf();
            }

            if (LastFieldWasEmpty)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unable to read field {0} in function {1}: The enumerated array does not contain any elements.",
                    fieldName ?? string.Empty,
                    nameof(ReadEnumeratedArray));
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string? fieldName)
        {
            fieldName = null;
            return ReadUInt32("SwitchField");
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            return ReadUInt32("EncodingMask");
        }

        /// <inheritdoc/>
        public bool HasField(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return true;
            }

            ElementContext context = m_contextStack.Peek();
            string ns = m_namespaces.Peek();
            return FindChildIndex(context, fieldName, ns) >= 0;
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
                    if (!typeInfo.IsUnknown && !value.IsNull)
                    {
                        if (typeInfo.BuiltInType == BuiltInType.Enumeration)
                        {
                            typeInfo = typeInfo.WithBuiltInType(BuiltInType.Int32);
                        }

                        if (value.TypeInfo != typeInfo)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Error reading value as variant. Type mismatch: Expected {0} != Actual {1}",
                                typeInfo, value.TypeInfo);
                        }
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
            // Find the first unconsumed child element to determine the type.
            ElementContext context = m_contextStack.Peek();
            string? typeName = null;
            string? typeNs = null;

            for (int i = context.Cursor; i < context.ChildElements.Count; i++)
            {
                if (!context.Consumed.Contains(i))
                {
                    typeName = context.ChildElements[i].LocalName;
                    typeNs = context.ChildElements[i].NamespaceURI;
                    break;
                }
            }

            if (typeName == null)
            {
                return Variant.Null;
            }

            try
            {
                m_namespaces.Push(Namespaces.OpcUaXsd);

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
                            return ReadString(typeName) ?? string.Empty;
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
#pragma warning disable CS8604 // Possible null reference argument
                            return ReadDataValue(typeName);
#pragma warning restore CS8604
                        case "Matrix":
                            return ReadMatrix(typeName);
                        default:
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Element '{1}:{0}' is not allowed in a Variant.",
                                typeName,
                                typeNs ?? string.Empty);
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
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                            return Variant.From(ReadStringArray(typeName));
#pragma warning restore CS8620
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
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                            return Variant.From(ReadDataValueArray(typeName));
#pragma warning restore CS8620
                        case "Variant":
                            return Variant.From(ReadVariantArray(typeName));
                        default:
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Element '{1}:{0}' is not allowed in a Variant.",
                                typeName,
                                typeNs ?? string.Empty);
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
            // Find the next unconsumed child element to determine body type.
            ElementContext context = m_contextStack.Peek();
            System.Xml.XmlElement? bodyChild = null;
            int bodyChildIdx = -1;

            for (int i = context.Cursor; i < context.ChildElements.Count; i++)
            {
                if (!context.Consumed.Contains(i))
                {
                    bodyChild = context.ChildElements[i];
                    bodyChildIdx = i;
                    break;
                }
            }

            if (bodyChild == null)
            {
                return new ExtensionObject(typeId);
            }

            // check for binary encoded body.
            if (bodyChild.LocalName == "ByteString" &&
                bodyChild.NamespaceURI == Namespaces.OpcUaXsd)
            {
                PushNamespace(Namespaces.OpcUaXsd);
                ByteString bytes = ReadByteString("ByteString");
                PopNamespace();

                return new ExtensionObject(typeId, bytes);
            }

            // lookup type.
            if (Context.Factory.TryGetEncodeableType(typeId, out _))
            {
                // decode known type.
                PushNamespace(bodyChild.NamespaceURI);
                IEncodeable encodeable = ReadEncodeable<IEncodeable>(
                    bodyChild.LocalName, typeId);
                PopNamespace();
                return new ExtensionObject(encodeable);
            }

            // Unknown type: consume the child element and return as XML.
            try
            {
                context.Consumed.Add(bodyChildIdx);
                context.Cursor = bodyChildIdx + 1;

                var xmlElement = XmlElement.From(bodyChild.OuterXml);
                if (!xmlElement.IsValid)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Invalid xml in extension object body: {0}",
                        xmlElement);
                }
                return new ExtensionObject(typeId, xmlElement);
            }
            catch (Exception ae) when (ae is not ServiceResultException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Failed to decode xml extension object body: {0}",
                    ae.Message);
            }
        }

        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfo nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private DiagnosticInfo? ReadDiagnosticInfo(int depth)
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
        private Variant ReadMatrix(string? fieldName)
        {
            CheckAndIncrementNestingLevel();

            Variant value = default;
            try
            {
                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    int[] dimensions = ReadInt32Array("Dimensions").ToArray() ?? [];
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
                // Find first unconsumed child to determine element type.
                ElementContext ctx = m_contextStack.Peek();
                string? elementTypeName = null;
                string? elementTypeNs = null;

                for (int i = ctx.Cursor; i < ctx.ChildElements.Count; i++)
                {
                    if (!ctx.Consumed.Contains(i))
                    {
                        elementTypeName = ctx.ChildElements[i].LocalName;
                        elementTypeNs = ctx.ChildElements[i].NamespaceURI;
                        break;
                    }
                }

                if (elementTypeName == null)
                {
                    return default;
                }

                switch (elementTypeName)
                {
                    case "Boolean":
                        return Variant.From(ReadBooleanArray(null).ToMatrix(dimensions));
                    case "SByte":
                        return Variant.From(ReadSByteArray(null).ToMatrix(dimensions));
                    case "Byte":
                        return Variant.From(ReadByteArray(null).ToMatrix(dimensions));
                    case "Int16":
                        return Variant.From(ReadInt16Array(null).ToMatrix(dimensions));
                    case "UInt16":
                        return Variant.From(ReadUInt16Array(null).ToMatrix(dimensions));
                    case "Int32":
                        return Variant.From(ReadInt32Array(null).ToMatrix(dimensions));
                    case "UInt32":
                        return Variant.From(ReadUInt32Array(null).ToMatrix(dimensions));
                    case "Int64":
                        return Variant.From(ReadInt64Array(null).ToMatrix(dimensions));
                    case "UInt64":
                        return Variant.From(ReadUInt64Array(null).ToMatrix(dimensions));
                    case "Float":
                        return Variant.From(ReadFloatArray(null).ToMatrix(dimensions));
                    case "Double":
                        return Variant.From(ReadDoubleArray(null).ToMatrix(dimensions));
                    case "String":
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(ReadStringArray(null).ToMatrix(dimensions));
#pragma warning restore CS8620
                    case "DateTime":
                        return Variant.From(ReadDateTimeArray(null).ToMatrix(dimensions));
                    case "Guid":
                        return Variant.From(ReadGuidArray(null).ToMatrix(dimensions));
                    case "ByteString":
                        return Variant.From(ReadByteStringArray(null).ToMatrix(dimensions));
                    case "XmlElement":
                        return Variant.From(ReadXmlElementArray(null).ToMatrix(dimensions));
                    case "NodeId":
                        return Variant.From(ReadNodeIdArray(null).ToMatrix(dimensions));
                    case "ExpandedNodeId":
                        return Variant.From(
                            ReadExpandedNodeIdArray(null).ToMatrix(dimensions));
                    case "StatusCode":
                        return Variant.From(
                            ReadStatusCodeArray(null).ToMatrix(dimensions));
                    case "QualifiedName":
                        return Variant.From(
                            ReadQualifiedNameArray(null).ToMatrix(dimensions));
                    case "LocalizedText":
                        return Variant.From(
                            ReadLocalizedTextArray(null).ToMatrix(dimensions));
                    case "ExtensionObject":
                        return Variant.From(
                            ReadExtensionObjectArray(null).ToMatrix(dimensions));
                    case "DataValue":
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(
                            ReadDataValueArray(null).ToMatrix(dimensions));
#pragma warning restore CS8620
                    case "Variant":
                        return Variant.From(
                            ReadVariantArray(null).ToMatrix(dimensions));
                    default:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Element '{1}:{0}' is not allowed in a Variant.",
                            elementTypeName ?? string.Empty,
                            elementTypeNs ?? string.Empty);
                }
            }
        }

        private T ReadEncodeable<T>(string fieldName, T value) where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();
            try
            {
                if (BeginField(fieldName, true))
                {
                    XmlQualifiedName? xmlName = TypeInfo.GetXmlName(value, Context);

                    PushNamespace(xmlName!.Namespace);
                    value.Decode(this);
                    PopNamespace();

                    EndField(fieldName);
                    return value;
                }

                // If the element exists but is empty (e.g., <ServerConfiguration />),
                // return the pre-created instance with defaults rather than null.
                if (LastFieldWasEmpty)
                {
                    return value;
                }

                return default!;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Reads the InnerText of the current context element.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private string? SafeReadString([CallerMemberName] string? functionName = null)
        {
            ElementContext context = m_contextStack.Peek();
            string? value = context.Element?.InnerText;

            // check the length.
            if (value != null &&
                Context.MaxStringLength > 0 &&
                Context.MaxStringLength < value.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "ReadString in {0} exceeds MaxStringLength: {1} > {2}",
                    functionName ?? string.Empty,
                    value.Length,
                    Context.MaxStringLength);
            }

            return value;
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
        /// Reads the start of field where the presences of the xsi:nil attribute
        /// is not significant.
        /// </summary>
        private bool BeginField(string? fieldName, bool isOptional)
        {
            return BeginField(fieldName, isOptional, out _);
        }

        /// <summary>
        /// Reads the start of field using DOM navigation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool BeginField(string? fieldName, bool isOptional, out bool isNil)
        {
            isNil = false;
            m_lastFieldWasEmpty = false;

            // allow caller to skip reading element tag if field name is not specified.
            if (string.IsNullOrEmpty(fieldName))
            {
                return true;
            }

            ElementContext context = m_contextStack.Peek();
            string ns = m_namespaces.Peek();

            int idx = FindChildIndex(context, fieldName!, ns);

            if (idx < 0)
            {
                if (!isOptional)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Expected element '{1}:{0}' not found.",
                        fieldName ?? string.Empty,
                        ns);
                }

                isNil = true;
                return false;
            }

            System.Xml.XmlElement found = context.ChildElements[idx];
            context.Consumed.Add(idx);
            context.Cursor = idx + 1;

            // check for nil attribute.
            string nilValue = found.GetAttribute("nil", Namespaces.XmlSchemaInstance);

            if (!string.IsNullOrEmpty(nilValue) &&
                SafeXmlConvert(fieldName, XmlConvert.ToBoolean, nilValue))
            {
                isNil = true;
            }

            // check for empty element.
            bool hasElementChildren = false;
            foreach (XmlNode child in found.ChildNodes)
            {
                if (child is System.Xml.XmlElement)
                {
                    hasElementChildren = true;
                    break;
                }
            }

            bool isEmpty = !hasElementChildren && string.IsNullOrWhiteSpace(found.InnerText);

            if (isNil || isEmpty)
            {
                m_lastFieldWasEmpty = isEmpty && !isNil;
                return false;
            }

            m_contextStack.Push(new ElementContext(found));
            return true;
        }

        /// <summary>
        /// Reads the end of a field by popping the context stack.
        /// </summary>
        private void EndField(string? fieldName)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                m_contextStack.Pop();
            }
        }

        /// <summary>
        /// Moves to the next start element in the current context.
        /// </summary>
        private bool MoveToElement(string elementName)
        {
            ElementContext context = m_contextStack.Peek();

            // Find the first unconsumed child element.
            for (int i = context.Cursor; i < context.ChildElements.Count; i++)
            {
                if (!context.Consumed.Contains(i))
                {
                    if (string.IsNullOrEmpty(elementName))
                    {
                        context.Cursor = i;
                        return true;
                    }

                    System.Xml.XmlElement child = context.ChildElements[i];
                    if (child.LocalName == elementName &&
                        child.NamespaceURI == m_namespaces.Peek())
                    {
                        context.Cursor = i;
                        return true;
                    }

                    // First unconsumed doesn't match - consistent with streaming
                    // behavior for array iteration.
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Searches for a child element by name with wrap-around, skipping consumed indices.
        /// </summary>
        private static int FindChildIndex(
            ElementContext context,
            string fieldName,
            string ns)
        {
            // Search from cursor forward.
            for (int i = context.Cursor; i < context.ChildElements.Count; i++)
            {
                if (!context.Consumed.Contains(i) &&
                    context.ChildElements[i].LocalName == fieldName &&
                    context.ChildElements[i].NamespaceURI == ns)
                {
                    return i;
                }
            }

            // Wrap around: search from beginning to cursor.
            for (int i = 0; i < context.Cursor; i++)
            {
                if (!context.Consumed.Contains(i) &&
                    context.ChildElements[i].LocalName == fieldName &&
                    context.ChildElements[i].NamespaceURI == ns)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckAndIncrementNestingLevel(
            [CallerMemberName] string? functionName = null)
        {
            if (m_nestingLevel > Context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} in function {1} was exceeded",
                    Context.MaxEncodingNestingLevels,
                    functionName ?? string.Empty);
            }
            m_nestingLevel++;
        }

        /// <summary>
        /// Helper to create a BadDecodingError exception.
        /// </summary>
        private static ServiceResultException CreateBadDecodingError(
            string? fieldName,
            Exception ex,
            [CallerMemberName] string? functionName = null,
            string? value = null)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unable to read field {0} in function {1}: {2}. Value: '{3}'",
                    fieldName ?? string.Empty,
                    functionName ?? string.Empty,
                    ex.Message,
                    value!);
            }
            return ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Unable to read field {0} in function {1}: {2}",
                fieldName ?? string.Empty,
                functionName ?? string.Empty,
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
            string? fieldName,
            Func<string, T> converter,
            string xml,
            [CallerMemberName] string? functionName = null)
        {
            try
            {
                return converter(xml);
            }
            catch (OverflowException ove)
            {
                throw CreateBadDecodingError(
                    fieldName, ove, functionName: functionName, value: xml);
            }
            catch (FormatException fe)
            {
                throw CreateBadDecodingError(
                    fieldName, fe, functionName: functionName, value: xml);
            }
        }

        private void PushWithSystemType(Type systemType)
        {
            string? ns = null;
            string? name = null;

            if (systemType != null)
            {
                XmlQualifiedName? typeName = TypeInfo.GetXmlName(systemType);
                ns = typeName!.Namespace;
                name = typeName.Name;
            }

            System.Xml.XmlElement? docElement = m_document.DocumentElement;

            if (ns == null)
            {
                ns = docElement!.NamespaceURI;
                name = docElement.LocalName;
            }

            int index = name!.IndexOf(':', StringComparison.Ordinal);

            if (index != -1)
            {
                name = name![(index + 1)..];
            }

            PushNamespace(ns);

            // Push the root element directly as context so children are
            // immediately accessible (equivalent of BeginField in streaming).
            m_contextStack.Push(new ElementContext(docElement!));
        }

        /// <summary>
        /// Represents the context for navigating a DOM element and its children.
        /// </summary>
        private sealed class ElementContext
        {
            public System.Xml.XmlElement? Element { get; }
            public List<System.Xml.XmlElement> ChildElements { get; } = [];
            public HashSet<int> Consumed { get; } = [];
            public int Cursor { get; set; }

            public ElementContext(System.Xml.XmlElement? element)
            {
                Element = element;
                if (element != null)
                {
                    foreach (XmlNode child in element.ChildNodes)
                    {
                        if (child is System.Xml.XmlElement childElem)
                        {
                            ChildElements.Add(childElem);
                        }
                    }
                }
            }
        }

        private readonly ILogger m_logger;
        private readonly XmlDocument m_document;
        private readonly Stack<ElementContext> m_contextStack = [];
        private readonly Stack<string> m_namespaces = [];
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
        private uint m_nestingLevel;
        private bool m_lastFieldWasEmpty;

        /// <summary>
        /// Returns true if the last BeginField found the element but it was empty.
        /// </summary>
        private bool LastFieldWasEmpty => m_lastFieldWasEmpty;
    }
}
