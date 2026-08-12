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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Reads AAS V3 XML Environment documents.
    /// </summary>
    public sealed class AasXmlReader
    {
        /// <summary>
        /// Reads an AAS XML Environment document from a stream.
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
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, 81920, cancellationToken).ConfigureAwait(false);
                string xml = Encoding.UTF8.GetString(memory.ToArray());
                using var text = new StringReader(xml);
                var settings = new XmlReaderSettings { Async = true };
                using XmlReader reader = XmlReader.Create(text, settings);
                var environment = new AasEnvironment();
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (reader.LocalName == "assetAdministrationShells")
                    {
                        environment = environment with
                        {
                            AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(
                                await ReadShellsAsync(reader).ConfigureAwait(false))
                        };
                    }
                    else if (reader.LocalName == "submodels")
                    {
                        environment = environment with
                        {
                            Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(
                                await ReadSubmodelsAsync(reader).ConfigureAwait(false))
                        };
                    }
                }

                return AasDocumentReadResult.Success(environment);
            }
            catch (XmlException ex)
            {
                return AasDocumentReadResult.Failure("The AAS XML document is malformed: " + ex.Message);
            }
        }

        private static async Task<ArrayOf<AasShell>> ReadShellsAsync(XmlReader reader)
        {
            var shells = new List<AasShell>();
            if (reader.IsEmptyElement)
            {
                return ArrayOf<AasShell>.Empty;
            }

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "assetAdministrationShells")
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "assetAdministrationShell")
                {
                    shells.Add(await ReadShellAsync(reader).ConfigureAwait(false));
                }
            }

            return new ArrayOf<AasShell>(shells.ToArray());
        }

        private static async Task<ArrayOf<AasSubmodel>> ReadSubmodelsAsync(XmlReader reader)
        {
            var submodels = new List<AasSubmodel>();
            if (reader.IsEmptyElement)
            {
                return ArrayOf<AasSubmodel>.Empty;
            }

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "submodels")
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "submodel")
                {
                    submodels.Add(await ReadSubmodelAsync(reader).ConfigureAwait(false));
                }
            }

            return new ArrayOf<AasSubmodel>(submodels.ToArray());
        }

        private static async Task<AasShell> ReadShellAsync(XmlReader reader)
        {
            string id = string.Empty;
            string? idShort = null;
            AASAssetKindDataType assetKind = AASAssetKindDataType.Instance;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "assetAdministrationShell")
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "idShort")
                {
                    idShort = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                }
                else if (reader.LocalName == "id")
                {
                    id = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                }
                else if (reader.LocalName == "assetKind")
                {
                    assetKind = AasJsonReader.ParseEnum<AASAssetKindDataType>(
                        await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                }
            }

            var shell = new AasShell
            {
                Id = id,
                AssetInformation = new AasAssetInformation { AssetKind = assetKind }
            };
            return idShort is null ? shell : shell with { IdShort = AasOptional<string>.Present(idShort) };
        }

        private static async Task<AasSubmodel> ReadSubmodelAsync(XmlReader reader)
        {
            string id = string.Empty;
            string? idShort = null;
            AasOptional<ArrayOf<AasSubmodelElement>> elements = AasOptional<ArrayOf<AasSubmodelElement>>.Absent;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "submodel")
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "idShort")
                {
                    idShort = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                }
                else if (reader.LocalName == "id")
                {
                    id = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                }
                else if (reader.LocalName == "submodelElements")
                {
                    elements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                        await ReadElementsAsync(reader).ConfigureAwait(false));
                }
            }

            var submodel = new AasSubmodel { Id = id, SubmodelElements = elements };
            return idShort is null ? submodel : submodel with { IdShort = AasOptional<string>.Present(idShort) };
        }

        private static async Task<ArrayOf<AasSubmodelElement>> ReadElementsAsync(XmlReader reader)
        {
            var elements = new List<AasSubmodelElement>();
            if (reader.IsEmptyElement)
            {
                return ArrayOf<AasSubmodelElement>.Empty;
            }

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "submodelElements")
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "submodelElement")
                {
                    elements.Add(await ReadElementAsync(reader).ConfigureAwait(false));
                }
            }

            return new ArrayOf<AasSubmodelElement>(elements.ToArray());
        }

        private static async Task<AasSubmodelElement> ReadElementAsync(XmlReader reader)
        {
            string outerXml = await reader.ReadOuterXmlAsync().ConfigureAwait(false);
            string? idShort = ExtractElementValue(outerXml, "idShort");
            string modelType = ExtractElementValue(outerXml, "modelType") ?? "Capability";
            AASDataTypeDefXsdDataType valueType = AASDataTypeDefXsdDataType.String;
            AasOptional<Variant> value = AasOptional<Variant>.Absent;
            string? valueTypeText = ExtractElementValue(outerXml, "valueType");
            if (valueTypeText is not null)
            {
                valueType = AasJsonReader.ParseValueType(valueTypeText);
            }

            string? valueText = ExtractElementValue(outerXml, "value");
            if (valueText is not null)
            {
                value = AasOptional<Variant>.Present(new Variant(valueText));
            }

            AasSubmodelElement element = modelType == "Property"
                ? new AasProperty { ValueType = valueType, Value = value }
                : new AasCapability();
            return idShort is null ? element : element with { IdShort = AasOptional<string>.Present(idShort) };
        }

        private static string? ExtractElementValue(string xml, string localName)
        {
            string start = "<" + localName + ">";
            string end = "</" + localName + ">";
            int startIndex = xml.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return null;
            }

            startIndex += start.Length;
            int endIndex = xml.IndexOf(end, startIndex, StringComparison.Ordinal);
            if (endIndex < startIndex)
            {
                return null;
            }

            return System.Net.WebUtility.HtmlDecode(xml.Substring(startIndex, endIndex - startIndex));
        }
    }
}
