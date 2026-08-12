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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Writes AAS V3 XML Environment documents.
    /// </summary>
    public sealed class AasXmlWriter
    {
        /// <summary>
        /// Writes an AAS XML Environment document to a stream.
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

            var settings = new XmlWriterSettings { Async = true, Indent = true };
            using XmlWriter writer = XmlWriter.Create(stream, settings);
            await writer.WriteStartDocumentAsync().ConfigureAwait(false);
            await writer.WriteStartElementAsync(null, "environment", null).ConfigureAwait(false);
            if (environment.AssetAdministrationShells.IsPresent)
            {
                await writer.WriteStartElementAsync(null, "assetAdministrationShells", null).ConfigureAwait(false);
                foreach (AasShell shell in environment.AssetAdministrationShells.Value.ToArray() ?? Array.Empty<AasShell>())
                {
                    await WriteShellAsync(writer, shell).ConfigureAwait(false);
                }

                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }

            if (environment.Submodels.IsPresent)
            {
                await writer.WriteStartElementAsync(null, "submodels", null).ConfigureAwait(false);
                foreach (AasSubmodel submodel in environment.Submodels.Value.ToArray() ?? Array.Empty<AasSubmodel>())
                {
                    await WriteSubmodelAsync(writer, submodel).ConfigureAwait(false);
                }

                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
            await writer.WriteEndDocumentAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async Task WriteShellAsync(XmlWriter writer, AasShell shell)
        {
            await writer.WriteStartElementAsync(null, "assetAdministrationShell", null).ConfigureAwait(false);
            await WriteReferableAsync(writer, shell).ConfigureAwait(false);
            await writer.WriteElementStringAsync(null, "id", null, shell.Id).ConfigureAwait(false);
            await writer.WriteElementStringAsync(null, "modelType", null, shell.ModelType).ConfigureAwait(false);
            await writer.WriteStartElementAsync(null, "assetInformation", null).ConfigureAwait(false);
            await writer.WriteElementStringAsync(
                null,
                "assetKind",
                null,
                AasJsonReader.FormatEnum(shell.AssetInformation.AssetKind)).ConfigureAwait(false);
            if (shell.AssetInformation.SpecificAssetIds.IsPresent)
            {
                await writer.WriteStartElementAsync(null, "specificAssetIds", null).ConfigureAwait(false);
                foreach (AASSpecificAssetIdDataType id in
                    shell.AssetInformation.SpecificAssetIds.Value.ToArray() ??
                    Array.Empty<AASSpecificAssetIdDataType>())
                {
                    await writer.WriteStartElementAsync(null, "specificAssetId", null).ConfigureAwait(false);
                    await writer.WriteElementStringAsync(null, "name", null, id.Name ?? string.Empty).ConfigureAwait(false);
                    await writer.WriteElementStringAsync(null, "value", null, id.Value ?? string.Empty).ConfigureAwait(false);
                    await writer.WriteEndElementAsync().ConfigureAwait(false);
                }

                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        private static async Task WriteSubmodelAsync(XmlWriter writer, AasSubmodel submodel)
        {
            await writer.WriteStartElementAsync(null, "submodel", null).ConfigureAwait(false);
            await WriteReferableAsync(writer, submodel).ConfigureAwait(false);
            await writer.WriteElementStringAsync(null, "id", null, submodel.Id).ConfigureAwait(false);
            await writer.WriteElementStringAsync(null, "modelType", null, submodel.ModelType).ConfigureAwait(false);
            if (submodel.SubmodelElements.IsPresent)
            {
                await writer.WriteStartElementAsync(null, "submodelElements", null).ConfigureAwait(false);
                foreach (AasSubmodelElement element in
                    submodel.SubmodelElements.Value.ToArray() ?? Array.Empty<AasSubmodelElement>())
                {
                    await WriteElementAsync(writer, element).ConfigureAwait(false);
                }

                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        private static async Task WriteElementAsync(XmlWriter writer, AasSubmodelElement element)
        {
            await writer.WriteStartElementAsync(null, "submodelElement", null).ConfigureAwait(false);
            await WriteReferableAsync(writer, element).ConfigureAwait(false);
            await writer.WriteElementStringAsync(null, "modelType", null, element.ModelType).ConfigureAwait(false);
            if (element is AasProperty property)
            {
                await writer.WriteElementStringAsync(
                    null,
                    "valueType",
                    null,
                    AasJsonReader.FormatValueType(property.ValueType)).ConfigureAwait(false);
                if (property.Value.IsPresent)
                {
                    await writer.WriteElementStringAsync(
                        null,
                        "value",
                        null,
                        Lexical(property.Value.Value, property.ValueType)).ConfigureAwait(false);
                }
            }
            else if (element is AasFile file)
            {
                await writer.WriteElementStringAsync(null, "contentType", null, file.ContentType).ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        private static async Task WriteReferableAsync(XmlWriter writer, AasReferable referable)
        {
            if (referable.IdShort.IsPresent)
            {
                await writer.WriteElementStringAsync(null, "idShort", null, referable.IdShort.Value).ConfigureAwait(false);
            }

            if (referable.Category.IsPresent)
            {
                await writer.WriteElementStringAsync(null, "category", null, referable.Category.Value).ConfigureAwait(false);
            }
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
