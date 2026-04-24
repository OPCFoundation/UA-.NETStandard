// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua.Client.Nodes;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Xml;
    using System.Xml.Schema;

    /// <summary>
    /// Xml schema type system. This type system is used to parse xml schema
    /// files and create data type definitions from them.
    /// </summary>
    /// <remarks>
    /// Support for V1.03 dictionaries with the following known restrictions:
    /// - WORK IN PROGRESS
    /// - Complex types are mapped to the V1.04 structured type definition.
    /// - Simple types with enum facet are mapped to the V1.04 enum definition.
    /// - V1.04 OptionSet are not supported.
    /// </remarks>
    internal sealed record class DefaultXmlTypeSystem : DataTypeSystem
    {
        /// <inheritdoc/>
        public override NodeId TypeSystemId
            => Objects.XmlSchema_TypeSystem;
        /// <inheritdoc/>
        public override QualifiedName TypeSystemName
            => BrowseNames.XmlSchema_TypeSystem;
        /// <inheritdoc/>
        public override QualifiedName EncodingName
            => BrowseNames.DefaultXml;

        /// <inheritdoc/>
        public DefaultXmlTypeSystem(INodeCache nodeCache,
            IServiceMessageContext context, ILogger<DefaultXmlTypeSystem> logger)
            : base(nodeCache, context, logger)
        {
        }

        /// <inheritdoc/>
        protected override DataTypeDictionary Load(NodeId dictionaryId,
            string targetNamespace, byte[] buffer, Dictionary<string, byte[]> imports)
        {
            using var istrm = new MemoryStream(buffer);
            var xmlSchemaValidator = new Schema.Xml.XmlSchemaValidator(imports);
            xmlSchemaValidator.Validate(istrm);
            return new DataTypeDictionary(dictionaryId, targetNamespace, TypeSystemId,
                TypeSystemName, null, xmlSchemaValidator.TargetSchema);
        }

        /// <inheritdoc/>
        protected override void LoadDictionaryDataTypeDefinitions(
            Dictionary<XmlQualifiedName,
                (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)> typeDictionary,
            DataTypeDictionary dictionary, NamespaceTable namespaceUris)
        {
            foreach (var xelem in dictionary.Schema!.Elements)
            {
                if (xelem is XmlSchemaType item)
                {
                    var qName = item.QualifiedName ??
                        new XmlQualifiedName(item.Name, dictionary.Namespace);
                    if (typeDictionary.TryGetValue(qName, out var entry))
                    {
                        switch (item)
                        {
                            case XmlSchemaComplexType complexType:
                                _ = ToStructureDefinition(
                                    complexType, entry.EncodingId, typeDictionary,
                                    namespaceUris, entry.TypeId);
                                var structure = new DictionaryDataTypeDefinition(
                                    ToStructureDefinition(complexType, entry.EncodingId,
                                        typeDictionary, namespaceUris, entry.TypeId),
                                    qName, entry.EncodingId);
                                Add(entry.EncodingId, entry.TypeId, structure);
                                break;
                            case XmlSchemaSimpleType simpleType:
                                var enumDefinition = ToEnumDefinition(simpleType);
                                var enumeration = new DictionaryDataTypeDefinition(
                                    enumDefinition, qName, entry.EncodingId);
                                Add(entry.EncodingId, entry.TypeId, enumeration);
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Convert the simple type enumeration facet to an enum definition
        /// </summary>
        /// <param name="simpleType"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        private static EnumDefinition ToEnumDefinition(XmlSchemaSimpleType simpleType)
        {
            var enumDefinition = new EnumDefinition();
            if (simpleType.Content is XmlSchemaSimpleTypeRestriction restriction)
            {
                foreach (var facet in restriction.Facets)
                {
                    if (facet is not XmlSchemaEnumerationFacet enumFacet)
                    {
                        // Is this allowed?
                        continue;
                    }
                    if (enumFacet.Value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadDataEncodingInvalid,
                            "Enumeration facet value is missing.");
                    }
                    var index = enumFacet.Value.LastIndexOf('_');
                    long value = 0;
                    if (index <= 0 ||
                        !long.TryParse(enumFacet.Value.AsSpan(index + 1), out value))
                    {
                        // Log
                    }
                    var enumTypeField = new EnumField
                    {
                        Name = enumFacet.Value,
                        Value = value,
                        Description = enumFacet.Annotation?.Items?.OfType<XmlSchemaDocumentation>()
                            .FirstOrDefault()?.Markup?.FirstOrDefault()?.InnerText,
                        DisplayName = enumFacet.Annotation?.Items?.OfType<XmlSchemaDocumentation>()
                            .FirstOrDefault()?.Markup?.FirstOrDefault()?.InnerText
                    };
                    enumDefinition.Fields.Add(enumTypeField);
                }
            }
            return enumDefinition;
        }

        /// <summary>
        /// Convert the complex type to a structure definition
        /// </summary>
        /// <param name="complexType"></param>
        /// <param name="encodingId"></param>
        /// <param name="typeDictionary"></param>
        /// <param name="namespaceUris"></param>
        /// <param name="typeId"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        private static StructureDefinition ToStructureDefinition(XmlSchemaComplexType complexType,
            ExpandedNodeId encodingId, Dictionary<XmlQualifiedName,
                (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)> typeDictionary,
            NamespaceTable namespaceUris, ExpandedNodeId typeId)
        {
            // TODO: Implement
            Debug.Assert(typeId != null);

            var structureDefinition = new StructureDefinition
            {
                BaseDataType = null,
                DefaultEncodingId = ExpandedNodeId.ToNodeId(encodingId, namespaceUris),
                Fields = [],
                StructureType = StructureType.Structure
            };

            if (complexType.Particle is not XmlSchemaSequence sequence)
            {
                throw ServiceResultException.Create(StatusCodes.BadDataEncodingInvalid,
                    "Complex type does not contain a sequence.");
            }
            foreach (var particle in sequence.Items)
            {
                if (particle is XmlSchemaElement element)
                {
                    var field = new StructureField
                    {
                        Name = element.Name,
                        Description = null,
                        DataType = ResolveDataType(element.SchemaTypeName,
                            typeDictionary, namespaceUris),
                        IsOptional = element.MinOccurs == 0,
                        MaxStringLength = 0,
                        ArrayDimensions = null,
                        ValueRank = element.MaxOccurs > 1 ? 1 : -1
                    };
                    structureDefinition.Fields.Add(field);
                }
            }

            return structureDefinition;

            static NodeId ResolveDataType(XmlQualifiedName typeName,
                Dictionary<XmlQualifiedName,
                    (ExpandedNodeId TypeId, ExpandedNodeId EncodingId)> typeDictionary,
                NamespaceTable namespaceUris)
            {
                if (typeDictionary.TryGetValue(typeName, out var referenceId))
                {
                    return ExpandedNodeId.ToNodeId(referenceId.TypeId, namespaceUris);
                }
                return NodeId.Null;
            }
        }
    }
}
