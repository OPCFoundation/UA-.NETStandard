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
 *
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
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Converts OPC UA NodeSet2 documents to and from WoT Thing Models and
    /// Thing Descriptions, including a byte-exact preservation envelope, a
    /// deterministic native <c>uav:nodes</c> projection and the native
    /// readable mapping of the OPC UA WoT Binding.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// OPC UA WoT Binding vocabulary namespace.
        /// </summary>
        public const string VocabularyNamespace = WotVocabulary.VocabularyNamespace;

        /// <summary>
        /// Creates a deterministic WoT Thing Model/Thing Description that carries
        /// a lossless NodeSet2 preservation envelope alongside a native
        /// <c>uav</c> projection and the native readable affordance mapping.
        /// </summary>
        /// <param name="nodeSet">The NodeSet2 document to convert.</param>
        /// <param name="title">An optional document title.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The generated, byte-preserving WoT document.</returns>
        public static WotDocument FromNodeSet(
            UANodeSet nodeSet,
            string? title = null,
            WotNodeSetConverterOptions? options = null)
        {
            WotConversionResult<WotDocument> result = FromNodeSetResult(nodeSet, title, options);
            ThrowIfErrors(result.Diagnostics);
            return result.Value
                ?? throw new FormatException("The NodeSet could not be converted to a WoT document.");
        }

        /// <summary>
        /// Creates a WoT document from a NodeSet2 document, returning structured
        /// diagnostics together with the result.
        /// </summary>
        /// <param name="nodeSet">The NodeSet2 document to convert.</param>
        /// <param name="title">An optional document title.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the returned WotDocument is transferred to the caller through the result.")]
        public static WotConversionResult<WotDocument> FromNodeSetResult(
            UANodeSet nodeSet,
            string? title = null,
            WotNodeSetConverterOptions? options = null)
        {
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            options ??= new WotNodeSetConverterOptions();
            options.Validate();

            var diagnostics = new List<WotDiagnostic>();

            byte[] nodeSetBytes;
            using (var nodeSetStream = new MemoryStream())
            {
                nodeSet.Write(nodeSetStream);
                if (nodeSetStream.Length > options.MaxNodeSetSize)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NodeSetTooLarge,
                        $"NodeSet exceeds the configured {options.MaxNodeSetSize} byte limit."));
                    return new WotConversionResult<WotDocument>(null, diagnostics);
                }
                nodeSetBytes = nodeSetStream.ToArray();
            }

            WotNativeModel model = WotNativeProjection.Build(nodeSet, options);
            UANode? root = SelectRootNode(nodeSet);
            string resolvedTitle = title
                ?? FirstText(root?.DisplayName)
                ?? (string.IsNullOrEmpty(model.ModelUri) ? "OPC UA NodeSet" : model.ModelUri!);

            byte[] digest = ComputeSha256(nodeSetBytes);

            byte[] json;
            using (var output = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(
                    output,
                    new JsonWriterOptions { Indented = true, SkipValidation = false }))
                {
                    writer.WriteStartObject();
                    WriteContext(writer);
                    WriteRootType(writer, root);
                    writer.WriteString("title", resolvedTitle);
                    if (!string.IsNullOrEmpty(root?.BrowseName))
                    {
                        writer.WriteString("uav:browseName", root!.BrowseName);
                    }
                    if (!string.IsNullOrEmpty(root?.NodeId))
                    {
                        writer.WriteString("uav:id", root!.NodeId);
                    }
                    WriteDescription(writer, root?.Description);
                    WriteAffordances(writer, nodeSet, root, diagnostics, options);

                    writer.WritePropertyName("uav:nodeSet");
                    writer.WriteStartObject();
                    writer.WriteString("@type", WotVocabulary.EnvelopeType);
                    writer.WriteString("contentType", WotVocabulary.NodeSetContentType);
                    writer.WriteString("encoding", WotVocabulary.Base64Encoding);
                    writer.WriteString("sha256", ToLowerHex(digest));
                    writer.WriteString("data", System.Convert.ToBase64String(nodeSetBytes));
                    writer.WriteString("profileVersion", WotVocabulary.ProfileVersion);
                    writer.WriteEndObject();

                    writer.WritePropertyName("uav:nodes");
                    WotNativeProjection.Write(writer, model);

                    writer.WriteEndObject();
                }
                json = output.ToArray();
            }

            WotDocument document = WotDocument.FromOwnedBytes(json);
            return new WotConversionResult<WotDocument>(document, diagnostics);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, throwing on any error diagnostic.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The restored or synthesized NodeSet2 document.</returns>
        /// <exception cref="FormatException">Thrown when the conversion fails.</exception>
        public static UANodeSet ToNodeSet(
            WotDocument document,
            WotNodeSetConverterOptions? options = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var diagnostics = new List<WotDiagnostic>();
            UANodeSet? nodeSet = ToNodeSetCore(document, options, null, null, diagnostics);
            ThrowIfErrors(diagnostics);
            return nodeSet
                ?? throw new FormatException("The WoT document could not be converted to a NodeSet.");
        }

        /// <summary>
        /// Parses and restores or synthesizes a NodeSet2 document from UTF-8 WoT
        /// JSON, throwing on any error diagnostic.
        /// </summary>
        /// <param name="utf8Json">The UTF-8 encoded WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The restored or synthesized NodeSet2 document.</returns>
        public static UANodeSet ToNodeSet(
            ReadOnlyMemory<byte> utf8Json,
            WotNodeSetConverterOptions? options = null)
        {
            using WotDocument document = WotDocument.Parse(utf8Json, options);
            return ToNodeSet(document, options);
        }

        /// <summary>
        /// Selects the root node of a projected NodeSet2 - the ObjectType or
        /// VariableType a Thing Model materializes, or the top-level Object a
        /// Thing Description projects - and returns it as an absolute
        /// <see cref="ExpandedNodeId"/> whose <see cref="ExpandedNodeId.NamespaceUri"/>
        /// is resolved from the NodeSet's own namespace table. Returns
        /// <c>null</c> when the NodeSet carries no nodes or the root NodeId
        /// cannot be parsed.
        /// </summary>
        /// <param name="nodeSet">The projected NodeSet2 document.</param>
        /// <returns>
        /// The root node as an absolute ExpandedNodeId, or <c>null</c> when the
        /// NodeSet has no identifiable root.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="nodeSet"/> is <c>null</c>.
        /// </exception>
        public static ExpandedNodeId? TrySelectProjectionRoot(UANodeSet nodeSet)
        {
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            UANode? root = SelectRootNode(nodeSet);
            if (root?.NodeId is not { Length: > 0 } rawNodeId)
            {
                return null;
            }
            NodeId parsed;
            try
            {
                parsed = NodeId.Parse(rawNodeId);
            }
            catch (ServiceResultException)
            {
                return null;
            }
            ushort localIndex = parsed.NamespaceIndex;
            string namespaceUri;
            if (localIndex == 0)
            {
                namespaceUri = Opc.Ua.Types.Namespaces.OpcUa;
            }
            else if (nodeSet.NamespaceUris is { Length: > 0 } uris &&
                localIndex - 1 < uris.Length)
            {
                namespaceUri = uris[localIndex - 1];
            }
            else
            {
                return null;
            }
            return new ExpandedNodeId(parsed, namespaceUri);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, returning structured diagnostics together with the result.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <param name="thingResolver">An optional resolver for referenced TD/TM documents.</param>
        /// <param name="resolutionContext">An optional resolution context for cycle and limit tracking.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        public static WotConversionResult<UANodeSet> ToNodeSetResult(
            WotDocument document,
            WotNodeSetConverterOptions? options = null,
            IWotThingResolver? thingResolver = null,
            WotResolutionContext? resolutionContext = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var diagnostics = new List<WotDiagnostic>();
            UANodeSet? nodeSet = ToNodeSetCore(
                document, options, thingResolver, resolutionContext, diagnostics);
            return new WotConversionResult<UANodeSet>(nodeSet, diagnostics);
        }

        private static UANodeSet? ToNodeSetCore(
            WotDocument document,
            WotNodeSetConverterOptions? options,
            IWotThingResolver? thingResolver,
            WotResolutionContext? resolutionContext,
            List<WotDiagnostic> diagnostics)
        {
            options ??= new WotNodeSetConverterOptions();
            options.Validate();

            if (document.TryGetEnvelope(out JsonElement envelope))
            {
                UANodeSet? restored = RestoreFromEnvelope(envelope, options, diagnostics);
                if (restored is null)
                {
                    return null;
                }
                if (document.TryGetNativeProjection(out JsonElement projection))
                {
                    ValidateNativeConsistency(restored, projection, options, diagnostics);
                }
                return restored;
            }

            if (document.TryGetNativeProjection(out JsonElement nativeProjection))
            {
                WotNativeModel model = WotNativeProjection.Read(nativeProjection, options);
                return WotNativeProjection.ToNodeSet(model, options, diagnostics);
            }

            return Synthesize(document, options, thingResolver, resolutionContext, diagnostics);
        }

        private static UANodeSet? RestoreFromEnvelope(
            JsonElement envelope,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var location = new WotLocation(jsonPointer: "/uav:nodeSet");

            if (!TryGetString(envelope, "contentType", out string? contentType) ||
                !string.Equals(contentType, WotVocabulary.NodeSetContentType, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.UnsupportedContentType,
                    $"Unsupported NodeSet content type '{contentType}'.",
                    location));
                return null;
            }

            if (!TryGetString(envelope, "encoding", out string? encoding) ||
                !string.Equals(encoding, WotVocabulary.Base64Encoding, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.UnsupportedEncoding,
                    $"Unsupported NodeSet encoding '{encoding}'.",
                    location));
                return null;
            }

            if (!TryGetString(envelope, "data", out string? data) || data is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.EnvelopeInvalid,
                    "The uav:nodeSet data value is required and must be a string.",
                    location));
                return null;
            }

            byte[] nodeSetBytes;
            try
            {
                nodeSetBytes = System.Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidBase64,
                    "The uav:nodeSet data is not valid base64.",
                    location));
                return null;
            }

            if (nodeSetBytes.Length > options.MaxNodeSetSize)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NodeSetTooLarge,
                    $"Decoded NodeSet exceeds the configured {options.MaxNodeSetSize} byte limit.",
                    location));
                return null;
            }

            if (envelope.TryGetProperty("sha256", out JsonElement digestElement))
            {
                if (digestElement.ValueKind != JsonValueKind.String ||
                    !TryParseDigest(digestElement.GetString()!, out byte[] expected))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidDigest,
                        "The uav:nodeSet sha256 value is not a valid SHA-256 digest.",
                        location));
                    return null;
                }
                byte[] actual = ComputeSha256(nodeSetBytes);
                if (!FixedEquals(expected, actual))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DigestMismatch,
                        "The uav:nodeSet digest does not match the payload.",
                        location));
                    return null;
                }
            }

            UANodeSet? nodeSet;
            using (var stream = new MemoryStream(nodeSetBytes, writable: false))
            {
                nodeSet = UANodeSet.Read(stream);
            }
            if (nodeSet is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.MalformedNodeSet,
                    "The uav:nodeSet payload is not a valid NodeSet2 document.",
                    location));
            }
            return nodeSet;
        }

        private static void ValidateNativeConsistency(
            UANodeSet baseline,
            JsonElement projection,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var byNodeId = new Dictionary<string, UANode>(StringComparer.Ordinal);
            if (baseline.Items is not null)
            {
                foreach (UANode node in baseline.Items)
                {
                    if (!string.IsNullOrEmpty(node.NodeId))
                    {
                        byNodeId[node.NodeId!] = node;
                    }
                }
            }

            WotNativeModel model = WotNativeProjection.Read(projection, options);
            foreach (WotNativeNode record in model.Nodes)
            {
                if (string.IsNullOrEmpty(record.NodeId) ||
                    !byNodeId.TryGetValue(record.NodeId!, out UANode? node))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(record.BrowseName) &&
                    !string.Equals(record.BrowseName, node.BrowseName, StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionConflict,
                        $"The native BrowseName '{record.BrowseName}' conflicts with the baseline BrowseName '{node.BrowseName}'.",
                        WotLocation.FromNode(record.NodeId!, "BrowseName")));
                }
                string? baselineRule = GetBaselineModellingRule(node);
                if (record.ModellingRule is not null &&
                    baselineRule is not null &&
                    !string.Equals(record.ModellingRule, baselineRule, StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionConflict,
                        $"The native modelling rule '{record.ModellingRule}' conflicts with the baseline '{baselineRule}'.",
                        WotLocation.FromNode(record.NodeId!, "HasModellingRule")));
                }
            }
        }

        private static string? GetBaselineModellingRule(UANode node)
        {
            if (node.References is null)
            {
                return null;
            }
            foreach (Reference reference in node.References)
            {
                if (string.Equals(reference.ReferenceType, "HasModellingRule", StringComparison.Ordinal) &&
                    reference.IsForward &&
                    reference.Value is not null &&
                    WotVocabulary.TryGetModellingRuleName(reference.Value, out string rule))
                {
                    return rule;
                }
            }
            return null;
        }

        private static void ThrowIfErrors(IReadOnlyList<WotDiagnostic> diagnostics)
        {
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                if (diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                {
                    throw new FormatException(diagnostics[ii].ToString());
                }
            }
        }

        private static bool TryGetString(JsonElement element, string name, out string? value)
        {
            if (element.TryGetProperty(name, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return true;
            }
            value = null;
            return false;
        }

        private static byte[] ComputeSha256(byte[] data)
        {
#if NET6_0_OR_GREATER
            return SHA256.HashData(data);
#else
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
#endif
        }

        private static string ToLowerHex(byte[] data)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[data.Length * 2];
            for (int ii = 0; ii < data.Length; ii++)
            {
                chars[ii * 2] = digits[data[ii] >> 4];
                chars[(ii * 2) + 1] = digits[data[ii] & 0x0F];
            }
            return new string(chars);
        }

        private static bool TryParseDigest(string text, out byte[] digest)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 64 && IsHex(trimmed))
            {
                digest = CoreUtils.FromHexString(trimmed);
                return true;
            }
            try
            {
                byte[] decoded = System.Convert.FromBase64String(trimmed);
                if (decoded.Length == 32)
                {
                    digest = decoded;
                    return true;
                }
            }
            catch (FormatException)
            {
                // Not base64; fall through.
            }
            digest = [];
            return false;
        }

        private static bool IsHex(string text)
        {
            foreach (char character in text)
            {
                bool isHex = (character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool FixedEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int ii = 0; ii < left.Length; ii++)
            {
                difference |= left[ii] ^ right[ii];
            }
            return difference == 0;
        }
    }
}
