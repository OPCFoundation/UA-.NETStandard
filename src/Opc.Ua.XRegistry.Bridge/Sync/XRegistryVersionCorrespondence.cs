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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// One logical Version identity and its independently assigned endpoint addresses.
    /// All three addresses must remain in the same Resource; collection/type remapping is not implied.
    /// </summary>
    public sealed record XRegistryVersionCorrespondence
    {
        /// <summary>
        /// Validates a one-to-one correspondence between exact Version addresses.
        /// </summary>
        public XRegistryVersionCorrespondence(string canonicalPath, string opcUaPath, string httpPath)
        {
            CanonicalPath = XRegistryPath.Normalize(canonicalPath);
            OpcUaPath = XRegistryPath.Normalize(opcUaPath);
            HttpPath = XRegistryPath.Normalize(httpPath);
            ArrayOf<string> canonical = XRegistryPath.GetSegments(CanonicalPath);
            ArrayOf<string> native = XRegistryPath.GetSegments(OpcUaPath);
            ArrayOf<string> http = XRegistryPath.GetSegments(HttpPath);
            if (canonical.Count != 6 ||
                native.Count != 6 ||
                http.Count != 6 ||
                canonical[4] != "versions" ||
                !canonical.Span[..5].SequenceEqual(native.Span[..5]) ||
                !canonical.Span[..5].SequenceEqual(http.Span[..5]))
            {
                throw new ArgumentException("Correspondence must identify Versions of the same Resource.");
            }
        }

        /// <summary>
        /// Gets the stable job-local Version path.
        /// </summary>
        public string CanonicalPath { get; }

        /// <summary>
        /// Gets the actual OPC UA Version path.
        /// </summary>
        public string OpcUaPath { get; }

        /// <summary>
        /// Gets the actual HTTP Version path.
        /// </summary>
        public string HttpPath { get; }
    }

    internal sealed class XRegistrySyncCorrespondence(
        IDictionary<string, XRegistryVersionCorrespondence> mappings, XRegistrySyncSide side)
    {
        public void SetDescription(XRegistryEndpointDescription description)
        {
            m_model = description.Model.ValueKind == JsonValueKind.Object
                ? XRegistrySyncJson.Object(description.Model) : null;
            m_publicRoot = description.PublicRoot;
        }

        public string Address(string path)
        {
            return mappings.TryGetValue(path, out XRegistryVersionCorrespondence? mapping)
                ? side == XRegistrySyncSide.OpcUa ? mapping.OpcUaPath : mapping.HttpPath : path;
        }

        public string Canonical(string path)
        {
            foreach (XRegistryVersionCorrespondence mapping in mappings.Values)
            {
                if ((side == XRegistrySyncSide.OpcUa ? mapping.OpcUaPath : mapping.HttpPath) == path)
                {
                    return mapping.CanonicalPath;
                }
            }
            return path;
        }

        public JsonElement Metadata(string path, JsonElement metadata, bool outbound)
        {
            if (mappings.Count == 0 || metadata.ValueKind != JsonValueKind.Object || path is "/model" or "/modelsource")
            {
                return metadata;
            }
            JsonObject value = XRegistrySyncJson.Object(metadata);
            Visit(value, path, outbound);
            return XRegistrySyncJson.Element(value);
        }

        public ArrayOf<XRegistryParameter> Parameters(string path, ArrayOf<XRegistryParameter> parameters)
        {
            if (mappings.Count == 0)
            {
                return parameters;
            }
            ArrayOf<string> segments = XRegistryPath.GetSegments(path);
            if (segments.Count < 4)
            {
                return parameters;
            }
            string resource = XRegistryPath.FromSegments(segments.Span[..4].ToArray());
            return [.. parameters.ToList().Select(parameter =>
                parameter.Name == "setdefaultversionid" && parameter.Value is not (null or "null" or "request")
                    ? parameter with { Value = VersionId(resource, parameter.Value, true) } : parameter)];
        }

        private void Visit(JsonObject value, string path, bool outbound)
        {
            ArrayOf<string> parts = XRegistryPath.GetSegments(path);
            if (parts.Count is 1 or 3 || (parts.Count == 5 && parts[4] == "versions"))
            {
                var replacement = new JsonObject();
                foreach ((string id, JsonNode? child) in value)
                {
                    string childPath = XRegistrySyncModel.Child(path, id);
                    string mapped = outbound ? Address(childPath) : Canonical(childPath);
                    string name = XRegistryPath.GetSegments(mapped)[^1];
                    if (replacement.ContainsKey(name))
                    {
                        throw new JsonException("Version correspondence would merge two observed identities.");
                    }
                    if (child is not JsonObject entity)
                    {
                        throw new JsonException("A registry collection must contain objects.");
                    }
                    var clone = (JsonObject)entity.DeepClone();
                    Visit(clone, childPath, outbound);
                    replacement[name] = clone;
                }
                value.Clear();
                foreach ((string name, JsonNode? child) in replacement)
                {
                    value[name] = child?.DeepClone();
                }
                return;
            }
            if (parts.Count >= 4)
            {
                string resource = XRegistryPath.FromSegments(parts.Span[..4].ToArray());
                string[] identityAttributes = parts.Count == 5
                    ? s_metaIdentityAttributes : s_versionIdentityAttributes;
                foreach (string name in identityAttributes)
                {
                    if (value[name] is JsonNode id &&
                        id.GetValue<string>() is string text &&
                        !(outbound && name == "ancestorid" && text == "request"))
                    {
                        value[name] = VersionId(resource, text, outbound);
                    }
                }
                if (value["meta"] is JsonObject meta)
                {
                    Visit(meta, resource + "/meta", outbound);
                }
                if (value["versions"] is JsonObject versions)
                {
                    Visit(versions, resource + "/versions", outbound);
                }
            }
            JsonObject? definition = parts.Count == 0 ? m_model :
                parts.Count == 2 ? m_model?["groups"]?[parts[0]] as JsonObject :
                parts.Count >= 4 ? m_model?["groups"]?[parts[0]]?["resources"]?[parts[2]] as JsonObject : null;
            MapAttributes(value, definition?[parts.Count == 5 ? "metaattributes" : "attributes"] as JsonObject,
                outbound, skipIdentityFields: true);
            foreach ((string key, JsonNode? child) in value.ToArray())
            {
                if (child is JsonObject collection &&
                    parts.Count is 0 or 2 &&
                    key is not ("model" or "modelsource" or "capabilities") &&
                    ((definition?[parts.Count == 0 ? "groups" : "resources"] is JsonObject types &&
                        types.ContainsKey(key)) ||
                        mappings.Keys.Any(version =>
                            version.StartsWith(XRegistrySyncModel.Child(path, key) + "/", StringComparison.Ordinal))))
                {
                    Visit(collection, XRegistrySyncModel.Child(path, key), outbound);
                }
            }
        }

        private string VersionId(string resource, string id, bool outbound)
        {
            string path = XRegistrySyncModel.Child(resource + "/versions", id);
            string mapped = outbound ? Address(path) : Canonical(path);
            return XRegistryPath.GetSegments(mapped)[^1];
        }

        private void MapAttributes(
            JsonObject value, JsonObject? attributes, bool outbound, bool skipIdentityFields = false)
        {
            if (attributes is null)
            {
                return;
            }
            foreach ((string name, JsonNode? child) in value.ToArray())
            {
                JsonObject? rule = attributes[name] as JsonObject ?? attributes["*"] as JsonObject;
                if (rule is null || (skipIdentityFields && name is "versionid" or "ancestorid" or "defaultversionid"))
                {
                    continue;
                }
                if (rule["ifvalues"] is JsonObject branches && child is JsonValue scalar)
                {
                    string key = scalar.GetValueKind() == JsonValueKind.String
                        ? scalar.GetValue<string>() : scalar.ToJsonString();
                    foreach ((string match, JsonNode? branch) in branches)
                    {
                        if (key.Equals(match, StringComparison.OrdinalIgnoreCase))
                        {
                            MapAttributes(value, branch?["siblingattributes"] as JsonObject, outbound);
                        }
                    }
                }
                value[name] = MapValue(child, rule, outbound);
            }
        }

        private JsonNode? MapValue(JsonNode? value, JsonObject rule, bool outbound)
        {
            string? type = rule["type"]?.GetValue<string>();
            if (value is JsonValue scalar &&
                scalar.TryGetValue(out string? text) &&
                text is not null &&
                (type == "xid" || (type is "uri" or "url" && rule["target"] is not null)))
            {
                string path = text;
                if (!text.StartsWith('/'))
                {
                    if (outbound ||
                        m_publicRoot is null ||
                        !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
                        uri.Scheme != m_publicRoot.Scheme ||
                        uri.IdnHost != m_publicRoot.IdnHost ||
                        uri.Port != m_publicRoot.Port ||
                        uri.Query.Length != 0 ||
                        uri.Fragment.Length != 0)
                    {
                        return value;
                    }
                    string prefix = m_publicRoot.AbsolutePath.TrimEnd('/');
                    if (uri.AbsolutePath != prefix &&
                        !uri.AbsolutePath.StartsWith(prefix + "/", StringComparison.Ordinal))
                    {
                        return value;
                    }
                    path = uri.AbsolutePath[prefix.Length..];
                }
                string mapped = outbound ? Address(path) : Canonical(path);
                return mapped == path && path == text ? value : JsonValue.Create(mapped);
            }
            if (type == "object" && value is JsonObject obj)
            {
                MapAttributes(obj, rule["attributes"] as JsonObject, outbound);
            }
            else if (type == "map" && value is JsonObject map && rule["item"] is JsonObject mapRule)
            {
                foreach ((string key, JsonNode? child) in map.ToArray())
                {
                    map[key] = MapValue(child, mapRule, outbound);
                }
            }
            else if (type == "array" && value is JsonArray array && rule["item"] is JsonObject itemRule)
            {
                for (int index = 0; index < array.Count; index++)
                {
                    JsonNode? original = array[index];
                    JsonNode? mapped = MapValue(original, itemRule, outbound);
                    if (!ReferenceEquals(original, mapped))
                    {
                        array[index] = mapped;
                    }
                }
            }
            return value;
        }

        private JsonObject? m_model;
        private Uri? m_publicRoot;
        private static readonly string[] s_metaIdentityAttributes = ["defaultversionid"];
        private static readonly string[] s_versionIdentityAttributes = ["versionid", "ancestorid"];
    }
}
