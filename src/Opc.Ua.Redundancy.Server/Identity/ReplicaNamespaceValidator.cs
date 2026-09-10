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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Visits namespace-bearing fields through the ordinary encodeable contract without reflection or serialization.
    /// Primitive fields have no namespace identity and are intentionally not written.
    /// </summary>
    internal sealed class ReplicaNamespaceValidator : IEncoder
    {
        internal ReplicaNamespaceValidator(IServiceMessageContext context, ArrayOf<string> sharedNamespaces)
        {
            Context = context;
            m_sharedNamespaces = sharedNamespaces;
        }

        public EncodingType EncodingType => EncodingType.Binary;

        public bool CanOmitFields => false;

        public IServiceMessageContext Context { get; }

        internal void ValidateNode(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance)
            {
                WriteNodeId(null, instance.TypeDefinitionId);
                WriteNodeId(null, instance.ReferenceTypeId);
                WriteNodeId(null, instance.ModellingRuleId);
            }
            if (node is BaseTypeState type)
            {
                WriteNodeId(null, type.SuperTypeId);
            }
            if (node is BaseVariableState variable)
            {
                WriteNodeId(null, variable.DataType);
                WriteVariant(null, variable.Value);
            }
            if (node is BaseVariableTypeState variableType)
            {
                WriteNodeId(null, variableType.DataType);
                WriteVariant(null, variableType.Value);
            }
            if (node is DataTypeState dataType)
            {
                WriteExtensionObject(null, dataType.DataTypeDefinition);
            }
            foreach (RolePermissionType permission in node.RolePermissions)
            {
                WriteNodeId(null, permission.RoleId);
            }
            foreach (RolePermissionType permission in node.UserRolePermissions)
            {
                WriteNodeId(null, permission.RoleId);
            }
            var references = new List<IReference>();
            node.GetReferences(context, references);
            foreach (IReference reference in references)
            {
                WriteNodeId(null, reference.ReferenceTypeId);
                WriteExpandedNodeId(null, reference.TargetId);
            }
        }

        public void WriteNodeId(string? fieldName, NodeId value)
        {
            ValidateIndex(value.NamespaceIndex);
        }

        public void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value)
        {
            if (value.ServerIndex != 0)
            {
                if (string.IsNullOrEmpty(Context.ServerUris.GetString(value.ServerIndex)))
                {
                    throw InvalidNamespace("An external reference uses an unregistered server URI index.");
                }
                return;
            }
            string? uri = value.NamespaceUri;
            if (string.IsNullOrEmpty(uri))
            {
                ValidateIndex(value.NamespaceIndex);
                return;
            }
            if (uri == Namespaces.OpcUa)
            {
                return;
            }
            for (int i = 0; i < m_sharedNamespaces.Count; i++)
            {
                if (m_sharedNamespaces[i] == uri)
                {
                    return;
                }
            }
            throw InvalidNamespace($"Shared metadata references undeclared namespace '{uri}'.");
        }

        public void WriteQualifiedName(string? fieldName, QualifiedName value)
        {
            ValidateIndex(value.NamespaceIndex);
        }

        public void WriteVariant(string? fieldName, in Variant value)
        {
            if (value.IsNull)
            {
                return;
            }
            Enter();
            try
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.NodeId:
                        VisitValues<NodeId, VariantBuilder>(value, id => WriteNodeId(null, id));
                        break;
                    case BuiltInType.ExpandedNodeId:
                        VisitValues<ExpandedNodeId, VariantBuilder>(value, id => WriteExpandedNodeId(null, id));
                        break;
                    case BuiltInType.QualifiedName:
                        VisitValues<QualifiedName, VariantBuilder>(value, name => WriteQualifiedName(null, name));
                        break;
                    case BuiltInType.ExtensionObject:
                        VisitValues<ExtensionObject, VariantBuilder>(value, body => WriteExtensionObject(null, body));
                        break;
                    case BuiltInType.DataValue:
                        VisitValues<DataValue, VariantBuilder>(value, data => WriteDataValue(null, data));
                        break;
                    case BuiltInType.Variant:
                        if (value.TryGetValue(out ArrayOf<Variant> array))
                        {
                            WriteVariantArray(null, array);
                        }
                        else if (value.TryGetValue(out MatrixOf<Variant> matrix))
                        {
                            WriteVariantArray(null, matrix.ToArrayOf());
                        }
                        else
                        {
                            throw InvalidNamespace("The nested shared Variant has an unsupported value shape.");
                        }
                        break;
                }
            }
            finally
            {
                m_depth--;
            }
        }

        public void WriteVariantValue(string? fieldName, in Variant value)
        {
            WriteVariant(fieldName, value);
        }

        public void WriteDataValue(string? fieldName, in DataValue value)
        {
            WriteVariant(fieldName, value.WrappedValue);
        }

        public void WriteExtensionObject(string? fieldName, ExtensionObject value)
        {
            if (value.IsNull)
            {
                return;
            }
            WriteExpandedNodeId(fieldName, value.TypeId);
            if (value.TryGetValue(out IEncodeable? body, Context))
            {
                VisitEncodeable(body);
            }
            else if (value.Encoding != ExtensionObjectEncoding.None)
            {
                throw InvalidNamespace(
                    "Register the structured value's codec before sharing opaque ExtensionObject data between replicas.");
            }
        }

        public void EncodeMessage<T>(T message) where T : IEncodeable, new()
        {
            VisitEncodeable(message);
        }

        public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            WriteEncodeable(null, message, encodeableTypeId);
        }

        public void WriteEncodeable<T>(string? fieldName, T value) where T : IEncodeable, new()
        {
            VisitEncodeable(value);
        }

        public void WriteEncodeable<T>(string? fieldName, T value, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteExpandedNodeId(fieldName, encodeableTypeId);
            VisitEncodeable(value);
        }

        public void WriteEncodeableAsExtensionObject<T>(string? fieldName, T value) where T : IEncodeable
        {
            VisitEncodeable(value);
        }

        public void WriteNodeIdArray(string? fieldName, ArrayOf<NodeId> values)
        {
            foreach (NodeId value in values)
            {
                WriteNodeId(fieldName, value);
            }
        }

        public void WriteExpandedNodeIdArray(string? fieldName, ArrayOf<ExpandedNodeId> values)
        {
            foreach (ExpandedNodeId value in values)
            {
                WriteExpandedNodeId(fieldName, value);
            }
        }

        public void WriteQualifiedNameArray(string? fieldName, ArrayOf<QualifiedName> values)
        {
            foreach (QualifiedName value in values)
            {
                WriteQualifiedName(fieldName, value);
            }
        }

        public void WriteVariantArray(string? fieldName, ArrayOf<Variant> values)
        {
            foreach (Variant value in values)
            {
                WriteVariant(fieldName, value);
            }
        }

        public void WriteDataValueArray(string? fieldName, ArrayOf<DataValue> values)
        {
            foreach (DataValue value in values)
            {
                WriteDataValue(fieldName, value);
            }
        }

        public void WriteExtensionObjectArray(string? fieldName, ArrayOf<ExtensionObject> values)
        {
            foreach (ExtensionObject value in values)
            {
                WriteExtensionObject(fieldName, value);
            }
        }

        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values) where T : IEncodeable, new()
        {
            WriteEncodeableArrayAsExtensionObjects(fieldName, values);
        }

        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteExpandedNodeId(fieldName, encodeableTypeId);
            WriteEncodeableArrayAsExtensionObjects(fieldName, values);
        }

        public void WriteEncodeableArrayAsExtensionObjects<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            foreach (T value in values)
            {
                VisitEncodeable(value);
            }
        }

        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values) where T : IEncodeable, new()
        {
            WriteEncodeableArrayAsExtensionObjects(fieldName, values.ToArrayOf());
        }

        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteEncodeableArray(fieldName, values.ToArrayOf(), encodeableTypeId);
        }

        public void Dispose()
        {
        }

        public int Close()
        {
            return 0;
        }

        public string? CloseAndReturnText()
        {
            return null;
        }

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
        public void WriteBoolean(string? fieldName, bool value)
        {
        }
        public void WriteSByte(string? fieldName, sbyte value)
        {
        }
        public void WriteByte(string? fieldName, byte value)
        {
        }
        public void WriteInt16(string? fieldName, short value)
        {
        }
        public void WriteUInt16(string? fieldName, ushort value)
        {
        }
        public void WriteInt32(string? fieldName, int value)
        {
        }
        public void WriteUInt32(string? fieldName, uint value)
        {
        }
        public void WriteInt64(string? fieldName, long value)
        {
        }
        public void WriteUInt64(string? fieldName, ulong value)
        {
        }
        public void WriteFloat(string? fieldName, float value)
        {
        }
        public void WriteDouble(string? fieldName, double value)
        {
        }
        public void WriteString(string? fieldName, string? value)
        {
        }
        public void WriteDateTime(string? fieldName, DateTimeUtc value)
        {
        }
        public void WriteGuid(string? fieldName, Uuid value)
        {
        }
        public void WriteByteString(string? fieldName, ByteString value)
        {
        }
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        public void WriteByteString(string? fieldName, ReadOnlySpan<byte> value)
        {
        }
#endif
        public void WriteXmlElement(string? fieldName, XmlElement value)
        {
        }
        public void WriteStatusCode(string? fieldName, StatusCode value)
        {
        }
        public void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value)
        {
        }
        public void WriteLocalizedText(string? fieldName, LocalizedText value)
        {
        }
        public void WriteEnumerated<T>(string? fieldName, T value) where T : struct, Enum
        {
        }
        public void WriteEnumerated(string? fieldName, EnumValue value)
        {
        }
        public void WriteBooleanArray(string? fieldName, ArrayOf<bool> values)
        {
        }
        public void WriteSByteArray(string? fieldName, ArrayOf<sbyte> values)
        {
        }
        public void WriteByteArray(string? fieldName, ArrayOf<byte> values)
        {
        }
        public void WriteInt16Array(string? fieldName, ArrayOf<short> values)
        {
        }
        public void WriteUInt16Array(string? fieldName, ArrayOf<ushort> values)
        {
        }
        public void WriteInt32Array(string? fieldName, ArrayOf<int> values)
        {
        }
        public void WriteUInt32Array(string? fieldName, ArrayOf<uint> values)
        {
        }
        public void WriteInt64Array(string? fieldName, ArrayOf<long> values)
        {
        }
        public void WriteUInt64Array(string? fieldName, ArrayOf<ulong> values)
        {
        }
        public void WriteFloatArray(string? fieldName, ArrayOf<float> values)
        {
        }
        public void WriteDoubleArray(string? fieldName, ArrayOf<double> values)
        {
        }
        public void WriteStringArray(string? fieldName, ArrayOf<string> values)
        {
        }
        public void WriteDateTimeArray(string? fieldName, ArrayOf<DateTimeUtc> values)
        {
        }
        public void WriteGuidArray(string? fieldName, ArrayOf<Uuid> values)
        {
        }
        public void WriteByteStringArray(string? fieldName, ArrayOf<ByteString> values)
        {
        }
        public void WriteXmlElementArray(string? fieldName, ArrayOf<XmlElement> values)
        {
        }
        public void WriteStatusCodeArray(string? fieldName, ArrayOf<StatusCode> values)
        {
        }
        public void WriteDiagnosticInfoArray(string? fieldName, ArrayOf<DiagnosticInfo> values)
        {
        }
        public void WriteLocalizedTextArray(string? fieldName, ArrayOf<LocalizedText> values)
        {
        }
        public void WriteEnumeratedArray<T>(string? fieldName, ArrayOf<T> values) where T : struct, Enum
        {
        }
        public void WriteEnumeratedArray(string? fieldName, ArrayOf<EnumValue> values)
        {
        }

        private void ValidateIndex(ushort index)
        {
            if (index == 0)
            {
                return;
            }
            if (index < 2 ||
                index >= m_sharedNamespaces.Count + 2 ||
                Context.NamespaceUris.GetString(index) != m_sharedNamespaces[index - 2])
            {
                throw InvalidNamespace($"Shared metadata or data references local or undeclared namespace index {index}.");
            }
        }

        private void VisitEncodeable<T>(T value) where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(value, default(T)))
            {
                return;
            }
            WriteExpandedNodeId(null, value.TypeId);
            WriteExpandedNodeId(null, value.BinaryEncodingId);
            Enter();
            try
            {
                value.Encode(this);
            }
            finally
            {
                m_depth--;
            }
        }

        private static void VisitValues<T, TBuilder>(in Variant value, Action<T> visitor)
            where TBuilder : struct, IVariantBuilder<T>, IVariantBuilder<ArrayOf<T>>, IVariantBuilder<MatrixOf<T>>
        {
            TBuilder builder = default;
            if (value.TypeInfo.IsScalar)
            {
                visitor(Get<T, TBuilder>(builder, value));
            }
            else if (value.TypeInfo.ValueRank == ValueRanks.OneDimension)
            {
                foreach (T item in Get<ArrayOf<T>, TBuilder>(builder, value))
                {
                    visitor(item);
                }
            }
            else
            {
                foreach (T item in Get<MatrixOf<T>, TBuilder>(builder, value))
                {
                    visitor(item);
                }
            }
        }

        private static T Get<T, TBuilder>(TBuilder builder, Variant value)
            where TBuilder : struct, IVariantBuilder<T>
        {
            return builder.GetValue(value);
        }

        private void Enter()
        {
            if (m_depth >= Context.MaxEncodingNestingLevels)
            {
                throw InvalidNamespace("Shared value namespace validation exceeded the configured nesting limit.");
            }
            m_depth++;
        }

        private static ServiceResultException InvalidNamespace(string message)
        {
            return new ServiceResultException(StatusCodes.BadConfigurationError, message);
        }

        private readonly ArrayOf<string> m_sharedNamespaces;
        private int m_depth;
    }
}
