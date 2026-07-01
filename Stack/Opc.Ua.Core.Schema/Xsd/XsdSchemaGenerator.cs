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
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Opc.Ua.Schema.Xsd
{
    /// <summary>
    /// Generates XML Schema (XSD) documents for OPC UA data types according to
    /// the OPC UA Part 6 XML encoding. The schema is built using the in-box
    /// <see cref="XmlSchema"/> object model so that no
    /// reflection-based serialization is required.
    /// </summary>
    internal sealed class XsdSchemaGenerator : IUaSchemaGenerator
    {
        /// <inheritdoc/>
        public bool CanGenerate(UaSchemaFormat format)
        {
            return format == UaSchemaFormat.Xsd;
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
            return new XmlSchemaDocument(type.NamespaceUri, context.Schema);
        }

        private sealed class GenerationContext
        {
            public GenerationContext(string targetNamespace, IDataTypeDefinitionResolver resolver)
            {
                m_resolver = resolver;
                m_targetNamespace = targetNamespace;
                m_emittedTypes = new HashSet<string>(StringComparer.Ordinal);
                m_visitingTypes = new HashSet<string>(StringComparer.Ordinal);
                m_emittedListTypes = new HashSet<string>(StringComparer.Ordinal);
                m_importedNamespaces = new HashSet<string>(StringComparer.Ordinal);
                m_nextNamespacePrefix = 1;

                Schema = new XmlSchema
                {
                    TargetNamespace = targetNamespace,
                    ElementFormDefault = XmlSchemaForm.Qualified,
                    Namespaces = new XmlSerializerNamespaces()
                };
                Schema.Namespaces.Add("xs", XmlSchema.Namespace);
                Schema.Namespaces.Add("ua", UaTypesNamespace);
                Schema.Namespaces.Add("tns", targetNamespace);
                Schema.Includes.Add(new XmlSchemaImport { Namespace = UaTypesNamespace });
            }

            public XmlSchema Schema { get; }

            public void EnsureType(UaTypeDescription type)
            {
                string typeKey = TypeKey(type);
                if (m_emittedTypes.Contains(typeKey) || m_visitingTypes.Contains(typeKey))
                {
                    return;
                }

                m_visitingTypes.Add(typeKey);
                switch (type.Definition)
                {
                    case StructureDefinition structure:
                        AddStructure(type, structure);
                        break;
                    case EnumDefinition enumeration:
                        AddEnum(type, enumeration);
                        break;
                }
                m_visitingTypes.Remove(typeKey);
                m_emittedTypes.Add(typeKey);
            }

            private void AddStructure(UaTypeDescription type, StructureDefinition structure)
            {
                bool isUnion = structure.StructureType
                    is StructureType.Union or StructureType.UnionWithSubtypedValues;
                var complexType = new XmlSchemaComplexType { Name = type.Name };

                if (isUnion)
                {
                    var sequence = new XmlSchemaSequence();
                    sequence.Items.Add(new XmlSchemaElement
                    {
                        Name = "SwitchField",
                        SchemaTypeName = Xs("unsignedInt"),
                        MinOccurs = 0
                    });

                    var choice = new XmlSchemaChoice();
                    AddStructureFields(choice.Items, structure.Fields, forceOptional: true);
                    sequence.Items.Add(choice);
                    complexType.Particle = sequence;
                }
                else
                {
                    var sequence = new XmlSchemaSequence();
                    AddStructureFields(sequence.Items, structure.Fields, forceOptional: false);
                    complexType.Particle = sequence;
                }

                Schema.Items.Add(complexType);
                AddElement(type.Name, Tns(type.Name), isNillable: false);
                AddListType(type.Name, Tns(type.Name), isNillable: true);
            }

            private void AddEnum(UaTypeDescription type, EnumDefinition enumeration)
            {
                var simpleType = new XmlSchemaSimpleType { Name = type.Name };
                var restriction = new XmlSchemaSimpleTypeRestriction
                {
                    BaseTypeName = enumeration.IsOptionSet ? Xs("int") : Xs("string")
                };
                ArrayOf<EnumField> fields = enumeration.Fields;
                for (int i = 0; i < fields.Count; i++)
                {
                    EnumField field = fields[i];
                    restriction.Facets.Add(new XmlSchemaEnumerationFacet
                    {
                        Value = enumeration.IsOptionSet ? XmlConvert.ToString(field.Value) : EnumValue(field, i)
                    });
                }

                simpleType.Content = restriction;
                Schema.Items.Add(simpleType);
                AddElement(type.Name, Tns(type.Name), isNillable: false);
                AddListType(type.Name, Tns(type.Name), isNillable: false);
            }

            private void AddStructureFields(
                XmlSchemaObjectCollection items,
                ArrayOf<StructureField> fields,
                bool forceOptional)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    StructureField field = fields[i];
                    items.Add(BuildFieldElement(field, i, forceOptional));
                }
            }

            private XmlSchemaElement BuildFieldElement(StructureField field, int index, bool forceOptional)
            {
                var element = new XmlSchemaElement
                {
                    Name = FieldName(field, index),
                    MinOccurs = field.IsOptional || forceOptional ? 0 : 1
                };

                if (field.ValueRank == ValueRanks.Scalar)
                {
                    TypeReference typeReference = ResolveType(field.DataType);
                    element.SchemaTypeName = typeReference.Name;
                    element.IsNillable = typeReference.IsNillable;
                    return element;
                }

                element.SchemaType = BuildArrayType(field.DataType, RankDepth(field.ValueRank));
                element.IsNillable = true;
                return element;
            }

            private XmlSchemaComplexType BuildArrayType(NodeId dataType, int depth)
            {
                TypeReference typeReference = ResolveType(dataType);
                var complexType = new XmlSchemaComplexType();
                var sequence = new XmlSchemaSequence();
                var element = new XmlSchemaElement
                {
                    Name = ElementName(typeReference),
                    MinOccurs = 0,
                    MaxOccursString = "unbounded",
                    IsNillable = typeReference.IsNillable
                };

                if (depth <= 1)
                {
                    element.SchemaTypeName = typeReference.Name;
                }
                else
                {
                    element.SchemaType = BuildArrayType(dataType, depth - 1);
                }

                sequence.Items.Add(element);
                complexType.Particle = sequence;
                return complexType;
            }

            private TypeReference ResolveType(NodeId dataType)
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType);
                if (builtInType != BuiltInType.Null)
                {
                    return BuiltInTypeReference(builtInType);
                }

                if (m_resolver.TryResolve(dataType, out UaTypeDescription? referenced))
                {
                    if (string.Equals(referenced.NamespaceUri, m_targetNamespace, StringComparison.Ordinal))
                    {
                        EnsureType(referenced);
                        return new TypeReference(Tns(referenced.Name), referenced.Name, true);
                    }

                    AddNamespaceImport(referenced.NamespaceUri);
                    return new TypeReference(new XmlQualifiedName(referenced.Name, referenced.NamespaceUri),
                        referenced.Name,
                        true);
                }

                return new TypeReference(Xs("anyType"), "Value", true);
            }

            private void AddElement(string name, XmlQualifiedName typeName, bool isNillable)
            {
                Schema.Items.Add(new XmlSchemaElement
                {
                    Name = name,
                    SchemaTypeName = typeName,
                    IsNillable = isNillable
                });
            }

            private void AddListType(string name, XmlQualifiedName typeName, bool isNillable)
            {
                string listName = "ListOf" + name;
                if (!m_emittedListTypes.Add(listName))
                {
                    return;
                }

                var complexType = new XmlSchemaComplexType { Name = listName };
                var sequence = new XmlSchemaSequence();
                sequence.Items.Add(new XmlSchemaElement
                {
                    Name = name,
                    SchemaTypeName = typeName,
                    MinOccurs = 0,
                    MaxOccursString = "unbounded",
                    IsNillable = isNillable
                });
                complexType.Particle = sequence;
                Schema.Items.Add(complexType);
                AddElement(listName, Tns(listName), isNillable: true);
            }

            private static TypeReference BuiltInTypeReference(BuiltInType builtInType)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        return new TypeReference(Xs("boolean"), "Boolean", false);
                    case BuiltInType.SByte:
                        return new TypeReference(Xs("byte"), "SByte", false);
                    case BuiltInType.Byte:
                        return new TypeReference(Xs("unsignedByte"), "Byte", false);
                    case BuiltInType.Int16:
                        return new TypeReference(Xs("short"), "Int16", false);
                    case BuiltInType.UInt16:
                        return new TypeReference(Xs("unsignedShort"), "UInt16", false);
                    case BuiltInType.Int32:
                    case BuiltInType.Enumeration:
                        return new TypeReference(Xs("int"), "Int32", false);
                    case BuiltInType.UInt32:
                    case BuiltInType.StatusCode:
                        return new TypeReference(Xs("unsignedInt"), "UInt32", false);
                    case BuiltInType.Int64:
                        return new TypeReference(Xs("long"), "Int64", false);
                    case BuiltInType.UInt64:
                        return new TypeReference(Xs("unsignedLong"), "UInt64", false);
                    case BuiltInType.Float:
                        return new TypeReference(Xs("float"), "Float", false);
                    case BuiltInType.Double:
                        return new TypeReference(Xs("double"), "Double", false);
                    case BuiltInType.String:
                        return new TypeReference(Xs("string"), "String", true);
                    case BuiltInType.DateTime:
                        return new TypeReference(Xs("dateTime"), "DateTime", true);
                    case BuiltInType.Guid:
                        return new TypeReference(Xs("string"), "Guid", true);
                    case BuiltInType.ByteString:
                        return new TypeReference(Xs("base64Binary"), "ByteString", true);
                    case BuiltInType.XmlElement:
                        return new TypeReference(Xs("anyType"), "XmlElement", true);
                    default:
                        return new TypeReference(Ua(builtInType.ToString()), builtInType.ToString(), true);
                }
            }

            private static int RankDepth(int valueRank)
            {
                if (valueRank == ValueRanks.Scalar)
                {
                    return 0;
                }

                if (valueRank is ValueRanks.Any
                    or ValueRanks.ScalarOrOneDimension
                    or ValueRanks.OneOrMoreDimensions)
                {
                    return 1;
                }

                return valueRank < 1 ? 1 : valueRank;
            }

            private static string ElementName(TypeReference typeReference)
            {
                return string.IsNullOrEmpty(typeReference.ElementName) ? "Value" : typeReference.ElementName;
            }

            private static string FieldName(StructureField field, int index)
            {
                return string.IsNullOrEmpty(field.Name) ? "Field" + index : field.Name!;
            }

            private static string EnumValue(EnumField field, int index)
            {
                string name = string.IsNullOrEmpty(field.Name) ? "Value" + index : field.Name!;
                return name + "_" + XmlConvert.ToString(field.Value);
            }

            private void AddNamespaceImport(string namespaceUri)
            {
                if (string.IsNullOrEmpty(namespaceUri) || m_importedNamespaces.Contains(namespaceUri))
                {
                    return;
                }

                m_importedNamespaces.Add(namespaceUri);
                Schema.Namespaces.Add("n" + m_nextNamespacePrefix, namespaceUri);
                m_nextNamespacePrefix++;
                Schema.Includes.Add(new XmlSchemaImport { Namespace = namespaceUri });
            }

            private static string TypeKey(UaTypeDescription type)
            {
                return type.NamespaceUri + "|" + type.Name;
            }

            private XmlQualifiedName Tns(string name)
            {
                return new XmlQualifiedName(name, m_targetNamespace);
            }

            private static XmlQualifiedName Xs(string name)
            {
                return new XmlQualifiedName(name, XmlSchema.Namespace);
            }

            private static XmlQualifiedName Ua(string name)
            {
                return new XmlQualifiedName(name, UaTypesNamespace);
            }

            private const string UaTypesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";

            private readonly IDataTypeDefinitionResolver m_resolver;
            private readonly string m_targetNamespace;
            private readonly HashSet<string> m_emittedTypes;
            private readonly HashSet<string> m_visitingTypes;
            private readonly HashSet<string> m_emittedListTypes;
            private readonly HashSet<string> m_importedNamespaces;
            private int m_nextNamespacePrefix;
        }

        private sealed class TypeReference
        {
            public TypeReference(XmlQualifiedName name, string elementName, bool isNillable)
            {
                Name = name;
                ElementName = elementName;
                IsNillable = isNillable;
            }

            public XmlQualifiedName Name { get; }

            public string ElementName { get; }

            public bool IsNillable { get; }
        }
    }
}
