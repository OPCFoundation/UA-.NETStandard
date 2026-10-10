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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Deliberately limited adapter for unextended servers. It reads the actual
    /// hierarchy and never infers layout from a version label or communication failure.
    /// </summary>
    internal sealed partial class XRegistryBaseOpcUaEndpoint(
        ISession session, NodeId root, XRegistryBridgeNativeOptions options, ITelemetryContext telemetry)
        : IXRegistryEndpoint
    {
        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            Inventory inventory = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return inventory.Description;
        }

        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            if (request.IsMutation ||
                request.ExpectedVersionIncarnation is not null ||
                request.ExpectedGeneration is not null ||
                request.AddressPath is not null)
            {
                return Unsupported(
                    "The base native binding cannot implement HTTP mutations, incarnation or generation guards.");
            }
            if (request.Action == XRegistryAction.Describe)
            {
                return new XRegistryResponse(204)
                {
                    AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
                };
            }
            foreach (XRegistryParameter parameter in request.Parameters)
            {
                if (s_unsupportedFlags.Contains(parameter.Name))
                {
                    return Unsupported($"The base native profile does not implement the '{parameter.Name}' flag.");
                }
            }
            Inventory inventory = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (request.Path is "/modelsource" or "/capabilitiesoffered" or "/export")
            {
                return Unsupported("This aspect has no faithfully discovered base-native operation.");
            }
            if (!inventory.Entries.TryGetValue(request.Path, out Entry? entry))
            {
                if (inventory.Collections.Contains(request.Path))
                {
                    var entities = new JsonObject();
                    string prefix = request.Path + "/";
                    foreach (KeyValuePair<string, Entry> candidate in inventory.Entries)
                    {
                        if (candidate.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                            candidate.Key.AsSpan(prefix.Length).IndexOf('/') < 0)
                        {
                            if (candidate.Value.Unsupported)
                            {
                                return Unsupported("A legacy resource has no authoritative default-version mapping.");
                            }
                            entities[XRegistryPath.GetSegments(candidate.Key)[^1]] =
                                JsonNode.Parse(candidate.Value.Metadata.GetRawText());
                        }
                    }
                    return new XRegistryResponse(200) { Metadata = XRegistryNativeJson.Element(entities) };
                }
                return new XRegistryResponse(404)
                {
                    Error = new XRegistryError("not_found", "The entity was not found.")
                };
            }
            if (entry.Unsupported)
            {
                return Unsupported(request.Path == "/"
                    ? "The native root omits required Epoch or SpecVersion metadata; no values are fabricated."
                    : "The runtime layout does not identify the logical resource's default or Meta.");
            }
            if (request.View == XRegistryView.Default && !entry.DocumentNode.IsNull)
            {
                ResourceTypeClient resource = inventory.Client.GetResource(entry.DocumentNode);
                uint handle = await resource.OpenAsync(1, cancellationToken).ConfigureAwait(false);
                try
                {
                    ByteString bytes = await XRegistryOpcUaEndpoint.ReadFileAsync(resource, handle,
                        XRegistryOpcUaEndpoint.ChunkSize(session, options), options.MaxDocumentBytes,
                        cancellationToken).ConfigureAwait(false);
                    return new XRegistryResponse(200)
                    {
                        Metadata = entry.Metadata,
                        Document = bytes,
                        ContentType = XRegistryNativeJson.Text(entry.Metadata, "contenttype") is { Length: > 0 } type
                            ? type : null
                    };
                }
                finally
                {
                    await resource.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
                }
            }
            return new XRegistryResponse(200) { Metadata = entry.Metadata };
        }

        private async ValueTask<Inventory> LoadAsync(CancellationToken ct)
        {
            var client = new GenericXRegistryClient(
                session, XRegistryWellKnown.XRegistryNamespaceUri, root, telemetry);
            ArrayOf<ReferenceDescription> rootChildren = await ChildrenAsync(root, ct).ConfigureAwait(false);
            JsonElement model = options.BaseModel;
            ReferenceDescription? modelFile = Child(rootChildren, "Model");
            if (modelFile is not null)
            {
                var file = new FileTypeClient(session, Local(modelFile), telemetry);
                model = await ReadJsonFileAsync(file, ct).ConfigureAwait(false);
            }
            JsonElement definitions = XRegistryNativeJson.RequiredObject(model, "groups");
            JsonObject registry = await MetadataAsync(rootChildren, EntityKind.Registry, ct).ConfigureAwait(false);
            ValidateAttributeMapping(model, "/");
            await ReadMappedAttributesAsync(root, rootChildren, EntityKind.Registry, registry, model, "/", ct)
                .ConfigureAwait(false);
            string registryId = XRegistryNativeJson.RequiredText(XRegistryNativeJson.Element(registry), "registryid");
            JsonElement capabilities = default;
            if (Child(rootChildren, "Capabilities") is { } capabilitiesFile)
            {
                var file = new FileTypeClient(session, Local(capabilitiesFile), telemetry);
                JsonElement raw = await ReadJsonFileAsync(file, ct).ConfigureAwait(false);
                var limited = (JsonObject)JsonNode.Parse(raw.GetRawText())!;
                limited["mutable"] = new JsonArray();
                limited["flags"] = new JsonArray();
                limited["apis"] = new JsonArray();
                capabilities = XRegistryNativeJson.Element(limited);
            }
            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal)
            {
                ["/"] = new(XRegistryNativeJson.Element(registry), NodeId.Null,
                    !registry.ContainsKey("epoch") || !registry.ContainsKey("specversion")),
                ["/model"] = new(model, NodeId.Null)
            };
            if (capabilities.ValueKind != JsonValueKind.Undefined)
            {
                entries["/capabilities"] = new Entry(capabilities, NodeId.Null);
            }
            var collections = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty definition in definitions.EnumerateObject())
            {
                collections.Add(XRegistryPath.FromSegments([definition.Name]));
            }
            bool legacy = false;
            foreach (ReferenceDescription group in rootChildren.ToArray() ?? [])
            {
                if (!await IsTypeAsync(group, ObjectTypeIds.GroupType, ct).ConfigureAwait(false))
                {
                    continue;
                }
                NodeId groupNode = Local(group);
                ArrayOf<ReferenceDescription> groupChildren = await ChildrenAsync(groupNode, ct).ConfigureAwait(false);
                JsonObject metadata = await MetadataAsync(groupChildren, EntityKind.Group, ct).ConfigureAwait(false);
                string id = Required(metadata, "groupid");
                string groupPath = ResolveEntityPath(metadata, definitions, id, "/");
                string collection = XRegistryPath.GetSegments(groupPath)[0];
                JsonElement definition = definitions.GetProperty(collection);
                ValidateAttributeMapping(definition, groupPath);
                metadata.Remove("groupid");
                metadata[XRegistryNativeJson.RequiredText(definition, "singular") + "id"] = id;
                metadata["xid"] = groupPath;
                await ReadMappedAttributesAsync(
                    groupNode, groupChildren, EntityKind.Group, metadata, definition, groupPath, ct)
                    .ConfigureAwait(false);
                entries.Add(groupPath, new Entry(XRegistryNativeJson.Element(metadata), NodeId.Null));
                JsonElement resourceDefinitions = XRegistryNativeJson.RequiredObject(definition, "resources");
                foreach (JsonProperty resourceDefinition in resourceDefinitions.EnumerateObject())
                {
                    collections.Add(groupPath + XRegistryPath.FromSegments([resourceDefinition.Name]));
                }
                foreach (ReferenceDescription resource in groupChildren.ToArray() ?? [])
                {
                    if (!await IsTypeAsync(resource, ObjectTypeIds.ResourceType, ct).ConfigureAwait(false))
                    {
                        continue;
                    }
                    NodeId resourceNode = Local(resource);
                    ArrayOf<ReferenceDescription> children = await ChildrenAsync(resourceNode, ct)
                        .ConfigureAwait(false);
                    JsonObject resourceMetadata = await MetadataAsync(children, EntityKind.Resource, ct)
                        .ConfigureAwait(false);
                    string resourceId = Required(resourceMetadata, "resourceid");
                    string resourcePath = ResolveEntityPath(
                        resourceMetadata, resourceDefinitions, resourceId, groupPath);
                    string resourceCollection = XRegistryPath.GetSegments(resourcePath)[2];
                    JsonElement resourceDefinition = resourceDefinitions.GetProperty(resourceCollection);
                    string resourceIdAttribute =
                        XRegistryNativeJson.RequiredText(resourceDefinition, "singular") + "id";
                    resourceMetadata.Remove("resourceid");
                    resourceMetadata[resourceIdAttribute] = resourceId;
                    collections.Add(resourcePath + "/versions");
                    ReferenceDescription? versions = Child(children, "Versions");
                    if (versions is null)
                    {
                        legacy = true;
                        string version = Required(resourceMetadata, "versionid");
                        string versionPath = resourcePath + "/versions" + XRegistryPath.FromSegments([version]);
                        resourceMetadata["xid"] = versionPath;
                        ValidateAttributeMapping(resourceDefinition, versionPath);
                        await ReadMappedAttributesAsync(
                            resourceNode, children, EntityKind.Resource,
                            resourceMetadata, resourceDefinition, versionPath, ct)
                            .ConfigureAwait(false);
                        AddVersion(entries, versionPath, resourceMetadata, resourceNode, resourceDefinition);
                        entries[resourcePath] = new Entry(default, NodeId.Null, true);
                        entries[resourcePath + "/meta"] = new Entry(default, NodeId.Null, true);
                        continue;
                    }
                    if (!await IsTypeAsync(versions, ObjectTypeIds.ResourceVersionsType, ct)
                        .ConfigureAwait(false))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Versions has the wrong type.");
                    }
                    string defaultVersion = Required(resourceMetadata, "versionid");
                    JsonObject meta = await MetadataAsync(children, EntityKind.Meta, ct).ConfigureAwait(false);
                    meta[resourceIdAttribute] = resourceId;
                    meta["defaultversionid"] = defaultVersion;
                    meta["xid"] = resourcePath + "/meta";
                    ValidateAttributeMapping(resourceDefinition, resourcePath + "/meta");
                    await ReadMappedAttributesAsync(
                        resourceNode, children, EntityKind.Meta, meta, resourceDefinition, resourcePath + "/meta", ct)
                        .ConfigureAwait(false);
                    entries.Add(resourcePath + "/meta", new Entry(XRegistryNativeJson.Element(meta), NodeId.Null));
                    ArrayOf<ReferenceDescription> versionChildren = await ChildrenAsync(Local(versions), ct)
                        .ConfigureAwait(false);
                    foreach (ReferenceDescription versionNode in versionChildren.ToArray() ?? [])
                    {
                        if (!await IsTypeAsync(versionNode, ObjectTypeIds.ResourceType, ct)
                            .ConfigureAwait(false))
                        {
                            continue;
                        }
                        ArrayOf<ReferenceDescription> versionProperties = await ChildrenAsync(Local(versionNode), ct)
                            .ConfigureAwait(false);
                        JsonObject versionMetadata = await MetadataAsync(versionProperties, EntityKind.Resource, ct)
                            .ConfigureAwait(false);
                        string version = Required(versionMetadata, "versionid");
                        string versionPath = resourcePath + "/versions" + XRegistryPath.FromSegments([version]);
                        if (Required(versionMetadata, "resourceid") != resourceId)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidState, "A version has another resource ID.");
                        }
                        versionMetadata.Remove("resourceid");
                        versionMetadata[resourceIdAttribute] = resourceId;
                        versionMetadata["xid"] = versionPath;
                        ValidateAttributeMapping(resourceDefinition, versionPath);
                        await ReadMappedAttributesAsync(
                            Local(versionNode), versionProperties, EntityKind.Resource,
                            versionMetadata, resourceDefinition, versionPath, ct)
                            .ConfigureAwait(false);
                        AddVersion(entries, versionPath, versionMetadata, Local(versionNode), resourceDefinition);
                    }
                    string defaultPath = resourcePath + "/versions" + XRegistryPath.FromSegments([defaultVersion]);
                    if (!entries.TryGetValue(defaultPath, out Entry? selected))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The selected native default version was absent from the actual Versions folder.");
                    }
                    resourceMetadata["xid"] = resourcePath;
                    ValidateAttributeMapping(resourceDefinition, resourcePath);
                    await ReadMappedAttributesAsync(
                        resourceNode, children, EntityKind.Resource,
                        resourceMetadata, resourceDefinition, resourcePath, ct)
                        .ConfigureAwait(false);
                    entries.Add(resourcePath,
                        new Entry(XRegistryNativeJson.Element(resourceMetadata), selected.DocumentNode));
                }
                if (entries.Count > options.MaxEntities)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
            }
            return new Inventory(new XRegistryEndpointDescription(registryId)
            {
                Profile = legacy ? "native-base-readonly-flat-explicit-versions" :
                    entries["/"].Unsupported
                        ? "native-base-readonly-partial-root" : "native-base-readonly-distinct-core",
                Model = model,
                Capabilities = capabilities
            }, entries, collections, client);
        }

        private async ValueTask<JsonObject> MetadataAsync(
            ArrayOf<ReferenceDescription> children, EntityKind kind, CancellationToken ct)
        {
            var properties = new List<(ReferenceDescription Node, string Name)>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReferenceDescription reference in children)
            {
                if (reference.NodeClass != NodeClass.Variable ||
                    !IsBaseNamespace(reference.BrowseName.NamespaceIndex))
                {
                    continue;
                }
                string name = reference.BrowseName.Name
                    ?? throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
                if (kind == EntityKind.Meta)
                {
                    if (!name.StartsWith("Meta", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    name = name[4..];
                }
                else if (name.StartsWith("Meta", StringComparison.Ordinal))
                {
                    continue;
                }
                if (s_coreProperties.Contains(name))
                {
                    if (!names.Add(name))
                    {
                        throw new ServiceResultException(StatusCodes.BadNotSupported,
                            "The configured base-property namespaces contain ambiguous fields.");
                    }
                    properties.Add((reference, name.ToLowerInvariant()));
                }
            }
            var metadata = new JsonObject();
            if (properties.Count != 0)
            {
                ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [.. properties.Select(property => new ReadValueId
                    {
                        NodeId = Local(property.Node),
                        AttributeId = Attributes.Value
                    })], ct).ConfigureAwait(false);
                if (response.Results.Count != properties.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }
                for (int index = 0; index < properties.Count; index++)
                {
                    DataValue data = response.Results[index];
                    if (data.StatusCode == StatusCodes.BadNoData)
                    {
                        continue;
                    }
                    if (StatusCode.IsBad(data.StatusCode))
                    {
                        throw new ServiceResultException(data.StatusCode);
                    }
                    string name = properties[index].Name;
                    if (data.WrappedValue.IsNull)
                    {
                        continue;
                    }
                    if (data.WrappedValue.TryGetValue(out string text))
                    {
                        metadata[name] = text;
                    }
                    else if (data.WrappedValue.TryGetValue(out uint epoch))
                    {
                        metadata[name] = epoch;
                    }
                    else if (data.WrappedValue.TryGetValue(out DateTimeUtc timestamp))
                    {
                        metadata[name] = timestamp.ToString("O", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "A base property has an unrepresentable runtime type.");
                    }
                }
            }
            if (kind != EntityKind.Registry || metadata.ContainsKey("epoch"))
            {
                _ = XRegistryNativeJson.Epoch(XRegistryNativeJson.Element(metadata));
            }
            return metadata;
        }

        private async ValueTask<JsonElement> ReadJsonFileAsync(FileTypeClient file, CancellationToken ct)
        {
            uint handle = await file.OpenAsync(1, ct).ConfigureAwait(false);
            try
            {
                ByteString bytes = await XRegistryOpcUaEndpoint.ReadFileAsync(file, handle,
                    XRegistryOpcUaEndpoint.ChunkSize(session, options), options.MaxMessageBytes, ct)
                    .ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes.Memory);
                XRegistryNativeJson.RequireObject(document.RootElement);
                return document.RootElement.Clone();
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async ValueTask<bool> IsTypeAsync(
            ReferenceDescription reference, ExpandedNodeId expected, CancellationToken ct)
        {
            if (reference.NodeClass != NodeClass.Object)
            {
                return false;
            }
            var type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, session.NamespaceUris);
            var expectedType = ExpandedNodeId.ToNodeId(expected, session.NamespaceUris);
            if (type == expectedType || session.TypeTree.IsTypeOf(type, expectedType))
            {
                return true;
            }
            if (!session.TypeTree.IsKnown(type))
            {
                await session.FetchTypeTreeAsync(expected, ct).ConfigureAwait(false);
            }
            return session.TypeTree.IsTypeOf(type, expectedType);
        }

        private ReferenceDescription? Child(ArrayOf<ReferenceDescription> children, string name)
        {
            ReferenceDescription[] matches = [.. (children.ToArray() ?? []).Where(reference =>
                reference.BrowseName.Name == name &&
                IsBaseNamespace(reference.BrowseName.NamespaceIndex))];
            return matches.Length switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new ServiceResultException(StatusCodes.BadNotSupported, "A base child is ambiguous.")
            };
        }

        private ValueTask<ArrayOf<ReferenceDescription>> ChildrenAsync(NodeId node, CancellationToken ct)
        {
            return XRegistryOpcUaEndpoint.BrowseAsync(session, node, options, ct);
        }

        private bool IsBaseNamespace(ushort index)
        {
            string? uri = session.NamespaceUris.GetString(index);
            return uri == XRegistryWellKnown.XRegistryNamespaceUri ||
                options.BasePropertyNamespaceUris.ToList().Contains(uri, StringComparer.Ordinal);
        }

        private NodeId Local(ReferenceDescription reference)
        {
            return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
        }

        private static string ResolveEntityPath(JsonObject metadata, JsonElement definitions, string id, string parent)
        {
            JsonElement values = XRegistryNativeJson.Element(metadata);
            string xid = XRegistryNativeJson.Text(values, "xid");
            int expectedSegments = parent == "/" ? 2 : 4;
            if (xid.Length != 0)
            {
                ArrayOf<string> segments = XRegistryPath.GetSegments(xid);
                if (segments.Count == expectedSegments + 2 && segments[^2] == "versions")
                {
                    segments = (segments.ToArray() ?? []).Take(expectedSegments).ToArray();
                }
                if (segments.Count != expectedSegments ||
                    segments[^1] != id ||
                    !definitions.TryGetProperty(segments[^2], out _) ||
                    (parent != "/" &&
                        XRegistryPath.FromSegments(
                            (segments.ToArray() ?? []).Take(expectedSegments - 2).ToArray()) != parent))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The native Xid cannot be mapped to the supplied collection model without renaming.");
                }
                return XRegistryPath.FromSegments(segments);
            }
            JsonProperty[] candidates = [.. definitions.EnumerateObject()];
            if (candidates.Length != 1)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "A native entity without Xid cannot disambiguate multiple collections.");
            }
            return parent.TrimEnd('/') + XRegistryPath.FromSegments([candidates[0].Name, id]);
        }

        private void ValidateAttributeMapping(JsonElement definition, string path)
        {
            ArrayOf<XRegistryNativeAttributeMapping> mappings =
                [.. options.AttributeMappings.ToList().Where(mapping => mapping.Matches(path))];
            string attributeName = XRegistryPath.GetSegments(path).Count == 5 ? "metaattributes" : "attributes";
            if (definition.TryGetProperty(attributeName, out JsonElement attributes))
            {
                foreach (JsonProperty attribute in attributes.EnumerateObject())
                {
                    string? singular = definition.TryGetProperty("singular", out JsonElement singularValue)
                        ? singularValue.GetString() : null;
                    bool collection = (definition.TryGetProperty("resources", out JsonElement resources) ||
                        definition.TryGetProperty("groups", out resources)) &&
                        resources.EnumerateObject().Any(resource => attribute.Name == resource.Name ||
                            attribute.Name == resource.Name + "url" ||
                            attribute.Name == resource.Name + "count");
                    if (!s_mappedAttributes.Contains(attribute.Name) &&
                        !s_standardAttributes.Contains(attribute.Name) &&
                        attribute.Name != singular + "id" &&
                        attribute.Name != singular &&
                        attribute.Name != singular + "url" &&
                        attribute.Name != singular + "base64" &&
                        !collection &&
                        !XRegistryNativeAttributePlan.CoversDefinition(attribute.Value, [attribute.Name], mappings))
                    {
                        throw new ServiceResultException(StatusCodes.BadNotSupported,
                            $"The model attribute '{attribute.Name}' needs a typed domain mapping or the extension.");
                    }
                }
            }
        }

        private static void AddVersion(
            Dictionary<string, Entry> entries, string path, JsonObject metadata,
            NodeId node, JsonElement definition)
        {
            if (entries.ContainsKey(path))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Two native nodes identify the same version.");
            }
            bool hasDocument = !definition.TryGetProperty("hasdocument", out JsonElement value) ||
                value.ValueKind == JsonValueKind.True;
            entries.Add(path, new Entry(XRegistryNativeJson.Element(metadata), hasDocument ? node : NodeId.Null));
        }

        private static string Required(JsonObject metadata, string name)
        {
            return XRegistryNativeJson.RequiredText(XRegistryNativeJson.Element(metadata), name);
        }

        private static XRegistryResponse Unsupported(string detail)
        {
            return new XRegistryResponse(405)
            {
                Error = new XRegistryError("action_not_supported", detail),
                AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
            };
        }

        private enum EntityKind
        {
            Registry,
            Group,
            Resource,
            Meta
        }

        private sealed record Entry(JsonElement Metadata, NodeId DocumentNode, bool Unsupported = false);

        private sealed record Inventory(
            XRegistryEndpointDescription Description, Dictionary<string, Entry> Entries,
            HashSet<string> Collections, GenericXRegistryClient Client);

        private static readonly HashSet<string> s_coreProperties = new(StringComparer.Ordinal)
        {
            "RegistryId", "GroupId", "ResourceId", "VersionId", "SpecVersion", "Xid",
            "Epoch", "Name", "Description", "CreatedAt", "ModifiedAt", "Format", "ContentType"
        };

        private static readonly HashSet<string> s_mappedAttributes = new(StringComparer.Ordinal)
        {
            "name", "description", "labels", "createdat", "modifiedat", "format", "contenttype"
        };

        private static readonly HashSet<string> s_standardAttributes = new(StringComparer.Ordinal)
        {
            "registryid", "versionid", "self", "shortself", "xid", "epoch", "specversion", "documentation",
            "icon", "deprecated", "constraints", "isdefault", "ancestorid", "xref", "compatibility",
            "formatvalidated", "formatvalidatedreason",
            "compatibilityvalidated", "compatibilityvalidatedreason", "meta", "metaurl", "versions", "versionsurl",
            "versionscount", "defaultversionid", "defaultversionsticky", "defaultversionurl", "readonly",
            "model", "modelsource", "capabilities", "capabilitiesoffered", "xregcorrelationid"
        };

        private static readonly HashSet<string> s_unsupportedFlags = new(StringComparer.Ordinal)
        {
            "inline", "filter", "sort", "doc", "binary", "collections", "ignore", "specversion", "epoch"
        };
    }
}
