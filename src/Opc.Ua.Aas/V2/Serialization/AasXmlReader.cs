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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Opc.Ua.Aas.V2
{
    /// <summary>
    /// Reads AAS V2.0.1 XML Environment documents.
    /// </summary>
    public sealed class AasXmlReader
    {
        /// <summary>
        /// Reads an AAS V2.0.1 XML Environment document from a stream.
        /// </summary>
        /// <param name="stream">The XML stream.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The parsed environment or a diagnostic.</returns>
        public async Task<AasDocumentReadResult> ReadAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            try
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;
                using XmlReader reader = XmlReader.Create(buffer);
                XDocument document = XDocument.Load(reader, LoadOptions.None);
                XElement root = document.Root ?? new XElement("empty");
                if (root.Name.LocalName != "aasenv")
                {
                    return AasDocumentReadResult.Failure(
                        "The XML document is not an AAS V2.0.1 aasenv document.");
                }

                if (!TryReadEnvironment(root, out AasEnvironment environment, out string? error))
                {
                    return AasDocumentReadResult.Failure(error ?? "The AAS V2 XML document is malformed.");
                }

                return AasDocumentReadResult.Success(environment);
            }
            catch (XmlException ex)
            {
                return AasDocumentReadResult.Failure("The AAS V2 XML document is malformed: " + ex.Message);
            }
        }

        private static bool TryReadEnvironment(XElement element, out AasEnvironment environment, out string? error)
        {
            error = null;
            var value = new AasEnvironment();
            var assetsById = new Dictionary<string, AasAsset>(StringComparer.Ordinal);
            XElement? assets = Child(element, "assets");
            if (assets is not null)
            {
                var items = new List<AasAsset>();
                foreach (XElement assetElement in Children(assets, "asset"))
                {
                    if (!TryReadAsset(assetElement, out AasAsset? asset, out error) || asset is null)
                    {
                        environment = value;
                        return false;
                    }

                    items.Add(asset);
                    assetsById[asset.Identification.Id] = asset;
                    assetsById[asset.IdShort] = asset;
                }

                value = value with { Assets = AasOptional<ArrayOf<AasAsset>>.Present(new(items.ToArray())) };
            }

            XElement? shells = Child(element, "assetAdministrationShells");
            if (shells is not null)
            {
                var items = new List<AasShell>();
                foreach (XElement shellElement in Children(shells, "assetAdministrationShell"))
                {
                    if (!TryReadShell(shellElement, assetsById, out AasShell? shell, out error) || shell is null)
                    {
                        environment = value;
                        return false;
                    }

                    items.Add(shell);
                }

                value = value with { AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(new(items.ToArray())) };
            }

            XElement? submodels = Child(element, "submodels");
            if (submodels is not null)
            {
                var items = new List<AasSubmodel>();
                foreach (XElement submodelElement in Children(submodels, "submodel"))
                {
                    if (!TryReadSubmodel(submodelElement, out AasSubmodel? submodel, out error) || submodel is null)
                    {
                        environment = value;
                        return false;
                    }

                    items.Add(submodel);
                }

                value = value with { Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new(items.ToArray())) };
            }

            XElement? concepts = Child(element, "conceptDescriptions");
            if (concepts is not null)
            {
                value = ReadConceptDescriptions(concepts, value);
            }

            environment = value;
            return true;
        }

        private static bool TryReadAsset(XElement element, out AasAsset? asset, out string? error)
        {
            asset = null;
            if (!TryReadIdentifiable(element, out string idShort, out string category, out AasIdentifier identifier,
                out AasAdministrativeInformation administration, out error))
            {
                return false;
            }

            var value = new AasAsset
            {
                Identification = identifier,
                Administration = administration,
                IdShort = idShort,
                Category = category,
                AssetKind = AasJsonReader.ParseEnum<AASAssetKindDataType>(Text(element, "kind"))
            };
            if (Child(element, "assetIdentificationModelRef") is XElement assetIdentificationModel)
            {
                value = value with
                {
                    AssetIdentificationModel = AasOptional<AasReference>.Present(ReadReference(assetIdentificationModel))
                };
            }

            if (Child(element, "billOfMaterialRef") is XElement billOfMaterial)
            {
                value = value with { BillOfMaterial = AasOptional<AasReference>.Present(ReadReference(billOfMaterial)) };
            }

            asset = value;
            return true;
        }

        private static bool TryReadShell(
            XElement element,
            Dictionary<string, AasAsset> assetsById,
            out AasShell? shell,
            out string? error)
        {
            shell = null;
            if (!TryReadIdentifiable(element, out string idShort, out string category, out AasIdentifier identifier,
                out AasAdministrativeInformation administration, out error))
            {
                return false;
            }

            AasReference assetReference = Child(element, "assetRef") is XElement assetRef
                ? ReadReference(assetRef)
                : EmptyReference();
            var value = new AasShell
            {
                Identification = identifier,
                Administration = administration,
                IdShort = idShort,
                Category = category,
                Asset = ResolveAsset(assetReference, assetsById)
            };

            if (Child(element, "derivedFrom") is XElement derivedFrom)
            {
                value = value with { DerivedFrom = AasOptional<AasReference>.Present(ReadReference(derivedFrom)) };
            }

            if (Child(element, "submodelRefs") is XElement submodelRefs)
            {
                value = value with
                {
                    SubmodelReferences = AasOptional<ArrayOf<AasReference>>.Present(
                        ReadReferences(submodelRefs, "submodelRef"))
                };
            }

            if (Child(element, "views") is XElement views)
            {
                var items = new List<AasView>();
                foreach (XElement view in Children(views, "view"))
                {
                    items.Add(ReadView(view));
                }

                value = value with { Views = AasOptional<ArrayOf<AasView>>.Present(new(items.ToArray())) };
            }

            if (Child(element, "conceptDictionaries") is XElement dictionaries)
            {
                var items = new List<AasConceptDictionary>();
                foreach (XElement dictionary in Children(dictionaries, "conceptDictionary"))
                {
                    items.Add(ReadConceptDictionary(dictionary));
                }

                value = value with { ConceptDictionaries = AasOptional<ArrayOf<AasConceptDictionary>>.Present(new(items.ToArray())) };
            }

            shell = value;
            return true;
        }

        private static bool TryReadSubmodel(XElement element, out AasSubmodel? submodel, out string? error)
        {
            submodel = null;
            if (!TryReadIdentifiable(element, out string idShort, out string category, out AasIdentifier identifier,
                out AasAdministrativeInformation administration, out error))
            {
                return false;
            }

            var value = new AasSubmodel
            {
                Identification = identifier,
                Administration = administration,
                IdShort = idShort,
                Category = category,
                ModelingKind = ReadModelingKind(element)
            };

            if (Child(element, "submodelElements") is XElement submodelElements)
            {
                var items = new List<AasSubmodelElement>();
                foreach (XElement wrapper in Children(submodelElements, "submodelElement"))
                {
                    if (!TryReadSubmodelElement(wrapper, out AasSubmodelElement? item, out error) || item is null)
                    {
                        return false;
                    }

                    items.Add(item);
                }

                value = value with { SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(new(items.ToArray())) };
            }

            submodel = value;
            return true;
        }

        private static bool TryReadSubmodelElement(
            XElement wrapper,
            out AasSubmodelElement? value,
            out string? error)
        {
            XElement? element = FirstElement(wrapper);
            value = null;
            if (element is null)
            {
                error = "AAS V2 XML submodelElement did not contain an element payload.";
                return false;
            }

            error = null;
            switch (element.Name.LocalName)
            {
                case "property":
                    value = new AasProperty
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        ValueType = AasJsonReader.ParseValueType(Text(element, "valueType")),
                        Value = Child(element, "value") is XElement propertyValue
                            ? AasOptional<Variant>.Present(new Variant(propertyValue.Value))
                            : AasOptional<Variant>.Absent,
                        ValueId = Child(element, "valueId") is XElement propertyValueId
                            ? AasOptional<AasReference>.Present(ReadReference(propertyValueId))
                            : AasOptional<AasReference>.Absent
                    };
                    return true;
                case "range":
                    value = new AasRange
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        ValueType = AasJsonReader.ParseValueType(Text(element, "valueType")),
                        Min = Child(element, "min") is XElement min
                            ? AasOptional<Variant>.Present(new Variant(min.Value))
                            : AasOptional<Variant>.Absent,
                        Max = Child(element, "max") is XElement max
                            ? AasOptional<Variant>.Present(new Variant(max.Value))
                            : AasOptional<Variant>.Absent
                    };
                    return true;
                case "multiLanguageProperty":
                    value = new AasMultiLanguageProperty
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        Value = Child(element, "value") is XElement multiValue
                            ? AasOptional<ArrayOf<LocalizedText>>.Present(ReadLangStrings(multiValue))
                            : AasOptional<ArrayOf<LocalizedText>>.Absent,
                        ValueId = Child(element, "valueId") is XElement multiValueId
                            ? AasOptional<AasReference>.Present(ReadReference(multiValueId))
                            : AasOptional<AasReference>.Absent
                    };
                    return true;
                case "blob":
                    value = new AasBlob
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        File = Child(element, "value") is XElement blobValue
                            ? AasOptional<AasFileObject>.Present(new AasFileObject
                            {
                                Value = AasOptional<ByteString>.Present(ByteString.From(
                                    Convert.FromBase64String(blobValue.Value)))
                            })
                            : AasOptional<AasFileObject>.Absent
                    };
                    return true;
                case "file":
                    value = new AasFile
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        MimeType = Text(element, "mimeType"),
                        Value = Text(element, "value")
                    };
                    return true;
                case "referenceElement":
                    value = new AasReferenceElement
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        Value = Child(element, "value") is XElement referenceValue
                            ? ReadReference(referenceValue)
                            : EmptyReference()
                    };
                    return true;
                case "relationshipElement":
                    value = ReadRelationship(element, annotated: false);
                    return true;
                case "annotatedRelationshipElement":
                    value = ReadRelationship(element, annotated: true);
                    return true;
                case "submodelElementCollection":
                    value = ReadCollection(element);
                    return true;
                case "entity":
                    value = ReadEntity(element);
                    return true;
                case "event":
                case "basicEvent":
                    value = new AasEvent
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    return true;
                case "operation":
                    value = new AasOperation
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    return true;
                case "capability":
                    value = new AasCapability
                    {
                        IdShort = Text(element, "idShort"),
                        Category = Text(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    return true;
                default:
                    error = "Unsupported AAS V2 XML submodel element '" + element.Name.LocalName + "'.";
                    return false;
            }
        }

        private static AasRelationshipElementBase ReadRelationship(XElement element, bool annotated)
        {
            AasReference first = Child(element, "first") is XElement firstElement
                ? ReadReference(firstElement)
                : EmptyReference();
            AasReference second = Child(element, "second") is XElement secondElement
                ? ReadReference(secondElement)
                : EmptyReference();
            if (!annotated)
            {
                return new AasRelationshipElement
                {
                    IdShort = Text(element, "idShort"),
                    Category = Text(element, "category"),
                    ModelingKind = ReadModelingKind(element),
                    First = first,
                    Second = second
                };
            }

            var value = new AasAnnotatedRelationshipElement
            {
                IdShort = Text(element, "idShort"),
                Category = Text(element, "category"),
                ModelingKind = ReadModelingKind(element),
                First = first,
                Second = second
            };
            if (Child(element, "annotations") is XElement annotations)
            {
                var items = new List<AasSubmodelElement>();
                foreach (XElement dataElement in Children(annotations, "dataElement"))
                {
                    if (TryReadSubmodelElement(dataElement, out AasSubmodelElement? item, out _) && item is not null)
                    {
                        items.Add(item);
                    }
                }

                value = value with { DataElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(new(items.ToArray())) };
            }

            return value;
        }

        private static AasSubmodelElementCollectionBase ReadCollection(XElement element)
        {
            bool ordered = string.Equals(Text(element, "ordered"), "true", StringComparison.OrdinalIgnoreCase);
            AasSubmodelElementCollectionBase value = ordered
                ? new AasOrderedSubmodelElementCollection
                {
                    IdShort = Text(element, "idShort"),
                    Category = Text(element, "category"),
                    ModelingKind = ReadModelingKind(element)
                }
                : new AasSubmodelElementCollection
                {
                    IdShort = Text(element, "idShort"),
                    Category = Text(element, "category"),
                    ModelingKind = ReadModelingKind(element)
                };
            if (Child(element, "value") is XElement members)
            {
                var items = new List<AasSubmodelElement>();
                foreach (XElement wrapper in Children(members, "submodelElement"))
                {
                    if (TryReadSubmodelElement(wrapper, out AasSubmodelElement? item, out _) && item is not null)
                    {
                        items.Add(item);
                    }
                }

                value = value with { SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(new(items.ToArray())) };
            }

            if (Child(element, "allowDuplicates") is XElement allowDuplicates)
            {
                value = value with
                {
                    AllowDuplicates = AasOptional<bool>.Present(
                        string.Equals(allowDuplicates.Value, "true", StringComparison.OrdinalIgnoreCase))
                };
            }

            return value;
        }

        private static AasEntity ReadEntity(XElement element)
        {
            var value = new AasEntity
            {
                IdShort = Text(element, "idShort"),
                Category = Text(element, "category"),
                ModelingKind = ReadModelingKind(element),
                EntityType = AasJsonReader.ParseEnum<AASEntityTypeDataType>(Text(element, "entityType"))
            };
            if (Child(element, "assetRef") is XElement asset)
            {
                value = value with { Asset = AasOptional<AasReference>.Present(ReadReference(asset)) };
            }

            if (Child(element, "statements") is XElement statements)
            {
                var items = new List<AasSubmodelElement>();
                foreach (XElement wrapper in Children(statements, "submodelElement"))
                {
                    if (TryReadSubmodelElement(wrapper, out AasSubmodelElement? item, out _) && item is not null)
                    {
                        items.Add(item);
                    }
                }

                value = value with { Statements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(new(items.ToArray())) };
            }

            return value;
        }

        private static AasEnvironment ReadConceptDescriptions(XElement element, AasEnvironment current)
        {
            var custom = new List<AasCustomConceptDescription>();
            var irdi = new List<AasIrdiConceptDescription>();
            var iri = new List<AasIriConceptDescription>();
            foreach (XElement item in Children(element, "conceptDescription"))
            {
                if (!TryReadIdentifiable(item, out string idShort, out string category, out AasIdentifier identifier,
                    out AasAdministrativeInformation administration, out _))
                {
                    continue;
                }

                switch (identifier.IdType)
                {
                    case AASIdentifierTypeDataType.IRDI:
                        irdi.Add(new AasIrdiConceptDescription
                        {
                            Identification = identifier,
                            Administration = administration,
                            IdShort = idShort,
                            Category = category,
                            ConceptDescriptions = AasOptional<ArrayOf<AasReference>>.Present(
                                ReadReferences(item, "isCaseOf"))
                        });
                        break;
                    case AASIdentifierTypeDataType.IRI:
                        iri.Add(new AasIriConceptDescription
                        {
                            Identification = identifier,
                            Administration = administration,
                            IdShort = idShort,
                            Category = category,
                            ConceptDescriptions = AasOptional<ArrayOf<AasReference>>.Present(
                                ReadReferences(item, "isCaseOf"))
                        });
                        break;
                    default:
                        custom.Add(new AasCustomConceptDescription
                        {
                            Identification = identifier,
                            Administration = administration,
                            IdShort = idShort,
                            Category = category,
                            ConceptDescriptions = AasOptional<ArrayOf<AasReference>>.Present(
                                ReadReferences(item, "isCaseOf"))
                        });
                        break;
                }
            }

            return current with
            {
                CustomConceptDescriptions = AasOptional<ArrayOf<AasCustomConceptDescription>>.Present(new(custom.ToArray())),
                IrdiConceptDescriptions = AasOptional<ArrayOf<AasIrdiConceptDescription>>.Present(new(irdi.ToArray())),
                IriConceptDescriptions = AasOptional<ArrayOf<AasIriConceptDescription>>.Present(new(iri.ToArray()))
            };
        }

        private static AasView ReadView(XElement element)
        {
            var value = new AasView();
            if (Child(element, "containedElements") is XElement contained)
            {
                value = value with
                {
                    Referables = AasOptional<ArrayOf<AasReference>>.Present(
                        ReadReferences(contained, "containedElementRef"))
                };
            }

            return value;
        }

        private static AasConceptDictionary ReadConceptDictionary(XElement element)
        {
            var value = new AasConceptDictionary();
            if (Child(element, "conceptDescriptionRefs") is XElement refs)
            {
                value = value with
                {
                    ConceptDescriptions = AasOptional<ArrayOf<AasReference>>.Present(
                        ReadReferences(refs, "conceptDescriptionRef"))
                };
            }

            return value;
        }

        private static bool TryReadIdentifiable(
            XElement element,
            out string idShort,
            out string category,
            out AasIdentifier identifier,
            out AasAdministrativeInformation administration,
            out string? error)
        {
            idShort = Text(element, "idShort");
            category = Text(element, "category");
            administration = Child(element, "administration") is XElement admin
                ? new AasAdministrativeInformation { Version = Text(admin, "version"), Revision = Text(admin, "revision") }
                : new AasAdministrativeInformation { Version = string.Empty, Revision = string.Empty };
            XElement? identification = Child(element, "identification");
            if (identification is null)
            {
                identifier = new AasIdentifier { Id = string.Empty, IdType = AASIdentifierTypeDataType.Custom };
                error = "AAS V2.0.1 identifiable member '" + idShort + "' is missing identification idType.";
                return false;
            }

            identifier = new AasIdentifier
            {
                Id = identification.Value,
                IdType = AasJsonReader.ParseEnum<AASIdentifierTypeDataType>(
                    identification.Attribute("idType")?.Value ?? string.Empty)
            };
            error = null;
            return true;
        }

        private static AasReference ReadReference(XElement element)
        {
            XElement? keys = Child(element, "keys");
            if (keys is null)
            {
                return EmptyReference();
            }

            var items = new List<AASKeyDataType>();
            foreach (XElement key in Children(keys, "key"))
            {
                items.Add(new AASKeyDataType
                {
                    Type = AasJsonReader.ParseEnum<AASKeyElementsDataType>(key.Attribute("type")?.Value ?? string.Empty),
                    IdType = AasJsonReader.ParseEnum<AASKeyTypeDataType>(key.Attribute("idType")?.Value ?? string.Empty),
                    Local = string.Equals(key.Attribute("local")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                    Value = key.Value
                });
            }

            return new AasReference { Keys = new ArrayOf<AASKeyDataType>(items.ToArray()) };
        }

        private static ArrayOf<AasReference> ReadReferences(XElement element, string childName)
        {
            var items = new List<AasReference>();
            foreach (XElement child in Children(element, childName))
            {
                items.Add(ReadReference(child));
            }

            return new ArrayOf<AasReference>(items.ToArray());
        }

        private static ArrayOf<LocalizedText> ReadLangStrings(XElement element)
        {
            var items = new List<LocalizedText>();
            foreach (XElement langString in Children(element, "langString"))
            {
                items.Add(new LocalizedText(langString.Attribute("lang")?.Value ?? string.Empty, langString.Value));
            }

            return new ArrayOf<LocalizedText>(items.ToArray());
        }

        private static AASModelingKindDataType ReadModelingKind(XElement element)
        {
            string kind = Text(element, "kind");
            return string.IsNullOrEmpty(kind)
                ? AASModelingKindDataType.Instance
                : AasJsonReader.ParseEnum<AASModelingKindDataType>(kind);
        }

        private static AasAsset ResolveAsset(AasReference reference, Dictionary<string, AasAsset> assetsById)
        {
            foreach (AASKeyDataType key in reference.Keys)
            {
                if (key.Value is not null && assetsById.TryGetValue(key.Value, out AasAsset? asset))
                {
                    return asset;
                }
            }

            return new AasAsset
            {
                Identification = new AasIdentifier { Id = string.Empty, IdType = AASIdentifierTypeDataType.Custom },
                Administration = new AasAdministrativeInformation { Version = string.Empty, Revision = string.Empty },
                IdShort = string.Empty,
                Category = string.Empty,
                AssetKind = AASAssetKindDataType.Instance
            };
        }

        private static AasReference EmptyReference()
        {
            return new AasReference { Keys = ArrayOf<AASKeyDataType>.Empty };
        }

        private static XElement? Child(XElement element, string localName)
        {
            foreach (XElement child in element.Elements())
            {
                if (child.Name.LocalName == localName)
                {
                    return child;
                }
            }

            return null;
        }

        private static IEnumerable<XElement> Children(XElement element, string localName)
        {
            foreach (XElement child in element.Elements())
            {
                if (child.Name.LocalName == localName)
                {
                    yield return child;
                }
            }
        }

        private static XElement? FirstElement(XElement element)
        {
            foreach (XElement child in element.Elements())
            {
                return child;
            }

            return null;
        }

        private static string Text(XElement element, string localName)
        {
            return Child(element, localName)?.Value ?? string.Empty;
        }
    }
}
