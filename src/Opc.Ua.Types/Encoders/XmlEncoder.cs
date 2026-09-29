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
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Writes objects to a XML stream.
    /// </summary>
    public sealed class XmlEncoder : IEncoder
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public XmlEncoder(IServiceMessageContext context)
        {
            Context = context;
            m_destination = new StringBuilder();
            m_nestingLevel = 0;

            XmlWriterSettings settings = CoreUtils.DefaultXmlWriterSettings();
            settings.ConformanceLevel = ConformanceLevel.Auto;
            settings.NamespaceHandling = NamespaceHandling.OmitDuplicates;
            settings.NewLineHandling = NewLineHandling.Replace;

            m_writer = XmlWriter.Create(m_destination, settings);
        }

        /// <summary>
        /// Initializes the object with a system type to encode and a XML writer.
        /// </summary>
        public XmlEncoder(Type systemType, XmlWriter writer, IServiceMessageContext context)
            : this(TypeInfo.GetXmlName(systemType)!, writer, context)
        {
        }

        /// <summary>
        /// Initializes the object with a system type to encode and a XML writer.
        /// </summary>
        public XmlEncoder(XmlQualifiedName root, XmlWriter writer, IServiceMessageContext context)
        {
            Context = context;
            if (writer == null)
            {
                m_destination = new StringBuilder();
                m_writer = XmlWriter.Create(m_destination, CoreUtils.DefaultXmlWriterSettings());
            }
            else
            {
                m_destination = null;
                m_writer = writer;
            }

            Initialize(root.Name, root.Namespace);
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Writes the root element to the stream.
        /// </summary>
        private void Initialize(string? fieldName, string namespaceUri)
        {
            m_root = new XmlQualifiedName(fieldName, namespaceUri);

            string? uaxPrefix = m_writer.LookupPrefix(Namespaces.OpcUaXsd);

            uaxPrefix ??= "uax";

            if (namespaceUri == Namespaces.OpcUaXsd)
            {
                m_writer.WriteStartElement(uaxPrefix, fieldName ?? string.Empty, namespaceUri);
            }
            else
            {
                m_writer.WriteStartElement(fieldName ?? string.Empty, namespaceUri);
            }

            string? xsiPrefix = m_writer.LookupPrefix(Namespaces.XmlSchemaInstance);

            if (xsiPrefix == null)
            {
                m_writer.WriteAttributeString("xmlns", "xsi", null, Namespaces.XmlSchemaInstance);
            }

            uaxPrefix = m_writer.LookupPrefix(Namespaces.OpcUaXsd);

            if (uaxPrefix == null)
            {
                m_writer.WriteAttributeString("xmlns", "uax", null, Namespaces.OpcUaXsd);
            }

            m_prefixesInitialized = true;
            PushNamespace(namespaceUri);
        }

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during encoding.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs referenced by the data being encoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being encoded.</param>
        public void SetMappingTables(NamespaceTable? namespaceUris, StringTable? serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && Context.NamespaceUris != null)
            {
                m_namespaceMappings = namespaceUris.CreateMapping(Context.NamespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && Context.ServerUris != null)
            {
                m_serverMappings = serverUris.CreateMapping(Context.ServerUris, false);
            }
        }

        /// <summary>
        /// Saves a string table from an XML stream.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="elementName">Name of the element.</param>
        /// <param name="stringTable">The string table.</param>
        public void SaveStringTable(string tableName, string elementName, StringTable stringTable)
        {
            if (stringTable == null || stringTable.Count <= 1)
            {
                return;
            }

            PushNamespace(Namespaces.OpcUaXsd);

            try
            {
                Push(tableName, Namespaces.OpcUaXsd);

                for (ushort ii = 1; ii < stringTable.Count; ii++)
                {
                    WriteString(elementName, stringTable.GetString(ii));
                }

                Pop();
            }
            finally
            {
                PopNamespace();
            }
        }

        /// <summary>
        /// Writes a start element.
        /// </summary>
        /// <param name="fieldName">The name of the element.</param>
        /// <param name="namespaceUri">The namespace that qualifies the element name.</param>
        public void Push(string? fieldName, string namespaceUri)
        {
            m_writer.WriteStartElement(fieldName ?? string.Empty, namespaceUri);
            PushNamespace(namespaceUri);
        }

        /// <summary>
        /// Writes an end element.
        /// </summary>
        public void Pop()
        {
            m_writer.WriteEndElement();
            PopNamespace();
        }

        /// <inheritdoc/>
        public int Close()
        {
            if (m_root != null)
            {
                m_writer.WriteEndElement();
            }

            m_writer.Flush();
            m_writer.Dispose();

            if (m_destination != null)
            {
                return m_destination.Length;
            }

            return 0;
        }

        /// <inheritdoc/>
        public string? CloseAndReturnText()
        {
            Close();

            if (m_destination != null)
            {
                return m_destination.ToString();
            }

            return null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_writer.Flush();
                m_writer.Dispose();
                m_disposed = true;
            }
        }

        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Xml;

        /// <inheritdoc/>
        public bool CanOmitFields => true;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Xml Encoder always produces reversible encoding.
        /// </summary>
        public bool UseReversibleEncoding => true;

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

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message) where T : IEncodeable, new()
        {
            if (EqualityComparer<T>.Default.Equals(message, default!))
            {
                throw new ArgumentNullException(nameof(message));
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // write the message.
            WriteEncodeable(message.GetType().Name, message);

            PopNamespace();
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(message, default!))
            {
                throw new ArgumentNullException(nameof(message));
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // write the message.
            WriteEncodeable(message.GetType().Name, message, encodeableTypeId);

            PopNamespace();
        }

        /// <inheritdoc/>
        public void WriteBoolean(string? fieldName, bool value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteSByte(string? fieldName, sbyte value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteByte(string? fieldName, byte value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt16(string? fieldName, short value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt16(string? fieldName, ushort value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt32(string? fieldName, int value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt32(string? fieldName, uint value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt64(string? fieldName, long value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt64(string? fieldName, ulong value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(XmlConvert.ToString(value));
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteFloat(string? fieldName, float value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDouble(string? fieldName, double value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteString(string? fieldName, string? value)
        {
            WriteString(fieldName, value, false);
        }

        private void WriteString(string? fieldName, string? value, bool isArrayElement)
        {
            if (BeginField(fieldName, value == null, true, isArrayElement))
            {
                // check the length.
                EncodingLimits.CheckStringLength(Context.MaxStringLength, value);

                // Write whitespace only strings verbatim (xs:string preserves
                // whitespace, Part 6 5.3.1.5); XmlDecoder/XmlParser read them
                // back unchanged. Only NodeSet/design importers opt in to read
                // such an element as "" (XmlDecoder.TreatWhitespaceOnlyStringsAsEmpty)
                // because pretty-printed NodeSets use it for empty values.
                if (!string.IsNullOrEmpty(value))
                {
                    CheckXmlChars(value!);
                    m_writer.WriteString(value);
                }

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Throws BadEncodingError for a string XML 1.0 cannot represent.
        /// </summary>
        /// <remarks>
        /// xs:string (Part 6 5.3.1.5) only holds XML characters: most C0 control
        /// characters, U+FFFE/U+FFFF and unpaired surrogates have no XML form,
        /// not even as a character reference. Writing them produced a document
        /// that every XML parser, XmlDecoder included, rejects as a whole, or
        /// left the writer in an error state.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        private static void CheckXmlChars(string value)
        {
            try
            {
                XmlConvert.VerifyXmlChars(value);
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "String cannot be encoded in XML: {0}",
                    xe.Message);
            }

            // a surrogate is only a character as part of a pair.
            for (int ii = 0; ii < value.Length; ii++)
            {
                if (char.IsHighSurrogate(value[ii]) &&
                    ii + 1 < value.Length &&
                    char.IsLowSurrogate(value[ii + 1]))
                {
                    ii++;
                }
                else if (char.IsSurrogate(value[ii]))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingError,
                        "String cannot be encoded in XML: unpaired surrogate at index {0}.",
                        ii);
                }
            }
        }

        /// <inheritdoc/>
        public void WriteDateTime(string? fieldName, DateTimeUtc value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue((DateTime)value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteGuid(string? fieldName, Uuid value)
        {
            if (BeginField(fieldName, false, false))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                WriteString("String", value.ToString());
                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ByteString value)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            WriteByteString(fieldName, value.Span);
#else
            WriteByteString(fieldName, value, false);
#endif
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteString(string? fieldName, ReadOnlySpan<byte> value)
        {
            WriteByteString(fieldName, value, false);
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteByteString(string? fieldName, ReadOnlySpan<byte> value, bool isArrayElement)
        {
            // == compares memory reference, comparing to empty means we compare to the default
            // If null array is converted to span the span is default
            if (BeginField(fieldName, value == ReadOnlySpan<byte>.Empty, true, isArrayElement))
            {
                // check the length.
                if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                m_writer.WriteValue(
                    Convert.ToBase64String(value, Base64FormattingOptions.InsertLineBreaks));
                EndField(fieldName);
            }
        }
#endif

        private void WriteByteString(string? fieldName, ByteString value, bool isArrayElement)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            WriteByteString(fieldName, value.Span, isArrayElement);
#else
            if (BeginField(fieldName, value.IsNull, true, isArrayElement))
            {
                // check the length.
                if (Context.MaxByteStringLength > 0 &&
                    Context.MaxByteStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                m_writer.WriteValue(value.ToBase64(Base64FormattingOptions.InsertLineBreaks));
                EndField(fieldName);
            }
#endif
        }

        /// <inheritdoc/>
        public void WriteXmlElement(string? fieldName, XmlElement value)
        {
            WriteXmlElement(fieldName, value, false);
        }

        private void WriteXmlElement(string? fieldName, XmlElement value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsEmpty, true, isArrayElement))
            {
                WriteXmlBody(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes XML that is kept as raw XML: an XmlElement value or the body
        /// of an ExtensionObject of an unknown type.
        /// </summary>
        /// <remarks>
        /// The XML is parsed first: writing unparsable (or injected) markup
        /// would produce a document no decoder can read, and an XML
        /// declaration or DOCTYPE - which may only appear at the start of a
        /// document - would be injected into the middle of this one. The
        /// element is copied without its prolog, as a single root
        /// (Part 6 5.3.1.9, 5.3.1.16), keeping its whitespace, and bounded by
        /// MaxStringLength and the XML element depth limit.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteXmlBody(XmlElement value)
        {
            string body;
            try
            {
                using var reader = XmlReader.Create(
                    new StringReader(value.OuterXml ?? string.Empty),
                    CoreUtils.DefaultXmlReaderSettings());
                if (reader.MoveToContent() != XmlNodeType.Element)
                {
                    throw new XmlException("The XML has no root element.");
                }

                // an element in no namespace declares it, so it is not taken
                // into the default namespace in scope where it is written.
                body = EncodingLimits.ReadXmlElementContent(
                    reader,
                    EncodingLimits.GetMaxXmlElementDepth(Context),
                    Context.MaxStringLength,
                    declareNoNamespace: true);

                // the reader rejects a second root element or trailing text.
                while (reader.Read())
                {
                }
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "XML body is not a single well formed element: {0}",
                    xe.Message);
            }

            // written raw: the indenting writer would add whitespace inside it.
            m_writer.WriteRaw(body);
        }

        /// <inheritdoc/>
        public void WriteNodeId(string? fieldName, NodeId value)
        {
            WriteNodeId(fieldName, value, false);
        }

        private void WriteNodeId(string? fieldName, NodeId value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsNull, true, isArrayElement))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                ushort namespaceIndex = value.NamespaceIndex;

                if (!value.IsNull && m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                {
                    namespaceIndex = m_namespaceMappings[namespaceIndex];
                }

                var buffer = new StringBuilder();
                NodeId.Format(
                    CultureInfo.InvariantCulture,
                    buffer,
                    value.WithNamespaceIndex(namespaceIndex));
                WriteString("Identifier", buffer.ToString());

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value)
        {
            WriteExpandedNodeId(fieldName, value, false);
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        private void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsNull, true, isArrayElement))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                ushort namespaceIndex = value.NamespaceIndex;

                if (!value.IsNull && m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                {
                    namespaceIndex = m_namespaceMappings[namespaceIndex];
                }

                uint serverIndex = value.ServerIndex;

                if (!value.IsNull && m_serverMappings != null && m_serverMappings.Length > serverIndex)
                {
                    serverIndex = m_serverMappings[serverIndex];
                }

                var buffer = new StringBuilder();
                ExpandedNodeId.Format(
                    CultureInfo.InvariantCulture,
                    buffer,
                    value.IdentifierAsString,
                    value.IdType,
                    namespaceIndex,
                    value.NamespaceUri,
                    serverIndex);
                WriteString("Identifier", buffer.ToString());

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteStatusCode(string? fieldName, StatusCode value)
        {
            if (BeginField(fieldName, false, false))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                WriteUInt32("Code", value.Code);

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value)
        {
            WriteDiagnosticInfo(fieldName, value, 0);
        }

        /// <summary>
        /// Writes a DiagnosticInfo to the stream.
        /// </summary>
        private void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value, int depth)
        {
            CheckAndIncrementNestingLevel();

            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (value != null)
                {
                    WriteInt32("SymbolicId", value.SymbolicId);
                    WriteInt32("NamespaceUri", value.NamespaceUri);
                    WriteInt32("Locale", value.Locale);
                    WriteInt32("LocalizedText", value.LocalizedText);
                    WriteString("AdditionalInfo", value.AdditionalInfo);
                    WriteStatusCode("InnerStatusCode", value.InnerStatusCode);
                    if (depth < DiagnosticInfo.MaxInnerDepth)
                    {
                        WriteDiagnosticInfo(
                            "InnerDiagnosticInfo",
                            value.InnerDiagnosticInfo,
                            depth + 1);
                    }
                    else
                    {
                        Logger.InnerDiagnosticInfoDropped(DiagnosticInfo.MaxInnerDepth);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void WriteQualifiedName(string? fieldName, QualifiedName value)
        {
            WriteQualifiedName(fieldName, value, false);
        }

        /// <summary>
        /// Writes an QualifiedName to the stream.
        /// </summary>
        private void WriteQualifiedName(string? fieldName, QualifiedName value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsNull, true, isArrayElement))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                ushort namespaceIndex = value.NamespaceIndex;

                if (!value.IsNull && m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                {
                    namespaceIndex = m_namespaceMappings[namespaceIndex];
                }

                if (!value.IsNull)
                {
                    WriteUInt16("NamespaceIndex", namespaceIndex);
                    WriteString("Name", value.Name);
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteLocalizedText(string? fieldName, LocalizedText value)
        {
            WriteLocalizedText(fieldName, value, false);
        }

        /// <summary>
        /// Writes an LocalizedText to the stream.
        /// </summary>
        private void WriteLocalizedText(string? fieldName, LocalizedText value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsNull, true, isArrayElement))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (!value.IsNull)
                {
                    if (!string.IsNullOrEmpty(value.Locale))
                    {
                        WriteString("Locale", value.Locale);
                    }

                    if (!string.IsNullOrEmpty(value.Text))
                    {
                        WriteString("Text", value.Text);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteVariant(string? fieldName, in Variant value)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                if (BeginField(fieldName, false, false))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    m_writer.WriteStartElement("Value", Namespaces.OpcUaXsd);

                    WriteVariantValue(null, value, false);

                    m_writer.WriteEndElement();

                    PopNamespace();

                    EndField(fieldName);
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public void WriteDataValue(string? fieldName, in DataValue value)
        {
            if (BeginField(fieldName, value.IsNull, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                WriteVariant("Value", value.WrappedValue);
                WriteStatusCode("StatusCode", value.StatusCode);
                WriteDateTime("SourceTimestamp", value.SourceTimestamp);
                WriteUInt16("SourcePicoseconds", value.SourcePicoseconds);
                WriteDateTime("ServerTimestamp", value.ServerTimestamp);
                WriteUInt16("ServerPicoseconds", value.ServerPicoseconds);

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteExtensionObject(string? fieldName, ExtensionObject value)
        {
            WriteExtensionObject(fieldName, value, false);
        }

        /// <summary>
        /// Writes an ExtensionObject to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteExtensionObject(string? fieldName, ExtensionObject value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsNull, true, isArrayElement))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                // write the type id.
                ExpandedNodeId typeId = value.TypeId;

                if (value.TryGetValue(out IEncodeable? encodeable))
                {
                    typeId = encodeable!.XmlEncodingId;
                }

                var localTypeId = ExpandedNodeId.ToNodeId(typeId, Context.NamespaceUris);

                if (localTypeId.IsNull && !typeId.IsNull)
                {
                    if (encodeable != null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Cannot encode bodies of type '{0}' in ExtensionObject unless the NamespaceUri ({1}) is in the encoder's NamespaceTable.",
                            encodeable.GetType().FullName ?? string.Empty,
                            typeId.NamespaceUri ?? string.Empty);
                    }

                    localTypeId = NodeId.Null;
                }

                WriteNodeId("TypeId", localTypeId);

                // write the body.
                m_writer.WriteStartElement("Body", Namespaces.OpcUaXsd);

                WriteExtensionObjectBody(value);

                // end of body.
                m_writer.WriteEndElement();

                EndField(fieldName);
                PopNamespace();
            }
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value) where T : IEncodeable, new()
        {
            WriteEncodeable(fieldName, value, default);
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();

            if (BeginField(fieldName, EqualityComparer<T>.Default.Equals(value, default!), true))
            {
                value?.Encode(this);

                EndField(fieldName);
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void WriteEncodeableAsExtensionObject<T>(string? fieldName, T value)
            where T : IEncodeable
        {
            WriteExtensionObject(fieldName, new ExtensionObject(value));
        }

        /// <inheritdoc/>
        public void WriteEnumerated<T>(string? fieldName, T value) where T : struct, Enum
        {
            int int32Value = EnumHelper.EnumToInt32(value); // TODO: We assume 0 is default == null

            if (BeginField(fieldName, int32Value == 0, true))
            {
                if (int32Value != 0)
                {
                    m_writer.WriteString(CoreUtils.Format("{0}_{1}",
                        value.ToString(),
                        int32Value));
                }

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteEnumerated(string? fieldName, EnumValue value)
        {
            if (BeginField(fieldName, value.Value == 0, true))
            {
                if (value.Value != 0)
                {
                    if (!string.IsNullOrEmpty(value.Symbol))
                    {
                        m_writer.WriteString(CoreUtils.Format("{0}_{1}",
                            value.Symbol!,
                            value.Value));
                    }
                    else
                    {
                        m_writer.WriteString(CoreUtils.Format("{0}",
                            value.Value));
                    }
                }

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string? fieldName, ArrayOf<bool> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteBoolean("Boolean", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteSByteArray(string? fieldName, ArrayOf<sbyte> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteSByte("SByte", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteByteArray(string? fieldName, ArrayOf<byte> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteByte("Byte", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt16Array(string? fieldName, ArrayOf<short> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteInt16("Int16", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt16Array(string? fieldName, ArrayOf<ushort> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteUInt16("UInt16", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt32Array(string? fieldName, ArrayOf<int> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteInt32("Int32", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt32Array(string? fieldName, ArrayOf<uint> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteUInt32("UInt32", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt64Array(string? fieldName, ArrayOf<long> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteInt64("Int64", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt64Array(string? fieldName, ArrayOf<ulong> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteUInt64("UInt64", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteFloatArray(string? fieldName, ArrayOf<float> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteFloat("Float", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDoubleArray(string? fieldName, ArrayOf<double> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteDouble("Double", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteStringArray(string? fieldName, ArrayOf<string> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteString("String", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDateTimeArray(string? fieldName, ArrayOf<DateTimeUtc> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteDateTime("DateTime", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteGuidArray(string? fieldName, ArrayOf<Uuid> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteGuid("Guid", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteByteStringArray(string? fieldName, ArrayOf<ByteString> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteByteString("ByteString", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteXmlElementArray(string? fieldName, ArrayOf<XmlElement> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteXmlElement("XmlElement", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteNodeIdArray(string? fieldName, ArrayOf<NodeId> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteNodeId("NodeId", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeIdArray(string? fieldName, ArrayOf<ExpandedNodeId> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteExpandedNodeId("ExpandedNodeId", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteStatusCodeArray(string? fieldName, ArrayOf<StatusCode> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteStatusCode("StatusCode", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfoArray(string? fieldName, ArrayOf<DiagnosticInfo> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteDiagnosticInfo("DiagnosticInfo", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteQualifiedNameArray(string? fieldName, ArrayOf<QualifiedName> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteQualifiedName("QualifiedName", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteLocalizedTextArray(string? fieldName, ArrayOf<LocalizedText> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteLocalizedText("LocalizedText", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteVariantArray(string? fieldName, ArrayOf<Variant> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteVariant("Variant", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDataValueArray(string? fieldName, ArrayOf<DataValue> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteDataValue("DataValue", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteExtensionObjectArray(string? fieldName, ArrayOf<ExtensionObject> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteExtensionObject("ExtensionObject", values[ii], true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteEncodeableArrayAsExtensionObjects<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            WriteExtensionObjectArray(fieldName, values.ConvertAll(v => new ExtensionObject(v)));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName,
            ArrayOf<T> values,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Encodeable Array length={0}",
                        values.Count);
                }

                // encode each element in the array.
                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteEncodeable(values[ii]);
                }

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable, new()
        {
            WriteEncodeableArray(fieldName, values, default);
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName,
            MatrixOf<T> values,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();

            // The Dimensions of an XML Matrix must all be greater than zero
            // (OPC 10000-6 5.3.1.17), so an empty matrix is written as null,
            // which is equivalent to an empty array (5.1.11).
            bool isNull = values.IsNull || values.Count == 0;
            if (BeginField(fieldName, isNull, true, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                if (!isNull)
                {
                    // The field element is of the Matrix type (5.3.4) with at
                    // least two dimensions (5.3.1.17).
                    WriteInt32Array("Dimensions", MatrixOf.GetInlineMatrixDimensions(
                        values.Dimensions,
                        values.Count));
                    WriteEncodeableArray("Elements", values.ToArrayOf(), encodeableTypeId);
                }
                PopNamespace();
                EndField(fieldName);
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values)
            where T : IEncodeable, new()
        {
            WriteEncodeableMatrix(fieldName, values, default);
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string? fieldName, ArrayOf<T> values)
            where T : struct, Enum
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Enumerated Array length={0}",
                        values.Count);
                }

                // get name for type being encoded.
                XmlQualifiedName xmlName =
                    TypeInfo.GetXmlName(typeof(T)) ??
                    new XmlQualifiedName("Enumerated", Namespaces.OpcUaXsd);

                PushNamespace(xmlName.Namespace);

                // encode each element in the array.
                foreach (T value in values)
                {
                    WriteEnumeratedElement(xmlName.Name, EnumHelper.EnumToInt32(value), value.ToString());
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray(string? fieldName, ArrayOf<EnumValue> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Enumerated Array length={0}",
                        values.Count);
                }

                // encode each element in the array.
                foreach (EnumValue value in values)
                {
                    XmlQualifiedName xmlName = value.XmlName
                        ?? new XmlQualifiedName("Enumerated", Namespaces.OpcUaXsd);

                    PushNamespace(xmlName.Namespace);
                    WriteEnumeratedElement(xmlName.Name, value.Value, value.Symbol);
                    PopNamespace();
                }

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an element of an enumeration array. Unlike a field, an
        /// array element is written also for the value 0, which would
        /// otherwise be dropped from the array (OPC 10000-6 5.3.4).
        /// </summary>
        private void WriteEnumeratedElement(string name, int value, string? symbol)
        {
            m_writer.WriteStartElement(name, m_namespaces.Peek());
            m_writer.WriteString(string.IsNullOrEmpty(symbol)
                ? value.ToString(CultureInfo.InvariantCulture)
                : CoreUtils.Format("{0}_{1}", symbol!, value));
            m_writer.WriteEndElement();
        }
        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string? fieldName)
        {
            fieldName = null;
            WriteUInt32("SwitchField", switchField);
        }

        /// <inheritdoc/>
        public void WriteEncodingMask(uint encodingMask)
        {
            WriteUInt32("EncodingMask", encodingMask);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A named field is the raw value of a structure field, whose
        /// matrix is written as an inline matrix (OPC 10000-6 5.2.5, 5.3.4).
        /// Without a field name the content of the Variant is written with
        /// the Variant rules (the way <see cref="WriteVariant"/> writes it),
        /// e.g. the value of a Variable in a NodeSet or a serialized Variant:
        /// a null matrix is a nil ListOf element, a matrix with a single
        /// dimension an array.
        /// A named scalar or array of a built-in type is written like the
        /// typed writer of its type writes a structure field (OPC 10000-6
        /// 5.3.1, 5.3.4, 5.3.5): <c>&lt;A&gt;1&lt;/A&gt;</c> and an array
        /// as <c>&lt;A&gt;&lt;Int32&gt;1&lt;/Int32&gt;...&lt;/A&gt;</c>, not
        /// wrapped as a Variant body (<c>&lt;A&gt;&lt;Int32&gt;</c>,
        /// <c>&lt;A&gt;&lt;ListOfInt32&gt;</c>) as earlier versions did.
        /// </remarks>
        public void WriteVariantValue(string? fieldName, in Variant value)
        {
            if (fieldName != null && TryWriteFieldValue(this, fieldName, in value))
            {
                return;
            }
            WriteVariantValue(fieldName, in value, fieldName != null);
        }

        /// <summary>
        /// Writes a structure field value of a built-in scalar or array type
        /// with the typed writer of the type (OPC 10000-6 5.3.1, 5.3.4,
        /// 5.3.5), the way generated code writes the field. False for a
        /// value without type information, a matrix and the types that are
        /// not written as a field of their own type.
        /// </summary>
        private static bool TryWriteFieldValue(XmlEncoder encoder, string fieldName, in Variant value)
        {
            TypeInfo typeInfo = value.TypeInfo;
            if (typeInfo.IsUnknown)
            {
                return false;
            }
            if (typeInfo.ValueRank == ValueRanks.Scalar)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        encoder.WriteBoolean(fieldName, value.GetBoolean());
                        return true;
                    case BuiltInType.SByte:
                        encoder.WriteSByte(fieldName, value.GetSByte());
                        return true;
                    case BuiltInType.Byte:
                        encoder.WriteByte(fieldName, value.GetByte());
                        return true;
                    case BuiltInType.Int16:
                        encoder.WriteInt16(fieldName, value.GetInt16());
                        return true;
                    case BuiltInType.UInt16:
                        encoder.WriteUInt16(fieldName, value.GetUInt16());
                        return true;
                    case BuiltInType.Int32:
                        encoder.WriteInt32(fieldName, value.GetInt32());
                        return true;
                    case BuiltInType.UInt32:
                        encoder.WriteUInt32(fieldName, value.GetUInt32());
                        return true;
                    case BuiltInType.Int64:
                        encoder.WriteInt64(fieldName, value.GetInt64());
                        return true;
                    case BuiltInType.UInt64:
                        encoder.WriteUInt64(fieldName, value.GetUInt64());
                        return true;
                    case BuiltInType.Float:
                        encoder.WriteFloat(fieldName, value.GetFloat());
                        return true;
                    case BuiltInType.Double:
                        encoder.WriteDouble(fieldName, value.GetDouble());
                        return true;
                    case BuiltInType.String:
                        encoder.WriteString(fieldName, value.GetString());
                        return true;
                    case BuiltInType.DateTime:
                        encoder.WriteDateTime(fieldName, value.GetDateTime());
                        return true;
                    case BuiltInType.Guid:
                        encoder.WriteGuid(fieldName, value.GetGuid());
                        return true;
                    case BuiltInType.ByteString:
                        encoder.WriteByteString(fieldName, value.GetByteString());
                        return true;
                    case BuiltInType.XmlElement:
                        encoder.WriteXmlElement(fieldName, value.GetXmlElement());
                        return true;
                    case BuiltInType.NodeId:
                        encoder.WriteNodeId(fieldName, value.GetNodeId());
                        return true;
                    case BuiltInType.ExpandedNodeId:
                        encoder.WriteExpandedNodeId(fieldName, value.GetExpandedNodeId());
                        return true;
                    case BuiltInType.StatusCode:
                        encoder.WriteStatusCode(fieldName, value.GetStatusCode());
                        return true;
                    case BuiltInType.QualifiedName:
                        encoder.WriteQualifiedName(fieldName, value.GetQualifiedName());
                        return true;
                    case BuiltInType.LocalizedText:
                        encoder.WriteLocalizedText(fieldName, value.GetLocalizedText());
                        return true;
                    case BuiltInType.ExtensionObject:
                        encoder.WriteExtensionObject(fieldName, value.GetExtensionObject());
                        return true;
                    case BuiltInType.DataValue:
                        encoder.WriteDataValue(fieldName, value.GetDataValue());
                        return true;
                    case BuiltInType.Enumeration when value.IsNull:
                        encoder.WriteEnumerated(fieldName, default(EnumValue));
                        return true;
                    case BuiltInType.Enumeration when value.TryGetValue(out EnumValue enumValue):
                        encoder.WriteEnumerated(fieldName, enumValue);
                        return true;
                    default:
                        return false;
                }
            }
            if (typeInfo.ValueRank != ValueRanks.OneDimension ||
                value.IsInlineMatrix(out _))
            {
                return false;
            }
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                    encoder.WriteBooleanArray(fieldName, value.GetBooleanArray());
                    return true;
                case BuiltInType.SByte:
                    encoder.WriteSByteArray(fieldName, value.GetSByteArray());
                    return true;
                case BuiltInType.Byte:
                    encoder.WriteByteArray(fieldName, value.GetByteArray());
                    return true;
                case BuiltInType.Int16:
                    encoder.WriteInt16Array(fieldName, value.GetInt16Array());
                    return true;
                case BuiltInType.UInt16:
                    encoder.WriteUInt16Array(fieldName, value.GetUInt16Array());
                    return true;
                case BuiltInType.Int32:
                    encoder.WriteInt32Array(fieldName, value.GetInt32Array());
                    return true;
                case BuiltInType.UInt32:
                    encoder.WriteUInt32Array(fieldName, value.GetUInt32Array());
                    return true;
                case BuiltInType.Int64:
                    encoder.WriteInt64Array(fieldName, value.GetInt64Array());
                    return true;
                case BuiltInType.UInt64:
                    encoder.WriteUInt64Array(fieldName, value.GetUInt64Array());
                    return true;
                case BuiltInType.Float:
                    encoder.WriteFloatArray(fieldName, value.GetFloatArray());
                    return true;
                case BuiltInType.Double:
                    encoder.WriteDoubleArray(fieldName, value.GetDoubleArray());
                    return true;
                case BuiltInType.String:
                    encoder.WriteStringArray(fieldName, value.GetStringArray());
                    return true;
                case BuiltInType.DateTime:
                    encoder.WriteDateTimeArray(fieldName, value.GetDateTimeArray());
                    return true;
                case BuiltInType.Guid:
                    encoder.WriteGuidArray(fieldName, value.GetGuidArray());
                    return true;
                case BuiltInType.ByteString:
                    encoder.WriteByteStringArray(fieldName, value.GetByteStringArray());
                    return true;
                case BuiltInType.XmlElement:
                    encoder.WriteXmlElementArray(fieldName, value.GetXmlElementArray());
                    return true;
                case BuiltInType.NodeId:
                    encoder.WriteNodeIdArray(fieldName, value.GetNodeIdArray());
                    return true;
                case BuiltInType.ExpandedNodeId:
                    encoder.WriteExpandedNodeIdArray(fieldName, value.GetExpandedNodeIdArray());
                    return true;
                case BuiltInType.StatusCode:
                    encoder.WriteStatusCodeArray(fieldName, value.GetStatusCodeArray());
                    return true;
                case BuiltInType.QualifiedName:
                    encoder.WriteQualifiedNameArray(fieldName, value.GetQualifiedNameArray());
                    return true;
                case BuiltInType.LocalizedText:
                    encoder.WriteLocalizedTextArray(fieldName, value.GetLocalizedTextArray());
                    return true;
                case BuiltInType.ExtensionObject:
                    encoder.WriteExtensionObjectArray(fieldName, value.GetExtensionObjectArray());
                    return true;
                case BuiltInType.DataValue:
                    encoder.WriteDataValueArray(fieldName, value.GetDataValueArray());
                    return true;
                case BuiltInType.Variant:
                    encoder.WriteVariantArray(fieldName, value.GetVariantArray());
                    return true;
                case BuiltInType.Enumeration when value.IsNull:
                    encoder.WriteEnumeratedArray(fieldName, default(ArrayOf<EnumValue>));
                    return true;
                case BuiltInType.Enumeration when value.TryGetValue(out ArrayOf<EnumValue> enumValues):
                    encoder.WriteEnumeratedArray(fieldName, enumValues);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Writes the body of an ExtensionObject to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExtensionObjectBody(ExtensionObject extensionObject)
        {
            // nothing to do for null bodies.
            if (!extensionObject.IsNull)
            {
                // encode byte body.
                if (extensionObject.TryGetAsBinary(out ByteString bytes))
                {
                    m_writer.WriteStartElement("ByteString", Namespaces.OpcUaXsd);
                    m_writer.WriteString(
                        bytes.ToBase64(Base64FormattingOptions.InsertLineBreaks));
                    m_writer.WriteEndElement();
                }
                // encode xml body.
                else if (extensionObject.TryGetAsXml(out XmlElement xml))
                {
                    // the body is not validated when it is received (e.g. an
                    // XML body of an unknown type in a binary message).
                    WriteXmlBody(xml);
                }
                else if (extensionObject.TryGetValue(out IEncodeable? encodeable))
                {
                    // encode extension object in xml.
                    XmlQualifiedName? xmlName = TypeInfo.GetXmlName(encodeable, Context);
                    m_writer.WriteStartElement(xmlName!.Name, xmlName.Namespace);

                    // count the body against the nesting budget like
                    // XmlDecoder.ReadExtensionObject does.
                    CheckAndIncrementNestingLevel();
                    try
                    {
                        encodeable!.Encode(this);
                    }
                    finally
                    {
                        m_nestingLevel--;
                    }

                    m_writer.WriteEndElement();
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError,
                        CoreUtils.Format(
                            "Don't know how to encode extension object body with type '{0}'.",
                            extensionObject));
                }
            }
        }

        /// <summary>
        /// Write a variant value in raw mode (content as it would be encoded
        /// if each encoder call would be called) or as variant encoding
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteVariantValue(string? fieldName, in Variant value, bool writeRawValue)
        {
            if (fieldName != null && BeginField(fieldName, false, false))
            {
                PushNamespace(Namespaces.OpcUaXsd);
            }
            try
            {
                // A raw matrix field value is always an inline matrix, also
                // when the Variant lost the matrix type info (a null or an
                // empty MatrixOf).
                bool isInlineMatrix = false;
                bool isNullMatrix = false;
                if (writeRawValue && !value.TypeInfo.IsScalar)
                {
                    isInlineMatrix = value.IsInlineMatrix(out isNullMatrix);
                }

                // check for null. The Dimensions of an XML Matrix must all be
                // greater than zero (OPC 10000-6 5.3.1.17), so an empty inline
                // matrix is written as null, which is equivalent to an empty
                // array (5.1.11).
                if (value.IsNull ||
                    (isInlineMatrix && (isNullMatrix || value.IsEmptyMatrix)))
                {
                    m_writer.WriteAttributeString("xsi", "nil", Namespaces.XmlSchemaInstance, "true");
                    return;
                }

                // An empty matrix Variant has no valid Dimensions and is
                // written as an empty array (OPC 10000-6 5.2.2.16, 5.3.1.17).
                if (!writeRawValue && value.IsEmptyMatrix)
                {
                    WriteVariantValue(null, value.ToEmptyArray(), false);
                    return;
                }
                try
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    if (value.TypeInfo.IsScalar)
                    {
                        // write scalar.
                        switch (value.TypeInfo.BuiltInType)
                        {
                            case BuiltInType.Boolean:
                                WriteBoolean("Boolean", value.GetBoolean());
                                return;
                            case BuiltInType.SByte:
                                WriteSByte("SByte", value.GetSByte());
                                return;
                            case BuiltInType.Byte:
                                WriteByte("Byte", value.GetByte());
                                return;
                            case BuiltInType.Int16:
                                WriteInt16("Int16", value.GetInt16());
                                return;
                            case BuiltInType.UInt16:
                                WriteUInt16("UInt16", value.GetUInt16());
                                return;
                            // case BuiltInType.Enumeration when writeRawValue:
                            //     WriteEnumerated("Enumeration", value.GetEnumeration());
                            case BuiltInType.Int32:
                            case BuiltInType.Enumeration:
                                WriteInt32("Int32", value.GetInt32());
                                return;
                            case BuiltInType.UInt32:
                                WriteUInt32("UInt32", value.GetUInt32());
                                return;
                            case BuiltInType.Int64:
                                WriteInt64("Int64", value.GetInt64());
                                return;
                            case BuiltInType.UInt64:
                                WriteUInt64("UInt64", value.GetUInt64());
                                return;
                            case BuiltInType.Float:
                                WriteFloat("Float", value.GetFloat());
                                return;
                            case BuiltInType.Double:
                                WriteDouble("Double", value.GetDouble());
                                return;
                            case BuiltInType.String:
                                WriteString("String", value.GetString());
                                return;
                            case BuiltInType.DateTime:
                                WriteDateTime("DateTime", value.GetDateTime());
                                return;
                            case BuiltInType.Guid:
                                WriteGuid("Guid", value.GetGuid());
                                return;
                            case BuiltInType.ByteString:
                                WriteByteString("ByteString", value.GetByteString());
                                return;
                            case BuiltInType.XmlElement:
                                WriteXmlElement("XmlElement", value.GetXmlElement());
                                return;
                            case BuiltInType.NodeId:
                                WriteNodeId("NodeId", value.GetNodeId());
                                return;
                            case BuiltInType.ExpandedNodeId:
                                WriteExpandedNodeId("ExpandedNodeId", value.GetExpandedNodeId());
                                return;
                            case BuiltInType.StatusCode:
                                WriteStatusCode("StatusCode", value.GetStatusCode());
                                return;
                            case BuiltInType.QualifiedName:
                                WriteQualifiedName("QualifiedName", value.GetQualifiedName());
                                return;
                            case BuiltInType.LocalizedText:
                                WriteLocalizedText("LocalizedText", value.GetLocalizedText());
                                return;
                            case BuiltInType.ExtensionObject:
                                WriteExtensionObject("ExtensionObject", value.GetExtensionObject());
                                return;
                            case BuiltInType.DataValue:
                                WriteDataValue("DataValue", value.GetDataValue());
                                return;
                            case BuiltInType.Null:
                            case BuiltInType.Variant:
                            case BuiltInType.DiagnosticInfo:
                            case BuiltInType.Number:
                            case BuiltInType.Integer:
                            case BuiltInType.UInteger:
                                throw new ServiceResultException(
                                    StatusCodes.BadEncodingError,
                                    CoreUtils.Format(
                                        "Type '{0}' is not allowed in an Variant.",
                                        value.TypeInfo));
                            default:
                                throw ServiceResultException.Unexpected(
                                    $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                        }
                    }
                    else if (value.TypeInfo.IsArray && !isInlineMatrix)
                    {
                        // write array.
                        switch (value.TypeInfo.BuiltInType)
                        {
                            case BuiltInType.Boolean:
                                WriteBooleanArray("ListOfBoolean", value.GetBooleanArray());
                                return;
                            case BuiltInType.SByte:
                                WriteSByteArray("ListOfSByte", value.GetSByteArray());
                                return;
                            case BuiltInType.Byte:
                                WriteByteArray("ListOfByte", value.GetByteArray());
                                return;
                            case BuiltInType.Int16:
                                WriteInt16Array("ListOfInt16", value.GetInt16Array());
                                return;
                            case BuiltInType.UInt16:
                                WriteUInt16Array("ListOfUInt16", value.GetUInt16Array());
                                return;
                            // case BuiltInType.Enumeration when writeRawValue:
                            //     WriteEnumerated("ListOfEnumeration", value.GetEnumerationArray());
                            case BuiltInType.Int32:
                            case BuiltInType.Enumeration:
                                WriteInt32Array("ListOfInt32", value.GetInt32Array());
                                return;
                            case BuiltInType.UInt32:
                                WriteUInt32Array("ListOfUInt32", value.GetUInt32Array());
                                return;
                            case BuiltInType.Int64:
                                WriteInt64Array("ListOfInt64", value.GetInt64Array());
                                return;
                            case BuiltInType.UInt64:
                                WriteUInt64Array("ListOfUInt64", value.GetUInt64Array());
                                return;
                            case BuiltInType.Float:
                                WriteFloatArray("ListOfFloat", value.GetFloatArray());
                                return;
                            case BuiltInType.Double:
                                WriteDoubleArray("ListOfDouble", value.GetDoubleArray());
                                return;
                            case BuiltInType.String:
                                WriteStringArray("ListOfString", value.GetStringArray());
                                return;
                            case BuiltInType.DateTime:
                                WriteDateTimeArray("ListOfDateTime", value.GetDateTimeArray());
                                return;
                            case BuiltInType.Guid:
                                WriteGuidArray("ListOfGuid", value.GetGuidArray());
                                return;
                            case BuiltInType.ByteString:
                                WriteByteStringArray("ListOfByteString", value.GetByteStringArray());
                                return;
                            case BuiltInType.XmlElement:
                                WriteXmlElementArray("ListOfXmlElement", value.GetXmlElementArray());
                                return;
                            case BuiltInType.NodeId:
                                WriteNodeIdArray("ListOfNodeId", value.GetNodeIdArray());
                                return;
                            case BuiltInType.ExpandedNodeId:
                                WriteExpandedNodeIdArray(
                                    "ListOfExpandedNodeId",
                                    value.GetExpandedNodeIdArray());
                                return;
                            case BuiltInType.StatusCode:
                                WriteStatusCodeArray("ListOfStatusCode", value.GetStatusCodeArray());
                                return;
                            case BuiltInType.QualifiedName:
                                WriteQualifiedNameArray("ListOfQualifiedName", value.GetQualifiedNameArray());
                                return;
                            case BuiltInType.LocalizedText:
                                WriteLocalizedTextArray("ListOfLocalizedText", value.GetLocalizedTextArray());
                                return;
                            case BuiltInType.ExtensionObject:
                                WriteExtensionObjectArray(
                                    "ListOfExtensionObject",
                                    value.GetExtensionObjectArray());
                                return;
                            case BuiltInType.DataValue:
                                WriteDataValueArray("ListOfDataValue", value.GetDataValueArray());
                                return;
                            case BuiltInType.Variant:
                                WriteVariantArray("ListOfVariant", value.GetVariantArray());
                                return;
                            case BuiltInType.Null:
                            case BuiltInType.DiagnosticInfo:
                            case BuiltInType.Number:
                            case BuiltInType.Integer:
                            case BuiltInType.UInteger:
                                throw new ServiceResultException(
                                    StatusCodes.BadEncodingError,
                                    CoreUtils.Format(
                                        "Type '{0}' is not allowed in an Variant.",
                                        value.TypeInfo));
                            default:
                                throw ServiceResultException.Unexpected(
                                    $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                        }
                    }
                    else
                    {
                        CheckAndIncrementNestingLevel();

                        // A matrix Variant is a Matrix element (5.3.1.17). A
                        // matrix structure field is of the Matrix type itself:
                        // the field element directly contains Dimensions and
                        // Elements, without a Matrix wrapper (OPC 10000-6 5.3.4).
                        bool wrapInMatrix = !writeRawValue;
                        if (!wrapInMatrix || BeginField("Matrix", value.IsNull, true, true))
                        {
                            const string elements = "Elements";

                            // A Matrix must carry Dimensions where every entry
                            // is greater than zero and the product equals the
                            // flattened element count (Part 6 5.2.2.16,
                            // 5.3.1.17). Refuse to emit inconsistent dimensions
                            // instead of writing data a conforming peer must
                            // reject with BadDecodingError. An empty matrix was
                            // written as null or an empty array above.
                            void WriteDimensions<T>(MatrixOf<T> matrix)
                            {
                                int[] dimensions = matrix.Dimensions;
                                if (!MatrixOf.IsValidMatrix(dimensions))
                                {
                                    throw ServiceResultException.Create(
                                        StatusCodes.BadEncodingError,
                                        "Cannot encode a matrix Variant with " +
                                        "inconsistent Dimensions [{0}].",
                                        string.Join(",", dimensions));
                                }
                                WriteInt32Array("Dimensions", dimensions);
                            }

                            PushNamespace(Namespaces.OpcUaXsd);
                            if (!value.IsNull)
                            {
                                switch (value.TypeInfo.BuiltInType)
                                {
                                    case BuiltInType.Boolean:
                                    {
                                        MatrixOf<bool> matrix = value.GetBooleanMatrix();
                                        WriteDimensions(matrix);
                                        WriteBooleanArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.SByte:
                                    {
                                        MatrixOf<sbyte> matrix = value.GetSByteMatrix();
                                        WriteDimensions(matrix);
                                        WriteSByteArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.Byte:
                                    {
                                        MatrixOf<byte> matrix = value.GetByteMatrix();
                                        WriteDimensions(matrix);
                                        WriteByteArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.Int16:
                                    {
                                        MatrixOf<short> matrix = value.GetInt16Matrix();
                                        WriteDimensions(matrix);
                                        WriteInt16Array(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.UInt16:
                                    {
                                        MatrixOf<ushort> matrix = value.GetUInt16Matrix();
                                        WriteDimensions(matrix);
                                        WriteUInt16Array(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    // case BuiltInType.Enumeration when writeRawValue:
                                    // {
                                    //     MatrixOf<int> matrix = value.GetEnumerationMatrix();
                                    //     WriteDimensions(matrix);
                                    //     WriteEnumeratedArray(elements, matrix.ToArrayOf());
                                    //     break;
                                    // }
                                    case BuiltInType.Int32:
                                    case BuiltInType.Enumeration:
                                    {
                                        MatrixOf<int> matrix = value.GetInt32Matrix();
                                        WriteDimensions(matrix);
                                        WriteInt32Array(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.UInt32:
                                    {
                                        MatrixOf<uint> matrix = value.GetUInt32Matrix();
                                        WriteDimensions(matrix);
                                        WriteUInt32Array(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.Int64:
                                    {
                                        MatrixOf<long> matrix = value.GetInt64Matrix();
                                        WriteDimensions(matrix);
                                        WriteInt64Array(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.UInt64:
                                    {
                                        MatrixOf<ulong> matrix = value.GetUInt64Matrix();
                                        WriteDimensions(matrix);
                                        WriteUInt64Array(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.Float:
                                    {
                                        MatrixOf<float> matrix = value.GetFloatMatrix();
                                        WriteDimensions(matrix);
                                        WriteFloatArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.Double:
                                    {
                                        MatrixOf<double> matrix = value.GetDoubleMatrix();
                                        WriteDimensions(matrix);
                                        WriteDoubleArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.String:
                                    {
                                        MatrixOf<string> matrix = value.GetStringMatrix();
                                        WriteDimensions(matrix);
                                        WriteStringArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.DateTime:
                                    {
                                        MatrixOf<DateTimeUtc> matrix = value.GetDateTimeMatrix();
                                        WriteDimensions(matrix);
                                        WriteDateTimeArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.Guid:
                                    {
                                        MatrixOf<Uuid> matrix = value.GetGuidMatrix();
                                        WriteDimensions(matrix);
                                        WriteGuidArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.ByteString:
                                    {
                                        MatrixOf<ByteString> matrix = value.GetByteStringMatrix();
                                        WriteDimensions(matrix);
                                        WriteByteStringArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.XmlElement:
                                    {
                                        MatrixOf<XmlElement> matrix = value.GetXmlElementMatrix();
                                        WriteDimensions(matrix);
                                        WriteXmlElementArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.NodeId:
                                    {
                                        MatrixOf<NodeId> matrix = value.GetNodeIdMatrix();
                                        WriteDimensions(matrix);
                                        WriteNodeIdArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.ExpandedNodeId:
                                    {
                                        MatrixOf<ExpandedNodeId> matrix = value.GetExpandedNodeIdMatrix();
                                        WriteDimensions(matrix);
                                        WriteExpandedNodeIdArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.StatusCode:
                                    {
                                        MatrixOf<StatusCode> matrix = value.GetStatusCodeMatrix();
                                        WriteDimensions(matrix);
                                        WriteStatusCodeArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.QualifiedName:
                                    {
                                        MatrixOf<QualifiedName> matrix = value.GetQualifiedNameMatrix();
                                        WriteDimensions(matrix);
                                        WriteQualifiedNameArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.LocalizedText:
                                    {
                                        MatrixOf<LocalizedText> matrix = value.GetLocalizedTextMatrix();
                                        WriteDimensions(matrix);
                                        WriteLocalizedTextArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.ExtensionObject:
                                    {
                                        MatrixOf<ExtensionObject> matrix = value.GetExtensionObjectMatrix();
                                        WriteDimensions(matrix);
                                        WriteExtensionObjectArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.DataValue:
                                    {
                                        MatrixOf<DataValue> matrix = value.GetDataValueMatrix();
                                        WriteDimensions(matrix);
                                        WriteDataValueArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.Variant:
                                    {
                                        MatrixOf<Variant> matrix = value.GetVariantMatrix();
                                        WriteDimensions(matrix);
                                        WriteVariantArray(elements, matrix.ToArrayOf());
                                        break;
                                    }
                                    case BuiltInType.DiagnosticInfo:
                                    case BuiltInType.Null:
                                    case BuiltInType.Number:
                                    case BuiltInType.Integer:
                                    case BuiltInType.UInteger:
                                        throw ServiceResultException.Create(
                                            StatusCodes.BadEncodingError,
                                            "Unexpected type encountered while encoding a Variant: {0}",
                                            value.TypeInfo);
                                    default:
                                        throw ServiceResultException.Unexpected(
                                            $"Unexpected BuiltInType {value.TypeInfo}");
                                }
                            }

                            PopNamespace();

                            if (wrapInMatrix)
                            {
                                EndField("Matrix");
                            }
                        }

                        m_nestingLevel--;
                    }
                }
                finally
                {
                    PopNamespace();
                }
            }
            finally
            {
                if (fieldName != null)
                {
                    PopNamespace();
                    EndField(fieldName);
                }
            }
        }

        /// <summary>
        /// Write encodeable structure
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="encodeable"></param>
        private void WriteEncodeable<T>(T encodeable) where T : IEncodeable
        {
            // get name for type being encoded.
            XmlQualifiedName xmlName =
                TypeInfo.GetXmlName(encodeable, Context)
                ?? new XmlQualifiedName("IEncodeable", Namespaces.OpcUaXsd);
            PushNamespace(xmlName.Namespace);
            WriteEncodeable(xmlName.Name, encodeable, encodeable.TypeId);
            PopNamespace();
        }

        /// <summary>
        /// Writes the start element for a field.
        /// </summary>
        private bool BeginField(
            string? fieldName,
            bool isDefault,
            bool isNillable,
            bool isArrayElement = false)
        {
            // specifying a null field name means the start/end tags should not be written.
            if (!string.IsNullOrEmpty(fieldName))
            {
                if (isNillable && isDefault && !isArrayElement)
                {
                    return false;
                }

                m_writer.WriteStartElement(fieldName, m_namespaces.Peek());

                // On the first element this encoder writes, declare the xsi prefix so
                // subsequent xsi:nil attributes inherit it instead of each site emitting
                // its own xmlns declaration under a synthesized prefix (e.g. p5).
                if (!m_prefixesInitialized)
                {
                    m_prefixesInitialized = true;
                    if (m_writer.LookupPrefix(Namespaces.XmlSchemaInstance) == null)
                    {
                        m_writer.WriteAttributeString("xmlns", "xsi", null, Namespaces.XmlSchemaInstance);
                    }
                }

                if (isDefault)
                {
                    if (isNillable)
                    {
                        m_writer.WriteAttributeString("xsi", "nil", Namespaces.XmlSchemaInstance, "true");
                    }

                    m_writer.WriteEndElement();
                    return false;
                }
            }

            return !isDefault;
        }

        /// <summary>
        /// Writes the end element for a field.
        /// </summary>
        private void EndField(string? fieldName)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                m_writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckAndIncrementNestingLevel()
        {
            if (m_nestingLevel > Context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    Context.MaxEncodingNestingLevels);
            }
            m_nestingLevel++;
        }

        private ILogger Logger => m_logger ??= Context.Telemetry.CreateLogger<XmlEncoder>();
        private ILogger? m_logger;
        private readonly StringBuilder? m_destination;
        private readonly XmlWriter m_writer;
        private readonly Stack<string> m_namespaces = [];
        private XmlQualifiedName? m_root;
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
        private uint m_nestingLevel;
        private bool m_disposed;
        private bool m_prefixesInitialized;
    }
}
