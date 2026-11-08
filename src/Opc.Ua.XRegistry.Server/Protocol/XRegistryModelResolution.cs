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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Supplies explicitly approved model documents. Implementations must not
    /// forward registry credentials or resolve arbitrary request-controlled URLs.
    /// Includes are resolved only at startup or a modelsource mutation.
    /// </summary>
    public interface IXRegistryModelDocumentResolver
    {
        /// <summary>
        /// Resolves a document URI without a fragment. Missing/unauthorized
        /// documents must fail explicitly, never return an empty model.
        /// </summary>
        ValueTask<JsonElement> ResolveAsync(Uri documentUri, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One approved model document with owned JSON lifetime.
    /// </summary>
    public sealed record XRegistryModelDocument
    {
        /// <summary>
        /// Retains a validated absolute location and an owned document value.
        /// </summary>
        public XRegistryModelDocument(Uri location, JsonElement value)
        {
            Location = location.ThrowIfNull(nameof(location));
            if (!location.IsAbsoluteUri ||
                location.Fragment.Length != 0 ||
                location.UserInfo.Length != 0 ||
                value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("An absolute document URI and JSON object are required.");
            }
            Value = value.Clone();
        }

        /// <summary>
        /// Gets the approved document URI without fragment or credentials.
        /// </summary>
        public Uri Location { get; }

        /// <summary>
        /// Gets the document's immutable owned JSON.
        /// </summary>
        public JsonElement Value { get; }
    }

    /// <summary>
    /// Offline, allow-listed include resolution with no network or file access.
    /// </summary>
    public sealed class XRegistryModelDocumentResolver : IXRegistryModelDocumentResolver
    {
        /// <summary>
        /// Creates an offline catalog; duplicate document identities are rejected.
        /// </summary>
        public XRegistryModelDocumentResolver(ArrayOf<XRegistryModelDocument> documents)
        {
            foreach (XRegistryModelDocument document in documents)
            {
                document.ThrowIfNull(nameof(documents));
                m_documents.Add(document.Location.AbsoluteUri, document.Value);
            }
        }

        /// <inheritdoc/>
        public ValueTask<JsonElement> ResolveAsync(Uri documentUri, CancellationToken cancellationToken = default)
        {
            documentUri.ThrowIfNull(nameof(documentUri));
            cancellationToken.ThrowIfCancellationRequested();
            return m_documents.TryGetValue(documentUri.AbsoluteUri, out JsonElement value)
                ? new ValueTask<JsonElement>(value.Clone())
                : throw new XRegistryRejectionException(
                    "model_error", "The include document is not in the approved model catalog.");
        }

        private readonly Dictionary<string, JsonElement> m_documents = new(StringComparer.Ordinal);
    }

    internal sealed class XRegistryModelExpansion(XRegistryTransactionalOptions options)
    {
        public JsonObject ResourceOrigins { get; private set; } = [];

        public async ValueTask<JsonObject> ExpandAsync(JsonObject source, CancellationToken ct)
        {
            Uri origin = options.ModelSourceUri ?? new Uri(options.PublicRoot, "modelsource");
            if (Encoding.UTF8.GetByteCount(source.ToJsonString()) > options.MaxModelBytes)
            {
                throw new XRegistryRejectionException("model_error", "The source model exceeds its byte limit.");
            }
            JsonNode expanded =
                await ExpandAsync(source, source, origin, new HashSet<string>(StringComparer.Ordinal), 0, ct)
                .ConfigureAwait(false);
            var model = new XRegistryModelRules(XRegistryModelRules.Object(expanded));
            ResourceOrigins = (JsonObject)model.ResourceOrigins.DeepClone();
            _ = new Opc.Ua.XRegistry.Protocol.XRegistryProtocolCodec(options.MaxModelBytes).EncodeJson(model.Model);
            return model.Model;
        }

        private async ValueTask<JsonNode> ExpandAsync(JsonNode node, JsonObject document, Uri location,
            HashSet<string> active, int depth, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > options.MaxModelDepth)
            {
                throw new XRegistryRejectionException("model_error", "The model include depth limit was exceeded.");
            }
            if (node is JsonArray array)
            {
                var result = new JsonArray();
                foreach (JsonNode? child in array)
                {
                    JsonNode? value = child is null ? null :
                        await ExpandAsync(child, document, location, active, depth + 1, ct).ConfigureAwait(false);
                    result.Add(value);
                }
                return result;
            }
            if (node is not JsonObject obj)
            {
                return node.DeepClone();
            }
            var output = new JsonObject();
            if (obj.ContainsKey("$include") && obj.ContainsKey("$includes"))
            {
                throw new XRegistryRejectionException("model_error", "Use either $include or $includes, not both.");
            }
            var references = new List<string>();
            if (obj["$include"] is JsonNode single)
            {
                references.Add(XRegistryModelRules.Text(single));
            }
            if (obj["$includes"] is JsonNode included)
            {
                if (included is not JsonArray sequence)
                {
                    throw new XRegistryRejectionException("model_error", "$includes must be an array.");
                }
                foreach (JsonNode? value in sequence)
                {
                    references.Add(XRegistryModelRules.Text(value));
                }
            }
            foreach (string reference in references)
            {
                if (++m_includes > options.MaxModelDocuments ||
                    !Uri.TryCreate(reference, UriKind.RelativeOrAbsolute, out Uri? includeUri) ||
                    !Uri.TryCreate(location, includeUri, out Uri? resolved) ||
                    resolved.UserInfo.Length != 0 ||
                    !active.Add(resolved.AbsoluteUri))
                {
                    throw new XRegistryRejectionException(
                        "model_error", "An include is cyclic, invalid or exceeds its bound.");
                }
                var uri = new UriBuilder(resolved) { Fragment = string.Empty }.Uri;
                JsonObject source;
                if (uri == new UriBuilder(location) { Fragment = string.Empty }.Uri)
                {
                    source = document;
                }
                else if (!m_documents.TryGetValue(uri.AbsoluteUri, out source!))
                {
                    IXRegistryModelDocumentResolver resolver = options.ModelResolver
                        ?? throw new XRegistryRejectionException(
                            "model_error", "No approved model resolver is configured.");
                    JsonElement fetched = await resolver.ResolveAsync(uri, ct).ConfigureAwait(false);
                    if (fetched.ValueKind != JsonValueKind.Object ||
                        Encoding.UTF8.GetByteCount(fetched.GetRawText()) > options.MaxModelBytes)
                    {
                        throw new XRegistryRejectionException(
                            "model_error", "The model include is not a bounded JSON object.");
                    }
                    source = XRegistryModelRules.Object(JsonNode.Parse(fetched.GetRawText()));
                    m_documents.Add(uri.AbsoluteUri, source);
                }
                JsonObject fragment = SelectObject(source, resolved.Fragment);
                JsonObject part = XRegistryModelRules.Object(
                    await ExpandAsync(fragment, source, uri, active, depth + 1, ct).ConfigureAwait(false));
                foreach ((string name, JsonNode? value) in part)
                {
                    if (!output.ContainsKey(name))
                    {
                        output[name] = value?.DeepClone();
                    }
                }
                active.Remove(resolved.AbsoluteUri);
            }
            foreach ((string name, JsonNode? value) in obj)
            {
                if (name is not ("$include" or "$includes"))
                {
                    output[name] = value is null ? null :
                        await ExpandAsync(value, document, location, active, depth + 1, ct).ConfigureAwait(false);
                }
            }
            return output;
        }

        private static JsonObject SelectObject(JsonObject document, string fragment)
        {
            if (fragment.Length == 0 || fragment == "#")
            {
                return document;
            }
            string pointer = Uri.UnescapeDataString(fragment[1..]);
            if (!pointer.StartsWith('/'))
            {
                throw new XRegistryRejectionException("model_error", "An include fragment must be a JSON Pointer.");
            }
            JsonNode? current = document;
            foreach (string segment in pointer[1..].Split('/'))
            {
                for (int index = 0; index < segment.Length; index++)
                {
                    if (segment[index] == '~' && (++index >= segment.Length || segment[index] is not ('0' or '1')))
                    {
                        throw new XRegistryRejectionException("model_error", "Invalid JSON Pointer escape.");
                    }
                }
                string key =
                    segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                current = current switch
                {
                    JsonObject obj => obj[key],
                    JsonArray array when (key.Length == 1 || (key.Length > 1 && key[0] != '0')) &&
                        int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                        index < array.Count => array[index],
                    _ => null
                };
            }
            return current as JsonObject
                ?? throw new XRegistryRejectionException(
                    "model_error", "The include pointer does not reference an object.");
        }

        private int m_includes;
        private readonly Dictionary<string, JsonObject> m_documents = new(StringComparer.Ordinal);
    }
}
