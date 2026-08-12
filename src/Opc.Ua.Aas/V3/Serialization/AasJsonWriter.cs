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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Writes AAS V3 JSON Environment documents.
    /// </summary>
    public sealed class AasJsonWriter
    {
        /// <summary>
        /// Writes an AAS JSON Environment document to a stream.
        /// </summary>
        /// <param name="stream">The destination stream.</param>
        /// <param name="environment">The environment to write.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that completes when the document has been written.</returns>
        public async Task WriteAsync(
            Stream stream,
            AasEnvironment environment,
            CancellationToken cancellationToken = default)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            WriteEnvironment(writer, environment);

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void WriteEnvironment(Utf8JsonWriter writer, AasEnvironment environment)
        {
            writer.WriteStartObject();
            if (environment.AssetAdministrationShells.IsPresent)
            {
                writer.WritePropertyName("assetAdministrationShells");
                WriteArray(writer, environment.AssetAdministrationShells.Value, WriteShell);
            }

            if (environment.Submodels.IsPresent)
            {
                writer.WritePropertyName("submodels");
                WriteArray(writer, environment.Submodels.Value, WriteSubmodel);
            }

            if (environment.ConceptDescriptions.IsPresent)
            {
                writer.WritePropertyName("conceptDescriptions");
                WriteArray(writer, environment.ConceptDescriptions.Value, WriteConceptDescription);
            }

            writer.WriteEndObject();
        }

        private static void WriteShell(Utf8JsonWriter writer, AasShell shell)
        {
            writer.WriteStartObject();
            WriteReferable(writer, shell);
            writer.WriteString("id", shell.Id);
            writer.WriteString("modelType", shell.ModelType);
            writer.WritePropertyName("assetInformation");
            writer.WriteStartObject();
            writer.WriteString("assetKind", AasJsonReader.FormatEnum(shell.AssetInformation.AssetKind));
            if (shell.AssetInformation.SpecificAssetIds.IsPresent)
            {
                writer.WritePropertyName("specificAssetIds");
                WriteSpecificAssetIds(writer, shell.AssetInformation.SpecificAssetIds.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private static void WriteSubmodel(Utf8JsonWriter writer, AasSubmodel submodel)
        {
            writer.WriteStartObject();
            WriteReferable(writer, submodel);
            writer.WriteString("id", submodel.Id);
            writer.WriteString("modelType", submodel.ModelType);
            if (submodel.Qualifiers.IsPresent)
            {
                writer.WritePropertyName("qualifiers");
                WriteQualifiers(writer, submodel.Qualifiers.Value);
            }

            if (submodel.SubmodelElements.IsPresent)
            {
                writer.WritePropertyName("submodelElements");
                WriteArray(writer, submodel.SubmodelElements.Value, WriteSubmodelElement);
            }

            writer.WriteEndObject();
        }

        private static void WriteConceptDescription(Utf8JsonWriter writer, AasConceptDescription concept)
        {
            writer.WriteStartObject();
            WriteReferable(writer, concept);
            writer.WriteString("id", concept.Id);
            writer.WriteString("modelType", concept.ModelType);
            writer.WriteEndObject();
        }

        private static void WriteSubmodelElement(Utf8JsonWriter writer, AasSubmodelElement element)
        {
            writer.WriteStartObject();
            WriteReferable(writer, element);
            writer.WriteString("modelType", element.ModelType);
            switch (element)
            {
                case AasProperty property:
                    writer.WriteString("valueType", AasJsonReader.FormatValueType(property.ValueType));
                    if (property.Value.IsPresent)
                    {
                        writer.WriteString("value", Lexical(property.Value.Value, property.ValueType));
                    }

                    break;
                case AasRange range:
                    writer.WriteString("valueType", AasJsonReader.FormatValueType(range.ValueType));
                    if (range.Min.IsPresent)
                    {
                        writer.WriteString("min", Lexical(range.Min.Value, range.ValueType));
                    }

                    if (range.Max.IsPresent)
                    {
                        writer.WriteString("max", Lexical(range.Max.Value, range.ValueType));
                    }

                    break;
                case AasMultiLanguageProperty multiLanguage:
                    if (multiLanguage.Value.IsPresent)
                    {
                        writer.WritePropertyName("value");
                        WriteLangStrings(writer, multiLanguage.Value.Value);
                    }

                    break;
                case AasBlob blob:
                    writer.WriteString("contentType", blob.ContentType);
                    break;
                case AasFile file:
                    if (file.Value.IsPresent)
                    {
                        writer.WriteString("value", file.Value.Value);
                    }

                    writer.WriteString("contentType", file.ContentType);
                    break;
                case AasReferenceElement referenceElement:
                    if (referenceElement.Value.IsPresent)
                    {
                        writer.WritePropertyName("value");
                        WriteReference(writer, referenceElement.Value.Value);
                    }

                    break;
                case AasSubmodelElementList list:
                    if (list.OrderRelevant.IsPresent)
                    {
                        writer.WriteBoolean("orderRelevant", list.OrderRelevant.Value);
                    }

                    writer.WriteString("typeValueListElement", AasJsonReader.FormatEnum(list.TypeValueListElement));
                    if (list.Value.IsPresent)
                    {
                        writer.WritePropertyName("value");
                        WriteArray(writer, list.Value.Value, WriteSubmodelElement);
                    }

                    break;
                case AasEntity entity:
                    writer.WriteString("entityType", AasJsonReader.FormatEnum(entity.EntityType));
                    if (entity.Statements.IsPresent)
                    {
                        writer.WritePropertyName("statements");
                        WriteArray(writer, entity.Statements.Value, WriteSubmodelElement);
                    }

                    break;
                case AasBasicEventElement basicEvent:
                    writer.WritePropertyName("observed");
                    WriteReference(writer, basicEvent.Observed);
                    writer.WriteString("direction", AasJsonReader.FormatEnum(basicEvent.Direction));
                    writer.WriteString("state", AasJsonReader.FormatEnum(basicEvent.State));
                    break;
                case AasRelationshipElementBase relationship:
                    writer.WritePropertyName("first");
                    WriteReference(writer, relationship.First);
                    writer.WritePropertyName("second");
                    WriteReference(writer, relationship.Second);
                    if (relationship is AasAnnotatedRelationshipElement annotated &&
                        annotated.Annotations.IsPresent)
                    {
                        writer.WritePropertyName("annotations");
                        WriteArray(writer, annotated.Annotations.Value, WriteSubmodelElement);
                    }

                    break;
                case AasSubmodelElementCollection collection:
                    if (collection.Value.IsPresent)
                    {
                        writer.WritePropertyName("value");
                        WriteArray(writer, collection.Value.Value, WriteSubmodelElement);
                    }

                    break;
                case AasOperation operation:
                    if (operation.InputVariables.IsPresent)
                    {
                        writer.WritePropertyName("inputVariables");
                        WriteOperationVariables(writer, operation.InputVariables.Value);
                    }

                    if (operation.OutputVariables.IsPresent)
                    {
                        writer.WritePropertyName("outputVariables");
                        WriteOperationVariables(writer, operation.OutputVariables.Value);
                    }

                    if (operation.InoutputVariables.IsPresent)
                    {
                        writer.WritePropertyName("inoutputVariables");
                        WriteOperationVariables(writer, operation.InoutputVariables.Value);
                    }

                    break;
            }

            writer.WriteEndObject();
        }

        private static void WriteReferable(Utf8JsonWriter writer, AasReferable referable)
        {
            if (referable.IdShort.IsPresent)
            {
                writer.WriteString("idShort", referable.IdShort.Value);
            }

            if (referable.Category.IsPresent)
            {
                writer.WriteString("category", referable.Category.Value);
            }

            if (referable.DisplayName.IsPresent)
            {
                writer.WritePropertyName("displayName");
                WriteLangStrings(writer, referable.DisplayName.Value);
            }

            if (referable.Description.IsPresent)
            {
                writer.WritePropertyName("description");
                WriteLangStrings(writer, referable.Description.Value);
            }
        }

        private static void WriteReference(Utf8JsonWriter writer, AASReferenceDataType reference)
        {
            writer.WriteStartObject();
            writer.WriteString("type", AasJsonReader.FormatEnum(reference.Type));
            writer.WritePropertyName("keys");
            writer.WriteStartArray();
            foreach (AASKeyDataType key in reference.Keys.Span)
            {
                writer.WriteStartObject();
                writer.WriteString("type", AasJsonReader.FormatEnum(key.Type));
                writer.WriteString("value", key.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteSpecificAssetIds(Utf8JsonWriter writer, ArrayOf<AASSpecificAssetIdDataType> values)
        {
            writer.WriteStartArray();
            foreach (AASSpecificAssetIdDataType value in values.Span)
            {
                writer.WriteStartObject();
                writer.WriteString("name", value.Name);
                writer.WriteString("value", value.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteQualifiers(Utf8JsonWriter writer, ArrayOf<AASQualifierDataType> values)
        {
            writer.WriteStartArray();
            foreach (AASQualifierDataType value in values.Span)
            {
                writer.WriteStartObject();
                writer.WriteString("type", value.Type);
                writer.WriteString("valueType", AasJsonReader.FormatValueType(value.ValueType));
                if (value.Value is not null)
                {
                    writer.WriteString("value", value.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteLangStrings(Utf8JsonWriter writer, ArrayOf<AASLangStringDataType> values)
        {
            writer.WriteStartArray();
            foreach (AASLangStringDataType value in values.Span)
            {
                writer.WriteStartObject();
                writer.WriteString("language", value.Language);
                writer.WriteString("text", value.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteOperationVariables(Utf8JsonWriter writer, ArrayOf<AasSubmodelElement> values)
        {
            writer.WriteStartArray();
            foreach (AasSubmodelElement value in values.Span)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("value");
                WriteSubmodelElement(writer, value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteArray<T>(Utf8JsonWriter writer, ArrayOf<T> values, Action<Utf8JsonWriter, T> write)
        {
            writer.WriteStartArray();
            foreach (T value in values.Span)
            {
                write(writer, value);
            }

            writer.WriteEndArray();
        }

        private static string Lexical(in Variant value, AASDataTypeDefXsdDataType valueType)
        {
            if (value.TryGetValue(out string? text) && text is not null)
            {
                return text;
            }

            return AasLexicalCanonicalizer.TryCanonicalize(value, valueType, out string? lexical, out _) &&
                lexical is not null
                ? lexical
                : string.Empty;
        }

    }
}
