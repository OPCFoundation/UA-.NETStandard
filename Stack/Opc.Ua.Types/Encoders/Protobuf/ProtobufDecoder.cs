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
using System.Linq;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Decodes OPC UA values from the experimental Part 6 Protobuf wire representation.
    /// </summary>
    public sealed class ProtobufDecoder : IDecoder
    {
        /// <summary>
        /// Initializes a new ProtobufDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "buffer">The encoded payload buffer to decode.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ProtobufDecoder(byte[] buffer, IServiceMessageContext context)
            : this(new ReadOnlyMemory<byte>(buffer), context) { }

        /// <summary>
        /// Initializes a new ProtobufDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "buffer">The encoded payload buffer to decode.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ProtobufDecoder(ReadOnlyMemory<byte> buffer, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_stack.Push(new Frame(Proto.Parse(buffer)));
        }

        /// <summary>
        /// Initializes a new ProtobufDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ProtobufDecoder(Stream stream, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            m_stack.Push(new Frame(Proto.Parse(ms.ToArray())));
        }

        /// <inheritdoc/>
        public EncodingType EncodingType
        {
            get { return EncodingType.Json; }
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
            m_stack.Clear();
        }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_namespaceMappings = namespaceUris?.CreateMapping(Context.NamespaceUris, true);
            m_serverMappings = serverUris?.CreateMapping(Context.ServerUris, true);
        }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri) { }

        /// <inheritdoc/>
        public void PopNamespace() { }

        /// <inheritdoc/>
        public T DecodeMessage<T>()
            where T : IEncodeable
        {
            NodeId type = ReadNodeId("type_id");
            return ReadEncodeable<T>("body", type);
        }

        /// <inheritdoc/>
        public bool HasField(string fieldName)
        {
            return Current.Message.Has(Current.FieldForName(fieldName));
        }

        // Optional-field presence and union discriminators are carried explicitly on the wire in
        // reserved fields (Proto.UnionMaskField / Proto.UnionSwitchField) written by the encoder, so
        // ReadEncodingMask / ReadSwitchField reconstruct them authoritatively rather than by positional
        // probing. The positional HasField probe is retained only as a fallback for payloads that omit
        // those reserved fields. Field VALUES themselves are still numbered positionally (present fields
        // compacted from 1), which matches the encoder; the spec's fully fixed-numbering scheme
        // (OPC-UA-Part6-Protobuf-DataEncoding §5.6.4) remains a possible future refinement.
        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            // Prefer the persisted presence mask written by the encoder; it is authoritative and avoids
            // the positional-probe limitation for structures with multiple optional fields.
            ProtoField? persisted = Current.Message.First(Proto.UnionMaskField);
            if (persisted.HasValue)
            {
                return (uint)persisted.Value.Varint;
            }

            if (masks != null)
            {
                uint mask = 0;
                for (int i = 0; i < masks.Count && i < 32; i++)
                {
                    if (HasField(masks[i]))
                    {
                        mask |= 1u << i;
                    }
                }

                return mask;
            }

            return 0;
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string? fieldName)
        {
            fieldName = null;
            // Prefer the persisted union discriminator written by the encoder; the positional probe
            // below cannot distinguish which member is present and always resolves the first one.
            ProtoField? persisted = Current.Message.First(Proto.UnionSwitchField);
            if (persisted.HasValue)
            {
                uint switchValue = (uint)persisted.Value.Varint;
                if (switches != null && switchValue >= 1 && switchValue <= switches.Count)
                {
                    fieldName = switches[(int)switchValue - 1];
                }
                return switchValue;
            }

            if (switches != null)
            {
                for (int i = 0; i < switches.Count; i++)
                {
                    if (HasField(switches[i]))
                    {
                        fieldName = switches[i];
                        return (uint)(i + 1);
                    }
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string? fieldName)
        {
            return Get(fieldName).Varint != 0;
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string? fieldName)
        {
            return unchecked((sbyte)Get(fieldName).Varint);
        }

        /// <inheritdoc/>
        public byte ReadByte(string? fieldName)
        {
            return checked((byte)Get(fieldName).Varint);
        }

        /// <inheritdoc/>
        public short ReadInt16(string? fieldName)
        {
            return unchecked((short)Get(fieldName).Varint);
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string? fieldName)
        {
            return checked((ushort)Get(fieldName).Varint);
        }

        /// <inheritdoc/>
        public int ReadInt32(string? fieldName)
        {
            return unchecked((int)Get(fieldName).Varint);
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string? fieldName)
        {
            return checked((uint)Get(fieldName).Varint);
        }

        /// <inheritdoc/>
        public long ReadInt64(string? fieldName)
        {
            return unchecked((long)Get(fieldName).Varint);
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string? fieldName)
        {
            return Get(fieldName).Varint;
        }

        /// <inheritdoc/>
        public float ReadFloat(string? fieldName)
        {
            return BitConverter.UInt32BitsToSingle(Get(fieldName).Fixed32);
        }

        /// <inheritdoc/>
        public double ReadDouble(string? fieldName)
        {
            return BitConverter.UInt64BitsToDouble(Get(fieldName).Fixed64);
        }

        /// <inheritdoc/>
        public string? ReadString(string? fieldName)
        {
            var fld = GetNullable(fieldName);
            if (fld == null)
            {
                return null;
            }

            var m = Proto.Parse(fld.Value.Bytes);
            var values = m.First(1);
            return values.HasValue ? Proto.String(values.Value.Bytes) : null;
        }

        /// <inheritdoc/>
        public DateTimeUtc ReadDateTime(string? fieldName)
        {
            return new(unchecked((long)Get(fieldName).Fixed64));
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string? fieldName)
        {
            return new(Get(fieldName).Bytes.ToArray());
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string? fieldName)
        {
            var fld = GetNullable(fieldName);
            if (fld == null)
            {
                return default;
            }

            var m = Proto.Parse(fld.Value.Bytes);
            var values = m.First(1);
            return values.HasValue ? ByteString.From(values.Value.Bytes.Span) : default;
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string? fieldName)
        {
            string? xml = ReadString(fieldName);
            return xml == null ? default! : (XmlElement)xml;
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string? fieldName)
        {
            return new(Get(fieldName).Fixed32);
        }

        /// <inheritdoc/>
        public EnumValue ReadEnumerated(string? fieldName)
        {
            return new(ReadInt32(fieldName));
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string? fieldName)
            where T : struct, Enum
        {
            return (T)Enum.ToObject(typeof(T), ReadInt32(fieldName));
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string? fieldName)
        {
            return DecodeNodeId(Proto.Parse(Get(fieldName).Bytes));
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName)
        {
            var m = Proto.Parse(Get(fieldName).Bytes);
            NodeId n = m.First(1) is var nf && nf.HasValue ? DecodeNodeId(Proto.Parse(nf.Value.Bytes)) : NodeId.Null;
            string? uri = m.First(2) is var uf && uf.HasValue ? Proto.String(uf.Value.Bytes) : null;
            uint si = m.First(3) is var sf && sf.HasValue ? (uint)sf.Value.Varint : 0;
            return new ExpandedNodeId(n, uri, si);
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string? fieldName)
        {
            var m = Proto.Parse(Get(fieldName).Bytes);
            ushort ns = m.First(1) is var nf && nf.HasValue ? (ushort)nf.Value.Varint : (ushort)0;
            string? name = m.First(2) is var sf && sf.HasValue ? Proto.String(sf.Value.Bytes) : null;
            return new QualifiedName(name, ns);
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string? fieldName)
        {
            var m = Proto.Parse(Get(fieldName).Bytes);
            string? loc = m.First(1) is var lf && lf.HasValue ? Proto.String(lf.Value.Bytes) : null;
            string? text = m.First(2) is var tf && tf.HasValue ? Proto.String(tf.Value.Bytes) : null;
            return new LocalizedText(loc, text);
        }

        /// <inheritdoc/>
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName)
        {
            var fld = GetNullable(fieldName);
            return fld.HasValue ? DecodeDiagnosticInfo(Proto.Parse(fld.Value.Bytes)) : null;
        }

        /// <inheritdoc/>
        public DataValue ReadDataValue(string? fieldName)
        {
            return DecodeDataValue(Proto.Parse(Get(fieldName).Bytes));
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string? fieldName)
        {
            return DecodeExtensionObject(Proto.Parse(Get(fieldName).Bytes));
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string? fieldName)
        {
            return DecodeVariant(Proto.Parse(Get(fieldName).Bytes));
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string? fieldName, TypeInfo typeInfo)
        {
            return ReadVariant(fieldName);
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            ProtoField fld = Get(fieldName);
            if (!Context.Factory.TryGetEncodeableType(encodeableTypeId, out IEncodeableType? act))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"Cannot decode type '{encodeableTypeId}'."
                );
            }

            var values = (T)act.CreateInstance();
            DecodeInto(values, fld.Bytes);
            return values;
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            var values = new T();
            DecodeInto(values, Get(fieldName).Bytes);
            return values;
        }

        /// <inheritdoc/>
        public T ReadEncodeableAsExtensionObject<T>(string? fieldName)
            where T : IEncodeable
        {
            var eo = ReadExtensionObject(fieldName);
            if (eo.TryGetValue(out T? t, Context))
            {
                return t;
            }

            if (!eo.TypeId.IsNull)
            {
                return ReadEncodeable<T>(fieldName, eo.TypeId);
            }

            return default!;
        }

        private void DecodeInto(IEncodeable values, ReadOnlyMemory<byte> bytes)
        {
            m_stack.Push(new Frame(Proto.Parse(bytes)));
            try
            {
                values.Decode(this);
            }
            finally
            {
                m_stack.Pop();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            return ReadArray(fieldName, x => x.Varint != 0);
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName)
        {
            return ReadArray(fieldName, x => unchecked((sbyte)x.Varint));
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string? fieldName)
        {
            return ReadArray(fieldName, x => (byte)x.Varint);
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string? fieldName)
        {
            return ReadArray(fieldName, x => unchecked((short)x.Varint));
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName)
        {
            return ReadArray(fieldName, x => (ushort)x.Varint);
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string? fieldName)
        {
            return ReadArray(fieldName, x => unchecked((int)x.Varint));
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string? fieldName)
        {
            return ReadArray(fieldName, x => (uint)x.Varint);
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string? fieldName)
        {
            return ReadArray(fieldName, x => (long)x.Varint);
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName)
        {
            return ReadArray(fieldName, x => x.Varint);
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string? fieldName)
        {
            return ReadArray(fieldName, x => BitConverter.UInt32BitsToSingle(x.Fixed32));
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string? fieldName)
        {
            return ReadArray(fieldName, x => BitConverter.UInt64BitsToDouble(x.Fixed64));
        }

        /// <inheritdoc/>
        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            return ReadArray(
                fieldName,
                x =>
                {
                    var m = Proto.Parse(x.Bytes);
                    var values = m.First(1);
                    return values.HasValue ? Proto.String(values.Value.Bytes) : null;
                }
            );
        }

        /// <inheritdoc/>
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName)
        {
            return ReadArray(fieldName, x => new DateTimeUtc((long)x.Fixed64));
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName)
        {
            return ReadArray(fieldName, x => new Uuid(x.Bytes.ToArray()));
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName)
        {
            return ReadArray(
                fieldName,
                x =>
                {
                    var m = Proto.Parse(x.Bytes);
                    var values = m.First(1);
                    return values.HasValue ? ByteString.From(values.Value.Bytes.Span) : default;
                }
            );
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName)
        {
            return ReadArray(
                fieldName,
                x =>
                {
                    var m = Proto.Parse(x.Bytes);
                    var values = m.First(1);
                    return values.HasValue ? (XmlElement)Proto.String(values.Value.Bytes) : default!;
                }
            );
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName)
        {
            return ReadArray(fieldName, x => DecodeNodeId(Proto.Parse(x.Bytes)));
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName)
        {
            return ReadArray(
                fieldName,
                x =>
                {
                    m_stack.Push(new Frame(new ProtoMessage { Fields = { x } }));
                    try
                    {
                        return ReadExpandedNodeId(null);
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                }
            );
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName)
        {
            return ReadArray(fieldName, x => new StatusCode(x.Fixed32));
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName)
        {
            return new ArrayOf<DiagnosticInfo?>(
                ReadArray(fieldName, x => (DiagnosticInfo?)DecodeDiagnosticInfo(Proto.Parse(x.Bytes))).Memory
            );
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName)
        {
            return ReadArray(
                fieldName,
                x =>
                {
                    var m = Proto.Parse(x.Bytes);
                    return new QualifiedName(
                        m.First(2) is var n && n.HasValue ? Proto.String(n.Value.Bytes) : null,
                        m.First(1) is var ns && ns.HasValue ? (ushort)ns.Value.Varint : (ushort)0
                    );
                }
            );
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName)
        {
            return ReadArray(
                fieldName,
                x =>
                {
                    var m = Proto.Parse(x.Bytes);
                    return new LocalizedText(
                        m.First(1) is var l && l.HasValue ? Proto.String(l.Value.Bytes) : null,
                        m.First(2) is var t && t.HasValue ? Proto.String(t.Value.Bytes) : null
                    );
                }
            );
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string? fieldName)
        {
            return ReadArray(fieldName, x => DecodeVariant(Proto.Parse(x.Bytes)));
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue> ReadDataValueArray(string? fieldName)
        {
            return ReadArray(fieldName, x => DecodeDataValue(Proto.Parse(x.Bytes)));
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName)
        {
            return ReadArray(fieldName, x => DecodeExtensionObject(Proto.Parse(x.Bytes)));
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string? fieldName)
            where T : struct, Enum
        {
            return ReadArray(fieldName, x => (T)Enum.ToObject(typeof(T), (int)(long)x.Varint));
        }

        /// <inheritdoc/>
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName)
        {
            return ReadArray(fieldName, x => new EnumValue((int)(long)x.Varint));
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
            return ReadArray(
                fieldName,
                x =>
                {
                    var values = new T();
                    DecodeInto(values, x.Bytes);
                    return values;
                }
            );
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
            return ReadArray(
                fieldName,
                x =>
                {
                    if (!Context.Factory.TryGetEncodeableType(encodeableTypeId, out IEncodeableType? act))
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError);
                    }

                    var values = (T)act.CreateInstance();
                    DecodeInto(values, x.Bytes);
                    return values;
                }
            );
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
            throw new NotSupportedException(
                "Decoding abstract encodeable arrays requires generated subtype descriptors in the Protobuf reference decoder."
            );
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
            throw new NotSupportedException(
                "Decode encodeable matrix with an explicit generated T is not implemented in the minimal Protobuf reference decoder."
            );
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
            throw new NotSupportedException(
                "Decode encodeable matrix is not implemented in the minimal Protobuf reference decoder."
            );
        }

        private ArrayOf<T> ReadArray<T>(string? fieldName, Func<ProtoField, T> conv)
        {
            var fld = GetNullable(fieldName);
            if (!fld.HasValue)
            {
                return default;
            }

            var m = Proto.Parse(fld.Value.Bytes);
            return new ArrayOf<T>(m.All(1).Select(conv).ToArray());
        }

        private ProtoField Get(string? fieldName)
        {
            return GetNullable(fieldName) ?? default;
        }

        private ProtoField? GetNullable(string? fieldName)
        {
            int n = Current.Next(fieldName);
            var fld = Current.Message.First(n);
            return fld.HasValue && fld.Value.Number != 0 ? fld : null;
        }

        private Frame Current
        {
            get { return m_stack.Peek(); }
        }

        private static NodeId DecodeNodeId(ProtoMessage m)
        {
            ushort ns = m.First(1) is var nf && nf.HasValue ? (ushort)nf.Value.Varint : (ushort)0;
            if (m.First(3) is var s && s.HasValue)
            {
                return new NodeId(Proto.String(s.Value.Bytes), ns);
            }

            if (m.First(4) is var g && g.HasValue)
            {
                return new NodeId(new Uuid(g.Value.Bytes.ToArray()), ns);
            }

            if (m.First(5) is var o && o.HasValue)
            {
                return new NodeId(ByteString.From(o.Value.Bytes.Span), ns);
            }

            uint encodeableTypeId = m.First(2) is var num && num.HasValue ? (uint)num.Value.Varint : 0;
            return new NodeId(encodeableTypeId, ns);
        }

        private static ExpandedNodeId DecodeExpandedNodeId(ProtoMessage m)
        {
            NodeId n = m.First(1) is var nf && nf.HasValue ? DecodeNodeId(Proto.Parse(nf.Value.Bytes)) : NodeId.Null;
            string? uri = m.First(2) is var uf && uf.HasValue ? Proto.String(uf.Value.Bytes) : null;
            uint si = m.First(3) is var sf && sf.HasValue ? (uint)sf.Value.Varint : 0;
            return new ExpandedNodeId(n, uri, si);
        }

        private static QualifiedName DecodeQualifiedName(ProtoMessage m)
        {
            ushort ns = m.First(1) is var nf && nf.HasValue ? (ushort)nf.Value.Varint : (ushort)0;
            string? name = m.First(2) is var sf && sf.HasValue ? Proto.String(sf.Value.Bytes) : null;
            return new QualifiedName(name, ns);
        }

        private static LocalizedText DecodeLocalizedText(ProtoMessage m)
        {
            string? loc = m.First(1) is var lf && lf.HasValue ? Proto.String(lf.Value.Bytes) : null;
            string? text = m.First(2) is var tf && tf.HasValue ? Proto.String(tf.Value.Bytes) : null;
            return new LocalizedText(loc, text);
        }

        private DiagnosticInfo DecodeDiagnosticInfo(ProtoMessage m)
        {
            CheckAndIncrementNestingLevel();
            try
            {
                return new DiagnosticInfo
                {
                    SymbolicId = m.First(1) is var f1 && f1.HasValue ? (int)(long)f1.Value.Varint : -1,
                    NamespaceUri = m.First(2) is var f2 && f2.HasValue ? (int)(long)f2.Value.Varint : -1,
                    Locale = m.First(3) is var f3 && f3.HasValue ? (int)(long)f3.Value.Varint : -1,
                    LocalizedText = m.First(4) is var f4 && f4.HasValue ? (int)(long)f4.Value.Varint : -1,
                    AdditionalInfo = m.First(5) is var f5 && f5.HasValue ? Proto.String(f5.Value.Bytes) : null,
                    InnerStatusCode = m.First(6) is var f6 && f6.HasValue ? new StatusCode(f6.Value.Fixed32) : default,
                    InnerDiagnosticInfo =
                        m.First(7) is var f7 && f7.HasValue ? DecodeDiagnosticInfo(Proto.Parse(f7.Value.Bytes)) : null,
                };
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        private DataValue DecodeDataValue(ProtoMessage m)
        {
            CheckAndIncrementNestingLevel();
            try
            {
                Variant values =
                    m.First(1) is var vf && vf.HasValue ? DecodeVariant(Proto.Parse(vf.Value.Bytes)) : Variant.Null;
                StatusCode sc = m.First(2) is var sf && sf.HasValue ? new StatusCode(sf.Value.Fixed32) : StatusCodes.Good;
                DateTimeUtc st = m.First(3) is var stf && stf.HasValue ? new DateTimeUtc((long)stf.Value.Fixed64) : default;
                DateTimeUtc sv = m.First(5) is var svf && svf.HasValue ? new DateTimeUtc((long)svf.Value.Fixed64) : default;
                ushort sp = m.First(4) is var spf && spf.HasValue ? (ushort)spf.Value.Varint : (ushort)0;
                ushort vp = m.First(6) is var vpf && vpf.HasValue ? (ushort)vpf.Value.Varint : (ushort)0;
                return new DataValue(values, sc, st, sv, sp, vp);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        private ExtensionObject DecodeExtensionObject(ProtoMessage m)
        {
            CheckAndIncrementNestingLevel();
            try
            {
                ExpandedNodeId type =
                    m.First(1) is var tf && tf.HasValue ? DecodeNodeId(Proto.Parse(tf.Value.Bytes)) : ExpandedNodeId.Null;
                if (m.First(3) is var of && of.HasValue)
                {
                    return new ExtensionObject(type, ByteString.From(of.Value.Bytes.Span));
                }

                if (m.First(2) is var bf && bf.HasValue)
                {
                    return new ExtensionObject(type, ByteString.From(bf.Value.Bytes.Span));
                }

                return new ExtensionObject(type);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        private Variant DecodeVariant(ProtoMessage m)
        {
            if (!m.Has(1))
            {
                return Variant.Null;
            }

            CheckAndIncrementNestingLevel();
            try
            {
                var t = (BuiltInType)m.First(1)!.Value.Varint;
                var scalar = m.First(2);
                if (scalar.HasValue)
                {
                    return DecodeScalarVariant(t, Proto.Parse(scalar.Value.Bytes));
                }

                var array = m.First(3);
                if (array.HasValue)
                {
                    return DecodeArrayVariant(t, Proto.Parse(array.Value.Bytes));
                }

                var matrix = m.First(4);
                if (matrix.HasValue)
                {
                    return DecodeMatrixVariant(t, Proto.Parse(matrix.Value.Bytes));
                }

                return Variant.Null;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        private Variant DecodeScalarVariant(BuiltInType type, ProtoMessage message)
        {
            object? value = DecodeObjectFromField1(type, message.First(1) ?? default);
            return ToVariant(type, value);
        }

        private Variant DecodeArrayVariant(BuiltInType type, ProtoMessage message)
        {
            return type switch
            {
                BuiltInType.Boolean => Variant.From(DecodeVariantArray<bool>(type, message)),
                BuiltInType.SByte => Variant.From(DecodeVariantArray<sbyte>(type, message)),
                BuiltInType.Byte => Variant.From(DecodeVariantArray<byte>(type, message)),
                BuiltInType.Int16 => Variant.From(DecodeVariantArray<short>(type, message)),
                BuiltInType.UInt16 => Variant.From(DecodeVariantArray<ushort>(type, message)),
                BuiltInType.Int32 => Variant.From(DecodeVariantArray<int>(type, message)),
                BuiltInType.Enumeration => Variant.From(DecodeVariantArray<EnumValue>(type, message)),
                BuiltInType.UInt32 => Variant.From(DecodeVariantArray<uint>(type, message)),
                BuiltInType.Int64 => Variant.From(DecodeVariantArray<long>(type, message)),
                BuiltInType.UInt64 => Variant.From(DecodeVariantArray<ulong>(type, message)),
                BuiltInType.Float => Variant.From(DecodeVariantArray<float>(type, message)),
                BuiltInType.Double => Variant.From(DecodeVariantArray<double>(type, message)),
                BuiltInType.String => Variant.From(DecodeVariantArray<string>(type, message)),
                BuiltInType.DateTime => Variant.From(DecodeVariantArray<DateTimeUtc>(type, message)),
                BuiltInType.Guid => Variant.From(DecodeVariantArray<Uuid>(type, message)),
                BuiltInType.ByteString => Variant.From(DecodeVariantArray<ByteString>(type, message)),
                BuiltInType.XmlElement => Variant.From(DecodeVariantArray<XmlElement>(type, message)),
                BuiltInType.NodeId => Variant.From(DecodeVariantArray<NodeId>(type, message)),
                BuiltInType.ExpandedNodeId => Variant.From(DecodeVariantArray<ExpandedNodeId>(type, message)),
                BuiltInType.StatusCode => Variant.From(DecodeVariantArray<StatusCode>(type, message)),
                BuiltInType.QualifiedName => Variant.From(DecodeVariantArray<QualifiedName>(type, message)),
                BuiltInType.LocalizedText => Variant.From(DecodeVariantArray<LocalizedText>(type, message)),
                BuiltInType.ExtensionObject => Variant.From(DecodeVariantArray<ExtensionObject>(type, message)),
                BuiltInType.DataValue => Variant.From(DecodeVariantArray<DataValue>(type, message)),
                _ => throw new NotSupportedException(
                    $"Variant array type {type} is not supported by the Protobuf reference decoder."
                ),
            };
        }

        private Variant DecodeMatrixVariant(BuiltInType type, ProtoMessage message)
        {
            return type switch
            {
                BuiltInType.Boolean => Variant.From(DecodeVariantMatrix<bool>(type, message)),
                BuiltInType.SByte => Variant.From(DecodeVariantMatrix<sbyte>(type, message)),
                BuiltInType.Byte => Variant.From(DecodeVariantMatrix<byte>(type, message)),
                BuiltInType.Int16 => Variant.From(DecodeVariantMatrix<short>(type, message)),
                BuiltInType.UInt16 => Variant.From(DecodeVariantMatrix<ushort>(type, message)),
                BuiltInType.Int32 => Variant.From(DecodeVariantMatrix<int>(type, message)),
                BuiltInType.Enumeration => Variant.From(DecodeVariantMatrix<EnumValue>(type, message)),
                BuiltInType.UInt32 => Variant.From(DecodeVariantMatrix<uint>(type, message)),
                BuiltInType.Int64 => Variant.From(DecodeVariantMatrix<long>(type, message)),
                BuiltInType.UInt64 => Variant.From(DecodeVariantMatrix<ulong>(type, message)),
                BuiltInType.Float => Variant.From(DecodeVariantMatrix<float>(type, message)),
                BuiltInType.Double => Variant.From(DecodeVariantMatrix<double>(type, message)),
                BuiltInType.String => Variant.From(DecodeVariantMatrix<string>(type, message)),
                BuiltInType.DateTime => Variant.From(DecodeVariantMatrix<DateTimeUtc>(type, message)),
                BuiltInType.Guid => Variant.From(DecodeVariantMatrix<Uuid>(type, message)),
                BuiltInType.ByteString => Variant.From(DecodeVariantMatrix<ByteString>(type, message)),
                BuiltInType.XmlElement => Variant.From(DecodeVariantMatrix<XmlElement>(type, message)),
                BuiltInType.NodeId => Variant.From(DecodeVariantMatrix<NodeId>(type, message)),
                BuiltInType.ExpandedNodeId => Variant.From(DecodeVariantMatrix<ExpandedNodeId>(type, message)),
                BuiltInType.StatusCode => Variant.From(DecodeVariantMatrix<StatusCode>(type, message)),
                BuiltInType.QualifiedName => Variant.From(DecodeVariantMatrix<QualifiedName>(type, message)),
                BuiltInType.LocalizedText => Variant.From(DecodeVariantMatrix<LocalizedText>(type, message)),
                BuiltInType.ExtensionObject => Variant.From(DecodeVariantMatrix<ExtensionObject>(type, message)),
                BuiltInType.DataValue => Variant.From(DecodeVariantMatrix<DataValue>(type, message)),
                _ => throw new NotSupportedException(
                    $"Variant matrix type {type} is not supported by the Protobuf reference decoder."
                ),
            };
        }

        private ArrayOf<T> DecodeVariantArray<T>(BuiltInType type, ProtoMessage message)
        {
            return new ArrayOf<T>(message.All(1).Select(field => DecodeVariantElement<T>(type, field)).ToArray());
        }

        private MatrixOf<T> DecodeVariantMatrix<T>(BuiltInType type, ProtoMessage message)
        {
            int[] dimensions = message.All(1).Select(field => unchecked((int)(long)field.Varint)).ToArray();
            T[] values = message.All(2).Select(field => DecodeVariantElement<T>(type, field)).ToArray();
            return new ArrayOf<T>(values).ToMatrix(dimensions);
        }

        private T DecodeVariantElement<T>(BuiltInType type, ProtoField valueField)
        {
            var valueMessage = Proto.Parse(valueField.Bytes);
            object? value = DecodeObjectFromField1(type, valueMessage.First(1) ?? default);
            if (value == null)
            {
                return default!;
            }

            return (T)value;
        }

        private static Variant ToVariant(BuiltInType type, object? value)
        {
            return value switch
            {
                null => Variant.Null,
                bool typed => Variant.From(typed),
                sbyte typed => Variant.From(typed),
                byte typed => Variant.From(typed),
                short typed => Variant.From(typed),
                ushort typed => Variant.From(typed),
                int typed => Variant.From(typed),
                EnumValue typed => Variant.From(typed),
                uint typed => Variant.From(typed),
                long typed => Variant.From(typed),
                ulong typed => Variant.From(typed),
                float typed => Variant.From(typed),
                double typed => Variant.From(typed),
                string typed => Variant.From(typed),
                DateTimeUtc typed => Variant.From(typed),
                Uuid typed => Variant.From(typed),
                ByteString typed => Variant.From(typed),
                XmlElement typed => Variant.From(typed),
                NodeId typed => Variant.From(typed),
                ExpandedNodeId typed => Variant.From(typed),
                StatusCode typed => Variant.From(typed),
                QualifiedName typed => Variant.From(typed),
                LocalizedText typed => Variant.From(typed),
                ExtensionObject typed => Variant.From(typed),
                DataValue typed => Variant.From(typed),
                _ => throw new NotSupportedException(
                    $"Variant payload type {type} is not supported by the Protobuf reference decoder."
                ),
            };
        }

        private object? DecodeObjectFromField1(BuiltInType t, ProtoField fieldName)
        {
            if (fieldName.Number == 0)
            {
                return null;
            }

            return t switch
            {
                BuiltInType.Boolean => fieldName.Varint != 0,
                BuiltInType.SByte => (sbyte)(long)fieldName.Varint,
                BuiltInType.Byte => (byte)fieldName.Varint,
                BuiltInType.Int16 => (short)(long)fieldName.Varint,
                BuiltInType.UInt16 => (ushort)fieldName.Varint,
                BuiltInType.Int32 => (int)(long)fieldName.Varint,
                BuiltInType.Enumeration => new EnumValue((int)(long)fieldName.Varint),
                BuiltInType.UInt32 => (uint)fieldName.Varint,
                BuiltInType.Int64 => (long)fieldName.Varint,
                BuiltInType.UInt64 => fieldName.Varint,
                BuiltInType.Float => BitConverter.UInt32BitsToSingle(fieldName.Fixed32),
                BuiltInType.Double => BitConverter.UInt64BitsToDouble(fieldName.Fixed64),
                BuiltInType.String => Proto.Parse(fieldName.Bytes).First(1) is var sf && sf.HasValue
                    ? Proto.String(sf.Value.Bytes)
                    : null,
                BuiltInType.DateTime => new DateTimeUtc((long)fieldName.Fixed64),
                BuiltInType.Guid => new Uuid(fieldName.Bytes.ToArray()),
                BuiltInType.ByteString => Proto.Parse(fieldName.Bytes).First(1) is var bf && bf.HasValue
                    ? ByteString.From(bf.Value.Bytes.Span)
                    : default,
                BuiltInType.XmlElement => Proto.Parse(fieldName.Bytes).First(1) is var xf && xf.HasValue
                    ? (XmlElement)Proto.String(xf.Value.Bytes)
                    : null,
                BuiltInType.NodeId => DecodeNodeId(Proto.Parse(fieldName.Bytes)),
                BuiltInType.ExpandedNodeId => DecodeExpandedNodeId(Proto.Parse(fieldName.Bytes)),
                BuiltInType.StatusCode => new StatusCode(fieldName.Fixed32),
                BuiltInType.QualifiedName => DecodeQualifiedName(Proto.Parse(fieldName.Bytes)),
                BuiltInType.LocalizedText => DecodeLocalizedText(Proto.Parse(fieldName.Bytes)),
                BuiltInType.ExtensionObject => DecodeExtensionObject(Proto.Parse(fieldName.Bytes)),
                BuiltInType.DataValue => DecodeDataValue(Proto.Parse(fieldName.Bytes)),
                _ => null,
            };
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

        private sealed class Frame
        {
            /// <summary>
            /// Initializes a new Frame instance for the experimental OPC UA encoding support.
            /// </summary>
            /// <param name = "m">The parsed Protobuf message to decode.</param>
            public Frame(ProtoMessage m)
            {
                Message = m;
            }

            /// <summary>
            /// Gets the Protobuf message being decoded in this frame.
            /// </summary>
            public ProtoMessage Message;

            /// <summary>
            /// Stores the next positional Protobuf field number assigned by this frame.
            /// </summary>
            public int NextField = 1;
            private readonly Dictionary<string, int> _names = new();

            /// <summary>
            /// Returns the Protobuf field number for the supplied OPC UA field name.
            /// </summary>
            /// <param name = "name">The field or column name to assign.</param>
            /// <returns>The result produced by this codec helper.</returns>
            public int Next(string? name)
            {
                return FieldForName(name);
            }

            /// <summary>
            /// Maps an OPC UA field name to the positional Protobuf field number used in this frame.
            /// </summary>
            /// <param name = "name">The field or column name to assign.</param>
            /// <returns>The result produced by this codec helper.</returns>
            public int FieldForName(string? name)
            {
                if (name == null)
                {
                    return NextField++;
                }

                if (!_names.TryGetValue(name, out int n))
                {
                    n = NextField++;
                    _names[name] = n;
                }

                return n;
            }
        }

        private readonly Stack<Frame> m_stack = new();
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
        private uint m_nestingLevel;
    }
}
