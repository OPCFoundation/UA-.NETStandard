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
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Decodes objects from a UA Binary encoded stream.
    /// </summary>
    public class BinaryDecoder : IDecoder
    {
        #region Constructor
        /// <summary>
        /// Creates a decoder that reads from a memory buffer.
        /// </summary>
        public BinaryDecoder(byte[] buffer, IServiceMessageContext context)
            : this(buffer, 0, buffer.Length, context)
        {
        }

        /// <summary>
        /// Creates a decoder that reads from an ArraySegment.
        /// </summary>
        public BinaryDecoder(ArraySegment<byte> buffer, IServiceMessageContext context)
            : this(buffer.Array, buffer.Offset, buffer.Count, context)
        {
        }

        /// <summary>
        /// Creates a decoder that reads from a memory buffer.
        /// </summary>
        public BinaryDecoder(byte[] buffer, int start, int count, IServiceMessageContext context)
        {
            var stream = new MemoryStream(buffer, start, count, false);
            m_reader = new BinaryReader(stream);
            Initialize(context);
        }

        /// <summary>
        /// Creates a decoder that reads from a stream.
        /// </summary>
        public BinaryDecoder(Stream stream, IServiceMessageContext context, bool leaveOpen = false)
        {
            ValidateStreamRequirements(stream);
            m_reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
            Initialize(context);
        }

        /// <summary>
        /// Initializes the object.
        /// </summary>
        private void Initialize(IServiceMessageContext context)
        {
            m_context = context;
            m_nestingLevel = 0;
            m_encodeablesRecovered = 0;
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
                Utils.SilentDispose(m_reader);
                m_reader = null;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the tables used to map namespace and server uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being decoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being decoded.</param>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && m_context.NamespaceUris != null)
            {
                m_namespaceMappings = m_context.NamespaceUris.CreateMapping(namespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && m_context.ServerUris != null)
            {
                m_serverMappings = m_context.ServerUris.CreateMapping(serverUris, false);
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
        public int Position
        {
            get
            {
                var stream = BaseStream;
                if (stream?.CanSeek != true)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Stream does not support seeking.");
                }
                long position = (stream?.Position ?? 0);
                if (position > int.MaxValue || position < int.MinValue)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Stream Position exceeds int.MaxValue or int.MinValue.");
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
        public static IEncodeable DecodeMessage(Stream stream, System.Type expectedType, IServiceMessageContext context)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (context == null) throw new ArgumentNullException(nameof(context));

            using (var decoder = new BinaryDecoder(stream, context))
            {
                return decoder.DecodeMessage(expectedType);
            }
        }

        /// <summary>
        /// Decodes a session-less message from a buffer.
        /// </summary>
        public static IEncodeable DecodeSessionLessMessage(byte[] buffer, IServiceMessageContext context)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (context == null) throw new ArgumentNullException(nameof(context));

            using (var decoder = new BinaryDecoder(buffer, context))
            {
                // read the node id.
                NodeId typeId = decoder.ReadNodeId(null);

                // convert to absolute node id.
                ExpandedNodeId absoluteId = NodeId.ToExpandedNodeId(typeId, context.NamespaceUris);

                // lookup message session-less envelope type.
                Type actualType = context.Factory.GetSystemType(absoluteId);

                if (actualType == null || actualType != typeof(SessionlessInvokeRequestType))
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                        "Cannot decode session-less service message with type id: {0}.", absoluteId);
                }

                // decode the actual message.
                SessionLessServiceMessage message = new SessionLessServiceMessage();

                message.Decode(decoder);

                decoder.Close();

                return message.Message;
            }
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        public static IEncodeable DecodeMessage(byte[] buffer, Type expectedType, IServiceMessageContext context)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (context == null) throw new ArgumentNullException(nameof(context));

            using (var decoder = new BinaryDecoder(buffer, context))
            {
                return decoder.DecodeMessage(expectedType);
            }
        }

        /// <summary>
        /// Decodes an object from a buffer.
        /// </summary>
        public IEncodeable DecodeMessage(System.Type expectedType)
        {
            int start = Position;

            // read the node id.
            NodeId typeId = ReadNodeId(null);

            // convert to absolute node id.
            ExpandedNodeId absoluteId = NodeId.ToExpandedNodeId(typeId, m_context.NamespaceUris);

            // lookup message type.
            Type actualType = m_context.Factory.GetSystemType(absoluteId);

            if (actualType == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Cannot decode message with type id: {0}.", absoluteId);
            }

            // read the message.
            IEncodeable message = ReadEncodeable(null, actualType, absoluteId);

            // check that the max message size was not exceeded.
            int messageLength = Position - start;
            if (m_context.MaxMessageSize > 0 && m_context.MaxMessageSize < messageLength)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}", m_context.MaxMessageSize, messageLength);
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
        #endregion

        #region IDecoder Members
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Binary;

        /// <summary>
        /// The message context associated with the decoder.
        /// </summary>
        public IServiceMessageContext Context => m_context;

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
        /// Reads a boolean from the stream.
        /// </summary>
        public bool ReadBoolean(string fieldName)
        {
            return SafeReadBoolean();
        }

        /// <summary>
        /// Reads a sbyte from the stream.
        /// </summary>
        public sbyte ReadSByte(string fieldName)
        {
            return SafeReadSByte();
        }

        /// <summary>
        /// Reads a byte from the stream.
        /// </summary>
        public byte ReadByte(string fieldName)
        {
            return SafeReadByte();
        }

        /// <summary>
        /// Reads a short from the stream.
        /// </summary>
        public short ReadInt16(string fieldName)
        {
            return SafeReadInt16();
        }

        /// <summary>
        /// Reads a ushort from the stream.
        /// </summary>
        public ushort ReadUInt16(string fieldName)
        {
            return SafeReadUInt16();
        }

        /// <summary>
        /// Reads an int from the stream.
        /// </summary>
        public int ReadInt32(string fieldName)
        {
            return SafeReadInt32();
        }

        /// <summary>
        /// Reads a uint from the stream.
        /// </summary>
        public uint ReadUInt32(string fieldName)
        {
            return SafeReadUInt32();
        }

        /// <summary>
        /// Reads a long from the stream.
        /// </summary>
        public long ReadInt64(string fieldName)
        {
            return SafeReadInt64();
        }

        /// <summary>
        /// Reads a ulong from the stream.
        /// </summary>
        public ulong ReadUInt64(string fieldName)
        {
            return SafeReadUInt64();
        }

        /// <summary>
        /// Reads a float from the stream.
        /// </summary>
        public float ReadFloat(string fieldName)
        {
            return SafeReadFloat();
        }

        /// <summary>
        /// Reads a double from the stream.
        /// </summary>
        public double ReadDouble(string fieldName)
        {
            return SafeReadDouble();
        }

        /// <summary>
        /// Reads a string from the stream.
        /// </summary>
        public string ReadString(string fieldName)
        {
            return ReadString(fieldName, m_context.MaxStringLength);
        }

        /// <summary>
        /// Reads a string from the stream (throws an exception if
        /// its length is invalid or exceeds the limit specified).
        /// </summary>
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
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}", maxStringLength, length);
            }

            byte[] bytes = SafeReadBytes(length);

            // length is always >= 1 here

            // If 0 terminated, decrease length by one before converting to string
            var utf8StringLength = bytes[bytes.Length - 1] == 0 ? bytes.Length - 1 : bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, utf8StringLength);
        }

        /// <summary>
        /// Reads a UTC date/time from the stream.
        /// </summary>
        public DateTime ReadDateTime(string fieldName)
        {
            long ticks = SafeReadInt64();

            if (ticks >= (Int64.MaxValue - Utils.TimeBase.Ticks))
            {
                return DateTime.MaxValue;
            }

            ticks += Utils.TimeBase.Ticks;

            if (ticks >= DateTime.MaxValue.Ticks)
            {
                return DateTime.MaxValue;
            }

            if (ticks <= Utils.TimeBase.Ticks)
            {
                return DateTime.MinValue;
            }

            return new DateTime(ticks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Reads a GUID from the stream.
        /// </summary>
        public Uuid ReadGuid(string fieldName)
        {
            const int kGuidLength = 16;
            byte[] bytes = SafeReadBytes(kGuidLength);
            return new Uuid(new Guid(bytes));
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        public byte[] ReadByteString(string fieldName)
        {
            return ReadByteString(fieldName, m_context.MaxByteStringLength);
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        public byte[] ReadByteString(string fieldName, int maxByteStringLength)
        {
            int length = SafeReadInt32();

            if (length < 0)
            {
                return null;
            }

            if (maxByteStringLength > 0 && maxByteStringLength < length)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}", maxByteStringLength, length);
            }

            return SafeReadBytes(length);
        }

        /// <summary>
        /// Reads an XmlElement from the stream.
        /// </summary>
        public XmlElement ReadXmlElement(string fieldName)
        {
            byte[] bytes = ReadByteString(fieldName);

            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            XmlDocument document = new XmlDocument();

            try
            {
                // If 0 terminated, decrease length by one before converting to string
                var utf8StringLength = bytes[bytes.Length - 1] == 0 ? bytes.Length - 1 : bytes.Length;
                string xmlString = Encoding.UTF8.GetString(bytes, 0, utf8StringLength);
                using (StringReader stream = new StringReader(xmlString))
                using (XmlReader reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings()))
                {
                    document.Load(reader);
                }
            }
            catch (XmlException)
            {
                return null;
            }

            return document.DocumentElement;
        }

        /// <summary>
        /// Reads an NodeId from the stream.
        /// </summary>
        public NodeId ReadNodeId(string fieldName)
        {
            byte encodingByte = SafeReadByte();

            NodeId value = new NodeId();

            ReadNodeIdBody(encodingByte, value);

            if (m_namespaceMappings != null && m_namespaceMappings.Length > value.NamespaceIndex)
            {
                value.SetNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
            }

            return value;
        }

        /// <summary>
        /// Reads an ExpandedNodeId from the stream.
        /// </summary>
        public ExpandedNodeId ReadExpandedNodeId(string fieldName)
        {
            byte encodingByte = SafeReadByte();

            ExpandedNodeId value = new ExpandedNodeId();

            NodeId body = new NodeId();
            ReadNodeIdBody(encodingByte, body);
            value.InnerNodeId = body;

            string namespaceUri = null;
            uint serverIndex = 0;

            // read the namespace uri if present.
            if ((encodingByte & 0x80) != 0)
            {
                namespaceUri = ReadString(null);
                value.SetNamespaceUri(namespaceUri);
            }

            // read the server index if present.
            if ((encodingByte & 0x40) != 0)
            {
                serverIndex = SafeReadUInt32();
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

        /// <summary>
        /// Reads an StatusCode from the stream.
        /// </summary>
        public StatusCode ReadStatusCode(string fieldName)
        {
            return SafeReadUInt32();
        }

        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// </summary>
        public DiagnosticInfo ReadDiagnosticInfo(string fieldName)
        {
            return ReadDiagnosticInfo(fieldName, 0);
        }

        /// <summary>
        /// Reads an QualifiedName from the stream.
        /// </summary>
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

        /// <summary>
        /// Reads an LocalizedText from the stream.
        /// </summary>
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

        /// <summary>
        /// Reads an Variant from the stream.
        /// </summary>
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

        /// <summary>
        /// Reads an DataValue from the stream.
        /// </summary>
        public DataValue ReadDataValue(string fieldName)
        {
            // read the encoding byte.
            byte encodingByte = SafeReadByte();

            DataValue value = new DataValue();

            // read the fields of the DataValue structure.
            if ((encodingByte & (byte)DataValueEncodingBits.Value) != 0)
            {
                value.WrappedValue = ReadVariant(null);
            }

            if ((encodingByte & (byte)DataValueEncodingBits.StatusCode) != 0)
            {
                value.StatusCode = ReadStatusCode(null);
            }

            if ((encodingByte & (byte)DataValueEncodingBits.SourceTimestamp) != 0)
            {
                value.SourceTimestamp = ReadDateTime(null);
            }

            if ((encodingByte & (byte)DataValueEncodingBits.SourcePicoseconds) != 0)
            {
                value.SourcePicoseconds = ReadUInt16(null);
            }

            if ((encodingByte & (byte)DataValueEncodingBits.ServerTimestamp) != 0)
            {
                value.ServerTimestamp = ReadDateTime(null);
            }

            if ((encodingByte & (byte)DataValueEncodingBits.ServerPicoseconds) != 0)
            {
                value.ServerPicoseconds = ReadUInt16(null);
            }

            return value;
        }

        /// <summary>
        /// Reads an ExtensionObject from the stream.
        /// </summary>
        public ExtensionObject ReadExtensionObject(string fieldName)
        {
            return ReadExtensionObject();
        }

        /// <summary>
        /// Reads an encodeable object from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable object field name</param>
        /// <param name="systemType">The system type of the encodeable object to be read</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instance that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> object that was read from the stream.</returns>
        public IEncodeable ReadEncodeable(string fieldName, System.Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null) throw new ArgumentNullException(nameof(systemType));


            if (!(Activator.CreateInstance(systemType) is IEncodeable encodeable))
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Cannot decode type '{0}'.", systemType.FullName);
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

        /// <summary>
        ///  Reads an enumerated value from the stream.
        /// </summary>
        public Enum ReadEnumerated(string fieldName, System.Type enumType)
        {
            return (Enum)Enum.ToObject(enumType, SafeReadInt32());
        }

        /// <summary>
        /// Reads a boolean array from the stream.
        /// </summary>
        public BooleanCollection ReadBooleanArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            BooleanCollection values = new BooleanCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadBoolean(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a sbyte array from the stream.
        /// </summary>
        public SByteCollection ReadSByteArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            SByteCollection values = new SByteCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadSByte());
            }

            return values;
        }

        /// <summary>
        /// Reads a byte array from the stream.
        /// </summary>
        public ByteCollection ReadByteArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            ByteCollection values = new ByteCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadByte());
            }

            return values;
        }

        /// <summary>
        /// Reads a short array from the stream.
        /// </summary>
        public Int16Collection ReadInt16Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            Int16Collection values = new Int16Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadInt16(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a ushort array from the stream.
        /// </summary>
        public UInt16Collection ReadUInt16Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            UInt16Collection values = new UInt16Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadUInt16(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a int array from the stream.
        /// </summary>
        public Int32Collection ReadInt32Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            Int32Collection values = new Int32Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadInt32(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a uint array from the stream.
        /// </summary>
        public UInt32Collection ReadUInt32Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            UInt32Collection values = new UInt32Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(SafeReadUInt32());
            }

            return values;
        }

        /// <summary>
        /// Reads a long array from the stream.
        /// </summary>
        public Int64Collection ReadInt64Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            Int64Collection values = new Int64Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadInt64(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a ulong array from the stream.
        /// </summary>
        public UInt64Collection ReadUInt64Array(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            UInt64Collection values = new UInt64Collection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadUInt64(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a float array from the stream.
        /// </summary>
        public FloatCollection ReadFloatArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            FloatCollection values = new FloatCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadFloat(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a double array from the stream.
        /// </summary>
        public DoubleCollection ReadDoubleArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            DoubleCollection values = new DoubleCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadDouble(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a string array from the stream.
        /// </summary>
        public StringCollection ReadStringArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            StringCollection values = new StringCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadString(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a UTC date/time array from the stream.
        /// </summary>
        public DateTimeCollection ReadDateTimeArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            DateTimeCollection values = new DateTimeCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadDateTime(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a GUID array from the stream.
        /// </summary>
        public UuidCollection ReadGuidArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            UuidCollection values = new UuidCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadGuid(null));
            }

            return values;
        }

        /// <summary>
        /// Reads a byte string array from the stream.
        /// </summary>
        public ByteStringCollection ReadByteStringArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            ByteStringCollection values = new ByteStringCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadByteString(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an XmlElement array from the stream.
        /// </summary>
        public XmlElementCollection ReadXmlElementArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            XmlElementCollection values = new XmlElementCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadXmlElement(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an NodeId array from the stream.
        /// </summary>
        public NodeIdCollection ReadNodeIdArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            NodeIdCollection values = new NodeIdCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadNodeId(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an ExpandedNodeId array from the stream.
        /// </summary>
        public ExpandedNodeIdCollection ReadExpandedNodeIdArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            ExpandedNodeIdCollection values = new ExpandedNodeIdCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadExpandedNodeId(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an StatusCode array from the stream.
        /// </summary>
        public StatusCodeCollection ReadStatusCodeArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            StatusCodeCollection values = new StatusCodeCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadStatusCode(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an DiagnosticInfo array from the stream.
        /// </summary>
        public DiagnosticInfoCollection ReadDiagnosticInfoArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            DiagnosticInfoCollection values = new DiagnosticInfoCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadDiagnosticInfo(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an QualifiedName array from the stream.
        /// </summary>
        public QualifiedNameCollection ReadQualifiedNameArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            QualifiedNameCollection values = new QualifiedNameCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadQualifiedName(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an LocalizedText array from the stream.
        /// </summary>
        public LocalizedTextCollection ReadLocalizedTextArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            LocalizedTextCollection values = new LocalizedTextCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadLocalizedText(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an Variant array from the stream.
        /// </summary>
        public VariantCollection ReadVariantArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            VariantCollection values = new VariantCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadVariant(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an DataValue array from the stream.
        /// </summary>
        public DataValueCollection ReadDataValueArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            DataValueCollection values = new DataValueCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadDataValue(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an extension object array from the stream.
        /// </summary>
        public ExtensionObjectCollection ReadExtensionObjectArray(string fieldName)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            ExtensionObjectCollection values = new ExtensionObjectCollection(length);

            for (int ii = 0; ii < length; ii++)
            {
                values.Add(ReadExtensionObject(null));
            }

            return values;
        }

        /// <summary>
        /// Reads an encodeable array from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable array field name</param>
        /// <param name="systemType">The system type of the encodeable objects to be read object</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instances that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> array that was read from the stream.</returns>
        public Array ReadEncodeableArray(string fieldName, System.Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            Array values = Array.CreateInstance(systemType, length);

            for (int ii = 0; ii < length; ii++)
            {
                values.SetValue(ReadEncodeable(null, systemType, encodeableTypeId), ii);
            }

            return values;
        }

        /// <summary>
        /// Reads an enumerated value array from the stream.
        /// </summary>
        public Array ReadEnumeratedArray(string fieldName, System.Type enumType)
        {
            int length = ReadArrayLength();

            if (length == -1)
            {
                return null;
            }

            Array values = Array.CreateInstance(enumType, length);

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
                        return ReadBooleanArray(fieldName).ToArray();
                    case BuiltInType.SByte:
                        return ReadSByteArray(fieldName).ToArray();
                    case BuiltInType.Byte:
                        return ReadByteArray(fieldName).ToArray();
                    case BuiltInType.Int16:
                        return ReadInt16Array(fieldName).ToArray();
                    case BuiltInType.UInt16:
                        return ReadUInt16Array(fieldName).ToArray();
                    case BuiltInType.Enumeration:
                    {
                        DetermineIEncodeableSystemType(ref systemType, encodeableTypeId);
                        if (systemType?.IsEnum == true)
                        {
                            return ReadEnumeratedArray(fieldName, systemType);
                        }
                        // if system type is not known or not an enum, fall back to Int32
                        goto case BuiltInType.Int32;
                    }
                    case BuiltInType.Int32:
                        return ReadInt32Array(fieldName).ToArray();
                    case BuiltInType.UInt32:
                        return ReadUInt32Array(fieldName).ToArray();
                    case BuiltInType.Int64:
                        return ReadInt64Array(fieldName).ToArray();
                    case BuiltInType.UInt64:
                        return ReadUInt64Array(fieldName).ToArray();
                    case BuiltInType.Float:
                        return ReadFloatArray(fieldName).ToArray();
                    case BuiltInType.Double:
                        return ReadDoubleArray(fieldName).ToArray();
                    case BuiltInType.String:
                        return ReadStringArray(fieldName).ToArray();
                    case BuiltInType.DateTime:
                        return ReadDateTimeArray(fieldName).ToArray();
                    case BuiltInType.Guid:
                        return ReadGuidArray(fieldName).ToArray();
                    case BuiltInType.ByteString:
                        return ReadByteStringArray(fieldName).ToArray();
                    case BuiltInType.XmlElement:
                        return ReadXmlElementArray(fieldName).ToArray();
                    case BuiltInType.NodeId:
                        return ReadNodeIdArray(fieldName).ToArray();
                    case BuiltInType.ExpandedNodeId:
                        return ReadExpandedNodeIdArray(fieldName).ToArray();
                    case BuiltInType.StatusCode:
                        return ReadStatusCodeArray(fieldName).ToArray();
                    case BuiltInType.QualifiedName:
                        return ReadQualifiedNameArray(fieldName).ToArray();
                    case BuiltInType.LocalizedText:
                        return ReadLocalizedTextArray(fieldName).ToArray();
                    case BuiltInType.DataValue:
                        return ReadDataValueArray(fieldName).ToArray();
                    case BuiltInType.Variant:
                    {
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }
                        return ReadVariantArray(fieldName).ToArray();
                    }
                    case BuiltInType.ExtensionObject:
                        return ReadExtensionObjectArray(fieldName).ToArray();
                    case BuiltInType.DiagnosticInfo:
                        return ReadDiagnosticInfoArray(fieldName).ToArray();
                    default:
                    {
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }
                        throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                            "Cannot decode unknown type in Array object with BuiltInType: {0}.", builtInType);
                    }
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
                    (_, int length) = Matrix.ValidateDimensions(false, dimensions, Context.MaxArrayLength);

                    // read the elements
                    Array elements = null;
                    if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                    {
                        elements = Array.CreateInstance(systemType, length);
                        for (int i = 0; i < length; i++)
                        {
                            IEncodeable element = ReadEncodeable(null, systemType, encodeableTypeId);
                            elements.SetValue(Convert.ChangeType(element, systemType, CultureInfo.InvariantCulture), i);
                        }
                    }

                    if (elements == null)
                    {
                        elements = ReadArrayElements(length, builtInType);
                    }

                    if (elements == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                            "Unexpected null Array for multidimensional matrix with {0} elements.", length);
                    }

                    if (builtInType == BuiltInType.Enumeration && systemType?.IsEnum == true)
                    {
                        var newElements = Array.CreateInstance(systemType, elements.Length);
                        int ii = 0;
                        foreach (var element in elements)
                        {
                            newElements.SetValue(Enum.ToObject(systemType, element), ii++);
                        }
                        elements = newElements;
                    }

                    return new Matrix(elements, builtInType, dimensions.ToArray()).ToArray();
                }
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Unexpected null or empty Dimensions for multidimensional matrix.");
            }
            return null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads a DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfo nesting level.
        /// </summary>
        private DiagnosticInfo ReadDiagnosticInfo(string fieldName, int depth)
        {
            if (depth >= DiagnosticInfo.MaxInnerDepth)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
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

                DiagnosticInfo value = new DiagnosticInfo();

                // read the fields of the diagnostic info structure.
                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.SymbolicId) != 0)
                {
                    value.SymbolicId = ReadInt32(null);
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.NamespaceUri) != 0)
                {
                    value.NamespaceUri = ReadInt32(null);
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.Locale) != 0)
                {
                    value.Locale = ReadInt32(null);
                }

                if ((encodingByte & (byte)DiagnosticInfoEncodingBits.LocalizedText) != 0)
                {
                    value.LocalizedText = ReadInt32(null);
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
                    value.InnerDiagnosticInfo = ReadDiagnosticInfo(null, depth + 1);
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
        private bool DetermineIEncodeableSystemType(ref Type systemType, ExpandedNodeId encodeableTypeId)
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
        private Array ReadArrayElements(int length, BuiltInType builtInType)
        {
            Array array = null;
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                {
                    bool[] values = new bool[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadBoolean(null);
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
                        values[ii] = ReadInt16(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.UInt16:
                {
                    ushort[] values = new ushort[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadUInt16(null);
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
                        values[ii] = ReadInt32(null);
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
                        values[ii] = ReadInt64(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.UInt64:
                {
                    ulong[] values = new ulong[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadUInt64(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.Float:
                {
                    float[] values = new float[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadFloat(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.Double:
                {
                    double[] values = new double[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadDouble(null);
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
                    DateTime[] values = new DateTime[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadDateTime(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.Guid:
                {
                    Uuid[] values = new Uuid[length];

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
                    try
                    {
                        XmlElement[] values = new XmlElement[length];

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = ReadXmlElement(null);
                        }

                        array = values;
                    }
                    catch (Exception ex)
                    {
                        Utils.LogError(ex, "Error reading array of XmlElement.");
                    }

                    break;
                }

                case BuiltInType.NodeId:
                {
                    NodeId[] values = new NodeId[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadNodeId(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.ExpandedNodeId:
                {
                    ExpandedNodeId[] values = new ExpandedNodeId[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadExpandedNodeId(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.StatusCode:
                {
                    StatusCode[] values = new StatusCode[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadStatusCode(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.QualifiedName:
                {
                    QualifiedName[] values = new QualifiedName[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadQualifiedName(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.LocalizedText:
                {
                    LocalizedText[] values = new LocalizedText[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadLocalizedText(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.ExtensionObject:
                {
                    ExtensionObject[] values = new ExtensionObject[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadExtensionObject();
                    }

                    array = values;
                    break;
                }

                case BuiltInType.DataValue:
                {
                    DataValue[] values = new DataValue[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadDataValue(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.Variant:
                {
                    Variant[] values = new Variant[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadVariant(null);
                    }

                    array = values;
                    break;
                }

                case BuiltInType.DiagnosticInfo:
                {
                    DiagnosticInfo[] values = new DiagnosticInfo[length];

                    for (int ii = 0; ii < values.Length; ii++)
                    {
                        values[ii] = ReadDiagnosticInfo(null);
                    }

                    array = values;
                    break;
                }

                default:
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                        "Cannot decode unknown type in Variant object with BuiltInType: {0}.", builtInType);
                }
            }

            return array;
        }

        /// <summary>
        /// Reads the length of an array.
        /// </summary>
        private int ReadArrayLength()
        {
            int length = SafeReadInt32();

            if (length < 0)
            {
                return -1;
            }

            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < length)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength {0} < {1}", m_context.MaxArrayLength, length);
            }

            return length;
        }

        /// <summary>
        /// Reads the body of a node id.
        /// </summary>
        private void ReadNodeIdBody(byte encodingByte, NodeId value)
        {
            switch ((NodeIdEncodingBits)(encodingByte & 0x3F))
            {
                case NodeIdEncodingBits.TwoByte:
                {
                    value.SetNamespaceIndex(0);
                    value.SetIdentifier(IdType.Numeric, (uint)SafeReadByte());
                    break;
                }

                case NodeIdEncodingBits.FourByte:
                {
                    value.SetNamespaceIndex(SafeReadByte());
                    value.SetIdentifier(IdType.Numeric, (uint)SafeReadUInt16());
                    break;
                }

                case NodeIdEncodingBits.Numeric:
                {
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.Numeric, SafeReadUInt32());
                    break;
                }

                case NodeIdEncodingBits.String:
                {
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.String, ReadString(null));
                    break;
                }

                case NodeIdEncodingBits.Guid:
                {
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.Guid, (Guid)ReadGuid(null));
                    break;
                }

                case NodeIdEncodingBits.ByteString:
                {
                    value.SetNamespaceIndex(SafeReadUInt16());
                    value.SetIdentifier(IdType.Opaque, ReadByteString(null));
                    break;
                }

                default:
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                        "Invalid encoding byte (0x{0:X2}) for NodeId.", encodingByte);
                }
            }
        }

        /// <summary>
        /// Reads an extension object from the stream.
        /// </summary>
        private ExtensionObject ReadExtensionObject()
        {
            ExtensionObject extension = new ExtensionObject();

            // read type id.
            NodeId typeId = ReadNodeId(null);

            // convert to absolute node id.
            extension.TypeId = NodeId.ToExpandedNodeId(typeId, m_context.NamespaceUris);

            if (!NodeId.IsNull(typeId) && NodeId.IsNull(extension.TypeId))
            {
                Utils.LogWarning(
                    "Cannot deserialize extension objects if the NamespaceUri is not in the NamespaceTable: Type = {0}",
                    typeId);
            }

            // read encoding.
            ExtensionObjectEncoding encoding = (ExtensionObjectEncoding)Enum.ToObject(typeof(ExtensionObjectEncoding), SafeReadByte());

            // nothing more to do for empty bodies.
            if (encoding == ExtensionObjectEncoding.None)
            {
                return extension;
            }

            // check for known type.
            Type systemType = m_context.Factory.GetSystemType(extension.TypeId);

            // check for XML bodies.
            if (encoding == ExtensionObjectEncoding.Xml)
            {
                extension.Body = ReadXmlElement(null);

                // attempt to decode a known type.
                if (systemType != null && extension.Body != null)
                {
                    XmlElement element = extension.Body as XmlElement;
                    using (XmlDecoder xmlDecoder = new XmlDecoder(element, this.Context))
                    {
                        try
                        {
                            xmlDecoder.PushNamespace(element.NamespaceURI);
                            IEncodeable body = xmlDecoder.ReadEncodeable(element.LocalName, systemType, extension.TypeId);
                            xmlDecoder.PopNamespace();

                            // update body.
                            extension.Body = body;
                        }
                        catch (Exception e)
                        {
                            Utils.LogError("Could not decode known type {0} encoded as Xml. Error={1}, Value={2}", systemType.FullName, e.Message, element.OuterXml);
                        }
                    }
                }

                return extension;
            }

            // get the length.
            int length = ReadInt32(null);

            // save the current position.
            int start = Position;

            // create instance of type.
            IEncodeable encodeable = null;
            if (systemType != null && length >= 0)
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
                    if (length != used)
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
                catch (ServiceResultException sre) when
                    ((sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded) || (sre.StatusCode == StatusCodes.BadDecodingError))
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
                        m_encodeablesRecovered >= m_context.MaxDecoderRecoveries)
                    {
                        throw exception ??
                            ServiceResultException.Create(StatusCodes.BadDecodingError, "{0}, failed to decode encodeable type '{1}', NodeId='{2}'.",
                                errorMessage, systemType.Name, extension.TypeId);
                    }
                    else if (m_encodeablesRecovered == 0)
                    {
                        // log the error only once to avoid flooding the log.
                        Utils.LogWarning(exception, "{0}, failed to decode encodeable type '{1}', NodeId='{2}'. BinaryDecoder recovered.",
                            errorMessage, systemType.Name, extension.TypeId);
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
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                        "Cannot determine length of unknown extension object body with type '{0}'.", extension.TypeId);
                }

                // check the length.
                if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < length)
                {
                    throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                        "MaxByteStringLength {0} < {1}", m_context.MaxByteStringLength, length);
                }

                // read the bytes of the body.
                extension.Body = SafeReadBytes(length);

                return extension;
            }

            // any unread data indicates a decoding error.
            long unused = length - (Position - start);
            if (unused > 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Cannot skip {0} bytes of unknown extension object body with type '{1}'.", unused, extension.TypeId);
            }

            if (encodeable != null)
            {
                // Set the known TypeId for encodeables.
                extension.TypeId = encodeable.TypeId;
            }

            extension.Body = encodeable;
            return extension;
        }

        /// <summary>
        /// Reads an Variant from the stream.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private Variant ReadVariantValue(string fieldName)
        {
            // read the encoding byte.
            byte encodingByte = SafeReadByte();

            Variant value = new Variant();

            if ((encodingByte & (byte)VariantArrayEncodingBits.Array) != 0)
            {
                // read the array length.
                int length = ReadArrayLength();

                if (length < 0)
                {
                    return value;
                }

                BuiltInType builtInType = (BuiltInType)(encodingByte & (byte)VariantArrayEncodingBits.TypeMask);

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
                            throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                                "ArrayDimensions not specified when ArrayDimensions encoding bit was set in Variant object.");
                        }

                        int[] dimensionsArray = dimensions.ToArray();
                        (bool valid, int matrixLength) = Matrix.ValidateDimensions(dimensionsArray, length, Context.MaxArrayLength);

                        if (!valid || (matrixLength != length))
                        {
                            throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                                "ArrayDimensions length does not match with the ArrayLength in Variant object.");
                        }

                        value = new Variant(new Matrix(array, builtInType, dimensions.ToArray()));
                    }
                    else
                    {
                        value = new Variant(array, new TypeInfo(builtInType, 1));
                    }
                }
            }
            else
            {
                switch ((BuiltInType)encodingByte)
                {
                    case BuiltInType.Null:
                    {
                        value.Value = null;
                        break;
                    }

                    case BuiltInType.Boolean:
                    {
                        value.Set(ReadBoolean(null));
                        break;
                    }

                    case BuiltInType.SByte:
                    {
                        value.Set(SafeReadSByte());
                        break;
                    }

                    case BuiltInType.Byte:
                    {
                        value.Set(SafeReadByte());
                        break;
                    }

                    case BuiltInType.Int16:
                    {
                        value.Set(ReadInt16(null));
                        break;
                    }

                    case BuiltInType.UInt16:
                    {
                        value.Set(ReadUInt16(null));
                        break;
                    }

                    case BuiltInType.Int32:
                    case BuiltInType.Enumeration:
                    {
                        value.Set(ReadInt32(null));
                        break;
                    }

                    case BuiltInType.UInt32:
                    {
                        value.Set(SafeReadUInt32());
                        break;
                    }

                    case BuiltInType.Int64:
                    {
                        value.Set(ReadInt64(null));
                        break;
                    }

                    case BuiltInType.UInt64:
                    {
                        value.Set(ReadUInt64(null));
                        break;
                    }

                    case BuiltInType.Float:
                    {
                        value.Set(ReadFloat(null));
                        break;
                    }

                    case BuiltInType.Double:
                    {
                        value.Set(ReadDouble(null));
                        break;
                    }

                    case BuiltInType.String:
                    {
                        value.Set(ReadString(null));
                        break;
                    }

                    case BuiltInType.DateTime:
                    {
                        value.Set(ReadDateTime(null));
                        break;
                    }

                    case BuiltInType.Guid:
                    {
                        value.Set(ReadGuid(null));
                        break;
                    }

                    case BuiltInType.ByteString:
                    {
                        value.Set(ReadByteString(null));
                        break;
                    }

                    case BuiltInType.XmlElement:
                    {
                        try
                        {
                            value.Set(ReadXmlElement(null));
                        }
                        catch (Exception ex)
                        {
                            Utils.LogError(ex, "Error reading xml element for variant.");
                            value.Set(StatusCodes.BadDecodingError);
                        }
                        break;
                    }

                    case BuiltInType.NodeId:
                    {
                        value.Set(ReadNodeId(null));
                        break;
                    }

                    case BuiltInType.ExpandedNodeId:
                    {
                        value.Set(ReadExpandedNodeId(null));
                        break;
                    }

                    case BuiltInType.StatusCode:
                    {
                        value.Set(ReadStatusCode(null));
                        break;
                    }

                    case BuiltInType.QualifiedName:
                    {
                        value.Set(ReadQualifiedName(null));
                        break;
                    }

                    case BuiltInType.LocalizedText:
                    {
                        value.Set(ReadLocalizedText(null));
                        break;
                    }

                    case BuiltInType.ExtensionObject:
                    {
                        value.Set(ReadExtensionObject());
                        break;
                    }

                    case BuiltInType.DataValue:
                    {
                        value.Set(ReadDataValue(null));
                        break;
                    }

                    default:
                    {
                        throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                            "Cannot decode unknown type in Variant object (0x{0:X2}).", encodingByte);
                    }
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
                return Array.Empty<byte>();
            }

            byte[] bytes = m_reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Reading {0} bytes of {1} reached end of stream after {2} bytes.", length, functionName, bytes.Length);
            }
            return bytes;
        }

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
        ServiceResultException CreateDecodingError(string dataTypeName, string functionName)
        {
            return ServiceResultException.Create(StatusCodes.BadDecodingError,
                "Reading {0} in {1} reached end of stream.", dataTypeName, functionName);
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        private void CheckAndIncrementNestingLevel()
        {
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded", m_context.MaxEncodingNestingLevels);
            }
            m_nestingLevel++;
        }

        /// <summary>
        /// Validate the stream requirements.
        /// </summary>
        /// <param name="stream">The stream used for decoding.</param>
        private void ValidateStreamRequirements(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (stream?.CanSeek != true || stream?.CanRead != true)
            {
                throw new ArgumentException("Stream must be seekable and readable.");
            }
        }
        #endregion

        #region Private Fields
        private BinaryReader m_reader;
        private IServiceMessageContext m_context;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        private uint m_encodeablesRecovered;
        #endregion
    }
}
