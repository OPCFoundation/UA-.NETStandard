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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Opc.Ua.Bindings;
using Opc.Ua.Buffers;

namespace Opc.Ua
{
    /// <summary>
    /// Encodes objects in a stream using the UA Binary encoding.
    /// </summary>
    public class BinaryEncoder : IEncoder
    {
        #region Constructor
        /// <summary>
        /// Creates an encoder that writes to a memory buffer.
        /// </summary>
        public BinaryEncoder(IServiceMessageContext context)
        {
            m_ostrm = new MemoryStream();
            m_writer = new BinaryWriter(m_ostrm);
            m_context = context;
            m_leaveOpen = false;
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Creates an encoder that writes to a fixed size memory buffer.
        /// </summary>
        public BinaryEncoder(byte[] buffer, int start, int count, IServiceMessageContext context)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            m_ostrm = new MemoryStream(buffer, start, count);
            m_writer = new BinaryWriter(m_ostrm);
            m_context = context;
            m_leaveOpen = false;
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Creates an encoder that writes to the stream.
        /// </summary>
        /// <param name="stream">The stream to which the encoder writes.</param>
        /// <param name="context">The message context to use for the encoding.</param>
        /// <param name="leaveOpen">If the stream should be left open on dispose.</param>
        public BinaryEncoder(Stream stream, IServiceMessageContext context, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            m_ostrm = stream;
            m_writer = new BinaryWriter(m_ostrm, Encoding.UTF8, leaveOpen);
            m_context = context;
            m_leaveOpen = leaveOpen;
            m_nestingLevel = 0;
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
                if (m_writer != null)
                {
                    m_writer.Flush();
                    m_writer.Dispose();
                    m_writer = null;
                }

                if (!m_leaveOpen)
                {
                    m_ostrm?.Dispose();
                    m_ostrm = null;
                }
            }
        }
        #endregion

        #region Public Members
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
        /// Completes writing and returns the buffer (if available).
        /// </summary>
        public byte[] CloseAndReturnBuffer()
        {
            Close();

            if (m_ostrm is MemoryStream memoryStream)
            {
                return memoryStream.ToArray();
            }

            return null;
        }

        /// <summary>
        /// Completes writing and returns the buffer as base64 encoded string.
        /// </summary>
        public string CloseAndReturnText()
        {
            Close();

            if (m_ostrm is MemoryStream memoryStream)
            {
                return Convert.ToBase64String(memoryStream.ToArray());
            }

            return null;
        }

        /// <summary>
        /// Completes writing and returns position in the stream.
        /// </summary>
        public int Close()
        {
            int position = (int)m_writer.BaseStream.Position;
            m_writer.Flush();
            m_writer.Dispose();
            return position;
        }

        /// <summary>
        /// Gets or sets the position in the stream.
        /// </summary>
        public int Position
        {
            get
            {
                return (int)m_writer.BaseStream.Position;
            }

            set
            {
                m_writer.Seek(value, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// Writes raw bytes to the stream.
        /// </summary>
        public void WriteRawBytes(byte[] buffer, int offset, int count)
        {
            m_writer.Write(buffer, offset, count);
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <summary>
        /// Writes raw bytes to the stream.
        /// </summary>
        public void WriteRawBytes(ReadOnlySpan<byte> buffer)
        {
            m_writer.Write(buffer);
        }
#endif

        /// <summary>
        /// Encodes a message in a buffer.
        /// </summary>
        public static byte[] EncodeMessage(IEncodeable message, IServiceMessageContext context)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (context == null) throw new ArgumentNullException(nameof(context));

            // create encoder.
            using (BinaryEncoder encoder = new BinaryEncoder(context))
            {
                // encode message
                encoder.EncodeMessage(message);

                // close encoder.
                return encoder.CloseAndReturnBuffer();
            }
        }

        /// <summary>
        /// Encodes a session-less message to a buffer.
        /// </summary>
        public static void EncodeSessionLessMessage(IEncodeable message, Stream stream, IServiceMessageContext context, bool leaveOpen)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (context == null) throw new ArgumentNullException(nameof(context));

            // create encoder.
            using (BinaryEncoder encoder = new BinaryEncoder(stream, context, leaveOpen))
            {
                long start = encoder.m_ostrm.Position;

                // write the type id.
                encoder.WriteNodeId(null, DataTypeIds.SessionlessInvokeRequestType);

                // write the message.
                SessionLessServiceMessage envelope = new SessionLessServiceMessage();
                envelope.NamespaceUris = context.NamespaceUris;
                envelope.ServerUris = context.ServerUris;
                envelope.Message = message;

                envelope.Encode(encoder);

                // check that the max message size was not exceeded.
                if (context.MaxMessageSize > 0 && context.MaxMessageSize < (int)(encoder.m_ostrm.Position - start))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "MaxMessageSize {0} < {1}",
                        context.MaxMessageSize,
                        (int)(encoder.m_ostrm.Position - start));
                }
            }
        }

        /// <summary>
        /// Encodes a message in a stream.
        /// </summary>
        public static void EncodeMessage(IEncodeable message, Stream stream, IServiceMessageContext context, bool leaveOpen)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (context == null) throw new ArgumentNullException(nameof(context));

            // create encoder.
            using (BinaryEncoder encoder = new BinaryEncoder(stream, context, leaveOpen))
            {
                // encode message
                encoder.EncodeMessage(message);
            }
        }

        /// <summary>
        /// Encodes a message with its header.
        /// </summary>
        public void EncodeMessage(IEncodeable message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            long start = m_ostrm.Position;

            // convert the namespace uri to an index.
            NodeId typeId = ExpandedNodeId.ToNodeId(message.BinaryEncodingId, m_context.NamespaceUris);

            // write the type id.
            WriteNodeId(null, typeId);

            // write the message.
            WriteEncodeable(null, message, message.GetType());

            // check that the max message size was not exceeded.
            if (m_context.MaxMessageSize > 0 && m_context.MaxMessageSize < (int)(m_ostrm.Position - start))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    m_context.MaxMessageSize,
                    (int)(m_ostrm.Position - start));
            }
        }

        /// <summary>
        /// Saves a string table from a binary stream.
        /// </summary>
        public void SaveStringTable(StringTable stringTable)
        {
            if (stringTable == null || stringTable.Count <= 1)
            {
                WriteInt32(null, -1);
                return;
            }

            WriteInt32(null, stringTable.Count - 1);

            for (uint ii = 1; ii < stringTable.Count; ii++)
            {
                WriteString(null, stringTable.GetString(ii));
            }
        }
        #endregion

        #region IEncoder Members
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Binary;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public IServiceMessageContext Context => m_context;

        /// <summary>
        /// Binary Encoder always produces reversible encoding.
        /// </summary>
        public bool UseReversibleEncoding => true;

        /// <summary>
        /// Pushes a namespace onto the namespace stack.
        /// </summary>
        public void PushNamespace(string namespaceUri)
        {
            // not used in the binary encoding.
        }

        /// <summary>
        /// Pops a namespace from the namespace stack.
        /// </summary>
        public void PopNamespace()
        {
            // not used in the binary encoding.
        }

        /// <summary>
        /// Writes a boolean to the stream.
        /// </summary>
        public void WriteBoolean(string fieldName, bool value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a sbyte to the stream.
        /// </summary>
        public void WriteSByte(string fieldName, sbyte value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a byte to the stream.
        /// </summary>
        public void WriteByte(string fieldName, byte value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a short to the stream.
        /// </summary>
        public void WriteInt16(string fieldName, short value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a ushort to the stream.
        /// </summary>
        public void WriteUInt16(string fieldName, ushort value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes an int to the stream.
        /// </summary>
        public void WriteInt32(string fieldName, int value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a uint to the stream.
        /// </summary>
        public void WriteUInt32(string fieldName, uint value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a long to the stream.
        /// </summary>
        public void WriteInt64(string fieldName, long value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a ulong to the stream.
        /// </summary>
        public void WriteUInt64(string fieldName, ulong value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a float to the stream.
        /// </summary>
        public void WriteFloat(string fieldName, float value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a double to the stream.
        /// </summary>
        public void WriteDouble(string fieldName, double value)
        {
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a string to the stream.
        /// </summary>
        public void WriteString(string fieldName, string value)
        {
            if (value == null)
            {
                WriteInt32(null, -1);
                return;
            }

            if (m_context.MaxStringLength > 0 && m_context.MaxStringLength < value.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}",
                    m_context.MaxStringLength,
                    value.Length);
            }

            int maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
#if NET5_0_OR_GREATER
            const int minByteCountPerBuffer = 256;
            const int maxByteCountPerBuffer = 8192;
#endif
            const int maxStackAllocByteCount = 128;
            if (maxByteCount <= maxStackAllocByteCount)
            {
                Span<byte> encodedBytes = stackalloc byte[maxByteCount];
                int count = Encoding.UTF8.GetBytes(value, encodedBytes);
                WriteByteString(null, encodedBytes.Slice(0, count));
            }
            else
#if NET5_0_OR_GREATER
            if (maxByteCount > maxByteCountPerBuffer)
            {
                using (var bufferWriter = new ArrayPoolBufferWriter<byte>(minByteCountPerBuffer, maxByteCountPerBuffer))
                {
                    long count = Encoding.UTF8.GetBytes(value.AsSpan(), bufferWriter);
                    WriteByteString(null, bufferWriter.GetReadOnlySequence());
                }
            }
            else
#endif
#endif
            {
                byte[] encodedBytes = ArrayPool<byte>.Shared.Rent(maxByteCount);
                try
                {
                    int count = Encoding.UTF8.GetBytes(value, 0, value.Length, encodedBytes, 0);
                    WriteByteString(null, encodedBytes, 0, count);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(encodedBytes);
                }
            }
        }

        /// <summary>
        /// Writes a UTC date/time to the stream.
        /// </summary>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            value = Utils.ToOpcUaUniversalTime(value);

            long ticks = value.Ticks;

            // check for max value.
            if (ticks >= DateTime.MaxValue.Ticks)
            {
                ticks = Int64.MaxValue;
            }

            // check for min value.
            else
            {
                ticks -= Utils.TimeBase.Ticks;

                if (ticks <= 0)
                {
                    ticks = 0;
                }
            }

            m_writer.Write(ticks);
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Uuid value)
        {
            m_writer.Write(((Guid)value).ToByteArray());
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Guid value)
        {
            m_writer.Write(((Guid)value).ToByteArray());
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        public void WriteByteString(string fieldName, byte[] value)
        {
            if (value == null)
            {
                WriteInt32(null, -1);
                return;
            }

            WriteByteString(fieldName, value, 0, value.Length);
        }

        /// <summary>
        /// Writes a byte string to the stream from a given index and length.
        /// </summary>
        public void WriteByteString(string fieldName, byte[] value, int index, int count)
        {
            if (value == null)
            {
                WriteInt32(null, -1);
                return;
            }

            if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    m_context.MaxByteStringLength,
                    count);
            }

            WriteInt32(null, count);
            m_writer.Write(value, index, count);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        public void WriteByteString(string fieldName, ReadOnlySpan<byte> value)
        {
            if (value == null)
            {
                WriteInt32(null, -1);
                return;
            }

            if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < value.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    m_context.MaxByteStringLength,
                    value.Length);
            }

            WriteInt32(null, value.Length);
            m_writer.Write(value);
        }
#endif

        /// <summary>
        /// Writes an XmlElement to the stream.
        /// </summary>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            if (value == null)
            {
                WriteInt32(null, -1);
                return;
            }

            WriteString(fieldName, value.OuterXml);
        }

        /// <summary>
        /// Writes an NodeId to the stream.
        /// </summary>
        public void WriteNodeId(string fieldName, NodeId value)
        {
            // write a null node id.
            if (value == null)
            {
                WriteUInt16(null, 0);
                return;
            }

            ushort namespaceIndex = value.NamespaceIndex;

            if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
            {
                namespaceIndex = m_namespaceMappings[namespaceIndex];
            }

            // get the node encoding.
            byte encoding = GetNodeIdEncoding(value.IdType, value.Identifier, namespaceIndex);

            // write the encoding.
            WriteByte(null, encoding);

            // write the node.
            WriteNodeIdBody(encoding, value.Identifier, namespaceIndex);
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            // write a null node id.
            if (value == null)
            {
                WriteUInt16(null, 0);
                return;
            }

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

            // get the node encoding.
            byte encoding = GetNodeIdEncoding(value.IdType, value.Identifier, namespaceIndex);

            // add the bit indicating a uri string is encoded as well.
            if (!String.IsNullOrEmpty(value.NamespaceUri))
            {
                encoding |= 0x80;
            }

            // add the bit indicating a server index.
            if (serverIndex > 0)
            {
                encoding |= 0x40;
            }

            // write the encoding.
            WriteByte(null, encoding);

            // write the node id.
            WriteNodeIdBody(encoding, value.Identifier, namespaceIndex);

            // write the namespace uri.
            if ((encoding & 0x80) != 0)
            {
                WriteString(null, value.NamespaceUri);
            }

            // write the server index.
            if ((encoding & 0x40) != 0)
            {
                WriteUInt32(null, serverIndex);
            }
        }

        /// <summary>
        /// Writes an StatusCode to the stream.
        /// </summary>
        public void WriteStatusCode(string fieldName, StatusCode value)
        {
            WriteUInt32(null, value.Code);
        }

        /// <summary>
        /// Writes a DiagnosticInfo to the stream.
        /// </summary>
        public void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value)
        {
            WriteDiagnosticInfo(fieldName, value, 0);
        }

        /// <summary>
        /// Writes an QualifiedName to the stream.
        /// </summary>
        public void WriteQualifiedName(string fieldName, QualifiedName value)
        {
            // check for null.
            if (value == null)
            {
                value = new QualifiedName();
            }

            ushort namespaceIndex = value.NamespaceIndex;

            if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
            {
                namespaceIndex = m_namespaceMappings[namespaceIndex];
            }

            WriteUInt16(null, namespaceIndex);
            WriteString(null, value.Name);
        }

        /// <summary>
        /// Writes an LocalizedText to the stream.
        /// </summary>
        public void WriteLocalizedText(string fieldName, LocalizedText value)
        {
            // check for null.
            if (value == null)
            {
                WriteByte(null, 0);
                return;
            }

            // calculate the encoding.
            byte encoding = 0;

            if (value.Locale != null)
            {
                encoding |= (byte)LocalizedTextEncodingBits.Locale;
            }

            if (value.Text != null)
            {
                encoding |= (byte)LocalizedTextEncodingBits.Text;
            }

            // write the encoding.
            WriteByte(null, encoding);

            // write the fields.
            if ((encoding & (byte)LocalizedTextEncodingBits.Locale) != 0)
            {
                WriteString(null, value.Locale);
            }

            if ((encoding & (byte)LocalizedTextEncodingBits.Text) != 0)
            {
                WriteString(null, value.Text);
            }
        }

        /// <summary>
        /// Writes an Variant to the stream.
        /// </summary>
        public void WriteVariant(string fieldName, Variant value)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                WriteVariantValue(fieldName, value);
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
            // check for null.
            if (value == null)
            {
                WriteByte(null, 0);
                return;
            }

            // calculate the encoding.
            byte encoding = 0;

            if (!value.WrappedValue.IsNull)
            {
                encoding |= (byte)DataValueEncodingBits.Value;
            }

            if (value.StatusCode != StatusCodes.Good)
            {
                encoding |= (byte)DataValueEncodingBits.StatusCode;
            }

            if (value.SourceTimestamp != DateTime.MinValue)
            {
                encoding |= (byte)DataValueEncodingBits.SourceTimestamp;

                if (value.SourcePicoseconds != 0)
                {
                    encoding |= (byte)DataValueEncodingBits.SourcePicoseconds;
                }
            }

            if (value.ServerTimestamp != DateTime.MinValue)
            {
                encoding |= (byte)DataValueEncodingBits.ServerTimestamp;

                if (value.ServerPicoseconds != 0)
                {
                    encoding |= (byte)DataValueEncodingBits.ServerPicoseconds;
                }
            }

            // write the encoding.
            WriteByte(null, encoding);

            // write the fields of the data value structure.
            if ((encoding & (byte)DataValueEncodingBits.Value) != 0)
            {
                WriteVariant(null, value.WrappedValue);
            }

            if ((encoding & (byte)DataValueEncodingBits.StatusCode) != 0)
            {
                WriteStatusCode(null, value.StatusCode);
            }

            if ((encoding & (byte)DataValueEncodingBits.SourceTimestamp) != 0)
            {
                WriteDateTime(null, value.SourceTimestamp);

                if ((encoding & (byte)DataValueEncodingBits.SourcePicoseconds) != 0)
                {
                    WriteUInt16(null, value.SourcePicoseconds);
                }
            }

            if ((encoding & (byte)DataValueEncodingBits.ServerTimestamp) != 0)
            {
                WriteDateTime(null, value.ServerTimestamp);

                if ((encoding & (byte)DataValueEncodingBits.ServerPicoseconds) != 0)
                {
                    WriteUInt16(null, value.ServerPicoseconds);
                }
            }
        }

        /// <summary>
        /// Writes an ExtensionObject to the stream.
        /// </summary>
        public void WriteExtensionObject(string fieldName, ExtensionObject value)
        {
            // check for null.
            if (value == null)
            {
                WriteNodeId(null, NodeId.Null);
                WriteByte(null, (byte)ExtensionObjectEncoding.None);
                return;
            }

            IEncodeable encodeable = value.Body as IEncodeable;

            // write the type id.
            ExpandedNodeId typeId = value.TypeId;

            if (encodeable != null)
            {
                if (value.Encoding == ExtensionObjectEncoding.Xml)
                {
                    typeId = encodeable.XmlEncodingId;
                }
                else
                {
                    typeId = encodeable.BinaryEncodingId;
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

            WriteNodeId(null, localTypeId);

            object body = value.Body;
            if (body == null)
            {
                // nothing more to do for null bodies.
                WriteByte(null, (byte)ExtensionObjectEncoding.None);
                return;
            }

            // determine the encoding type.
            byte encoding;
            if (value.Encoding == ExtensionObjectEncoding.EncodeableObject)
            {
                encoding = (byte)ExtensionObjectEncoding.Binary;
            }
            else
            {
                encoding = (byte)value.Encoding;
            }

            // write the encoding type.
            WriteByte(null, encoding);

            // write binary bodies.
            if (body is byte[] bytes)
            {
                WriteByteString(null, bytes);
                return;
            }

            // write XML bodies.
            if (body is XmlElement xml)
            {
                WriteXmlElement(null, xml);
                return;
            }

            // write encodeable bodies.
            if (encodeable == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError,
                    Utils.Format("Cannot encode bodies of type '{0}' in extension objects.", body.GetType().FullName));
            }

            // check if it possible to write the extension directly to the stream.
            if (m_writer.BaseStream.CanSeek)
            {
                long start = m_writer.BaseStream.Position;

                // write a placeholder for the body length.
                WriteInt32(null, -1);
                encodeable.Encode(this);

                // update body length.
                long delta = m_writer.BaseStream.Position - start;

                m_writer.Seek((int)-delta, SeekOrigin.Current);
                WriteInt32(null, (int)(delta - 4));
                m_writer.Seek((int)(delta - 4), SeekOrigin.Current);
            }

            // must pre-encode and then write the bytes.
            else
            {
                using (BinaryEncoder encoder = new BinaryEncoder(this.m_context))
                {
                    encoder.WriteEncodeable(null, encodeable, null);
                    bytes = encoder.CloseAndReturnBuffer();
                    WriteByteString(null, bytes);
                }
            }
        }

        /// <summary>
        /// Writes an encodeable object to the stream.
        /// </summary>
        public void WriteEncodeable(string fieldName, IEncodeable value, System.Type systemType)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                // create a default object if a null object specified.
                if (value == null)
                {
                    if (systemType == null) throw new ArgumentNullException(nameof(systemType));
                    value = Activator.CreateInstance(systemType) as IEncodeable;
                }

                // encode the object.
                if (value != null)
                {
                    value.Encode(this);
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, Enum value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            WriteInt32(null, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes a boolean array to the stream.
        /// </summary>
        public void WriteBooleanArray(string fieldName, IList<bool> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteBoolean(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a sbyte array to the stream.
        /// </summary>
        public void WriteSByteArray(string fieldName, IList<sbyte> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteSByte(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a byte array to the stream.
        /// </summary>
        public void WriteByteArray(string fieldName, IList<byte> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteByte(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a short array to the stream.
        /// </summary>
        public void WriteInt16Array(string fieldName, IList<short> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt16(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a ushort array to the stream.
        /// </summary>
        public void WriteUInt16Array(string fieldName, IList<ushort> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt16(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a int array to the stream.
        /// </summary>
        public void WriteInt32Array(string fieldName, IList<int> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt32(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a uint array to the stream.
        /// </summary>
        public void WriteUInt32Array(string fieldName, IList<uint> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt32(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a long array to the stream.
        /// </summary>
        public void WriteInt64Array(string fieldName, IList<long> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt64(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a ulong array to the stream.
        /// </summary>
        public void WriteUInt64Array(string fieldName, IList<ulong> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt64(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a float array to the stream.
        /// </summary>
        public void WriteFloatArray(string fieldName, IList<float> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteFloat(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a double array to the stream.
        /// </summary>
        public void WriteDoubleArray(string fieldName, IList<double> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDouble(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a string array to the stream.
        /// </summary>
        public void WriteStringArray(string fieldName, IList<string> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteString(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a UTC date/time array to the stream.
        /// </summary>
        public void WriteDateTimeArray(string fieldName, IList<DateTime> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDateTime(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        public void WriteGuidArray(string fieldName, IList<Uuid> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteGuid(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        public void WriteGuidArray(string fieldName, IList<Guid> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteGuid(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes a byte string array to the stream.
        /// </summary>
        public void WriteByteStringArray(string fieldName, IList<byte[]> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteByteString(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an XmlElement array to the stream.
        /// </summary>
        public void WriteXmlElementArray(string fieldName, IList<XmlElement> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteXmlElement(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an NodeId array to the stream.
        /// </summary>
        public void WriteNodeIdArray(string fieldName, IList<NodeId> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteNodeId(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an ExpandedNodeId array to the stream.
        /// </summary>
        public void WriteExpandedNodeIdArray(string fieldName, IList<ExpandedNodeId> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteExpandedNodeId(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an StatusCode array to the stream.
        /// </summary>
        public void WriteStatusCodeArray(string fieldName, IList<StatusCode> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteStatusCode(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an DiagnosticInfo array to the stream.
        /// </summary>
        public void WriteDiagnosticInfoArray(string fieldName, IList<DiagnosticInfo> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDiagnosticInfo(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an QualifiedName array to the stream.
        /// </summary>
        public void WriteQualifiedNameArray(string fieldName, IList<QualifiedName> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteQualifiedName(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an LocalizedText array to the stream.
        /// </summary>
        public void WriteLocalizedTextArray(string fieldName, IList<LocalizedText> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteLocalizedText(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        public void WriteVariantArray(string fieldName, IList<Variant> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteVariant(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        public void WriteDataValueArray(string fieldName, IList<DataValue> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDataValue(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an extension object array to the stream.
        /// </summary>
        public void WriteExtensionObjectArray(string fieldName, IList<ExtensionObject> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteExtensionObject(null, values[ii]);
            }
        }

        /// <summary>
        /// Writes an encodeable object array to the stream.
        /// </summary>
        public void WriteEncodeableArray(string fieldName, IList<IEncodeable> values, System.Type systemType)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteEncodeable(null, values[ii], systemType);
            }
        }

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        public void WriteEnumeratedArray(string fieldName, Array values, System.Type systemType)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Length; ii++)
            {
                WriteEnumerated(null, (Enum)values.GetValue(ii));
            }
        }

        /// <summary>
        /// Encode an array according to its valueRank and BuiltInType
        /// </summary>
        public void WriteArray(string fieldName, object array, int valueRank, BuiltInType builtInType)
        {
            if (valueRank == ValueRanks.OneDimension)
            {
                /* One dimensional Arrays are encoded as a sequence of elements preceeded
                 * by the number of elements encoded as an Int32 value.
                 * If an Array is null, then its length is encoded as −1.*/
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        WriteBooleanArray(null, (bool[])array);
                        break;
                    case BuiltInType.SByte:
                        WriteSByteArray(null, (sbyte[])array);
                        break;
                    case BuiltInType.Byte:
                        WriteByteArray(null, (byte[])array);
                        break;
                    case BuiltInType.Int16:
                        WriteInt16Array(null, (short[])array);
                        break;
                    case BuiltInType.UInt16:
                        WriteUInt16Array(null, (ushort[])array);
                        break;
                    case BuiltInType.Int32:
                        WriteInt32Array(null, (int[])array);
                        break;
                    case BuiltInType.UInt32:
                        WriteUInt32Array(null, (uint[])array);
                        break;
                    case BuiltInType.Int64:
                        WriteInt64Array(null, (long[])array);
                        break;
                    case BuiltInType.UInt64:
                        WriteUInt64Array(null, (ulong[])array);
                        break;
                    case BuiltInType.Float:
                        WriteFloatArray(null, (float[])array);
                        break;
                    case BuiltInType.Double:
                        WriteDoubleArray(null, (double[])array);
                        break;
                    case BuiltInType.DateTime:
                        WriteDateTimeArray(null, (DateTime[])array);
                        break;
                    case BuiltInType.Guid:
                        WriteGuidArray(null, (Uuid[])array);
                        break;
                    case BuiltInType.String:
                        WriteStringArray(null, (string[])array);
                        break;
                    case BuiltInType.ByteString:
                        WriteByteStringArray(null, (byte[][])array);
                        break;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedNameArray(null, (QualifiedName[])array);
                        break;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedTextArray(null, (LocalizedText[])array);
                        break;
                    case BuiltInType.NodeId:
                        WriteNodeIdArray(null, (NodeId[])array);
                        break;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeIdArray(null, (ExpandedNodeId[])array);
                        break;
                    case BuiltInType.StatusCode:
                        WriteStatusCodeArray(null, (StatusCode[])array);
                        break;
                    case BuiltInType.XmlElement:
                        WriteXmlElementArray(null, (XmlElement[])array);
                        break;
                    case BuiltInType.Variant:
                    {
                        // try to write IEncodeable Array
                        if (array is IEncodeable[] encodeableArray)
                        {
                            WriteEncodeableArray(null, encodeableArray, array.GetType().GetElementType());
                            return;
                        }

                        if (array is Variant[] variantArray)
                        {
                            WriteVariantArray(null, variantArray);
                            return;
                        }

                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Matrix.");
                    }
                    case BuiltInType.Enumeration:
                        int[] ints = array as int[];
                        if (ints == null)
                        {
                            if (array is Enum[] enums)
                            {
                                ints = new int[enums.Length];
                                for (int ii = 0; ii < enums.Length; ii++)
                                {
                                    ints[ii] = Convert.ToInt32(enums[ii], CultureInfo.InvariantCulture);
                                }
                            }
                        }
                        if (ints != null)
                        {
                            WriteInt32Array(null, ints);
                        }
                        else
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingError,
                                "Unexpected type encountered while encoding an Enumeration Array.");
                        }
                        break;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObjectArray(null, (ExtensionObject[])array);
                        break;
                    case BuiltInType.DiagnosticInfo:
                        WriteDiagnosticInfoArray(null, (DiagnosticInfo[])array);
                        break;
                    case BuiltInType.DataValue:
                        WriteDataValueArray(null, (DataValue[])array);
                        break;
                    default:
                    {
                        // try to write IEncodeable Array
                        if (array is IEncodeable[] encodeableArray)
                        {
                            WriteEncodeableArray(fieldName, encodeableArray, array.GetType().GetElementType());
                            break;
                        }
                        if (array == null)
                        {
                            // write zero dimension
                            WriteInt32(null, -1);
                            return;
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding an Array with BuiltInType: {0}",
                            builtInType);
                    }
                }
            }
            else if (valueRank > ValueRanks.OneDimension)
            {
                /* Multi-dimensional Arrays are encoded as an Int32 Array containing the dimensions followed by
                 * a list of all the values in the Array. The total number of values is equal to the
                 * product of the dimensions.
                 * The number of values is 0 if one or more dimension is less than or equal to 0.*/

                Matrix matrix = array as Matrix;
                if (matrix == null)
                {
                    if (!(array is Array multiArray) || multiArray.Rank != valueRank)
                    {
                        // there is no Dimensions to write
                        WriteInt32(null, -1);
                        return;
                    }
                    matrix = new Matrix(multiArray, builtInType);
                }

                // Write the Dimensions
                WriteInt32Array(null, (int[])matrix.Dimensions);

                switch (matrix.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                    {
                        bool[] values = (bool[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteBoolean(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.SByte:
                    {
                        sbyte[] values = (sbyte[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteSByte(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Byte:
                    {
                        byte[] values = (byte[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteByte(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Int16:
                    {
                        Int16[] values = (Int16[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteInt16(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.UInt16:
                    {
                        UInt16[] values = (UInt16[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteUInt16(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Enumeration:
                    {
                        if (matrix.Elements is Enum[] values)
                        {
                            for (int ii = 0; ii < values.Length; ii++)
                            {
                                WriteEnumerated(null, values[ii]);
                            }
                            break;
                        }
                        goto case BuiltInType.Int32;
                    }
                    case BuiltInType.Int32:
                    {
                        Int32[] values = (Int32[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteInt32(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.UInt32:
                    {
                        UInt32[] values = (UInt32[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteUInt32(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Int64:
                    {
                        Int64[] values = (Int64[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteInt64(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.UInt64:
                    {
                        UInt64[] values = (UInt64[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteUInt64(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Float:
                    {
                        float[] values = (float[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteFloat(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Double:
                    {
                        double[] values = (double[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteDouble(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.String:
                    {
                        string[] values = (string[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteString(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.DateTime:
                    {
                        DateTime[] values = (DateTime[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteDateTime(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Guid:
                    {
                        Uuid[] values = (Uuid[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteGuid(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.ByteString:
                    {
                        byte[][] values = (byte[][])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteByteString(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.XmlElement:
                    {
                        XmlElement[] values = (XmlElement[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteXmlElement(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.NodeId:
                    {
                        NodeId[] values = (NodeId[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteNodeId(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.ExpandedNodeId:
                    {
                        ExpandedNodeId[] values = (ExpandedNodeId[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteExpandedNodeId(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.StatusCode:
                    {
                        StatusCode[] values = (StatusCode[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteStatusCode(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.QualifiedName:
                    {
                        QualifiedName[] values = (QualifiedName[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteQualifiedName(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.LocalizedText:
                    {
                        LocalizedText[] values = (LocalizedText[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteLocalizedText(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.ExtensionObject:
                    {
                        ExtensionObject[] values = (ExtensionObject[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteExtensionObject(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.DataValue:
                    {
                        DataValue[] values = (DataValue[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteDataValue(null, values[ii]);
                        }
                        break;
                    }
                    case BuiltInType.Variant:
                    {
                        if (matrix.Elements is Variant[] variants)
                        {
                            for (int ii = 0; ii < variants.Length; ii++)
                            {
                                WriteVariant(null, variants[ii]);
                            }
                            break;
                        }

                        // try to write IEncodeable Array
                        if (matrix.Elements is IEncodeable[] encodeableArray)
                        {
                            for (int ii = 0; ii < encodeableArray.Length; ii++)
                            {
                                WriteEncodeable(null, encodeableArray[ii], null);
                            }
                            break;
                        }

                        if (matrix.Elements is object[] objects)
                        {
                            for (int ii = 0; ii < objects.Length; ii++)
                            {
                                WriteVariant(null, new Variant(objects[ii]));
                            }
                            break;
                        }

                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Matrix.");
                    }
                    case BuiltInType.DiagnosticInfo:
                    {
                        DiagnosticInfo[] values = (DiagnosticInfo[])matrix.Elements;
                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            WriteDiagnosticInfo(null, values[ii]);
                        }
                        break;
                    }
                    default:
                    {
                        // try to write IEncodeable Array
                        if (matrix.Elements is IEncodeable[] encodeableArray)
                        {
                            for (int ii = 0; ii < encodeableArray.Length; ii++)
                            {
                                WriteEncodeable(null, encodeableArray[ii], null);
                            }
                            break;
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Matrix with BuiltInType: {0}",
                            matrix.TypeInfo.BuiltInType);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string fieldName)
        {
            fieldName = null;
            WriteUInt32("SwitchField", switchField);
        }

        /// <inheritdoc/>
        public void WriteEncodingMask(uint encodingMask) => WriteUInt32("EncodingMask", encodingMask);
        #endregion

        #region Private Methods
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Writes a byte string encoded in a ReadOnlySequence to the stream.
        /// </summary>
        private void WriteByteString(string fieldName, ReadOnlySequence<byte> value)
        {
            if (value.IsEmpty)
            {
                WriteInt32(null, -1);
                return;
            }

            if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < value.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    m_context.MaxByteStringLength,
                    value.Length);
            }

            WriteInt32(null, (int)value.Length);
            foreach (var segment in value)
            {
                m_writer.Write(segment.Span);
            }
        }
#endif

        /// <summary>
        /// Writes a DiagnosticInfo to the stream.
        /// Ignores InnerDiagnosticInfo field if the nesting level
        /// <see cref="DiagnosticInfo.MaxInnerDepth"/> is exceeded.
        /// </summary>
        private void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value, int depth)
        {

            // check for null.
            if (value == null)
            {
                WriteByte(null, 0);
                return;
            }

            CheckAndIncrementNestingLevel();

            try
            {
                // calculate the encoding.
                byte encoding = 0;

                if (value.SymbolicId >= 0)
                {
                    encoding |= (byte)DiagnosticInfoEncodingBits.SymbolicId;
                }

                if (value.NamespaceUri >= 0)
                {
                    encoding |= (byte)DiagnosticInfoEncodingBits.NamespaceUri;
                }

                if (value.Locale >= 0)
                {
                    encoding |= (byte)DiagnosticInfoEncodingBits.Locale;
                }

                if (value.LocalizedText >= 0)
                {
                    encoding |= (byte)DiagnosticInfoEncodingBits.LocalizedText;
                }

                if (value.AdditionalInfo != null)
                {
                    encoding |= (byte)DiagnosticInfoEncodingBits.AdditionalInfo;
                }

                if (value.InnerStatusCode != StatusCodes.Good)
                {
                    encoding |= (byte)DiagnosticInfoEncodingBits.InnerStatusCode;
                }

                if (value.InnerDiagnosticInfo != null)
                {
                    if (depth < DiagnosticInfo.MaxInnerDepth)
                    {
                        encoding |= (byte)DiagnosticInfoEncodingBits.InnerDiagnosticInfo;
                    }
                    else
                    {
                        Utils.LogWarning("InnerDiagnosticInfo dropped because nesting exceeds maximum of {0}.",
                            DiagnosticInfo.MaxInnerDepth);
                    }
                }

                // write the encoding.
                WriteByte(null, encoding);

                // write the fields of the diagnostic info structure.
                if ((encoding & (byte)DiagnosticInfoEncodingBits.SymbolicId) != 0)
                {
                    WriteInt32(null, value.SymbolicId);
                }

                if ((encoding & (byte)DiagnosticInfoEncodingBits.NamespaceUri) != 0)
                {
                    WriteInt32(null, value.NamespaceUri);
                }

                if ((encoding & (byte)DiagnosticInfoEncodingBits.Locale) != 0)
                {
                    WriteInt32(null, value.Locale);
                }

                if ((encoding & (byte)DiagnosticInfoEncodingBits.LocalizedText) != 0)
                {
                    WriteInt32(null, value.LocalizedText);
                }

                if ((encoding & (byte)DiagnosticInfoEncodingBits.AdditionalInfo) != 0)
                {
                    WriteString(null, value.AdditionalInfo);
                }

                if ((encoding & (byte)DiagnosticInfoEncodingBits.InnerStatusCode) != 0)
                {
                    WriteStatusCode(null, value.InnerStatusCode);
                }

                if ((encoding & (byte)DiagnosticInfoEncodingBits.InnerDiagnosticInfo) != 0)
                {
                    WriteDiagnosticInfo(null, value.InnerDiagnosticInfo, depth + 1);
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Writes an object array to the stream (converts to Variant first).
        /// </summary>
        private void WriteObjectArray(string fieldName, IList<object> values)
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteVariant(null, new Variant(values[ii]));
            }
        }

        /// <summary>
        /// Write the length of an array. Returns true if the array is empty.
        /// </summary>
        private bool WriteArrayLength<T>(ICollection<T> values)
        {
            // check for null.
            if (values == null)
            {
                WriteInt32(null, -1);
                return true;
            }

            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength {0} < {1}",
                    m_context.MaxArrayLength,
                    values.Count);
            }

            // write length.
            WriteInt32(null, values.Count);
            return values.Count == 0;
        }

        /// <summary>
        /// Write the length of an array. Returns true if the array is empty.
        /// </summary>
        private bool WriteArrayLength(Array values)
        {
            // check for null.
            if (values == null)
            {
                WriteInt32(null, -1);
                return true;
            }

            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength {0} < {1}",
                    m_context.MaxArrayLength,
                    values.Length);
            }

            // write length.
            WriteInt32(null, values.Length);
            return values.Length == 0;
        }

        /// <summary>
        /// Write the length of an array. Returns true if the array is empty.
        /// </summary>
        private bool WriteArrayLength<T>(ArraySegment<T> values)
        {
            // check for null.
            if (values.Array == null)
            {
                WriteInt32(null, -1);
                return true;
            }

            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength {0} < {1} exceeded for array of type {2}",
                    m_context.MaxArrayLength,
                    values.Count,
                    typeof(T).Name);
            }

            // write length.
            WriteInt32(null, values.Count);
            return values.Count == 0;
        }

        /// <summary>
        /// Returns the node id encoding byte for a node id value.
        /// </summary>
        private static byte GetNodeIdEncoding(IdType idType, object identifier, uint namespaceIndex)
        {
            NodeIdEncodingBits encoding = NodeIdEncodingBits.Numeric;

            switch (idType)
            {
                case IdType.Numeric:
                {
                    uint id = Convert.ToUInt32(identifier, CultureInfo.InvariantCulture);

                    if (id <= Byte.MaxValue && namespaceIndex == 0)
                    {
                        encoding = NodeIdEncodingBits.TwoByte;
                        break;
                    }

                    if (id <= UInt16.MaxValue && namespaceIndex <= Byte.MaxValue)
                    {
                        encoding = NodeIdEncodingBits.FourByte;
                        break;
                    }

                    encoding = NodeIdEncodingBits.Numeric;
                    break;
                }

                case IdType.String:
                {
                    encoding = NodeIdEncodingBits.String;
                    break;
                }

                case IdType.Guid:
                {
                    encoding = NodeIdEncodingBits.Guid;
                    break;
                }

                case IdType.Opaque:
                {
                    encoding = NodeIdEncodingBits.ByteString;
                    break;
                }

                default:
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError,
                        Utils.Format("NodeId identifier type '{0}' not supported.", idType));
                }
            }

            return Convert.ToByte(encoding, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Writes the body of a node id to the stream.
        /// </summary>
        private void WriteNodeIdBody(byte encoding, object identifier, ushort namespaceIndex)
        {
            // write the node id.
            switch ((NodeIdEncodingBits)(0x3F & encoding))
            {
                case NodeIdEncodingBits.TwoByte:
                {
                    WriteByte(null, Convert.ToByte(identifier, CultureInfo.InvariantCulture));
                    break;
                }

                case NodeIdEncodingBits.FourByte:
                {
                    WriteByte(null, Convert.ToByte(namespaceIndex));
                    WriteUInt16(null, Convert.ToUInt16(identifier, CultureInfo.InvariantCulture));
                    break;
                }

                case NodeIdEncodingBits.Numeric:
                {
                    WriteUInt16(null, namespaceIndex);
                    WriteUInt32(null, Convert.ToUInt32(identifier, CultureInfo.InvariantCulture));
                    break;
                }

                case NodeIdEncodingBits.String:
                {
                    WriteUInt16(null, namespaceIndex);
                    WriteString(null, (string)identifier);
                    break;
                }

                case NodeIdEncodingBits.Guid:
                {
                    WriteUInt16(null, namespaceIndex);
                    WriteGuid(null, new Uuid((Guid)identifier));
                    break;
                }

                case NodeIdEncodingBits.ByteString:
                {
                    WriteUInt16(null, namespaceIndex);
                    WriteByteString(null, (byte[])identifier);
                    break;
                }
            }
        }

        /// <summary>
        /// Writes an Variant to the stream.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private void WriteVariantValue(string fieldName, Variant value)
        {
            // check for null.
            if (value.IsNull || value.TypeInfo == null || value.TypeInfo.BuiltInType == BuiltInType.Null)
            {
                WriteByte(null, 0);
                return;
            }

            // encode enums as int32.
            byte encodingByte = (byte)value.TypeInfo.BuiltInType;
            if (value.TypeInfo.BuiltInType == BuiltInType.Enumeration)
            {
                encodingByte = (byte)BuiltInType.Int32;
            }

            object valueToEncode = value.Value;

            if (value.TypeInfo.ValueRank < 0)
            {
                WriteByte(null, encodingByte);

                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean: { WriteBoolean(null, (bool)valueToEncode); return; }
                    case BuiltInType.SByte: { WriteSByte(null, (sbyte)valueToEncode); return; }
                    case BuiltInType.Byte: { WriteByte(null, (byte)valueToEncode); return; }
                    case BuiltInType.Int16: { WriteInt16(null, (short)valueToEncode); return; }
                    case BuiltInType.UInt16: { WriteUInt16(null, (ushort)valueToEncode); return; }
                    case BuiltInType.Int32: { WriteInt32(null, (int)valueToEncode); return; }
                    case BuiltInType.UInt32: { WriteUInt32(null, (uint)valueToEncode); return; }
                    case BuiltInType.Int64: { WriteInt64(null, (long)valueToEncode); return; }
                    case BuiltInType.UInt64: { WriteUInt64(null, (ulong)valueToEncode); return; }
                    case BuiltInType.Float: { WriteFloat(null, (float)valueToEncode); return; }
                    case BuiltInType.Double: { WriteDouble(null, (double)valueToEncode); return; }
                    case BuiltInType.String: { WriteString(null, (string)valueToEncode); return; }
                    case BuiltInType.DateTime: { WriteDateTime(null, (DateTime)valueToEncode); return; }
                    case BuiltInType.Guid: { WriteGuid(null, (Uuid)valueToEncode); return; }
                    case BuiltInType.ByteString: { WriteByteString(null, (byte[])valueToEncode); return; }
                    case BuiltInType.XmlElement: { WriteXmlElement(null, (XmlElement)valueToEncode); return; }
                    case BuiltInType.NodeId: { WriteNodeId(null, (NodeId)valueToEncode); return; }
                    case BuiltInType.ExpandedNodeId: { WriteExpandedNodeId(null, (ExpandedNodeId)valueToEncode); return; }
                    case BuiltInType.StatusCode: { WriteStatusCode(null, (StatusCode)valueToEncode); return; }
                    case BuiltInType.QualifiedName: { WriteQualifiedName(null, (QualifiedName)valueToEncode); return; }
                    case BuiltInType.LocalizedText: { WriteLocalizedText(null, (LocalizedText)valueToEncode); return; }
                    case BuiltInType.ExtensionObject: { WriteExtensionObject(null, (ExtensionObject)valueToEncode); return; }
                    case BuiltInType.DataValue: { WriteDataValue(null, (DataValue)valueToEncode); return; }
                    case BuiltInType.Enumeration: { WriteInt32(null, Convert.ToInt32(valueToEncode, CultureInfo.InvariantCulture)); return; }
                    case BuiltInType.DiagnosticInfo: { WriteDiagnosticInfo(null, (DiagnosticInfo)valueToEncode); break; }
                }

                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "Unexpected type encountered while encoding a Variant: {0}",
                    value.TypeInfo.BuiltInType);
            }

            if (value.TypeInfo.ValueRank >= 0)
            {
                Matrix matrix = null;

                encodingByte |= (byte)VariantArrayEncodingBits.Array;

                if (value.TypeInfo.ValueRank > ValueRanks.OneDimension)
                {
                    encodingByte |= (byte)VariantArrayEncodingBits.ArrayDimensions;
                    matrix = (Matrix)valueToEncode;
                    valueToEncode = matrix.Elements;
                }

                WriteByte(null, encodingByte);

                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean: { WriteBooleanArray(null, (bool[])valueToEncode); break; }
                    case BuiltInType.SByte: { WriteSByteArray(null, (sbyte[])valueToEncode); break; }
                    case BuiltInType.Byte: { WriteByteArray(null, (byte[])valueToEncode); break; }
                    case BuiltInType.Int16: { WriteInt16Array(null, (short[])valueToEncode); break; }
                    case BuiltInType.UInt16: { WriteUInt16Array(null, (ushort[])valueToEncode); break; }
                    case BuiltInType.Int32: { WriteInt32Array(null, (int[])valueToEncode); break; }
                    case BuiltInType.UInt32: { WriteUInt32Array(null, (uint[])valueToEncode); break; }
                    case BuiltInType.Int64: { WriteInt64Array(null, (long[])valueToEncode); break; }
                    case BuiltInType.UInt64: { WriteUInt64Array(null, (ulong[])valueToEncode); break; }
                    case BuiltInType.Float: { WriteFloatArray(null, (float[])valueToEncode); break; }
                    case BuiltInType.Double: { WriteDoubleArray(null, (double[])valueToEncode); break; }
                    case BuiltInType.String: { WriteStringArray(null, (string[])valueToEncode); break; }
                    case BuiltInType.DateTime: { WriteDateTimeArray(null, (DateTime[])valueToEncode); break; }
                    case BuiltInType.Guid: { WriteGuidArray(null, (Uuid[])valueToEncode); break; }
                    case BuiltInType.ByteString: { WriteByteStringArray(null, (byte[][])valueToEncode); break; }
                    case BuiltInType.XmlElement: { WriteXmlElementArray(null, (XmlElement[])valueToEncode); break; }
                    case BuiltInType.NodeId: { WriteNodeIdArray(null, (NodeId[])valueToEncode); break; }
                    case BuiltInType.ExpandedNodeId: { WriteExpandedNodeIdArray(null, (ExpandedNodeId[])valueToEncode); break; }
                    case BuiltInType.StatusCode: { WriteStatusCodeArray(null, (StatusCode[])valueToEncode); break; }
                    case BuiltInType.QualifiedName: { WriteQualifiedNameArray(null, (QualifiedName[])valueToEncode); break; }
                    case BuiltInType.LocalizedText: { WriteLocalizedTextArray(null, (LocalizedText[])valueToEncode); break; }
                    case BuiltInType.ExtensionObject: { WriteExtensionObjectArray(null, (ExtensionObject[])valueToEncode); break; }
                    case BuiltInType.DataValue: { WriteDataValueArray(null, (DataValue[])valueToEncode); break; }

                    case BuiltInType.Enumeration:
                    {
                        // Check whether the value to encode is int array.
                        if (!(valueToEncode is int[] ints))
                        {
                            if (!(valueToEncode is Enum[] enums))
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

                        WriteInt32Array(null, ints);
                        break;
                    }

                    case BuiltInType.Variant:
                    {
                        if (valueToEncode is Variant[] variants)
                        {
                            WriteVariantArray(null, variants);
                            break;
                        }


                        if (valueToEncode is object[] objects)
                        {
                            WriteObjectArray(null, objects);
                            break;
                        }

                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Matrix: {0}",
                            valueToEncode.GetType());
                    }

                    case BuiltInType.DiagnosticInfo: { WriteDiagnosticInfoArray(null, (DiagnosticInfo[])valueToEncode); break; }
                    default:
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Variant: {0}",
                            value.TypeInfo.BuiltInType);
                    }
                }

                // write the dimensions.
                if (value.TypeInfo.ValueRank > ValueRanks.OneDimension)
                {
                    WriteInt32Array(null, (int[])matrix.Dimensions);
                }
            }
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckAndIncrementNestingLevel()
        {
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }
            m_nestingLevel++;
        }
        #endregion

        #region Private Fields
        private Stream m_ostrm;
        private BinaryWriter m_writer;
        private bool m_leaveOpen;
        private IServiceMessageContext m_context;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        #endregion
    }

    #region Internal Enumerations
    /// <summary>
    /// The possible values for the node id encoding byte.
    /// </summary>
    internal enum NodeIdEncodingBits
    {
        TwoByte = 0x00,
        FourByte = 0x01,
        Numeric = 0x02,
        String = 0x03,
        Guid = 0x04,
        ByteString = 0x05
    }

    /// <summary>
    /// The possible values for the diagnostic info encoding byte.
    /// </summary>
    [Flags]
    internal enum DiagnosticInfoEncodingBits
    {
        SymbolicId = 0x01,
        NamespaceUri = 0x02,
        LocalizedText = 0x04,
        Locale = 0x08,
        AdditionalInfo = 0x10,
        InnerStatusCode = 0x20,
        InnerDiagnosticInfo = 0x40,
    }

    /// <summary>
    /// The possible values for the localized text encoding byte.
    /// </summary>
    [Flags]
    internal enum LocalizedTextEncodingBits
    {
        Locale = 0x01,
        Text = 0x02
    }

    /// <summary>
    /// The possible values for the data value encoding byte.
    /// </summary>
    [Flags]
    internal enum DataValueEncodingBits
    {
        Value = 0x01,
        StatusCode = 0x02,
        SourceTimestamp = 0x04,
        ServerTimestamp = 0x08,
        SourcePicoseconds = 0x10,
        ServerPicoseconds = 0x20
    }

    /// <summary>
    /// The possible values for the data value encoding byte.
    /// </summary>
    [Flags]
    internal enum ExtensionObjectEncodingBits
    {
        TypeId = 0x01,
        BinaryBody = 0x02,
        XmlBody = 0x04
    }

    /// <summary>
    /// The possible values for Variant encoding bits.
    /// </summary>
    [Flags]
    internal enum VariantArrayEncodingBits
    {
        Array = 0x80,
        ArrayDimensions = 0x40,
        TypeMask = 0x3F
    }
    #endregion
}
