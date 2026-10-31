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
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private async ValueTask ValidateDocumentsAsync(
            Transaction transaction, XRegistryModelRules model, CancellationToken ct)
        {
            foreach (string resource in transaction.Resources)
            {
                if (transaction.Entries[resource + "/meta"] is not JsonObject metaEntry)
                {
                    continue;
                }
                JsonObject definition = model.Resolve(resource).Definition;
                List<string> paths = Children(transaction, resource + "/versions");
                if (!XRegistryModelRules.Boolean(definition["validateformat"]))
                {
                    foreach (string path in paths)
                    {
                        JsonObject metadata = Metadata(GetEntry(transaction, path));
                        foreach (string name in s_validationAttributes)
                        {
                            metadata.Remove(name);
                        }
                    }
                    continue;
                }
                var documents = new List<XRegistryValidationDocument>();
                foreach (string path in paths)
                {
                    JsonObject entry = GetEntry(transaction, path);
                    documents.Add(new XRegistryValidationDocument(path, Element(Metadata(entry)),
                        await ReadDocumentAsync(entry, ct).ConfigureAwait(false)));
                }
                ArrayOf<XRegistryValidationDocument> versions = [.. documents];
                for (int index = 0; index < versions.Count; index++)
                {
                    XRegistryValidationDocument document = versions[index];
                    JsonObject metadata = Metadata(GetEntry(transaction, document.Path));
                    foreach (string name in s_validationAttributes)
                    {
                        metadata.Remove(name);
                    }
                    if (!XRegistryModelRules.Boolean(definition["validateformat"]) || metadata["format"] is null)
                    {
                        continue;
                    }
                    string format = XRegistryModelRules.Text(metadata["format"]);
                    bool strict = XRegistryModelRules.Boolean(definition["strictvalidation"]);
                    string singular = XRegistryModelRules.Text(definition["singular"]);
                    IXRegistryDocumentValidator? validator = Validator(format);
                    XRegistryValidationResult result;
                    string unsupportedCode = "format_unknown";
                    if (metadata[singular + "url"] is not null)
                    {
                        result = new XRegistryValidationResult(XRegistryValidationOutcome.Unsupported,
                            "The document is externally referenced and was not fetched.");
                        unsupportedCode = "format_external";
                    }
                    else
                    {
                        result = validator is null
                            ? new XRegistryValidationResult(XRegistryValidationOutcome.Unsupported,
                                "No validator is configured for this format.")
                            : await validator.ValidateFormatAsync(document, ct).ConfigureAwait(false);
                    }
                    ApplyValidationResult(metadata, result, "format", strict, unsupportedCode);
                    if (XRegistryModelRules.Boolean(definition["validatecompatibility"]) &&
                        Metadata(metaEntry)["compatibility"] is JsonNode compatibility)
                    {
                        string mode = XRegistryModelRules.Text(compatibility);
                        result = result.Outcome != XRegistryValidationOutcome.Valid || validator is null
                            ? new XRegistryValidationResult(XRegistryValidationOutcome.Unsupported,
                                "Compatibility cannot be checked without a validated document format.")
                            : await validator.ValidateCompatibilityAsync(document, versions, mode, ct).ConfigureAwait(
                                false);
                        ApplyValidationResult(metadata, result, "compatibility", strict, "compatibility_unknown");
                    }
                }
            }
        }

        private IXRegistryDocumentValidator? Validator(string format)
        {
            foreach (IXRegistryDocumentValidator validator in m_options.DocumentValidators)
            {
                if (validator.Formats.ToList().Contains(format, StringComparer.OrdinalIgnoreCase))
                {
                    return validator;
                }
            }
            return null;
        }

        private static void ApplyValidationResult(
            JsonObject metadata, XRegistryValidationResult result, string kind, bool strict, string unsupportedCode)
        {
            if (result is null ||
                result.Outcome is < XRegistryValidationOutcome.Valid or > XRegistryValidationOutcome.Unsupported ||
                (result.Outcome != XRegistryValidationOutcome.Valid && string.IsNullOrWhiteSpace(result.Reason)))
            {
                throw new InvalidDataException("The document validator returned an incomplete outcome.");
            }
            if (result.Outcome == XRegistryValidationOutcome.Invalid)
            {
                throw new XRegistryRejectionException(kind + "_violation", result.Reason!);
            }
            if (result.Outcome == XRegistryValidationOutcome.Unsupported && strict)
            {
                throw new XRegistryRejectionException(unsupportedCode, result.Reason!);
            }
            metadata[kind + "validated"] = result.Outcome == XRegistryValidationOutcome.Valid;
            if (result.Outcome == XRegistryValidationOutcome.Unsupported)
            {
                metadata[kind + "validatedreason"] = result.Reason;
            }
        }

        private JsonObject ValidationCapabilities(bool canWrite)
        {
            JsonObject capabilities = Capabilities(canWrite);
            var formats = new JsonArray();
            var compatibilities = new JsonObject();
            foreach (IXRegistryDocumentValidator validator in m_options.DocumentValidators)
            {
                foreach (string format in validator.Formats)
                {
                    JsonNode? value = JsonValue.Create(format);
                    formats.Add(value);
                    compatibilities[format] = new JsonArray(validator.CompatibilityModes.ToList()
                        .Select(mode => (JsonNode?)JsonValue.Create(mode)).ToArray());
                }
            }
            capabilities["formats"] = formats;
            capabilities["compatibilities"] = compatibilities;
            return capabilities;
        }

        private static readonly string[] s_validationAttributes =
        [
            "formatvalidated", "formatvalidatedreason", "compatibilityvalidated", "compatibilityvalidatedreason"
        ];
    }
}
