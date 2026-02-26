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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
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
            m_logger = context.Telemetry.CreateLogger<XmlEncoder>();
            m_destination = new StringBuilder();
            m_nestingLevel = 0;

            XmlWriterSettings settings = CoreUtils.DefaultXmlWriterSettings();
            settings.CheckCharacters = false;
            settings.ConformanceLevel = ConformanceLevel.Auto;
            settings.NamespaceHandling = NamespaceHandling.OmitDuplicates;
            settings.NewLineHandling = NewLineHandling.Replace;

            m_writer = XmlWriter.Create(m_destination, settings);
        }

        /// <summary>
        /// Initializes the object with a system type to encode and a XML writer.
        /// </summary>
        public XmlEncoder(Type systemType, XmlWriter writer, IServiceMessageContext context)
            : this(TypeInfo.GetXmlName(systemType), writer, context)
        {
        }

        /// <summary>
        /// Initializes the object with a system type to encode and a XML writer.
        /// </summary>
        public XmlEncoder(XmlQualifiedName root, XmlWriter writer, IServiceMessageContext context)
        {
            Context = context;
            m_logger = context.Telemetry.CreateLogger<XmlEncoder>();
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
        private void Initialize(string fieldName, string namespaceUri)
        {
            m_root = new XmlQualifiedName(fieldName, namespaceUri);

            string uaxPrefix = m_writer.LookupPrefix(Namespaces.OpcUaXsd);

            uaxPrefix ??= "uax";

            if (namespaceUri == Namespaces.OpcUaXsd)
            {
                m_writer.WriteStartElement(uaxPrefix, fieldName, namespaceUri);
            }
            else
            {
                m_writer.WriteStartElement(fieldName, namespaceUri);
            }

            string xsiPrefix = m_writer.LookupPrefix(Namespaces.XmlSchemaInstance);

            if (xsiPrefix == null)
            {
                m_writer.WriteAttributeString("xmlns", "xsi", null, Namespaces.XmlSchemaInstance);
            }

            uaxPrefix = m_writer.LookupPrefix(Namespaces.OpcUaXsd);

            if (uaxPrefix == null)
            {
                m_writer.WriteAttributeString("xmlns", "uax", null, Namespaces.OpcUaXsd);
            }

            PushNamespace(namespaceUri);
        }

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during encoding.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs referenced by the data being encoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being encoded.</param>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
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
        public void Push(string fieldName, string namespaceUri)
        {
            m_writer.WriteStartElement(fieldName, namespaceUri);
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
        public string CloseAndReturnText()
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
        public void EncodeMessage<T>(T message) where T : IEncodeable
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // write the message.
            WriteEncodeable(typeof(T).Name, message);

            PopNamespace();
        }

        /// <inheritdoc/>
        public void WriteBoolean(string fieldName, bool value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteSByte(string fieldName, sbyte value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteByte(string fieldName, byte value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt16(string fieldName, short value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt16(string fieldName, ushort value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt32(string fieldName, int value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt32(string fieldName, uint value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteInt64(string fieldName, long value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt64(string fieldName, ulong value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(XmlConvert.ToString(value));
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteFloat(string fieldName, float value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDouble(string fieldName, double value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteString(string fieldName, string value)
        {
            WriteString(fieldName, value, false);
        }

        private void WriteString(string fieldName, string value, bool isArrayElement)
        {
            if (BeginField(fieldName, value == null, true, isArrayElement))
            {
                // check the length.
                if (Context.MaxStringLength > 0 && Context.MaxStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    m_writer.WriteString(value);
                }

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            if (BeginField(fieldName, false, false))
            {
                value = CoreUtils.ToOpcUaUniversalTime(value);
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteGuid(string fieldName, Uuid value)
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
        public void WriteByteString(string fieldName, ByteString value)
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
        public void WriteByteString(string fieldName, ReadOnlySpan<byte> value)
        {
            WriteByteString(fieldName, value, false);
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteByteString(string fieldName, ReadOnlySpan<byte> value, bool isArrayElement)
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

        private void WriteByteString(string fieldName, ByteString value, bool isArrayElement)
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
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            WriteXmlElement(fieldName, value, false);
        }

        private void WriteXmlElement(string fieldName, XmlElement value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsEmpty, true, isArrayElement))
            {
                m_writer.WriteRaw(value.OuterXml);
                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteNodeId(string fieldName, NodeId value)
        {
            WriteNodeId(fieldName, value, false);
        }

        private void WriteNodeId(string fieldName, NodeId value, bool isArrayElement)
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
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            WriteExpandedNodeId(fieldName, value, false);
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        private void WriteExpandedNodeId(string fieldName, ExpandedNodeId value, bool isArrayElement)
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
        public void WriteStatusCode(string fieldName, StatusCode value)
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
        public void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value)
        {
            WriteDiagnosticInfo(fieldName, value, 0);
        }

        /// <summary>
        /// Writes a DiagnosticInfo to the stream.
        /// </summary>
        private void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value, int depth)
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
                        m_logger.LogWarning(
                            "InnerDiagnosticInfo dropped because nesting exceeds maximum of {MaxInnerDepth}.",
                            DiagnosticInfo.MaxInnerDepth);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void WriteQualifiedName(string fieldName, QualifiedName value)
        {
            WriteQualifiedName(fieldName, value, false);
        }

        /// <summary>
        /// Writes an QualifiedName to the stream.
        /// </summary>
        private void WriteQualifiedName(string fieldName, QualifiedName value, bool isArrayElement)
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
        public void WriteLocalizedText(string fieldName, LocalizedText value)
        {
            WriteLocalizedText(fieldName, value, false);
        }

        /// <summary>
        /// Writes an LocalizedText to the stream.
        /// </summary>
        private void WriteLocalizedText(string fieldName, LocalizedText value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsNullOrEmpty, true, isArrayElement))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (!value.IsNullOrEmpty)
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
        public void WriteVariant(string fieldName, Variant value)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                if (BeginField(fieldName, false, false))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    m_writer.WriteStartElement("Value", Namespaces.OpcUaXsd);

                    WriteVariantValue(null, value);

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
        public void WriteDataValue(string fieldName, DataValue value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (value != null)
                {
                    WriteVariant("Value", value.WrappedValue);
                    WriteStatusCode("StatusCode", value.StatusCode);
                    WriteDateTime("SourceTimestamp", value.SourceTimestamp);
                    WriteUInt16("SourcePicoseconds", value.SourcePicoseconds);
                    WriteDateTime("ServerTimestamp", value.ServerTimestamp);
                    WriteUInt16("ServerPicoseconds", value.ServerPicoseconds);
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteExtensionObject(string fieldName, ExtensionObject value)
        {
            WriteExtensionObject(fieldName, value, false);
        }

        /// <summary>
        /// Writes an ExtensionObject to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteExtensionObject(string fieldName, ExtensionObject value, bool isArrayElement)
        {
            if (BeginField(fieldName, value.IsNull, true, isArrayElement))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                // write the type id.
                ExpandedNodeId typeId = value.TypeId;

                if (value.TryGetEncodeable(out IEncodeable encodeable))
                {
                    typeId = encodeable.XmlEncodingId;
                }

                var localTypeId = ExpandedNodeId.ToNodeId(typeId, Context.NamespaceUris);

                if (localTypeId.IsNull && !typeId.IsNull)
                {
                    if (encodeable != null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Cannot encode bodies of type '{0}' in ExtensionObject unless the NamespaceUri ({1}) is in the encoder's NamespaceTable.",
                            encodeable.GetType().FullName,
                            typeId.NamespaceUri);
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
        public void WriteEncodeable<T>(string fieldName, T value) where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();

            if (BeginField(fieldName, value == null, true))
            {
                value?.Encode(this);

                EndField(fieldName);
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void WriteEncodeableAsExtensionObject<T>(string fieldName, T value)
            where T : IEncodeable
        {
            WriteExtensionObject(fieldName, new ExtensionObject(value));
        }

        /// <inheritdoc/>
        public void WriteEnumerated<T>(string fieldName, T value) where T : Enum
        {
            if (BeginField(fieldName, value == null, true))
            {
                if (value != null)
                {
                    m_writer.WriteString(CoreUtils.Format("{0}_{1}",
                        value.ToString(),
                        EnumHelper.EnumToInt32(value)));
                }

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string fieldName, ArrayOf<bool> values)
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
        public void WriteSByteArray(string fieldName, ArrayOf<sbyte> values)
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
        public void WriteByteArray(string fieldName, ArrayOf<byte> values)
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
        public void WriteInt16Array(string fieldName, ArrayOf<short> values)
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
        public void WriteUInt16Array(string fieldName, ArrayOf<ushort> values)
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
        public void WriteInt32Array(string fieldName, ArrayOf<int> values)
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
        public void WriteUInt32Array(string fieldName, ArrayOf<uint> values)
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
        public void WriteInt64Array(string fieldName, ArrayOf<long> values)
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
        public void WriteUInt64Array(string fieldName, ArrayOf<ulong> values)
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
        public void WriteFloatArray(string fieldName, ArrayOf<float> values)
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
        public void WriteDoubleArray(string fieldName, ArrayOf<double> values)
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
        public void WriteStringArray(string fieldName, ArrayOf<string> values)
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
        public void WriteDateTimeArray(string fieldName, ArrayOf<DateTime> values)
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
        public void WriteGuidArray(string fieldName, ArrayOf<Uuid> values)
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
        public void WriteByteStringArray(string fieldName, ArrayOf<ByteString> values)
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
        public void WriteXmlElementArray(string fieldName, ArrayOf<XmlElement> values)
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
        public void WriteNodeIdArray(string fieldName, ArrayOf<NodeId> values)
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
        public void WriteExpandedNodeIdArray(string fieldName, ArrayOf<ExpandedNodeId> values)
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
        public void WriteStatusCodeArray(string fieldName, ArrayOf<StatusCode> values)
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
        public void WriteDiagnosticInfoArray(string fieldName, ArrayOf<DiagnosticInfo> values)
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
        public void WriteQualifiedNameArray(string fieldName, ArrayOf<QualifiedName> values)
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
        public void WriteLocalizedTextArray(string fieldName, ArrayOf<LocalizedText> values)
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
        public void WriteVariantArray(string fieldName, ArrayOf<Variant> values)
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
        public void WriteDataValueArray(string fieldName, ArrayOf<DataValue> values)
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
        public void WriteExtensionObjectArray(string fieldName, ArrayOf<ExtensionObject> values)
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
        public void WriteEncodeableArrayAsExtensionObjects<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            WriteExtensionObjectArray(fieldName, values.ConvertAll(v => new ExtensionObject(v)));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string fieldName, ArrayOf<T> values) where T : IEncodeable
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

                // get name for type being encoded.
                XmlQualifiedName xmlName =
                    TypeInfo.GetXmlName(typeof(T))
                    ?? new XmlQualifiedName("IEncodeable", Namespaces.OpcUaXsd);

                PushNamespace(xmlName.Namespace);

                // encode each element in the array.
                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteEncodeable(xmlName.Name, values[ii]);
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string fieldName, ArrayOf<T> values) where T : Enum
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
                foreach (var value in values)
                {
                    WriteEnumerated(xmlName.Name, value);
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string fieldName)
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
        public void WriteVariantValue(string fieldName, Variant value)
        {
            if (fieldName != null && BeginField(fieldName, false, false))
            {
                PushNamespace(Namespaces.OpcUaXsd);
            }
            try
            {
                // check for null.
                if (value.IsNull)
                {
                    m_writer.WriteAttributeString("nil", Namespaces.XmlSchemaInstance, "true");
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
                    else if (value.TypeInfo.IsArray)
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

                        if (BeginField("Matrix", value.IsNull, true, true))
                        {
                            const string elements = "Elements";
                            void WriteDimensions<T>(MatrixOf<T> matrix)
                                => WriteInt32Array("Dimensions", matrix.Dimensions);
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
                                        MatrixOf<DateTime> matrix = value.GetDateTimeMatrix();
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

                            EndField("Matrix");
                        }

                        m_nestingLevel--;
                        return;
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
                    using var reader = XmlReader.Create(
                        new StringReader(xml.OuterXml),
                        CoreUtils.DefaultXmlReaderSettings());
                    m_writer.WriteNode(reader, false);
                }
                else if (extensionObject.TryGetEncodeable(out IEncodeable encodeable))
                {
                    // encode extension object in xml.
                    XmlQualifiedName xmlName = TypeInfo.GetXmlName(encodeable, Context);
                    m_writer.WriteStartElement(xmlName.Name, xmlName.Namespace);
                    encodeable.Encode(this);
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
        /// Writes an Variant array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteObjectArray(string fieldName, ArrayOf<object> values)
        {
            if (BeginField(fieldName, values.IsNull, true, true))
            {
                // check the length.
                if (!values.IsNull &&
                    Context.MaxArrayLength > 0 &&
                    Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (!values.IsNull)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteVariant("Variant", new Variant(values[ii]));
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes the start element for a field.
        /// </summary>
        private bool BeginField(
            string fieldName,
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

                if (isDefault)
                {
                    if (isNillable)
                    {
                        m_writer.WriteAttributeString("nil", Namespaces.XmlSchemaInstance, "true");
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
        private void EndField(string fieldName)
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

        private readonly ILogger m_logger;
        private readonly StringBuilder m_destination;
        private readonly XmlWriter m_writer;
        private readonly Stack<string> m_namespaces = [];
        private XmlQualifiedName m_root;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        private bool m_disposed;
    }
}
