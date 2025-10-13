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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Writes objects to a XML stream.
    /// </summary>
    public class XmlEncoder : IEncoder
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

            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
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
                m_writer = XmlWriter.Create(m_destination, Utils.DefaultXmlWriterSettings());
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
            if (disposing && m_writer != null)
            {
                m_writer.Flush();
                m_writer.Dispose();
                m_writer = null;
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

        /// <summary>
        /// Encodes a message with its header.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public void EncodeMessage(IEncodeable message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // write the message.
            WriteEncodeable(message.GetType().Name, message, message.GetType());

            PopNamespace();
        }

        /// <summary>
        /// Writes a boolean to the stream.
        /// </summary>
        public void WriteBoolean(string fieldName, bool value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a sbyte to the stream.
        /// </summary>
        public void WriteSByte(string fieldName, sbyte value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a byte to the stream.
        /// </summary>
        public void WriteByte(string fieldName, byte value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a short to the stream.
        /// </summary>
        public void WriteInt16(string fieldName, short value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a ushort to the stream.
        /// </summary>
        public void WriteUInt16(string fieldName, ushort value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an int to the stream.
        /// </summary>
        public void WriteInt32(string fieldName, int value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a uint to the stream.
        /// </summary>
        public void WriteUInt32(string fieldName, uint value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a long to the stream.
        /// </summary>
        public void WriteInt64(string fieldName, long value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a ulong to the stream.
        /// </summary>
        public void WriteUInt64(string fieldName, ulong value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(XmlConvert.ToString(value));
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a float to the stream.
        /// </summary>
        public void WriteFloat(string fieldName, float value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a double to the stream.
        /// </summary>
        public void WriteDouble(string fieldName, double value)
        {
            if (BeginField(fieldName, false, false))
            {
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a string to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes a UTC date/time to the stream.
        /// </summary>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            if (BeginField(fieldName, false, false))
            {
                value = Utils.ToOpcUaUniversalTime(value);
                m_writer.WriteValue(value);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Uuid value)
        {
            if (BeginField(fieldName, false, false))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                WriteString("String", value.GuidString);
                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Guid value)
        {
            if (BeginField(fieldName, false, false))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                WriteString("String", value.ToString());
                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        public void WriteByteString(string fieldName, byte[] value)
        {
            WriteByteString(fieldName, value, 0, value?.Length ?? 0, false);
        }

        /// <summary>
        /// Writes a byte string to the stream with a given index and count.
        /// </summary>
        public void WriteByteString(string fieldName, byte[] value, int index, int count)
        {
            WriteByteString(fieldName, value, index, count, false);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteString(string fieldName, ReadOnlySpan<byte> value)
        {
            // == compares memory reference, comparing to empty means we compare to the default
            // If null array is converted to span the span is default
            if (BeginField(fieldName, value == ReadOnlySpan<byte>.Empty, true, false))
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

        private void WriteByteString(
            string fieldName,
            byte[] value,
            int index,
            int count,
            bool isArrayElement)
        {
            Debug.Assert(value == null || value.Length >= count - index);
            if (BeginField(fieldName, value == null, true, isArrayElement))
            {
                // check the length.
                if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                m_writer.WriteValue(
                    Convert.ToBase64String(
                        value,
                        index,
                        count,
                        Base64FormattingOptions.InsertLineBreaks));
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an XmlElement to the stream.
        /// </summary>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            WriteXmlElement(fieldName, value, false);
        }

        private void WriteXmlElement(string fieldName, XmlElement value, bool isArrayElement)
        {
            if (BeginField(fieldName, value == null, true, isArrayElement))
            {
                m_writer.WriteRaw(value.OuterXml);
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an NodeId to the stream.
        /// </summary>
        public void WriteNodeId(string fieldName, NodeId value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (value != null)
                {
                    ushort namespaceIndex = value.NamespaceIndex;

                    if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                    {
                        namespaceIndex = m_namespaceMappings[namespaceIndex];
                    }

                    var buffer = new StringBuilder();
                    NodeId.Format(
                        CultureInfo.InvariantCulture,
                        buffer,
                        value.Identifier,
                        value.IdType,
                        namespaceIndex);
                    WriteString("Identifier", buffer.ToString());
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (value != null)
                {
                    ushort namespaceIndex = value.NamespaceIndex;

                    if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                    {
                        namespaceIndex = m_namespaceMappings[namespaceIndex];
                    }

                    uint serverIndex = value.ServerIndex;

                    if (m_serverMappings != null && m_serverMappings.Length > serverIndex)
                    {
                        serverIndex = m_serverMappings[serverIndex];
                    }

                    var buffer = new StringBuilder();
                    ExpandedNodeId.Format(
                        CultureInfo.InvariantCulture,
                        buffer,
                        value.Identifier,
                        value.IdType,
                        namespaceIndex,
                        value.NamespaceUri,
                        serverIndex);
                    WriteString("Identifier", buffer.ToString());
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an StatusCode to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes an DiagnosticInfo to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes an QualifiedName to the stream.
        /// </summary>
        public void WriteQualifiedName(string fieldName, QualifiedName value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                ushort namespaceIndex = value.NamespaceIndex;

                if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                {
                    namespaceIndex = m_namespaceMappings[namespaceIndex];
                }

                if (value != null)
                {
                    WriteUInt16("NamespaceIndex", namespaceIndex);
                    WriteString("Name", value.Name);
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an LocalizedText to the stream.
        /// </summary>
        public void WriteLocalizedText(string fieldName, LocalizedText value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (value != null)
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

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        public void WriteVariant(string fieldName, Variant value)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                if (BeginField(fieldName, false, false))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    m_writer.WriteStartElement("Value", Namespaces.OpcUaXsd);
                    WriteVariantContents(value.Value, value.TypeInfo);
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

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes an ExtensionObject to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExtensionObject(string fieldName, ExtensionObject value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                // check for null.
                if (value == null)
                {
                    EndField(fieldName);
                    PopNamespace();
                    return;
                }

                var encodeable = value.Body as IEncodeable;

                // write the type id.
                ExpandedNodeId typeId = value.TypeId;

                if (encodeable != null)
                {
                    if (value.Encoding == ExtensionObjectEncoding.Binary)
                    {
                        typeId = encodeable.BinaryEncodingId;
                    }
                    else
                    {
                        typeId = encodeable.XmlEncodingId;
                    }
                }

                var localTypeId = ExpandedNodeId.ToNodeId(typeId, Context.NamespaceUris);

                if (NodeId.IsNull(localTypeId) && !NodeId.IsNull(typeId))
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

                object body = value.Body;

                if (body == null)
                {
                    EndField(fieldName);
                    PopNamespace();
                    return;
                }

                // write the body.
                m_writer.WriteStartElement("Body", Namespaces.OpcUaXsd);

                WriteExtensionObjectBody(body);

                // end of body.
                m_writer.WriteEndElement();

                EndField(fieldName);
                PopNamespace();
            }
        }

        /// <summary>
        /// Writes an encodeable object to the stream.
        /// </summary>
        public void WriteEncodeable(string fieldName, IEncodeable value, Type systemType)
        {
            CheckAndIncrementNestingLevel();

            if (BeginField(fieldName, value == null, true))
            {
                value?.Encode(this);

                EndField(fieldName);
            }

            m_nestingLevel--;
        }

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, Enum value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                if (value != null)
                {
                    string valueSymbol = value.ToString();
                    string valueInt32 = Convert
                        .ToInt32(value, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture);
                    if (valueSymbol != valueInt32)
                    {
                        m_writer.WriteString(Utils.Format("{0}_{1}", valueSymbol, valueInt32));
                    }
                    else
                    {
                        m_writer.WriteString(valueSymbol);
                    }
                }

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a boolean array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteBooleanArray(string fieldName, IList<bool> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a sbyte array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteSByteArray(string fieldName, IList<sbyte> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a byte array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteArray(string fieldName, IList<byte> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a short array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteInt16Array(string fieldName, IList<short> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a ushort array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteUInt16Array(string fieldName, IList<ushort> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a int array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteInt32Array(string fieldName, IList<int> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a uint array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteUInt32Array(string fieldName, IList<uint> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a long array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteInt64Array(string fieldName, IList<long> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a ulong array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteUInt64Array(string fieldName, IList<ulong> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a float array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteFloatArray(string fieldName, IList<float> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a double array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDoubleArray(string fieldName, IList<double> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a string array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteStringArray(string fieldName, IList<string> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a UTC date/time array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDateTimeArray(string fieldName, IList<DateTime> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteGuidArray(string fieldName, IList<Uuid> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteGuidArray(string fieldName, IList<Guid> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes a byte string array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteStringArray(string fieldName, IList<byte[]> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteByteString("ByteString", values[ii], 0, values[ii]?.Length ?? 0, true);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an XmlElement array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteXmlElementArray(string fieldName, IList<XmlElement> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes an NodeId array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteNodeIdArray(string fieldName, IList<NodeId> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteNodeId("NodeId", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an ExpandedNodeId array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExpandedNodeIdArray(string fieldName, IList<ExpandedNodeId> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteExpandedNodeId("ExpandedNodeId", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an StatusCode array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteStatusCodeArray(string fieldName, IList<StatusCode> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes an DiagnosticInfo array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDiagnosticInfoArray(string fieldName, IList<DiagnosticInfo> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes an QualifiedName array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteQualifiedNameArray(string fieldName, IList<QualifiedName> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteQualifiedName("QualifiedName", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an LocalizedText array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteLocalizedTextArray(string fieldName, IList<LocalizedText> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteLocalizedText("LocalizedText", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteVariantArray(string fieldName, IList<Variant> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDataValueArray(string fieldName, IList<DataValue> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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

        /// <summary>
        /// Writes an extension object array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExtensionObjectArray(string fieldName, IList<ExtensionObject> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteExtensionObject("ExtensionObject", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an encodeable object array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteEncodeableArray(
            string fieldName,
            IList<IEncodeable> values,
            Type systemType)
        {
            if (BeginField(fieldName, values == null, true, true))
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
                    TypeInfo.GetXmlName(systemType)
                    ?? new XmlQualifiedName("IEncodeable", Namespaces.OpcUaXsd);

                PushNamespace(xmlName.Namespace);

                // encode each element in the array.
                for (int ii = 0; ii < values.Count; ii++)
                {
                    IEncodeable value = values[ii];

                    if (systemType != null)
                    {
                        if (!systemType.IsInstanceOfType(value))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadEncodingError,
                                Utils.Format(
                                    "Objects with type '{0}' are not allowed in the array being serialized.",
                                    systemType.FullName));
                        }

                        WriteEncodeable(xmlName.Name, value, systemType);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteEnumeratedArray(string fieldName, Array values, Type systemType)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Length)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Enumerated Array length={0}",
                        values.Length);
                }

                // get name for type being encoded.
                XmlQualifiedName xmlName =
                    TypeInfo.GetXmlName(systemType) ??
                    new XmlQualifiedName("Enumerated", Namespaces.OpcUaXsd);

                PushNamespace(xmlName.Namespace);

                if (values != null)
                {
                    // encode each element in the array.
                    foreach (Enum value in values)
                    {
                        WriteEnumerated(xmlName.Name, value);
                    }
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

        /// <summary>
        /// Writes the contents of an Variant to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteVariantContents(object value, TypeInfo typeInfo)
        {
            // check for null.
            if (value == null)
            {
                m_writer.WriteAttributeString("nil", Namespaces.XmlSchemaInstance, "true");
                return;
            }

            try
            {
                PushNamespace(Namespaces.OpcUaXsd);

                // write scalar.
                if (typeInfo.ValueRank < 0)
                {
                    switch (typeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            WriteBoolean("Boolean", (bool)value);
                            return;
                        case BuiltInType.SByte:
                            WriteSByte("SByte", (sbyte)value);
                            return;
                        case BuiltInType.Byte:
                            WriteByte("Byte", (byte)value);
                            return;
                        case BuiltInType.Int16:
                            WriteInt16("Int16", (short)value);
                            return;
                        case BuiltInType.UInt16:
                            WriteUInt16("UInt16", (ushort)value);
                            return;
                        case BuiltInType.Int32:
                            WriteInt32("Int32", (int)value);
                            return;
                        case BuiltInType.UInt32:
                            WriteUInt32("UInt32", (uint)value);
                            return;
                        case BuiltInType.Int64:
                            WriteInt64("Int64", (long)value);
                            return;
                        case BuiltInType.UInt64:
                            WriteUInt64("UInt64", (ulong)value);
                            return;
                        case BuiltInType.Float:
                            WriteFloat("Float", (float)value);
                            return;
                        case BuiltInType.Double:
                            WriteDouble("Double", (double)value);
                            return;
                        case BuiltInType.String:
                            WriteString("String", (string)value);
                            return;
                        case BuiltInType.DateTime:
                            WriteDateTime("DateTime", (DateTime)value);
                            return;
                        case BuiltInType.Guid:
                            WriteGuid("Guid", (Uuid)value);
                            return;
                        case BuiltInType.ByteString:
                            WriteByteString("ByteString", (byte[])value);
                            return;
                        case BuiltInType.XmlElement:
                            WriteXmlElement("XmlElement", (XmlElement)value);
                            return;
                        case BuiltInType.NodeId:
                            WriteNodeId("NodeId", (NodeId)value);
                            return;
                        case BuiltInType.ExpandedNodeId:
                            WriteExpandedNodeId("ExpandedNodeId", (ExpandedNodeId)value);
                            return;
                        case BuiltInType.StatusCode:
                            WriteStatusCode("StatusCode", (StatusCode)value);
                            return;
                        case BuiltInType.QualifiedName:
                            WriteQualifiedName("QualifiedName", (QualifiedName)value);
                            return;
                        case BuiltInType.LocalizedText:
                            WriteLocalizedText("LocalizedText", (LocalizedText)value);
                            return;
                        case BuiltInType.ExtensionObject:
                            WriteExtensionObject("ExtensionObject", (ExtensionObject)value);
                            return;
                        case BuiltInType.DataValue:
                            WriteDataValue("DataValue", (DataValue)value);
                            return;
                        case BuiltInType.Enumeration:
                            WriteInt32("Int32", (int)value);
                            return;
                        case BuiltInType.Null:
                        case BuiltInType.Variant:
                        case BuiltInType.DiagnosticInfo:
                        case BuiltInType.Number:
                        case BuiltInType.Integer:
                        case BuiltInType.UInteger:
                            throw new ServiceResultException(
                                StatusCodes.BadEncodingError,
                                Utils.Format(
                                    "Type '{0}' is not allowed in an Variant.",
                                    value.GetType().FullName));
                        default:
                            throw new ServiceResultException(
                                StatusCodes.BadUnexpectedError,
                                $"Unexpected BuiltInType {typeInfo.BuiltInType}");
                    }
                }
                // write array.
                else if (typeInfo.ValueRank <= 1)
                {
                    switch (typeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            WriteBooleanArray("ListOfBoolean", (bool[])value);
                            return;
                        case BuiltInType.SByte:
                            WriteSByteArray("ListOfSByte", (sbyte[])value);
                            return;
                        case BuiltInType.Byte:
                            WriteByteArray("ListOfByte", (byte[])value);
                            return;
                        case BuiltInType.Int16:
                            WriteInt16Array("ListOfInt16", (short[])value);
                            return;
                        case BuiltInType.UInt16:
                            WriteUInt16Array("ListOfUInt16", (ushort[])value);
                            return;
                        case BuiltInType.Int32:
                            WriteInt32Array("ListOfInt32", (int[])value);
                            return;
                        case BuiltInType.UInt32:
                            WriteUInt32Array("ListOfUInt32", (uint[])value);
                            return;
                        case BuiltInType.Int64:
                            WriteInt64Array("ListOfInt64", (long[])value);
                            return;
                        case BuiltInType.UInt64:
                            WriteUInt64Array("ListOfUInt64", (ulong[])value);
                            return;
                        case BuiltInType.Float:
                            WriteFloatArray("ListOfFloat", (float[])value);
                            return;
                        case BuiltInType.Double:
                            WriteDoubleArray("ListOfDouble", (double[])value);
                            return;
                        case BuiltInType.String:
                            WriteStringArray("ListOfString", (string[])value);
                            return;
                        case BuiltInType.DateTime:
                            WriteDateTimeArray("ListOfDateTime", (DateTime[])value);
                            return;
                        case BuiltInType.Guid:
                            WriteGuidArray("ListOfGuid", (Uuid[])value);
                            return;
                        case BuiltInType.ByteString:
                            WriteByteStringArray("ListOfByteString", (byte[][])value);
                            return;
                        case BuiltInType.XmlElement:
                            WriteXmlElementArray("ListOfXmlElement", (XmlElement[])value);
                            return;
                        case BuiltInType.NodeId:
                            WriteNodeIdArray("ListOfNodeId", (NodeId[])value);
                            return;
                        case BuiltInType.ExpandedNodeId:
                            WriteExpandedNodeIdArray(
                                "ListOfExpandedNodeId",
                                (ExpandedNodeId[])value);
                            return;
                        case BuiltInType.StatusCode:
                            WriteStatusCodeArray("ListOfStatusCode", (StatusCode[])value);
                            return;
                        case BuiltInType.QualifiedName:
                            WriteQualifiedNameArray("ListOfQualifiedName", (QualifiedName[])value);
                            return;
                        case BuiltInType.LocalizedText:
                            WriteLocalizedTextArray("ListOfLocalizedText", (LocalizedText[])value);
                            return;
                        case BuiltInType.ExtensionObject:
                            WriteExtensionObjectArray(
                                "ListOfExtensionObject",
                                (ExtensionObject[])value);
                            return;
                        case BuiltInType.DataValue:
                            WriteDataValueArray("ListOfDataValue", (DataValue[])value);
                            return;
                        case BuiltInType.Enumeration:
                            if (value is not int[] ints)
                            {
                                if (value is not Enum[] enums)
                                {
                                    throw ServiceResultException.Create(
                                        StatusCodes.BadEncodingError,
                                        "Type '{0}' is not allowed in an Enumeration.",
                                        value.GetType().FullName);
                                }
                                ints = new int[enums.Length];
                                for (int ii = 0; ii < enums.Length; ii++)
                                {
                                    ints[ii] = (int)(object)enums[ii];
                                }
                            }

                            WriteInt32Array("ListOfInt32", ints);
                            return;
                        case BuiltInType.Variant:
                            if (value is Variant[] variants)
                            {
                                WriteVariantArray("ListOfVariant", variants);
                                return;
                            }

                            if (value is object[] objects)
                            {
                                WriteObjectArray("ListOfVariant", objects);
                                return;
                            }

                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingError,
                                "Unexpected type encountered while encoding an array of Variants: {0}",
                                value.GetType());
                        case BuiltInType.Null:
                        case BuiltInType.DiagnosticInfo:
                        case BuiltInType.Number:
                        case BuiltInType.Integer:
                        case BuiltInType.UInteger:
                            throw new ServiceResultException(
                                StatusCodes.BadEncodingError,
                                Utils.Format(
                                    "Type '{0}' is not allowed in an Variant.",
                                    value.GetType().FullName));
                        default:
                            throw new ServiceResultException(
                                StatusCodes.BadUnexpectedError,
                                $"Unexpected BuiltInType {typeInfo.BuiltInType}");
                    }
                }
                // write matrix.
                else if (typeInfo.ValueRank > 1)
                {
                    WriteMatrix("Matrix", (Matrix)value);
                    return;
                }
            }
            finally
            {
                PopNamespace();
            }
        }

        /// <summary>
        /// Writes the body of an ExtensionObject to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExtensionObjectBody(object body)
        {
            // nothing to do for null bodies.
            if (body == null)
            {
                return;
            }

            // encode byte body.

            if (body is byte[] bytes)
            {
                m_writer.WriteStartElement("ByteString", Namespaces.OpcUaXsd);
                m_writer.WriteString(
                    Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks));
                m_writer.WriteEndElement();
                return;
            }

            // encode xml body.
            if (body is XmlElement xml)
            {
                using var reader = XmlReader.Create(
                    new StringReader(xml.OuterXml),
                    Utils.DefaultXmlReaderSettings());
                m_writer.WriteNode(reader, false);
                return;
            }

            if (body is not IEncodeable encodeable)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError,
                    Utils.Format(
                        "Don't know how to encode extension object body with type '{0}'.",
                        body.GetType().FullName));
            }

            // encode extension object in xml.
            XmlQualifiedName xmlName = TypeInfo.GetXmlName(encodeable, Context);
            m_writer.WriteStartElement(xmlName.Name, xmlName.Namespace);
            encodeable.Encode(this);
            m_writer.WriteEndElement();
        }

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteObjectArray(string fieldName, IList<object> values)
        {
            if (BeginField(fieldName, values == null, true, true))
            {
                // check the length.
                if (values != null &&
                    Context.MaxArrayLength > 0 &&
                    Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
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
        /// Encode an array according to its valueRank and BuiltInType
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteArray(
            string fieldName,
            object array,
            int valueRank,
            BuiltInType builtInType)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                // write array.
                if (valueRank == ValueRanks.OneDimension)
                {
                    /*One dimensional Array parameters are always encoded by wrapping the elements in a container element
                    * and inserting the container into the structure. The name of the container element should be the name of the parameter.
                    * The name of the element in the array shall be the type name.*/
                    switch (builtInType)
                    {
                        case BuiltInType.Boolean:
                            WriteBooleanArray(fieldName, (bool[])array);
                            return;
                        case BuiltInType.SByte:
                            WriteSByteArray(fieldName, (sbyte[])array);
                            return;
                        case BuiltInType.Byte:
                            WriteByteArray(fieldName, (byte[])array);
                            return;
                        case BuiltInType.Int16:
                            WriteInt16Array(fieldName, (short[])array);
                            return;
                        case BuiltInType.UInt16:
                            WriteUInt16Array(fieldName, (ushort[])array);
                            return;
                        case BuiltInType.Int32:
                            WriteInt32Array(fieldName, (int[])array);
                            return;
                        case BuiltInType.UInt32:
                            WriteUInt32Array(fieldName, (uint[])array);
                            return;
                        case BuiltInType.Int64:
                            WriteInt64Array(fieldName, (long[])array);
                            return;
                        case BuiltInType.UInt64:
                            WriteUInt64Array(fieldName, (ulong[])array);
                            return;
                        case BuiltInType.Float:
                            WriteFloatArray(fieldName, (float[])array);
                            return;
                        case BuiltInType.Double:
                            WriteDoubleArray(fieldName, (double[])array);
                            return;
                        case BuiltInType.String:
                            WriteStringArray(fieldName, (string[])array);
                            return;
                        case BuiltInType.DateTime:
                            WriteDateTimeArray(fieldName, (DateTime[])array);
                            return;
                        case BuiltInType.Guid:
                            WriteGuidArray(fieldName, (Uuid[])array);
                            return;
                        case BuiltInType.ByteString:
                            WriteByteStringArray(fieldName, (byte[][])array);
                            return;
                        case BuiltInType.XmlElement:
                            WriteXmlElementArray(fieldName, (XmlElement[])array);
                            return;
                        case BuiltInType.NodeId:
                            WriteNodeIdArray(fieldName, (NodeId[])array);
                            return;
                        case BuiltInType.ExpandedNodeId:
                            WriteExpandedNodeIdArray(fieldName, (ExpandedNodeId[])array);
                            return;
                        case BuiltInType.StatusCode:
                            WriteStatusCodeArray(fieldName, (StatusCode[])array);
                            return;
                        case BuiltInType.QualifiedName:
                            WriteQualifiedNameArray(fieldName, (QualifiedName[])array);
                            return;
                        case BuiltInType.LocalizedText:
                            WriteLocalizedTextArray(fieldName, (LocalizedText[])array);
                            return;
                        case BuiltInType.ExtensionObject:
                            WriteExtensionObjectArray(fieldName, (ExtensionObject[])array);
                            return;
                        case BuiltInType.DataValue:
                            WriteDataValueArray(fieldName, (DataValue[])array);
                            return;
                        case BuiltInType.DiagnosticInfo:
                            WriteDiagnosticInfoArray(fieldName, (DiagnosticInfo[])array);
                            return;
                        case BuiltInType.Enumeration:
                            if (array is not int[] ints)
                            {
                                if (array is not Enum[] enums)
                                {
                                    throw new ServiceResultException(
                                        StatusCodes.BadEncodingError,
                                        Utils.Format(
                                            "Type '{0}' is not allowed in an Enumeration.",
                                            array.GetType().FullName));
                                }
                                ints = new int[enums.Length];
                                for (int ii = 0; ii < enums.Length; ii++)
                                {
                                    ints[ii] = Convert.ToInt32(
                                        enums[ii],
                                        CultureInfo.InvariantCulture);
                                }
                            }

                            WriteInt32Array(fieldName, ints);
                            return;
                        case BuiltInType.Variant:
                        {
                            if (array is Variant[] variants)
                            {
                                WriteVariantArray(fieldName, variants);
                                return;
                            }

                            // try to write IEncodeable Array
                            if (array is IEncodeable[] encodeableArray)
                            {
                                WriteEncodeableArray(
                                    fieldName,
                                    encodeableArray,
                                    array.GetType().GetElementType());
                                return;
                            }

                            if (array is object[] objects)
                            {
                                WriteObjectArray(fieldName, objects);
                                return;
                            }

                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingError,
                                "Unexpected type encountered while encoding an array of Variants: {0}",
                                array.GetType());
                        }
                        case BuiltInType.Null:
                        case BuiltInType.Number:
                        case BuiltInType.Integer:
                        case BuiltInType.UInteger:
                        {
                            // try to write IEncodeable Array
                            if (array is IEncodeable[] encodeableArray)
                            {
                                WriteEncodeableArray(
                                    fieldName,
                                    encodeableArray,
                                    array.GetType().GetElementType());
                                return;
                            }

                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingError,
                                "Unexpected BuiltInType encountered while encoding an array: {0}",
                                builtInType);
                        }
                        default:
                            throw new ServiceResultException(
                               StatusCodes.BadUnexpectedError,
                               $"Unexpected BuiltInType {builtInType}");
                    }
                }
                // write matrix.
                else if (valueRank > ValueRanks.OneDimension)
                {
                    /* Multi-dimensional Arrays are encoded as an Int32 Array containing the dimensions followed by
                     * a list of all the values in the Array. The total number of values is equal to the
                     * product of the dimensions.
                     * The number of values is 0 if one or more dimension is less than or equal to 0.*/

                    if (array is not Matrix matrix)
                    {
                        if (array is Array multiArray && multiArray.Rank == valueRank)
                        {
                            matrix = new Matrix(multiArray, builtInType);
                        }
                        else
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingError,
                                "Unexpected array type encountered while encoding array: {0}",
                                array.GetType().Name);
                        }
                    }

                    if (BeginField(fieldName, matrix == null, true, true))
                    {
                        PushNamespace(Namespaces.OpcUaXsd);

                        if (matrix != null)
                        {
                            // dimensions element is written first
                            WriteInt32Array("Dimensions", matrix.Dimensions);

                            WriteArray(
                                "Elements",
                                matrix.Elements,
                                ValueRanks.OneDimension,
                                builtInType);
                        }

                        PopNamespace();

                        EndField(fieldName);
                    }
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Writes a DataValue array to the stream.
        /// </summary>
        private void WriteMatrix(string fieldName, Matrix value)
        {
            CheckAndIncrementNestingLevel();

            if (BeginField(fieldName, value == null, true, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (value != null)
                {
                    WriteInt32Array("Dimensions", value.Dimensions);

                    WriteArray(
                        "Elements",
                        value.Elements,
                        ValueRanks.OneDimension,
                        value.TypeInfo.BuiltInType);
                }

                PopNamespace();

                EndField(fieldName);
            }

            m_nestingLevel--;
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
        private XmlWriter m_writer;
        private readonly Stack<string> m_namespaces = [];
        private XmlQualifiedName m_root;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
    }
}
