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
    /// Writes objects to a JSON stream.
    /// </summary>
    public class JsonEncoder : IEncoder, IDisposable
    {
        #region Private Fields
        private MemoryStream m_destination;
        private StreamWriter m_writer;
        private Stack<string> m_namespaces;
        private bool m_commaRequired;
        private ServiceMessageContext m_context;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        private bool m_topLevelIsArray;
        private bool m_levelOneSkipped;
        private bool m_dontWriteClosing;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public JsonEncoder(
            ServiceMessageContext context,
            bool useReversibleEncoding,
            StreamWriter writer = null,
            bool topLevelIsArray = false)
        {
            Initialize();

            m_context = context;
            m_nestingLevel = 0;
            m_writer = writer;
            UseReversibleEncoding = useReversibleEncoding;
            m_topLevelIsArray = topLevelIsArray;
            m_levelOneSkipped = false;

            // defaults for JSON encoding
            // -- encode namespace index for reversible encoding
            // -- do not include default values for built in types
            //    which are not a Number or a bool
            // -- include default values for numbers and bool
            ForceNamespaceUri = false;
            IncludeDefaultValues = false;
            IncludeDefaultNumberValues = true;

            if (m_writer == null)
            {
                m_destination = new MemoryStream();
                m_writer = new StreamWriter(m_destination, new UTF8Encoding(false));
            }

            if (m_topLevelIsArray)
            {
                m_writer.Write("[");
            }
            else
            {
                m_writer.Write("{");
            }
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_destination = null;
            m_writer = null;
            m_namespaces = new Stack<string>();
            m_commaRequired = false;
        }

        /// <summary>
        /// Writes the root element to the stream.
        /// </summary>
        private void Initialize(string fieldName, string namespaceUri)
        {
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Encodes a session-less message to a buffer.
        /// </summary>
        public static void EncodeSessionLessMessage(IEncodeable message, Stream stream, ServiceMessageContext context, bool leaveOpen = false)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (context == null) throw new ArgumentNullException(nameof(context));

            // create encoder.
            JsonEncoder encoder = new JsonEncoder(context, true, new StreamWriter(stream, new UTF8Encoding(false), 65535, leaveOpen));

            try
            {
                long start = stream.Position;

                // write the message.
                var envelope = new SessionLessServiceMessage {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    Message = message
                };

                envelope.Encode(encoder);

                // check that the max message size was not exceeded.
                if (context.MaxMessageSize > 0 && context.MaxMessageSize < (int)(stream.Position - start))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "MaxMessageSize {0} < {1}",
                        context.MaxMessageSize,
                        (int)(stream.Position - start));
                }

                encoder.Close();
            }
            finally
            {
                if (leaveOpen)
                {
                    encoder.m_writer.Flush();
                    stream.Position = 0;
                }
            }
        }

        /// <summary>
        /// Encodes a message in a stream.
        /// </summary>
        public static ArraySegment<byte> EncodeMessage(IEncodeable message, byte[] buffer, ServiceMessageContext context)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (context == null) throw new ArgumentNullException(nameof(context));

            using (MemoryStream stream = new MemoryStream(buffer, true))
            {
                var encoder = new JsonEncoder(context, true, new StreamWriter(stream, new UTF8Encoding(false), 65535, false));

                // encode message
                encoder.EncodeMessage(message);
                int length = encoder.Close();

                return new ArraySegment<byte>(buffer, 0, length);
            }
        }

        /// <summary>
        /// Encodes a message with its header.
        /// </summary>
        public void EncodeMessage(IEncodeable message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            // convert the namespace uri to an index.
            NodeId typeId = ExpandedNodeId.ToNodeId(message.TypeId, m_context.NamespaceUris);

            // write the type id.
            WriteNodeId("TypeId", typeId);

            // write the message.
            WriteEncodeable("Body", message, message.GetType());
        }

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
        /// Completes writing and returns the JSON text.
        /// </summary>
        public string CloseAndReturnText()
        {
            Close();
            if (m_destination == null)
            {
                return String.Empty;
            }
            return Encoding.UTF8.GetString(m_destination.ToArray());
        }

        /// <summary>
        /// Completes writing and returns the text length.
        /// </summary>
        public int Close()
        {
            if (!m_dontWriteClosing)
            {
                if (m_topLevelIsArray)
                {
                    m_writer.Write("]");
                }
                else
                {
                    m_writer.Write("}");
                }
            }

            m_writer.Flush();
            int length = (int)m_writer.BaseStream.Position;
            m_writer.Dispose();
            m_writer = null;
            return length;
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
                    Close();
                }
            }
        }
        #endregion

        #region IEncoder Members
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Json;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public ServiceMessageContext Context => m_context;

        /// <summary>
        /// The Json encoder reversible encoding option
        /// </summary>
        public bool UseReversibleEncoding { get; private set; }

        /// <summary>
        /// The Json encoder to encoder namespace URI instead of
        /// namespace Index in NodeIds.
        /// </summary>
        public bool ForceNamespaceUri { get; set; }

        /// <summary>
        /// The Json encoder default value option.
        /// </summary>
        public bool IncludeDefaultValues { get; set; }

        /// <summary>
        /// The Json encoder default value option.
        /// </summary>
        public bool IncludeDefaultNumberValues { get; set; }

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
        /// Push the begin of a structure on the decoder stack.
        /// </summary>
        /// <param name="fieldName">The name of the structure field.</param>
        public void PushStructure(string fieldName)
        {
            m_nestingLevel++;

            if (m_commaRequired)
            {
                m_writer.Write(",");
            }

            if (!String.IsNullOrEmpty(fieldName))
            {
                m_writer.Write("\"");
                EscapeString(fieldName);
                m_writer.Write("\":");
            }
            else if (!m_commaRequired)
            {
                if (m_nestingLevel == 1 && !m_topLevelIsArray)
                {
                    m_levelOneSkipped = true;
                    return;
                }
            }

            m_commaRequired = false;
            m_writer.Write("{");
        }

        /// <summary>
        /// Push the begin of an array on the decoder stack.
        /// </summary>
        /// <param name="fieldName">The name of the array field.</param>
        public void PushArray(string fieldName)
        {
            m_nestingLevel++;

            if (m_commaRequired)
            {
                m_writer.Write(",");
            }

            if (!String.IsNullOrEmpty(fieldName))
            {
                m_writer.Write("\"");
                EscapeString(fieldName);
                m_writer.Write("\":");
            }
            else if (!m_commaRequired)
            {
                if (m_nestingLevel == 1 && !m_topLevelIsArray)
                {
                    m_levelOneSkipped = true;
                    return;
                }
            }

            m_commaRequired = false;
            m_writer.Write("[");
        }

        /// <summary>
        /// Pop the structure from the decoder stack.
        /// </summary>
        public void PopStructure()
        {
            if (m_nestingLevel > 1 || m_topLevelIsArray ||
               (m_nestingLevel == 1 && !m_levelOneSkipped))
            {
                m_writer.Write("}");
                m_commaRequired = true;
            }

            m_nestingLevel--;
        }

        /// <summary>
        /// Pop the array from the decoder stack.
        /// </summary>
        public void PopArray()
        {
            if (m_nestingLevel > 1 || m_topLevelIsArray ||
               (m_nestingLevel == 1 && !m_levelOneSkipped))
            {
                m_writer.Write("]");
                m_commaRequired = true;
            }

            m_nestingLevel--;
        }

        private readonly char[] m_specialChars = new char[] { '"', '\\', '\n', '\r', '\t', '\b', '\f', };
        private readonly char[] m_substitution = new char[] { '"', '\\', 'n', 'r', 't', 'b', 'f' };

        private void EscapeString(string value)
        {
            foreach (char ch in value)
            {
                bool found = false;

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        m_writer.Write('\\');
                        m_writer.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (ch < 32)
                    {
                        m_writer.Write("\\u");
                        m_writer.Write("{0:X4}", (int)ch);
                        continue;
                    }

                    m_writer.Write(ch);
                }
            }
        }

        private void WriteSimpleField(string fieldName, string value, bool quotes)
        {
            if (!String.IsNullOrEmpty(fieldName))
            {
                if (value == null)
                {
                    return;
                }

                if (m_commaRequired)
                {
                    m_writer.Write(",");
                }

                m_writer.Write("\"");
                EscapeString(fieldName);
                m_writer.Write("\":");
            }
            else
            {
                if (m_commaRequired)
                {
                    m_writer.Write(",");
                }
            }

            if (value != null)
            {
                if (quotes)
                {
                    m_writer.Write("\"");
                    EscapeString(value);
                    m_writer.Write("\"");
                }
                else
                {
                    m_writer.Write(value);
                }
            }
            else
            {
                m_writer.Write("null");
            }

            m_commaRequired = true;
        }

        /// <summary>
        /// Writes a boolean to the stream.
        /// </summary>
        public void WriteBoolean(string fieldName, bool value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && !value)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (value)
            {
                WriteSimpleField(fieldName, "true", false);
            }
            else
            {
                WriteSimpleField(fieldName, "false", false);
            }
        }

        /// <summary>
        /// Writes a sbyte to the stream.
        /// </summary>
        public void WriteSByte(string fieldName, sbyte value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), false);
        }

        /// <summary>
        /// Writes a byte to the stream.
        /// </summary>
        public void WriteByte(string fieldName, byte value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), false);
        }

        /// <summary>
        /// Writes a short to the stream.
        /// </summary>
        public void WriteInt16(string fieldName, short value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), false);
        }

        /// <summary>
        /// Writes a ushort to the stream.
        /// </summary>
        public void WriteUInt16(string fieldName, ushort value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), false);
        }

        /// <summary>
        /// Writes an int to the stream.
        /// </summary>
        public void WriteInt32(string fieldName, int value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), false);
        }

        /// <summary>
        /// Writes a uint to the stream.
        /// </summary>
        public void WriteUInt32(string fieldName, uint value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), false);
        }

        /// <summary>
        /// Writes a long to the stream.
        /// </summary>
        public void WriteInt64(string fieldName, long value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), true);
        }

        /// <summary>
        /// Writes a ulong to the stream.
        /// </summary>
        public void WriteUInt64(string fieldName, ulong value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), true);
        }

        /// <summary>
        /// Writes a float to the stream.
        /// </summary>
        public void WriteFloat(string fieldName, float value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && (value > -Single.Epsilon) && (value < Single.Epsilon))
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (Single.IsNaN(value))
            {
                WriteSimpleField(fieldName, "NaN", true);
            }
            else if (Single.IsPositiveInfinity(value))
            {
                WriteSimpleField(fieldName, "Infinity", true);
            }
            else if (Single.IsNegativeInfinity(value))
            {
                WriteSimpleField(fieldName, "-Infinity", true);
            }
            else
            {
                WriteSimpleField(fieldName, value.ToString("R", CultureInfo.InvariantCulture), false);
            }
        }

        /// <summary>
        /// Writes a double to the stream.
        /// </summary>
        public void WriteDouble(string fieldName, double value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && (value > -Double.Epsilon) && (value < Double.Epsilon))
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (Double.IsNaN(value))
            {
                WriteSimpleField(fieldName, "NaN", true);
            }
            else if (Double.IsPositiveInfinity(value))
            {
                WriteSimpleField(fieldName, "Infinity", true);
            }
            else if (Double.IsNegativeInfinity(value))
            {
                WriteSimpleField(fieldName, "-Infinity", true);
            }
            else
            {
                WriteSimpleField(fieldName, value.ToString("R", CultureInfo.InvariantCulture), false);
            }
        }

        /// <summary>
        /// Writes a string to the stream.
        /// </summary>
        public void WriteString(string fieldName, string value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value, true);
        }

        /// <summary>
        /// Writes a UTC date/time to the stream.
        /// </summary>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == DateTime.MinValue)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (value <= DateTime.MinValue)
            {
                WriteSimpleField(fieldName, "0001-01-01T00:00:00Z", true);
            }
            else if (value >= DateTime.MaxValue)
            {
                WriteSimpleField(fieldName, "9999-12-31T23:59:59Z", true);
            }
            else
            {
                WriteSimpleField(fieldName, value.ToUniversalTime()
                    .ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture), true);
            }
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Uuid value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == Uuid.Empty)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(), true);
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Guid value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == Guid.Empty)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(), true);
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        public void WriteByteString(string fieldName, byte[] value)
        {
            if (value == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            // check the length.
            if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < value.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            WriteSimpleField(fieldName, Convert.ToBase64String(value), true);
        }

        /// <summary>
        /// Writes an XmlElement to the stream.
        /// </summary>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            if (value == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            var xml = value.OuterXml;
            var bytes = Encoding.UTF8.GetBytes(xml);

            WriteSimpleField(fieldName, Convert.ToBase64String(bytes), true);
        }

        private void WriteNamespaceIndex(string fieldName, ushort namespaceIndex)
        {
            if (namespaceIndex == 0)
            {
                return;
            }

            if (!UseReversibleEncoding && namespaceIndex > 1)
            {
                var uri = m_context.NamespaceUris.GetString(namespaceIndex);
                if (!String.IsNullOrEmpty(uri))
                {
                    WriteSimpleField(fieldName, uri, true);
                    return;
                }
            }

            if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
            {
                namespaceIndex = m_namespaceMappings[namespaceIndex];
            }

            if (namespaceIndex != 0)
            {
                WriteUInt16(fieldName, namespaceIndex);
            }
        }

        private void WriteNodeIdContents(NodeId value, string namespaceUri = null)
        {
            if (value.IdType > IdType.Numeric)
            {
                WriteInt32("IdType", (int)value.IdType);
            }

            switch (value.IdType)
            {
                case IdType.Numeric:
                {
                    WriteUInt32("Id", (uint)value.Identifier);
                    break;
                }

                case IdType.String:
                {
                    WriteString("Id", (string)value.Identifier);
                    break;
                }

                case IdType.Guid:
                {
                    WriteGuid("Id", (Guid)value.Identifier);
                    break;
                }

                case IdType.Opaque:
                {
                    WriteByteString("Id", (byte[])value.Identifier);
                    break;
                }
            }

            if (namespaceUri != null)
            {
                WriteString("Namespace", namespaceUri);
            }
            else
            {
                WriteNamespaceIndex("Namespace", value.NamespaceIndex);
            }
        }


        /// <summary>
        /// Writes an NodeId to the stream.
        /// </summary>
        public void WriteNodeId(string fieldName, NodeId value)
        {
            if (value == null ||
                (NodeId.IsNull(value) && (value.IdType == IdType.Numeric)))
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushStructure(fieldName);

            ushort namespaceIndex = value.NamespaceIndex;
            if (ForceNamespaceUri && namespaceIndex > 1)
            {
                string namespaceUri = Context.NamespaceUris.GetString(namespaceIndex);
                WriteNodeIdContents(value, namespaceUri);
            }
            else
            {
                WriteNodeIdContents(value);
            }
            PopStructure();
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            if (value == null || value.InnerNodeId == null ||
                (!UseReversibleEncoding && NodeId.IsNull(value)))
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushStructure(fieldName);

            string namespaceUri = value.NamespaceUri;
            ushort namespaceIndex = value.InnerNodeId.NamespaceIndex;
            if (ForceNamespaceUri && namespaceUri == null && namespaceIndex > 1)
            {
                namespaceUri = Context.NamespaceUris.GetString(namespaceIndex);
            }
            WriteNodeIdContents(value.InnerNodeId, namespaceUri);

            uint serverIndex = value.ServerIndex;

            if (serverIndex >= 1)
            {
                var uri = m_context.ServerUris.GetString(serverIndex);

                if (!String.IsNullOrEmpty(uri))
                {
                    WriteSimpleField("ServerUri", uri, true);
                    PopStructure();
                    return;
                }

                if (m_serverMappings != null && m_serverMappings.Length > serverIndex)
                {
                    serverIndex = m_serverMappings[serverIndex];
                }

                if (serverIndex != 0)
                {
                    WriteUInt32("ServerUri", serverIndex);
                }
            }

            PopStructure();
        }


        /// <summary>
        /// Writes an StatusCode to the stream.
        /// </summary>
        public void WriteStatusCode(string fieldName, StatusCode value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == StatusCodes.Good)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (UseReversibleEncoding)
            {
                WriteUInt32(fieldName, value.Code);
                return;
            }

            if (value != StatusCodes.Good)
            {
                PushStructure(fieldName);
                WriteSimpleField("Code", value.Code.ToString(CultureInfo.InvariantCulture), false);
                WriteSimpleField("Symbol", StatusCode.LookupSymbolicId(value.CodeBits), true);
                PopStructure();
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

            if (value == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            m_nestingLevel++;

            PushStructure(fieldName);

            if (value.SymbolicId >= 0)
            {
                WriteSimpleField("SymbolicId", value.SymbolicId.ToString(CultureInfo.InvariantCulture), false);
            }

            if (value.NamespaceUri >= 0)
            {
                WriteSimpleField("NamespaceUri", value.NamespaceUri.ToString(CultureInfo.InvariantCulture), false);
            }

            if (value.Locale >= 0)
            {
                WriteSimpleField("Locale", value.Locale.ToString(CultureInfo.InvariantCulture), false);
            }

            if (value.LocalizedText >= 0)
            {
                WriteSimpleField("LocalizedText", value.LocalizedText.ToString(CultureInfo.InvariantCulture), false);
            }

            if (value.AdditionalInfo != null)
            {
                WriteSimpleField("AdditionalInfo", value.AdditionalInfo, true);
            }

            if (value.InnerStatusCode != StatusCodes.Good)
            {
                WriteStatusCode("InnerStatusCode", value.InnerStatusCode);
            }

            if (value.InnerDiagnosticInfo != null)
            {
                WriteDiagnosticInfo("InnerDiagnosticInfo", value.InnerDiagnosticInfo);
            }

            PopStructure();

            m_nestingLevel--;
        }

        /// <summary>
        /// Writes an QualifiedName to the stream.
        /// </summary>
        public void WriteQualifiedName(string fieldName, QualifiedName value)
        {
            if (QualifiedName.IsNull(value))
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushStructure(fieldName);

            WriteString("Name", value.Name);
            WriteNamespaceIndex("Uri", value.NamespaceIndex);

            PopStructure();
        }

        /// <summary>
        /// Writes an LocalizedText to the stream.
        /// </summary>
        public void WriteLocalizedText(string fieldName, LocalizedText value)
        {
            if (LocalizedText.IsNullOrEmpty(value))
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (UseReversibleEncoding)
            {
                PushStructure(fieldName);

                WriteSimpleField("Text", value.Text, true);

                if (!String.IsNullOrEmpty(value.Locale))
                {
                    WriteSimpleField("Locale", value.Locale, true);
                }

                PopStructure();
            }
            else
            {
                WriteSimpleField(fieldName, value.Text, true);
            }
        }

        /// <summary>
        /// Writes an Variant to the stream.
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

            if (Variant.Null == value)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            m_nestingLevel++;

            bool isNull = (value.TypeInfo == null || value.TypeInfo.BuiltInType == BuiltInType.Null || value.Value == null);

            if (UseReversibleEncoding && !isNull)
            {
                PushStructure(fieldName);
                // encode enums as int32.
                byte encodingByte = (byte)value.TypeInfo.BuiltInType;
                if (value.TypeInfo.BuiltInType == BuiltInType.Enumeration)
                {
                    encodingByte = (byte)BuiltInType.Int32;
                }

                WriteByte("Type", encodingByte);
                fieldName = "Body";
            }

            if (m_commaRequired)
            {
                m_writer.Write(",");
            }

            if (!String.IsNullOrEmpty(fieldName))
            {
                m_writer.Write("\"");
                EscapeString(fieldName);
                m_writer.Write("\":");
            }

            WriteVariantContents(value.Value, value.TypeInfo);

            if (UseReversibleEncoding && !isNull)
            {
                Matrix matrix = value.Value as Matrix;

                if (matrix != null)
                {
                    WriteInt32Array("Dimensions", matrix.Dimensions);
                }

                PopStructure();
            }

            m_nestingLevel--;
        }

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        public void WriteDataValue(string fieldName, DataValue value, bool useReversibleEncoding)
        {
            bool currentValue = UseReversibleEncoding;

            try
            {
                UseReversibleEncoding = useReversibleEncoding;
                WriteDataValue(fieldName, value);
            }
            finally
            {
                UseReversibleEncoding = currentValue;
            }
        }

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        public void WriteDataValue(string fieldName, DataValue value)
        {
            if (value == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushStructure(fieldName);

            if (value != null)
            {
                if (value.WrappedValue.TypeInfo != null && value.WrappedValue.TypeInfo.BuiltInType != BuiltInType.Null)
                {
                    WriteVariant("Value", value.WrappedValue);
                }

                if (value.StatusCode != StatusCodes.Good)
                {
                    WriteStatusCode("StatusCode", value.StatusCode);
                }

                if (value.SourceTimestamp != DateTime.MinValue)
                {
                    WriteDateTime("SourceTimestamp", value.SourceTimestamp);

                    if (value.SourcePicoseconds != 0)
                    {
                        WriteUInt16("SourcePicoseconds", value.SourcePicoseconds);
                    }
                }

                if (value.ServerTimestamp != DateTime.MinValue)
                {
                    WriteDateTime("ServerTimestamp", value.ServerTimestamp);

                    if (value.ServerPicoseconds != 0)
                    {
                        WriteUInt16("ServerPicoseconds", value.ServerPicoseconds);
                    }
                }
            }

            PopStructure();
        }

        /// <summary>
        /// Writes an ExtensionObject to the stream.
        /// </summary>
        public void WriteExtensionObject(string fieldName, ExtensionObject value)
        {
            if (value == null || value.Encoding == ExtensionObjectEncoding.None)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            var encodeable = value.Body as IEncodeable;
            if (!UseReversibleEncoding && encodeable != null)
            {
                var structureType = value.Body as IStructureTypeInfo;
                if (structureType != null &&
                    structureType.StructureType == StructureType.Union)
                {
                    encodeable.Encode(this);
                    return;
                }

                PushStructure(fieldName);
                encodeable.Encode(this);
                PopStructure();
                return;
            }

            PushStructure(fieldName);

            if (UseReversibleEncoding)
            {
                var nodeId = ExpandedNodeId.ToNodeId(value.TypeId, Context.NamespaceUris);
                WriteNodeId("TypeId", nodeId);
            }
            else
            {
                WriteExpandedNodeId("TypeId", value.TypeId);
            }

            if (encodeable != null)
            {
                WriteEncodeable("Body", encodeable, null);
            }
            else
            {
                if (value.Body != null)
                {
                    if (value.Encoding == ExtensionObjectEncoding.Json)
                    {
                        WriteSimpleField("Body", value.Body as string, true);
                    }
                    else
                    {
                        WriteByte("Encoding", (byte)value.Encoding);
                        if (value.Encoding == ExtensionObjectEncoding.Binary)
                        {
                            WriteByteString("Body", value.Body as byte[]);
                        }
                        else if (value.Encoding == ExtensionObjectEncoding.Xml)
                        {
                            WriteXmlElement("Body", value.Body as XmlElement);
                        }
                    }
                }
            }

            PopStructure();
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

            if (value == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (m_nestingLevel == 0 && (m_commaRequired || m_topLevelIsArray))
            {
                if (string.IsNullOrWhiteSpace(fieldName) ^ m_topLevelIsArray)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingError,
                        "With Array as top level, encodeables with fieldname will create invalid json");
                }
            }

            if (m_nestingLevel == 0 && !m_commaRequired)
            {
                if (string.IsNullOrWhiteSpace(fieldName) && !m_topLevelIsArray)
                {
                    m_writer.Flush();
                    if (m_writer.BaseStream.Length == 1) //Opening "{"
                    {
                        m_writer.BaseStream.Seek(0, SeekOrigin.Begin);
                    }
                    m_dontWriteClosing = true;
                }
            }

            m_nestingLevel++;

            PushStructure(fieldName);

            value?.Encode(this);

            PopStructure();

            m_nestingLevel--;

        }

        /// <summary>
        /// Writes an enumerated value to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, Enum value)
        {
            int numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            var numericString = numeric.ToString();
            if (UseReversibleEncoding)
            {
                WriteSimpleField(fieldName, numericString, false);
            }
            else
            {
                var valueString = value.ToString();
                if (valueString == numericString)
                {
                    WriteSimpleField(fieldName, numericString, true);
                }
                else
                {
                    WriteSimpleField(fieldName, Utils.Format("{0}_{1}", value.ToString(), numeric), true);
                }
            }
        }

        /// <summary>
        /// Writes an enumerated Int32 value to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, int numeric)
        {
            var numericString = numeric.ToString(CultureInfo.InvariantCulture);
            WriteSimpleField(fieldName, numericString, !UseReversibleEncoding);
        }

        /// <summary>
        /// Writes a boolean array to the stream.
        /// </summary>
        public void WriteBooleanArray(string fieldName, IList<bool> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteBoolean(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a sbyte array to the stream.
        /// </summary>
        public void WriteSByteArray(string fieldName, IList<sbyte> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteSByte(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a sbyte array to the stream.
        /// </summary>
        public void WriteByteArray(string fieldName, IList<byte> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteByte(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a short array to the stream.
        /// </summary>
        public void WriteInt16Array(string fieldName, IList<short> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt16(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a ushort array to the stream.
        /// </summary>
        public void WriteUInt16Array(string fieldName, IList<ushort> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt16(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a int array to the stream.
        /// </summary>
        public void WriteInt32Array(string fieldName, IList<int> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt32(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a uint array to the stream.
        /// </summary>
        public void WriteUInt32Array(string fieldName, IList<uint> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt32(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a long array to the stream.
        /// </summary>
        public void WriteInt64Array(string fieldName, IList<long> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt64(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a ulong array to the stream.
        /// </summary>
        public void WriteUInt64Array(string fieldName, IList<ulong> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt64(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a float array to the stream.
        /// </summary>
        public void WriteFloatArray(string fieldName, IList<float> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteFloat(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a double array to the stream.
        /// </summary>
        public void WriteDoubleArray(string fieldName, IList<double> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDouble(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a string array to the stream.
        /// </summary>
        public void WriteStringArray(string fieldName, IList<string> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteString(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a UTC date/time array to the stream.
        /// </summary>
        public void WriteDateTimeArray(string fieldName, IList<DateTime> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (values[ii] <= DateTime.MinValue)
                {
                    WriteSimpleField(null, null, false);
                }
                else
                {
                    WriteDateTime(null, values[ii]);
                }
            }

            PopArray();
        }

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        public void WriteGuidArray(string fieldName, IList<Uuid> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteGuid(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        public void WriteGuidArray(string fieldName, IList<Guid> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteGuid(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes a byte string array to the stream.
        /// </summary>
        public void WriteByteStringArray(string fieldName, IList<byte[]> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteByteString(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an XmlElement array to the stream.
        /// </summary>
        public void WriteXmlElementArray(string fieldName, IList<XmlElement> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteXmlElement(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an NodeId array to the stream.
        /// </summary>
        public void WriteNodeIdArray(string fieldName, IList<NodeId> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteNodeId(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an ExpandedNodeId array to the stream.
        /// </summary>
        public void WriteExpandedNodeIdArray(string fieldName, IList<ExpandedNodeId> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteExpandedNodeId(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an StatusCode array to the stream.
        /// </summary>
        public void WriteStatusCodeArray(string fieldName, IList<StatusCode> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (!UseReversibleEncoding &&
                    values[ii] == StatusCodes.Good)
                {
                    WriteSimpleField(null, null, false);
                }
                else
                {
                    WriteStatusCode(null, values[ii]);
                }
            }

            PopArray();
        }

        /// <summary>
        /// Writes an DiagnosticInfo array to the stream.
        /// </summary>
        public void WriteDiagnosticInfoArray(string fieldName, IList<DiagnosticInfo> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDiagnosticInfo(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an QualifiedName array to the stream.
        /// </summary>
        public void WriteQualifiedNameArray(string fieldName, IList<QualifiedName> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteQualifiedName(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an LocalizedText array to the stream.
        /// </summary>
        public void WriteLocalizedTextArray(string fieldName, IList<LocalizedText> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteLocalizedText(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        public void WriteVariantArray(string fieldName, IList<Variant> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (values[ii] == Variant.Null)
                {
                    WriteSimpleField(null, null, false);
                    continue;
                }

                WriteVariant(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        public void WriteDataValueArray(string fieldName, IList<DataValue> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDataValue(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an extension object array to the stream.
        /// </summary>
        public void WriteExtensionObjectArray(string fieldName, IList<ExtensionObject> values)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteExtensionObject(null, values[ii]);
            }

            PopArray();
        }

        /// <summary>
        /// Writes an encodeable object array to the stream.
        /// </summary>
        public void WriteEncodeableArray(string fieldName, IList<IEncodeable> values, System.Type systemType)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            if (string.IsNullOrWhiteSpace(fieldName) && m_nestingLevel == 0 && !m_topLevelIsArray)
            {
                m_writer.Flush();
                if (m_writer.BaseStream.Length == 1) //Opening "{"
                {
                    m_writer.BaseStream.Seek(0, SeekOrigin.Begin);
                }

                m_nestingLevel++;
                PushArray(fieldName);

                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteEncodeable(null, values[ii], systemType);
                }

                PopArray();
                m_dontWriteClosing = true;
                m_nestingLevel--;

            }
            else if (!string.IsNullOrWhiteSpace(fieldName) && m_nestingLevel == 0 && m_topLevelIsArray)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "With Array as top level, encodeables array with filename will create invalid json");
            }
            else
            {
                PushArray(fieldName);

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteEncodeable(null, values[ii], systemType);
                }

                PopArray();
            }
        }

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        public void WriteEnumeratedArray(string fieldName, Array values, System.Type systemType)
        {
            if (values == null)
            {
                WriteSimpleField(fieldName, null, false);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            // encode each element in the array.
            Type arrayType = values.GetType().GetElementType();
            if (arrayType.IsEnum)
            {
                foreach (Enum value in values)
                {
                    WriteEnumerated(null, value);
                }
            }
            else
            {
                if (arrayType != typeof(Int32))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError,
                        Utils.Format("Type '{0}' is not allowed in an Enumeration.", arrayType.FullName));
                }
                foreach (Int32 value in values)
                {
                    WriteEnumerated(null, value);
                }
            }

            PopArray();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Writes the contents of an Variant to the stream.
        /// </summary>
        public void WriteVariantContents(object value, TypeInfo typeInfo)
        {
            // check for null.
            if (value == null)
            {
                return;
            }

            m_commaRequired = false;

            // write scalar.
            if (typeInfo.ValueRank < 0)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean: { WriteBoolean(null, (bool)value); return; }
                    case BuiltInType.SByte: { WriteSByte(null, (sbyte)value); return; }
                    case BuiltInType.Byte: { WriteByte(null, (byte)value); return; }
                    case BuiltInType.Int16: { WriteInt16(null, (short)value); return; }
                    case BuiltInType.UInt16: { WriteUInt16(null, (ushort)value); return; }
                    case BuiltInType.Int32: { WriteInt32(null, (int)value); return; }
                    case BuiltInType.UInt32: { WriteUInt32(null, (uint)value); return; }
                    case BuiltInType.Int64: { WriteInt64(null, (long)value); return; }
                    case BuiltInType.UInt64: { WriteUInt64(null, (ulong)value); return; }
                    case BuiltInType.Float: { WriteFloat(null, (float)value); return; }
                    case BuiltInType.Double: { WriteDouble(null, (double)value); return; }
                    case BuiltInType.String: { WriteString(null, (string)value); return; }
                    case BuiltInType.DateTime: { WriteDateTime(null, (DateTime)value); return; }
                    case BuiltInType.Guid: { WriteGuid(null, (Uuid)value); return; }
                    case BuiltInType.ByteString: { WriteByteString(null, (byte[])value); return; }
                    case BuiltInType.XmlElement: { WriteXmlElement(null, (XmlElement)value); return; }
                    case BuiltInType.NodeId: { WriteNodeId(null, (NodeId)value); return; }
                    case BuiltInType.ExpandedNodeId: { WriteExpandedNodeId(null, (ExpandedNodeId)value); return; }
                    case BuiltInType.StatusCode: { WriteStatusCode(null, (StatusCode)value); return; }
                    case BuiltInType.QualifiedName: { WriteQualifiedName(null, (QualifiedName)value); return; }
                    case BuiltInType.LocalizedText: { WriteLocalizedText(null, (LocalizedText)value); return; }
                    case BuiltInType.ExtensionObject: { WriteExtensionObject(null, (ExtensionObject)value); return; }
                    case BuiltInType.DataValue: { WriteDataValue(null, (DataValue)value); return; }
                    case BuiltInType.Enumeration: { WriteEnumerated(null, (Enum)value); return; }
                }
            }

            // write array.
            else if (typeInfo.ValueRank <= 1)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean: { WriteBooleanArray(null, (bool[])value); return; }
                    case BuiltInType.SByte: { WriteSByteArray(null, (sbyte[])value); return; }
                    case BuiltInType.Byte: { WriteByteArray(null, (byte[])value); return; }
                    case BuiltInType.Int16: { WriteInt16Array(null, (short[])value); return; }
                    case BuiltInType.UInt16: { WriteUInt16Array(null, (ushort[])value); return; }
                    case BuiltInType.Int32: { WriteInt32Array(null, (int[])value); return; }
                    case BuiltInType.UInt32: { WriteUInt32Array(null, (uint[])value); return; }
                    case BuiltInType.Int64: { WriteInt64Array(null, (long[])value); return; }
                    case BuiltInType.UInt64: { WriteUInt64Array(null, (ulong[])value); return; }
                    case BuiltInType.Float: { WriteFloatArray(null, (float[])value); return; }
                    case BuiltInType.Double: { WriteDoubleArray(null, (double[])value); return; }
                    case BuiltInType.String: { WriteStringArray(null, (string[])value); return; }
                    case BuiltInType.DateTime: { WriteDateTimeArray(null, (DateTime[])value); return; }
                    case BuiltInType.Guid: { WriteGuidArray(null, (Uuid[])value); return; }
                    case BuiltInType.ByteString: { WriteByteStringArray(null, (byte[][])value); return; }
                    case BuiltInType.XmlElement: { WriteXmlElementArray(null, (XmlElement[])value); return; }
                    case BuiltInType.NodeId: { WriteNodeIdArray(null, (NodeId[])value); return; }
                    case BuiltInType.ExpandedNodeId: { WriteExpandedNodeIdArray(null, (ExpandedNodeId[])value); return; }
                    case BuiltInType.StatusCode: { WriteStatusCodeArray(null, (StatusCode[])value); return; }
                    case BuiltInType.QualifiedName: { WriteQualifiedNameArray(null, (QualifiedName[])value); return; }
                    case BuiltInType.LocalizedText: { WriteLocalizedTextArray(null, (LocalizedText[])value); return; }
                    case BuiltInType.ExtensionObject: { WriteExtensionObjectArray(null, (ExtensionObject[])value); return; }
                    case BuiltInType.DataValue: { WriteDataValueArray(null, (DataValue[])value); return; }
                    case BuiltInType.Enumeration:
                    {
                        Array array = value as Array;
                        WriteEnumeratedArray(null, array, array.GetType().GetElementType());
                        return;
                    }

                    case BuiltInType.Variant:
                    {
                        Variant[] variants = value as Variant[];

                        if (variants != null)
                        {
                            WriteVariantArray(null, variants);
                            return;
                        }

                        object[] objects = value as object[];

                        if (objects != null)
                        {
                            WriteObjectArray(null, objects);
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
                Matrix matrix = value as Matrix;
                if (matrix != null)
                {
                    if (UseReversibleEncoding)
                    {
                        WriteVariantContents(matrix.Elements, new TypeInfo(typeInfo.BuiltInType, 1));
                    }
                    else
                    {
                        int index = 0;
                        WriteStructureMatrix(matrix, 0, ref index, typeInfo);
                    }
                    return;
                }
            }

            // oops - should never happen.
            throw new ServiceResultException(
                StatusCodes.BadEncodingError,
                Utils.Format("Type '{0}' is not allowed in an Variant.", value.GetType().FullName));
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Write multi dimensional array in structure.
        /// </summary>
        private void WriteStructureMatrix(
            Matrix matrix,
            int dim,
            ref int index,
            TypeInfo typeInfo)
        {
            var arrayLen = matrix.Dimensions[dim];
            if (dim == matrix.Dimensions.Length - 1)
            {
                // Create a slice of values for the top dimension
                var copy = Array.CreateInstance(
                    matrix.Elements.GetType().GetElementType(), arrayLen);
                Array.Copy(matrix.Elements, index, copy, 0, arrayLen);
                // Write slice as value rank
                if (m_commaRequired)
                {
                    m_writer.Write(",");
                }
                WriteVariantContents(copy, new TypeInfo(typeInfo.BuiltInType, 1));
                index += arrayLen;
            }
            else
            {
                PushArray(null);
                for (var i = 0; i < arrayLen; i++)
                {
                    WriteStructureMatrix(matrix, dim + 1, ref index, typeInfo);
                }
                PopArray();
            }
        }

        /// <summary>
        /// Write multi dimensional array in Variant.
        /// </summary>
        private void WriteVariantMatrix(string fieldName, Matrix value)
        {
            PushStructure(fieldName);
            WriteVariant("Matrix", new Variant(value.Elements, new TypeInfo(value.TypeInfo.BuiltInType, ValueRanks.OneDimension)));
            WriteInt32Array("Dimensions", value.Dimensions);
            PopStructure();
        }

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        public void WriteObjectArray(string fieldName, IList<object> values)
        {
            PushArray(fieldName);

            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            if (values != null)
            {
                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteVariant("Variant", new Variant(values[ii]));
                }
            }

            PopArray();
        }
        #endregion
    }
}
