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
using System.Xml;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Whether a domain check succeeded, failed, or could not be performed.
    /// Unsupported must never be used for a document that was checked and found invalid.
    /// </summary>
    public enum XRegistryValidationOutcome
    {
        /// <summary>
        /// The provider performed the check and accepted the document.
        /// </summary>
        Valid,

        /// <summary>
        /// The provider performed the check and rejected the document.
        /// </summary>
        Invalid,

        /// <summary>
        /// No check could be performed for this format or compatibility mode.
        /// </summary>
        Unsupported
    }

    /// <summary>
    /// A domain validation outcome; invalid and unsupported outcomes require an explanatory reason.
    /// </summary>
    public sealed record XRegistryValidationResult(XRegistryValidationOutcome Outcome, string? Reason = null);

    /// <summary>
    /// An immutable Version input to a domain validator. Documentless Versions supply zero bytes.
    /// Validators must not mutate the registry or fetch external references.
    /// </summary>
    public sealed record XRegistryValidationDocument
    {
        /// <summary>
        /// Owns the metadata and content of a Version being checked.
        /// </summary>
        public XRegistryValidationDocument(string path, JsonElement metadata, ByteString document)
        {
            Path = XRegistryPath.Normalize(path);
            if (metadata.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Version metadata must be a JSON object.", nameof(metadata));
            }
            Metadata = metadata.Clone();
            Document = document.IsNull ? ByteString.Empty : ByteString.From(document.Span);
        }

        /// <summary>
        /// Gets the exact registry-relative Version address.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the Version metadata.
        /// </summary>
        public JsonElement Metadata { get; }

        /// <summary>
        /// Gets the exact domain bytes, with absent documents normalized to zero bytes.
        /// </summary>
        public ByteString Document { get; }
    }

    /// <summary>
    /// An injectable format-specific validator. Format and compatibility names are matched ignoring case.
    /// Providers define their compatibility semantics rather than claiming generic schema compatibility.
    /// </summary>
    public interface IXRegistryDocumentValidator
    {
        /// <summary>
        /// Gets the supported domain format names.
        /// </summary>
        ArrayOf<string> Formats { get; }

        /// <summary>
        /// Gets the compatibility modes supported for the provider's formats.
        /// </summary>
        ArrayOf<string> CompatibilityModes { get; }

        /// <summary>
        /// Checks one candidate Version without publishing or changing it.
        /// </summary>
        ValueTask<XRegistryValidationResult> ValidateFormatAsync(
            XRegistryValidationDocument document, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks the candidate against the Resource's other Versions with their ancestor metadata retained.
        /// </summary>
        ValueTask<XRegistryValidationResult> ValidateCompatibilityAsync(
            XRegistryValidationDocument document, ArrayOf<XRegistryValidationDocument> versions,
            string mode, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Validates JSON/1.0 and XML/1.0 syntax and the explicit "identical" document compatibility mode.
    /// This is not a JSON Schema, XSD, Avro or Protobuf validator.
    /// </summary>
    public sealed class XRegistrySyntaxDocumentValidator : IXRegistryDocumentValidator
    {
        /// <inheritdoc/>
        public ArrayOf<string> Formats => ["JSON/1.0", "XML/1.0"];

        /// <inheritdoc/>
        public ArrayOf<string> CompatibilityModes => ["identical"];

        /// <inheritdoc/>
        public async ValueTask<XRegistryValidationResult> ValidateFormatAsync(
            XRegistryValidationDocument document, CancellationToken cancellationToken = default)
        {
            document.ThrowIfNull(nameof(document));
            cancellationToken.ThrowIfCancellationRequested();
            string
                ? format = document.Metadata.TryGetProperty("format", out JsonElement value) ? value.GetString() : null;
            try
            {
                if (string.Equals(format, "JSON/1.0", StringComparison.OrdinalIgnoreCase))
                {
                    using JsonDocument parsed = JsonDocument.Parse(document.Document.Memory);
                    return new XRegistryValidationResult(XRegistryValidationOutcome.Valid);
                }
                if (string.Equals(format, "XML/1.0", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new MemoryStream(document.Document.ToArray(), writable: false);
                    using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
                    {
                        Async = true,
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = Math.Max(1, document.Document.Length),
                        MaxCharactersFromEntities = 1
                    });
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    return new XRegistryValidationResult(XRegistryValidationOutcome.Valid);
                }
                return new XRegistryValidationResult(XRegistryValidationOutcome.Unsupported,
                    "Only JSON/1.0 and XML/1.0 syntax are supported by this validator.");
            }
            catch (Exception exception) when (exception is JsonException or XmlException)
            {
                return new XRegistryValidationResult(XRegistryValidationOutcome.Invalid, exception.Message);
            }
        }

        /// <inheritdoc/>
        public ValueTask<XRegistryValidationResult> ValidateCompatibilityAsync(
            XRegistryValidationDocument document, ArrayOf<XRegistryValidationDocument> versions,
            string mode, CancellationToken cancellationToken = default)
        {
            document.ThrowIfNull(nameof(document));
            mode.ThrowIfNull(nameof(mode));
            cancellationToken.ThrowIfCancellationRequested();
            if (!mode.Equals("identical", StringComparison.OrdinalIgnoreCase))
            {
                return new ValueTask<XRegistryValidationResult>(new XRegistryValidationResult(
                    XRegistryValidationOutcome.Unsupported, "Only identical document compatibility is supported."));
            }
            string? format = document.Metadata.GetProperty("format").GetString();
            foreach (XRegistryValidationDocument other in versions)
            {
                if (!other.Metadata.TryGetProperty("format", out JsonElement otherFormat) ||
                    !string.Equals(format, otherFormat.GetString(), StringComparison.OrdinalIgnoreCase) ||
                    !document.Document.Span.SequenceEqual(other.Document.Span))
                {
                    return new ValueTask<XRegistryValidationResult>(new XRegistryValidationResult(
                        XRegistryValidationOutcome.Invalid,
                            "Versions must have identical format identifiers and document bytes."));
                }
            }
            return new ValueTask<XRegistryValidationResult>(
                new XRegistryValidationResult(XRegistryValidationOutcome.Valid));
        }
    }
}
