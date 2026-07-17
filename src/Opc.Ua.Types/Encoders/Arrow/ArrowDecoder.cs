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
#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Decodes OPC UA values from the experimental Apache Arrow stream representation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Encoders")]
    public sealed class ArrowDecoder : IDecoder
    {
        private readonly RecordBatch _batch;
        private readonly Dictionary<string, int> _columns = new(StringComparer.Ordinal);
        private bool _closed;

        /// <summary>
        /// Initializes a new ArrowDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "buffer">The encoded payload buffer to decode.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ArrowDecoder(byte[] buffer, IServiceMessageContext context)
            : this(new MemoryStream(buffer, false), context) { }

        /// <summary>
        /// Initializes a new ArrowDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "buffer">The encoded payload buffer to decode.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ArrowDecoder(ReadOnlyMemory<byte> buffer, IServiceMessageContext context)
            : this(buffer.ToArray(), context) { }

        /// <summary>
        /// Initializes a new ArrowDecoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ArrowDecoder(Stream stream, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            using var reader = new ArrowStreamReader(
                stream ?? throw new ArgumentNullException(nameof(stream)),
                leaveOpen: true
            );
            _batch =
                reader.ReadNextRecordBatch() ?? throw new FormatException("Arrow stream contains no record batch.");
            for (int ii = 0; ii < _batch.Schema.FieldsList.Count; ii++)
            {
                _columns[_batch.Schema.GetFieldByIndex(ii).Name] = ii;
            }
        }

        /// <inheritdoc/>
        public EncodingType EncodingType
        {
            get { return EncodingType.Arrow; }
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
            if (!_closed)
            {
                _batch.Dispose();
                _closed = true;
            }
        }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris) { }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri) { }

        /// <inheritdoc/>
        public void PopNamespace() { }

        private (Field Field, IArrowArray Array) Col(string? name)
        {
            if (_closed)
            {
                throw new ObjectDisposedException(nameof(ArrowDecoder));
            }

            string key = name ?? ArrowEncoder.ValueName;
            if (!_columns.TryGetValue(key, out int index))
            {
                throw new FormatException($"Arrow field '{key}' is not present.");
            }

            return (_batch.Schema.GetFieldByIndex(index), _batch.Column(index));
        }

        private ExtensionObject ResolveExtension(ExtensionObject value)
        {
            if (value.IsNull
                || value.TypeId.IsNull
                || !value.TryGetAsBinary(out ByteString body)
                || body.IsNull)
            {
                return value;
            }

            if (!Context.Factory.TryGetEncodeableType(value.TypeId, out IEncodeableType? activator))
            {
                return value;
            }

            IEncodeable instance = activator.CreateInstance();
            using var decoder = new BinaryDecoder(body.ToArray(), Context);
            instance.Decode(decoder);
            return new ExtensionObject(value.TypeId, instance);
        }

        private Variant ResolveVariant(Variant value)
        {
            if (value.TypeInfo.BuiltInType != BuiltInType.ExtensionObject)
            {
                return value;
            }

            if (value.TypeInfo.IsArray)
            {
                return new Variant(value.GetExtensionObjectArray().ConvertAll(ResolveExtension));
            }

            if (value.TypeInfo.IsMatrix)
            {
                return new Variant(value.GetExtensionObjectMatrix().ConvertAll(ResolveExtension));
            }

            return new Variant(ResolveExtension(value.GetExtensionObject()));
        }

        private DataValue ResolveDataValue(DataValue value)
        {
            if (value.WrappedValue.TypeInfo.BuiltInType != BuiltInType.ExtensionObject)
            {
                return value;
            }

            return new DataValue(
                ResolveVariant(value.WrappedValue),
                value.StatusCode,
                value.SourceTimestamp,
                value.ServerTimestamp,
                value.SourcePicoseconds,
                value.ServerPicoseconds);
        }

        private T DecodeEncodeableBody<T>(ByteString body, Func<T> factory)
            where T : IEncodeable
        {
            T instance = factory();
            if (!body.IsNull)
            {
                using var decoder = new BinaryDecoder(body.ToArray(), Context);
                instance.Decode(decoder);
            }

            return instance;
        }

        private T CreateEncodeable<T>(ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (!Context.Factory.TryGetEncodeableType(encodeableTypeId, out IEncodeableType? activator))
            {
                throw new NotSupportedException($"Cannot decode Arrow encodeable type {encodeableTypeId}.");
            }

            return (T)activator.CreateInstance();
        }

        private static T ExtractEncodeable<T>(ExtensionObject value)
            where T : IEncodeable
        {
            if (value.TryGetValue(out IEncodeable? body) && body is T typed)
            {
                return typed;
            }

            throw new NotSupportedException($"Cannot decode Arrow encodeable {typeof(T).Name}.");
        }

        /// <inheritdoc/>
        public T DecodeMessage<T>()
            where T : IEncodeable
        {
            throw new NotSupportedException("Arrow message decode is not supported yet.");
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string? fieldName)
        {
            return ((BooleanArray)Col(fieldName).Array).GetValue(0) ?? false;
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string? fieldName)
        {
            return ((Int8Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public byte ReadByte(string? fieldName)
        {
            return ((UInt8Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public short ReadInt16(string? fieldName)
        {
            return ((Int16Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string? fieldName)
        {
            return ((UInt16Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public int ReadInt32(string? fieldName)
        {
            return ((Int32Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string? fieldName)
        {
            return ((UInt32Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public long ReadInt64(string? fieldName)
        {
            return ((Int64Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string? fieldName)
        {
            return ((UInt64Array)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public float ReadFloat(string? fieldName)
        {
            return ((FloatArray)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public double ReadDouble(string? fieldName)
        {
            return ((DoubleArray)Col(fieldName).Array).GetValue(0) ?? default;
        }

        /// <inheritdoc/>
        public string? ReadString(string? fieldName)
        {
            var a = (StringArray)Col(fieldName).Array;
            return a.IsNull(0) ? null : a.GetString(0);
        }

        /// <inheritdoc/>
        public DateTimeUtc ReadDateTime(string? fieldName)
        {
            var a = (Int64Array)Col(fieldName).Array;
            return a.IsNull(0) ? default : new DateTimeUtc(a.GetValue(0) ?? 0);
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string? fieldName)
        {
            return A.ReadGuid(Col(fieldName).Array, 0);
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string? fieldName)
        {
            return A.ReadBytes(Col(fieldName).Array, 0);
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string? fieldName)
        {
            string? xml = ReadString(fieldName);
            return xml == null ? default! : (XmlElement)xml;
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string? fieldName)
        {
            return A.ReadNodeId(Col(fieldName).Array, 0);
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName)
        {
            return A.ReadExpandedNodeId(Col(fieldName).Array, 0);
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string? fieldName)
        {
            return new StatusCode(ReadUInt32(fieldName));
        }

        /// <inheritdoc/>
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName)
        {
            return A.ReadDiagnostic(Col(fieldName).Array, 0);
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string? fieldName)
        {
            return A.ReadQualifiedName(Col(fieldName).Array, 0);
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string? fieldName)
        {
            return A.ReadLocalizedText(Col(fieldName).Array, 0);
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string? fieldName)
        {
            return ResolveVariant(A.ReadVariant(Col(fieldName).Array, 0));
        }

        /// <inheritdoc/>
        public DataValue ReadDataValue(string? fieldName)
        {
            return ResolveDataValue(A.ReadDataValue(Col(fieldName).Array, 0));
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string? fieldName)
        {
            return ResolveExtension(A.ReadExtension(Col(fieldName).Array, 0));
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            return DecodeEncodeableBody(A.ReadBytes(Col(fieldName).Array, 0), () => CreateEncodeable<T>(encodeableTypeId));
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            return DecodeEncodeableBody(A.ReadBytes(Col(fieldName).Array, 0), () => new T());
        }

        /// <inheritdoc/>
        public T ReadEncodeableAsExtensionObject<T>(string? fieldName)
            where T : IEncodeable
        {
            return ExtractEncodeable<T>(ReadExtensionObject(fieldName));
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string? fieldName)
            where T : struct, Enum
        {
            return (T)Enum.ToObject(typeof(T), ReadInt32(fieldName));
        }

        /// <inheritdoc/>
        public EnumValue ReadEnumerated(string? fieldName)
        {
            return new EnumValue(ReadInt32(fieldName));
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadBoolMany);
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadI8Many);
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadU8Many);
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadI16Many);
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadU16Many);
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadI32Many);
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadU32Many);
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadI64Many);
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadU64Many);
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadF32Many);
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadF64Many);
        }

        /// <inheritdoc/>
        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadStrMany);
        }

        /// <inheritdoc/>
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadDateTimeMany);
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadGuidMany);
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadBytesMany);
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName)
        {
            return ReadStringArray(fieldName).ConvertAll(x => x == null ? default! : (XmlElement)x);
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadNodeIdMany);
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadExpandedNodeIdMany);
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadStatusMany);
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadDiagnosticMany);
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadQualifiedNameMany);
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadLocalizedTextMany);
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadVariantMany).ConvertAll(ResolveVariant);
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue> ReadDataValueArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadDataValueMany).ConvertAll(ResolveDataValue);
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName)
        {
            return A.ReadList(Col(fieldName), A.ReadExtensionMany).ConvertAll(ResolveExtension);
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
            return A.ReadList(Col(fieldName), A.ReadBytesMany).ConvertAll(b => DecodeEncodeableBody(b, () => new T()));
        }

        /// <summary>
        /// Reads EncodeableArray from the experimental encoded representation.
        /// </summary>
        /// <typeparam name = "T">The OPC UA encodeable or value type processed by this member.</typeparam>
        /// <param name = "fieldName">The OPC UA field name associated with the encoded member.</param>
        /// <param name = "encodeableTypeId">The expanded type identifier used to resolve the encodeable body.</param>
        /// <returns>The result produced by this codec helper.</returns>
        /// <exception cref = "NotSupportedException">The requested OPC UA value shape is not supported by this experimental codec.</exception>
        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            return A.ReadList(Col(fieldName), A.ReadBytesMany)
                .ConvertAll(b => DecodeEncodeableBody(b, () => CreateEncodeable<T>(encodeableTypeId)));
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
            return ReadExtensionObjectArray(fieldName).ConvertAll(ExtractEncodeable<T>);
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
            return A.ReadMatrix(Col(fieldName).Array, 0, A.ReadBytesMany)
                .ConvertAll(b => DecodeEncodeableBody(b, () => CreateEncodeable<T>(encodeableTypeId)));
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
            return A.ReadMatrix(Col(fieldName).Array, 0, A.ReadBytesMany)
                .ConvertAll(b => DecodeEncodeableBody(b, () => new T()));
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string? fieldName)
            where T : struct, Enum
        {
            return ReadInt32Array(fieldName).ConvertAll(x => (T)Enum.ToObject(typeof(T), x));
        }

        /// <inheritdoc/>
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName)
        {
            return ReadInt32Array(fieldName).ConvertAll(x => new EnumValue(x));
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string? fieldName, TypeInfo typeInfo)
        {
            return ReadVariant(fieldName);
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string? fieldName)
        {
            fieldName = null;
            return ReadUInt32(ArrowEncoder.SwitchName);
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            return HasField(ArrowEncoder.MaskName) ? ReadUInt32(ArrowEncoder.MaskName) : 0;
        }

        /// <inheritdoc/>
        public bool HasField(string fieldName)
        {
            return _columns.ContainsKey(fieldName);
        }
    }
}
#endif
