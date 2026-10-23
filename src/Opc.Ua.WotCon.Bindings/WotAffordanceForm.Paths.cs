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
using System.Globalization;
using System.IO;
using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// An immutable description of a single WoT interaction-affordance form. It
    /// carries the affordance metadata and a reflection-free snapshot of the form
    /// and affordance JSON (cloned, so it is safe to retain on the immutable
    /// registry snapshot) together with the RFC 6901 JSON Pointer that locates the
    /// form in the originating document. Binders read protocol vocabulary terms
    /// from <see cref="FormElement"/>; the object performs no transport I/O.
    /// </summary>
    public sealed partial class WotAffordanceForm
    {
        /// <summary>
        /// Gets the captured original source Object identity.
        /// </summary>
        internal string? SourceCallReceiver { get; private init; }

        /// <summary>
        /// Gets the unambiguous endpoint that supplied the source Object identity.
        /// </summary>
        internal string? SourceCallEndpoint { get; private init; }

        /// <summary>
        /// Gets the diagnostic when source receiver authority cannot be captured.
        /// </summary>
        internal string? SourceCallError { get; private init; }

        /// <summary>
        /// Gets original action component ownership evidence retained before document detachment.
        /// </summary>
        internal string? SourceComponentOf { get; private init; }

        /// <summary>
        /// Gets whether published-mode target identities were captured from the original document.
        /// </summary>
        internal bool HasPublishedTargetCapture { get; private init; }

        /// <summary>
        /// Gets the canonical form-level target NodeId captured for published processing.
        /// </summary>
        internal string? PublishedNodeId { get; private init; }

        /// <summary>
        /// Gets the canonical href target NodeId captured for published processing.
        /// </summary>
        internal string? PublishedHrefNodeId { get; private init; }

        /// <summary>
        /// Gets whether the published href target came from its path rather than an id query.
        /// </summary>
        internal bool PublishedHrefNodeIdInPath { get; private init; }

        /// <summary>
        /// Gets the published target capture error, if any.
        /// </summary>
        internal string? PublishedTargetError { get; private init; }

        private WotBrowsePathTarget? BrowsePathTarget { get; init; }

        private string? BrowsePathError { get; init; }

        private bool HasBrowsePathCapture { get; init; }

        /// <summary>
        /// Returns a copy of the form that carries the captured browse path target or error.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="allowNumericNamespacePrefixes">Whether the selected interpretation permits numeric prefixes.</param>
        internal WotAffordanceForm WithBrowsePathCapture(
            WotDocument document,
            bool allowNumericNamespacePrefixes = false)
        {
            Capture(
                document,
                JsonPointer,
                out WotBrowsePathTarget? target,
                out string? error,
                allowNumericNamespacePrefixes);
            string receiver = string.Empty;
            string endpoint = string.Empty;
            string receiverError = string.Empty;
            JsonElement originalAffordance = AffordanceElement;
            if (Kind == WotAffordanceKind.Action &&
                document.RootElement.TryGetProperty("actions", out JsonElement actions) &&
                actions.ValueKind == JsonValueKind.Object &&
                actions.TryGetProperty(AffordanceName, out JsonElement original))
            {
                originalAffordance = original;
            }
            bool hasReceiver = Kind == WotAffordanceKind.Action &&
                WotCallReceiver.TryCaptureSourceObject(
                    document, originalAffordance, out receiver, out endpoint, out receiverError);
            string? componentOf = null;
            if (Kind == WotAffordanceKind.Action &&
                WotCallReceiver.TryCaptureActionComponentOf(
                    document, originalAffordance, out string capturedComponentOf))
            {
                componentOf = capturedComponentOf;
            }
            string? publishedNodeId = null;
            string? publishedHrefNodeId = null;
            bool publishedHrefNodeIdInPath = false;
            string? publishedTargetError = null;
            if (allowNumericNamespacePrefixes)
            {
                _ = TryCapturePublishedTargets(
                    document,
                    JsonPointer,
                    Href,
                    out publishedNodeId,
                    out publishedHrefNodeId,
                    out publishedHrefNodeIdInPath,
                    out publishedTargetError);
            }
            return new WotAffordanceForm(
                Kind, AffordanceName, Operations, Href, ContentType, Subprotocol, SecuritySchemes,
                JsonPointer, FormElement, AffordanceElement, TargetMapping)
            {
                PayloadSchema = PayloadSchema,
                BrowsePathTarget = target,
                BrowsePathError = error,
                HasBrowsePathCapture = true,
                SourceCallReceiver = hasReceiver ? receiver : null,
                SourceCallEndpoint = hasReceiver ? endpoint : null,
                SourceCallError = hasReceiver ? null : receiverError,
                SourceComponentOf = componentOf,
                HasPublishedTargetCapture = allowNumericNamespacePrefixes,
                PublishedNodeId = publishedNodeId,
                PublishedHrefNodeId = publishedHrefNodeId,
                PublishedHrefNodeIdInPath = publishedHrefNodeIdInPath,
                PublishedTargetError = publishedTargetError
            };
        }

        private static bool TryCapturePublishedTargets(
            WotDocument document,
            string jsonPointer,
            string? href,
            out string? nodeId,
            out string? hrefNodeId,
            out bool hrefNodeIdInPath,
            out string? error)
        {
            nodeId = null;
            hrefNodeId = null;
            hrefNodeIdInPath = false;
            error = null;
            if (!document.TryEvaluatePointer(jsonPointer, out JsonElement sourceForm) ||
                sourceForm.ValueKind != JsonValueKind.Object)
            {
                error = "The original form context is unavailable for published NodeId resolution.";
                return false;
            }
            if (!IsOpcUaAddress(document, href))
            {
                return true;
            }
            if (sourceForm.TryGetProperty("uav:id", out JsonElement identity))
            {
                if (identity.ValueKind != JsonValueKind.String ||
                    identity.GetString() is not { } value ||
                    !TryCanonicalPublishedNodeId(document, sourceForm, value, out nodeId, out error))
                {
                    error ??= "The form uav:id must be a resolvable published NodeId.";
                    return false;
                }
            }
            if (string.IsNullOrEmpty(href))
            {
                return true;
            }
            try
            {
                string? target = WotPortableIdentity.ReadUriTargetNodeId(href, out _);
                if (target is not null)
                {
                    if (!TryCanonicalPublishedNodeId(document, sourceForm, target, out hrefNodeId, out error))
                    {
                        error ??= "The href id query must be a resolvable published NodeId.";
                        return false;
                    }
                }
                else if (TryReadHrefPathNodeId(href, out string pathTarget))
                {
                    if (!TryCanonicalPublishedNodeId(
                        document, sourceForm, pathTarget, out hrefNodeId, out error))
                    {
                        error ??= "The href path target must be a resolvable published NodeId.";
                        return false;
                    }
                    hrefNodeIdInPath = true;
                }
            }
            catch (FormatException exception)
            {
                error = exception.Message;
                return false;
            }
            if (nodeId is not null && hrefNodeId is not null &&
                !string.Equals(nodeId, hrefNodeId, StringComparison.Ordinal))
            {
                error = "The form uav:id and href target identify different NodeIds.";
                return false;
            }
            return true;
        }

        private static bool IsOpcUaAddress(WotDocument document, string? href)
        {
            string? baseAddress = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("base", out JsonElement baseElement) &&
                baseElement.ValueKind == JsonValueKind.String
                ? baseElement.GetString()
                : null;
            if (string.IsNullOrEmpty(href))
            {
                return Uri.TryCreate(baseAddress, UriKind.Absolute, out Uri? baseUri) &&
                    IsOpcUaScheme(baseUri.Scheme);
            }
            if (Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute))
            {
                return IsOpcUaScheme(absolute.Scheme);
            }
            return Uri.TryCreate(baseAddress, UriKind.Absolute, out Uri? source) &&
                Uri.TryCreate(href, UriKind.Relative, out Uri? relative) &&
                Uri.TryCreate(source, relative, out Uri? resolved) &&
                IsOpcUaScheme(resolved.Scheme);
        }

        private static bool IsOpcUaScheme(string scheme)
        {
            return scheme is "opc.tcp" or "opc.https" or "opc.wss";
        }

        private static bool TryReadHrefPathNodeId(string href, out string nodeId)
        {
            nodeId = string.Empty;
            string path;
            if (Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute))
            {
                path = absolute.AbsolutePath.Trim('/');
            }
            else
            {
                int query = href.IndexOf('?', StringComparison.Ordinal);
                int fragment = href.IndexOf('#', StringComparison.Ordinal);
                int end = query < 0 ? href.Length : query;
                if (fragment >= 0 && fragment < end)
                {
                    end = fragment;
                }
                path = href[..end].Trim('/');
            }
            if (path.Length == 0)
            {
                return false;
            }
            string decoded = Uri.UnescapeDataString(path);
            if (!ExpandedNodeId.TryParse(decoded, out _))
            {
                return false;
            }
            nodeId = decoded;
            return true;
        }

        private static bool TryCanonicalPublishedNodeId(
            WotDocument document,
            JsonElement carryingNode,
            string value,
            out string? canonical,
            out string? error)
        {
            canonical = null;
            error = null;
            if (WotPortableIdentity.IsPortableNodeId(value) &&
                ExpandedNodeId.TryParse(value, out ExpandedNodeId expanded) &&
                !expanded.IsNull)
            {
                canonical = WotPortableIdentity.CanonicalNodeId(value);
                return true;
            }
            if (!value.StartsWith("ns=", StringComparison.Ordinal))
            {
                error = "A published target NodeId must use portable syntax or a mapped ns=<index> form.";
                return false;
            }
            int separator = value.IndexOf(';', StringComparison.Ordinal);
            ushort namespaceIndex = 0;
#if NETFRAMEWORK || NETSTANDARD2_0
            bool parsedNamespace = separator >= 3 &&
                ushort.TryParse(
                    value.Substring(3, separator - 3),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out namespaceIndex);
#else
            bool parsedNamespace = separator >= 3 &&
                ushort.TryParse(
                    value.AsSpan(3, separator - 3),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out namespaceIndex);
#endif
            if (separator < 3 ||
                !parsedNamespace ||
                !NodeId.TryParse(value[(separator + 1)..], out NodeId nodeId) ||
                nodeId.NamespaceIndex != 0 ||
                nodeId.IsNull)
            {
                error = "A published numeric target NodeId is malformed or null.";
                return false;
            }
            if (namespaceIndex == 0)
            {
                canonical = nodeId.ToString();
                return true;
            }
            if (!document.TryGetContextPrefix(
                namespaceIndex.ToString(CultureInfo.InvariantCulture),
                out string namespaceUri,
                carryingNode))
            {
                error = $"The original document context does not map namespace index {namespaceIndex}.";
                return false;
            }
            canonical = WotPortableIdentity.CanonicalNodeId(
                "nsu=" + CoreUtils.EscapeUri(namespaceUri) + ";" + nodeId);
            return true;
        }

        /// <summary>
        /// Tries to get the captured browse path target for the form.
        /// </summary>
        /// <param name="context">The WoT binding plan context.</param>
        /// <param name="target">The WoT browse path target.</param>
        /// <param name="error">The error message when no target is available.</param>
        internal bool TryGetBrowsePath(
            WotBindingPlanContext context, out WotBrowsePathTarget? target, out string? error)
        {
            target = BrowsePathTarget;
            error = BrowsePathError;
            if (HasBrowsePathCapture)
            {
                return error is null;
            }
            if (FormElement.ValueKind != JsonValueKind.Object ||
                (!FormElement.TryGetProperty(WotBrowsePathTarget.PathTerm, out _) &&
                    (AffordanceElement.ValueKind != JsonValueKind.Object ||
                        !AffordanceElement.TryGetProperty(WotBrowsePathTarget.PathTerm, out _))))
            {
                return true;
            }
            string collection = Kind switch
            {
                WotAffordanceKind.Action => "actions",
                WotAffordanceKind.Event => "events",
                _ => "properties"
            };
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("@context");
                foreach (KeyValuePair<string, string> prefix in context.NamespacePrefixes)
                {
                    writer.WriteString(prefix.Key, prefix.Value);
                }
                writer.WriteEndObject();
                writer.WriteStartObject(collection);
                writer.WriteStartObject("target");
                if (AffordanceElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in AffordanceElement.EnumerateObject())
                    {
                        if (property.Name != "forms")
                        {
                            property.WriteTo(writer);
                        }
                    }
                }
                writer.WriteStartArray("forms");
                FormElement.WriteTo(writer);
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            using var document = WotDocument.Parse(stream.ToArray());
            Capture(
                document,
                "/" + collection + "/target/forms/0",
                out target,
                out error,
                context.ProcessingMode == WotBindingProcessingMode.PublishedOneZero);
            return error is null;
        }

        private static void Capture(
            WotDocument document,
            string pointer,
            out WotBrowsePathTarget? target,
            out string? error,
            bool allowNumericNamespacePrefixes = false)
        {
            try
            {
                target = WotBrowsePathTarget.FromForm(
                    document, pointer, allowNumericNamespacePrefixes: allowNumericNamespacePrefixes);
                error = null;
            }
            catch (ServiceResultException exception)
            {
                target = null;
                error = exception.Message;
            }
        }
    }
}
