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
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Reads AAS V3 JSON Environment documents.
    /// </summary>
    public sealed class AasJsonReader
    {
        /// <summary>
        /// Reads an AAS JSON Environment document from a stream.
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
                AasJsonDocumentObject? _ = JsonSerializer.Deserialize(
                    bytes,
                    AasJsonSerializerContext.Default.AasJsonDocumentObject);
                using JsonDocument document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return AasDocumentReadResult.Failure("The AAS JSON document root is not an object.");
                }

                if (!TryReadEnvironment(document.RootElement, out AasEnvironment? environment, out string? error))
                {
                    return AasDocumentReadResult.Failure(error ?? "The AAS JSON document is malformed.");
                }

                if (environment is null)
                {
                    return AasDocumentReadResult.Failure("The AAS JSON document did not contain an environment.");
                }

                return AasDocumentReadResult.Success(environment);
            }
            catch (JsonException ex)
            {
                return AasDocumentReadResult.Failure("The AAS JSON document is malformed: " + ex.Message);
            }
            catch (FormatException ex)
            {
                return AasDocumentReadResult.Failure("The AAS JSON document is malformed: " + ex.Message);
            }
        }

        internal static bool TryReadEnvironment(
            JsonElement element,
            out AasEnvironment? environment,
            out string? error)
        {
            environment = null;
            error = null;
            var value = new AasEnvironment();

            if (element.TryGetProperty("assetAdministrationShells", out JsonElement shells))
            {
                if (!TryReadArray(shells, TryReadShell, out ArrayOf<AasShell> items, out error))
                {
                    return false;
                }

                value = value with { AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(items) };
            }

            if (element.TryGetProperty("submodels", out JsonElement submodels))
            {
                if (!TryReadArray(submodels, TryReadSubmodel, out ArrayOf<AasSubmodel> items, out error))
                {
                    return false;
                }

                value = value with { Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(items) };
            }

            if (element.TryGetProperty("conceptDescriptions", out JsonElement concepts))
            {
                if (!TryReadArray(concepts, TryReadConceptDescription, out ArrayOf<AasConceptDescription> items,
                    out error))
                {
                    return false;
                }

                value = value with { ConceptDescriptions = AasOptional<ArrayOf<AasConceptDescription>>.Present(items) };
            }

            environment = value;
            return true;
        }

        internal static AASDataTypeDefXsdDataType ParseValueType(string text)
        {
            switch (text)
            {
                case "xs:anyURI":
                    return AASDataTypeDefXsdDataType.AnyUri;
                case "xs:base64Binary":
                    return AASDataTypeDefXsdDataType.Base64Binary;
                case "xs:dateTime":
                    return AASDataTypeDefXsdDataType.DateTime;
                case "xs:gDay":
                    return AASDataTypeDefXsdDataType.GDay;
                case "xs:gMonth":
                    return AASDataTypeDefXsdDataType.GMonth;
                case "xs:gMonthDay":
                    return AASDataTypeDefXsdDataType.GMonthDay;
                case "xs:gYear":
                    return AASDataTypeDefXsdDataType.GYear;
                case "xs:gYearMonth":
                    return AASDataTypeDefXsdDataType.GYearMonth;
                case "xs:hexBinary":
                    return AASDataTypeDefXsdDataType.HexBinary;
                default:
                    string name = text.StartsWith("xs:", StringComparison.Ordinal) ? text.Substring(3) : text;
                    return ParseEnum<AASDataTypeDefXsdDataType>(name);
            }
        }

        internal static string FormatValueType(AASDataTypeDefXsdDataType value)
        {
            switch (value)
            {
                case AASDataTypeDefXsdDataType.AnyUri:
                    return "xs:anyURI";
                case AASDataTypeDefXsdDataType.Base64Binary:
                    return "xs:base64Binary";
                case AASDataTypeDefXsdDataType.DateTime:
                    return "xs:dateTime";
                case AASDataTypeDefXsdDataType.GDay:
                    return "xs:gDay";
                case AASDataTypeDefXsdDataType.GMonth:
                    return "xs:gMonth";
                case AASDataTypeDefXsdDataType.GMonthDay:
                    return "xs:gMonthDay";
                case AASDataTypeDefXsdDataType.GYear:
                    return "xs:gYear";
                case AASDataTypeDefXsdDataType.GYearMonth:
                    return "xs:gYearMonth";
                case AASDataTypeDefXsdDataType.HexBinary:
                    return "xs:hexBinary";
                default:
                    string name = value.ToString();
                    return "xs:" + char.ToLowerInvariant(name[0]) + name.Substring(1);
            }
        }

        internal static T ParseEnum<T>(string text)
            where T : struct
        {
            if (Enum.TryParse(UpperFirst(text), true, out T value))
            {
                return value;
            }

            return default;
        }

        internal static string FormatEnum<T>(T value)
            where T : struct
        {
            string? formatted = value.ToString();
            string text = formatted ?? string.Empty;
            if (text.Length == 0)
            {
                return string.Empty;
            }

            if (typeof(T) == typeof(AASDirectionDataType) ||
                typeof(T) == typeof(AASStateOfEventDataType))
            {
                return char.ToLowerInvariant(text[0]) + text.Substring(1);
            }

            return text;
        }

        private static bool TryReadShell(JsonElement element, out AasShell? shell, out string? error)
        {
            shell = null;
            if (!RequiredString(element, "id", out string id, out error))
            {
                return false;
            }

            AasAssetInformation asset = new() { AssetKind = AASAssetKindDataType.Instance };
            if (element.TryGetProperty("assetInformation", out JsonElement assetElement))
            {
                if (assetElement.TryGetProperty("assetKind", out JsonElement kind))
                {
                    asset = asset with { AssetKind = ParseEnum<AASAssetKindDataType>(kind.GetString() ?? string.Empty) };
                }

                if (assetElement.TryGetProperty("specificAssetIds", out JsonElement ids))
                {
                    asset = asset with
                    {
                        SpecificAssetIds = AasOptional<ArrayOf<AASSpecificAssetIdDataType>>.Present(
                            ReadSpecificAssetIds(ids))
                    };
                }
            }

            var value = new AasShell { Id = id, AssetInformation = asset };
            ReadReferable(element, ref value);
            shell = value;
            return true;
        }

        private static bool TryReadSubmodel(JsonElement element, out AasSubmodel? submodel, out string? error)
        {
            submodel = null;
            if (!RequiredString(element, "id", out string id, out error))
            {
                return false;
            }

            var value = new AasSubmodel { Id = id };
            ReadReferable(element, ref value);
            if (element.TryGetProperty("submodelElements", out JsonElement submodelElements))
            {
                if (!TryReadArray(submodelElements, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items,
                    out error))
                {
                    return false;
                }

                value = value with { SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
            }

            if (element.TryGetProperty("qualifiers", out JsonElement qualifiers))
            {
                value = value with
                {
                    Qualifiers = AasOptional<ArrayOf<AASQualifierDataType>>.Present(ReadQualifiers(qualifiers))
                };
            }

            submodel = value;
            return true;
        }

        private static bool TryReadConceptDescription(
            JsonElement element,
            out AasConceptDescription? concept,
            out string? error)
        {
            concept = null;
            if (!RequiredString(element, "id", out string id, out error))
            {
                return false;
            }

            var value = new AasConceptDescription { Id = id };
            ReadReferable(element, ref value);
            concept = value;
            return true;
        }

        private static bool TryReadSubmodelElement(
            JsonElement element,
            out AasSubmodelElement? value,
            out string? error)
        {
            value = null;
            if (!RequiredString(element, "modelType", out string modelType, out error))
            {
                return false;
            }

            AasSubmodelElement result;
            switch (modelType)
            {
                case "Property":
                    AASDataTypeDefXsdDataType propertyType = ParseValueType(MemberString(element, "valueType"));
                    var property = new AasProperty { ValueType = propertyType };
                    if (element.TryGetProperty("value", out JsonElement propertyValue))
                    {
                        string lexical = propertyValue.GetString() ?? string.Empty;
                        if (!AasLexicalCanonicalizer.TryParse(lexical, propertyType, out _, out error))
                        {
                            return false;
                        }

                        property = property with { Value = AasOptional<Variant>.Present(new Variant(lexical)) };
                    }

                    result = property;
                    break;
                case "Range":
                    AASDataTypeDefXsdDataType rangeType = ParseValueType(MemberString(element, "valueType"));
                    var range = new AasRange { ValueType = rangeType };
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
                    var multi = new AasMultiLanguageProperty();
                    if (element.TryGetProperty("value", out JsonElement multiValue))
                    {
                        multi = multi with
                        {
                            Value = AasOptional<ArrayOf<AASLangStringDataType>>.Present(ReadLangStrings(multiValue))
                        };
                    }

                    result = multi;
                    break;
                case "Blob":
                    result = new AasBlob { ContentType = MemberString(element, "contentType") };
                    break;
                case "File":
                    result = new AasFile { ContentType = MemberString(element, "contentType") };
                    break;
                case "ReferenceElement":
                    var referenceElement = new AasReferenceElement();
                    if (element.TryGetProperty("value", out JsonElement referenceValue))
                    {
                        referenceElement = referenceElement with
                        {
                            Value = AasOptional<AASReferenceDataType>.Present(ReadReference(referenceValue))
                        };
                    }

                    result = referenceElement;
                    break;
                case "RelationshipElement":
                    result = new AasRelationshipElement
                    {
                        First = ReadReference(element.GetProperty("first")),
                        Second = ReadReference(element.GetProperty("second"))
                    };
                    break;
                case "AnnotatedRelationshipElement":
                    var annotated = new AasAnnotatedRelationshipElement
                    {
                        First = ReadReference(element.GetProperty("first")),
                        Second = ReadReference(element.GetProperty("second"))
                    };
                    if (element.TryGetProperty("annotations", out JsonElement annotations))
                    {
                        TryReadArray(annotations, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items, out _);
                        annotated = annotated with { Annotations = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
                    }

                    result = annotated;
                    break;
                case "SubmodelElementCollection":
                    var collection = new AasSubmodelElementCollection();
                    if (element.TryGetProperty("value", out JsonElement collectionValue))
                    {
                        TryReadArray(collectionValue, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items, out _);
                        collection = collection with { Value = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
                    }

                    result = collection;
                    break;
                case "SubmodelElementList":
                    var list = new AasSubmodelElementList
                    {
                        TypeValueListElement = ParseEnum<AASSubmodelElementsDataType>(
                            MemberString(element, "typeValueListElement"))
                    };
                    if (element.TryGetProperty("orderRelevant", out JsonElement orderRelevant))
                    {
                        list = list with { OrderRelevant = AasOptional<bool>.Present(orderRelevant.GetBoolean()) };
                    }

                    if (element.TryGetProperty("value", out JsonElement listValue))
                    {
                        TryReadArray(listValue, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items, out _);
                        list = list with { Value = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
                    }

                    result = list;
                    break;
                case "Entity":
                    var entity = new AasEntity
                    {
                        EntityType = ParseEnum<AASEntityTypeDataType>(MemberString(element, "entityType"))
                    };
                    if (element.TryGetProperty("statements", out JsonElement statements))
                    {
                        TryReadArray(statements, TryReadSubmodelElement, out ArrayOf<AasSubmodelElement> items, out _);
                        entity = entity with { Statements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(items) };
                    }

                    result = entity;
                    break;
                case "BasicEventElement":
                    result = new AasBasicEventElement
                    {
                        Observed = element.TryGetProperty("observed", out JsonElement observed)
                            ? ReadReference(observed)
                            : EmptyReference(),
                        Direction = ParseEnum<AASDirectionDataType>(MemberString(element, "direction")),
                        State = ParseEnum<AASStateOfEventDataType>(MemberString(element, "state"))
                    };
                    break;
                case "Operation":
                    var operation = new AasOperation();
                    if (element.TryGetProperty("inputVariables", out JsonElement inputVariables))
                    {
                        operation = operation with
                        {
                            InputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                                ReadOperationVariables(inputVariables))
                        };
                    }

                    if (element.TryGetProperty("outputVariables", out JsonElement outputVariables))
                    {
                        operation = operation with
                        {
                            OutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                                ReadOperationVariables(outputVariables))
                        };
                    }

                    if (element.TryGetProperty("inoutputVariables", out JsonElement inoutputVariables))
                    {
                        operation = operation with
                        {
                            InoutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                                ReadOperationVariables(inoutputVariables))
                        };
                    }

                    result = operation;
                    break;
                case "Capability":
                    result = new AasCapability();
                    break;
                default:
                    error = "Unsupported AAS submodel element modelType '" + modelType + "'.";
                    return false;
            }

            ReadReferable(element, ref result);
            value = result;
            return true;
        }

        private static AASReferenceDataType ReadReference(JsonElement element)
        {
            var keys = new List<AASKeyDataType>();
            if (element.TryGetProperty("keys", out JsonElement keyElements) && keyElements.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement key in keyElements.EnumerateArray())
                {
                    keys.Add(new AASKeyDataType
                    {
                        Type = ParseEnum<AASKeyTypesDataType>(MemberString(key, "type")),
                        Value = MemberString(key, "value")
                    });
                }
            }

            AASReferenceDataType reference = EmptyReference();
            reference.Type = ParseEnum<AASReferenceTypesDataType>(MemberString(element, "type"));
            reference.Keys = new ArrayOf<AASKeyDataType>(keys.ToArray());
            return reference;
        }

        private static AASReferenceDataType EmptyReference()
        {
#pragma warning disable SYSLIB0050 // TODO: remove when generated recursive default constructors are fixed.
            return (AASReferenceDataType)FormatterServices.GetUninitializedObject(typeof(AASReferenceDataType));
#pragma warning restore SYSLIB0050
        }

        private static ArrayOf<AASLangStringDataType> ReadLangStrings(JsonElement element)
        {
            var items = new List<AASLangStringDataType>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(new AASLangStringDataType
                    {
                        Language = MemberString(item, "language"),
                        Text = MemberString(item, "text")
                    });
                }
            }

            return new ArrayOf<AASLangStringDataType>(items.ToArray());
        }

        private static ArrayOf<AasSubmodelElement> ReadOperationVariables(JsonElement element)
        {
            var items = new List<AasSubmodelElement>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    JsonElement value = item.TryGetProperty("value", out JsonElement wrapper) ? wrapper : item;
                    if (TryReadSubmodelElement(value, out AasSubmodelElement? parsed, out _) && parsed is not null)
                    {
                        items.Add(parsed);
                    }
                }
            }

            return new ArrayOf<AasSubmodelElement>(items.ToArray());
        }

        private static ArrayOf<AASSpecificAssetIdDataType> ReadSpecificAssetIds(JsonElement element)
        {
            var items = new List<AASSpecificAssetIdDataType>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AASSpecificAssetIdDataType specific = EmptySpecificAssetId();
                    specific.Name = MemberString(item, "name");
                    specific.Value = MemberString(item, "value");
                    items.Add(specific);
                }
            }

            return new ArrayOf<AASSpecificAssetIdDataType>(items.ToArray());
        }

        private static ArrayOf<AASQualifierDataType> ReadQualifiers(JsonElement element)
        {
            var items = new List<AASQualifierDataType>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AASQualifierDataType qualifier = EmptyQualifier();
                    qualifier.Type = MemberString(item, "type");
                    qualifier.ValueType = ParseValueType(MemberString(item, "valueType"));
                    qualifier.Value = MemberString(item, "value");
                    items.Add(qualifier);
                }
            }

            return new ArrayOf<AASQualifierDataType>(items.ToArray());
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

        private static AASSpecificAssetIdDataType EmptySpecificAssetId()
        {
#pragma warning disable SYSLIB0050 // TODO: remove when generated recursive default constructors are fixed.
            return (AASSpecificAssetIdDataType)FormatterServices.GetUninitializedObject(
                typeof(AASSpecificAssetIdDataType));
#pragma warning restore SYSLIB0050
        }

        private static AASQualifierDataType EmptyQualifier()
        {
#pragma warning disable SYSLIB0050 // TODO: remove when generated recursive default constructors are fixed.
            return (AASQualifierDataType)FormatterServices.GetUninitializedObject(typeof(AASQualifierDataType));
#pragma warning restore SYSLIB0050
        }

        private static void ReadReferable<T>(JsonElement element, ref T value)
            where T : AasReferable
        {
            if (element.TryGetProperty("idShort", out JsonElement idShort))
            {
                value = value with { IdShort = AasOptional<string>.Present(idShort.GetString() ?? string.Empty) };
            }

            if (element.TryGetProperty("category", out JsonElement category))
            {
                value = value with { Category = AasOptional<string>.Present(category.GetString() ?? string.Empty) };
            }

            if (element.TryGetProperty("displayName", out JsonElement displayName))
            {
                value = value with
                {
                    DisplayName = AasOptional<ArrayOf<AASLangStringDataType>>.Present(ReadLangStrings(displayName))
                };
            }

            if (element.TryGetProperty("description", out JsonElement description))
            {
                value = value with
                {
                    Description = AasOptional<ArrayOf<AASLangStringDataType>>.Present(ReadLangStrings(description))
                };
            }
        }

        private static bool RequiredString(JsonElement element, string name, out string value, out string? error)
        {
            value = string.Empty;
            if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            {
                error = "Required JSON string member '" + name + "' is missing.";
                return false;
            }

            value = property.GetString() ?? string.Empty;
            error = null;
            return true;
        }

        private static string MemberString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement property) ? property.GetString() ?? string.Empty : string.Empty;
        }

        private static string UpperFirst(string text)
        {
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private delegate bool TryReadElement<T>(JsonElement element, out T? value, out string? error)
            where T : class;
    }
}
