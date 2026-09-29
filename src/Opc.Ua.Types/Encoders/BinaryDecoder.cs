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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
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
            : this(buffer.Array!, buffer.Offset, buffer.Count, context)
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
            var stream = new MemoryStream(buffer, start, count, false);
            m_reader = new BinaryReader(stream);
            m_buffer = new ReadOnlyMemory<byte>(buffer, start, count);
            m_hasBuffer = true;
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
                m_reader?.Dispose();
                m_reader = null!;
            }
        }

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being decoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being decoded.</param>
        public void SetMappingTables(NamespaceTable? namespaceUris, StringTable? serverUris)
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
        /// Adopts already resolved mapping tables and the current nesting depth
        /// from an outer decoder. Used when an ExtensionObject body is decoded
        /// from inside another decoder - the nested body is part of the same
        /// message and must share both.
        /// </summary>
        /// <param name="namespaceMappings">The outer namespace mappings.</param>
        /// <param name="serverMappings">The outer server mappings.</param>
        /// <param name="nestingLevel">The outer nesting level.</param>
        /// <param name="bodyEnd">
        /// The end of the ExtensionObject body when the whole buffer of this
        /// decoder is such a body, -1 when unknown.
        /// </param>
        internal void InheritDecodingState(
            ushort[]? namespaceMappings,
            ushort[]? serverMappings,
            uint nestingLevel,
            int bodyEnd = -1)
        {
            m_namespaceMappings = namespaceMappings;
            m_serverMappings = serverMappings;
            m_nestingLevel = nestingLevel;
            m_bodyEnd = bodyEnd;
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
        public Stream BaseStream
        {
            get
            {
                if (m_hasBuffer && !m_baseStreamExposed)
                {
                    m_reader.BaseStream.Position = m_bufferPosition;
                    m_synchronizedStreamPosition = m_bufferPosition;
                    m_baseStreamExposed = true;
                }

                return m_reader?.BaseStream!;
            }
        }

        /// <summary>
        /// Reads whatever is left of the ExtensionObject body currently being
        /// decoded.
        /// </summary>
        /// <remarks>
        /// Almost every encodeable knows where each of its fields ends, because
        /// every field is either fixed width or carries its own length. The
        /// <c>Decimal</c> of OPC 10000-6 5.1.10 is the built-in exception: its
        /// unscaled value is a run of raw octets whose count is the
        /// ExtensionObject <c>Length</c> minus the two bytes of <c>Scale</c>,
        /// so the field cannot be read without knowing the extent of the body
        /// that contains it. <see cref="Decimal.Decode"/> is the only caller,
        /// and this stays internal so it remains the only one.
        /// </remarks>
        /// <param name="bytes">The rest of the body.</param>
        /// <returns>
        /// <c>false</c> when no body is being decoded or the writer did not
        /// fill in its length, in which case the extent is unknown and nothing
        /// is read.
        /// </returns>
        internal bool TryReadRemainingBodyBytes(out byte[] bytes)
        {
            if (m_bodyEnd < 0)
            {
                bytes = [];
                return false;
            }

            // A declared Length shorter than what the encodeable has already
            // consumed leaves nothing behind it. Subtracting would hand a
            // negative count to the reader, which answers with an exception no
            // caller of ReadExtensionObject expects and which therefore escapes
            // as something other than BadDecodingError. An empty run says the
            // same thing and lets the caller reject it in its own terms.
            int remaining = m_bodyEnd - Position;
            bytes = remaining > 0 ? SafeReadBytes(remaining) : [];
            return true;
        }

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
                stringTable.Append(ReadString(null) ?? string.Empty);
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
        public bool ReadBoolean(string? fieldName)
        {
            return SafeReadBoolean();
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string? fieldName)
        {
            return SafeReadSByte();
        }

        /// <inheritdoc/>
        public byte ReadByte(string? fieldName)
        {
            return SafeReadByte();
        }

        /// <inheritdoc/>
        public short ReadInt16(string? fieldName)
        {
            return SafeReadInt16();
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string? fieldName)
        {
            return SafeReadUInt16();
        }

        /// <inheritdoc/>
        public int ReadInt32(string? fieldName)
        {
            return SafeReadInt32();
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string? fieldName)
        {
            return SafeReadUInt32();
        }

        /// <inheritdoc/>
        public long ReadInt64(string? fieldName)
        {
            return SafeReadInt64();
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string? fieldName)
        {
            return SafeReadUInt64();
        }

        /// <inheritdoc/>
        public float ReadFloat(string? fieldName)
        {
            return SafeReadFloat();
        }

        /// <inheritdoc/>
        public double ReadDouble(string? fieldName)
        {
            return SafeReadDouble();
        }

        /// <inheritdoc/>
        public string? ReadString(string? fieldName)
        {
            return ReadString(fieldName, Context.MaxStringLength);
        }

        /// <summary>
        /// Reads a string from the stream (throws an exception if
        /// its length is invalid or exceeds the limit specified).
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public string? ReadString(string? fieldName, int maxStringLength)
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

            // Do not rent or allocate a declared length the message cannot hold.
            CheckRemainingBytes(length, nameof(ReadString));

            // length is always >= 1 here
#if NET6_0_OR_GREATER
            const int maxStackAlloc = 1024;
            byte[]? buffer = null;
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
        public DateTimeUtc ReadDateTime(string? fieldName)
        {
            return new DateTimeUtc(SafeReadInt64());
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string? fieldName)
        {
            const int kGuidLength = 16;
            byte[] bytes = SafeReadBytes(kGuidLength);
            return new Uuid(bytes);
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string? fieldName)
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
        public XmlElement ReadXmlElement(string? fieldName)
        {
            ByteString bytes = ReadByteString(Context.MaxStringLength);

            if (bytes.IsEmpty)
            {
                return default;
            }

            // If 0 terminated, decrease length before converting to string
            int utf8StringLength = bytes.Length;
            while (utf8StringLength > 0 && bytes[utf8StringLength - 1] == 0)
            {
                utf8StringLength--;
            }
            string xmlString = Encoding.UTF8.GetString(bytes.ToArray(), 0, utf8StringLength);
            return XmlElement.From(xmlString);
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string? fieldName)
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
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName)
        {
            byte encodingByte = SafeReadByte();

            ReadNodeIdBody(encodingByte, out NodeId body);
            var expandedNodeId = new ExpandedNodeId(body);

            // read the namespace uri if present.
            if ((encodingByte & 0x80) != 0)
            {
                string? namespaceUri = ReadString(null);
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
        public StatusCode ReadStatusCode(string? fieldName)
        {
            return SafeReadUInt32();
        }

        /// <inheritdoc/>
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName)
        {
            return ReadDiagnosticInfo(0);
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string? fieldName)
        {
            ushort namespaceIndex = ReadUInt16(null);
            string? name = ReadString(null);

            if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
            {
                namespaceIndex = m_namespaceMappings[namespaceIndex];
            }

            return new QualifiedName(name, namespaceIndex);
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string? fieldName)
        {
            // read the encoding byte.
            byte encodingByte = SafeReadByte();

            string? text = null;
            string? locale = null;

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
        public Variant ReadVariant(string? fieldName)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                byte encodingByte = SafeReadByte();
                int typeId = encodingByte & (byte)VariantArrayEncodingBits.TypeMask;

                // The built-in type ids 26 through 31 are reserved: decoders
                // shall accept them and assume the value is a ByteString
                // (OPC 10000-6 5.2.2.16). The ids 26-29 the SDK assigns to its
                // pseudo types (Number, Integer, UInteger, Enumeration) are
                // never written to the wire (Enumeration is sent as Int32).
                if (typeId is >= kFirstReservedVariantTypeId and <= kLastReservedVariantTypeId)
                {
                    typeId = (int)BuiltInType.ByteString;
                }

                var typeInfo = TypeInfo.Create(
                    (BuiltInType)typeId,
                    (encodingByte & (byte)VariantArrayEncodingBits.Array) == 0 ?
                        ValueRanks.Scalar :
                    (encodingByte & (byte)VariantArrayEncodingBits.ArrayDimensions) == 0 ?
                        ValueRanks.OneDimension :
                        ValueRanks.TwoDimensions);
                return ReadVariantValue(typeInfo, false);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public DataValue ReadDataValue(string? fieldName)
        {
            // read the encoding byte.
            byte encodingByte = SafeReadByte();

            if (encodingByte == 0)
            {
                // BinaryEncoder writes a single zero byte when IsNull is
                // true (i.e. the value was default(DataValue)). Round-trip
                // that as DataValue.Null so callers can distinguish
                // "absent" from "explicitly empty".
                return DataValue.Null;
            }

            // read the fields of the DataValue structure into locals so the
            // DataValue is constructed once instead of via a chain of
            // intermediate "With..." struct copies.
            Variant wrappedValue = Variant.Null;
            StatusCode statusCode = StatusCodes.Good;
            DateTimeUtc sourceTimestamp = DateTimeUtc.MinValue;
            DateTimeUtc serverTimestamp = DateTimeUtc.MinValue;
            ushort sourcePicoseconds = 0;
            ushort serverPicoseconds = 0;

            if ((encodingByte & (byte)DataValueEncodingBits.Value) != 0)
            {
                wrappedValue = ReadVariant(null);
            }

            if ((encodingByte & (byte)DataValueEncodingBits.StatusCode) != 0)
            {
                statusCode = ReadStatusCode(null);
            }

            // Picoseconds without their timestamp are read and ignored, and
            // values >= 10000 are treated as 9999 (OPC 10000-6 5.2.2.17).
            bool hasTimestamp = (encodingByte &
                (byte)DataValueEncodingBits.SourceTimestamp) != 0;
            if (hasTimestamp)
            {
                sourceTimestamp = ReadDateTime(null);
            }
            if ((encodingByte & (byte)DataValueEncodingBits.SourcePicoseconds) != 0)
            {
                ushort picoseconds = ReadUInt16(null);
                if (hasTimestamp)
                {
                    sourcePicoseconds = Math.Min(picoseconds, kMaxPicoseconds);
                }
            }

            hasTimestamp = (encodingByte & (byte)DataValueEncodingBits.ServerTimestamp) != 0;
            if (hasTimestamp)
            {
                serverTimestamp = ReadDateTime(null);
            }
            if ((encodingByte & (byte)DataValueEncodingBits.ServerPicoseconds) != 0)
            {
                ushort picoseconds = ReadUInt16(null);
                if (hasTimestamp)
                {
                    serverPicoseconds = Math.Min(picoseconds, kMaxPicoseconds);
                }
            }

            return new DataValue(
                wrappedValue,
                statusCode,
                sourceTimestamp,
                serverTimestamp,
                sourcePicoseconds,
                serverPicoseconds);
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string? fieldName)
        {
            // read type id.
            NodeId typeId = ReadNodeId(null);

            // convert to absolute node id.
            var extension = new ExtensionObject(
                NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris));

            if (!typeId.IsNull && extension.TypeId.IsNull)
            {
                Logger.CannotDeserializeExtensionObject(typeId);
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
            if (!Context.Factory.TryGetEncodeableType(
                extension.TypeId,
                out IEncodeableType? activator))
            {
                Logger.ActivatorNotFound();
                // Continue without registered type by reading the binary blob for later.
            }

            // check for XML bodies.
            if (encoding == (byte)ExtensionObjectEncoding.Xml)
            {
                extension = new ExtensionObject(
                    extension.TypeId,
                    ReadXmlElement(null));

                // attempt to decode a known type.
                if (activator != null && !extension.IsNull)
                {
                    XmlElement element = extension.TryGetAsXml(out XmlElement xe) ? xe : default;
                    using var xmlDecoder = new XmlDecoder(element, Context);
                    xmlDecoder.InheritDecodingState(
                        m_namespaceMappings,
                        m_serverMappings,
                        m_nestingLevel);
                    try
                    {
                        System.Xml.XmlElement? xmlElement = element.AsXmlElement();
                        xmlDecoder.PushNamespace(xmlElement!.NamespaceURI);
                        IEncodeable body = xmlDecoder.ReadEncodeable<IEncodeable>(
                            xmlElement.LocalName,
                            extension.TypeId);
                        xmlDecoder.PopNamespace();

                        // update body, preserve the wire encoding TypeId.
                        extension = new ExtensionObject(extension.TypeId, body);

                        xmlDecoder.Close();
                    }
                    catch (Exception e) when (
                        e is not ServiceResultException sre ||
                        sre.StatusCode != StatusCodes.BadEncodingLimitsExceeded)
                    {
                        // An encoding limit breach must not be downgraded into a
                        // successful decode that keeps the over limit raw body.
                        // Otherwise the policy of a binary body applies: a known
                        // type in ns=0 must decode, other types are kept raw only
                        // up to MaxDecoderRecoveries times and logged once, so a
                        // message cannot flood the log with its own content.
                        if (typeId.NamespaceIndex == 0 ||
                            m_encodeablesRecovered >= Context.MaxDecoderRecoveries)
                        {
                            throw e as ServiceResultException ??
                                ServiceResultException.Create(
                                    StatusCodes.BadDecodingError,
                                    e,
                                    "Failed to decode encodeable type '{0}' encoded as Xml, NodeId='{1}'.",
                                    activator.XmlName,
                                    extension.TypeId);
                        }

                        if (m_encodeablesRecovered == 0)
                        {
                            Logger.CouldNotDecodeKnownTypeXml(activator.XmlName, e.Message);
                        }

                        m_encodeablesRecovered++;
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
            IEncodeable? encodeable = null;
            if (activator != null && length >= -1)
            {
                encodeable = activator.CreateInstance();
            }

            // process known type.
            if (encodeable != null)
            {
                bool resetStream = true;
                string errorMessage = string.Empty;
                Exception? exception = null;
                uint nestingLevel = m_nestingLevel;

                // Publish the extent of this body for the duration of the call.
                // A type whose last field has no self-describing length - the
                // Decimal of OPC 10000-6 5.1.10 is the one built-in case -
                // cannot otherwise know where its value ends, because the count
                // is carried by the ExtensionObject Length rather than by the
                // field. Nested bodies save and restore it like the nesting
                // level below.
                int bodyEnd = m_bodyEnd;
                m_bodyEnd = length >= 0 ? start + length : -1;

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
                    m_bodyEnd = bodyEnd;
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
                                activator!.XmlName,
                                extension.TypeId);
                    }
                    else if (m_encodeablesRecovered == 0)
                    {
                        // log the error only once to avoid flooding the log.
                        Logger.DecodeEncodeableRecovered(
                            exception,
                            errorMessage,
                            activator!.XmlName,
                            extension.TypeId);
                    }

                    // reset the stream to the begin of the ExtensionObject body.
                    SetPosition(start);
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
            return new ExtensionObject(extension.TypeId, encodeable);
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
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

            if (activator.CreateInstance() is not T encodeable)
            {
                // The type id comes from the wire and need not name a T at all.
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Type '{0}' is not a {1}.",
                    encodeableTypeId,
                    typeof(T).Name);
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
        public T ReadEncodeable<T>(string? fieldName) where T : IEncodeable, new()
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
        public T ReadEncodeableAsExtensionObject<T>(string? fieldName)
            where T : IEncodeable
        {
            ExtensionObject extensionObject = ReadExtensionObject(fieldName);
            if (extensionObject.TryGetValue(out T? value))
            {
                return value!;
            }
            return default!;
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string? fieldName) where T : struct, Enum
        {
            return EnumHelper.Int32ToEnum<T>(SafeReadInt32());
        }

        /// <inheritdoc/>
        public EnumValue ReadEnumerated(string? fieldName)
        {
            return EnumValue.From(SafeReadInt32());
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            return ReadArray(1, static d => d.ReadBoolean(null));
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName)
        {
            return ReadFixedWidthArray<sbyte>();
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string? fieldName)
        {
            return ReadFixedWidthArray<byte>();
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string? fieldName)
        {
            return ReadFixedWidthArray<short>();
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName)
        {
            return ReadFixedWidthArray<ushort>();
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string? fieldName)
        {
            return ReadFixedWidthArray<int>();
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string? fieldName)
        {
            return ReadFixedWidthArray<uint>();
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string? fieldName)
        {
            return ReadFixedWidthArray<long>();
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName)
        {
            return ReadFixedWidthArray<ulong>();
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string? fieldName)
        {
            return ReadFixedWidthArray<float>();
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string? fieldName)
        {
            return ReadFixedWidthArray<double>();
        }

        /// <inheritdoc/>
        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            return ReadArray(4, static d => d.ReadString(null));
        }

        /// <inheritdoc/>
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName)
        {
            return ReadArray(8, static d => d.ReadDateTime(null));
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName)
        {
            return ReadArray(16, static d => d.ReadGuid(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName)
        {
            return ReadArray(4, static d => d.ReadByteString(null));
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName)
        {
            return ReadArray(4, static d => d.ReadXmlElement(null));
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName)
        {
            return ReadArray(2, static d => d.ReadNodeId(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName)
        {
            return ReadArray(2, static d => d.ReadExpandedNodeId(null));
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName)
        {
            return ReadArray(4, static d => d.ReadStatusCode(null));
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName)
        {
            return ReadArray(1, static d => d.ReadDiagnosticInfo(null));
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName)
        {
            return ReadArray(6, static d => d.ReadQualifiedName(null));
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName)
        {
            return ReadArray(1, static d => d.ReadLocalizedText(null));
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string? fieldName)
        {
            return ReadArray(1, static d => d.ReadVariant(null));
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue> ReadDataValueArray(string? fieldName)
        {
            return ReadArray(1, static d => d.ReadDataValue(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName)
        {
            return ReadArray(3, static d => d.ReadExtensionObject(null));
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            // An encodeable can encode to no bytes at all, only the
            // MaxArrayLength limit applies to its element count.
            return ReadArray(0, d => d.ReadEncodeable<T>(null, encodeableTypeId));
        }

        /// <inheritdoc/>
        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            // see https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.5
            // An encodeable can encode to no bytes at all, only the
            // MaxArrayLength limit applies to its element count.
            return ReadInlineMatrix(encodeableTypeId, 0,
                d => d.ReadEncodeable<T>(null, encodeableTypeId));
        }

        /// <inheritdoc/>
        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            // see https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.5
            return ReadInlineMatrix(typeof(T).Name, 0,
                static d => d.ReadEncodeable<T>(null));
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArrayAsExtensionObjects<T>(string? fieldName)
            where T : IEncodeable
        {
            return ReadArray(3, static d => d.ReadEncodeableAsExtensionObject<T>(null));
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            return ReadArray(0, static d => d.ReadEncodeable<T>(null));
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string? fieldName) where T : struct, Enum
        {
            return ReadArray(4, static d => d.ReadEnumerated<T>(null));
        }

        /// <inheritdoc/>
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName)
        {
            return ReadArray(4, static d => d.ReadEnumerated(null));
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string? fieldName, TypeInfo typeInfo)
        {
            return ReadVariantValue(typeInfo, true);
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
            return true;
        }

        /// <summary>
        /// Read variant value which is the content of the variant.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadVariantValue(TypeInfo typeInfo, bool readRawValue)
        {
            if (typeInfo.IsUnknown)
            {
                return default;
            }

            // read the encoding byte if we do not have the type info.
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
                        return Variant.From(SafeReadInt32());
                    case BuiltInType.Enumeration:
                        return Variant.From(ReadEnumerated(null));
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
                        return Variant.From(ReadString(null)!);
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
                        return Variant.From(ReadExtensionObject(null));
                    case BuiltInType.DataValue:
                        return Variant.From(ReadDataValue(null));
                    case BuiltInType.Variant:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.DiagnosticInfo:
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
                    case BuiltInType.Null:
                        return Variant.Null;
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
                        return Variant.From(ReadInt32Array(null));
                    case BuiltInType.Enumeration:
                        return Variant.From(ReadEnumeratedArray(null));
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
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(ReadStringArray(null));
#pragma warning restore CS8620
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
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(ReadDataValueArray(null));
#pragma warning restore CS8620
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
            else if (readRawValue)
            {
                // A multi-dimensional structure field is an inline matrix.
                // see https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.5
                return ReadInlineMatrix(typeInfo);
            }
            else
            {
                // read the dimensions for variant encoding after the array.
                // see https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.2.16
                int[] ReadDims()
                {
                    return ReadInt32Array(null).ToArray() ?? [];
                }

                static MatrixOf<T> ToMatrix<T>(
                    ArrayOf<T> values,
                    int[] dimensions,
                    TypeInfo typeInfo)
                {
                    if (!MatrixOf.IsValidMatrix(dimensions, values.Count))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Variant matrix ArrayDimensions [{0}] are inconsistent with {1} element(s) ({2}).",
                            string.Join(",", dimensions),
                            values.Count,
                            typeInfo);
                    }

                    try
                    {
                        return values.ToMatrix(dimensions);
                    }
                    catch (ArgumentException)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Variant matrix ArrayDimensions [{0}] are inconsistent with {1} element(s) ({2}).",
                            string.Join(",", dimensions),
                            values.Count,
                            typeInfo);
                    }
                }

                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        return Variant.Null;
                    case BuiltInType.Boolean:
                        return Variant.From(ToMatrix(
                            ReadBooleanArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.SByte:
                        return Variant.From(ToMatrix(
                            ReadSByteArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Byte:
                        return Variant.From(ToMatrix(
                            ReadByteArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Int16:
                        return Variant.From(ToMatrix(
                            ReadInt16Array(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.UInt16:
                        return Variant.From(ToMatrix(
                            ReadUInt16Array(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Int32:
                        return Variant.From(ToMatrix(
                            ReadInt32Array(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Enumeration:
                        return Variant.From(ToMatrix(
                            ReadEnumeratedArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.UInt32:
                        return Variant.From(ToMatrix(
                            ReadUInt32Array(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Int64:
                        return Variant.From(ToMatrix(
                            ReadInt64Array(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.UInt64:
                        return Variant.From(ToMatrix(
                            ReadUInt64Array(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Float:
                        return Variant.From(ToMatrix(
                            ReadFloatArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Double:
                        return Variant.From(ToMatrix(
                            ReadDoubleArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.String:
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(ToMatrix(
                            ReadStringArray(null),
                            ReadDims(),
                            typeInfo));
#pragma warning restore CS8620
                    case BuiltInType.DateTime:
                        return Variant.From(ToMatrix(
                            ReadDateTimeArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.Guid:
                        return Variant.From(ToMatrix(
                            ReadGuidArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.ByteString:
                        return Variant.From(ToMatrix(
                            ReadByteStringArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.XmlElement:
                        return Variant.From(ToMatrix(
                            ReadXmlElementArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.NodeId:
                        return Variant.From(ToMatrix(
                            ReadNodeIdArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.ExpandedNodeId:
                        return Variant.From(ToMatrix(
                            ReadExpandedNodeIdArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.StatusCode:
                        return Variant.From(ToMatrix(
                            ReadStatusCodeArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.QualifiedName:
                        return Variant.From(ToMatrix(
                            ReadQualifiedNameArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.LocalizedText:
                        return Variant.From(ToMatrix(
                            ReadLocalizedTextArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.ExtensionObject:
                        return Variant.From(ToMatrix(
                            ReadExtensionObjectArray(null),
                            ReadDims(),
                            typeInfo));
                    case BuiltInType.DataValue:
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(ToMatrix(
                            ReadDataValueArray(null),
                            ReadDims(),
                            typeInfo));
#pragma warning restore CS8620
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        return Variant.From(ToMatrix(
                            ReadVariantArray(null),
                            ReadDims(),
                            typeInfo));
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

        /// <summary>
        /// Reads the value of a multi-dimensional structure field encoded
        /// with the inline matrix representation of OPC 10000-6 5.2.5
        /// Table 28.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadInlineMatrix(TypeInfo typeInfo)
        {
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.Null:
                    return Variant.Null;
                case BuiltInType.Boolean:
                    return Variant.From(ReadInlineMatrix(typeInfo, 1,
                        static d => d.ReadBoolean(null)));
                case BuiltInType.SByte:
                    return Variant.From(ReadInlineMatrixFixed<sbyte>(typeInfo));
                case BuiltInType.Byte:
                    return Variant.From(ReadInlineMatrixFixed<byte>(typeInfo));
                case BuiltInType.Int16:
                    return Variant.From(ReadInlineMatrixFixed<short>(typeInfo));
                case BuiltInType.UInt16:
                    return Variant.From(ReadInlineMatrixFixed<ushort>(typeInfo));
                case BuiltInType.Int32:
                    return Variant.From(ReadInlineMatrixFixed<int>(typeInfo));
                case BuiltInType.Enumeration:
                    return Variant.From(ReadInlineMatrix(typeInfo, 4,
                        static d => d.ReadEnumerated(null)));
                case BuiltInType.UInt32:
                    return Variant.From(ReadInlineMatrixFixed<uint>(typeInfo));
                case BuiltInType.Int64:
                    return Variant.From(ReadInlineMatrixFixed<long>(typeInfo));
                case BuiltInType.UInt64:
                    return Variant.From(ReadInlineMatrixFixed<ulong>(typeInfo));
                case BuiltInType.Float:
                    return Variant.From(ReadInlineMatrixFixed<float>(typeInfo));
                case BuiltInType.Double:
                    return Variant.From(ReadInlineMatrixFixed<double>(typeInfo));
                case BuiltInType.String:
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                    return Variant.From(ReadInlineMatrix(typeInfo, 4,
                        static d => d.ReadString(null)));
#pragma warning restore CS8620
                case BuiltInType.DateTime:
                    return Variant.From(ReadInlineMatrix(typeInfo, 8,
                        static d => d.ReadDateTime(null)));
                case BuiltInType.Guid:
                    return Variant.From(ReadInlineMatrix(typeInfo, 16,
                        static d => d.ReadGuid(null)));
                case BuiltInType.ByteString:
                    return Variant.From(ReadInlineMatrix(typeInfo, 4,
                        static d => d.ReadByteString(null)));
                case BuiltInType.XmlElement:
                    return Variant.From(ReadInlineMatrix(typeInfo, 4,
                        static d => d.ReadXmlElement(null)));
                case BuiltInType.NodeId:
                    return Variant.From(ReadInlineMatrix(typeInfo, 2,
                        static d => d.ReadNodeId(null)));
                case BuiltInType.ExpandedNodeId:
                    return Variant.From(ReadInlineMatrix(typeInfo, 2,
                        static d => d.ReadExpandedNodeId(null)));
                case BuiltInType.StatusCode:
                    return Variant.From(ReadInlineMatrix(typeInfo, 4,
                        static d => d.ReadStatusCode(null)));
                case BuiltInType.QualifiedName:
                    return Variant.From(ReadInlineMatrix(typeInfo, 6,
                        static d => d.ReadQualifiedName(null)));
                case BuiltInType.LocalizedText:
                    return Variant.From(ReadInlineMatrix(typeInfo, 1,
                        static d => d.ReadLocalizedText(null)));
                case BuiltInType.ExtensionObject:
                    return Variant.From(ReadInlineMatrix(typeInfo, 3,
                        static d => d.ReadExtensionObject(null)));
                case BuiltInType.DataValue:
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                    return Variant.From(ReadInlineMatrix(typeInfo, 1,
                        static d => d.ReadDataValue(null)));
#pragma warning restore CS8620
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                case BuiltInType.Variant:
                    return Variant.From(ReadInlineMatrix(typeInfo, 1,
                        static d => d.ReadVariant(null)));
                case BuiltInType.DiagnosticInfo:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unsupported built in type for inline matrix content ({0}).",
                        typeInfo);
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unexpected inline matrix built in type ({0}).",
                        typeInfo);
            }
        }

        /// <summary>
        /// Reads an inline matrix (OPC 10000-6 5.2.5 Table 28): the Int32
        /// dimensions array followed by the product of the dimensions values
        /// without a length prefix. A null dimensions array is a null matrix
        /// and a dimension &lt;= 0 an empty matrix without values. The
        /// dimensions are attacker controlled: fewer than 2 dimensions, an
        /// overflowing product, a product beyond
        /// <see cref="IServiceMessageContext.MaxArrayLength"/> or beyond the
        /// remaining bytes of the message are rejected before the values are
        /// allocated.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="description">The field type, for diagnostics.</param>
        /// <param name="minElementSize">The minimum number of bytes a single
        /// element takes on the wire, 0 when an element can be empty.</param>
        /// <param name="readElement">Reads a single element.</param>
        /// <exception cref="ServiceResultException"></exception>
        private MatrixOf<T> ReadInlineMatrix<T>(
            object description,
            int minElementSize,
            Func<BinaryDecoder, T> readElement)
        {
            int[]? dimensions = ReadInt32Array(null).ToArray();
            if (dimensions == null)
            {
                return default;
            }
            int count = GetInlineMatrixElementCount(dimensions, minElementSize, description);
            if (count == 0)
            {
                return new MatrixOf<T>(Array.Empty<T>(), dimensions);
            }
            return new MatrixOf<T>(ReadArrayElements(count, readElement), dimensions);
        }

        /// <summary>
        /// Reads an inline matrix of a fixed width primitive type, whose
        /// values follow the dimensions as one block.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private MatrixOf<T> ReadInlineMatrixFixed<T>(object description)
            where T : unmanaged
        {
            int[]? dimensions = ReadInt32Array(null).ToArray();
            if (dimensions == null)
            {
                return default;
            }
            int count = GetInlineMatrixElementCount(
                dimensions,
                Unsafe.SizeOf<T>(),
                description);
            return new MatrixOf<T>(
                count == 0 ? Array.Empty<T>() : ReadFixedWidthArray<T>(count),
                dimensions);
        }

        /// <summary>
        /// Validates the dimensions of an inline matrix read from the wire and
        /// returns the number of values that follow them. A dimension
        /// &lt;= 0 means no values are encoded (OPC 10000-6 5.2.5 Table 28),
        /// it is normalized to 0 in place.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private int GetInlineMatrixElementCount(
            int[] dimensions,
            int minElementSize,
            object description)
        {
            if (dimensions.Length is < 2 or > MatrixOf.MaxMatrixRank)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Inline matrix has {0} dimension(s), 2 to {1} are required ({2}).",
                    dimensions.Length,
                    MatrixOf.MaxMatrixRank,
                    description);
            }

            MatrixOf.NormalizeInlineMatrixDimensions(dimensions);

            // The dimensions are bounded also when the matrix is empty: the
            // product of the non zero dimensions (which bounds each single
            // dimension) must neither overflow nor exceed MaxArrayLength, or
            // a consumer materializing the shape (Array.CreateInstance) of
            // e.g. [100000,100000,0] runs out of memory.
            if (!MatrixOf.TryGetInlineMatrixElementCount(
                dimensions,
                out int count,
                out int shapeLength))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Inline matrix dimensions [{0}] exceed the maximum number of elements ({1}).",
                    string.Join(",", dimensions),
                    description);
            }

            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < shapeLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength exceeded in inline matrix: {0} < {1} ({2})",
                    Context.MaxArrayLength,
                    shapeLength,
                    description);
            }

            // A populated matrix read for a structure field must have the
            // rank the field declares (like the XML and JSON decoders).
            if (description is TypeInfo typeInfo &&
                !MatrixOf.HasInlineMatrixRank(dimensions, count, typeInfo))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Inline matrix dimensions [{0}] do not have the rank of the field ({1}).",
                    string.Join(",", dimensions),
                    typeInfo);
            }

            if (count == 0)
            {
                return 0;
            }

            // Each element takes at least minElementSize bytes: reject a
            // matrix the remaining message cannot hold before allocating it.
            long remaining = GetRemainingLength();
            if (minElementSize > 0 && remaining >= 0 && (long)count * minElementSize > remaining)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Inline matrix dimensions [{0}] need at least {1} bytes, only {2} remain ({3}).",
                    string.Join(",", dimensions),
                    (long)count * minElementSize,
                    remaining,
                    description);
            }
            return count;
        }

        /// <summary>
        /// The number of bytes left to decode, or -1 if unknown.
        /// </summary>
        private long GetRemainingLength()
        {
            if (m_hasBuffer)
            {
                SynchronizeBufferPosition();
                return Math.Max(0, m_buffer.Length - m_bufferPosition);
            }
            Stream stream = m_reader.BaseStream;
            return stream.CanSeek ? Math.Max(0, stream.Length - stream.Position) : -1;
        }

        /// <summary>
        /// Reads a DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfo nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private DiagnosticInfo? ReadDiagnosticInfo(int depth)
        {
            if (depth > DiagnosticInfo.MaxInnerDepth)
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
                    value.SymbolicId = ReadDiagnosticInfoIndex(nameof(DiagnosticInfo.SymbolicId));
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.NamespaceUri) != 0)
                {
                    value.NamespaceUri = ReadDiagnosticInfoIndex(nameof(DiagnosticInfo.NamespaceUri));
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.Locale) != 0)
                {
                    value.Locale = ReadDiagnosticInfoIndex(nameof(DiagnosticInfo.Locale));
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.LocalizedText) != 0)
                {
                    value.LocalizedText = ReadDiagnosticInfoIndex(nameof(DiagnosticInfo.LocalizedText));
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

        private int ReadDiagnosticInfoIndex(string fieldName)
        {
            int value = SafeReadInt32();
            if (value < -1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The DiagnosticInfo {0} index is invalid: {1}.",
                    fieldName,
                    value);
            }

            return value;
        }

        /// <summary>
        /// Reads the length of an array. The length is attacker controlled:
        /// besides <see cref="IServiceMessageContext.MaxArrayLength"/> a length
        /// whose elements (of at least <paramref name="minElementSize"/> bytes
        /// each) the remaining message cannot hold is rejected before anything
        /// is allocated for them.
        /// </summary>
        /// <param name="minElementSize">The minimum number of bytes a single
        /// element takes on the wire, 0 when an element can be empty.</param>
        /// <param name="callerMemberName">The caller, for diagnostics.</param>
        /// <exception cref="ServiceResultException"></exception>
        private int ReadArrayLength(
            int minElementSize,
            [CallerMemberName] string callerMemberName = "")
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

            long remaining = GetRemainingLength();
            if (minElementSize > 0 && remaining >= 0 && (long)length * minElementSize > remaining)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Array length {0} in {1} needs at least {2} bytes, only {3} remain.",
                    length,
                    callerMemberName,
                    (long)length * minElementSize,
                    remaining);
            }

            return length;
        }

        /// <summary>
        /// Reads an array whose elements are read one by one.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="minElementSize">The minimum number of bytes a single
        /// element takes on the wire, 0 when an element can be empty.</param>
        /// <param name="readElement">Reads a single element.</param>
        /// <param name="callerMemberName">The caller, for diagnostics.</param>
        /// <exception cref="ServiceResultException"></exception>
        private ArrayOf<T> ReadArray<T>(
            int minElementSize,
            Func<BinaryDecoder, T> readElement,
            [CallerMemberName] string callerMemberName = "")
        {
            int length = ReadArrayLength(minElementSize, callerMemberName);

            if (length == -1)
            {
                return default;
            }

            return ReadArrayElements(length, readElement);
        }

        /// <summary>
        /// Reads <paramref name="length"/> elements into an array that grows
        /// as the elements are read. The remaining bytes check of the length
        /// does not bound what a nested element allocates: every level of a
        /// Variant, DataValue or ExtensionObject array nested in the first
        /// element of its parent is checked against the same remaining bytes.
        /// Preallocating at most <see cref="kMaxPreallocatedArrayBytes"/> per
        /// level keeps the memory held by such a chain proportional to the
        /// elements actually decoded instead of to the length prefixes.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="length">The validated number of elements.</param>
        /// <param name="readElement">Reads a single element.</param>
        private T[] ReadArrayElements<T>(int length, Func<BinaryDecoder, T> readElement)
        {
            var values = new T[GetInitialArrayCapacity<T>(length)];
            for (int ii = 0; ii < length; ii++)
            {
                if (ii == values.Length)
                {
                    Array.Resize(ref values, (int)Math.Min(length, 2L * values.Length));
                }
                values[ii] = readElement(this);
            }
            return values;
        }

        /// <summary>
        /// The number of elements to allocate up front for an array of
        /// <paramref name="length"/> elements that is grown while reading.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        private static int GetInitialArrayCapacity<T>(int length)
        {
            int maxElements = Math.Max(1, kMaxPreallocatedArrayBytes / Unsafe.SizeOf<T>());
            return Math.Min(length, maxElements);
        }

        /// <summary>
        /// Reads a length prefixed array of a fixed-width unmanaged numeric type.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="callerMemberName">The caller, for diagnostics.</param>
        /// <exception cref="ServiceResultException"></exception>
        private ArrayOf<T> ReadFixedWidthArray<T>(
            [CallerMemberName] string callerMemberName = "")
            where T : unmanaged
        {
            int length = ReadArrayLength(Unsafe.SizeOf<T>(), callerMemberName);

            if (length == -1)
            {
                return default;
            }

            return ReadFixedWidthArray<T>(length);
        }

        /// <summary>
        /// Reads a fixed-width unmanaged numeric array from raw little-endian bytes.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type.</typeparam>
        /// <param name="length">The number of elements to read, checked
        /// against the remaining bytes by the caller.</param>
        private T[] ReadFixedWidthArray<T>(int length) where T : unmanaged
        {
            // The caller verified the length against the remaining bytes. If
            // they are unknown the array grows with the bytes actually read.
            int capacity = GetRemainingLength() >= 0
                ? length
                : GetInitialArrayCapacity<T>(length);
            var values = new T[capacity];
            int read = 0;
            while (true)
            {
                ReadRawBytes(MemoryMarshal.AsBytes(values.AsSpan(read)));
                read = values.Length;
                if (read == length)
                {
                    break;
                }
                Array.Resize(ref values, (int)Math.Min(length, 2L * read));
            }

            if (!BitConverter.IsLittleEndian)
            {
                ReverseFixedWidthElements<T>(values);
            }

            return values;
        }

        private static void ReverseFixedWidthElements<T>(Span<T> values) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            Span<byte> bytes = MemoryMarshal.AsBytes(values);
            for (int offset = 0; offset < bytes.Length; offset += size)
            {
                bytes.Slice(offset, size).Reverse();
            }
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
                    value = new NodeId(ReadString(null) ?? string.Empty, namespaceIndex);
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
        /// Try to read contiguous buffer bytes for optimized bulk decoders.
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="bytes">The buffer span, if this decoder is buffer backed.</param>
        /// <param name="functionName">The name of the calling function.</param>
        /// <returns>True if the bytes were read from the buffer; otherwise false for stream-backed decoders.</returns>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReadBufferBytes(
            int length,
            out ReadOnlySpan<byte> bytes,
            [CallerMemberName] string? functionName = null)
        {
            if (!m_hasBuffer)
            {
                bytes = default;
                return false;
            }

            bytes = SafeReadSpan(length, functionName);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadRawBytes(Span<byte> destination, [CallerMemberName] string? functionName = null)
        {
            if (destination.Length == 0)
            {
                return;
            }

            if (TryReadBufferBytes(destination.Length, out ReadOnlySpan<byte> source, functionName))
            {
                source.CopyTo(destination);
                return;
            }

            int offset = 0;
            while (offset < destination.Length)
            {
                int length;
#if NET6_0_OR_GREATER
                length = m_reader.Read(destination[offset..]);
#else
                byte[] buffer = m_reader.ReadBytes(destination.Length - offset);
                length = buffer.Length;
                buffer.AsSpan().CopyTo(destination[offset..]);
#endif

                if (length == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                        destination.Length,
                        functionName ?? string.Empty,
                        offset);
                }

                offset += length;
            }
        }

        /// <summary>
        /// Read bytes from stream and validate the length of the returned buffer.
        /// Throws decoding error if less than the expected number of bytes were read.
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="functionName">The name of the calling function.</param>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] SafeReadBytes(int length, [CallerMemberName] string? functionName = null)
        {
            if (length == 0)
            {
                return [];
            }

            if (m_hasBuffer)
            {
                return SafeReadSpan(length, functionName).ToArray();
            }

            // BinaryReader.ReadBytes allocates the requested length before it
            // reads: never hand it a length the stream cannot satisfy.
            CheckRemainingBytes(length, functionName);
            if (GetRemainingLength() >= 0)
            {
                byte[] bytes = m_reader.ReadBytes(length);
                if (bytes.Length != length)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                        length,
                        functionName ?? string.Empty,
                        bytes.Length);
                }
                return bytes;
            }

            // The remaining bytes are unknown: grow with the bytes read.
            byte[] buffer = new byte[GetInitialArrayCapacity<byte>(length)];
            int read = 0;
            while (true)
            {
                ReadRawBytes(buffer.AsSpan(read), functionName);
                read = buffer.Length;
                if (read == length)
                {
                    return buffer;
                }
                Array.Resize(ref buffer, (int)Math.Min(length, 2L * read));
            }
        }

        /// <summary>
        /// Rejects a length prefixed value of <paramref name="length"/> bytes
        /// that the rest of the message cannot hold, before the caller
        /// allocates anything for it.
        /// </summary>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        private void CheckRemainingBytes(int length, string? functionName)
        {
            long remaining = GetRemainingLength();
            if (remaining >= 0 && length > remaining)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                    length,
                    functionName ?? string.Empty,
                    remaining);
            }
        }

        /// <summary>
        /// Read bytes into a caller-provided span.
        /// </summary>
        /// <param name="bytes">The destination span.</param>
        /// <param name="functionName">The name of the calling function.</param>
        /// <exception cref="ServiceResultException"> with <see cref="StatusCodes.BadDecodingError"/></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SafeReadBytes(
            Span<byte> bytes,
            [CallerMemberName] string? functionName = null)
        {
            if (bytes.Length == 0)
            {
                return;
            }

            if (m_hasBuffer)
            {
                _ = SafeReadBufferBytes(bytes, functionName);
                return;
            }

            int length;
#if NET6_0_OR_GREATER
            length = m_reader.Read(bytes);
#else
            byte[] buffer = m_reader.ReadBytes(bytes.Length);
            length = buffer.Length;
            buffer.CopyTo(bytes);
#endif

            if (bytes.Length != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                    length,
                    functionName ?? string.Empty,
                    bytes.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int SafeReadBufferBytes(Span<byte> bytes, string? functionName)
        {
            ReadOnlySpan<byte> source = SafeReadSpan(bytes.Length, functionName);
            source.CopyTo(bytes);
            return bytes.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> SafeReadSpan(int length, string? functionName)
        {
            SynchronizeBufferPosition();

            int available = m_bufferPosition <= m_buffer.Length
                ? m_buffer.Length - m_bufferPosition
                : 0;
            if (length > available)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                    length,
                    functionName ?? string.Empty,
                    available);
            }

            ReadOnlySpan<byte> bytes = m_buffer.Span.Slice(m_bufferPosition, length);
            m_bufferPosition += length;
            SynchronizeBaseStreamPosition();
            return bytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> SafeReadPrimitiveSpan(
            int length,
            string dataTypeName,
            string? functionName)
        {
            SynchronizeBufferPosition();

            if (m_bufferPosition > m_buffer.Length || length > m_buffer.Length - m_bufferPosition)
            {
                throw CreateDecodingError(dataTypeName, functionName);
            }

            ReadOnlySpan<byte> bytes = m_buffer.Span.Slice(m_bufferPosition, length);
            m_bufferPosition += length;
            SynchronizeBaseStreamPosition();
            return bytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeBufferPosition()
        {
            if (!m_baseStreamExposed)
            {
                return;
            }

            long position = m_reader.BaseStream.Position;
            if (position == m_synchronizedStreamPosition)
            {
                return;
            }

            if (position > int.MaxValue)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Stream Position exceeds int.MaxValue or int.MinValue.");
            }

            m_bufferPosition = (int)position;
            m_synchronizedStreamPosition = position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeBaseStreamPosition()
        {
            if (!m_baseStreamExposed)
            {
                return;
            }

            m_reader.BaseStream.Position = m_bufferPosition;
            m_synchronizedStreamPosition = m_bufferPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetPosition(int position)
        {
            if (m_hasBuffer)
            {
                m_bufferPosition = position;
                if (m_baseStreamExposed)
                {
                    m_reader.BaseStream.Position = position;
                    m_synchronizedStreamPosition = position;
                }
                return;
            }

            m_reader.BaseStream.Position = position;
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
            [CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return SafeReadBufferBytes(bytes, functionName);
            }

            int length = m_reader.Read(bytes);

            if (bytes.Length != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Reading {0} bytes of {1} reached end of stream after {2} bytes.",
                    length,
                    functionName ?? string.Empty,
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
        private bool SafeReadBoolean([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return SafeReadPrimitiveSpan(1, nameof(ReadBoolean), functionName)[0] != 0;
            }

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
        private sbyte SafeReadSByte([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return unchecked((sbyte)SafeReadPrimitiveSpan(1, nameof(ReadSByte), functionName)[0]);
            }

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
        private byte SafeReadByte([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return SafeReadPrimitiveSpan(1, nameof(ReadByte), functionName)[0];
            }

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
        private short SafeReadInt16([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return BinaryPrimitives.ReadInt16LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(short), nameof(ReadInt16), functionName));
            }

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
        private ushort SafeReadUInt16([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return BinaryPrimitives.ReadUInt16LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(ushort), nameof(ReadUInt16), functionName));
            }

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
        private int SafeReadInt32([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return BinaryPrimitives.ReadInt32LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(int), nameof(ReadInt32), functionName));
            }

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
        private uint SafeReadUInt32([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(uint), nameof(ReadUInt32), functionName));
            }

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
        private long SafeReadInt64([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return BinaryPrimitives.ReadInt64LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(long), nameof(ReadInt64), functionName));
            }

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
        private ulong SafeReadUInt64([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                return BinaryPrimitives.ReadUInt64LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(ulong), nameof(ReadUInt64), functionName));
            }

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
        private float SafeReadFloat([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                int bits = BinaryPrimitives.ReadInt32LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(float), nameof(ReadFloat), functionName));
                return Unsafe.As<int, float>(ref bits);
            }

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
        private double SafeReadDouble([CallerMemberName] string? functionName = null)
        {
            if (m_hasBuffer)
            {
                long bits = BinaryPrimitives.ReadInt64LittleEndian(
                    SafeReadPrimitiveSpan(sizeof(double), nameof(ReadDouble), functionName));
                return Unsafe.As<long, double>(ref bits);
            }

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
            string? functionName)
        {
            return ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Reading {0} in {1} reached end of stream.",
                dataTypeName,
                functionName ?? string.Empty);
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
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
        private readonly ReadOnlyMemory<byte> m_buffer;
        private int m_bufferPosition;
        private long m_synchronizedStreamPosition;
        private uint m_nestingLevel;

        // The end position of the ExtensionObject body being decoded, or -1
        // when none is. See TryReadRemainingBodyBytes.
        private int m_bodyEnd = -1;
        private uint m_encodeablesRecovered;

        // The most bytes allocated up front for an array read element by
        // element, see ReadArrayElements. Below the large object heap limit.
        private const int kMaxPreallocatedArrayBytes = 16 * 1024;

        // The reserved Variant built-in type ids, OPC 10000-6 5.2.2.16.
        private const int kFirstReservedVariantTypeId = 26;
        private const int kLastReservedVariantTypeId = 31;

        // The largest valid DataValue picoseconds value, OPC 10000-6 5.2.2.17.
        private const ushort kMaxPicoseconds = 9999;
        private readonly bool m_hasBuffer;
        private bool m_baseStreamExposed;
        private ILogger Logger => m_logger ??= Context.Telemetry.CreateLogger<BinaryDecoder>();
        private ILogger? m_logger;
    }
}
