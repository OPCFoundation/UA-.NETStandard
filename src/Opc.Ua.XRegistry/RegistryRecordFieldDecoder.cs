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
    internal enum ProjectedFieldKind
    {
        String,
        Boolean,
        Strings,
        Booleans,
        Structure,
        Structures
    }

    /// <summary>
    /// One projected field value, in the representation that the generated Decode reads.
    /// </summary>
    internal readonly struct ProjectedField
    {
        private ProjectedField(
            ProjectedFieldKind kind,
            string? text = null,
            bool flag = false,
            ArrayOf<string?> texts = default,
            ArrayOf<bool> flags = default,
            IEncodeable? value = null,
            IEncodeable[]? values = null)
        {
            Kind = kind;
            Text = text;
            Flag = flag;
            Texts = texts;
            Flags = flags;
            Value = value;
            Values = values ?? [];
        }

        public ProjectedFieldKind Kind { get; }
        public string? Text { get; }
        public bool Flag { get; }
        public ArrayOf<string?> Texts { get; }
        public ArrayOf<bool> Flags { get; }
        public IEncodeable? Value { get; }
        public IEncodeable[] Values { get; }

        public static ProjectedField FromString(string value)
        {
            return new ProjectedField(ProjectedFieldKind.String, text: value);
        }

        public static ProjectedField FromBoolean(bool value)
        {
            return new ProjectedField(ProjectedFieldKind.Boolean, flag: value);
        }

        public static ProjectedField FromStrings(ArrayOf<string?> values)
        {
            return new ProjectedField(ProjectedFieldKind.Strings, texts: values);
        }

        public static ProjectedField FromBooleans(ArrayOf<bool> values)
        {
            return new ProjectedField(ProjectedFieldKind.Booleans, flags: values);
        }

        public static ProjectedField FromStructure(IEncodeable? value)
        {
            return new ProjectedField(ProjectedFieldKind.Structure, value: value);
        }

        public static ProjectedField FromStructures(IEncodeable[] values)
        {
            return new ProjectedField(ProjectedFieldKind.Structures, values: values);
        }
    }

    /// <summary>
    /// Feeds projected field values to the generated Decode implementation of a native record, so a
    /// record is constructed without reflection or per-field generated setters. The field order is the
    /// published catalog order; any disagreement with the generated layout is an explicit error.
    /// </summary>
    internal sealed class RegistryRecordFieldDecoder : IDecoder
    {
        public RegistryRecordFieldDecoder(
            IServiceMessageContext context,
            string typeName,
            ArrayOf<RegistryNativeFieldDescriptor> fields,
            ProjectedField[] values)
        {
            Context = context;
            m_typeName = typeName;
            m_fields = fields;
            m_values = values;
        }

        public EncodingType EncodingType => EncodingType.Binary;

        public IServiceMessageContext Context { get; }

        public void Complete()
        {
            if (m_next != m_fields.Count)
            {
                throw Layout(null);
            }
        }

        public bool ReadBoolean(string? fieldName)
        {
            return Next(fieldName, ProjectedFieldKind.Boolean).Flag;
        }

        public string? ReadString(string? fieldName)
        {
            return Next(fieldName, ProjectedFieldKind.String).Text;
        }

        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            return Next(fieldName, ProjectedFieldKind.Strings).Texts;
        }

        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            return Next(fieldName, ProjectedFieldKind.Booleans).Flags;
        }

        public T ReadEncodeable<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            return Structure<T>(fieldName, allowNull: false);
        }

        public T ReadEncodeable<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            return Structure<T>(fieldName, allowNull: false);
        }

        public T ReadEncodeableAsExtensionObject<T>(string? fieldName)
            where T : IEncodeable
        {
            return Structure<T>(fieldName, allowNull: true);
        }

        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            return Structures<T>(fieldName);
        }

        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            return Structures<T>(fieldName);
        }

        public ArrayOf<T> ReadEncodeableArrayAsExtensionObjects<T>(string? fieldName)
            where T : IEncodeable
        {
            return Structures<T>(fieldName);
        }

        public bool HasField(string fieldName)
        {
            foreach (RegistryNativeFieldDescriptor field in m_fields)
            {
                if (string.Equals(field.Name, fieldName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public void PushNamespace(string namespaceUri)
        {
        }

        public void PopNamespace()
        {
        }

        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }

        public T DecodeMessage<T>() where T : IEncodeable => throw Unsupported(null);
        public sbyte ReadSByte(string? fieldName) => throw Unsupported(fieldName);
        public byte ReadByte(string? fieldName) => throw Unsupported(fieldName);
        public short ReadInt16(string? fieldName) => throw Unsupported(fieldName);
        public ushort ReadUInt16(string? fieldName) => throw Unsupported(fieldName);
        public int ReadInt32(string? fieldName) => throw Unsupported(fieldName);
        public uint ReadUInt32(string? fieldName) => throw Unsupported(fieldName);
        public long ReadInt64(string? fieldName) => throw Unsupported(fieldName);
        public ulong ReadUInt64(string? fieldName) => throw Unsupported(fieldName);
        public float ReadFloat(string? fieldName) => throw Unsupported(fieldName);
        public double ReadDouble(string? fieldName) => throw Unsupported(fieldName);
        public DateTimeUtc ReadDateTime(string? fieldName) => throw Unsupported(fieldName);
        public Uuid ReadGuid(string? fieldName) => throw Unsupported(fieldName);
        public ByteString ReadByteString(string? fieldName) => throw Unsupported(fieldName);
        public XmlElement ReadXmlElement(string? fieldName) => throw Unsupported(fieldName);
        public NodeId ReadNodeId(string? fieldName) => throw Unsupported(fieldName);
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName) => throw Unsupported(fieldName);
        public StatusCode ReadStatusCode(string? fieldName) => throw Unsupported(fieldName);
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName) => throw Unsupported(fieldName);
        public QualifiedName ReadQualifiedName(string? fieldName) => throw Unsupported(fieldName);
        public LocalizedText ReadLocalizedText(string? fieldName) => throw Unsupported(fieldName);
        public Variant ReadVariant(string? fieldName) => throw Unsupported(fieldName);
        public DataValue ReadDataValue(string? fieldName) => throw Unsupported(fieldName);
        public ExtensionObject ReadExtensionObject(string? fieldName) => throw Unsupported(fieldName);
        public T ReadEnumerated<T>(string? fieldName) where T : struct, Enum => throw Unsupported(fieldName);
        public EnumValue ReadEnumerated(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<byte> ReadByteArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<short> ReadInt16Array(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<int> ReadInt32Array(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<uint> ReadUInt32Array(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<long> ReadInt64Array(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<float> ReadFloatArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<double> ReadDoubleArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<Variant> ReadVariantArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<DataValue> ReadDataValueArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName) => throw Unsupported(fieldName);
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName) => throw Unsupported(fieldName);
        public Variant ReadVariantValue(string? fieldName, TypeInfo typeInfo) => throw Unsupported(fieldName);

        public ArrayOf<T> ReadEnumeratedArray<T>(string? fieldName)
            where T : struct, Enum => throw Unsupported(fieldName);

        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable => throw Unsupported(fieldName);

        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName)
            where T : IEncodeable, new() => throw Unsupported(fieldName);

        public uint ReadSwitchField(IList<string> switches, out string? fieldName) => throw Unsupported(null);
        public uint ReadEncodingMask(IList<string> masks) => throw Unsupported(null);

        private ProjectedField Next(string? fieldName, ProjectedFieldKind kind)
        {
            if (m_next >= m_fields.Count ||
                !string.Equals(m_fields[m_next].Name, fieldName, StringComparison.Ordinal) ||
                m_values[m_next].Kind != kind)
            {
                throw Layout(fieldName);
            }
            return m_values[m_next++];
        }

        private T Structure<T>(string? fieldName, bool allowNull)
            where T : IEncodeable
        {
            IEncodeable? value = Next(fieldName, ProjectedFieldKind.Structure).Value;
            if (value is T typed)
            {
                return typed;
            }
            return value is null && allowNull ? default! : throw Layout(fieldName);
        }

        private ArrayOf<T> Structures<T>(string? fieldName)
            where T : IEncodeable
        {
            IEncodeable[] values = Next(fieldName, ProjectedFieldKind.Structures).Values;
            var result = new T[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                result[index] = values[index] is T typed ? typed : throw Layout(fieldName);
            }
            return result;
        }

        private ServiceResultException Layout(string? fieldName)
        {
            return new ServiceResultException(
                StatusCodes.BadInvalidState,
                $"The generated layout of {m_typeName} disagrees with the published native catalog at field " +
                $"'{fieldName}'.");
        }

        private ServiceResultException Unsupported(string? fieldName)
        {
            return new ServiceResultException(
                StatusCodes.BadNotSupported,
                $"The field '{fieldName}' of {m_typeName} has a DataType that native registry records do not use.");
        }

        private readonly string m_typeName;
        private readonly ArrayOf<RegistryNativeFieldDescriptor> m_fields;
        private readonly ProjectedField[] m_values;
        private int m_next;
    }
}
