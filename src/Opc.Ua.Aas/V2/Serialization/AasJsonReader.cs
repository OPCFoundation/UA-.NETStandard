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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.V2
{
    /// <summary>
    /// Reads AAS V2.0.1 JSON Environment documents.
    /// </summary>
    public sealed class AasJsonReader
    {
        /// <summary>
        /// Reads an AAS V2.0.1 JSON Environment document from a stream.
        /// </summary>
        /// <param name="stream">The UTF-8 JSON stream.</param>
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
                byte[] bytes = buffer.ToArray();
                AasV2JsonDocumentObject? _ = JsonSerializer.Deserialize(
                    bytes,
                    AasV2JsonSerializerContext.Default.AasV2JsonDocumentObject);
                using JsonDocument document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return AasDocumentReadResult.Failure("The AAS V2 JSON document root is not an object.");
                }

                if (!TryReadEnvironment(document.RootElement, out AasEnvironment? environment, out string? error))
                {
                    return AasDocumentReadResult.Failure(error ?? "The AAS V2 JSON document is malformed.");
                }

                return AasDocumentReadResult.Success(environment);
            }
            catch (JsonException ex)
            {
                return AasDocumentReadResult.Failure("The AAS V2 JSON document is malformed: " + ex.Message);
            }
            catch (FormatException ex)
            {
                return AasDocumentReadResult.Failure("The AAS V2 JSON document is malformed: " + ex.Message);
            }
        }

        internal static bool TryReadEnvironment(
            JsonElement element,
            out AasEnvironment environment,
            out string? error)
        {
            error = null;
            var value = new AasEnvironment();

            if (LooksLikeV3(element))
            {
                environment = value;
                error = "The document looks like AAS V3; AAS V2.0.1 JSON requires identification.idType.";
                return false;
            }

            var assetsById = new Dictionary<string, AasAsset>(StringComparer.Ordinal);
            if (element.TryGetProperty("assets", out JsonElement assets))
            {
                if (!TryReadArray(assets, TryReadAsset, out ArrayOf<AasAsset> items, out error))
                {
                    environment = value;
                    return false;
                }

                value = value with { Assets = AasOptional<ArrayOf<AasAsset>>.Present(items) };
                foreach (AasAsset asset in items)
                {
                    assetsById[asset.Identification.Id] = asset;
                    assetsById[asset.IdShort] = asset;
                }
            }

            if (element.TryGetProperty("assetAdministrationShells", out JsonElement shells))
            {
                if (!TryReadArray(shells, (JsonElement item, out AasShell? shell, out string? readError) =>
                    TryReadShell(item, assetsById, out shell, out readError), out ArrayOf<AasShell> items, out error))
                {
                    environment = value;
                    return false;
                }

                value = value with { AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(items) };
            }

            if (element.TryGetProperty("submodels", out JsonElement submodels))
            {
                if (!TryReadArray(submodels, TryReadSubmodel, out ArrayOf<AasSubmodel> items, out error))
                {
                    environment = value;
                    return false;
                }

                value = value with { Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(items) };
            }

            if (element.TryGetProperty("conceptDescriptions", out JsonElement concepts) &&
                !TryReadConceptDescriptions(concepts, out value, value, out error))
            {
                environment = value;
                return false;
            }

            environment = value;
            return true;
        }

        internal static AASValueTypeDataType ParseValueType(string text)
        {
            switch (text)
            {
                case "boolean":
                    return AASValueTypeDataType.Boolean;
                case "byte":
                    return AASValueTypeDataType.SByte;
                case "unsignedByte":
                    return AASValueTypeDataType.Byte;
                case "short":
                    return AASValueTypeDataType.Int16;
                case "unsignedShort":
                    return AASValueTypeDataType.UInt16;
                case "int":
                case "integer":
                    return AASValueTypeDataType.Int32;
                case "unsignedInt":
                    return AASValueTypeDataType.UInt32;
                case "long":
                    return AASValueTypeDataType.Int64;
                case "unsignedLong":
                    return AASValueTypeDataType.UInt64;
                case "float":
                    return AASValueTypeDataType.Float;
                case "double":
                case "decimal":
                    return AASValueTypeDataType.Double;
                case "dateTime":
                case "dateTimeStamp":
                    return AASValueTypeDataType.DateTime;
                case "base64Binary":
                case "hexBinary":
                    return AASValueTypeDataType.ByteString;
                default:
                    return AASValueTypeDataType.String;
            }
        }

        internal static T ParseEnum<T>(string text)
            where T : struct
        {
            if (Enum.TryParse(NormalizeEnumName(text), true, out T value))
            {
                return value;
            }

            return default;
        }

        private static bool TryReadAsset(JsonElement element, out AasAsset? asset, out string? error)
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
                AssetKind = ParseEnum<AASAssetKindDataType>(MemberString(element, "kind"))
            };

            if (element.TryGetProperty("assetIdentificationModel", out JsonElement identificationModel))
            {
                value = value with
                {
                    AssetIdentificationModel = AasOptional<AasReference>.Present(ReadReference(identificationModel))
                };
            }

            if (element.TryGetProperty("billOfMaterial", out JsonElement billOfMaterial))
            {
                value = value with { BillOfMaterial = AasOptional<AasReference>.Present(ReadReference(billOfMaterial)) };
            }

            value = value with { DataSpecifications = ReadDataSpecifications(element) };
            asset = value;
            return true;
        }

        private static bool TryReadShell(
            JsonElement element,
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

            AasReference assetReference = element.TryGetProperty("asset", out JsonElement assetElement)
                ? ReadReference(assetElement)
                : EmptyReference();
            AasAsset asset = ResolveAsset(assetReference, assetsById);
            var value = new AasShell
            {
                Identification = identifier,
                Administration = administration,
                IdShort = idShort,
                Category = category,
                Asset = asset
            };

            if (element.TryGetProperty("derivedFrom", out JsonElement derivedFrom))
            {
                value = value with { DerivedFrom = AasOptional<AasReference>.Present(ReadReference(derivedFrom)) };
            }

            if (element.TryGetProperty("submodels", out JsonElement submodels))
            {
                value = value with
                {
                    SubmodelReferences = AasOptional<ArrayOf<AasReference>>.Present(ReadReferences(submodels))
                };
            }

            if (element.TryGetProperty("views", out JsonElement views))
            {
                if (!TryReadArray(views, TryReadView, out ArrayOf<AasView> viewItems, out error))
                {
                    return false;
                }

                value = value with { Views = AasOptional<ArrayOf<AasView>>.Present(viewItems) };
            }

            if (element.TryGetProperty("conceptDictionaries", out JsonElement dictionaries))
            {
                if (!TryReadArray(dictionaries, TryReadConceptDictionary, out ArrayOf<AasConceptDictionary> items,
                    out error))
                {
                    return false;
                }

                value = value with { ConceptDictionaries = AasOptional<ArrayOf<AasConceptDictionary>>.Present(items) };
            }

            value = value with { DataSpecifications = ReadDataSpecifications(element) };
            shell = value;
            return true;
        }

        private static bool TryReadSubmodel(JsonElement element, out AasSubmodel? submodel, out string? error)
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
                ModelingKind = ParseEnum<AASModelingKindDataType>(MemberString(element, "kind"))
            };

            if (element.TryGetProperty("submodelElements", out JsonElement submodelElements))
            {
                if (!TryReadArray(submodelElements, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items,
                    out error))
                {
                    return false;
                }

                value = value with { SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
            }

            value = value with
            {
                Qualifiers = ReadQualifiers(element),
                DataSpecifications = ReadDataSpecifications(element)
            };
            submodel = value;
            return true;
        }

        private static bool TryReadSubmodelElement(
            JsonElement element,
            out AasSubmodelElement? value,
            out string? error)
        {
            value = null;
            error = null;
            string modelType = ReadModelType(element);
            AasSubmodelElement result;
            switch (modelType)
            {
                case "Property":
                    var property = new AasProperty
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        ValueType = ParseValueType(MemberString(element, "valueType"))
                    };
                    if (element.TryGetProperty("value", out JsonElement propertyValue))
                    {
                        property = property with
                        {
                            Value = AasOptional<Variant>.Present(new Variant(propertyValue.GetString() ?? string.Empty))
                        };
                    }

                    if (element.TryGetProperty("valueId", out JsonElement propertyValueId))
                    {
                        property = property with
                        {
                            ValueId = AasOptional<AasReference>.Present(ReadReference(propertyValueId))
                        };
                    }

                    result = property;
                    break;
                case "Range":
                    var range = new AasRange
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        ValueType = ParseValueType(MemberString(element, "valueType"))
                    };
                    if (element.TryGetProperty("min", out JsonElement min))
                    {
                        range = range with { Min = AasOptional<Variant>.Present(new Variant(min.GetString() ?? string.Empty)) };
                    }

                    if (element.TryGetProperty("max", out JsonElement max))
                    {
                        range = range with { Max = AasOptional<Variant>.Present(new Variant(max.GetString() ?? string.Empty)) };
                    }

                    result = range;
                    break;
                case "MultiLanguageProperty":
                    var multi = new AasMultiLanguageProperty
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    if (element.TryGetProperty("value", out JsonElement multiValue))
                    {
                        multi = multi with
                        {
                            Value = AasOptional<ArrayOf<LocalizedText>>.Present(ReadLangStrings(multiValue))
                        };
                    }

                    if (element.TryGetProperty("valueId", out JsonElement multiValueId))
                    {
                        multi = multi with { ValueId = AasOptional<AasReference>.Present(ReadReference(multiValueId)) };
                    }

                    result = multi;
                    break;
                case "Blob":
                    var blob = new AasBlob
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    if (element.TryGetProperty("value", out JsonElement blobValue))
                    {
                        blob = blob with
                        {
                            File = AasOptional<AasFileObject>.Present(new AasFileObject
                            {
                                Value = AasOptional<ByteString>.Present(ReadByteString(blobValue.GetString()))
                            })
                        };
                    }

                    result = blob;
                    break;
                case "File":
                    result = new AasFile
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        MimeType = MemberString(element, "mimeType"),
                        Value = MemberString(element, "value")
                    };
                    break;
                case "ReferenceElement":
                    result = new AasReferenceElement
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element),
                        Value = element.TryGetProperty("value", out JsonElement referenceValue)
                            ? ReadReference(referenceValue)
                            : EmptyReference()
                    };
                    break;
                case "RelationshipElement":
                    result = ReadRelationship(element, annotated: false);
                    break;
                case "AnnotatedRelationshipElement":
                    result = ReadRelationship(element, annotated: true);
                    break;
                case "SubmodelElementCollection":
                    result = ReadCollection(element);
                    break;
                case "Entity":
                    result = ReadEntity(element);
                    break;
                case "BasicEvent":
                case "Event":
                    result = new AasEvent
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    break;
                case "Operation":
                    result = new AasOperation
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    break;
                case "Capability":
                    result = new AasCapability
                    {
                        IdShort = RequiredIdShort(element),
                        Category = MemberString(element, "category"),
                        ModelingKind = ReadModelingKind(element)
                    };
                    break;
                default:
                    error = "Unsupported AAS V2 submodel element modelType '" + modelType + "'.";
                    return false;
            }

            result = result with
            {
                Qualifiers = ReadQualifiers(element),
                DataSpecifications = ReadDataSpecifications(element)
            };
            value = result;
            return true;
        }

        private static AasSubmodelElement ReadRelationship(JsonElement element, bool annotated)
        {
            AasReference first = element.TryGetProperty("first", out JsonElement firstElement)
                ? ReadReference(firstElement)
                : EmptyReference();
            AasReference second = element.TryGetProperty("second", out JsonElement secondElement)
                ? ReadReference(secondElement)
                : EmptyReference();
            if (!annotated)
            {
                return new AasRelationshipElement
                {
                    IdShort = RequiredIdShort(element),
                    Category = MemberString(element, "category"),
                    ModelingKind = ReadModelingKind(element),
                    First = first,
                    Second = second
                };
            }

            var value = new AasAnnotatedRelationshipElement
            {
                IdShort = RequiredIdShort(element),
                Category = MemberString(element, "category"),
                ModelingKind = ReadModelingKind(element),
                First = first,
                Second = second
            };
            if (element.TryGetProperty("annotation", out JsonElement annotation) ||
                element.TryGetProperty("annotations", out annotation))
            {
                TryReadArray(annotation, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> dataElements, out _);
                value = value with { DataElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(dataElements) };
            }

            return value;
        }

        private static AasSubmodelElementCollectionBase ReadCollection(JsonElement element)
        {
            bool ordered = element.TryGetProperty("ordered", out JsonElement orderedElement) &&
                orderedElement.ValueKind == JsonValueKind.True;
            AasSubmodelElementCollectionBase value = ordered
                ? new AasOrderedSubmodelElementCollection
                {
                    IdShort = RequiredIdShort(element),
                    Category = MemberString(element, "category"),
                    ModelingKind = ReadModelingKind(element)
                }
                : new AasSubmodelElementCollection
                {
                    IdShort = RequiredIdShort(element),
                    Category = MemberString(element, "category"),
                    ModelingKind = ReadModelingKind(element)
                };
            if (element.TryGetProperty("value", out JsonElement members))
            {
                TryReadArray(members, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items, out _);
                value = value with { SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
            }

            if (element.TryGetProperty("allowDuplicates", out JsonElement allowDuplicates))
            {
                value = value with { AllowDuplicates = AasOptional<bool>.Present(allowDuplicates.GetBoolean()) };
            }

            return value;
        }

        private static AasEntity ReadEntity(JsonElement element)
        {
            var value = new AasEntity
            {
                IdShort = RequiredIdShort(element),
                Category = MemberString(element, "category"),
                ModelingKind = ReadModelingKind(element),
                EntityType = ParseEnum<AASEntityTypeDataType>(MemberString(element, "entityType"))
            };
            if (element.TryGetProperty("asset", out JsonElement asset))
            {
                value = value with { Asset = AasOptional<AasReference>.Present(ReadReference(asset)) };
            }

            if (element.TryGetProperty("statements", out JsonElement statements))
            {
                TryReadArray(statements, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items, out _);
                value = value with { Statements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
            }

            return value;
        }

        private static bool TryReadConceptDescriptions(
            JsonElement element,
            out AasEnvironment environment,
            AasEnvironment current,
            out string? error)
        {
            environment = current;
            error = null;
            if (element.ValueKind != JsonValueKind.Array)
            {
                error = "Expected a JSON array.";
                return false;
            }

            var custom = new List<AasCustomConceptDescription>();
            var irdi = new List<AasIrdiConceptDescription>();
            var iri = new List<AasIriConceptDescription>();
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!TryReadIdentifiable(item, out string idShort, out string category, out AasIdentifier identifier,
                    out AasAdministrativeInformation administration, out error))
                {
                    return false;
                }

                switch (identifier.IdType)
                {
                    case AASIdentifierTypeDataType.IRDI:
                        var irdiConcept = new AasIrdiConceptDescription
                        {
                            Identification = identifier,
                            Administration = administration,
                            IdShort = idShort,
                            Category = category
                        };
                        irdiConcept = irdiConcept with
                        {
                            ConceptDescriptions = ReadConceptDescriptions(item),
                            DataSpecifications = ReadDataSpecifications(item)
                        };
                        irdi.Add(irdiConcept);
                        break;
                    case AASIdentifierTypeDataType.IRI:
                        var iriConcept = new AasIriConceptDescription
                        {
                            Identification = identifier,
                            Administration = administration,
                            IdShort = idShort,
                            Category = category
                        };
                        iriConcept = iriConcept with
                        {
                            ConceptDescriptions = ReadConceptDescriptions(item),
                            DataSpecifications = ReadDataSpecifications(item)
                        };
                        iri.Add(iriConcept);
                        break;
                    default:
                        var customConcept = new AasCustomConceptDescription
                        {
                            Identification = identifier,
                            Administration = administration,
                            IdShort = idShort,
                            Category = category
                        };
                        customConcept = customConcept with
                        {
                            ConceptDescriptions = ReadConceptDescriptions(item),
                            DataSpecifications = ReadDataSpecifications(item)
                        };
                        custom.Add(customConcept);
                        break;
                }
            }

            environment = current with
            {
                CustomConceptDescriptions = AasOptional<ArrayOf<AasCustomConceptDescription>>.Present(
                    new ArrayOf<AasCustomConceptDescription>(custom.ToArray())),
                IrdiConceptDescriptions = AasOptional<ArrayOf<AasIrdiConceptDescription>>.Present(
                    new ArrayOf<AasIrdiConceptDescription>(irdi.ToArray())),
                IriConceptDescriptions = AasOptional<ArrayOf<AasIriConceptDescription>>.Present(
                    new ArrayOf<AasIriConceptDescription>(iri.ToArray()))
            };
            return true;
        }

        private static bool TryReadView(JsonElement element, out AasView? view, out string? error)
        {
            error = null;
            var value = new AasView();
            if (element.TryGetProperty("containedElements", out JsonElement contained))
            {
                value = value with { Referables = AasOptional<ArrayOf<AasReference>>.Present(ReadReferences(contained)) };
            }

            value = value with { DataSpecifications = ReadDataSpecifications(element) };
            view = value;
            return true;
        }

        private static bool TryReadConceptDictionary(
            JsonElement element,
            out AasConceptDictionary? dictionary,
            out string? error)
        {
            error = null;
            var value = new AasConceptDictionary();
            if (element.TryGetProperty("conceptDescriptions", out JsonElement concepts))
            {
                value = value with
                {
                    ConceptDescriptions = AasOptional<ArrayOf<AasReference>>.Present(ReadReferences(concepts))
                };
            }

            dictionary = value;
            return true;
        }

        private static bool TryReadIdentifiable(
            JsonElement element,
            out string idShort,
            out string category,
            out AasIdentifier identifier,
            out AasAdministrativeInformation administration,
            out string? error)
        {
            idShort = RequiredIdShort(element);
            category = MemberString(element, "category");
            administration = ReadAdministration(element);
            if (!element.TryGetProperty("identification", out JsonElement identification))
            {
                identifier = new AasIdentifier { Id = string.Empty, IdType = AASIdentifierTypeDataType.Custom };
                error = "AAS V2.0.1 identifiable member '" + idShort + "' is missing identification.idType.";
                return false;
            }

            identifier = new AasIdentifier
            {
                Id = MemberString(identification, "id"),
                IdType = ParseEnum<AASIdentifierTypeDataType>(MemberString(identification, "idType"))
            };
            error = null;
            return true;
        }

        private static AasAdministrativeInformation ReadAdministration(JsonElement element)
        {
            if (!element.TryGetProperty("administration", out JsonElement administration))
            {
                return new AasAdministrativeInformation { Version = string.Empty, Revision = string.Empty };
            }

            return new AasAdministrativeInformation
            {
                Version = MemberString(administration, "version"),
                Revision = MemberString(administration, "revision")
            };
        }

        private static AasReference ReadReference(JsonElement element)
        {
            if (element.TryGetProperty("keys", out JsonElement keys) && keys.ValueKind == JsonValueKind.Array)
            {
                var items = new List<AASKeyDataType>();
                foreach (JsonElement key in keys.EnumerateArray())
                {
                    items.Add(new AASKeyDataType
                    {
                        Type = ParseEnum<AASKeyElementsDataType>(MemberString(key, "type")),
                        IdType = ParseEnum<AASKeyTypeDataType>(MemberString(key, "idType")),
                        Value = MemberString(key, "value"),
                        Local = key.TryGetProperty("local", out JsonElement local) && local.GetBoolean()
                    });
                }

                return new AasReference { Keys = new ArrayOf<AASKeyDataType>(items.ToArray()) };
            }

            return EmptyReference();
        }

        private static ArrayOf<AasReference> ReadReferences(JsonElement element)
        {
            var items = new List<AasReference>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(ReadReference(item));
                }
            }

            return new ArrayOf<AasReference>(items.ToArray());
        }

        private static ArrayOf<LocalizedText> ReadLangStrings(JsonElement element)
        {
            var items = new List<LocalizedText>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(new LocalizedText(MemberString(item, "language"), MemberString(item, "text")));
                }
            }

            return new ArrayOf<LocalizedText>(items.ToArray());
        }

        private static bool TryReadArray<T>(
            JsonElement element,
            TryReadElement<T> reader,
            out ArrayOf<T> values,
            out string? error)
            where T : class
        {
            values = ArrayOf<T>.Empty;
            error = null;
            if (element.ValueKind != JsonValueKind.Array)
            {
                error = "Expected a JSON array.";
                return false;
            }

            var items = new List<T>();
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!reader(item, out T? value, out error) || value is null)
                {
                    return false;
                }

                items.Add(value);
            }

            values = new ArrayOf<T>(items.ToArray());
            return true;
        }

        private static AasOptional<ArrayOf<AasQualifier>> ReadQualifiers(JsonElement element)
        {
            if (!element.TryGetProperty("qualifiers", out JsonElement qualifiers) ||
                qualifiers.ValueKind != JsonValueKind.Array)
            {
                return AasOptional<ArrayOf<AasQualifier>>.Absent;
            }

            var items = new List<AasQualifier>();
            foreach (JsonElement item in qualifiers.EnumerateArray())
            {
                items.Add(new AasQualifier
                {
                    Type = MemberString(item, "type"),
                    ValueType = ParseValueType(MemberString(item, "valueType")),
                    Value = item.TryGetProperty("value", out JsonElement qualifierValue)
                        ? AasOptional<Variant>.Present(new Variant(qualifierValue.GetString() ?? string.Empty))
                        : AasOptional<Variant>.Absent,
                    ValueId = item.TryGetProperty("valueId", out JsonElement valueId)
                        ? AasOptional<AasReference>.Present(ReadReference(valueId))
                        : AasOptional<AasReference>.Absent
                });
            }

            return AasOptional<ArrayOf<AasQualifier>>.Present(new ArrayOf<AasQualifier>(items.ToArray()));
        }

        private static AasOptional<ArrayOf<AasReference>> ReadDataSpecifications(JsonElement element)
        {
            if (!element.TryGetProperty("embeddedDataSpecifications", out JsonElement specifications) ||
                specifications.ValueKind != JsonValueKind.Array)
            {
                return AasOptional<ArrayOf<AasReference>>.Absent;
            }

            var items = new List<AasReference>();
            foreach (JsonElement item in specifications.EnumerateArray())
            {
                if (item.TryGetProperty("dataSpecification", out JsonElement reference))
                {
                    items.Add(ReadReference(reference));
                }
            }

            return AasOptional<ArrayOf<AasReference>>.Present(new ArrayOf<AasReference>(items.ToArray()));
        }

        private static AasOptional<ArrayOf<AasReference>> ReadConceptDescriptions(JsonElement element)
        {
            if (element.TryGetProperty("isCaseOf", out JsonElement isCaseOf))
            {
                return AasOptional<ArrayOf<AasReference>>.Present(ReadReferences(isCaseOf));
            }

            return AasOptional<ArrayOf<AasReference>>.Absent;
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

        private static AASModelingKindDataType ReadModelingKind(JsonElement element)
        {
            string kind = MemberString(element, "kind");
            return string.IsNullOrEmpty(kind) ? AASModelingKindDataType.Instance : ParseEnum<AASModelingKindDataType>(kind);
        }

        private static string RequiredIdShort(JsonElement element)
        {
            return MemberString(element, "idShort");
        }

        private static string ReadModelType(JsonElement element)
        {
            if (!element.TryGetProperty("modelType", out JsonElement modelType))
            {
                return string.Empty;
            }

            if (modelType.ValueKind == JsonValueKind.String)
            {
                return modelType.GetString() ?? string.Empty;
            }

            return MemberString(modelType, "name");
        }

        private static string MemberString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement property) ? property.GetString() ?? string.Empty : string.Empty;
        }

        private static ByteString ReadByteString(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return ByteString.Empty;
            }

            return ByteString.From(Convert.FromBase64String(text));
        }

        private static bool LooksLikeV3(JsonElement element)
        {
            if (!element.TryGetProperty("submodels", out JsonElement submodels) ||
                submodels.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement submodel in submodels.EnumerateArray())
            {
                if (submodel.TryGetProperty("id", out _) && !submodel.TryGetProperty("identification", out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeEnumName(string text)
        {
            return text == "TIME_STAMP" ? "TIME_STAMP" : text.Replace("_", string.Empty, StringComparison.Ordinal);
        }

        private delegate bool TryReadElement<T>(JsonElement element, out T? value, out string? error)
            where T : class;
    }
}
