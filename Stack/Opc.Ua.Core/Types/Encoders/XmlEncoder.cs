/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
    /// Writes objects to a XML stream.
    /// </summary>
    public class XmlEncoder : IEncoder, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public XmlEncoder(ServiceMessageContext context)
        {
            Initialize();

            m_destination = new StringBuilder();
            m_context = context;
            m_nestingLevel = 0;

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.CheckCharacters = false;
            settings.ConformanceLevel = ConformanceLevel.Auto;

            m_writer = XmlWriter.Create(m_destination, settings);
        }

        /// <summary>
        /// Initializes the object with a system type to encode and a XML writer.
        /// </summary>
        public XmlEncoder(System.Type systemType, XmlWriter writer, ServiceMessageContext context)
        :
            this(EncodeableFactory.GetXmlName(systemType), writer, context)
        {
        }

        /// <summary>
        /// Initializes the object with a system type to encode and a XML writer.
        /// </summary>
        public XmlEncoder(XmlQualifiedName root, XmlWriter writer, ServiceMessageContext context)
        {
            Initialize();

            if (writer == null)
            {
                m_destination = new StringBuilder();
                m_writer = XmlWriter.Create(m_destination);
            }
            else
            {
                m_destination = null;
                m_writer = writer;
            }

            Initialize(root.Name, root.Namespace);
            m_context = context;
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_destination = null;
            m_writer = null;
            m_namespaces = new Stack<string>();
            m_root = null;
        }

        /// <summary>
        /// Writes the root element to the stream.
        /// </summary>
        private void Initialize(string fieldName, string namespaceUri)
        {
            m_root = new XmlQualifiedName(fieldName, namespaceUri);

            string uaxPrefix = m_writer.LookupPrefix(Namespaces.OpcUaXsd);

            if (uaxPrefix == null)
            {
                uaxPrefix = "uax";
            }

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
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the tables used to map namespace and server uris during encoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being encoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being encoded.</param>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && m_context.NamespaceUris != null)
            {
                m_namespaceMappings = namespaceUris.CreateMapping(m_context.NamespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && m_context.ServerUris != null)
            {
                m_serverMappings = serverUris.CreateMapping(m_context.ServerUris, false);
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

        /// <summary>
        /// Completes writing and returns the XML text.
        /// </summary>
        public string Close()
        {
            if (m_root != null)
            {
                m_writer.WriteEndElement();
            }

            m_writer.Flush();
            m_writer.Dispose();

            if (m_destination != null)
            {
                return m_destination.ToString();
            }

            return null;
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
                if (m_writer != null)
                {
                    m_writer.Flush();
                    m_writer.Dispose();
                    m_writer = null;
                }
            }
        }
        #endregion

        #region IEncoder Members
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Xml;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public ServiceMessageContext Context => m_context;

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
                if (Single.IsNaN(value))
                {
                    m_writer.WriteValue("NaN");
                }
                else if (Single.IsPositiveInfinity(value))
                {
                    m_writer.WriteValue("INF");
                }
                else if (Single.IsNegativeInfinity(value))
                {
                    m_writer.WriteValue("-INF");
                }
                else
                {
                    m_writer.WriteValue(value);
                }

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
            if (BeginField(fieldName, value == null, true))
            {
                // check the length.
                if (m_context.MaxStringLength > 0 && m_context.MaxStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                m_writer.WriteString(value);
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
            if (BeginField(fieldName, value == null, true))
            {
                // check the length.
                if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < value.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                m_writer.WriteValue(Convert.ToBase64String(value));
                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an XmlElement to the stream.
        /// </summary>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            if (BeginField(fieldName, value == null, true))
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

                    StringBuilder buffer = new StringBuilder();
                    NodeId.Format(buffer, value.Identifier, value.IdType, namespaceIndex);
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

                    StringBuilder buffer = new StringBuilder();
                    ExpandedNodeId.Format(buffer, value.Identifier, value.IdType, namespaceIndex, value.NamespaceUri, serverIndex);
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

                if (value != null)
                {
                    WriteUInt32("Code", value.Code);
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an DiagnosticInfo to the stream.
        /// </summary>
        public void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value)
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
                    WriteDiagnosticInfo("InnerDiagnosticInfo", value.InnerDiagnosticInfo);
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
                    WriteString("Locale", value.Locale);
                    WriteString("Text", value.Text);
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
            // check the nesting level for avoiding a stack overflow.
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }

            m_nestingLevel++;

            if (BeginField(fieldName, false, false))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                m_writer.WriteStartElement("Value", Namespaces.OpcUaXsd);
                WriteVariantContents(value.Value, value.TypeInfo);
                m_writer.WriteEndElement();

                PopNamespace();

                EndField(fieldName);
            }

            m_nestingLevel--;
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

                IEncodeable encodeable = value.Body as IEncodeable;

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

                NodeId localTypeId = ExpandedNodeId.ToNodeId(typeId, m_context.NamespaceUris);

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
        public void WriteEncodeable(string fieldName, IEncodeable value, System.Type systemType)
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

            if (BeginField(fieldName, value == null, true))
            {
                if (value != null)
                {
                    value.Encode(this);
                }

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
                    var valueSymbol = value.ToString();
                    var valueInt32 = Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString();
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
        public void WriteBooleanArray(string fieldName, IList<bool> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteSByteArray(string fieldName, IList<sbyte> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        /// Writes a sbyte array to the stream.
        /// </summary>
        public void WriteByteArray(string fieldName, IList<byte> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteInt16Array(string fieldName, IList<short> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteUInt16Array(string fieldName, IList<ushort> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteInt32Array(string fieldName, IList<int> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteUInt32Array(string fieldName, IList<uint> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteInt64Array(string fieldName, IList<long> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteUInt64Array(string fieldName, IList<ulong> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteFloatArray(string fieldName, IList<float> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteDoubleArray(string fieldName, IList<double> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteStringArray(string fieldName, IList<string> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteString("String", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes a UTC date/time array to the stream.
        /// </summary>
        public void WriteDateTimeArray(string fieldName, IList<DateTime> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteGuidArray(string fieldName, IList<Uuid> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteGuidArray(string fieldName, IList<Guid> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteByteStringArray(string fieldName, IList<byte[]> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteByteString("ByteString", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an XmlElement array to the stream.
        /// </summary>
        public void WriteXmlElementArray(string fieldName, IList<XmlElement> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                PushNamespace(Namespaces.OpcUaXsd);

                if (values != null)
                {
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        WriteXmlElement("XmlElement", values[ii]);
                    }
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an NodeId array to the stream.
        /// </summary>
        public void WriteNodeIdArray(string fieldName, IList<NodeId> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteExpandedNodeIdArray(string fieldName, IList<ExpandedNodeId> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteStatusCodeArray(string fieldName, IList<StatusCode> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteDiagnosticInfoArray(string fieldName, IList<DiagnosticInfo> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteQualifiedNameArray(string fieldName, IList<QualifiedName> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteLocalizedTextArray(string fieldName, IList<LocalizedText> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteVariantArray(string fieldName, IList<Variant> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteDataValueArray(string fieldName, IList<DataValue> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteExtensionObjectArray(string fieldName, IList<ExtensionObject> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteEncodeableArray(string fieldName, IList<IEncodeable> values, System.Type systemType)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                // get name for type being encoded.
                XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(systemType);

                if (xmlName == null)
                {
                    xmlName = new XmlQualifiedName("IEncodeable", Namespaces.OpcUaXsd);
                }

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
                                Utils.Format("Objects with type '{0}' are not allowed in the array being serialized.", systemType.FullName));
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
        public void WriteEnumeratedArray(string fieldName, Array values, System.Type systemType)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                // get name for type being encoded.
                XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(systemType);

                if (xmlName == null)
                {
                    xmlName = new XmlQualifiedName("Enumerated", Namespaces.OpcUaXsd);
                }

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
        #endregion

        #region Public Methods
        /// <summary>
        /// Writes the contents of an Variant to the stream.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public void WriteVariantContents(object value, TypeInfo typeInfo)
        {
            // check for null.
            if (value == null)
            {
                m_writer.WriteStartElement("Null", Namespaces.OpcUaXsd);
                m_writer.WriteEndElement();
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
                        case BuiltInType.Boolean: { WriteBoolean("Boolean", (bool)value); return; }
                        case BuiltInType.SByte: { WriteSByte("SByte", (sbyte)value); return; }
                        case BuiltInType.Byte: { WriteByte("Byte", (byte)value); return; }
                        case BuiltInType.Int16: { WriteInt16("Int16", (short)value); return; }
                        case BuiltInType.UInt16: { WriteUInt16("UInt16", (ushort)value); return; }
                        case BuiltInType.Int32: { WriteInt32("Int32", (int)value); return; }
                        case BuiltInType.UInt32: { WriteUInt32("UInt32", (uint)value); return; }
                        case BuiltInType.Int64: { WriteInt64("Int64", (long)value); return; }
                        case BuiltInType.UInt64: { WriteUInt64("UInt64", (ulong)value); return; }
                        case BuiltInType.Float: { WriteFloat("Float", (float)value); return; }
                        case BuiltInType.Double: { WriteDouble("Double", (double)value); return; }
                        case BuiltInType.String: { WriteString("String", (string)value); return; }
                        case BuiltInType.DateTime: { WriteDateTime("DateTime", (DateTime)value); return; }
                        case BuiltInType.Guid: { WriteGuid("Guid", (Uuid)value); return; }
                        case BuiltInType.ByteString: { WriteByteString("ByteString", (byte[])value); return; }
                        case BuiltInType.XmlElement: { WriteXmlElement("XmlElement", (XmlElement)value); return; }
                        case BuiltInType.NodeId: { WriteNodeId("NodeId", (NodeId)value); return; }
                        case BuiltInType.ExpandedNodeId: { WriteExpandedNodeId("ExpandedNodeId", (ExpandedNodeId)value); return; }
                        case BuiltInType.StatusCode: { WriteStatusCode("StatusCode", (StatusCode)value); return; }
                        case BuiltInType.QualifiedName: { WriteQualifiedName("QualifiedName", (QualifiedName)value); return; }
                        case BuiltInType.LocalizedText: { WriteLocalizedText("LocalizedText", (LocalizedText)value); return; }
                        case BuiltInType.ExtensionObject: { WriteExtensionObject("ExtensionObject", (ExtensionObject)value); return; }
                        case BuiltInType.DataValue: { WriteDataValue("DataValue", (DataValue)value); return; }
                        case BuiltInType.Enumeration: { WriteInt32("Int32", (int)value); return; }
                    }
                }

                // write array.
                else if (typeInfo.ValueRank <= 1)
                {
                    switch (typeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean: { WriteBooleanArray("ListOfBoolean", (bool[])value); return; }
                        case BuiltInType.SByte: { WriteSByteArray("ListOfSByte", (sbyte[])value); return; }
                        case BuiltInType.Byte: { WriteByteArray("ListOfByte", (byte[])value); return; }
                        case BuiltInType.Int16: { WriteInt16Array("ListOfInt16", (short[])value); return; }
                        case BuiltInType.UInt16: { WriteUInt16Array("ListOfUInt16", (ushort[])value); return; }
                        case BuiltInType.Int32: { WriteInt32Array("ListOfInt32", (int[])value); return; }
                        case BuiltInType.UInt32: { WriteUInt32Array("ListOfUInt32", (uint[])value); return; }
                        case BuiltInType.Int64: { WriteInt64Array("ListOfInt64", (long[])value); return; }
                        case BuiltInType.UInt64: { WriteUInt64Array("ListOfUInt64", (ulong[])value); return; }
                        case BuiltInType.Float: { WriteFloatArray("ListOfFloat", (float[])value); return; }
                        case BuiltInType.Double: { WriteDoubleArray("ListOfDouble", (double[])value); return; }
                        case BuiltInType.String: { WriteStringArray("ListOfString", (string[])value); return; }
                        case BuiltInType.DateTime: { WriteDateTimeArray("ListOfDateTime", (DateTime[])value); return; }
                        case BuiltInType.Guid: { WriteGuidArray("ListOfGuid", (Uuid[])value); return; }
                        case BuiltInType.ByteString: { WriteByteStringArray("ListOfByteString", (byte[][])value); return; }
                        case BuiltInType.XmlElement: { WriteXmlElementArray("ListOfXmlElement", (XmlElement[])value); return; }
                        case BuiltInType.NodeId: { WriteNodeIdArray("ListOfNodeId", (NodeId[])value); return; }
                        case BuiltInType.ExpandedNodeId: { WriteExpandedNodeIdArray("ListOfExpandedNodeId", (ExpandedNodeId[])value); return; }
                        case BuiltInType.StatusCode: { WriteStatusCodeArray("ListOfStatusCode", (StatusCode[])value); return; }
                        case BuiltInType.QualifiedName: { WriteQualifiedNameArray("ListOfQualifiedName", (QualifiedName[])value); return; }
                        case BuiltInType.LocalizedText: { WriteLocalizedTextArray("ListOfLocalizedText", (LocalizedText[])value); return; }
                        case BuiltInType.ExtensionObject: { WriteExtensionObjectArray("ListOfExtensionObject", (ExtensionObject[])value); return; }
                        case BuiltInType.DataValue: { WriteDataValueArray("ListOfDataValue", (DataValue[])value); return; }

                        case BuiltInType.Enumeration:
                        {
                            int[] ints = value as int[];
                            if (ints == null)
                            {
                                Enum[] enums = value as Enum[];
                                if (enums == null)
                                {
                                    throw new ServiceResultException(
                                        StatusCodes.BadEncodingError,
                                        Utils.Format("Type '{0}' is not allowed in an Enumeration.", value.GetType().FullName));
                                }
                                ints = new int[enums.Length];
                                for (int ii = 0; ii < enums.Length; ii++)
                                {
                                    ints[ii] = (int)(object)enums[ii];
                                }
                            }
                            WriteInt32Array("ListOfInt32", ints);
                            return;
                        }

                        case BuiltInType.Variant:
                        {
                            Variant[] variants = value as Variant[];

                            if (variants != null)
                            {
                                WriteVariantArray("ListOfVariant", variants);
                                return;
                            }

                            object[] objects = value as object[];

                            if (objects != null)
                            {
                                WriteObjectArray("ListOfVariant", objects);
                                return;
                            }

                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingError,
                                "Unexpected type encountered while encoding an array of Variants: {0}",
                                value.GetType());
                        }
                    }
                }

                // write matrix.
                else if (typeInfo.ValueRank > 1)
                {
                    WriteMatrix("Matrix", (Matrix)value);
                    return;
                }

                // oops - should never happen.
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError,
                    Utils.Format("Type '{0}' is not allowed in an Variant.", value.GetType().FullName));
            }
            finally
            {
                PopNamespace();
            }
        }

        /// <summary>
        /// Writes the body of an ExtensionObject to the stream.
        /// </summary>
        public void WriteExtensionObjectBody(object body)
        {
            // nothing to do for null bodies.
            if (body == null)
            {
                return;
            }

            // encode byte body.
            byte[] bytes = body as byte[];

            if (bytes != null)
            {
                m_writer.WriteStartElement("ByteString", Namespaces.OpcUaXsd);
                m_writer.WriteString(Convert.ToBase64String(bytes));
                m_writer.WriteEndElement();
                return;
            }

            // encode xml body.
            XmlElement xml = body as XmlElement;

            if (xml != null)
            {
                XmlReader reader = XmlReader.Create(new StringReader(xml.OuterXml));
                m_writer.WriteNode(reader, false);
                reader.Dispose();
                return;
            }

            IEncodeable encodeable = body as IEncodeable;

            if (encodeable == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError,
                    Utils.Format("Don't know how to encode extension object body with type '{0}'.", body.GetType().FullName));
            }

            // encode extension object in xml.
            XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(encodeable.GetType());
            m_writer.WriteStartElement(xmlName.Name, xmlName.Namespace);
            encodeable.Encode(this);
            m_writer.WriteEndElement();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        private void WriteMatrix(string fieldName, Matrix value)
        {
            if (BeginField(fieldName, value == null, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                if (value != null)
                {
                    m_writer.WriteStartElement("Elements", Namespaces.OpcUaXsd);
                    WriteVariantContents(value.Elements, new TypeInfo(value.TypeInfo.BuiltInType, ValueRanks.OneDimension));
                    m_writer.WriteEndElement();

                    WriteInt32Array("Dimensions", value.Dimensions);
                }

                PopNamespace();

                EndField(fieldName);
            }
        }

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        public void WriteObjectArray(string fieldName, IList<object> values)
        {
            if (BeginField(fieldName, values == null, true))
            {
                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        /// Writes the start element for a field.
        /// </summary>
        private bool BeginField(string fieldName, bool isDefault, bool isNillable)
        {
            // specifying a null field name means the start/end tags should not be written.
            if (!String.IsNullOrEmpty(fieldName))
            {
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
            if (!String.IsNullOrEmpty(fieldName))
            {
                m_writer.WriteEndElement();
            }
        }
        #endregion

        #region Private Fields
        private StringBuilder m_destination;
        private XmlWriter m_writer;
        private Stack<string> m_namespaces;
        private XmlQualifiedName m_root;
        private ServiceMessageContext m_context;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        #endregion
    }
}
