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
            : this(buffer, 0, buffer?.Length ?? 0, context)
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
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<BinaryDecoder>();
            var stream = new MemoryStream(buffer, start, count, false);
            m_reader = new BinaryReader(stream);
        }

        /// <summary>
        /// Creates a decoder that reads from a stream.
        /// </summary>
        public BinaryDecoder(
            Stream stream,
            IServiceMessageContext context,
            bool leaveOpen = false)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (!stream.CanSeek || !stream.CanRead)
            {
                throw new ArgumentException("Stream must be seekable and readable.");
            }
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<BinaryDecoder>();
            m_reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
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
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Stream does not support seeking.");
                }
                long position = stream?.Position ?? 0;
                if (position is > int.MaxValue or < int.MinValue)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Stream Position exceeds int.MaxValue or int.MinValue.");
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
        /// <typeparam name="T">The type of the message to read</typeparam>
        public static T DecodeMessage<T>(
            Stream stream,
            IServiceMessageContext context) where T : IEncodeable
        {
            using var decoder = new BinaryDecoder(stream, context);
            return decoder.DecodeMessage<T>();
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        /// <typeparam name="T">The type of the message to read</typeparam>
        public static T DecodeMessage<T>(
            byte[] buffer,
            IServiceMessageContext context) where T : IEncodeable
        {
            using var decoder = new BinaryDecoder(buffer, context);
            return decoder.DecodeMessage<T>();
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
        public T DecodeMessage<T>() where T : IEncodeable
        {
            int start = Position;

            // read the node id.
            NodeId typeId = ReadNodeId(null);

            // convert to absolute node id.
            var absoluteId = NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris);

            // read the message.
            T message = ReadEncodeable<T>(null, absoluteId);

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
            return new Uuid(bytes);
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string fieldName)
        {
            return ReadByteString(Context.MaxByteStringLength);
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ByteString ReadByteString(int maxByteStringLength)
        {
            int length = SafeReadInt32();

            if (length < 0)
            {
                return default;
            }

            if (maxByteStringLength > 0 && maxByteStringLength < length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    maxByteStringLength,
                    length);
            }

            return ByteString.From(SafeReadBytes(length));
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string fieldName)
        {
            ByteString bytes = ReadByteString(Context.MaxStringLength);

            if (bytes.IsEmpty)
            {
                return default;
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
                string xmlString = Encoding.UTF8.GetString(bytes.ToArray(), 0, utf8StringLength);
                using var stream = new StringReader(xmlString);
                using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
                document.Load(reader);
            }
            catch (XmlException)
            {
                return default;
            }

            return XmlElement.From(document.DocumentElement);
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string fieldName)
        {
            byte encodingByte = SafeReadByte();

            ReadNodeIdBody(encodingByte, out NodeId value);

            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex)
            {
                return value.WithNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
            }

            return value;
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string fieldName)
        {
            byte encodingByte = SafeReadByte();

            ReadNodeIdBody(encodingByte, out NodeId body);
            var expandedNodeId = new ExpandedNodeId(body);

            // read the namespace uri if present.
            if ((encodingByte & 0x80) != 0)
            {
                string namespaceUri = ReadString(null);
                expandedNodeId = expandedNodeId.WithNamespaceUri(namespaceUri);
            }

            // read the server index if present.
            if ((encodingByte & 0x40) != 0)
            {
                uint serverIndex = SafeReadUInt32();
                expandedNodeId = expandedNodeId.WithServerIndex(serverIndex);
            }

            if (m_namespaceMappings != null &&
                m_namespaceMappings.Length > expandedNodeId.NamespaceIndex)
            {
                expandedNodeId = expandedNodeId.WithNamespaceIndex(
                    m_namespaceMappings[expandedNodeId.NamespaceIndex]);
            }

            if (m_serverMappings != null &&
                m_serverMappings.Length > expandedNodeId.ServerIndex)
            {
                expandedNodeId = expandedNodeId.WithServerIndex(
                    m_serverMappings[expandedNodeId.ServerIndex]);
            }

            return expandedNodeId;
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string fieldName)
        {
            return SafeReadUInt32();
        }

        /// <inheritdoc/>
        public DiagnosticInfo ReadDiagnosticInfo(string fieldName)
        {
            return ReadDiagnosticInfo(0);
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
                return ReadVariantValue(null, default);
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
        public T ReadEncodeable<T>(string fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (!Context.Factory.TryGetEncodeableType(
                encodeableTypeId,
                out IEncodeableType activator))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode type '{0}'.",
                    encodeableTypeId);
            }

            var encodeable = (T)activator.CreateInstance();

            // set type identifier for custom complex data types before decode.
            if (encodeable is IComplexTypeInstance complexTypeInstance)
            {
                complexTypeInstance.TypeId = encodeableTypeId;
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
        public T ReadEncodeable<T>(string fieldName) where T : IEncodeable, new()
        {
            CheckAndIncrementNestingLevel();
            var encodeable = new T();
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
        public T ReadEnumerated<T>(string fieldName) where T : struct, Enum
        {
            return EnumHelper.Int32ToEnum<T>(SafeReadInt32());
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            bool[] values = new bool[length];
            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadBoolean(null);
            }
            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            sbyte[] values = new sbyte[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadSByte();
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            byte[] values = new byte[length];
            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadByte();
            }
            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            short[] values = new short[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadInt16(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            ushort[] values = new ushort[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadUInt16(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            int[] values = new int[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadInt32();
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            uint[] values = new uint[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadUInt32();
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            long[] values = new long[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadInt64();
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            ulong[] values = new ulong[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadUInt64();
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            float[] values = new float[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadFloat();
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            double[] values = new double[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = SafeReadDouble();
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<string> ReadStringArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            string[] values = new string[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadString(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<DateTime> ReadDateTimeArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new DateTime[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadDateTime(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new Uuid[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadGuid(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new ByteString[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadByteString(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new XmlElement[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadXmlElement(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new NodeId[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadNodeId(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new ExpandedNodeId[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadExpandedNodeId(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new StatusCode[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadStatusCode(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo> ReadDiagnosticInfoArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new DiagnosticInfo[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadDiagnosticInfo(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new QualifiedName[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadQualifiedName(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new LocalizedText[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadLocalizedText(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new Variant[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadVariant(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue> ReadDataValueArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new DataValue[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadDataValue(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new ExtensionObject[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadExtensionObject(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(
            string fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new T[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadEncodeable<T>(null, encodeableTypeId);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string fieldName)
            where T : IEncodeable, new()
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new T[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadEncodeable<T>(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string fieldName) where T : struct, Enum
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return default;
            }

            var values = new T[length];

            for (int ii = 0; ii < length; ii++)
            {
                values[ii] = ReadEnumerated<T>(null);
            }

            return values;
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string fieldName, TypeInfo typeInfo)
        {
            // read the encoding byte if we do not have the type info.
            bool isRaw = !typeInfo.IsUnknown;
            if (!isRaw)
            {
                byte encodingByte = SafeReadByte();
                typeInfo = TypeInfo.Create(
                    (BuiltInType)
                        (encodingByte & (byte)VariantArrayEncodingBits.TypeMask),
                    (encodingByte & (byte)VariantArrayEncodingBits.Array) == 0 ?
                        ValueRanks.Scalar :
                    (encodingByte & (byte)VariantArrayEncodingBits.ArrayDimensions) == 0 ?
                        ValueRanks.OneDimension :
                        ValueRanks.TwoDimensions);
            }
            if (typeInfo.IsScalar)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        return Variant.Null;
                    case BuiltInType.Boolean:
                        return Variant.From(SafeReadBoolean());
                    case BuiltInType.SByte:
                        return Variant.From(SafeReadSByte());
                    case BuiltInType.Byte:
                        return Variant.From(SafeReadByte());
                    case BuiltInType.Int16:
                        return Variant.From(SafeReadInt16());
                    case BuiltInType.UInt16:
                        return Variant.From(SafeReadUInt16());
                    case BuiltInType.Int32:
                    case BuiltInType.Enumeration:
                        return Variant.From(SafeReadInt32());
                    case BuiltInType.UInt32:
                        return Variant.From(SafeReadUInt32());
                    case BuiltInType.Int64:
                        return Variant.From(SafeReadInt64());
                    case BuiltInType.UInt64:
                        return Variant.From(SafeReadUInt64());
                    case BuiltInType.Float:
                        return Variant.From(SafeReadFloat());
                    case BuiltInType.Double:
                        return Variant.From(SafeReadDouble());
                    case BuiltInType.String:
                        return Variant.From(ReadString(null));
                    case BuiltInType.DateTime:
                        return Variant.From(ReadDateTime(null));
                    case BuiltInType.Guid:
                        return Variant.From(ReadGuid(null));
                    case BuiltInType.ByteString:
                        return Variant.From(ReadByteString(null));
                    case BuiltInType.XmlElement:
                        return Variant.From(ReadXmlElement(null));
                    case BuiltInType.NodeId:
                        return Variant.From(ReadNodeId(null));
                    case BuiltInType.ExpandedNodeId:
                        return Variant.From(ReadExpandedNodeId(null));
                    case BuiltInType.StatusCode:
                        return Variant.From(ReadStatusCode(null));
                    case BuiltInType.QualifiedName:
                        return Variant.From(ReadQualifiedName(null));
                    case BuiltInType.LocalizedText:
                        return Variant.From(ReadLocalizedText(null));
                    case BuiltInType.ExtensionObject:
                        return Variant.From(ReadExtensionObject());
                    case BuiltInType.DataValue:
                        return Variant.From(ReadDataValue(null));
                    case BuiltInType.Variant:
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unsupported built in type for Variant content ({0}).",
                            typeInfo);
                    default:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unexpected scalar built in type ({0}).",
                            typeInfo);
                }
            }
            if (typeInfo.IsArray)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return Variant.From(ReadBooleanArray(null));
                    case BuiltInType.SByte:
                        return Variant.From(ReadSByteArray(null));
                    case BuiltInType.Byte:
                        return Variant.From(ReadByteArray(null));
                    case BuiltInType.Int16:
                        return Variant.From(ReadInt16Array(null));
                    case BuiltInType.UInt16:
                        return Variant.From(ReadUInt16Array(null));
                    case BuiltInType.Int32:
                    case BuiltInType.Enumeration:
                        return Variant.From(ReadInt32Array(null));
                    case BuiltInType.UInt32:
                        return Variant.From(ReadUInt32Array(null));
                    case BuiltInType.Int64:
                        return Variant.From(ReadInt64Array(null));
                    case BuiltInType.UInt64:
                        return Variant.From(ReadUInt64Array(null));
                    case BuiltInType.Float:
                        return Variant.From(ReadFloatArray(null));
                    case BuiltInType.Double:
                        return Variant.From(ReadDoubleArray(null));
                    case BuiltInType.String:
                        return Variant.From(ReadStringArray(null));
                    case BuiltInType.DateTime:
                        return Variant.From(ReadDateTimeArray(null));
                    case BuiltInType.Guid:
                        return Variant.From(ReadGuidArray(null));
                    case BuiltInType.ByteString:
                        return Variant.From(ReadByteStringArray(null));
                    case BuiltInType.XmlElement:
                        return Variant.From(ReadXmlElementArray(null));
                    case BuiltInType.NodeId:
                        return Variant.From(ReadNodeIdArray(null));
                    case BuiltInType.ExpandedNodeId:
                        return Variant.From(ReadExpandedNodeIdArray(null));
                    case BuiltInType.StatusCode:
                        return Variant.From(ReadStatusCodeArray(null));
                    case BuiltInType.QualifiedName:
                        return Variant.From(ReadQualifiedNameArray(null));
                    case BuiltInType.LocalizedText:
                        return Variant.From(ReadLocalizedTextArray(null));
                    case BuiltInType.ExtensionObject:
                        return Variant.From(ReadExtensionObjectArray(null));
                    case BuiltInType.DataValue:
                        return Variant.From(ReadDataValueArray(null));
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        return Variant.From(ReadVariantArray(null));
                    case BuiltInType.DiagnosticInfo:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unsupported built in type for Variant array content ({0}).",
                            typeInfo);
                    default:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unexpected array built in type ({0}).",
                            typeInfo);
                }
            }
            else
            {
                // TODO: Remove on encoder side if (isRaw)
                // TODO: Remove on encoder side {
                // TODO: Remove on encoder side     // Need to read the encoding byte for multi dimensional raw mode
                // TODO: Remove on encoder side     SafeReadByte();
                // TODO: Remove on encoder side }

                int[] ReadDims() => ReadInt32Array(null).ToArray();
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return Variant.From(ReadBooleanArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.SByte:
                        return Variant.From(ReadSByteArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.Byte:
                        return Variant.From(ReadByteArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.Int16:
                        return Variant.From(ReadInt16Array(null).ToMatrix(ReadDims()));
                    case BuiltInType.UInt16:
                        return Variant.From(ReadUInt16Array(null).ToMatrix(ReadDims()));
                    case BuiltInType.Int32:
                    case BuiltInType.Enumeration:
                        return Variant.From(ReadInt32Array(null).ToMatrix(ReadDims()));
                    case BuiltInType.UInt32:
                        return Variant.From(ReadUInt32Array(null).ToMatrix(ReadDims()));
                    case BuiltInType.Int64:
                        return Variant.From(ReadInt64Array(null).ToMatrix(ReadDims()));
                    case BuiltInType.UInt64:
                        return Variant.From(ReadUInt64Array(null).ToMatrix(ReadDims()));
                    case BuiltInType.Float:
                        return Variant.From(ReadFloatArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.Double:
                        return Variant.From(ReadDoubleArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.String:
                        return Variant.From(ReadStringArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.DateTime:
                        return Variant.From(ReadDateTimeArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.Guid:
                        return Variant.From(ReadGuidArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.ByteString:
                        return Variant.From(ReadByteStringArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.XmlElement:
                        return Variant.From(ReadXmlElementArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.NodeId:
                        return Variant.From(ReadNodeIdArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.ExpandedNodeId:
                        return Variant.From(ReadExpandedNodeIdArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.StatusCode:
                        return Variant.From(ReadStatusCodeArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.QualifiedName:
                        return Variant.From(ReadQualifiedNameArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.LocalizedText:
                        return Variant.From(ReadLocalizedTextArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.ExtensionObject:
                        return Variant.From(ReadExtensionObjectArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.DataValue:
                        return Variant.From(ReadDataValueArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        return Variant.From(ReadVariantArray(null).ToMatrix(ReadDims()));
                    case BuiltInType.DiagnosticInfo:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unsupported built in type for Variant matrix content ({0}).",
                            typeInfo);
                    default:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unexpected matrix built in type ({0}).",
                            typeInfo);
                }
            }
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
                    value.InnerDiagnosticInfo = ReadDiagnosticInfo(depth + 1) ??
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
        private void ReadNodeIdBody(byte encodingByte, out NodeId value)
        {
            ushort namespaceIndex;
            switch ((NodeIdEncodingBits)(encodingByte & 0x3F))
            {
                case NodeIdEncodingBits.TwoByte:
                    value = new NodeId(SafeReadByte());
                    break;
                case NodeIdEncodingBits.FourByte:
                    namespaceIndex = SafeReadByte();
                    value = new NodeId(SafeReadUInt16(), namespaceIndex);
                    break;
                case NodeIdEncodingBits.Numeric:
                    namespaceIndex = SafeReadUInt16();
                    value = new NodeId(SafeReadUInt32(), namespaceIndex);
                    break;
                case NodeIdEncodingBits.String:
                    namespaceIndex = SafeReadUInt16();
                    value = new NodeId(ReadString(null), namespaceIndex);
                    break;
                case NodeIdEncodingBits.Guid:
                    namespaceIndex = SafeReadUInt16();
                    value = new NodeId((Guid)ReadGuid(null), namespaceIndex);
                    break;
                case NodeIdEncodingBits.ByteString:
                    namespaceIndex = SafeReadUInt16();
                    value = new NodeId(ReadByteString(null), namespaceIndex);
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
            // read type id.
            NodeId typeId = ReadNodeId(null);

            // convert to absolute node id.
            var extension = new ExtensionObject(
                NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris));

            if (!typeId.IsNull && extension.TypeId.IsNull)
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

            if (encoding is
                not ((byte)ExtensionObjectEncoding.Binary) and
                not ((byte)ExtensionObjectEncoding.Xml))
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
                extension = new ExtensionObject(
                    extension.TypeId,
                    ReadXmlElement(null));

                // attempt to decode a known type.
                if (systemType != null && !extension.IsNull)
                {
                    XmlElement element = extension.TryGetAsXml(out XmlElement xe) ? xe : default;
                    using var xmlDecoder = new XmlDecoder(element, Context);
                    try
                    {
                        System.Xml.XmlElement xmlElement = element.AsXmlElement();
                        xmlDecoder.PushNamespace(xmlElement.NamespaceURI);
                        IEncodeable body = xmlDecoder.ReadEncodeable<IEncodeable>(
                            xmlElement.LocalName,
                            extension.TypeId);
                        xmlDecoder.PopNamespace();

                        // update body.
                        extension = new ExtensionObject(body);

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
                catch (ServiceResultException sre) when (
                    sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded ||
                    sre.StatusCode == StatusCodes.BadDecodingError)
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
                return new ExtensionObject(
                    extension.TypeId,
                    ByteString.From(SafeReadBytes(length)));
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
            return new ExtensionObject(encodeable);
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

        private BinaryReader m_reader;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        private uint m_encodeablesRecovered;
        private readonly ILogger m_logger;
    }
}
