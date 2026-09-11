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
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    internal sealed record XRegistryNativeSnapshot(
        XRegistryEndpointDescription Description,
        JsonElement Root,
        ArrayOf<XRegistryNativeGroup> NativeGroups) : IXRegistryProjectionSnapshot
    {
        public ImmutableSortedDictionary<string, string> Labels => XRegistryNativeJson.Labels(Root);

        public IEnumerable<IXRegistryProjectionGroup> Groups => NativeGroups.ToArray() ?? [];

        public static async ValueTask<XRegistryNativeSnapshot> LoadAsync(
            IXRegistryEndpoint endpoint, XRegistryBridgeNativeOptions options, CancellationToken ct)
        {
            XRegistryCallContext context = options.ProjectionContext;
            XRegistryEndpointDescription description = await endpoint.InspectAsync(context, ct).ConfigureAwait(false);
            JsonElement groupDefinitions = XRegistryNativeJson.RequiredObject(description.Model, "groups");
            JsonElement root = await ReadAsync(endpoint, "/", context, ct).ConfigureAwait(false);
            _ = XRegistryNativeJson.Epoch(root);
            var groups = new List<XRegistryNativeGroup>();
            int count = 1;
            foreach (JsonProperty groupDefinition in groupDefinitions.EnumerateObject())
            {
                string singular = XRegistryNativeJson.RequiredText(groupDefinition.Value, "singular");
                JsonElement groupMap = await CollectionAsync(
                    endpoint, XRegistryPath.FromSegments([groupDefinition.Name]), context, options, ct)
                    .ConfigureAwait(false);
                foreach (JsonProperty item in groupMap.EnumerateObject())
                {
                    string groupPath = XRegistryPath.FromSegments([groupDefinition.Name, item.Name]);
                    XRegistryNativeJson.ValidateIdentity(item.Value, singular + "id", item.Name);
                    var resources = new List<XRegistryNativeResource>();
                    JsonElement resourceDefinitions = XRegistryNativeJson.RequiredObject(
                        groupDefinition.Value, "resources");
                    foreach (JsonProperty resourceDefinition in resourceDefinitions.EnumerateObject())
                    {
                        string resourceSingular = XRegistryNativeJson.RequiredText(
                            resourceDefinition.Value, "singular");
                        string collectionPath = groupPath + XRegistryPath.FromSegments([resourceDefinition.Name]);
                        JsonElement resourceMap = await CollectionAsync(
                            endpoint, collectionPath, context, options, ct).ConfigureAwait(false);
                        foreach (JsonProperty resource in resourceMap.EnumerateObject())
                        {
                            string path = collectionPath + XRegistryPath.FromSegments([resource.Name]);
                            XRegistryNativeJson.ValidateIdentity(
                                resource.Value, resourceSingular + "id", resource.Name);
                            JsonElement meta = await ReadAsync(endpoint, path + "/meta", context, ct)
                                .ConfigureAwait(false);
                            string defaultId = XRegistryNativeJson.RequiredText(meta, "defaultversionid");
                            JsonElement versions = await CollectionAsync(
                                endpoint, path + "/versions", context, options, ct).ConfigureAwait(false);
                            if (!versions.TryGetProperty(defaultId, out _))
                            {
                                throw new ServiceResultException(StatusCodes.BadInvalidState,
                                    "The authoritative default version was absent from the version inventory.");
                            }
                            foreach (JsonProperty version in versions.EnumerateObject())
                            {
                                XRegistryNativeJson.ValidateIdentity(version.Value, "versionid", version.Name);
                                resources.Add(new XRegistryNativeResource(
                                    groupPath, path, resource.Name, version.Name, resourceDefinition.Value,
                                    version.Value, meta, version.Name == defaultId));
                                if (++count > options.MaxEntities)
                                {
                                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                        "The complete projection exceeds its entity limit.");
                                }
                            }
                            if (++count > options.MaxEntities)
                            {
                                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                            }
                        }
                    }
                    groups.Add(new XRegistryNativeGroup(
                        groupPath, groupDefinition.Name, item.Name,
                        groupDefinition.Value, item.Value, [.. resources]));
                    if (++count > options.MaxEntities)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                }
            }
            return new XRegistryNativeSnapshot(description, root, [.. groups]);
        }

        internal static async ValueTask<JsonElement> ReadAsync(
            IXRegistryEndpoint endpoint, string path, XRegistryCallContext context, CancellationToken ct)
        {
            XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, path)
            {
                View = XRegistryView.Metadata,
                Context = context
            }, ct).ConfigureAwait(false);
            XRegistryNativeJson.EnsureSuccess(response);
            XRegistryNativeJson.RequireObject(response.Metadata);
            return response.Metadata;
        }

        private static async ValueTask<JsonElement> CollectionAsync(
            IXRegistryEndpoint endpoint, string path, XRegistryCallContext context,
            XRegistryBridgeNativeOptions options, CancellationToken ct)
        {
            var request = new XRegistryRequest(XRegistryAction.Read, path)
            {
                View = XRegistryView.Metadata,
                Context = context
            };
            var merged = new JsonObject();
            var seenPages = new HashSet<string>(StringComparer.Ordinal);
            for (int page = 0; page < options.MaxBrowsePages; page++)
            {
                XRegistryResponse response = await endpoint.ExecuteAsync(request, ct).ConfigureAwait(false);
                XRegistryNativeJson.EnsureSuccess(response);
                XRegistryNativeJson.RequireObject(response.Metadata);
                foreach (JsonProperty entity in response.Metadata.EnumerateObject())
                {
                    if (merged.ContainsKey(entity.Name))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "An inventory page repeated an entity; the scan is not complete.");
                    }
                    merged[entity.Name] = JsonNode.Parse(entity.Value.GetRawText());
                    if (merged.Count > options.MaxEntities)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                }
                XRegistryLink? next = (response.Links.ToArray() ?? [])
                    .FirstOrDefault(link => link.Relation == "next");
                if (next is null)
                {
                    return XRegistryNativeJson.Element(merged);
                }
                if (!seenPages.Add(next.Target))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "An inventory cursor repeated.");
                }
                int query = next.Target.IndexOf('?', StringComparison.Ordinal);
                string nextPath = query < 0 ? next.Target : next.Target[..query];
                if (!nextPath.StartsWith('/') ||
                    nextPath.StartsWith("//", StringComparison.Ordinal) ||
                    XRegistryPath.Normalize(nextPath) != path)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The endpoint must return a registry-relative, same-collection continuation link.");
                }
                var parameters = new List<XRegistryParameter>();
                if (query >= 0)
                {
                    foreach (string pair in next.Target[(query + 1)..].Split('&'))
                    {
                        string[] parts = pair.Split(['='], 2);
                        parameters.Add(new XRegistryParameter(
                            Uri.UnescapeDataString(parts[0]),
                            parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : null));
                    }
                }
                request = request with { Parameters = [.. parameters] };
            }
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                "The inventory page limit was reached; no partial projection was published.");
        }
    }

    internal sealed class XRegistryNativeGroup : IXRegistryProjectionGroup
    {
        public XRegistryNativeGroup(
            string path, string collection, string id, JsonElement definition,
            JsonElement metadata, ArrayOf<XRegistryNativeResource> resources)
        {
            Path = path;
            Collection = collection;
            Id = id;
            Definition = definition;
            Metadata = metadata;
            NativeResources = resources;
            Epoch = XRegistryNativeJson.Epoch(metadata);
            Labels = XRegistryNativeJson.Labels(metadata);
        }

        public string Path { get; }
        public string Collection { get; }
        public string Id { get; }
        public JsonElement Definition { get; }
        public JsonElement Metadata { get; }
        public ArrayOf<XRegistryNativeResource> NativeResources { get; }
        public string GroupId => Uri.EscapeDataString(Path);
        public string Xid => Path;
        public string Name => XRegistryNativeJson.Text(Metadata, "name");
        public string Description => XRegistryNativeJson.Text(Metadata, "description");
        public long Epoch { get; }
        public ImmutableSortedDictionary<string, string> Labels { get; }
        public IEnumerable<IXRegistryProjectionResource> Resources => NativeResources.ToArray() ?? [];
    }

    internal sealed class XRegistryNativeResource : IXRegistryProjectionResource, IXRegistryProjectionResourceMeta
    {
        public XRegistryNativeResource(
            string groupPath, string resourcePath, string id, string version,
            JsonElement definition, JsonElement metadata, JsonElement meta, bool isDefault)
        {
            GroupPath = groupPath;
            Path = resourcePath;
            Id = id;
            Version = version;
            Definition = definition;
            Metadata = metadata;
            Meta = meta;
            IsDefaultVersion = isDefault;
            Epoch = XRegistryNativeJson.Epoch(metadata);
            MetaEpoch = XRegistryNativeJson.Epoch(meta);
            Labels = XRegistryNativeJson.Labels(metadata);
            MetaLabels = XRegistryNativeJson.Labels(meta);
        }

        public string GroupPath { get; }
        public string Path { get; }
        public string Id { get; }
        public string Version { get; }
        public JsonElement Definition { get; }
        public JsonElement Metadata { get; }
        public JsonElement Meta { get; }
        public string VersionPath => Path + "/versions" + XRegistryPath.FromSegments([Version]);
        public string GroupId => Uri.EscapeDataString(GroupPath);
        public string ResourceId => Uri.EscapeDataString(Path);
        public string Xid => VersionPath;
        public string Name => XRegistryNativeJson.Text(Metadata, "name");
        public string Description => XRegistryNativeJson.Text(Metadata, "description");
        public string VersionId => Uri.EscapeDataString(Version);
        public string Format => XRegistryNativeJson.Text(Metadata, "format");
        public string ContentType => XRegistryNativeJson.Text(Metadata, "contenttype");
        public long Epoch { get; }
        public DateTime CreatedAt => XRegistryNativeJson.Timestamp(Metadata, "createdat");
        public DateTime ModifiedAt => XRegistryNativeJson.Timestamp(Metadata, "modifiedat");
        public ImmutableSortedDictionary<string, string> Labels { get; }
        public long MetaEpoch { get; }
        public ImmutableSortedDictionary<string, string> MetaLabels { get; }
        public DateTime MetaCreatedAt => XRegistryNativeJson.Timestamp(Meta, "createdat");
        public DateTime MetaModifiedAt => XRegistryNativeJson.Timestamp(Meta, "modifiedat");
        public bool IsDefaultVersion { get; }

        public bool HasDocument => !Definition.TryGetProperty("hasdocument", out JsonElement value) ||
            value.ValueKind == JsonValueKind.True;
    }

    internal static class XRegistryNativeJson
    {
        public static JsonElement Element(JsonNode value)
        {
            using var document = JsonDocument.Parse(value.ToJsonString());
            return document.RootElement.Clone();
        }

        public static ByteString Bytes(JsonElement value)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(value.GetRawText()));
        }

        public static JsonElement RequiredObject(JsonElement value, string name)
        {
            RequireObject(value);
            if (!value.TryGetProperty(name, out JsonElement property))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, $"The model has no '{name}' definition.");
            }
            RequireObject(property);
            return property;
        }

        public static void RequireObject(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "This native layout requires model-defined entity objects and collection maps.");
            }
        }

        public static string RequiredText(JsonElement value, string name)
        {
            string text = Text(value, name);
            if (text.Length == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, $"The required '{name}' is absent.");
            }
            return text;
        }

        public static string Text(JsonElement value, string name)
        {
            RequireObject(value);
            if (!value.TryGetProperty(name, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
            {
                return string.Empty;
            }
            if (property.ValueKind != JsonValueKind.String)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    $"The native string property '{name}' cannot represent this JSON type.");
            }
            return property.GetString()!;
        }

        public static uint Epoch(JsonElement value)
        {
            RequireObject(value);
            if (!value.TryGetProperty("epoch", out JsonElement epoch) ||
                epoch.ValueKind != JsonValueKind.Number ||
                !epoch.TryGetUInt32(out uint result))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The base native Epoch property requires an explicitly present UInt32 counter.");
            }
            return result;
        }

        public static DateTime Timestamp(JsonElement value, string name)
        {
            string text = Text(value, name);
            if (text.Length == 0)
            {
                return default;
            }
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "An entity timestamp is invalid.");
            }
            return parsed;
        }

        public static ImmutableSortedDictionary<string, string> Labels(JsonElement value)
        {
            if (!value.TryGetProperty("labels", out JsonElement labels) || labels.ValueKind == JsonValueKind.Null)
            {
                return ImmutableSortedDictionary<string, string>.Empty;
            }
            RequireObject(labels);
            ImmutableSortedDictionary<string, string>.Builder result = ImmutableSortedDictionary.CreateBuilder<string,
                string>(StringComparer.Ordinal);
            foreach (JsonProperty label in labels.EnumerateObject())
            {
                if (label.Value.ValueKind != JsonValueKind.String)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "AttributesType supports string labels, not stringified typed JSON attributes.");
                }
                result.Add(label.Name, label.Value.GetString()!);
            }
            return result.ToImmutable();
        }

        public static void ValidateIdentity(JsonElement value, string property, string identity)
        {
            if (RequiredText(value, property) != identity)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "An entity identity differs from its collection key.");
            }
        }

        public static void EnsureSuccess(XRegistryResponse response)
        {
            if (!response.IsSuccess)
            {
                throw new ServiceResultException(Status(response),
                    response.Error?.Detail ?? $"The endpoint rejected the request with status {response.StatusCode}.");
            }
        }

        public static StatusCode Status(XRegistryResponse response)
        {
            return response.Error?.Code == "mismatched_epoch"
                ? StatusCodes.BadInvalidState
                : response.StatusCode switch
                {
                    >= 200 and < 300 => StatusCodes.Good,
                    401 or 403 => StatusCodes.BadUserAccessDenied,
                    404 => StatusCodes.BadNotFound,
                    405 => StatusCodes.BadNotSupported,
                    409 => StatusCodes.BadAlreadyExists,
                    413 => StatusCodes.BadEncodingLimitsExceeded,
                    >= 500 => StatusCodes.BadNoCommunication,
                    _ => StatusCodes.BadInvalidArgument
                };
        }
    }
}
