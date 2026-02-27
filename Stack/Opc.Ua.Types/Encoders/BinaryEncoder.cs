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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;
#if NET5_0_OR_GREATER
using Opc.Ua.Buffers;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Encodes objects in a stream using the UA Binary encoding.
    /// </summary>
    public class BinaryEncoder : IEncoder
    {
        /// <summary>
        /// Creates an encoder that writes to a memory buffer.
        /// </summary>
        public BinaryEncoder(IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<BinaryEncoder>();
            m_ostrm = new MemoryStream();
            m_writer = new BinaryWriter(m_ostrm);
            m_leaveOpen = false;
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Creates an encoder that writes to a fixed size memory buffer.
        /// </summary>
        public BinaryEncoder(
            byte[] buffer,
            int start,
            int count,
            IServiceMessageContext context)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<BinaryEncoder>();
            m_ostrm = new MemoryStream(buffer, start, count);
            m_writer = new BinaryWriter(m_ostrm);
            m_leaveOpen = false;
            m_nestingLevel = 0;
        }

        /// <summary>
        /// Creates an encoder that writes to the stream.
        /// </summary>
        /// <param name="stream">The stream to which the encoder writes.</param>
        /// <param name="context">The message context to use for the encoding.</param>
        /// <param name="leaveOpen">If the stream should be left open on dispose.</param>
        public BinaryEncoder(
            Stream stream,
            IServiceMessageContext context,
            bool leaveOpen)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<BinaryEncoder>();
            m_ostrm = stream ?? throw new ArgumentNullException(nameof(stream));
            m_writer = new BinaryWriter(m_ostrm, Encoding.UTF8, leaveOpen);
            m_leaveOpen = leaveOpen;
            m_nestingLevel = 0;
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

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during encoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being encoded.</param>
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
            get => (int)m_writer.BaseStream.Position;
            set => m_writer.Seek(value, SeekOrigin.Begin);
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
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public static byte[] EncodeMessage(IEncodeable message, IServiceMessageContext context)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // create encoder.
            using var encoder = new BinaryEncoder(context);
            // encode message
            encoder.EncodeMessage(message);

            // close encoder.
            return encoder.CloseAndReturnBuffer();
        }

        /// <summary>
        /// Encodes a message in a stream.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="message"/> is <c>null</c>.</exception>
        public static void EncodeMessage(
            IEncodeable message,
            Stream stream,
            IServiceMessageContext context,
            bool leaveOpen)
        {
            // create encoder.
            using var encoder = new BinaryEncoder(stream, context, leaveOpen);
            // encode message
            encoder.EncodeMessage(message);
        }

        /// <summary>
        /// Encodes a message with its header.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="message"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException"></exception>
        public void EncodeMessage<T>(T message) where T : IEncodeable
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            long start = m_ostrm.Position;

            // convert the namespace uri to an index.
            var typeId = ExpandedNodeId.ToNodeId(message.BinaryEncodingId, Context.NamespaceUris);

            // write the type id.
            WriteNodeId(null, typeId);

            // write the message.
            WriteEncodeable<T>(null, message);

            // check that the max message size was not exceeded.
            if (Context.MaxMessageSize > 0 &&
                Context.MaxMessageSize < (int)(m_ostrm.Position - start))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    Context.MaxMessageSize,
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

        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Binary;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public IServiceMessageContext Context { get; }

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
        /// <exception cref="ServiceResultException"></exception>
        public void WriteString(string fieldName, string value)
        {
            if (value == null)
            {
                WriteInt32(null, -1);
                return;
            }

            if (Context.MaxStringLength > 0 && Context.MaxStringLength < value.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}",
                    Context.MaxStringLength,
                    value.Length);
            }

            int maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            const int maxStackAllocByteCount = 128;
            if (maxByteCount <= maxStackAllocByteCount)
            {
                Span<byte> encoded = stackalloc byte[maxByteCount];
                int count = Encoding.UTF8.GetBytes(value, encoded);
                WriteByteString(null, encoded[..count]);
                return;
            }
#endif

#if NET5_0_OR_GREATER
            const int minByteCountPerBuffer = 256;
            const int maxByteCountPerBuffer = 8192;
            if (maxByteCount > maxByteCountPerBuffer)
            {
                using var bufferWriter = new ArrayPoolBufferWriter<byte>(
                    minByteCountPerBuffer,
                    maxByteCountPerBuffer);
                long count = Encoding.UTF8.GetBytes(value.AsSpan(), bufferWriter);
                WriteByteString(null, bufferWriter.GetReadOnlySequence());
                return;
            }
#endif
            byte[] encodedBytes = ArrayPool<byte>.Shared.Rent(maxByteCount);
            try
            {
                int count = Encoding.UTF8.GetBytes(value, 0, value.Length, encodedBytes, 0);
                WriteInt32(null, count);
                m_writer.Write(encodedBytes, 0, count);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(encodedBytes);
            }
        }

        /// <summary>
        /// Writes a UTC date/time to the stream.
        /// </summary>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            value = CoreUtils.ToOpcUaUniversalTime(value);

            long ticks = value.Ticks;

            // check for max value.
            if (ticks >= DateTime.MaxValue.Ticks)
            {
                ticks = long.MaxValue;
            }
            // check for min value.
            else
            {
                ticks -= CoreUtils.TimeBase.Ticks;

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
            m_writer.Write(value.ToByteArray());
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        public void WriteByteString(string fieldName, ByteString value)
        {
            if (value.IsEmpty)
            {
                WriteInt32(null, -1);
                return;
            }

            WriteByteString(fieldName, value.Span);
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteString(string fieldName, ReadOnlySpan<byte> value)
        {
            // == compares memory reference, comparing to empty means we compare to the default
            // If null array is converted to span the span is default
            if (value == ReadOnlySpan<byte>.Empty)
            {
                WriteInt32(null, -1);
                return;
            }

            if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < value.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    Context.MaxByteStringLength,
                    value.Length);
            }

            WriteInt32(null, value.Length);
            m_writer.Write(value);
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteString(string fieldName, ReadOnlySequence<byte> value)
        {
            if (value.IsEmpty)
            {
                WriteInt32(null, -1);
                return;
            }

            if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < value.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    Context.MaxByteStringLength,
                    value.Length);
            }

            WriteInt32(null, (int)value.Length);
            foreach (ReadOnlyMemory<byte> element in value)
            {
                m_writer.Write(element.Span);
            }
        }

        /// <summary>
        /// Writes an XmlElement to the stream.
        /// </summary>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            if (value.IsEmpty)
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
            if (value.IsNull)
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
            byte encoding = GetNodeIdEncoding(value, namespaceIndex);

            // write the encoding.
            WriteByte(null, encoding);

            // write the node.
            WriteNodeIdBody(encoding, value, namespaceIndex);
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            // write a null node id.
            if (value.IsNull)
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
            byte encoding = GetNodeIdEncoding(value.InnerNodeId, namespaceIndex);

            // add the bit indicating a uri string is encoded as well.
            if (!string.IsNullOrEmpty(value.NamespaceUri))
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
            WriteNodeIdBody(encoding, value.InnerNodeId, namespaceIndex);

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
            if (value.IsNullOrEmpty)
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
                WriteVariantValue(value, false);
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
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExtensionObject(string fieldName, ExtensionObject value)
        {
            // check for null.
            if (value.IsNull)
            {
                WriteNodeId(null, NodeId.Null);
                WriteByte(null, (byte)ExtensionObjectEncoding.None);
                return;
            }

            // write the type id.
            ExpandedNodeId typeId = value.TypeId;

            if (value.TryGetEncodeable(out IEncodeable encodeable))
            {
                typeId = encodeable.BinaryEncodingId;
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

            WriteNodeId(null, localTypeId);

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
            if (value.TryGetAsBinary(out ByteString bytes))
            {
                WriteByteString(null, bytes);
                return;
            }

            // write XML bodies.
            if (value.TryGetAsXml(out XmlElement xml))
            {
                WriteXmlElement(null, xml);
                return;
            }

            // write encodeable bodies.
            if (encodeable == null)
            {
                if (value.Encoding == ExtensionObjectEncoding.None)
                {
                    // nothing more to do for null bodies == None.
                    return;
                }

                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "Cannot encode extension object '{0}'.",
                    value);
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
                using var encoder = new BinaryEncoder(Context);
                encoder.WriteEncodeable(encodeable);
                bytes = ByteString.From(encoder.CloseAndReturnBuffer());
                WriteByteString(null, bytes);
            }
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string fieldName, T value) where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(value, default))
            {
                // create a default object if a null object specified.
                if (!Context.Factory.TryGetEncodeableType<T>(out IEncodeableType activator))
                {
                    throw ServiceResultException.Create(StatusCodes.BadEncodingError,
                        "Cannot create default instance of type T because activator is not registered.");
                }
                value = (T)activator.CreateInstance();
            }
            WriteEncodeable(value);
        }

        /// <inheritdoc/>
        public void WriteEncodeableAsExtensionObject<T>(string fieldName, T value) where T : IEncodeable
        {
            WriteExtensionObject(fieldName, new ExtensionObject(value));
        }

        /// <inheritdoc/>
        public void WriteEnumerated<T>(string fieldName, T value) where T : struct, Enum
        {
            WriteInt32(null, EnumHelper.EnumToInt32(value));
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string fieldName, ArrayOf<bool> values)
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

        /// <inheritdoc/>
        public void WriteSByteArray(string fieldName, ArrayOf<sbyte> values)
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

        /// <inheritdoc/>
        public void WriteByteArray(string fieldName, ArrayOf<byte> values)
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

        /// <inheritdoc/>
        public void WriteInt16Array(string fieldName, ArrayOf<short> values)
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

        /// <inheritdoc/>
        public void WriteUInt16Array(string fieldName, ArrayOf<ushort> values)
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

        /// <inheritdoc/>
        public void WriteInt32Array(string fieldName, ArrayOf<int> values)
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

        /// <inheritdoc/>
        public void WriteUInt32Array(string fieldName, ArrayOf<uint> values)
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

        /// <inheritdoc/>
        public void WriteInt64Array(string fieldName, ArrayOf<long> values)
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

        /// <inheritdoc/>
        public void WriteUInt64Array(string fieldName, ArrayOf<ulong> values)
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

        /// <inheritdoc/>
        public void WriteFloatArray(string fieldName, ArrayOf<float> values)
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

        /// <inheritdoc/>
        public void WriteDoubleArray(string fieldName, ArrayOf<double> values)
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

        /// <inheritdoc/>
        public void WriteStringArray(string fieldName, ArrayOf<string> values)
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

        /// <inheritdoc/>
        public void WriteDateTimeArray(string fieldName, ArrayOf<DateTime> values)
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

        /// <inheritdoc/>
        public void WriteGuidArray(string fieldName, ArrayOf<Uuid> values)
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

        /// <inheritdoc/>
        public void WriteByteStringArray(string fieldName, ArrayOf<ByteString> values)
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

        /// <inheritdoc/>
        public void WriteXmlElementArray(string fieldName, ArrayOf<XmlElement> values)
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

        /// <inheritdoc/>
        public void WriteNodeIdArray(string fieldName, ArrayOf<NodeId> values)
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

        /// <inheritdoc/>
        public void WriteExpandedNodeIdArray(string fieldName, ArrayOf<ExpandedNodeId> values)
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

        /// <inheritdoc/>
        public void WriteStatusCodeArray(string fieldName, ArrayOf<StatusCode> values)
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

        /// <inheritdoc/>
        public void WriteDiagnosticInfoArray(string fieldName, ArrayOf<DiagnosticInfo> values)
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

        /// <inheritdoc/>
        public void WriteQualifiedNameArray(string fieldName, ArrayOf<QualifiedName> values)
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

        /// <inheritdoc/>
        public void WriteLocalizedTextArray(string fieldName, ArrayOf<LocalizedText> values)
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

        /// <inheritdoc/>
        public void WriteVariantArray(string fieldName, ArrayOf<Variant> values)
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

        /// <inheritdoc/>
        public void WriteDataValueArray(string fieldName, ArrayOf<DataValue> values)
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

        /// <inheritdoc/>
        public void WriteExtensionObjectArray(string fieldName, ArrayOf<ExtensionObject> values)
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

        /// <inheritdoc/>
        public void WriteEncodeableArrayAsExtensionObjects<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            WriteExtensionObjectArray(fieldName, values.ConvertAll(v => new ExtensionObject(v)));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteEncodeable(null, values[ii]);
            }
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string fieldName, ArrayOf<T> values)
            where T : struct, Enum
        {
            // write length.
            if (WriteArrayLength(values))
            {
                return;
            }

            // write contents.
            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteEnumerated(null, values[ii]);
            }
        }

        /// <inheritdoc/>
        public void WriteVariantValue(string fieldName, Variant value)
        {
            WriteVariantValue(value, true);
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
        /// Write variant value either in raw mode (just the value)
        /// or with type information.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteVariantValue(Variant value, bool raw)
        {
            // check for null.
            if (value.IsNull ||
                value.TypeInfo.IsUnknown ||
                value.TypeInfo.BuiltInType == BuiltInType.Null)
            {
                if (!raw)
                {
                    WriteByte(null, 0);
                }
                return;
            }

            // encode enums as int32.
            byte encodingByte = (byte)value.TypeInfo.BuiltInType;
            if (value.TypeInfo.BuiltInType == BuiltInType.Enumeration)
            {
                encodingByte = (byte)BuiltInType.Int32;
            }

            if (value.TypeInfo.IsScalar)
            {
                // Write scalar
                if (!raw)
                {
                    WriteByte(null, encodingByte);
                }
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        WriteBoolean(null, value.GetBoolean());
                        return;
                    case BuiltInType.SByte:
                        WriteSByte(null, value.GetSByte());
                        return;
                    case BuiltInType.Byte:
                        WriteByte(null, value.GetByte());
                        return;
                    case BuiltInType.Int16:
                        WriteInt16(null, value.GetInt16());
                        return;
                    case BuiltInType.UInt16:
                        WriteUInt16(null, value.GetUInt16());
                        return;
                    case BuiltInType.Int32:
                        WriteInt32(null, value.GetInt32());
                        return;
                    case BuiltInType.UInt32:
                        WriteUInt32(null, value.GetUInt32());
                        return;
                    case BuiltInType.Int64:
                        WriteInt64(null, value.GetInt64());
                        return;
                    case BuiltInType.UInt64:
                        WriteUInt64(null, value.GetUInt64());
                        return;
                    case BuiltInType.Float:
                        WriteFloat(null, value.GetFloat());
                        return;
                    case BuiltInType.Double:
                        WriteDouble(null, value.GetDouble());
                        return;
                    case BuiltInType.String:
                        WriteString(null, value.GetString());
                        return;
                    case BuiltInType.DateTime:
                        WriteDateTime(null, value.GetDateTime());
                        return;
                    case BuiltInType.Guid:
                        WriteGuid(null, value.GetGuid());
                        return;
                    case BuiltInType.ByteString:
                        WriteByteString(null, value.GetByteString());
                        return;
                    case BuiltInType.XmlElement:
                        WriteXmlElement(null, value.GetXmlElement());
                        return;
                    case BuiltInType.NodeId:
                        WriteNodeId(null, value.GetNodeId());
                        return;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeId(null, value.GetExpandedNodeId());
                        return;
                    case BuiltInType.StatusCode:
                        WriteStatusCode(null, value.GetStatusCode());
                        return;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedName(null, value.GetQualifiedName());
                        return;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedText(null, value.GetLocalizedText());
                        return;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObject(null, value.GetExtensionObject());
                        return;
                    case BuiltInType.DataValue:
                        WriteDataValue(null, value.GetDataValue());
                        return;
                    case BuiltInType.Enumeration:
                        WriteInt32(null, value.GetInt32());
                        return;
                    case BuiltInType.Null:
                    case BuiltInType.Variant:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.DiagnosticInfo:
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Variant: {0}",
                            value.TypeInfo.BuiltInType);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                }
            }
            else if (value.TypeInfo.IsArray)
            {
                // Write arrays

                if (!raw)
                {
                    WriteByte(
                        null,
                        (byte)(encodingByte | (byte)VariantArrayEncodingBits.Array));
                }

                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        WriteBooleanArray(null, value.GetBooleanArray());
                        break;
                    case BuiltInType.SByte:
                        WriteSByteArray(null, value.GetSByteArray());
                        break;
                    case BuiltInType.Byte:
                        WriteByteArray(null, value.GetByteArray());
                        break;
                    case BuiltInType.Int16:
                        WriteInt16Array(null, value.GetInt16Array());
                        break;
                    case BuiltInType.UInt16:
                        WriteUInt16Array(null, value.GetUInt16Array());
                        break;
                    case BuiltInType.Int32:
                        WriteInt32Array(null, value.GetInt32Array());
                        break;
                    case BuiltInType.UInt32:
                        WriteUInt32Array(null, value.GetUInt32Array());
                        break;
                    case BuiltInType.Int64:
                        WriteInt64Array(null, value.GetInt64Array());
                        break;
                    case BuiltInType.UInt64:
                        WriteUInt64Array(null, value.GetUInt64Array());
                        break;
                    case BuiltInType.Float:
                        WriteFloatArray(null, value.GetFloatArray());
                        break;
                    case BuiltInType.Double:
                        WriteDoubleArray(null, value.GetDoubleArray());
                        break;
                    case BuiltInType.String:
                        WriteStringArray(null, value.GetStringArray());
                        break;
                    case BuiltInType.DateTime:
                        WriteDateTimeArray(null, value.GetDateTimeArray());
                        break;
                    case BuiltInType.Guid:
                        WriteGuidArray(null, value.GetGuidArray());
                        break;
                    case BuiltInType.ByteString:
                        WriteByteStringArray(null, value.GetByteStringArray());
                        break;
                    case BuiltInType.XmlElement:
                        WriteXmlElementArray(null, value.GetXmlElementArray());
                        break;
                    case BuiltInType.NodeId:
                        WriteNodeIdArray(null, value.GetNodeIdArray());
                        break;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeIdArray(null, value.GetExpandedNodeIdArray());
                        break;
                    case BuiltInType.StatusCode:
                        WriteStatusCodeArray(null, value.GetStatusCodeArray());
                        break;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedNameArray(null, value.GetQualifiedNameArray());
                        break;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedTextArray(null, value.GetLocalizedTextArray());
                        break;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObjectArray(null, value.GetExtensionObjectArray());
                        break;
                    case BuiltInType.DataValue:
                        WriteDataValueArray(null, value.GetDataValueArray());
                        break;
                    case BuiltInType.Enumeration:
                        WriteInt32Array(null, value.GetInt32Array());
                        break;
                    case BuiltInType.Variant:
                        WriteVariantArray(null, value.GetVariantArray());
                        break;
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.Null:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Variant: {0}",
                            value.TypeInfo.BuiltInType);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                }
            }
            else // Write multi dimensional arrays
            {
                if (!raw)
                {
                    WriteByte(null, (byte)(
                        encodingByte |
                        (byte)VariantArrayEncodingBits.Array |
                        (byte)VariantArrayEncodingBits.ArrayDimensions));
                }
                int[] dim;
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        WriteBooleanArray(null, value.GetBooleanMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.SByte:
                        WriteSByteArray(null, value.GetSByteMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Byte:
                        WriteByteArray(null, value.GetByteMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Int16:
                        WriteInt16Array(null, value.GetInt16Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.UInt16:
                        WriteUInt16Array(null, value.GetUInt16Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Int32:
                        WriteInt32Array(null, value.GetInt32Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.UInt32:
                        WriteUInt32Array(null, value.GetUInt32Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Int64:
                        WriteInt64Array(null, value.GetInt64Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.UInt64:
                        WriteUInt64Array(null, value.GetUInt64Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Float:
                        WriteFloatArray(null, value.GetFloatMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Double:
                        WriteDoubleArray(null, value.GetDoubleMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.String:
                        WriteStringArray(null, value.GetStringMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.DateTime:
                        WriteDateTimeArray(null, value.GetDateTimeMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Guid:
                        WriteGuidArray(null, value.GetGuidMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.ByteString:
                        WriteByteStringArray(null, value.GetByteStringMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.XmlElement:
                        WriteXmlElementArray(null, value.GetXmlElementMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.NodeId:
                        WriteNodeIdArray(null, value.GetNodeIdMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeIdArray(null, value.GetExpandedNodeIdMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.StatusCode:
                        WriteStatusCodeArray(null, value.GetStatusCodeMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedNameArray(null, value.GetQualifiedNameMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedTextArray(null, value.GetLocalizedTextMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObjectArray(null, value.GetExtensionObjectMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.DataValue:
                        WriteDataValueArray(null, value.GetDataValueMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Enumeration:
                        WriteInt32Array(null, value.GetInt32Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Variant:
                        WriteVariantArray(null, value.GetVariantMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.Null:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Variant: {0}",
                            value.TypeInfo.BuiltInType);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                }

                // write the dimensions.
                WriteInt32Array(null, dim);
            }
        }

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
                        m_logger.LogWarning(
                            "InnerDiagnosticInfo dropped because nesting exceeds maximum of {MaxInnerDepth}.",
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
        /// Write the length of an array. Returns true if the array is empty.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private bool WriteArrayLength<T>(ArrayOf<T> values)
        {
            // check for null.
            if (values.IsNull)
            {
                WriteInt32(null, -1);
                return true;
            }

            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength {0} < {1}",
                    Context.MaxArrayLength,
                    values.Count);
            }

            // write length.
            WriteInt32(null, values.Count);
            return values.Count == 0;
        }

        /// <summary>
        /// Returns the node id encoding byte for a node id value.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static byte GetNodeIdEncoding(NodeId nodeId, int namespaceIndex)
        {
            NodeIdEncodingBits encoding;
            switch (nodeId.IdType)
            {
                case IdType.String:
                    encoding = NodeIdEncodingBits.String;
                    break;
                case IdType.Guid:
                    encoding = NodeIdEncodingBits.Guid;
                    break;
                case IdType.Opaque:
                    encoding = NodeIdEncodingBits.ByteString;
                    break;
                default:
                    uint id = Convert.ToUInt32(nodeId.NumericIdentifier, CultureInfo.InvariantCulture);

                    if (id <= byte.MaxValue && namespaceIndex == 0)
                    {
                        encoding = NodeIdEncodingBits.TwoByte;
                        break;
                    }

                    if (id <= ushort.MaxValue && namespaceIndex <= byte.MaxValue)
                    {
                        encoding = NodeIdEncodingBits.FourByte;
                        break;
                    }

                    encoding = NodeIdEncodingBits.Numeric;
                    break;
            }

            return Convert.ToByte(encoding, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Writes the body of a node id to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteNodeIdBody(byte encoding, NodeId nodeId, ushort namespaceIndex)
        {
            // write the node id.
            switch ((NodeIdEncodingBits)(0x3F & encoding))
            {
                case NodeIdEncodingBits.TwoByte:
                    WriteByte(null, unchecked((byte)nodeId.NumericIdentifier));
                    break;
                case NodeIdEncodingBits.FourByte:
                    WriteByte(null, Convert.ToByte(namespaceIndex));
                    WriteUInt16(null, unchecked((ushort)nodeId.NumericIdentifier));
                    break;
                case NodeIdEncodingBits.Numeric:
                    WriteUInt16(null, namespaceIndex);
                    WriteUInt32(null, nodeId.NumericIdentifier);
                    break;
                case NodeIdEncodingBits.String:
                    WriteUInt16(null, namespaceIndex);
                    WriteString(null, nodeId.StringIdentifier);
                    break;
                case NodeIdEncodingBits.Guid:
                    WriteUInt16(null, namespaceIndex);
                    WriteGuid(null, nodeId.GuidIdentifier);
                    break;
                case NodeIdEncodingBits.ByteString:
                    WriteUInt16(null, namespaceIndex);
                    WriteByteString(null, nodeId.OpaqueIdentifer);
                    break;
            }
        }

        /// <summary>
        /// Write encodeable
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        private void WriteEncodeable<T>(T value) where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();
            try
            {
                // encode the object.
                value.Encode(this);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        private Stream m_ostrm;
        private BinaryWriter m_writer;
        private readonly bool m_leaveOpen;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
    }

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
        None = 0,
        SymbolicId = 0x01,
        NamespaceUri = 0x02,
        LocalizedText = 0x04,
        Locale = 0x08,
        AdditionalInfo = 0x10,
        InnerStatusCode = 0x20,
        InnerDiagnosticInfo = 0x40
    }

    /// <summary>
    /// The possible values for the localized text encoding byte.
    /// </summary>
    [Flags]
    internal enum LocalizedTextEncodingBits
    {
        None = 0,
        Locale = 0x01,
        Text = 0x02
    }

    /// <summary>
    /// The possible values for the data value encoding byte.
    /// </summary>
    [Flags]
    internal enum DataValueEncodingBits
    {
        None = 0,
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
        None = 0,
        TypeId = 0x01,
        BinaryBody = 0x02,
        XmlBody = 0x04
    }

    /// <summary>
    /// The possible values for Variant encoding bits.
    /// </summary>
    [Flags]
    internal enum VariantArrayEncodingBits : byte
    {
        None = 0,
        TypeBit0 = 0x01,
        TypeBit1 = 0x02,
        TypeBit2 = 0x04,
        TypeBit3 = 0x08,
        TypeBit4 = 0x10,
        TypeBit5 = 0x20,
        TypeMask = TypeBit0 | TypeBit1 | TypeBit2 | TypeBit3 | TypeBit4 | TypeBit5,
        ArrayDimensions = 0x40,
        Array = 0x80
    }
}
