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
using System.Xml;
using Opc.Ua.Schema.Binary;

namespace Opc.Ua.Schema.Bsd
{
    /// <summary>
    /// Generates OPC Binary schema (BSD) documents for OPC UA data types
    /// according to the OPC UA Part 6 binary encoding. The schema is built using
    /// the existing <see cref="Opc.Ua.Schema.Binary"/> object model and is
    /// serialized with a direct XML writer to remain trimming and NativeAOT
    /// compatible.
    /// </summary>
    internal sealed class BsdSchemaGenerator : IUaSchemaGenerator
    {
        /// <inheritdoc/>
        public bool CanGenerate(UaSchemaFormat format)
        {
            return format == UaSchemaFormat.Bsd;
        }

        /// <inheritdoc/>
        public IUaSchema Generate(
            UaTypeDescription type,
            IDataTypeDefinitionResolver resolver,
            UaSchemaFormat format,
            UaSchemaScope scope)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            var context = new GenerationContext(type.NamespaceUri, resolver);
            if (scope == UaSchemaScope.Namespace)
            {
                foreach (UaTypeDescription namespaceType in resolver.GetNamespaceTypes(type.NamespaceUri))
                {
                    context.EnsureType(namespaceType);
                }
            }

            context.EnsureType(type);
            return new BinarySchemaDocument(type.NamespaceUri, context.Dictionary);
        }

        private sealed class GenerationContext
        {
            public GenerationContext(string targetNamespace, IDataTypeDefinitionResolver resolver)
            {
                m_resolver = resolver;
                m_targetNamespace = targetNamespace;
                m_items = [];
                m_emittedTypes = new HashSet<string>(StringComparer.Ordinal);
                m_visitingTypes = new HashSet<string>(StringComparer.Ordinal);
                m_importedNamespaces = new HashSet<string>(StringComparer.Ordinal);

                Dictionary = new TypeDictionary
                {
                    TargetNamespace = targetNamespace,
                    DefaultByteOrder = ByteOrder.LittleEndian,
                    DefaultByteOrderSpecified = true,
                    Import =
                    [
                        new ImportDirective { Namespace = UaTypesNamespace }
                    ]
                };
            }

            public TypeDictionary Dictionary { get; }

            public void EnsureType(UaTypeDescription type)
            {
                string typeKey = TypeKey(type);
                if (m_emittedTypes.Contains(typeKey) || m_visitingTypes.Contains(typeKey))
                {
                    return;
                }

                m_visitingTypes.Add(typeKey);
                TypeDescription? description = type.Definition switch
                {
                    StructureDefinition structure => BuildStructure(type, structure),
                    EnumDefinition enumeration => BuildEnum(type, enumeration),
                    _ => null
                };
                m_visitingTypes.Remove(typeKey);

                if (description != null)
                {
                    m_items.Add(description);
                    Dictionary.Items = [.. m_items];
                    m_emittedTypes.Add(typeKey);
                }
            }

            private StructuredType BuildStructure(UaTypeDescription type, StructureDefinition structure)
            {
                bool isUnion = structure.StructureType
                    is StructureType.Union or StructureType.UnionWithSubtypedValues;
                var fields = new List<FieldType>();
                ArrayOf<StructureField> structureFields = structure.Fields;

                if (isUnion)
                {
                    fields.Add(new FieldType
                    {
                        Name = "SwitchField",
                        TypeName = Opc("UInt32")
                    });
                }
                else
                {
                    AddOptionalEncodingMask(fields, structureFields);
                }

                for (int i = 0; i < structureFields.Count; i++)
                {
                    AddField(fields, structureFields[i], i, isUnion);
                }

                return new StructuredType
                {
                    Name = type.Name,
                    Field = [.. fields]
                };
            }

            private static void AddOptionalEncodingMask(
                List<FieldType> fields,
                ArrayOf<StructureField> structureFields)
            {
                int optionalCount = 0;
                for (int i = 0; i < structureFields.Count; i++)
                {
                    if (structureFields[i].IsOptional)
                    {
                        optionalCount++;
                    }
                }
                if (optionalCount == 0)
                {
                    return;
                }

                // The binary encoding prefixes optional-field structures with a
                // 32-bit EncodingMask: one presence bit per optional field (in
                // field order) followed by a reserved bit-field that pads the
                // mask to 32 bits. The optional data fields reference their
                // presence bit through SwitchField.
                for (int i = 0; i < structureFields.Count; i++)
                {
                    StructureField field = structureFields[i];
                    if (field.IsOptional)
                    {
                        fields.Add(new FieldType
                        {
                            Name = FieldName(field, i) + "Specified",
                            TypeName = Opc("Bit")
                        });
                    }
                }

                int reservedBits = EncodingMaskBits - optionalCount;
                if (reservedBits > 0)
                {
                    fields.Add(new FieldType
                    {
                        Name = "Reserved1",
                        TypeName = Opc("Bit"),
                        Length = (uint)reservedBits,
                        LengthSpecified = true
                    });
                }
            }

            private EnumeratedType BuildEnum(UaTypeDescription type, EnumDefinition enumeration)
            {
                ArrayOf<EnumField> fields = enumeration.Fields;
                var values = new EnumeratedValue[fields.Count];
                for (int i = 0; i < fields.Count; i++)
                {
                    EnumField field = fields[i];
                    values[i] = new EnumeratedValue
                    {
                        Name = EnumName(field, i),
                        Value = checked((int)field.Value),
                        ValueSpecified = true
                    };
                }

                return new EnumeratedType
                {
                    Name = type.Name,
                    LengthInBits = 32,
                    LengthInBitsSpecified = true,
                    EnumeratedValue = values
                };
            }

            private void AddField(List<FieldType> fields, StructureField field, int index, bool isUnion)
            {
                string name = FieldName(field, index);
                XmlQualifiedName typeName = ResolveType(field.DataType);
                string? switchField = null;
                uint switchValue = 0;
                bool switchValueSpecified = false;

                if (isUnion)
                {
                    switchField = "SwitchField";
                    switchValue = checked((uint)(index + 1));
                    switchValueSpecified = true;
                }
                else if (field.IsOptional)
                {
                    switchField = name + "Specified";
                }

                if (field.ValueRank == ValueRanks.Scalar)
                {
                    fields.Add(new FieldType
                    {
                        Name = name,
                        TypeName = typeName,
                        SwitchField = switchField,
                        SwitchValue = switchValue,
                        SwitchValueSpecified = switchValueSpecified
                    });
                    return;
                }

                string lengthField = "NoOf" + name;
                fields.Add(new FieldType
                {
                    Name = lengthField,
                    TypeName = Opc("Int32"),
                    SwitchField = switchField,
                    SwitchValue = switchValue,
                    SwitchValueSpecified = switchValueSpecified
                });
                fields.Add(new FieldType
                {
                    Name = name,
                    TypeName = typeName,
                    LengthField = lengthField,
                    SwitchField = switchField,
                    SwitchValue = switchValue,
                    SwitchValueSpecified = switchValueSpecified
                });
            }

            private XmlQualifiedName ResolveType(NodeId dataType)
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType);
                if (builtInType != BuiltInType.Null)
                {
                    return BuiltInTypeName(builtInType);
                }

                if (m_resolver.TryResolve(dataType, out UaTypeDescription? referenced))
                {
                    if (string.Equals(referenced.NamespaceUri, m_targetNamespace, StringComparison.Ordinal))
                    {
                        EnsureType(referenced);
                        return Tns(referenced.Name);
                    }

                    AddNamespaceImport(referenced.NamespaceUri);
                    return new XmlQualifiedName(referenced.Name, referenced.NamespaceUri);
                }

                return Ua("ExtensionObject");
            }

            private XmlQualifiedName Tns(string name)
            {
                return new XmlQualifiedName(name, m_targetNamespace);
            }

            private static XmlQualifiedName BuiltInTypeName(BuiltInType builtInType)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        return Opc("Boolean");
                    case BuiltInType.SByte:
                        return Opc("SByte");
                    case BuiltInType.Byte:
                        return Opc("Byte");
                    case BuiltInType.Int16:
                        return Opc("Int16");
                    case BuiltInType.UInt16:
                        return Opc("UInt16");
                    case BuiltInType.Int32:
                    case BuiltInType.Enumeration:
                        return Opc("Int32");
                    case BuiltInType.UInt32:
                        return Opc("UInt32");
                    case BuiltInType.Int64:
                        return Opc("Int64");
                    case BuiltInType.UInt64:
                        return Opc("UInt64");
                    case BuiltInType.Float:
                        return Opc("Float");
                    case BuiltInType.Double:
                        return Opc("Double");
                    case BuiltInType.String:
                        return Opc("CharArray");
                    case BuiltInType.DateTime:
                        return Opc("DateTime");
                    case BuiltInType.Guid:
                        return Opc("Guid");
                    case BuiltInType.ByteString:
                        return Opc("ByteString");
                    case BuiltInType.XmlElement:
                        return Ua("XmlElement");
                    case BuiltInType.NodeId:
                        return Ua("NodeId");
                    case BuiltInType.ExpandedNodeId:
                        return Ua("ExpandedNodeId");
                    case BuiltInType.StatusCode:
                        return Ua("StatusCode");
                    case BuiltInType.QualifiedName:
                        return Ua("QualifiedName");
                    case BuiltInType.LocalizedText:
                        return Ua("LocalizedText");
                    case BuiltInType.ExtensionObject:
                        return Ua("ExtensionObject");
                    case BuiltInType.DataValue:
                        return Ua("DataValue");
                    case BuiltInType.Variant:
                        return Ua("Variant");
                    case BuiltInType.DiagnosticInfo:
                        return Ua("DiagnosticInfo");
                    default:
                        return Ua(builtInType.ToString());
                }
            }

            private static XmlQualifiedName Opc(string name)
            {
                return new XmlQualifiedName(name, OpcBinaryNamespace);
            }

            private static XmlQualifiedName Ua(string name)
            {
                return new XmlQualifiedName(name, UaTypesNamespace);
            }

            private static string FieldName(StructureField field, int index)
            {
                return string.IsNullOrEmpty(field.Name) ? "Field" + index : field.Name!;
            }

            private static string EnumName(EnumField field, int index)
            {
                return string.IsNullOrEmpty(field.Name) ? "Value" + index : field.Name!;
            }

            private void AddNamespaceImport(string namespaceUri)
            {
                if (string.IsNullOrEmpty(namespaceUri) || m_importedNamespaces.Contains(namespaceUri))
                {
                    return;
                }

                m_importedNamespaces.Add(namespaceUri);
                ImportDirective[] imports = Dictionary.Import ?? [];
                Dictionary.Import = [.. imports, new ImportDirective { Namespace = namespaceUri }];
            }

            private static string TypeKey(UaTypeDescription type)
            {
                return type.NamespaceUri + "|" + type.Name;
            }

            private const string OpcBinaryNamespace = "http://opcfoundation.org/BinarySchema/";
            private const string UaTypesNamespace = "http://opcfoundation.org/UA/";
            private const int EncodingMaskBits = 32;

            private readonly IDataTypeDefinitionResolver m_resolver;
            private readonly string m_targetNamespace;
            private readonly List<TypeDescription> m_items;
            private readonly HashSet<string> m_emittedTypes;
            private readonly HashSet<string> m_visitingTypes;
            private readonly HashSet<string> m_importedNamespaces;
        }
    }
}
