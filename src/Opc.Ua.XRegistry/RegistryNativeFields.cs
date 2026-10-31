/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// One encoded native field without expanding a nested Structure or array.
    /// </summary>
    public readonly record struct RegistryNativeField(string Name, Variant Value);

    /// <summary>
    /// Traverses encodeable fields through their real generated Encode implementation.
    /// No reflection, JSON serialization or knowledge of a vendor CLR type is required.
    /// </summary>
    public static class RegistryNativeFields
    {
        /// <summary>
        /// Reads the present fields in encoding order, including inherited fields.
        /// The caller owns the immutable source generation; returned values are native views of it.
        /// </summary>
        public static ArrayOf<RegistryNativeField> Read(
            IEncodeable value,
            IServiceMessageContext context,
            int maxFields = 100000)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (maxFields < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFields));
            }
            using var encoder = new FieldEncoder(context, maxFields);
            value.Encode(encoder);
            return encoder.Fields;
        }

        private sealed class FieldEncoder(IServiceMessageContext context, int maximum) : IEncoder
        {
            public EncodingType EncodingType => EncodingType.Binary;
            public bool CanOmitFields => false;
            public IServiceMessageContext Context => context;
            public ArrayOf<RegistryNativeField> Fields => m_fields.ToArray();

            public void WriteBoolean(string? fieldName, bool value) => Add(fieldName, Variant.From(value));
            public void WriteSByte(string? fieldName, sbyte value) => Add(fieldName, Variant.From(value));
            public void WriteByte(string? fieldName, byte value) => Add(fieldName, Variant.From(value));
            public void WriteInt16(string? fieldName, short value) => Add(fieldName, Variant.From(value));
            public void WriteUInt16(string? fieldName, ushort value) => Add(fieldName, Variant.From(value));
            public void WriteInt32(string? fieldName, int value) => Add(fieldName, Variant.From(value));
            public void WriteUInt32(string? fieldName, uint value) => Add(fieldName, Variant.From(value));
            public void WriteInt64(string? fieldName, long value) => Add(fieldName, Variant.From(value));
            public void WriteUInt64(string? fieldName, ulong value) => Add(fieldName, Variant.From(value));
            public void WriteFloat(string? fieldName, float value) => Add(fieldName, Variant.From(value));
            public void WriteDouble(string? fieldName, double value) => Add(fieldName, Variant.From(value));
            public void WriteString(string? fieldName, string? value) =>
                Add(fieldName, value is null ? Variant.Null : Variant.From(value));
            public void WriteDateTime(string? fieldName, DateTimeUtc value) => Add(fieldName, Variant.From(value));
            public void WriteGuid(string? fieldName, Uuid value) => Add(fieldName, Variant.From(value));
            public void WriteByteString(string? fieldName, ByteString value) => Add(fieldName, Variant.From(value));
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            public void WriteByteString(string? fieldName, ReadOnlySpan<byte> value) =>
                Add(fieldName, Variant.From(ByteString.From(value.ToArray())));
#endif
            public void WriteXmlElement(string? fieldName, XmlElement value) => Add(fieldName, Variant.From(value));
            public void WriteNodeId(string? fieldName, NodeId value) => Add(fieldName, Variant.From(value));
            public void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value) =>
                Add(fieldName, Variant.From(value));
            public void WriteStatusCode(string? fieldName, StatusCode value) => Add(fieldName, Variant.From(value));
            public void WriteQualifiedName(string? fieldName, QualifiedName value) =>
                Add(fieldName, Variant.From(value));
            public void WriteLocalizedText(string? fieldName, LocalizedText value) =>
                Add(fieldName, Variant.From(value));
            public void WriteVariant(string? fieldName, in Variant value) => Add(fieldName, value);
            public void WriteVariantValue(string? fieldName, in Variant value) => Add(fieldName, value);
            public void WriteDataValue(string? fieldName, in DataValue value) => Add(fieldName, Variant.From(value));
            public void WriteExtensionObject(string? fieldName, ExtensionObject value) =>
                Add(fieldName, Variant.From(value));

            public void WriteEncodeable<T>(string? fieldName, T value) where T : IEncodeable, new() =>
                Add(fieldName, Variant.FromStructure(value));
            public void WriteEncodeable<T>(string? fieldName, T value, ExpandedNodeId encodeableTypeId)
                where T : IEncodeable => Add(fieldName, Variant.FromStructure(value));
            public void WriteEncodeableAsExtensionObject<T>(string? fieldName, T value) where T : IEncodeable =>
                Add(fieldName, Variant.FromStructure(value));
            public void WriteEnumerated<T>(string? fieldName, T value) where T : struct, Enum =>
                Add(fieldName, Variant.From(EnumValue.From(value)));
            public void WriteEnumerated(string? fieldName, EnumValue value) => Add(fieldName, Variant.From(value));

            public void WriteBooleanArray(string? fieldName, ArrayOf<bool> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteSByteArray(string? fieldName, ArrayOf<sbyte> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteByteArray(string? fieldName, ArrayOf<byte> values) => Add(fieldName, Variant.From(values));
            public void WriteInt16Array(string? fieldName, ArrayOf<short> values) => Add(fieldName, Variant.From(values));
            public void WriteUInt16Array(string? fieldName, ArrayOf<ushort> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteInt32Array(string? fieldName, ArrayOf<int> values) => Add(fieldName, Variant.From(values));
            public void WriteUInt32Array(string? fieldName, ArrayOf<uint> values) => Add(fieldName, Variant.From(values));
            public void WriteInt64Array(string? fieldName, ArrayOf<long> values) => Add(fieldName, Variant.From(values));
            public void WriteUInt64Array(string? fieldName, ArrayOf<ulong> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteFloatArray(string? fieldName, ArrayOf<float> values) => Add(fieldName, Variant.From(values));
            public void WriteDoubleArray(string? fieldName, ArrayOf<double> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteStringArray(string? fieldName, ArrayOf<string> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteDateTimeArray(string? fieldName, ArrayOf<DateTimeUtc> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteGuidArray(string? fieldName, ArrayOf<Uuid> values) => Add(fieldName, Variant.From(values));
            public void WriteByteStringArray(string? fieldName, ArrayOf<ByteString> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteXmlElementArray(string? fieldName, ArrayOf<XmlElement> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteNodeIdArray(string? fieldName, ArrayOf<NodeId> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteExpandedNodeIdArray(string? fieldName, ArrayOf<ExpandedNodeId> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteStatusCodeArray(string? fieldName, ArrayOf<StatusCode> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteQualifiedNameArray(string? fieldName, ArrayOf<QualifiedName> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteLocalizedTextArray(string? fieldName, ArrayOf<LocalizedText> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteVariantArray(string? fieldName, ArrayOf<Variant> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteDataValueArray(string? fieldName, ArrayOf<DataValue> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteExtensionObjectArray(string? fieldName, ArrayOf<ExtensionObject> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteEnumeratedArray(string? fieldName, ArrayOf<EnumValue> values) =>
                Add(fieldName, Variant.From(values));
            public void WriteEnumeratedArray<T>(string? fieldName, ArrayOf<T> values) where T : struct, Enum =>
                Add(fieldName, Variant.From(EnumValue.From(values)));
            public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values) where T : IEncodeable, new() =>
                Add(fieldName, Variant.FromStructure(values));
            public void WriteEncodeableArray<T>(
                string? fieldName, ArrayOf<T> values, ExpandedNodeId encodeableTypeId) where T : IEncodeable =>
                Add(fieldName, Variant.FromStructure(values));
            public void WriteEncodeableArrayAsExtensionObjects<T>(string? fieldName, ArrayOf<T> values)
                where T : IEncodeable => Add(fieldName, Variant.FromStructure(values));
            public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values) where T : IEncodeable, new() =>
                Add(fieldName, Variant.FromStructure(values));
            public void WriteEncodeableMatrix<T>(
                string? fieldName, MatrixOf<T> values, ExpandedNodeId encodeableTypeId) where T : IEncodeable =>
                Add(fieldName, Variant.FromStructure(values));

            public void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value) =>
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "DiagnosticInfo is a service diagnostic, not a Variant-valued registry document field.");
            public void WriteDiagnosticInfoArray(string? fieldName, ArrayOf<DiagnosticInfo> values) =>
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "DiagnosticInfo arrays are not registry document fields.");
            public int Close() => throw new NotSupportedException("Native field traversal is not a byte stream.");
            public string? CloseAndReturnText() =>
                throw new NotSupportedException("Native field traversal does not produce text.");
            public void EncodeMessage<T>(T message) where T : IEncodeable, new() => message.Encode(this);
            public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId) where T : IEncodeable =>
                message.Encode(this);
            public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
            {
            }
            public void PushNamespace(string namespaceUri)
            {
            }
            public void PopNamespace()
            {
            }
            public void WriteSwitchField(uint switchField, out string? fieldName)
            {
                fieldName = null;
            }
            public void WriteEncodingMask(uint encodingMask)
            {
            }
            public void Dispose()
            {
            }

            private void Add(string? name, in Variant value)
            {
                if (name is null || !m_names.Add(name))
                {
                    throw new ArgumentException("A native document has unnamed or duplicate fields.", nameof(name));
                }
                if (m_fields.Count == maximum)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                m_fields.Add(new RegistryNativeField(name, value));
            }

            private readonly List<RegistryNativeField> m_fields = [];
            private readonly HashSet<string> m_names = new(StringComparer.Ordinal);
        }
    }
}
