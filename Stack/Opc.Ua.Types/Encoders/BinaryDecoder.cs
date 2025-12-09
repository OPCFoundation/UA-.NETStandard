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
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;
#if NET6_0_OR_GREATER
using System.Buffers;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Decodes objects from a UA Binary encoded stream.
    /// </summary>
    public class BinaryDecoder : IDecoder
    {
        /// <summary>
        /// Creates a decoder that reads from a memory buffer.
        /// </summary>
        public BinaryDecoder(
            byte[] buffer,
            IServiceMessageContext context)
            : this(buffer, 0, buffer.Length, context)
        {
        }

        /// <summary>
        /// Creates a decoder that reads from an ArraySegment.
        /// </summary>
        public BinaryDecoder(
            ArraySegment<byte> buffer,
            IServiceMessageContext context)
            : this(buffer.Array, buffer.Offset, buffer.Count, context)
        {
        }

        /// <summary>
        /// Creates a decoder that reads from a memory buffer.
        /// </summary>
        public BinaryDecoder(
            byte[] buffer,
            int start,
            int count,
            IServiceMessageContext context)
        {
            m_logger = context.Telemetry.CreateLogger<BinaryDecoder>();
            var stream = new MemoryStream(buffer, start, count, false);
            m_reader = new BinaryReader(stream);
            Context = context;
        }

        /// <summary>
        /// Creates a decoder that reads from a stream.
        /// </summary>
        public BinaryDecoder(
            Stream stream,
            IServiceMessageContext context,
            bool leaveOpen = false)
        {
            m_logger = context.Telemetry.CreateLogger<BinaryDecoder>();
            ValidateStreamRequirements(stream);
            m_reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
            Context = context;
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
        /// Initializes the tables used to map namespace and server uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being decoded.</param>
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
        /// Completes reading and closes the stream.
        /// </summary>
        public void Close()
        {
            m_reader.Close();
        }

        /// <summary>
        /// Returns the current position in the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public int Position
        {
            get
            {
                Stream stream = BaseStream;
                if (stream?.CanSeek != true)
                {
#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Stream does not support seeking.");
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations
                }
                long position = stream?.Position ?? 0;
                if (position is > int.MaxValue or < int.MinValue)
                {
#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Stream Position exceeds int.MaxValue or int.MinValue.");
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations
                }
                return (int)position;
            }
        }

        /// <summary>
        /// Gets the stream that the decoder is reading from.
        /// </summary>
        public Stream BaseStream => m_reader?.BaseStream;

        /// <summary>
        /// Decodes a message from a stream.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <c>null</c>.</exception>
        public static IEncodeable DecodeMessage(
            Stream stream,
            Type expectedType,
            IServiceMessageContext context)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var decoder = new BinaryDecoder(stream, context);
            return decoder.DecodeMessage(expectedType);
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <c>null</c>.</exception>
        public static IEncodeable DecodeMessage(
            byte[] buffer,
            Type expectedType,
            IServiceMessageContext context)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var decoder = new BinaryDecoder(buffer, context);
            return decoder.DecodeMessage(expectedType);
        }

        /// <summary>
        /// Decodes an object from a buffer.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public IEncodeable DecodeMessage(Type expectedType)
        {
            int start = Position;

            // read the node id.
            NodeId typeId = ReadNodeId(null);

            // convert to absolute node id.
            var absoluteId = NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris);

            // lookup message type.
            Type actualType =
                Context.Factory.GetSystemType(absoluteId)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode message with type id: {0}.",
                    absoluteId);

            // read the message.
            IEncodeable message = ReadEncodeable(null, actualType, absoluteId);

            // check that the max message size was not exceeded.
            int messageLength = Position - start;
            if (Context.MaxMessageSize > 0 && Context.MaxMessageSize < messageLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    Context.MaxMessageSize,
                    messageLength);
            }

            // return the message.
            return message;
        }

        /// <summary>
        /// Loads a string table from a binary stream.
        /// </summary>
        public bool LoadStringTable(StringTable stringTable)
        {
            int count = SafeReadInt32();

            if (count < -0)
            {
                return false;
            }

            for (uint ii = 0; ii < count; ii++)
            {
                stringTable.Append(ReadString(null));
            }

            return true;
        }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Binary;

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri)
        {
            // not used in the binary encoding.
        }

        /// <inheritdoc/>
        public void PopNamespace()
        {
            // not used in the binary encoding.
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string fieldName)
        {
            return SafeReadBoolean();
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string fieldName)
        {
            return SafeReadSByte();
        }

        /// <inheritdoc/>
        public byte ReadByte(string fieldName)
        {
            return SafeReadByte();
        }

        /// <inheritdoc/>
        public short ReadInt16(string fieldName)
        {
            return SafeReadInt16();
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string fieldName)
        {
            return SafeReadUInt16();
        }

        /// <inheritdoc/>
        public int ReadInt32(string fieldName)
        {
            return SafeReadInt32();
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string fieldName)
        {
            return SafeReadUInt32();
        }

        /// <inheritdoc/>
        public long ReadInt64(string fieldName)
        {
            return SafeReadInt64();
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string fieldName)
        {
            return SafeReadUInt64();
        }

        /// <inheritdoc/>
        public float ReadFloat(string fieldName)
        {
            return SafeReadFloat();
        }

        /// <inheritdoc/>
        public double ReadDouble(string fieldName)
        {
            return SafeReadDouble();
        }

        /// <inheritdoc/>
        public string ReadString(string fieldName)
        {
            return ReadString(fieldName, Context.MaxStringLength);
        }

        /// <summary>
        /// Reads a string from the stream (throws an exception if
        /// its length is invalid or exceeds the limit specified).
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public string ReadString(string fieldName, int maxStringLength)
        {
            int length = SafeReadInt32();

            if (length < 0)
            {
                return null;
            }

            if (length == 0)
            {
                return string.Empty;
            }

            if (maxStringLength > 0 && maxStringLength < length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}",
                    maxStringLength,
                    length);
            }

            // length is always >= 1 here
#if NET6_0_OR_GREATER
            const int maxStackAlloc = 1024;
            byte[] buffer = null;
            try
            {
                Span<byte> bytes =
                    length <= maxStackAlloc
                        ? stackalloc byte[length]
                        : (buffer = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

                // throws decoding error if length is not met
                int utf8StringLength = SafeReadCharBytes(bytes);

                // If 0 terminated, decrease length to remove 0 terminators before converting to string
                while (utf8StringLength > 0 && bytes[utf8StringLength - 1] == 0)
                {
                    utf8StringLength--;
                }
                return Encoding.UTF8.GetString(bytes[..utf8StringLength]);
            }
            finally
            {
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
#else
            byte[] bytes = SafeReadBytes(length);

            // If 0 terminated, decrease length to remove 0 terminators before converting to string
            int utf8StringLength = bytes.Length;
            while (utf8StringLength > 0 && bytes[utf8StringLength - 1] == 0)
            {
                utf8StringLength--;
            }
            return Encoding.UTF8.GetString(bytes, 0, utf8StringLength);
#endif
        }

        /// <inheritdoc/>
        public DateTime ReadDateTime(string fieldName)
        {
            long ticks = SafeReadInt64();

            if (ticks >= (long.MaxValue - CoreUtils.TimeBase.Ticks))
            {
                return DateTime.MaxValue;
            }

            ticks += CoreUtils.TimeBase.Ticks;

            if (ticks >= DateTime.MaxValue.Ticks)
            {
                return DateTime.MaxValue;
            }

            if (ticks <= CoreUtils.TimeBase.Ticks)
            {
                return DateTime.MinValue;
            }

            return new DateTime(ticks, DateTimeKind.Utc);
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string fieldName)
        {
            const int kGuidLength = 16;
            byte[] bytes = SafeReadBytes(kGuidLength);
            return new Uuid(new Guid(bytes));
        }

        /// <inheritdoc/>
        public byte[] ReadByteString(string fieldName)
        {
            return ReadByteString(fieldName, Context.MaxByteStringLength);
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public byte[] ReadByteString(string fieldName, int maxByteStringLength)
        {
            int length = SafeReadInt32();

            if (length < 0)
            {
                return null;
            }

            if (maxByteStringLength > 0 && maxByteStringLength < length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    maxByteStringLength,
                    length);
            }

            return SafeReadBytes(length);
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string fieldName)
        {
            byte[] bytes = ReadByteString(fieldName);

            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            var document = new XmlDocument();

            try
            {
                // If 0 terminated, decrease length before converting to string
                int utf8StringLength = bytes.Length;
                while (utf8StringLength > 0 && bytes[utf8StringLength - 1] == 0)
                {
                    utf8StringLength--;
                }
                string xmlString = Encoding.UTF8.GetString(bytes, 0, utf8StringLength);
                using var stream = new StringReader(xmlString);
                using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
                document.Load(reader);
            }
            catch (XmlException)
            {
                return null;
            }

            return document.DocumentElement;
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string fieldName)
        {
            byte encodingByte = SafeReadByte();

            var value = new NodeId();

            ReadNodeIdBody(encodingByte, value);

            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex)
            {
                value.SetNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
            }

            return value;
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string fieldName)
        {
            byte encodingByte = SafeReadByte();

            var value = new ExpandedNodeId();

            var body = new NodeId();
            ReadNodeIdBody(encodingByte, body);
            value.InnerNodeId = body;

            // read the namespace uri if present.
            if ((encodingByte & 0x80) != 0)
            {
                string namespaceUri = ReadString(null);
                value.SetNamespaceUri(namespaceUri);
            }

            // read the server index if present.
            if ((encodingByte & 0x40) != 0)
            {
                uint serverIndex = SafeReadUInt32();
                value.SetServerIndex(serverIndex);
            }

            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex)
            {
                value.SetNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
            }

            if (m_serverMappings != null && m_serverMappings.Length > value.ServerIndex)
            {
                value.SetServerIndex(m_serverMappings[value.NamespaceIndex]);
            }

            return value;
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string fieldName)
        {
            return SafeReadUInt32();
        }

        /// <inheritdoc/>
        public DiagnosticInfo ReadDiagnosticInfo(string fieldName)
        {
            return ReadDiagnosticInfo(fieldName, 0);
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string fieldName)
        {
            ushort namespaceIndex = ReadUInt16(null);
            string name = ReadString(null);

            if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
            {
                namespaceIndex = m_namespaceMappings[namespaceIndex];
            }

            return new QualifiedName(name, namespaceIndex);
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string fieldName)
        {
            // read the encoding byte.
            byte encodingByte = SafeReadByte();

            string text = null;
            string locale = null;

            // read the fields of the diagnostic info structure.
            if ((encodingByte & (byte)LocalizedTextEncodingBits.Locale) != 0)
            {
                locale = ReadString(null);
            }

            if ((encodingByte & (byte)LocalizedTextEncodingBits.Text) != 0)
            {
                text = ReadString(null);
            }

            return new LocalizedText(locale, text);
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string fieldName)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                return ReadVariantValue(fieldName);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public DataValue ReadDataValue(string fieldName)
        {
            // read the encoding byte.
            byte encodingByte = SafeReadByte();

            var value = new DataValue();

            // read the fields of the DataValue structure.
            if ((encodingByte & (byte)DataValueEncodingBits.Value) != 0)
            {
                value.WrappedValue = ReadVariant(null);
            }

            if ((encodingByte & (byte)DataValueEncodingBits.StatusCode) != 0)
            {
                value.StatusCode = ReadStatusCode(null);
            }

            ushort sourcePicoseconds = 0;
            bool hasPicoseconds = (encodingByte &
                (byte)DataValueEncodingBits.SourcePicoseconds) != 0;
            if ((encodingByte & (byte)DataValueEncodingBits.SourceTimestamp) != 0)
            {
                value.SourceTimestamp = ReadDateTime(null);
                if (hasPicoseconds)
                {
                    sourcePicoseconds = ReadUInt16(null);
                }
            }
            else if (hasPicoseconds)
            {
                _ = ReadUInt16(null);
            }
            value.SourcePicoseconds = sourcePicoseconds;

            ushort serverPicoseconds = 0;
            hasPicoseconds = (encodingByte & (byte)DataValueEncodingBits.ServerPicoseconds) != 0;
            if ((encodingByte & (byte)DataValueEncodingBits.ServerTimestamp) != 0)
            {
                value.ServerTimestamp = ReadDateTime(null);
                if (hasPicoseconds)
                {
                    serverPicoseconds = ReadUInt16(null);
                }
            }
            else if (hasPicoseconds)
            {
                _ = ReadUInt16(null);
            }
            value.ServerPicoseconds = serverPicoseconds;

            return value;
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string fieldName)
        {
            return ReadExtensionObject();
        }

        /// <inheritdoc/>
        public IEncodeable ReadEncodeable(
            string fieldName,
            Type systemType,
            ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null)
            {
                throw new ArgumentNullException(nameof(systemType));
            }

            if (Activator.CreateInstance(systemType) is not IEncodeable encodeable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode type '{0}'.",
                    systemType.FullName);
            }

            if (encodeableTypeId != null)
            {
                // set type identifier for custom complex data types before decode.

                if (encodeable is IComplexTypeInstance complexTypeInstance)
                {
                    complexTypeInstance.TypeId = encodeableTypeId;
                }
            }

            CheckAndIncrementNestingLevel();

            try
            {
                encodeable.Decode(this);
            }
            finally
            {
                m_nestingLevel--;
            }

            return encodeable;
        }

        /// <inheritdoc/>
        public Enum ReadEnumerated(string fieldName, Type enumType)
        {
            return (Enum)Enum.ToObject(enumType, SafeReadInt32());
        }

        /// <inheritdoc/>
        public BooleanCollection ReadBooleanArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new BooleanCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadBoolean(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public SByteCollection ReadSByteArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new SByteCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadSByte());
            }

            return values;
        }

        /// <inheritdoc/>
        public ByteCollection ReadByteArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new ByteCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadByte());
            }

            return values;
        }

        /// <inheritdoc/>
        public Int16Collection ReadInt16Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new Int16Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadInt16(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public UInt16Collection ReadUInt16Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new UInt16Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadUInt16(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public Int32Collection ReadInt32Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new Int32Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadInt32());
            }

            return values;
        }

        /// <inheritdoc/>
        public UInt32Collection ReadUInt32Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new UInt32Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadUInt32());
            }

            return values;
        }

        /// <inheritdoc/>
        public Int64Collection ReadInt64Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new Int64Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadInt64());
            }

            return values;
        }

        /// <inheritdoc/>
        public UInt64Collection ReadUInt64Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new UInt64Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadUInt64());
            }

            return values;
        }

        /// <inheritdoc/>
        public FloatCollection ReadFloatArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new FloatCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadFloat());
            }

            return values;
        }

        /// <inheritdoc/>
        public DoubleCollection ReadDoubleArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new DoubleCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadDouble());
            }

            return values;
        }

        /// <inheritdoc/>
        public StringCollection ReadStringArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new StringCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadString(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public DateTimeCollection ReadDateTimeArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new DateTimeCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadDateTime(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public UuidCollection ReadGuidArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new UuidCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadGuid(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public ByteStringCollection ReadByteStringArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new ByteStringCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadByteString(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public XmlElementCollection ReadXmlElementArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new XmlElementCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadXmlElement(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public NodeIdCollection ReadNodeIdArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new NodeIdCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadNodeId(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public ExpandedNodeIdCollection ReadExpandedNodeIdArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new ExpandedNodeIdCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadExpandedNodeId(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public StatusCodeCollection ReadStatusCodeArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new StatusCodeCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadStatusCode(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public DiagnosticInfoCollection ReadDiagnosticInfoArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new DiagnosticInfoCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadDiagnosticInfo(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public QualifiedNameCollection ReadQualifiedNameArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new QualifiedNameCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadQualifiedName(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public LocalizedTextCollection ReadLocalizedTextArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new LocalizedTextCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadLocalizedText(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public VariantCollection ReadVariantArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new VariantCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadVariant(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public DataValueCollection ReadDataValueArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new DataValueCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadDataValue(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public ExtensionObjectCollection ReadExtensionObjectArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = new ExtensionObjectCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadExtensionObject(null));
            }

            return values;
        }

        /// <inheritdoc/>
        public Array ReadEncodeableArray(
            string fieldName,
            Type systemType,
            ExpandedNodeId encodeableTypeId = null)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = Array.CreateInstance(systemType, length);

            for (int ii = 0; ii < length; ii++)
            {
                values.SetValue(ReadEncodeable(null, systemType, encodeableTypeId), ii);
            }

            return values;
        }

        /// <inheritdoc/>
        public Array ReadEnumeratedArray(string fieldName, Type enumType)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            var values = Array.CreateInstance(enumType, length);

            for (int ii = 0; ii < length; ii++)
            {
                values.SetValue(ReadEnumerated(null, enumType), ii);
            }

            return values;
        }

        /// <inheritdoc/>
        public Array ReadArray(
            string fieldName,
            int valueRank,
            BuiltInType builtInType,
            Type systemType = null,
            ExpandedNodeId encodeableTypeId = null)
        {
            if (valueRank == ValueRanks.OneDimension)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        return ReadBooleanArray(fieldName)?.ToArray();
                    case BuiltInType.SByte:
                        return ReadSByteArray(fieldName)?.ToArray();
                    case BuiltInType.Byte:
                        return ReadByteArray(fieldName)?.ToArray();
                    case BuiltInType.Int16:
                        return ReadInt16Array(fieldName)?.ToArray();
                    case BuiltInType.UInt16:
                        return ReadUInt16Array(fieldName)?.ToArray();
                    case BuiltInType.Enumeration:
                        DetermineIEncodeableSystemType(ref systemType, encodeableTypeId);
                        if (systemType?.IsEnum == true)
                        {
                            return ReadEnumeratedArray(fieldName, systemType);
                        }
                        // if system type is not known or not an enum, fall back to Int32
                        goto case BuiltInType.Int32;
                    case BuiltInType.Int32:
                        return ReadInt32Array(fieldName)?.ToArray();
                    case BuiltInType.UInt32:
                        return ReadUInt32Array(fieldName)?.ToArray();
                    case BuiltInType.Int64:
                        return ReadInt64Array(fieldName)?.ToArray();
                    case BuiltInType.UInt64:
                        return ReadUInt64Array(fieldName)?.ToArray();
                    case BuiltInType.Float:
                        return ReadFloatArray(fieldName)?.ToArray();
                    case BuiltInType.Double:
                        return ReadDoubleArray(fieldName)?.ToArray();
                    case BuiltInType.String:
                        return ReadStringArray(fieldName)?.ToArray();
                    case BuiltInType.DateTime:
                        return ReadDateTimeArray(fieldName)?.ToArray();
                    case BuiltInType.Guid:
                        return ReadGuidArray(fieldName)?.ToArray();
                    case BuiltInType.ByteString:
                        return ReadByteStringArray(fieldName)?.ToArray();
                    case BuiltInType.XmlElement:
                        return ReadXmlElementArray(fieldName)?.ToArray();
                    case BuiltInType.NodeId:
                        return ReadNodeIdArray(fieldName)?.ToArray();
                    case BuiltInType.ExpandedNodeId:
                        return ReadExpandedNodeIdArray(fieldName)?.ToArray();
                    case BuiltInType.StatusCode:
                        return ReadStatusCodeArray(fieldName)?.ToArray();
                    case BuiltInType.QualifiedName:
                        return ReadQualifiedNameArray(fieldName)?.ToArray();
                    case BuiltInType.LocalizedText:
                        return ReadLocalizedTextArray(fieldName)?.ToArray();
                    case BuiltInType.DataValue:
                        return ReadDataValueArray(fieldName)?.ToArray();
                    case BuiltInType.Variant:
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }
                        return ReadVariantArray(fieldName)?.ToArray();
                    case BuiltInType.ExtensionObject:
                        return ReadExtensionObjectArray(fieldName)?.ToArray();
                    case BuiltInType.DiagnosticInfo:
                        return ReadDiagnosticInfoArray(fieldName)?.ToArray();
                    case BuiltInType.Null:
                        // For null arrays, read the array length and return object array with null elements
                        int nullArrayLength = ReadArrayLength();
                        if (nullArrayLength < 0)
                        {
                            return null;
                        }
                        return new object[nullArrayLength];
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
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {builtInType}");
                }
            }

            // two or more dimensions
            if (valueRank >= ValueRanks.TwoDimensions)
            {
                // read dimensions array
                Int32Collection dimensions = ReadInt32Array(null);
                if (dimensions != null && dimensions.Count > 0)
                {
                    //int length;
                    (_, int length) = Matrix.ValidateDimensions(
                        false,
                        dimensions,
                        Context.MaxArrayLength,
                        m_logger);

                    // read the elements
                    Array elements = null;
                    if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                    {
                        elements = Array.CreateInstance(systemType, length);
                        for (int i = 0; i < length; i++)
                        {
                            IEncodeable element = ReadEncodeable(
                                null,
                                systemType,
                                encodeableTypeId);
                            elements.SetValue(Convert.ChangeType(
                                element,
                                systemType,
                                CultureInfo.InvariantCulture), i);
                        }
                    }

                    elements ??= ReadArrayElements(length, builtInType);

                    if (elements == null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unexpected null Array for multidimensional matrix with {0} elements.",
                            length);
                    }

                    if (builtInType == BuiltInType.Enumeration && systemType?.IsEnum == true)
                    {
                        var newElements = Array.CreateInstance(systemType, elements.Length);
                        int ii = 0;
                        foreach (object element in elements)
                        {
                            newElements.SetValue(Enum.ToObject(systemType, element), ii++);
                        }
                        elements = newElements;
                    }

                    return new Matrix(elements, builtInType, [.. dimensions]).ToArray();
                }
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unexpected null or empty Dimensions for multidimensional matrix.");
            }
            return null;
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
        /// Reads a DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfo nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private DiagnosticInfo ReadDiagnosticInfo(string fieldName, int depth)
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
                // read the encoding byte.
                byte encodingByte = SafeReadByte();

                // check if the diagnostic info is null.
                if (encodingByte == 0)
                {
                    return null;
                }

                var value = new DiagnosticInfo();

                // read the fields of the diagnostic info structure.
                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.SymbolicId) != 0)
                {
                    value.SymbolicId = SafeReadInt32();
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.NamespaceUri) != 0)
                {
                    value.NamespaceUri = SafeReadInt32();
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.Locale) != 0)
                {
                    value.Locale = SafeReadInt32();
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.LocalizedText) != 0)
                {
                    value.LocalizedText = SafeReadInt32();
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.AdditionalInfo) != 0)
                {
                    value.AdditionalInfo = ReadString(null);
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.InnerStatusCode) != 0)
                {
                    value.InnerStatusCode = ReadStatusCode(null);
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.InnerDiagnosticInfo) != 0)
                {
                    value.InnerDiagnosticInfo = ReadDiagnosticInfo(null, depth + 1) ??
                        new DiagnosticInfo();
                }

                return value;
            }
            finally
            {
                m_nestingLevel--;
            }
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
            if (encodeableTypeId != null && systemType == null)
            {
                systemType = Context.Factory.GetSystemType(encodeableTypeId);
            }
            return typeof(IEncodeable).IsAssignableFrom(systemType);
        }

        /// <summary>
        /// Reads and returns an array of elements of the specified length and builtInType
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Array ReadArrayElements(int length, BuiltInType builtInType)
        {
            Array array;
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                {
                    bool[] values = new bool[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadBoolean();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.SByte:
                {
                    sbyte[] values = new sbyte[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadSByte();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Byte:
                {
                    byte[] values = new byte[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadByte();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Int16:
                {
                    short[] values = new short[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadInt16();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.UInt16:
                {
                    ushort[] values = new ushort[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadUInt16();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Int32:
                case BuiltInType.Enumeration:
                {
                    int[] values = new int[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadInt32();
                    }
                    array = values;
                    break;
                }
                case BuiltInType.UInt32:
                {
                    uint[] values = new uint[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadUInt32();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Int64:
                {
                    long[] values = new long[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadInt64();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.UInt64:
                {
                    ulong[] values = new ulong[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadUInt64();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Float:
                {
                    float[] values = new float[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadFloat();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Double:
                {
                    double[] values = new double[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = SafeReadDouble();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.String:
                {
                    string[] values = new string[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadString(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.DateTime:
                {
                    var values = new DateTime[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadDateTime(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Guid:
                {
                    var values = new Uuid[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadGuid(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.ByteString:
                {
                    byte[][] values = new byte[length][];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadByteString(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.XmlElement:
                {
                    var values = new XmlElement[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        try
                        {
                            values[ii] = ReadXmlElement(null);
                        }
                        catch (ServiceResultException)
                        {
                            // fatal decoding error
                            throw;
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(ex, "Error reading array of XmlElement.");
                            values[ii] = null;
                        }
                    }

                    array = values;
                    break;
                }
                case BuiltInType.NodeId:
                {
                    var values = new NodeId[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadNodeId(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.ExpandedNodeId:
                {
                    var values = new ExpandedNodeId[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadExpandedNodeId(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.StatusCode:
                {
                    var values = new StatusCode[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadStatusCode(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.QualifiedName:
                {
                    var values = new QualifiedName[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadQualifiedName(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.LocalizedText:
                {
                    var values = new LocalizedText[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadLocalizedText(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.ExtensionObject:
                {
                    var values = new ExtensionObject[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadExtensionObject();
                    }

                    array = values;
                    break;
                }
                case BuiltInType.DataValue:
                {
                    var values = new DataValue[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadDataValue(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Variant:
                {
                    var values = new Variant[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadVariant(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.DiagnosticInfo:
                {
                    var values = new DiagnosticInfo[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadDiagnosticInfo(null);
                    }

                    array = values;
                    break;
                }
                case BuiltInType.Null:
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Cannot decode unknown type in Variant object with BuiltInType: {0}.",
                        builtInType);
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {builtInType}");
            }

            return array;
        }

        /// <summary>
        /// Reads the length of an array.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private int ReadArrayLength([CallerMemberName] string callerMemberName = "")
        {
            int length = SafeReadInt32();

            if (length < 0)
            {
                return -1;
            }

            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength exceeded in {0}: {1} < {2}",
                    callerMemberName,
                    Context.MaxArrayLength,
                    length);
            }

            return length;
        }

        /// <summary>
        /// Reads the body of a node id.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ReadNodeIdBody(byte encodingByte, NodeId value)
        {
            switch ((NodeIdEncodingBits)(encodingByte & 0x3F))
            {
                case NodeIdEncodingBits.TwoByte:
                    value.SetNamespaceIndex(0);
                    value.SetIdentifier(IdType.Numeric, (uint)SafeReadByte());
                    break;
                case NodeIdEncodingBits.FourByte:
                    value.SetNamespaceIndex(SafeReadByte());
                    value.SetIdentifier(IdType.Numeric, (uint)SafeReadUInt16());
                    break;
                case NodeIdEncodingBits.Numeric:
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.Numeric, SafeReadUInt32());
                    break;
                case NodeIdEncodingBits.String:
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.String, ReadString(null));
                    break;
                case NodeIdEncodingBits.Guid:
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.Guid, (Guid)ReadGuid(null));
                    break;
                case NodeIdEncodingBits.ByteString:
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.Opaque, ReadByteString(null));
                    break;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Invalid encoding byte (0x{0:X2}) for NodeId.",
                        encodingByte);
            }
        }

        /// <summary>
        /// Reads an extension object from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ExtensionObject ReadExtensionObject()
        {
            var extension = new ExtensionObject();

            // read type id.
            NodeId typeId = ReadNodeId(null);

            // convert to absolute node id.
            extension.TypeId = NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris);

            if (!NodeId.IsNull(typeId) && NodeId.IsNull(extension.TypeId))
            {
                m_logger.LogWarning(
                    "Cannot deserialize extension objects if the NamespaceUri is not in the NamespaceTable: Type = {Type}",
                    typeId);
            }

            // read encoding.
            byte encoding = SafeReadByte();

            // nothing more to do for empty bodies.
            if (encoding == (byte)ExtensionObjectEncoding.None)
            {
                return extension;
            }

            if (encoding is not ((byte)ExtensionObjectEncoding.Binary) and not ((byte)ExtensionObjectEncoding.Xml))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Invalid encoding byte (0x{0:X2}) for ExtensionObject.",
                    encoding);
            }

            // check for known type.
            Type systemType = Context.Factory.GetSystemType(extension.TypeId);

            // check for XML bodies.
            if (encoding == (byte)ExtensionObjectEncoding.Xml)
            {
                extension.Body = ReadXmlElement(null);

                // attempt to decode a known type.
                if (systemType != null && extension.Body != null)
                {
                    var element = extension.Body as XmlElement;
                    using var xmlDecoder = new XmlDecoder(element, Context);
                    try
                    {
                        xmlDecoder.PushNamespace(element.NamespaceURI);
                        IEncodeable body = xmlDecoder.ReadEncodeable(
                            element.LocalName,
                            systemType,
                            extension.TypeId);
                        xmlDecoder.PopNamespace();

                        // update body.
                        extension.Body = body;

                        xmlDecoder.Close();
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(
                            "Could not decode known type {Name} encoded as Xml. Error={Message}, Value={OuterXml}",
                            systemType.FullName,
                            e.Message,
                            element.OuterXml);
                    }
                }

                return extension;
            }

            // Get the length.
            // Allow a length of -1 to support legacy devices that don't fill the length correctly
            int length = SafeReadInt32();

            // save the current position.
            int start = Position;

            // create instance of type.
            IEncodeable encodeable = null;
            if (systemType != null && length >= -1)
            {
                encodeable = Activator.CreateInstance(systemType) as IEncodeable;

                // set type identifier for custom complex data types before decode.
                if (encodeable is IComplexTypeInstance complexTypeInstance)
                {
                    complexTypeInstance.TypeId = extension.TypeId;
                }
            }

            // process known type.
            if (encodeable != null)
            {
                bool resetStream = true;
                string errorMessage = string.Empty;
                Exception exception = null;
                uint nestingLevel = m_nestingLevel;

                CheckAndIncrementNestingLevel();

                try
                {
                    // decode body.
                    encodeable.Decode(this);

                    // verify the decoder did not exceed the length of the encodeable object
                    int used = Position - start;
                    if (length >= 0 && length != used)
                    {
                        errorMessage = "Length mismatch";
                        exception = null;
                    }
                    else
                    {
                        // success!
                        resetStream = false;
                    }
                }
                catch (EndOfStreamException eofStream)
                {
                    errorMessage = "End of stream";
                    exception = eofStream;
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode is
                    StatusCodes.BadEncodingLimitsExceeded or
                    StatusCodes.BadDecodingError)
                {
                    errorMessage = sre.Message;
                    exception = sre;
                }
                finally
                {
                    m_nestingLevel = nestingLevel;
                }

                if (resetStream)
                {
                    // type was known but decoding failed,
                    // reset stream to return ExtensionObject if configured to do so!
                    // decoding failure of a known type in ns=0 is always a decoding error.
                    if (typeId.NamespaceIndex == 0 ||
                        m_encodeablesRecovered >= Context.MaxDecoderRecoveries)
                    {
                        throw exception
                            ?? ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "{0}, failed to decode encodeable type '{1}', NodeId='{2}'.",
                                errorMessage,
                                systemType.Name,
                                extension.TypeId);
                    }
                    else if (m_encodeablesRecovered == 0)
                    {
                        // log the error only once to avoid flooding the log.
                        m_logger.LogWarning(
                            exception,
                            "{Message}, failed to decode encodeable type '{Name}', NodeId='{NodeId}'. BinaryDecoder recovered.",
                            errorMessage,
                            systemType.Name,
                            extension.TypeId);
                    }

                    // reset the stream to the begin of the ExtensionObject body.
                    m_reader.BaseStream.Position = start;
                    encodeable = null;

                    // count number of recoveries
                    m_encodeablesRecovered++;
                }
            }

            // process unknown type.
            if (encodeable == null)
            {
                // figure out how long the object is.
                if (length < 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Cannot determine length of unknown extension object body with type '{0}'.",
                        extension.TypeId);
                }

                // check the length.
                if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < length)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "MaxByteStringLength exceeded in ExtensionObject: {0} < {1}",
                        Context.MaxByteStringLength,
                        length);
                }

                // read the bytes of the body.
                extension.Body = SafeReadBytes(length);

                return extension;
            }

            // any unread data indicates a decoding error.
            if (length >= 0)
            {
                long unused = length - (Position - start);
                if (unused > 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Cannot skip {0} bytes of unknown extension object body with type '{1}'.",
                        unused,
                        extension.TypeId);
                }
            }

            // Set the known TypeId for encodeables.
            extension.TypeId = encodeable.TypeId;
            extension.Body = encodeable;
            return extension;
        }

        /// <summary>
        /// Reads an Variant from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadVariantValue(string fieldName)
        {
            // read the encoding byte.
            byte encodingByte = SafeReadByte();

            Variant value = Variant.Null;

            if ((encodingByte & (byte)VariantArrayEncodingBits.Array) != 0)
            {
                // read the array length.
                int length = ReadArrayLength();

                if (length < 0)
                {
                    return value;
                }

                var builtInType = (BuiltInType)(encodingByte &
                    (byte)VariantArrayEncodingBits.TypeMask);

                Array array = ReadArrayElements(length, builtInType);

                if (array == null)
                {
                    value = new Variant((StatusCode)StatusCodes.BadDecodingError);
                }
                else
                {
                    // check for multi-dimensional arrays.
                    if ((encodingByte & (byte)VariantArrayEncodingBits.ArrayDimensions) != 0)
                    {
                        Int32Collection dimensions = ReadInt32Array(null);

                        // check if ArrayDimensions are consistent with the ArrayLength.
                        if (dimensions == null || dimensions.Count == 0)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "ArrayDimensions not specified when ArrayDimensions encoding bit was set in Variant object.");
                        }

                        int[] dimensionsArray = [.. dimensions];
                        (bool valid, int matrixLength) = Matrix.ValidateDimensions(
                            dimensionsArray,
                            length,
                            Context.MaxArrayLength);

                        if (!valid || (matrixLength != length))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "ArrayDimensions length does not match with the ArrayLength in Variant object.");
                        }

                        if (dimensions.Count == 1)
                        {
                            value = new Variant(array, TypeInfo.CreateArray(builtInType));
                        }
                        else
                        {
                            value = new Variant(new Matrix(array, builtInType, dimensionsArray));
                        }
                    }
                    else
                    {
                        value = new Variant(array, TypeInfo.CreateArray(builtInType));
                    }
                }
            }
            else
            {
                switch ((BuiltInType)encodingByte)
                {
                    case BuiltInType.Null:
                        value = new Variant((object)null);
                        break;
                    case BuiltInType.Boolean:
                        value = new Variant(SafeReadBoolean());
                        break;
                    case BuiltInType.SByte:
                        value = new Variant(SafeReadSByte());
                        break;
                    case BuiltInType.Byte:
                        value = new Variant(SafeReadByte());
                        break;
                    case BuiltInType.Int16:
                        value = new Variant(SafeReadInt16());
                        break;
                    case BuiltInType.UInt16:
                        value = new Variant(SafeReadUInt16());
                        break;
                    case BuiltInType.Int32:
                    case BuiltInType.Enumeration:
                        value = new Variant(SafeReadInt32());
                        break;
                    case BuiltInType.UInt32:
                        value = new Variant(SafeReadUInt32());
                        break;
                    case BuiltInType.Int64:
                        value = new Variant(SafeReadInt64());
                        break;
                    case BuiltInType.UInt64:
                        value = new Variant(SafeReadUInt64());
                        break;
                    case BuiltInType.Float:
                        value = new Variant(SafeReadFloat());
                        break;
                    case BuiltInType.Double:
                        value = new Variant(SafeReadDouble());
                        break;
                    case BuiltInType.String:
                        value = new Variant(ReadString(null));
                        break;
                    case BuiltInType.DateTime:
                        value = new Variant(ReadDateTime(null));
                        break;
                    case BuiltInType.Guid:
                        value = new Variant(ReadGuid(null));
                        break;
                    case BuiltInType.ByteString:
                        value = new Variant(ReadByteString(null));
                        break;
                    case BuiltInType.XmlElement:
                        try
                        {
                            value = new Variant(ReadXmlElement(null));
                        }
                        catch (ServiceResultException)
                        {
                            // fatal decoder error, invalid data
                            throw;
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogDebug(ex, "Error reading xml element for Variant.");
                            value = new Variant(StatusCodes.BadDecodingError);
                        }
                        break;
                    case BuiltInType.NodeId:
                        value = new Variant(ReadNodeId(null));
                        break;
                    case BuiltInType.ExpandedNodeId:
                        value = new Variant(ReadExpandedNodeId(null));
                        break;
                    case BuiltInType.StatusCode:
                        value = new Variant(ReadStatusCode(null));
                        break;
                    case BuiltInType.QualifiedName:
                        value = new Variant(ReadQualifiedName(null));
                        break;
                    case BuiltInType.LocalizedText:
                        value = new Variant(ReadLocalizedText(null));
                        break;
                    case BuiltInType.ExtensionObject:
                        value = new Variant(ReadExtensionObject());
                        break;
                    case BuiltInType.DataValue:
                        value = new Variant(ReadDataValue(null));
                        break;
                    case BuiltInType.Variant:
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Cannot decode unknown type in Variant object (0x{0:X2}).",
                            encodingByte);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {encodingByte}");
                }
            }

            return value;
        }

        /// <summary>
        /// Read bytes from stream and validate the length of the returned buffer.
        /// Throws decoding error if less than the expected number of bytes were read.
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="functionName">The name of the calling function.</param>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] SafeReadBytes(int length, [CallerMemberName] string functionName = null)
        {
            if (length == 0)
            {
                return [];
            }

            byte[] bytes = m_reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                    length,
                    functionName,
                    bytes.Length);
            }
            return bytes;
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Read char bytes from the stream and validate the length of the returned buffer.
        /// Throws decoding error if less than the expected number of bytes were read.
        /// </summary>
        /// <param name="bytes">A Span with the number of Utf8 characters to read.</param>
        /// <param name="functionName">The name of the calling function.</param>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int SafeReadCharBytes(
            Span<byte> bytes,
            [CallerMemberName] string functionName = null)
        {
            int length = m_reader.Read(bytes);

            if (bytes.Length != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                    length,
                    functionName,
                    bytes.Length);
            }

            return length;
        }
#endif

        /// <summary>
        /// Safe version of <see cref="ReadBoolean"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SafeReadBoolean([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadBoolean();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadBoolean), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadSByte"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private sbyte SafeReadSByte([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadSByte();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadSByte), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadByte"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte SafeReadByte([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadByte();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadByte), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadInt16"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private short SafeReadInt16([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadInt16();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadInt16), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadUInt16"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort SafeReadUInt16([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadUInt16();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadUInt16), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadInt32"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int SafeReadInt32([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadInt32), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadUInt32"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint SafeReadUInt32([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadUInt32();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadUInt32), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadInt64"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long SafeReadInt64([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadInt64();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadInt64), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadUInt64"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong SafeReadUInt64([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadUInt64();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadUInt64), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadInt64"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float SafeReadFloat([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadSingle();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadFloat), functionName);
            }
        }

        /// <summary>
        /// Safe version of <see cref="ReadUInt64"></see> which returns a ServiceResultException on error.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double SafeReadDouble([CallerMemberName] string functionName = null)
        {
            try
            {
                return m_reader.ReadDouble();
            }
            catch (EndOfStreamException)
            {
                throw CreateDecodingError(nameof(ReadDouble), functionName);
            }
        }

        /// <summary>
        /// Throws a BadDecodingError for the specific dataType and function.
        /// </summary>
        /// <param name="dataTypeName">The datatype which reached the end of the stream.</param>
        /// <param name="functionName">The property which tried to read the datatype.</param>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        private static ServiceResultException CreateDecodingError(
            string dataTypeName,
            string functionName)
        {
            return ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Reading {0} in {1} reached end of stream.",
                dataTypeName,
                functionName);
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

        /// <summary>
        /// Validate the stream requirements.
        /// </summary>
        /// <param name="stream">The stream used for decoding.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateStreamRequirements(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (stream.CanSeek != true || stream.CanRead != true)
            {
                throw new ArgumentException("Stream must be seekable and readable.");
            }
        }

        private BinaryReader m_reader;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        private uint m_encodeablesRecovered;
        private readonly ILogger m_logger;
    }
}
