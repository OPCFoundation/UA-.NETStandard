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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Decodes OPC UA values using the experimental Avro binary mapping.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Avro")]
    public sealed class AvroDecoder : IDecoder
    {
        private readonly Stream m_stream;
        private readonly AvroBinaryReader m_reader;
        private readonly bool m_leaveOpen;
        private uint m_nestingLevel;
        // Mirrors AvroEncoder.m_nextArrayPlain: when set, the next ReadArray/ReadMatrix reads a
        // *plain* Avro array body (no nullable-union present-marker). Consumed on entry.
        private bool m_nextArrayPlain;

        /// <summary>
        /// Initializes a new AvroDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "buffer">The encoded payload buffer to decode.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public AvroDecoder(byte[] buffer, IServiceMessageContext context)
            : this(new MemoryStream(buffer, false), context, false) { }

        /// <summary>
        /// Initializes a new AvroDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        /// <param name = "leaveOpen">True to leave the caller-owned stream open when the codec is closed.</param>
        public AvroDecoder(Stream stream, IServiceMessageContext context, bool leaveOpen = true)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_stream = stream ?? throw new ArgumentNullException(nameof(stream));
            m_leaveOpen = leaveOpen;
            m_reader = new AvroBinaryReader(m_stream);
        }

        /// <inheritdoc/>
        public EncodingType EncodingType
        {
            get { return EncodingType.Avro; }
        }

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
        }

        /// <inheritdoc/>
        public void Close()
        {
            m_reader.Release();
            if (!m_leaveOpen)
            {
                m_stream.Dispose();
            }
        }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris) { }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri) { }

        /// <inheritdoc/>
        public void PopNamespace() { }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("Trimming", "IL2091",
            Justification = "All IEncodeable message types declare a public parameterless constructor.")]
        public T DecodeMessage<T>()
            where T : IEncodeable
        {
            T value =
                Activator.CreateInstance<T>()
                ?? throw new NotSupportedException($"Cannot create {typeof(T).FullName}.");
            value.Decode(this);
            return value;
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string? fieldName)
        {
            return m_reader.ReadBoolean();
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string? fieldName)
        {
            return checked((sbyte)m_reader.ReadInt());
        }

        /// <inheritdoc/>
        public byte ReadByte(string? fieldName)
        {
            return checked((byte)m_reader.ReadInt());
        }

        /// <inheritdoc/>
        public short ReadInt16(string? fieldName)
        {
            return checked((short)m_reader.ReadInt());
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string? fieldName)
        {
            return checked((ushort)m_reader.ReadInt());
        }

        /// <inheritdoc/>
        public int ReadInt32(string? fieldName)
        {
            return m_reader.ReadInt();
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string? fieldName)
        {
            return unchecked((uint)m_reader.ReadInt());
        }

        /// <inheritdoc/>
        public long ReadInt64(string? fieldName)
        {
            return m_reader.ReadLong();
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string? fieldName)
        {
            return unchecked((ulong)m_reader.ReadLong());
        }

        /// <inheritdoc/>
        public float ReadFloat(string? fieldName)
        {
            return m_reader.ReadFloat();
        }

        /// <inheritdoc/>
        public double ReadDouble(string? fieldName)
        {
            return m_reader.ReadDouble();
        }

        /// <inheritdoc/>
        public string? ReadString(string? fieldName)
        {
            return ReadNullable(() => m_reader.ReadString(Context.MaxStringLength));
        }

        /// <inheritdoc/>
        public DateTimeUtc ReadDateTime(string? fieldName)
        {
            return new DateTimeUtc(m_reader.ReadLong());
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string? fieldName)
        {
            return new Uuid(GuidFromRfc4122(m_reader.ReadFixed(16)));
        }

        // Mirrors AvroEncoder.GuidToRfc4122: the Avro `uuid` fixed is RFC-4122 (big-endian) order,
        // so re-order to .NET's mixed-endian layout before constructing the Guid.
        private static Guid GuidFromRfc4122(byte[] be)
        {
            byte[] n =
            [
                be[3], be[2], be[1], be[0],
                be[5], be[4],
                be[7], be[6],
                be[8], be[9], be[10], be[11], be[12], be[13], be[14], be[15],
            ];
            return new Guid(n);
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string? fieldName)
        {
            byte[]? bytes = ReadNullable(() => m_reader.ReadBytes(Context.MaxByteStringLength));
            return bytes == null ? default : ByteString.From(bytes);
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string? fieldName)
        {
            return XmlElement.From(ReadString(fieldName));
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string? fieldName)
        {
            long branch = m_reader.ReadLong();
            if (branch == 0)
            {
                return NodeId.Null;
            }

            ExpectBranch(branch, 1);
            return ReadNodeIdRecord();
        }

        private NodeId ReadNodeIdRecord()
        {
            var ns = (ushort)ReadInt32(null);
            var type = (IdType)ReadInt32(null);

            long numeric = 0;
            if (m_reader.ReadLong() != 0)
            {
                numeric = m_reader.ReadLong();
            }

            string? text = null;
            if (m_reader.ReadLong() != 0)
            {
                text = m_reader.ReadString(Context.MaxStringLength);
            }

            Guid guid = default;
            if (m_reader.ReadLong() != 0)
            {
                guid = GuidFromRfc4122(m_reader.ReadFixed(16));
            }

            ByteString opaque = default;
            if (m_reader.ReadLong() != 0)
            {
                opaque = ByteString.From(m_reader.ReadBytes(Context.MaxByteStringLength));
            }

            return type switch
            {
                IdType.Numeric => new NodeId((uint)numeric, ns),
                IdType.String => new NodeId(text ?? string.Empty, ns),
                IdType.Guid => new NodeId(guid, ns),
                IdType.Opaque => new NodeId(opaque, ns),
                _ => throw new NotSupportedException($"Unsupported NodeId identifier type {type}."),
            };
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName)
        {
            long branch = m_reader.ReadLong();
            if (branch == 0)
            {
                return ExpandedNodeId.Null;
            }

            ExpectBranch(branch, 1);
            NodeId nodeId = ReadNodeIdRecord();
            string? namespaceUri = ReadString(null);
            var serverIndex = (uint)m_reader.ReadLong();
            return new ExpandedNodeId(nodeId, namespaceUri, serverIndex);
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string? fieldName)
        {
            return new StatusCode(ReadUInt32(null));
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string? fieldName)
        {
            long branch = m_reader.ReadLong();
            if (branch == 0)
            {
                return QualifiedName.Null;
            }

            ExpectBranch(branch, 1);
            var namespaceIndex = (ushort)ReadInt32(null);
            string? name = ReadString(null);
            return new QualifiedName(name, namespaceIndex);
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string? fieldName)
        {
            long branch = m_reader.ReadLong();
            if (branch == 0)
            {
                return LocalizedText.Null;
            }

            ExpectBranch(branch, 1);
            return new LocalizedText(ReadString(null), ReadString(null));
        }

        /// <inheritdoc/>
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName)
        {
            long branch = m_reader.ReadLong();
            if (branch == 0)
            {
                return null;
            }

            ExpectBranch(branch, 1);
            CheckAndIncrementNestingLevel();
            try
            {
                var d = new DiagnosticInfo();
                d.SymbolicId = ReadNullableValue(() => ReadInt32(null), -1);
                d.NamespaceUri = ReadNullableValue(() => ReadInt32(null), -1);
                d.Locale = ReadNullableValue(() => ReadInt32(null), -1);
                d.LocalizedText = ReadNullableValue(() => ReadInt32(null), -1);
                d.AdditionalInfo = ReadString(null);
                d.InnerStatusCode = ReadNullableValue(() => ReadStatusCode(null), StatusCodes.Good);
                d.InnerDiagnosticInfo = ReadDiagnosticInfo(null);
                return d;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public DataValue ReadDataValue(string? fieldName)
        {
            CheckAndIncrementNestingLevel();
            try
            {
                Variant value = ReadNullableValue(() => ReadVariant(null), Variant.Null);
                StatusCode status = ReadNullableValue(() => ReadStatusCode(null), StatusCodes.Good);
                DateTimeUtc sourceTs = ReadNullableValue(() => ReadDateTime(null), DateTimeUtc.MinValue);
                ushort sourcePs = ReadNullableValue(() => ReadUInt16(null), (ushort)0);
                DateTimeUtc serverTs = ReadNullableValue(() => ReadDateTime(null), DateTimeUtc.MinValue);
                ushort serverPs = ReadNullableValue(() => ReadUInt16(null), (ushort)0);
                if (
                    value.IsNull
                    && status.Equals(StatusCodes.Good, StatusCodeComparison.AllBits)
                    && sourceTs.IsNull
                    && serverTs.IsNull
                    && sourcePs == 0
                    && serverPs == 0
                )
                {
                    return DataValue.Null;
                }

                return new DataValue(value, status, sourceTs, serverTs, sourcePs, serverPs);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string? fieldName)
        {
            ExpandedNodeId typeId = ReadExpandedNodeId(null);
            long branch = m_reader.ReadLong();
            if (branch == 0)
            {
                return new ExtensionObject(typeId);
            }

            if (branch == 1)
            {
                if (!Context.Factory.TryGetEncodeableType(typeId, out IEncodeableType? activator))
                {
                    throw new NotSupportedException(
                        $"Cannot decode Avro ExtensionObject body for unregistered type {typeId}."
                    );
                }

                CheckAndIncrementNestingLevel();
                try
                {
                    IEncodeable body = activator.CreateInstance();
                    body.Decode(this);
                    return new ExtensionObject(typeId, body);
                }
                finally
                {
                    m_nestingLevel--;
                }
            }

            if (branch == 2)
            {
                return new ExtensionObject(typeId, ByteString.From(m_reader.ReadBytes(Context.MaxByteStringLength)));
            }

            if (branch == 3)
            {
                return new ExtensionObject(typeId, XmlElement.From(m_reader.ReadString(Context.MaxStringLength)));
            }

            throw new FormatException($"Invalid ExtensionObject Avro body branch {branch}.");
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (!Context.Factory.TryGetEncodeableType(encodeableTypeId, out IEncodeableType? activator))
            {
                throw new NotSupportedException($"Cannot decode type {encodeableTypeId}.");
            }

            CheckAndIncrementNestingLevel();
            try
            {
                var value = (T)activator.CreateInstance();
                value.Decode(this);
                return value;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            CheckAndIncrementNestingLevel();
            try
            {
                var value = new T();
                value.Decode(this);
                return value;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public T ReadEncodeableAsExtensionObject<T>(string? fieldName)
            where T : IEncodeable
        {
            ExtensionObject eo = ReadExtensionObject(fieldName);
            return eo.TryGetValue(out T? value) ? value! : default!;
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string? fieldName)
            where T : struct, Enum
        {
            return EnumHelper.Int32ToEnum<T>(ReadInt32(fieldName));
        }

        /// <inheritdoc/>
        public EnumValue ReadEnumerated(string? fieldName)
        {
            return EnumValue.From(ReadInt32(fieldName));
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string? fieldName)
        {
            fieldName = null;
            return ReadUInt32("switch");
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            return ReadUInt32("encodingMask");
        }

        /// <inheritdoc/>
        public bool HasField(string fieldName)
        {
            return true;
        }

        private T? ReadNullable<T>(Func<T> read)
            where T : class
        {
            long b = m_reader.ReadLong();
            if (b == 0)
            {
                return null;
            }

            ExpectBranch(b, 1);
            return read();
        }

        private T ReadNullableValue<T>(Func<T> read, T defaultValue)
        {
            long b = m_reader.ReadLong();
            if (b == 0)
            {
                return defaultValue;
            }

            ExpectBranch(b, 1);
            return read();
        }

        private static void ExpectBranch(long actual, long expected)
        {
            if (actual != expected)
            {
                throw new FormatException($"Unexpected Avro union branch {actual}; expected {expected}.");
            }
        }

        private ArrayOf<T> ReadArray<T>(Func<T> read)
        {
            bool plain = m_nextArrayPlain;
            m_nextArrayPlain = false;
            if (!plain)
            {
                long branch = m_reader.ReadLong();
                if (branch == 0)
                {
                    return default;
                }

                ExpectBranch(branch, 1);
            }

            var values = new List<T>();
            while (true)
            {
                long count = m_reader.ReadLong();
                if (count == 0)
                {
                    break;
                }

                if (count < 0)
                {
                    _ = m_reader.ReadLong();
                    count = -count;
                }

                for (long i = 0; i < count; i++)
                {
                    values.Add(read());
                }
            }

            return values.ToArray();
        }

        private MatrixOf<T> ReadMatrix<T>(Func<ArrayOf<T>> readArray)
        {
            bool plain = m_nextArrayPlain;
            m_nextArrayPlain = false;
            if (!plain)
            {
                long branch = m_reader.ReadLong();
                if (branch == 0)
                {
                    return default;
                }

                ExpectBranch(branch, 1);
                int[] outerDims = ReadInt32Array(null).ToArray() ?? Array.Empty<int>();
                return readArray().ToMatrix(outerDims);
            }

            // A Variant matrix body: MatrixBody { dimensions: plain array<int>, values: plain array }.
            m_nextArrayPlain = true;
            int[] dims = ReadInt32Array(null).ToArray() ?? Array.Empty<int>();
            m_nextArrayPlain = true;
            return readArray().ToMatrix(dims);
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            return ReadArray(() => ReadBoolean(null));
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName)
        {
            return ReadArray(() => ReadSByte(null));
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string? fieldName)
        {
            return ReadArray(() => ReadByte(null));
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string? fieldName)
        {
            return ReadArray(() => ReadInt16(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName)
        {
            return ReadArray(() => ReadUInt16(null));
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string? fieldName)
        {
            return ReadArray(() => ReadInt32(null));
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string? fieldName)
        {
            return ReadArray(() => ReadUInt32(null));
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string? fieldName)
        {
            return ReadArray(() => ReadInt64(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName)
        {
            return ReadArray(() => ReadUInt64(null));
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string? fieldName)
        {
            return ReadArray(() => ReadFloat(null));
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string? fieldName)
        {
            return ReadArray(() => ReadDouble(null));
        }

        /// <inheritdoc/>
        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            return ReadArray(() => ReadString(null));
        }

        /// <inheritdoc/>
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName)
        {
            return ReadArray(() => ReadDateTime(null));
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName)
        {
            return ReadArray(() => ReadGuid(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName)
        {
            return ReadArray(() => ReadByteString(null));
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName)
        {
            return ReadArray(() => ReadXmlElement(null));
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName)
        {
            return ReadArray(() => ReadNodeId(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName)
        {
            return ReadArray(() => ReadExpandedNodeId(null));
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName)
        {
            return ReadArray(() => ReadStatusCode(null));
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName)
        {
            return ReadArray(() => ReadDiagnosticInfo(null));
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName)
        {
            return ReadArray(() => ReadQualifiedName(null));
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName)
        {
            return ReadArray(() => ReadLocalizedText(null));
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string? fieldName)
        {
            return ReadArray(() => ReadVariant(null));
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue> ReadDataValueArray(string? fieldName)
        {
            return ReadArray(() => ReadDataValue(null));
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName)
        {
            return ReadArray(() => ReadExtensionObject(null));
        }

        /// <summary>
        /// Reads EncodeableArray from the experimental encoded representation.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "fieldName">The OPC UA field name associated with the encoded member.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            return ReadArray(() => ReadEncodeable<T>(null));
        }

        /// <summary>
        /// Reads EncodeableArray from the experimental encoded representation.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "fieldName">The OPC UA field name associated with the encoded member.</param>
        /// <param name = "encodeableTypeId">The expanded type identifier used to resolve the encodeable body.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            return ReadArray(() => ReadEncodeable<T>(null, encodeableTypeId));
        }

        /// <summary>
        /// Reads EncodeableArrayAsExtensionObjects from the experimental encoded representation.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "fieldName">The OPC UA field name associated with the encoded member.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public ArrayOf<T> ReadEncodeableArrayAsExtensionObjects<T>(string? fieldName)
            where T : IEncodeable
        {
            return ReadArray(() => ReadEncodeableAsExtensionObject<T>(null));
        }

        /// <summary>
        /// Reads EncodeableMatrix from the experimental encoded representation.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "fieldName">The OPC UA field name associated with the encoded member.</param>
        /// <param name = "encodeableTypeId">The expanded type identifier used to resolve the encodeable body.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            return ReadMatrix(() => ReadEncodeableArray<T>(null, encodeableTypeId));
        }

        /// <summary>
        /// Reads EncodeableMatrix from the experimental encoded representation.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "fieldName">The OPC UA field name associated with the encoded member.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            return ReadMatrix(() => ReadEncodeableArray<T>(null));
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string? fieldName)
            where T : struct, Enum
        {
            return ReadArray(() => ReadEnumerated<T>(null));
        }

        /// <inheritdoc/>
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName)
        {
            return ReadArray(() => ReadEnumerated(null));
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string? fieldName)
        {
            BuiltInType headerType = (BuiltInType)ReadInt32(null);
            // dimensions: nullable(array<int>) — present only for matrices, consumed here.
            _ = ReadInt32Array(null);
            return ReadVariantBody(headerType);
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string? fieldName, TypeInfo typeInfo)
        {
            return ReadVariantBody(typeInfo.BuiltInType);
        }

        private Variant ReadVariantBody(BuiltInType headerType)
        {
            long branch = m_reader.ReadLong();
            if (branch == 0 || headerType == BuiltInType.Null)
            {
                return Variant.Null;
            }

            // Invert the body-union branch: branch = 1 + 3*(type-1) + shapeOffset.
            long index = branch - 1;
            var type = (BuiltInType)((index / 3) + 1);
            long shapeOffset = index % 3;

            CheckAndIncrementNestingLevel();
            try
            {
                if (shapeOffset == 0)
                {
                    return ReadScalarVariant(type);
                }

                if (shapeOffset == 1)
                {
                    m_nextArrayPlain = true;
                    return ReadArrayVariant(type);
                }

                m_nextArrayPlain = true;
                return ReadMatrixVariant(type);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Guards against unbounded decode recursion on hostile input by enforcing
        /// <see cref="IServiceMessageContext.MaxEncodingNestingLevels"/>, mirroring the built-in
        /// BinaryDecoder/XmlDecoder/JsonDecoder. Callers must decrement <c>m_nestingLevel</c> in a
        /// <c>finally</c> block.
        /// </summary>
        /// <exception cref="ServiceResultException">The maximum nesting level was exceeded.</exception>
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

        private Variant ReadScalarVariant(BuiltInType type)
        {
            return type switch
            {
                BuiltInType.Boolean => Variant.From(ReadBoolean(null)),
                BuiltInType.SByte => Variant.From(ReadSByte(null)),
                BuiltInType.Byte => Variant.From(ReadByte(null)),
                BuiltInType.Int16 => Variant.From(ReadInt16(null)),
                BuiltInType.UInt16 => Variant.From(ReadUInt16(null)),
                BuiltInType.Int32 => Variant.From(ReadInt32(null)),
                BuiltInType.Enumeration => Variant.From(ReadEnumerated(null)),
                BuiltInType.UInt32 => Variant.From(ReadUInt32(null)),
                BuiltInType.Int64 => Variant.From(ReadInt64(null)),
                BuiltInType.UInt64 => Variant.From(ReadUInt64(null)),
                BuiltInType.Float => Variant.From(ReadFloat(null)),
                BuiltInType.Double => Variant.From(ReadDouble(null)),
                BuiltInType.String => Variant.From(ReadString(null)!),
                BuiltInType.DateTime => Variant.From(ReadDateTime(null)),
                BuiltInType.Guid => Variant.From(ReadGuid(null)),
                BuiltInType.ByteString => Variant.From(ReadByteString(null)),
                BuiltInType.XmlElement => Variant.From(ReadXmlElement(null)),
                BuiltInType.NodeId => Variant.From(ReadNodeId(null)),
                BuiltInType.ExpandedNodeId => Variant.From(ReadExpandedNodeId(null)),
                BuiltInType.StatusCode => Variant.From(ReadStatusCode(null)),
                BuiltInType.QualifiedName => Variant.From(ReadQualifiedName(null)),
                BuiltInType.LocalizedText => Variant.From(ReadLocalizedText(null)),
                BuiltInType.ExtensionObject => Variant.From(ReadExtensionObject(null)),
                BuiltInType.DataValue => Variant.From(ReadDataValue(null)),
                _ => throw new NotSupportedException($"Variant scalar {type} is not supported by the Avro decoder."),
            };
        }

        private Variant ReadArrayVariant(BuiltInType type)
        {
            return type switch
            {
                BuiltInType.Boolean => Variant.From(ReadBooleanArray(null)),
                BuiltInType.SByte => Variant.From(ReadSByteArray(null)),
                BuiltInType.Byte => Variant.From(ReadByteArray(null)),
                BuiltInType.Int16 => Variant.From(ReadInt16Array(null)),
                BuiltInType.UInt16 => Variant.From(ReadUInt16Array(null)),
                BuiltInType.Int32 => Variant.From(ReadInt32Array(null)),
                BuiltInType.Enumeration => Variant.From(ReadEnumeratedArray(null)),
                BuiltInType.UInt32 => Variant.From(ReadUInt32Array(null)),
                BuiltInType.Int64 => Variant.From(ReadInt64Array(null)),
                BuiltInType.UInt64 => Variant.From(ReadUInt64Array(null)),
                BuiltInType.Float => Variant.From(ReadFloatArray(null)),
                BuiltInType.Double => Variant.From(ReadDoubleArray(null)),
                BuiltInType.String => Variant.From(ReadStringArray(null).ConvertAll(value => value!)),
                BuiltInType.DateTime => Variant.From(ReadDateTimeArray(null)),
                BuiltInType.Guid => Variant.From(ReadGuidArray(null)),
                BuiltInType.ByteString => Variant.From(ReadByteStringArray(null)),
                BuiltInType.XmlElement => Variant.From(ReadXmlElementArray(null)),
                BuiltInType.NodeId => Variant.From(ReadNodeIdArray(null)),
                BuiltInType.ExpandedNodeId => Variant.From(ReadExpandedNodeIdArray(null)),
                BuiltInType.StatusCode => Variant.From(ReadStatusCodeArray(null)),
                BuiltInType.QualifiedName => Variant.From(ReadQualifiedNameArray(null)),
                BuiltInType.LocalizedText => Variant.From(ReadLocalizedTextArray(null)),
                BuiltInType.ExtensionObject => Variant.From(ReadExtensionObjectArray(null)),
                BuiltInType.DataValue => Variant.From(ReadDataValueArray(null)),
                BuiltInType.Variant => Variant.From(ReadVariantArray(null)),
                _ => throw new NotSupportedException($"Variant array {type} is not supported by the Avro decoder."),
            };
        }

        private Variant ReadMatrixVariant(BuiltInType type)
        {
            return type switch
            {
                BuiltInType.Boolean => Variant.From(ReadMatrix(() => ReadBooleanArray(null))),
                BuiltInType.SByte => Variant.From(ReadMatrix(() => ReadSByteArray(null))),
                BuiltInType.Byte => Variant.From(ReadMatrix(() => ReadByteArray(null))),
                BuiltInType.Int16 => Variant.From(ReadMatrix(() => ReadInt16Array(null))),
                BuiltInType.UInt16 => Variant.From(ReadMatrix(() => ReadUInt16Array(null))),
                BuiltInType.Int32 => Variant.From(ReadMatrix(() => ReadInt32Array(null))),
                BuiltInType.Enumeration => Variant.From(ReadMatrix(() => ReadEnumeratedArray(null))),
                BuiltInType.UInt32 => Variant.From(ReadMatrix(() => ReadUInt32Array(null))),
                BuiltInType.Int64 => Variant.From(ReadMatrix(() => ReadInt64Array(null))),
                BuiltInType.UInt64 => Variant.From(ReadMatrix(() => ReadUInt64Array(null))),
                BuiltInType.Float => Variant.From(ReadMatrix(() => ReadFloatArray(null))),
                BuiltInType.Double => Variant.From(ReadMatrix(() => ReadDoubleArray(null))),
                BuiltInType.String => Variant.From(ReadMatrix(() => ReadStringArray(null)).ConvertAll(value => value!)),
                BuiltInType.DateTime => Variant.From(ReadMatrix(() => ReadDateTimeArray(null))),
                BuiltInType.Guid => Variant.From(ReadMatrix(() => ReadGuidArray(null))),
                BuiltInType.ByteString => Variant.From(ReadMatrix(() => ReadByteStringArray(null))),
                BuiltInType.XmlElement => Variant.From(ReadMatrix(() => ReadXmlElementArray(null))),
                BuiltInType.NodeId => Variant.From(ReadMatrix(() => ReadNodeIdArray(null))),
                BuiltInType.ExpandedNodeId => Variant.From(ReadMatrix(() => ReadExpandedNodeIdArray(null))),
                BuiltInType.StatusCode => Variant.From(ReadMatrix(() => ReadStatusCodeArray(null))),
                BuiltInType.QualifiedName => Variant.From(ReadMatrix(() => ReadQualifiedNameArray(null))),
                BuiltInType.LocalizedText => Variant.From(ReadMatrix(() => ReadLocalizedTextArray(null))),
                BuiltInType.ExtensionObject => Variant.From(ReadMatrix(() => ReadExtensionObjectArray(null))),
                BuiltInType.DataValue => Variant.From(ReadMatrix(() => ReadDataValueArray(null))),
                BuiltInType.Variant => Variant.From(ReadMatrix(() => ReadVariantArray(null))),
                _ => throw new NotSupportedException($"Variant matrix {type} is not supported by the Avro decoder."),
            };
        }
    }
}
