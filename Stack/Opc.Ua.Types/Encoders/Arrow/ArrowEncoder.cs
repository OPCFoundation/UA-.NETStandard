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
using System.Linq;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Encodes OPC UA values into the experimental Apache Arrow stream representation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_1")]
    public sealed class ArrowEncoder : IEncoder
    {
        /// <summary>
        /// Names the default Arrow field used when no OPC UA field name is supplied.
        /// </summary>
        internal const string ValueName = "value";

        /// <summary>
        /// Names the synthetic Arrow field that stores union switch values.
        /// </summary>
        internal const string SwitchName = "__switch";

        /// <summary>
        /// Names the synthetic Arrow field that stores optional-field encoding masks.
        /// </summary>
        internal const string MaskName = "__encodingMask";
        private readonly Stream _stream;
        private readonly bool _ownsStream;
        private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
        private bool _closed;

        /// <summary>
        /// Initializes a new ArrowEncoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ArrowEncoder(IServiceMessageContext context)
            : this(new MemoryStream(), context, false) { }

        /// <summary>
        /// Initializes a new ArrowEncoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        /// <param name = "leaveOpen">True to leave the caller-owned stream open when the codec is closed.</param>
        public ArrowEncoder(Stream stream, IServiceMessageContext context, bool leaveOpen = true)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _ownsStream = !leaveOpen;
        }

        /// <inheritdoc/>
        public EncodingType EncodingType
        {
            get { return EncodingType.Arrow; }
        }

        /// <inheritdoc/>
        public bool CanOmitFields
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_closed)
            {
                Close();
            }

            if (_ownsStream)
            {
                _stream.Dispose();
            }
        }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri) { }

        /// <inheritdoc/>
        public void PopNamespace() { }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris) { }

        /// <inheritdoc/>
        public int Close()
        {
            if (_closed)
            {
                throw new ObjectDisposedException(nameof(ArrowEncoder));
            }

            long start = _stream.CanSeek ? _stream.Position : 0;
            var schema = new Apache.Arrow.Schema.Builder().Metadata("opcua-arrow", "1");
            var arrays = new List<IArrowArray>();
            foreach (var item in _slots)
            {
                schema.Field(item.Value.Field(item.Key));
                arrays.Add(item.Value.Array);
            }

            Apache.Arrow.Schema built = schema.Build();
            using var batch = new RecordBatch(built, arrays, 1);
            using var writer = new ArrowStreamWriter(_stream, built, leaveOpen: true);
            writer.WriteStart();
            writer.WriteRecordBatch(batch);
            writer.WriteEnd();
            _closed = true;
            return _stream.CanSeek ? checked((int)(_stream.Position - start)) : 0;
        }

        /// <inheritdoc/>
        public string? CloseAndReturnText()
        {
            return Convert.ToBase64String(CloseAndReturnBuffer());
        }

        /// <summary>
        /// Completes the encoder and returns the encoded bytes from its memory stream.
        /// </summary>
        /// <returns>The encoded payload bytes.</returns>
        public byte[] CloseAndReturnBuffer()
        {
            Close();
            if (_stream is MemoryStream ms)
            {
                return ms.ToArray();
            }

            throw new NotSupportedException("ArrowEncoder can only return bytes when backed by a MemoryStream.");
        }

        private void Put(string? name, Slot slot)
        {
            _slots[name ?? ValueName] = slot;
        }

        private static NotSupportedException Unsupported(string member)
        {
            return new($"OPC UA Arrow reference encoder does not yet support {member}.");
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message)
            where T : IEncodeable, new()
        {
            WriteEncodeable(ValueName, message);
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteEncodeable(ValueName, message, encodeableTypeId);
        }

        /// <inheritdoc/>
        public void WriteBoolean(string? fieldName, bool value)
        {
            Put(fieldName, A.Bool(value));
        }

        /// <inheritdoc/>
        public void WriteSByte(string? fieldName, sbyte value)
        {
            Put(fieldName, A.I8(value));
        }

        /// <inheritdoc/>
        public void WriteByte(string? fieldName, byte value)
        {
            Put(fieldName, A.U8(value));
        }

        /// <inheritdoc/>
        public void WriteInt16(string? fieldName, short value)
        {
            Put(fieldName, A.I16(value));
        }

        /// <inheritdoc/>
        public void WriteUInt16(string? fieldName, ushort value)
        {
            Put(fieldName, A.U16(value));
        }

        /// <inheritdoc/>
        public void WriteInt32(string? fieldName, int value)
        {
            Put(fieldName, A.I32(value));
        }

        /// <inheritdoc/>
        public void WriteUInt32(string? fieldName, uint value)
        {
            Put(fieldName, A.U32(value));
        }

        /// <inheritdoc/>
        public void WriteInt64(string? fieldName, long value)
        {
            Put(fieldName, A.I64(value));
        }

        /// <inheritdoc/>
        public void WriteUInt64(string? fieldName, ulong value)
        {
            Put(fieldName, A.U64(value));
        }

        /// <inheritdoc/>
        public void WriteFloat(string? fieldName, float value)
        {
            Put(fieldName, A.F32(value));
        }

        /// <inheritdoc/>
        public void WriteDouble(string? fieldName, double value)
        {
            Put(fieldName, A.F64(value));
        }

        /// <inheritdoc/>
        public void WriteString(string? fieldName, string? value)
        {
            Put(fieldName, A.Str(value));
        }

        /// <inheritdoc/>
        public void WriteDateTime(string? fieldName, DateTimeUtc value)
        {
            Put(fieldName, A.DateTime(value));
        }

        /// <inheritdoc/>
        public void WriteGuid(string? fieldName, Uuid value)
        {
            Put(fieldName, A.Guid(value));
        }

        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ByteString value)
        {
            Put(fieldName, A.Bytes(value));
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ReadOnlySpan<byte> value)
        {
            WriteByteString(fieldName, ByteString.From(value));
        }
#endif

        /// <inheritdoc/>
        public void WriteXmlElement(string? fieldName, XmlElement value)
        {
            Put(fieldName, A.Str(value.OuterXml));
        }

        /// <inheritdoc/>
        public void WriteNodeId(string? fieldName, NodeId value)
        {
            Put(fieldName, A.NodeId(value));
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value)
        {
            Put(fieldName, A.ExpandedNodeId(value));
        }

        /// <inheritdoc/>
        public void WriteStatusCode(string? fieldName, StatusCode value)
        {
            Put(fieldName, A.Status(value));
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value)
        {
            Put(fieldName, A.Diagnostic(value));
        }

        /// <inheritdoc/>
        public void WriteQualifiedName(string? fieldName, QualifiedName value)
        {
            Put(fieldName, A.QualifiedName(value));
        }

        /// <inheritdoc/>
        public void WriteLocalizedText(string? fieldName, LocalizedText value)
        {
            Put(fieldName, A.LocalizedText(value));
        }

        /// <inheritdoc/>
        public void WriteVariant(string? fieldName, in Variant value)
        {
            Variant v = value;
            Put(fieldName, A.Variant(v));
        }

        /// <inheritdoc/>
        public void WriteDataValue(string? fieldName, in DataValue value)
        {
            DataValue v = value;
            Put(fieldName, A.DataValue(v));
        }

        /// <inheritdoc/>
        public void WriteExtensionObject(string? fieldName, ExtensionObject value)
        {
            Put(fieldName, A.Extension(value));
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value)
            where T : IEncodeable, new()
        {
            throw Unsupported(nameof(WriteEncodeable));
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            throw Unsupported(nameof(WriteEncodeable));
        }

        /// <inheritdoc/>
        public void WriteEncodeableAsExtensionObject<T>(string? fieldName, T value)
            where T : IEncodeable
        {
            WriteExtensionObject(fieldName, new ExtensionObject(value));
        }

        /// <inheritdoc/>
        public void WriteEnumerated<T>(string? fieldName, T value)
            where T : struct, Enum
        {
            WriteInt32(fieldName, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteEnumerated(string? fieldName, EnumValue value)
        {
            WriteInt32(fieldName, value.Value);
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string? fieldName, ArrayOf<bool> values)
        {
            Put(fieldName, A.List(values, A.BoolMany));
        }

        /// <inheritdoc/>
        public void WriteSByteArray(string? fieldName, ArrayOf<sbyte> values)
        {
            Put(fieldName, A.List(values, A.I8Many));
        }

        /// <inheritdoc/>
        public void WriteByteArray(string? fieldName, ArrayOf<byte> values)
        {
            Put(fieldName, A.List(values, A.U8Many));
        }

        /// <inheritdoc/>
        public void WriteInt16Array(string? fieldName, ArrayOf<short> values)
        {
            Put(fieldName, A.List(values, A.I16Many));
        }

        /// <inheritdoc/>
        public void WriteUInt16Array(string? fieldName, ArrayOf<ushort> values)
        {
            Put(fieldName, A.List(values, A.U16Many));
        }

        /// <inheritdoc/>
        public void WriteInt32Array(string? fieldName, ArrayOf<int> values)
        {
            Put(fieldName, A.List(values, A.I32Many));
        }

        /// <inheritdoc/>
        public void WriteUInt32Array(string? fieldName, ArrayOf<uint> values)
        {
            Put(fieldName, A.List(values, A.U32Many));
        }

        /// <inheritdoc/>
        public void WriteInt64Array(string? fieldName, ArrayOf<long> values)
        {
            Put(fieldName, A.List(values, A.I64Many));
        }

        /// <inheritdoc/>
        public void WriteUInt64Array(string? fieldName, ArrayOf<ulong> values)
        {
            Put(fieldName, A.List(values, A.U64Many));
        }

        /// <inheritdoc/>
        public void WriteFloatArray(string? fieldName, ArrayOf<float> values)
        {
            Put(fieldName, A.List(values, A.F32Many));
        }

        /// <inheritdoc/>
        public void WriteDoubleArray(string? fieldName, ArrayOf<double> values)
        {
            Put(fieldName, A.List(values, A.F64Many));
        }

        /// <inheritdoc/>
        public void WriteStringArray(string? fieldName, ArrayOf<string> values)
        {
            Put(fieldName, A.List(values, A.StrMany));
        }

        /// <inheritdoc/>
        public void WriteDateTimeArray(string? fieldName, ArrayOf<DateTimeUtc> values)
        {
            Put(fieldName, A.List(values, A.DateTimeMany));
        }

        /// <inheritdoc/>
        public void WriteGuidArray(string? fieldName, ArrayOf<Uuid> values)
        {
            Put(fieldName, A.List(values, A.GuidMany));
        }

        /// <inheritdoc/>
        public void WriteByteStringArray(string? fieldName, ArrayOf<ByteString> values)
        {
            Put(fieldName, A.List(values, A.BytesMany));
        }

        /// <inheritdoc/>
        public void WriteXmlElementArray(string? fieldName, ArrayOf<XmlElement> values)
        {
            Put(fieldName, A.List(values.ConvertAll<string>(x => x.OuterXml!), A.StrMany));
        }

        /// <inheritdoc/>
        public void WriteNodeIdArray(string? fieldName, ArrayOf<NodeId> values)
        {
            Put(fieldName, A.ListStruct(values, A.NodeId));
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeIdArray(string? fieldName, ArrayOf<ExpandedNodeId> values)
        {
            Put(fieldName, A.ListStruct(values, A.ExpandedNodeId));
        }

        /// <inheritdoc/>
        public void WriteStatusCodeArray(string? fieldName, ArrayOf<StatusCode> values)
        {
            Put(fieldName, A.List(values.ConvertAll(x => x.Code), A.U32Many));
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfoArray(string? fieldName, ArrayOf<DiagnosticInfo> values)
        {
            Put(fieldName, A.ListStruct(values, A.Diagnostic));
        }

        /// <inheritdoc/>
        public void WriteQualifiedNameArray(string? fieldName, ArrayOf<QualifiedName> values)
        {
            Put(fieldName, A.ListStruct(values, A.QualifiedName));
        }

        /// <inheritdoc/>
        public void WriteLocalizedTextArray(string? fieldName, ArrayOf<LocalizedText> values)
        {
            Put(fieldName, A.ListStruct(values, A.LocalizedText));
        }

        /// <inheritdoc/>
        public void WriteVariantArray(string? fieldName, ArrayOf<Variant> values)
        {
            Put(fieldName, A.ListStruct(values, A.Variant));
        }

        /// <inheritdoc/>
        public void WriteDataValueArray(string? fieldName, ArrayOf<DataValue> values)
        {
            Put(fieldName, A.ListStruct(values, A.DataValue));
        }

        /// <inheritdoc/>
        public void WriteExtensionObjectArray(string? fieldName, ArrayOf<ExtensionObject> values)
        {
            Put(fieldName, A.ListStruct(values, A.Extension));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable, new()
        {
            throw Unsupported(nameof(WriteEncodeableArray));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            throw Unsupported(nameof(WriteEncodeableArray));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArrayAsExtensionObjects<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            WriteExtensionObjectArray(fieldName, values.ConvertAll(x => new ExtensionObject(x)));
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string? fieldName, ArrayOf<T> values)
            where T : struct, Enum
        {
            WriteInt32Array(fieldName, values.ConvertAll(x => Convert.ToInt32(x, CultureInfo.InvariantCulture)));
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray(string? fieldName, ArrayOf<EnumValue> values)
        {
            WriteInt32Array(fieldName, values.ConvertAll(x => x.Value));
        }

        /// <inheritdoc/>
        public void WriteVariantValue(string? fieldName, in Variant value)
        {
            Variant v = value;
            Put(fieldName, A.Variant(v));
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values)
            where T : IEncodeable, new()
        {
            throw Unsupported(nameof(WriteEncodeableMatrix));
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            throw Unsupported(nameof(WriteEncodeableMatrix));
        }

        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string? fieldName)
        {
            fieldName = null;
            WriteUInt32(SwitchName, switchField);
        }

        /// <inheritdoc/>
        public void WriteEncodingMask(uint encodingMask)
        {
            WriteUInt32(MaskName, encodingMask);
        }
    }
}
