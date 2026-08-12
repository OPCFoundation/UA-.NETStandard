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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Opc.Ua.Export;
using SystemXml = System.Xml;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Serializes an AAS V3 NodeSet subtree produced by clause 6.1.6 back into an AAS Environment.
    /// </summary>
    public static class AasEnvironmentSerializer
    {
        /// <summary>
        /// Serializes a clause 6.1.6 NodeSet into the corresponding AAS Environment.
        /// </summary>
        /// <param name="nodeSet">The NodeSet to read.</param>
        /// <returns>The reconstructed environment and structured diagnostics.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="nodeSet"/> is <c>null</c>.</exception>
        public static AasSerializationResult Serialize(UANodeSet nodeSet)
        {
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }

            var reader = new NodeSetReader(nodeSet);
            return reader.Serialize();
        }

        private sealed class NodeSetReader
        {
            public NodeSetReader(UANodeSet nodeSet)
            {
                m_nodes = new Dictionary<string, UANode>(StringComparer.Ordinal);
                if (nodeSet.Items is not null)
                {
                    foreach (UANode node in nodeSet.Items)
                    {
                        if (!string.IsNullOrEmpty(node.NodeId))
                        {
                            m_nodes[node.NodeId] = node;
                        }
                    }
                }

                m_context = ServiceMessageContext.CreateEmpty(null!);
                m_namespaceUris = m_context.NamespaceUris;
                if (nodeSet.NamespaceUris is not null)
                {
                    foreach (string namespaceUri in nodeSet.NamespaceUris)
                    {
                        m_namespaceUris.GetIndexOrAppend(namespaceUri);
                    }
                }
            }

            public AasSerializationResult Serialize()
            {
                var environment = new AasEnvironment();
                UANode? root = FindByBrowseName("1:AASEnvironment");
                if (root is null)
                {
                    AddError("The NodeSet does not contain the AASEnvironment root.", null);
                    return new AasSerializationResult(environment, m_diagnostics);
                }

                var shells = new List<AasShell>();
                var submodels = new List<AasSubmodel>();
                var concepts = new List<AasConceptDescription>();
                foreach (UANode child in Children(root, "Organizes"))
                {
                    if (!TryParseAasNodeId(child.NodeId, out AasParsedNodeId parsed))
                    {
                        AddError("A top-level child has no canonical AAS NodeId.", child);
                        continue;
                    }

                    switch (parsed.Kind)
                    {
                        case AasNodeKind.Shell:
                            shells.Add(ReadShell(child, parsed.Id));
                            break;
                        case AasNodeKind.Submodel:
                            submodels.Add(ReadSubmodel(child, parsed.Id));
                            break;
                        case AasNodeKind.ConceptDescription:
                            concepts.Add(ReadConceptDescription(child, parsed.Id));
                            break;
                    }
                }

                if (shells.Count > 0)
                {
                    environment = environment with
                    {
                        AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(
                            new ArrayOf<AasShell>(shells.ToArray()))
                    };
                }

                if (submodels.Count > 0)
                {
                    environment = environment with
                    {
                        Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new ArrayOf<AasSubmodel>(
                            submodels.ToArray()))
                    };
                }

                if (concepts.Count > 0)
                {
                    environment = environment with
                    {
                        ConceptDescriptions = AasOptional<ArrayOf<AasConceptDescription>>.Present(
                            new ArrayOf<AasConceptDescription>(concepts.ToArray()))
                    };
                }

                return new AasSerializationResult(environment, m_diagnostics);
            }

            private AasShell ReadShell(UANode node, string id)
            {
                var shell = new AasShell
                {
                    Id = StringProperty(node, "Id") ?? id,
                    AssetInformation = ReadAssetInformation(Component(node, "AssetInformation"))
                };
                ReadReferable(node, ref shell);
                shell = shell with
                {
                    Administration = StructureProperty<AASAdministrativeInformationDataType>(node, "Administration"),
                    SubmodelReferences = StructureArrayProperty<AASReferenceDataType>(node, "SubmodelReferences"),
                    DerivedFrom = StructureProperty<AASReferenceDataType>(node, "DerivedFrom"),
                    EmbeddedDataSpecifications = StructureArrayProperty<AASEmbeddedDataSpecificationDataType>(
                        node,
                        "EmbeddedDataSpecifications")
                };
                return shell;
            }

            private AasSubmodel ReadSubmodel(UANode node, string id)
            {
                var submodel = new AasSubmodel { Id = StringProperty(node, "Id") ?? id };
                ReadReferable(node, ref submodel);
                submodel = submodel with
                {
                    Administration = StructureProperty<AASAdministrativeInformationDataType>(node, "Administration"),
                    Kind = EnumProperty<AASModellingKindDataType>(node, "Kind"),
                    SemanticId = StructureProperty<AASReferenceDataType>(node, "SemanticId"),
                    SupplementalSemanticIds = StructureArrayProperty<AASReferenceDataType>(
                        node,
                        "SupplementalSemanticIds"),
                    Qualifiers = StructureArrayProperty<AASQualifierDataType>(node, "Qualifiers"),
                    EmbeddedDataSpecifications = StructureArrayProperty<AASEmbeddedDataSpecificationDataType>(
                        node,
                        "EmbeddedDataSpecifications")
                };

                AasSubmodelElement[] elements = ElementChildren(node, includeOrdered: false)
                    .Select(ReadElement)
                    .ToArray();
                if (elements.Length > 0)
                {
                    submodel = submodel with
                    {
                        SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                            new ArrayOf<AasSubmodelElement>(elements))
                    };
                }

                return submodel;
            }

            private AasConceptDescription ReadConceptDescription(UANode node, string id)
            {
                var concept = new AasConceptDescription { Id = StringProperty(node, "Id") ?? id };
                ReadReferable(node, ref concept);
                concept = concept with
                {
                    Administration = StructureProperty<AASAdministrativeInformationDataType>(node, "Administration"),
                    IsCaseOf = StructureArrayProperty<AASReferenceDataType>(node, "IsCaseOf"),
                    EmbeddedDataSpecifications = StructureArrayProperty<AASEmbeddedDataSpecificationDataType>(
                        node,
                        "EmbeddedDataSpecifications")
                };
                return concept;
            }

            private AasAssetInformation ReadAssetInformation(UANode? node)
            {
                if (node is null)
                {
                    AddError("A shell is missing its mandatory AssetInformation component.", null);
                    return new AasAssetInformation { AssetKind = AASAssetKindDataType.Instance };
                }

                return new AasAssetInformation
                {
                    AssetKind = EnumProperty<AASAssetKindDataType>(node, "AssetKind").IsPresent
                        ? EnumProperty<AASAssetKindDataType>(node, "AssetKind").Value
                        : AASAssetKindDataType.Instance,
                    GlobalAssetId = StringOptionalProperty(node, "GlobalAssetId"),
                    AssetType = StringOptionalProperty(node, "AssetType"),
                    SpecificAssetIds = StructureArrayProperty<AASSpecificAssetIdDataType>(node, "SpecificAssetIds"),
                    DefaultThumbnail = StructureProperty<AASResourceDataType>(node, "DefaultThumbnail")
                };
            }

            private AasSubmodelElement ReadElement(UANode node)
            {
                string modelType = StringProperty(node, "ModelType") ?? ModelTypeFromObjectType(node);
                AasSubmodelElement element;
                switch (modelType)
                {
                    case "Property":
                        element = ReadProperty(node);
                        break;
                    case "MultiLanguageProperty":
                        element = new AasMultiLanguageProperty
                        {
                            Value = StructureArrayProperty<AASLangStringDataType>(node, "Value"),
                            ValueId = StructureProperty<AASReferenceDataType>(node, "ValueId")
                        };
                        break;
                    case "Range":
                        element = ReadRange(node);
                        break;
                    case "Blob":
                        element = new AasBlob
                        {
                            Value = ByteStringOptionalProperty(node, "Value"),
                            ContentType = StringProperty(node, "ContentType") ?? string.Empty
                        };
                        break;
                    case "File":
                        element = new AasFile
                        {
                            Value = StringOptionalProperty(node, "Value"),
                            ContentType = StringProperty(node, "ContentType") ?? string.Empty
                        };
                        break;
                    case "ReferenceElement":
                        element = new AasReferenceElement
                        {
                            Value = StructureProperty<AASReferenceDataType>(node, "Value")
                        };
                        break;
                    case "RelationshipElement":
                        element = new AasRelationshipElement
                        {
                            First = StructureProperty(node, "First", EmptyReference()),
                            Second = StructureProperty(node, "Second", EmptyReference())
                        };
                        break;
                    case "AnnotatedRelationshipElement":
                        element = new AasAnnotatedRelationshipElement
                        {
                            First = StructureProperty(node, "First", EmptyReference()),
                            Second = StructureProperty(node, "Second", EmptyReference()),
                            Annotations = PresentElements(ElementChildren(node, includeOrdered: false).Select(ReadElement))
                        };
                        break;
                    case "SubmodelElementCollection":
                        element = new AasSubmodelElementCollection
                        {
                            Value = PresentElements(ElementChildren(node, includeOrdered: false).Select(ReadElement))
                        };
                        break;
                    case "SubmodelElementList":
                        element = ReadList(node);
                        break;
                    case "Entity":
                        element = new AasEntity
                        {
                            EntityType = EnumProperty<AASEntityTypeDataType>(node, "EntityType").IsPresent
                                ? EnumProperty<AASEntityTypeDataType>(node, "EntityType").Value
                                : default,
                            GlobalAssetId = StringOptionalProperty(node, "GlobalAssetId"),
                            SpecificAssetIds = StructureArrayProperty<AASSpecificAssetIdDataType>(node, "SpecificAssetIds"),
                            Statements = PresentElements(ElementChildren(node, includeOrdered: false).Select(ReadElement))
                        };
                        break;
                    case "BasicEventElement":
                        element = new AasBasicEventElement
                        {
                            Observed = StructureProperty(node, "Observed", EmptyReference()),
                            Direction = EnumProperty<AASDirectionDataType>(node, "Direction").IsPresent
                                ? EnumProperty<AASDirectionDataType>(node, "Direction").Value
                                : default,
                            State = EnumProperty<AASStateOfEventDataType>(node, "State").IsPresent
                                ? EnumProperty<AASStateOfEventDataType>(node, "State").Value
                                : default,
                            MessageTopic = StringOptionalProperty(node, "MessageTopic"),
                            MessageBroker = StructureProperty<AASReferenceDataType>(node, "MessageBroker"),
                            LastUpdate = VariantOptionalProperty(node, "LastUpdate"),
                            MinInterval = VariantOptionalProperty(node, "MinInterval"),
                            MaxInterval = VariantOptionalProperty(node, "MaxInterval")
                        };
                        break;
                    case "Operation":
                        element = new AasOperation
                        {
                            InputVariables = ReadOperationRole(node, "InputVariables"),
                            OutputVariables = ReadOperationRole(node, "OutputVariables"),
                            InoutputVariables = ReadOperationRole(node, "InoutputVariables")
                        };
                        break;
                    case "Capability":
                        element = new AasCapability();
                        break;
                    default:
                        AddError("Unsupported AAS ModelType '" + modelType + "'.", node);
                        element = new AasCapability();
                        break;
                }

                ReadReferable(node, ref element);
                element = element with
                {
                    SemanticId = StructureProperty<AASReferenceDataType>(node, "SemanticId"),
                    SupplementalSemanticIds = StructureArrayProperty<AASReferenceDataType>(
                        node,
                        "SupplementalSemanticIds"),
                    Qualifiers = StructureArrayProperty<AASQualifierDataType>(node, "Qualifiers"),
                    EmbeddedDataSpecifications = StructureArrayProperty<AASEmbeddedDataSpecificationDataType>(
                        node,
                        "EmbeddedDataSpecifications"),
                    Index = UInt32OptionalProperty(node, "Index")
                };
                return element;
            }

            private AasProperty ReadProperty(UANode node)
            {
                AASDataTypeDefXsdDataType valueType = EnumProperty<AASDataTypeDefXsdDataType>(node, "ValueType").IsPresent
                    ? EnumProperty<AASDataTypeDefXsdDataType>(node, "ValueType").Value
                    : AASDataTypeDefXsdDataType.String;
                AasOptional<Variant> value = CanonicalValueProperty(node, "Value", valueType);
                return new AasProperty
                {
                    ValueType = valueType,
                    Value = value,
                    ValueId = StructureProperty<AASReferenceDataType>(node, "ValueId")
                };
            }

            private AasRange ReadRange(UANode node)
            {
                AASDataTypeDefXsdDataType valueType = EnumProperty<AASDataTypeDefXsdDataType>(node, "ValueType").IsPresent
                    ? EnumProperty<AASDataTypeDefXsdDataType>(node, "ValueType").Value
                    : AASDataTypeDefXsdDataType.String;
                return new AasRange
                {
                    ValueType = valueType,
                    Min = CanonicalValueProperty(node, "Min", valueType),
                    Max = CanonicalValueProperty(node, "Max", valueType)
                };
            }

            private AasSubmodelElementList ReadList(UANode node)
            {
                UANode[] ordered = OrderedElementChildren(node).ToArray();
                UANode[] unordered = Children(node, "HasComponent")
                    .Where(IsSubmodelElementNode)
                    .ToArray();
                bool orderRelevant = ordered.Length > 0 || unordered.Length == 0;
                AasSubmodelElement[] values = (orderRelevant ? ordered : unordered)
                    .Select(ReadElement)
                    .ToArray();

                return new AasSubmodelElementList
                {
                    OrderRelevant = orderRelevant
                        ? AasOptional<bool>.Absent
                        : AasOptional<bool>.Present(false),
                    TypeValueListElement = EnumProperty<AASSubmodelElementsDataType>(node, "TypeValueListElement").IsPresent
                        ? EnumProperty<AASSubmodelElementsDataType>(node, "TypeValueListElement").Value
                        : default,
                    SemanticIdListElement = StructureProperty<AASReferenceDataType>(node, "SemanticIdListElement"),
                    ValueTypeListElement = EnumProperty<AASDataTypeDefXsdDataType>(node, "ValueTypeListElement"),
                    Value = values.Length == 0
                        ? AasOptional<ArrayOf<AasSubmodelElement>>.Absent
                        : AasOptional<ArrayOf<AasSubmodelElement>>.Present(new ArrayOf<AasSubmodelElement>(values))
                };
            }

            private AasOptional<ArrayOf<AasSubmodelElement>> ReadOperationRole(UANode node, string propertyName)
            {
                UAVariable? role = Property(node, propertyName);
                if (role is null)
                {
                    return AasOptional<ArrayOf<AasSubmodelElement>>.Absent;
                }

                AASOperationVariableDataType[] descriptors = StructureArray<AASOperationVariableDataType>(role);
                var values = new List<AasSubmodelElement>();
                for (int ii = 0; ii < descriptors.Length; ii++)
                {
                    string nodeId = ToNodeSetId(descriptors[ii].ValueNodeId);
                    if (m_nodes.TryGetValue(nodeId, out UANode? valueNode))
                    {
                        values.Add(ReadElement(valueNode));
                    }
                    else
                    {
                        AddError("An OperationVariable descriptor points at a missing value node.", role);
                    }
                }

                return AasOptional<ArrayOf<AasSubmodelElement>>.Present(new ArrayOf<AasSubmodelElement>(
                    values.ToArray()));
            }

            private void ReadReferable<T>(UANode node, ref T value)
                where T : AasReferable
            {
                value = value with
                {
                    IdShort = StringOptionalProperty(node, "IdShort"),
                    Category = StringOptionalProperty(node, "Category"),
                    DisplayName = StructureArrayProperty<AASLangStringDataType>(node, "DisplayNameSet"),
                    Description = StructureArrayProperty<AASLangStringDataType>(node, "DescriptionSet"),
                    Extensions = StructureArrayProperty<AASExtensionDataType>(node, "Extensions")
                };
            }

            private AasOptional<ArrayOf<AasSubmodelElement>> PresentElements(IEnumerable<AasSubmodelElement> source)
            {
                AasSubmodelElement[] values = source.ToArray();
                return values.Length == 0
                    ? AasOptional<ArrayOf<AasSubmodelElement>>.Absent
                    : AasOptional<ArrayOf<AasSubmodelElement>>.Present(new ArrayOf<AasSubmodelElement>(values));
            }

            private AasOptional<Variant> CanonicalValueProperty(
                UANode node,
                string browseName,
                AASDataTypeDefXsdDataType fallback)
            {
                UAVariable? variable = Property(node, browseName);
                if (variable is null)
                {
                    return AasOptional<Variant>.Absent;
                }

                Variant value = ReadVariant(variable);
                AASDataTypeDefXsdDataType valueType = TryGetValueType(variable, out AASDataTypeDefXsdDataType declared)
                    ? declared
                    : fallback;
                if (!AasLexicalCanonicalizer.TryCanonicalize(value, valueType, out string? lexical, out string? error) ||
                    lexical is null)
                {
                    if (valueType == AASDataTypeDefXsdDataType.Decimal &&
                        TryReadDecimalLexical(variable.Value, out lexical) &&
                        AasLexicalCanonicalizer.TryCanonicalizeLexical(
                            lexical,
                            valueType,
                            out string? canonical,
                            out _) &&
                        canonical is not null)
                    {
                        return AasOptional<Variant>.Present(new Variant(canonical));
                    }

                    AddError("A value could not be serialized canonically: " + (error ?? "unknown error"), variable);
                    return AasOptional<Variant>.Present(value);
                }

                return AasOptional<Variant>.Present(new Variant(lexical));
            }

            private AasOptional<Variant> VariantOptionalProperty(UANode node, string browseName)
            {
                UAVariable? variable = Property(node, browseName);
                return variable is null
                    ? AasOptional<Variant>.Absent
                    : AasOptional<Variant>.Present(ReadVariant(variable));
            }

            private AasOptional<string> StringOptionalProperty(UANode node, string browseName)
            {
                string? value = StringProperty(node, browseName);
                return value is null ? AasOptional<string>.Absent : AasOptional<string>.Present(value);
            }

            private string? StringProperty(UANode node, string browseName)
            {
                UAVariable? variable = Property(node, browseName);
                if (variable is null)
                {
                    return null;
                }

                Variant value = ReadVariant(variable);
                return value.TryGetValue(out string? text) ? text : null;
            }

            private AasOptional<ByteString> ByteStringOptionalProperty(UANode node, string browseName)
            {
                UAVariable? variable = Property(node, browseName);
                if (variable is null)
                {
                    return AasOptional<ByteString>.Absent;
                }

                Variant value = ReadVariant(variable);
                return value.TryGetValue(out ByteString bytes)
                    ? AasOptional<ByteString>.Present(bytes)
                    : AasOptional<ByteString>.Absent;
            }

            private AasOptional<uint> UInt32OptionalProperty(UANode node, string browseName)
            {
                UAVariable? variable = Property(node, browseName);
                if (variable is null)
                {
                    return AasOptional<uint>.Absent;
                }

                Variant value = ReadVariant(variable);
                if (value.TryGetValue(out uint unsigned))
                {
                    return AasOptional<uint>.Present(unsigned);
                }

                return value.TryGetValue(out int signed) && signed >= 0
                    ? AasOptional<uint>.Present((uint)signed)
                    : AasOptional<uint>.Absent;
            }

            private AasOptional<T> EnumProperty<T>(UANode node, string browseName)
                where T : struct
            {
                UAVariable? variable = Property(node, browseName);
                if (variable is null)
                {
                    return AasOptional<T>.Absent;
                }

                Variant value = ReadVariant(variable);
                if (value.TryGetValue(out int signed))
                {
                    return AasOptional<T>.Present((T)Enum.ToObject(typeof(T), signed));
                }

                if (value.TryGetValue(out uint unsigned))
                {
                    return AasOptional<T>.Present((T)Enum.ToObject(typeof(T), unsigned));
                }

                return AasOptional<T>.Absent;
            }

            private AasOptional<T> StructureProperty<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(UANode node, string browseName)
                where T : class, IEncodeable
            {
                UAVariable? variable = Property(node, browseName);
                if (variable is null)
                {
                    return AasOptional<T>.Absent;
                }

                return AasOptional<T>.Present(Structure<T>(variable));
            }

            private T StructureProperty<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(UANode node, string browseName, T fallback)
                where T : class, IEncodeable
            {
                UAVariable? variable = Property(node, browseName);
                return variable is null ? fallback : Structure<T>(variable);
            }

            private AasOptional<ArrayOf<T>> StructureArrayProperty<T>(UANode node, string browseName)
                where T : class, IEncodeable
            {
                UAVariable? variable = Property(node, browseName);
                if (variable is null)
                {
                    return AasOptional<ArrayOf<T>>.Absent;
                }

                return AasOptional<ArrayOf<T>>.Present(new ArrayOf<T>(StructureArray<T>(variable)));
            }

            private T Structure<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(UAVariable variable)
                where T : class, IEncodeable
            {
                Variant value = ReadVariant(variable);
                ExtensionObject extension = default;
                if (value.TryGetValue(out extension) &&
                    TryDecodeExtension(extension, out T? decoded) &&
                    decoded is not null)
                {
                    return decoded;
                }

                AddError("A structure property could not be decoded.", variable);
                return Empty<T>();
            }

            private T[] StructureArray<T>(UAVariable variable)
                where T : class, IEncodeable
            {
                Variant value = ReadVariant(variable);
                if (value.TryGetValue(out ArrayOf<ExtensionObject> extensions))
                {
                    return DecodeArray<T>(extensions);
                }

                return Array.Empty<T>();
            }

            private T[] DecodeArray<T>(ArrayOf<ExtensionObject> extensions)
                where T : class, IEncodeable
            {
                var values = new List<T>(extensions.Count);
                foreach (ExtensionObject extension in extensions.Span)
                {
                    if (TryDecodeExtension(extension, out T? decoded) && decoded is not null)
                    {
                        values.Add(decoded);
                    }
                }

                return values.ToArray();
            }

            private bool TryDecodeExtension<T>(ExtensionObject extension, out T? decoded)
                where T : class, IEncodeable
            {
                if (extension.TryGetValue(out decoded, m_context))
                {
                    return true;
                }

                if (extension.TryGetAsXml(out XmlElement xml, m_context))
                {
                    decoded = DecodeXmlExtension<T>(xml.AsXmlElement());
                    return decoded is not null;
                }

                decoded = default;
                return false;
            }

            private T? DecodeXmlExtension<T>(SystemXml.XmlElement? xml)
                where T : class, IEncodeable
            {
                SystemXml.XmlElement? body = FirstElement(xml, "Body");
                SystemXml.XmlElement? value = body is null ? xml : FirstElement(body);
                if (value is null)
                {
                    return default;
                }

                if (typeof(T) == typeof(AASLangStringDataType))
                {
                    return (T)(IEncodeable)new AASLangStringDataType
                    {
                        Language = ChildText(value, "Language"),
                        Text = ChildText(value, "Text")
                    };
                }

                if (typeof(T) == typeof(AASKeyDataType))
                {
                    return (T)(IEncodeable)new AASKeyDataType
                    {
                        Type = ParseEnumText<AASKeyTypesDataType>(ChildText(value, "Type")),
                        Value = ChildText(value, "Value")
                    };
                }

                if (typeof(T) == typeof(AASReferenceDataType))
                {
                    AASReferenceDataType reference = Empty<AASReferenceDataType>();
                    string type = ChildText(value, "Type");
                    reference.Type = string.IsNullOrEmpty(type)
                        ? AASReferenceTypesDataType.ExternalReference
                        : ParseEnumText<AASReferenceTypesDataType>(type);
                    reference.Keys = new ArrayOf<AASKeyDataType>(ChildElements(FirstElement(value, "Keys"), "AASKeyDataType")
                        .Select(DecodeKey)
                        .ToArray());
                    return (T)(IEncodeable)reference;
                }

                if (typeof(T) == typeof(AASOperationVariableDataType))
                {
                    AASOperationVariableDataType descriptor = Empty<AASOperationVariableDataType>();
                    descriptor.ValueNodeId = NodeId.Parse(ChildText(FirstElement(value, "ValueNodeId"), "Identifier"));
                    return (T)(IEncodeable)descriptor;
                }

                if (typeof(T) == typeof(AASQualifierDataType))
                {
                    AASQualifierDataType qualifier = Empty<AASQualifierDataType>();
                    qualifier.Type = ChildText(value, "Type");
                    qualifier.ValueType = ParseEnumText<AASDataTypeDefXsdDataType>(ChildText(value, "ValueType"));
                    qualifier.Value = ChildText(value, "Value");
                    return (T)(IEncodeable)qualifier;
                }

                if (typeof(T) == typeof(AASSpecificAssetIdDataType))
                {
                    AASSpecificAssetIdDataType specific = Empty<AASSpecificAssetIdDataType>();
                    specific.Name = ChildText(value, "Name");
                    specific.Value = ChildText(value, "Value");
                    return (T)(IEncodeable)specific;
                }

                return default;
            }

            private static AASKeyDataType DecodeKey(SystemXml.XmlElement value)
            {
                return new AASKeyDataType
                {
                    Type = ParseEnumText<AASKeyTypesDataType>(ChildText(value, "Type")),
                    Value = ChildText(value, "Value")
                };
            }

            private static T ParseEnumText<T>(string text)
                where T : struct
            {
                int suffix = text.LastIndexOf('_');
                string candidate = suffix > 0 ? text.Substring(0, suffix) : text;
                return Enum.TryParse(candidate, true, out T value) ? value : default;
            }

            private static IEnumerable<SystemXml.XmlElement> ChildElements(SystemXml.XmlElement? element, string localName)
            {
                if (element is null)
                {
                    yield break;
                }

                foreach (SystemXml.XmlNode child in element.ChildNodes)
                {
                    if (child is SystemXml.XmlElement childElement &&
                        string.Equals(childElement.LocalName, localName, StringComparison.Ordinal))
                    {
                        yield return childElement;
                    }
                }
            }

            private static SystemXml.XmlElement? FirstElement(SystemXml.XmlElement? element, string? localName = null)
            {
                if (element is null)
                {
                    return null;
                }

                foreach (SystemXml.XmlNode child in element.ChildNodes)
                {
                    if (child is SystemXml.XmlElement childElement &&
                        (localName is null || string.Equals(childElement.LocalName, localName, StringComparison.Ordinal)))
                    {
                        return childElement;
                    }
                }

                return null;
            }

            private static string ChildText(SystemXml.XmlElement? element, string localName)
            {
                return FirstElement(element, localName)?.InnerText ?? string.Empty;
            }

            private static bool TryReadDecimalLexical(SystemXml.XmlElement? value, out string lexical)
            {
                lexical = string.Empty;
                SystemXml.XmlElement? body = FirstElement(value, "Body");
                SystemXml.XmlElement? dec = FirstElement(body, "Decimal");
                if (dec is null ||
                    !int.TryParse(ChildText(dec, "Scale"), NumberStyles.None, CultureInfo.InvariantCulture, out int scale))
                {
                    return false;
                }

                string digits = ChildText(dec, "Value");
                bool negative = digits.StartsWith('-');
                if (negative)
                {
                    digits = digits.Substring(1);
                }

                if (scale <= 0)
                {
                    lexical = (negative ? "-" : string.Empty) + digits + new string('0', -scale);
                    return true;
                }

                if (digits.Length <= scale)
                {
                    digits = new string('0', scale - digits.Length + 1) + digits;
                }

                int point = digits.Length - scale;
                lexical = (negative ? "-" : string.Empty) + digits.Insert(point, ".");
                return true;
            }

            private Variant ReadVariant(UAVariable variable)
            {
                if (variable.Value is null)
                {
                    return Variant.Null;
                }

                try
                {
                    using var decoder = new XmlDecoder(variable.Value, m_context);
                    return decoder.ReadVariantValue();
                }
                catch (Exception ex) when (ex is ServiceResultException || ex is FormatException || ex is InvalidOperationException)
                {
                    AddError("A Variable value could not be decoded: " + ex.Message, variable);
                    return Variant.Null;
                }
            }

            private bool TryGetValueType(UAVariable variable, out AASDataTypeDefXsdDataType valueType)
            {
                valueType = default;
                NodeId dataType = ParseNodeId(variable.DataType);
                return !dataType.IsNull && AasXsdTypeMap.TryGetValueType(dataType, m_namespaceUris, out valueType);
            }

            private UANode? FindByBrowseName(string browseName)
            {
                foreach (UANode node in m_nodes.Values)
                {
                    if (string.Equals(node.BrowseName, browseName, StringComparison.Ordinal))
                    {
                        return node;
                    }
                }

                return null;
            }

            private UANode? Component(UANode node, string browseName)
            {
                return Children(node, "HasComponent")
                    .FirstOrDefault(child => string.Equals(child.BrowseName, BrowseName(browseName), StringComparison.Ordinal));
            }

            private UAVariable? Property(UANode node, string browseName)
            {
                return Children(node, "HasProperty")
                    .OfType<UAVariable>()
                    .FirstOrDefault(child => string.Equals(child.BrowseName, BrowseName(browseName), StringComparison.Ordinal));
            }

            private IEnumerable<UANode> ElementChildren(UANode node, bool includeOrdered)
            {
                IEnumerable<UANode> unordered = Children(node, "HasComponent").Where(IsSubmodelElementNode);
                return includeOrdered ? unordered.Concat(OrderedElementChildren(node)) : unordered;
            }

            private IEnumerable<UANode> OrderedElementChildren(UANode node)
            {
                return Children(node, "HasOrderedComponent")
                    .Where(IsSubmodelElementNode)
                    .OrderBy(IndexOf);
            }

            private uint IndexOf(UANode node)
            {
                return UInt32OptionalProperty(node, "Index").IsPresent ? UInt32OptionalProperty(node, "Index").Value : 0;
            }

            private IEnumerable<UANode> Children(UANode node, string referenceType)
            {
                if (node.References is null)
                {
                    yield break;
                }

                foreach (Reference reference in node.References)
                {
                    if (reference.IsForward &&
                        string.Equals(reference.ReferenceType, referenceType, StringComparison.Ordinal) &&
                        reference.Value is not null &&
                        m_nodes.TryGetValue(reference.Value, out UANode? child))
                    {
                        yield return child;
                    }
                }
            }

            private bool IsSubmodelElementNode(UANode node)
            {
                string modelType = StringProperty(node, "ModelType") ?? string.Empty;
                return modelType.Length > 0 &&
                    !string.Equals(modelType, "AssetAdministrationShell", StringComparison.Ordinal) &&
                    !string.Equals(modelType, "Submodel", StringComparison.Ordinal) &&
                    !string.Equals(modelType, "ConceptDescription", StringComparison.Ordinal);
            }

            private string ModelTypeFromObjectType(UANode node)
            {
                string typeId = TypeDefinitionOf(node);
                return s_objectTypeModelTypes.TryGetValue(typeId, out string? modelType) ? modelType : string.Empty;
            }

            private string TypeDefinitionOf(UANode node)
            {
                if (node.References is null)
                {
                    return string.Empty;
                }

                foreach (Reference reference in node.References)
                {
                    if (reference.IsForward &&
                        string.Equals(reference.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal))
                    {
                        return reference.Value ?? string.Empty;
                    }
                }

                return string.Empty;
            }

            private bool TryParseAasNodeId(string? nodeId, out AasParsedNodeId parsed)
            {
                parsed = default;
                const string prefix = "ns=1;s=";
                return nodeId is not null &&
                    nodeId.StartsWith(prefix, StringComparison.Ordinal) &&
                    AasNodeIdEncoding.TryParse(nodeId.Substring(prefix.Length), out parsed);
            }

            private NodeId ParseNodeId(string? text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return NodeId.Null;
                }

                try
                {
                    return NodeId.Parse(text);
                }
                catch (Exception ex) when (ex is ServiceResultException || ex is FormatException)
                {
                    return NodeId.Null;
                }
            }

            private string ToNodeSetId(NodeId nodeId)
            {
                if (nodeId.NamespaceIndex == 0 && nodeId.TryGetValue(out uint coreId))
                {
                    return "i=" + coreId.ToString(CultureInfo.InvariantCulture);
                }

                if (nodeId.TryGetValue(out uint id))
                {
                    return "ns=" + nodeId.NamespaceIndex.ToString(CultureInfo.InvariantCulture) +
                        ";i=" + id.ToString(CultureInfo.InvariantCulture);
                }

                return nodeId.ToString();
            }

            private void AddError(string message, UANode? node)
            {
                m_diagnostics.Add(new AasSerializationDiagnostic(
                    AasMaterializationDiagnosticSeverity.Error,
                    AasSerializationDiagnosticCode.InvalidNodeSet,
                    message,
                    node?.NodeId));
            }

            private static string BrowseName(string name)
            {
                return "1:" + name;
            }

            private static AASReferenceDataType EmptyReference()
            {
                return Empty<AASReferenceDataType>();
            }

            private static T Empty<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>()
                where T : class
            {
#pragma warning disable SYSLIB0050 // TODO: remove when generated recursive default constructors are fixed.
                return (T)FormatterServices.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
            }

            private static readonly Dictionary<string, string> s_objectTypeModelTypes = new(StringComparer.Ordinal)
            {
                ["ns=1;i=1021"] = "Property",
                ["ns=1;i=1022"] = "MultiLanguageProperty",
                ["ns=1;i=1023"] = "Range",
                ["ns=1;i=1024"] = "Blob",
                ["ns=1;i=1025"] = "File",
                ["ns=1;i=1026"] = "ReferenceElement",
                ["ns=1;i=1027"] = "RelationshipElement",
                ["ns=1;i=1028"] = "AnnotatedRelationshipElement",
                ["ns=1;i=1029"] = "SubmodelElementCollection",
                ["ns=1;i=1030"] = "SubmodelElementList",
                ["ns=1;i=1031"] = "Entity",
                ["ns=1;i=1032"] = "BasicEventElement",
                ["ns=1;i=1033"] = "Operation",
                ["ns=1;i=1034"] = "Capability"
            };

            private readonly ServiceMessageContext m_context;
            private readonly List<AasSerializationDiagnostic> m_diagnostics = [];
            private readonly Dictionary<string, UANode> m_nodes;
            private readonly NamespaceTable m_namespaceUris;
        }
    }

    /// <summary>
    /// The result of serializing an AAS NodeSet subtree into an Environment.
    /// </summary>
    public sealed class AasSerializationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AasSerializationResult"/> class.
        /// </summary>
        /// <param name="environment">The reconstructed environment.</param>
        /// <param name="diagnostics">The diagnostics produced while serializing.</param>
        public AasSerializationResult(
            AasEnvironment environment,
            IReadOnlyList<AasSerializationDiagnostic> diagnostics)
        {
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Gets the reconstructed environment.
        /// </summary>
        public AasEnvironment Environment { get; }

        /// <summary>
        /// Gets the diagnostics produced while serializing.
        /// </summary>
        public IReadOnlyList<AasSerializationDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets a value indicating whether any error diagnostic was produced.
        /// </summary>
        public bool HasErrors
        {
            get
            {
                for (int ii = 0; ii < Diagnostics.Count; ii++)
                {
                    if (Diagnostics[ii].Severity == AasMaterializationDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Stable diagnostic codes emitted by the AAS serializer.
    /// </summary>
    public enum AasSerializationDiagnosticCode
    {
        /// <summary>
        /// No specific code.
        /// </summary>
        None = 0,

        /// <summary>
        /// The NodeSet is not a conformant clause 6.1.6 materialization.
        /// </summary>
        InvalidNodeSet = 2000
    }

    /// <summary>
    /// A single structured AAS serialization diagnostic.
    /// </summary>
    public sealed class AasSerializationDiagnostic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AasSerializationDiagnostic"/> class.
        /// </summary>
        /// <param name="severity">The diagnostic severity.</param>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="message">The human-readable message.</param>
        /// <param name="nodeId">The NodeSet location, when applicable.</param>
        public AasSerializationDiagnostic(
            AasMaterializationDiagnosticSeverity severity,
            AasSerializationDiagnosticCode code,
            string message,
            string? nodeId = null)
        {
            Severity = severity;
            Code = code;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            NodeId = nodeId;
        }

        /// <summary>
        /// Gets the diagnostic severity.
        /// </summary>
        public AasMaterializationDiagnosticSeverity Severity { get; }

        /// <summary>
        /// Gets the stable diagnostic code.
        /// </summary>
        public AasSerializationDiagnosticCode Code { get; }

        /// <summary>
        /// Gets the human-readable message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the NodeSet location, when applicable.
        /// </summary>
        public string? NodeId { get; }
    }
}
