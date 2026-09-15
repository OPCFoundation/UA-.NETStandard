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
using System.IO;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Encodes OPC UA values using the experimental Avro binary mapping.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Avro")]
    public sealed class AvroEncoder : IEncoder
    {
        private readonly Stream m_stream;
        private readonly AvroBinaryWriter m_writer;
        private readonly bool m_leaveOpen;
        private bool m_closed;
        // When set, the next WriteArray/WriteMatrix emits a *plain* Avro array body (no
        // nullable-union present-marker) for a Variant array/matrix body. Consumed on entry so
        // nested values (e.g. Variant elements) revert to their normal nullable-union encoding.
        private bool m_nextArrayPlain;

        /// <summary>
        /// Initializes a new AvroEncoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public AvroEncoder(IServiceMessageContext context)
            : this(new MemoryStream(), context, false) { }

        /// <summary>
        /// Initializes a new AvroEncoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        /// <param name = "leaveOpen">True to leave the caller-owned stream open when the codec is closed.</param>
        public AvroEncoder(Stream stream, IServiceMessageContext context, bool leaveOpen = true)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_stream = stream ?? throw new ArgumentNullException(nameof(stream));
            m_leaveOpen = leaveOpen;
            m_writer = new AvroBinaryWriter(m_stream);
        }

        /// <inheritdoc/>
        public EncodingType EncodingType
        {
            get { return EncodingType.Avro; }
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
            if (!m_closed)
            {
                Close();
            }
        }

        /// <inheritdoc/>
        public int Close()
        {
            m_writer.Flush();
            m_closed = true;
            int p = m_stream.CanSeek ? (int)m_stream.Position : 0;
            m_writer.Release();
            if (!m_leaveOpen)
            {
                m_stream.Dispose();
            }

            return p;
        }

        /// <summary>
        /// Completes the encoder and returns the encoded bytes from its memory stream.
        /// </summary>
        /// <returns>The encoded payload bytes.</returns>
        public byte[] CloseAndReturnBuffer()
        {
            Close();
            return m_stream is MemoryStream ms
                ? ms.ToArray()
                : throw new NotSupportedException("AvroEncoder was not created over a memory stream.");
        }

        /// <inheritdoc/>
        public string? CloseAndReturnText()
        {
            return Convert.ToBase64String(CloseAndReturnBuffer());
        }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris) { }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri) { }

        /// <inheritdoc/>
        public void PopNamespace() { }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message)
            where T : IEncodeable, new()
        {
            WriteEncodeable(null, message);
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteEncodeable(null, message, encodeableTypeId);
        }

        /// <inheritdoc/>
        public void WriteBoolean(string? fieldName, bool value)
        {
            m_writer.WriteBoolean(value);
        }

        /// <inheritdoc/>
        public void WriteSByte(string? fieldName, sbyte value)
        {
            m_writer.WriteInt(value);
        }

        /// <inheritdoc/>
        public void WriteByte(string? fieldName, byte value)
        {
            m_writer.WriteInt(value);
        }

        /// <inheritdoc/>
        public void WriteInt16(string? fieldName, short value)
        {
            m_writer.WriteInt(value);
        }

        /// <inheritdoc/>
        public void WriteUInt16(string? fieldName, ushort value)
        {
            m_writer.WriteInt(value);
        }

        /// <inheritdoc/>
        public void WriteInt32(string? fieldName, int value)
        {
            m_writer.WriteInt(value);
        }

        /// <inheritdoc/>
        public void WriteUInt32(string? fieldName, uint value)
        {
            m_writer.WriteInt(unchecked((int)value));
        }

        /// <inheritdoc/>
        public void WriteInt64(string? fieldName, long value)
        {
            m_writer.WriteLong(value);
        }

        /// <inheritdoc/>
        public void WriteUInt64(string? fieldName, ulong value)
        {
            m_writer.WriteLong(unchecked((long)value));
        }

        /// <inheritdoc/>
        public void WriteFloat(string? fieldName, float value)
        {
            m_writer.WriteFloat(value);
        }

        /// <inheritdoc/>
        public void WriteDouble(string? fieldName, double value)
        {
            m_writer.WriteDouble(value);
        }

        /// <inheritdoc/>
        public void WriteString(string? fieldName, string? value)
        {
            WriteNullable(value, s => m_writer.WriteString(s));
        }

        /// <inheritdoc/>
        public void WriteDateTime(string? fieldName, DateTimeUtc value)
        {
            m_writer.WriteLong(value.Value);
        }

        /// <inheritdoc/>
        public void WriteGuid(string? fieldName, Uuid value)
        {
            m_writer.WriteFixed(GuidToRfc4122(value.Guid));
        }

        // The Avro `uuid` logical type (and the reference codec) serialises a Guid as its 16 bytes in
        // RFC-4122 (big-endian) order, which differs from .NET's mixed-endian Guid.ToByteArray().
        private static byte[] GuidToRfc4122(Guid value)
        {
            byte[] b = value.ToByteArray();
            return
            [
                b[3], b[2], b[1], b[0],
                b[5], b[4],
                b[7], b[6],
                b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15],
            ];
        }

        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ByteString value)
        {
            WriteNullableBytes(value);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ReadOnlySpan<byte> value)
        {
            WriteNullableBytes(ByteString.From(value));
        }
#endif

        /// <inheritdoc/>
        public void WriteXmlElement(string? fieldName, XmlElement value)
        {
            WriteNullable(value.IsNull ? null : value.OuterXml, s => m_writer.WriteString(s));
        }

        /// <inheritdoc/>
        public void WriteNodeId(string? fieldName, NodeId value)
        {
            if (value.IsNull)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            WriteNodeIdRecord(value);
        }

        // The canonical NodeId record (Part 6 6.6): namespace:int, idType:int, and four id-slots
        // numeric/string/guid/opaque each a nullable(<raw>) union with exactly one present.
        private void WriteNodeIdRecord(NodeId value)
        {
            WriteInt32(null, value.NamespaceIndex);
            WriteInt32(null, (int)value.IdType);

            if (value.IdType == IdType.Numeric && value.TryGetValue(out uint numeric))
            {
                m_writer.WriteLong(1);
                m_writer.WriteLong(numeric);
            }
            else
            {
                m_writer.WriteLong(0);
            }

            if (value.IdType == IdType.String && value.TryGetValue(out string text))
            {
                m_writer.WriteLong(1);
                m_writer.WriteString(text ?? string.Empty);
            }
            else
            {
                m_writer.WriteLong(0);
            }

            if (value.IdType == IdType.Guid && value.TryGetValue(out Guid guid))
            {
                m_writer.WriteLong(1);
                m_writer.WriteFixed(GuidToRfc4122(guid));
            }
            else
            {
                m_writer.WriteLong(0);
            }

            if (value.IdType == IdType.Opaque && value.TryGetValue(out ByteString opaque))
            {
                m_writer.WriteLong(1);
                m_writer.WriteBytes(opaque.Span);
            }
            else
            {
                m_writer.WriteLong(0);
            }
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value)
        {
            if (value.IsNull)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            // nodeId is a non-nullable NodeId record; namespaceUri a nullable(string); serverIndex a long.
            WriteNodeIdRecord(value.InnerNodeId);
            WriteString(null, value.NamespaceUri);
            m_writer.WriteLong(value.ServerIndex);
        }

        /// <inheritdoc/>
        public void WriteStatusCode(string? fieldName, StatusCode value)
        {
            WriteUInt32(null, value.Code);
        }

        /// <inheritdoc/>
        public void WriteQualifiedName(string? fieldName, QualifiedName value)
        {
            if (value.IsNull)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            WriteInt32(null, value.NamespaceIndex);
            WriteString(null, value.Name);
        }

        /// <inheritdoc/>
        public void WriteLocalizedText(string? fieldName, LocalizedText value)
        {
            if (value.IsNull)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            WriteString(null, value.Locale);
            WriteString(null, value.Text);
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value)
        {
            if (value == null)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            WriteNullableInt(value.SymbolicId >= 0, value.SymbolicId);
            WriteNullableInt(value.NamespaceUri >= 0, value.NamespaceUri);
            WriteNullableInt(value.Locale >= 0, value.Locale);
            WriteNullableInt(value.LocalizedText >= 0, value.LocalizedText);
            WriteString(null, value.AdditionalInfo);
            WriteNullable(
                value.InnerStatusCode.Code != StatusCodes.Good,
                value.InnerStatusCode,
                v => WriteStatusCode(null, v)
            );
            WriteDiagnosticInfo(null, value.InnerDiagnosticInfo);
        }

        /// <inheritdoc/>
        public void WriteDataValue(string? fieldName, in DataValue value)
        {
            if (value.IsNull)
            {
                WriteNullFields(6);
                return;
            }

            WriteNullable(!value.WrappedValue.IsNull, value.WrappedValue, v => WriteVariant(null, in v));
            WriteNullable(
                !value.StatusCode.Equals(StatusCodes.Good, StatusCodeComparison.AllBits),
                value.StatusCode,
                v => WriteStatusCode(null, v)
            );
            WriteNullable(!value.SourceTimestamp.IsNull, value.SourceTimestamp, v => WriteDateTime(null, v));
            WriteNullable(value.SourcePicoseconds != 0, value.SourcePicoseconds, v => WriteUInt16(null, v));
            WriteNullable(!value.ServerTimestamp.IsNull, value.ServerTimestamp, v => WriteDateTime(null, v));
            WriteNullable(value.ServerPicoseconds != 0, value.ServerPicoseconds, v => WriteUInt16(null, v));
        }

        /// <inheritdoc/>
        public void WriteExtensionObject(string? fieldName, ExtensionObject value)
        {
            WriteExpandedNodeId(null, value.TypeId);
            if (value.IsNull || value.Encoding == ExtensionObjectEncoding.None)
            {
                m_writer.WriteLong(0);
                return;
            }

            if (value.TryGetValue(out IEncodeable? enc))
            {
                m_writer.WriteLong(1);
                enc!.Encode(this);
                return;
            }

            if (value.TryGetAsBinary(out ByteString bytes))
            {
                m_writer.WriteLong(2);
                m_writer.WriteBytes(bytes.Span);
                return;
            }

            if (value.TryGetAsXml(out XmlElement xml))
            {
                m_writer.WriteLong(3);
                m_writer.WriteString(xml.OuterXml ?? string.Empty);
                return;
            }

            throw new NotSupportedException($"Unsupported ExtensionObject body encoding {value.Encoding}.");
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value)
            where T : IEncodeable, new()
        {
            (value ?? new T()).Encode(this);
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            value.Encode(this);
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
            WriteInt32(fieldName, EnumHelper.EnumToInt32(value));
        }

        /// <inheritdoc/>
        public void WriteEnumerated(string? fieldName, EnumValue value)
        {
            WriteInt32(fieldName, value.Value);
        }

        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string? fieldName)
        {
            fieldName = null;
            WriteUInt32("switch", switchField);
        }

        /// <inheritdoc/>
        public void WriteEncodingMask(uint encodingMask)
        {
            WriteUInt32("encodingMask", encodingMask);
        }

        private void WriteNullable<T>(bool hasValue, T value, Action<T> write)
        {
            if (!hasValue)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            write(value);
        }

        private void WriteNullable<T>(T? value, Action<T> write)
            where T : class
        {
            if (value == null)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            write(value);
        }

        private void WriteNullableInt(bool hasValue, int value)
        {
            WriteNullable(hasValue, value, v => WriteInt32(null, v));
        }

        private void WriteNullFields(int count)
        {
            for (int i = 0; i < count; i++)
            {
                m_writer.WriteLong(0);
            }
        }

        private void WriteNullableBytes(ByteString value)
        {
            if (value.IsNull)
            {
                m_writer.WriteLong(0);
                return;
            }

            m_writer.WriteLong(1);
            m_writer.WriteBytes(value.Span);
        }

        private void WriteArray<T>(ArrayOf<T> values, Action<T> write)
        {
            bool plain = m_nextArrayPlain;
            m_nextArrayPlain = false;
            if (!plain)
            {
                if (values.IsNull)
                {
                    m_writer.WriteLong(0);
                    return;
                }

                m_writer.WriteLong(1);
            }

            if (!values.IsNull && values.Count > 0)
            {
                m_writer.WriteLong(values.Count);
                foreach (T value in values.Span)
                {
                    write(value);
                }
            }

            m_writer.WriteLong(0);
        }

        private void WriteMatrix<T>(MatrixOf<T> matrix, Action<ArrayOf<T>> writeArray)
        {
            bool plain = m_nextArrayPlain;
            m_nextArrayPlain = false;
            if (!plain)
            {
                if (matrix.IsNull)
                {
                    m_writer.WriteLong(0);
                    return;
                }

                m_writer.WriteLong(1);
                WriteInt32Array(null, matrix.Dimensions);
                writeArray(matrix.ToArrayOf());
                return;
            }

            // A Variant matrix body is a MatrixBody record { dimensions: array<int>, values: array }
            // where both are *plain* Avro arrays (no present-marker); the Variant-level dimensions
            // written by WriteVariant carry the shape a second time (nullable there).
            m_nextArrayPlain = true;
            WriteInt32Array(null, matrix.Dimensions);
            m_nextArrayPlain = true;
            writeArray(matrix.ToArrayOf());
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string? fieldName, ArrayOf<bool> values)
        {
            WriteArray(values, v => WriteBoolean(null, v));
        }

        /// <inheritdoc/>
        public void WriteSByteArray(string? fieldName, ArrayOf<sbyte> values)
        {
            WriteArray(values, v => WriteSByte(null, v));
        }

        /// <inheritdoc/>
        public void WriteByteArray(string? fieldName, ArrayOf<byte> values)
        {
            WriteArray(values, v => WriteByte(null, v));
        }

        /// <inheritdoc/>
        public void WriteInt16Array(string? fieldName, ArrayOf<short> values)
        {
            WriteArray(values, v => WriteInt16(null, v));
        }

        /// <inheritdoc/>
        public void WriteUInt16Array(string? fieldName, ArrayOf<ushort> values)
        {
            WriteArray(values, v => WriteUInt16(null, v));
        }

        /// <inheritdoc/>
        public void WriteInt32Array(string? fieldName, ArrayOf<int> values)
        {
            WriteArray(values, v => WriteInt32(null, v));
        }

        /// <inheritdoc/>
        public void WriteUInt32Array(string? fieldName, ArrayOf<uint> values)
        {
            WriteArray(values, v => WriteUInt32(null, v));
        }

        /// <inheritdoc/>
        public void WriteInt64Array(string? fieldName, ArrayOf<long> values)
        {
            WriteArray(values, v => WriteInt64(null, v));
        }

        /// <inheritdoc/>
        public void WriteUInt64Array(string? fieldName, ArrayOf<ulong> values)
        {
            WriteArray(values, v => WriteUInt64(null, v));
        }

        /// <inheritdoc/>
        public void WriteFloatArray(string? fieldName, ArrayOf<float> values)
        {
            WriteArray(values, v => WriteFloat(null, v));
        }

        /// <inheritdoc/>
        public void WriteDoubleArray(string? fieldName, ArrayOf<double> values)
        {
            WriteArray(values, v => WriteDouble(null, v));
        }

        /// <inheritdoc/>
        public void WriteStringArray(string? fieldName, ArrayOf<string> values)
        {
            WriteArray(values, v => WriteString(null, v));
        }

        /// <inheritdoc/>
        public void WriteDateTimeArray(string? fieldName, ArrayOf<DateTimeUtc> values)
        {
            WriteArray(values, v => WriteDateTime(null, v));
        }

        /// <inheritdoc/>
        public void WriteGuidArray(string? fieldName, ArrayOf<Uuid> values)
        {
            WriteArray(values, v => WriteGuid(null, v));
        }

        /// <inheritdoc/>
        public void WriteByteStringArray(string? fieldName, ArrayOf<ByteString> values)
        {
            WriteArray(values, v => WriteByteString(null, v));
        }

        /// <inheritdoc/>
        public void WriteXmlElementArray(string? fieldName, ArrayOf<XmlElement> values)
        {
            WriteArray(values, v => WriteXmlElement(null, v));
        }

        /// <inheritdoc/>
        public void WriteNodeIdArray(string? fieldName, ArrayOf<NodeId> values)
        {
            WriteArray(values, v => WriteNodeId(null, v));
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeIdArray(string? fieldName, ArrayOf<ExpandedNodeId> values)
        {
            WriteArray(values, v => WriteExpandedNodeId(null, v));
        }

        /// <inheritdoc/>
        public void WriteStatusCodeArray(string? fieldName, ArrayOf<StatusCode> values)
        {
            WriteArray(values, v => WriteStatusCode(null, v));
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfoArray(string? fieldName, ArrayOf<DiagnosticInfo> values)
        {
            WriteArray(values, v => WriteDiagnosticInfo(null, v));
        }

        /// <inheritdoc/>
        public void WriteQualifiedNameArray(string? fieldName, ArrayOf<QualifiedName> values)
        {
            WriteArray(values, v => WriteQualifiedName(null, v));
        }

        /// <inheritdoc/>
        public void WriteLocalizedTextArray(string? fieldName, ArrayOf<LocalizedText> values)
        {
            WriteArray(values, v => WriteLocalizedText(null, v));
        }

        /// <inheritdoc/>
        public void WriteVariantArray(string? fieldName, ArrayOf<Variant> values)
        {
            WriteArray(values, v => WriteVariant(null, in v));
        }

        /// <inheritdoc/>
        public void WriteDataValueArray(string? fieldName, ArrayOf<DataValue> values)
        {
            WriteArray(values, v => WriteDataValue(null, in v));
        }

        /// <inheritdoc/>
        public void WriteExtensionObjectArray(string? fieldName, ArrayOf<ExtensionObject> values)
        {
            WriteArray(values, v => WriteExtensionObject(null, v));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable, new()
        {
            WriteArray(values, v => WriteEncodeable(null, v));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteArray(values, v => WriteEncodeable(null, v, encodeableTypeId));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArrayAsExtensionObjects<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            WriteArray(values, v => WriteEncodeableAsExtensionObject(null, v));
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string? fieldName, ArrayOf<T> values)
            where T : struct, Enum
        {
            WriteArray(values, v => WriteEnumerated(null, v));
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray(string? fieldName, ArrayOf<EnumValue> values)
        {
            WriteArray(values, v => WriteEnumerated(null, v));
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values)
            where T : IEncodeable, new()
        {
            WriteMatrix(values, a => WriteEncodeableArray(null, a));
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteMatrix(values, a => WriteEncodeableArray(null, a, encodeableTypeId));
        }

        /// <inheritdoc/>
        public void WriteVariant(string? fieldName, in Variant value)
        {
            WriteInt32(null, (int)NormalizeVariantType(value.TypeInfo.BuiltInType));
            if (value.TypeInfo.IsMatrix)
            {
                WriteInt32Array(null, GetVariantDimensions(in value));
            }
            else
            {
                // dimensions is a nullable(array<int>) that is null for scalars and arrays.
                m_writer.WriteLong(0);
            }

            WriteVariantBody(in value);
        }

        private static ArrayOf<int> GetVariantDimensions(in Variant value)
        {
            if (value.TypeInfo.IsScalar || value.TypeInfo.IsArray)
            {
                return Array.Empty<int>();
            }

            return value.TypeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => value.GetBooleanMatrix().Dimensions,
                BuiltInType.SByte => value.GetSByteMatrix().Dimensions,
                BuiltInType.Byte => value.GetByteMatrix().Dimensions,
                BuiltInType.Int16 => value.GetInt16Matrix().Dimensions,
                BuiltInType.UInt16 => value.GetUInt16Matrix().Dimensions,
                BuiltInType.Int32 => value.GetInt32Matrix().Dimensions,
                BuiltInType.Enumeration => value.GetEnumerationMatrix().Dimensions,
                BuiltInType.UInt32 => value.GetUInt32Matrix().Dimensions,
                BuiltInType.Int64 => value.GetInt64Matrix().Dimensions,
                BuiltInType.UInt64 => value.GetUInt64Matrix().Dimensions,
                BuiltInType.Float => value.GetFloatMatrix().Dimensions,
                BuiltInType.Double => value.GetDoubleMatrix().Dimensions,
                BuiltInType.String => value.GetStringMatrix().Dimensions,
                BuiltInType.DateTime => value.GetDateTimeMatrix().Dimensions,
                BuiltInType.Guid => value.GetGuidMatrix().Dimensions,
                BuiltInType.ByteString => value.GetByteStringMatrix().Dimensions,
                BuiltInType.XmlElement => value.GetXmlElementMatrix().Dimensions,
                BuiltInType.NodeId => value.GetNodeIdMatrix().Dimensions,
                BuiltInType.ExpandedNodeId => value.GetExpandedNodeIdMatrix().Dimensions,
                BuiltInType.StatusCode => value.GetStatusCodeMatrix().Dimensions,
                BuiltInType.QualifiedName => value.GetQualifiedNameMatrix().Dimensions,
                BuiltInType.LocalizedText => value.GetLocalizedTextMatrix().Dimensions,
                BuiltInType.ExtensionObject => value.GetExtensionObjectMatrix().Dimensions,
                BuiltInType.DataValue => value.GetDataValueMatrix().Dimensions,
                BuiltInType.Variant => value.GetVariantMatrix().Dimensions,
                _ => Array.Empty<int>(),
            };
        }

        /// <inheritdoc/>
        public void WriteVariantValue(string? fieldName, in Variant value)
        {
            WriteVariantBody(in value);
        }

        private void WriteVariantBody(in Variant value)
        {
            TypeInfo typeInfo = value.TypeInfo;
            BuiltInType type = typeInfo.BuiltInType;
            if (value.IsNull || type == BuiltInType.Null || typeInfo.IsUnknown)
            {
                m_writer.WriteLong(0);
                return;
            }

            // The Variant body is an Avro union [ null, then per built-in type: <Type>Scalar,
            // <Type>Array, <Type>MatrixBody ]; the branch index encodes both type and shape.
            int shapeOffset = typeInfo.IsScalar ? 0 : typeInfo.IsArray ? 1 : 2;
            m_writer.WriteLong(VariantBodyBranch(NormalizeVariantType(type), shapeOffset));
            if (typeInfo.IsScalar)
            {
                WriteScalarVariant(in value, type);
            }
            else if (typeInfo.IsArray)
            {
                m_nextArrayPlain = true;
                WriteArrayVariant(in value, type);
            }
            else
            {
                m_nextArrayPlain = true;
                WriteMatrixVariant(in value, type);
            }
        }

        // Body-union branch = 1 + 3*pos + shapeOffset, pos = builtInType enum value - 1
        // (VARIANT_BODY_TYPES is Boolean(1)..ExtensionObject(22)); shapeOffset scalar=0/array=1/matrix=2.
        private static long VariantBodyBranch(BuiltInType type, int shapeOffset)
        {
            return 1 + (3 * ((int)type - 1)) + shapeOffset;
        }

        // Enumeration variant bodies are carried as Int32 (their on-wire built-in type).
        private static BuiltInType NormalizeVariantType(BuiltInType type)
        {
            return type == BuiltInType.Enumeration ? BuiltInType.Int32 : type;
        }

        private void WriteScalarVariant(in Variant value, BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    WriteBoolean(null, value.GetBoolean());
                    break;
                case BuiltInType.SByte:
                    WriteSByte(null, value.GetSByte());
                    break;
                case BuiltInType.Byte:
                    WriteByte(null, value.GetByte());
                    break;
                case BuiltInType.Int16:
                    WriteInt16(null, value.GetInt16());
                    break;
                case BuiltInType.UInt16:
                    WriteUInt16(null, value.GetUInt16());
                    break;
                case BuiltInType.Int32:
                    WriteInt32(null, value.GetInt32());
                    break;
                case BuiltInType.Enumeration:
                    WriteEnumerated(null, value.GetEnumeration());
                    break;
                case BuiltInType.UInt32:
                    WriteUInt32(null, value.GetUInt32());
                    break;
                case BuiltInType.Int64:
                    WriteInt64(null, value.GetInt64());
                    break;
                case BuiltInType.UInt64:
                    WriteUInt64(null, value.GetUInt64());
                    break;
                case BuiltInType.Float:
                    WriteFloat(null, value.GetFloat());
                    break;
                case BuiltInType.Double:
                    WriteDouble(null, value.GetDouble());
                    break;
                case BuiltInType.String:
                    WriteString(null, value.GetString());
                    break;
                case BuiltInType.DateTime:
                    WriteDateTime(null, value.GetDateTime());
                    break;
                case BuiltInType.Guid:
                    WriteGuid(null, value.GetGuid());
                    break;
                case BuiltInType.ByteString:
                    WriteByteString(null, value.GetByteString());
                    break;
                case BuiltInType.XmlElement:
                    WriteXmlElement(null, value.GetXmlElement());
                    break;
                case BuiltInType.NodeId:
                    WriteNodeId(null, value.GetNodeId());
                    break;
                case BuiltInType.ExpandedNodeId:
                    WriteExpandedNodeId(null, value.GetExpandedNodeId());
                    break;
                case BuiltInType.StatusCode:
                    WriteStatusCode(null, value.GetStatusCode());
                    break;
                case BuiltInType.QualifiedName:
                    WriteQualifiedName(null, value.GetQualifiedName());
                    break;
                case BuiltInType.LocalizedText:
                    WriteLocalizedText(null, value.GetLocalizedText());
                    break;
                case BuiltInType.ExtensionObject:
                    WriteExtensionObject(null, value.GetExtensionObject());
                    break;
                case BuiltInType.DataValue:
                    WriteDataValue(null, value.GetDataValue());
                    break;
                default:
                    throw new NotSupportedException($"Variant scalar {type} is not supported by the Avro encoder.");
            }
        }

        private void WriteArrayVariant(in Variant value, BuiltInType type)
        {
            switch (type)
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
                case BuiltInType.Enumeration:
                    WriteEnumeratedArray(null, value.GetEnumerationArray());
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
                case BuiltInType.Variant:
                    WriteVariantArray(null, value.GetVariantArray());
                    break;
                default:
                    throw new NotSupportedException($"Variant array {type} is not supported by the Avro encoder.");
            }
        }

        private void WriteMatrixVariant(in Variant value, BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    WriteMatrix(value.GetBooleanMatrix(), a => WriteBooleanArray(null, a));
                    break;
                case BuiltInType.SByte:
                    WriteMatrix(value.GetSByteMatrix(), a => WriteSByteArray(null, a));
                    break;
                case BuiltInType.Byte:
                    WriteMatrix(value.GetByteMatrix(), a => WriteByteArray(null, a));
                    break;
                case BuiltInType.Int16:
                    WriteMatrix(value.GetInt16Matrix(), a => WriteInt16Array(null, a));
                    break;
                case BuiltInType.UInt16:
                    WriteMatrix(value.GetUInt16Matrix(), a => WriteUInt16Array(null, a));
                    break;
                case BuiltInType.Int32:
                    WriteMatrix(value.GetInt32Matrix(), a => WriteInt32Array(null, a));
                    break;
                case BuiltInType.Enumeration:
                    WriteMatrix(value.GetEnumerationMatrix(), a => WriteEnumeratedArray(null, a));
                    break;
                case BuiltInType.UInt32:
                    WriteMatrix(value.GetUInt32Matrix(), a => WriteUInt32Array(null, a));
                    break;
                case BuiltInType.Int64:
                    WriteMatrix(value.GetInt64Matrix(), a => WriteInt64Array(null, a));
                    break;
                case BuiltInType.UInt64:
                    WriteMatrix(value.GetUInt64Matrix(), a => WriteUInt64Array(null, a));
                    break;
                case BuiltInType.Float:
                    WriteMatrix(value.GetFloatMatrix(), a => WriteFloatArray(null, a));
                    break;
                case BuiltInType.Double:
                    WriteMatrix(value.GetDoubleMatrix(), a => WriteDoubleArray(null, a));
                    break;
                case BuiltInType.String:
                    WriteMatrix(value.GetStringMatrix(), a => WriteStringArray(null, a));
                    break;
                case BuiltInType.DateTime:
                    WriteMatrix(value.GetDateTimeMatrix(), a => WriteDateTimeArray(null, a));
                    break;
                case BuiltInType.Guid:
                    WriteMatrix(value.GetGuidMatrix(), a => WriteGuidArray(null, a));
                    break;
                case BuiltInType.ByteString:
                    WriteMatrix(value.GetByteStringMatrix(), a => WriteByteStringArray(null, a));
                    break;
                case BuiltInType.XmlElement:
                    WriteMatrix(value.GetXmlElementMatrix(), a => WriteXmlElementArray(null, a));
                    break;
                case BuiltInType.NodeId:
                    WriteMatrix(value.GetNodeIdMatrix(), a => WriteNodeIdArray(null, a));
                    break;
                case BuiltInType.ExpandedNodeId:
                    WriteMatrix(value.GetExpandedNodeIdMatrix(), a => WriteExpandedNodeIdArray(null, a));
                    break;
                case BuiltInType.StatusCode:
                    WriteMatrix(value.GetStatusCodeMatrix(), a => WriteStatusCodeArray(null, a));
                    break;
                case BuiltInType.QualifiedName:
                    WriteMatrix(value.GetQualifiedNameMatrix(), a => WriteQualifiedNameArray(null, a));
                    break;
                case BuiltInType.LocalizedText:
                    WriteMatrix(value.GetLocalizedTextMatrix(), a => WriteLocalizedTextArray(null, a));
                    break;
                case BuiltInType.ExtensionObject:
                    WriteMatrix(value.GetExtensionObjectMatrix(), a => WriteExtensionObjectArray(null, a));
                    break;
                case BuiltInType.DataValue:
                    WriteMatrix(value.GetDataValueMatrix(), a => WriteDataValueArray(null, a));
                    break;
                case BuiltInType.Variant:
                    WriteMatrix(value.GetVariantMatrix(), a => WriteVariantArray(null, a));
                    break;
                default:
                    throw new NotSupportedException($"Variant matrix {type} is not supported by the Avro encoder.");
            }
        }
    }
}
