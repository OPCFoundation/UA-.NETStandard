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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class WotCanonicalViewState
    {
        private static ByteString SerializeCanonicalState(WotCanonicalViewState state)
        {
            return ByteString.From(JsonSerializer.SerializeToUtf8Bytes(
                ToDto(state), WotCanonicalViewJsonContext.Default.CanonicalGraphDto));
        }

        private static WotCanonicalViewState ParseCanonicalState(ByteString payload)
        {
            if (payload.IsNull || payload.Length == 0 || payload.Length > kMaxPayloadBytes)
            {
                throw new FormatException("A bounded serialized canonical graph is required; absence is not an empty image.");
            }
            try
            {
                using JsonDocument json = JsonDocument.Parse(
                    payload.Memory, new JsonDocumentOptions { MaxDepth = 128 });
                JsonElement root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("schemaVersion", out JsonElement revision) ||
                    revision.ValueKind != JsonValueKind.Number ||
                    !revision.TryGetInt32(out int schema))
                {
                    throw new FormatException("The canonical graph has no schema revision.");
                }
                if (schema != 1)
                {
                    throw new NotSupportedException("The canonical graph schema revision is unsupported.");
                }
                ValidateJsonShape(root);
                CanonicalGraphDto dto = JsonSerializer.Deserialize(
                    payload.Span, WotCanonicalViewJsonContext.Default.CanonicalGraphDto)
                    ?? throw new FormatException("The canonical graph is missing.");
                ValidateDto(dto);
                var views = new List<WotCanonicalViewPublication>(dto.Views.Length);
                foreach (CanonicalViewDto view in dto.Views)
                {
                    views.Add(new WotCanonicalViewPublication(
                        view.ResourceXid, ExpandedNodeId.Parse(view.ResourceNodeId),
                        ExpandedNodeId.Parse(view.ViewNodeId), view.Requested, view.Active, view.ViewVersion,
                        ByteString.From(view.MembershipDigest),
                        view.Membership.Select(ExpandedNodeId.Parse).ToArrayOf(), view.Omissions.ToArrayOf(),
                        view.MaterializedNodeCount));
                }
                var captured = new WotCanonicalViewState(
                    dto.LogicalServerUri, dto.AllocationNamespaceUri, views.ToArrayOf(), [], [],
                    dto.Definitions.ToArrayOf(), dto.Sources.ToArrayOf());
                WotCanonicalViewState rebuilt = WotProjectionViewBuilder.RestoreCanonicalGraph(captured);
                byte[] expected = JsonSerializer.SerializeToUtf8Bytes(
                    Normalize(dto), WotCanonicalViewJsonContext.Default.CanonicalGraphDto);
                if (!rebuilt.ToByteString().Span.SequenceEqual(expected))
                {
                    throw new FormatException(
                        "Canonical nodes, references, roles, ownership, membership, tokens or fingerprints disagree.");
                }
                return rebuilt;
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                throw new FormatException("The canonical graph is invalid.", exception);
            }
        }

        private static CanonicalGraphDto ToDto(WotCanonicalViewState state)
        {
            var views = new List<CanonicalViewDto>(state.Views.Count);
            foreach (WotCanonicalViewPublication view in state.Views)
            {
                var members = new List<string>(view.Membership.Count);
                foreach (ExpandedNodeId nodeId in view.Membership)
                {
                    members.Add(nodeId.ToString());
                }
                var omissions = new List<string>(view.Omissions.Count);
                foreach (string omission in view.Omissions)
                {
                    omissions.Add(omission);
                }
                views.Add(new CanonicalViewDto(
                    view.ResourceXid, view.ResourceNodeId.ToString(), view.ViewNodeId.ToString(),
                    view.Requested, view.Active, view.ViewVersion, view.MembershipDigest.Span.ToArray(),
                    members.ToArray(), omissions.ToArray(), view.MaterializedNodeCount));
            }
            var nodes = new List<CanonicalNodeDto>(state.Nodes.Count);
            foreach (WotCanonicalViewNode node in state.Nodes)
            {
                nodes.Add(new CanonicalNodeDto(
                    node.NodeId.ToString(), node.ResourceXid, node.Role, node.NodeClass,
                    node.BrowseName.NamespaceUri, node.BrowseName.Name,
                    node.TypeDefinition.IsNull ? null : node.TypeDefinition.ToString(),
                    node.DataType.IsNull ? null : node.DataType.ToString(),
                    node.NodeIdValue.IsNull ? null : node.NodeIdValue.ToString(),
                    node.VersionValue, node.ValueRank, node.AccessLevel));
            }
            var references = new List<CanonicalReferenceDto>(state.References.Count);
            foreach (WotCanonicalViewReference reference in state.References)
            {
                references.Add(new CanonicalReferenceDto(
                    reference.SourceId.ToString(), reference.ReferenceTypeId.ToString(),
                    reference.TargetId.ToString(), reference.IsInverse));
            }
            return Normalize(new CanonicalGraphDto(
                1, state.LogicalServerUri, state.AllocationNamespaceUri,
                state.Definitions.ToArray() ?? [], state.SourceFacts.ToArray() ?? [],
                views.ToArray(), nodes.ToArray(), references.ToArray()));
        }

        private static CanonicalGraphDto Normalize(CanonicalGraphDto dto)
        {
            return dto with
            {
                Definitions = dto.Definitions.Select(value => value with
                {
                    Members = value.Members.OrderBy(member => member, StringComparer.Ordinal).ToArray(),
                    Links = value.Links.OrderBy(link => link.Key, StringComparer.Ordinal).ToArray(),
                    Omissions = value.Omissions.OrderBy(omission => omission, StringComparer.Ordinal).ToArray()
                }).OrderBy(value => value.ResourceXid, StringComparer.Ordinal).ToArray(),
                Sources = dto.Sources.OrderBy(value => value.NodeId, StringComparer.Ordinal).ToArray(),
                Views = dto.Views.Select(value => value with
                {
                    Membership = value.Membership.OrderBy(member => member, StringComparer.Ordinal).ToArray(),
                    Omissions = value.Omissions.OrderBy(omission => omission, StringComparer.Ordinal).ToArray()
                }).OrderBy(value => value.ResourceXid, StringComparer.Ordinal).ToArray(),
                Nodes = dto.Nodes.OrderBy(value => value.NodeId, StringComparer.Ordinal).ToArray(),
                References = dto.References.OrderBy(value => value.SourceId, StringComparer.Ordinal)
                    .ThenBy(value => value.ReferenceTypeId, StringComparer.Ordinal)
                    .ThenBy(value => value.IsInverse)
                    .ThenBy(value => value.TargetId, StringComparer.Ordinal).ToArray()
            };
        }

        private static void ValidateDto(CanonicalGraphDto dto)
        {
            WotCanonicalViewGraphContext.RequireUri(dto.LogicalServerUri, nameof(dto.LogicalServerUri));
            WotCanonicalViewGraphContext.RequireUri(dto.AllocationNamespaceUri, nameof(dto.AllocationNamespaceUri));
            if (dto.AllocationNamespaceUri == global::Opc.Ua.Namespaces.OpcUa ||
                dto.Definitions.Length > 10_000 || dto.Sources.Length > 1_000_000 ||
                dto.Nodes.Length > 1_000_000 || dto.References.Length > 4_000_000)
            {
                throw new FormatException("The canonical graph violates allocation or size bounds.");
            }
            var definitions = new Dictionary<string, CanonicalViewDefinition>(StringComparer.Ordinal);
            var knownMembership = new HashSet<string>(StringComparer.Ordinal);
            foreach (CanonicalViewSource source in dto.Sources)
            {
                RequireCanonicalIdentity(source.NodeId);
                if (source.NodeClass is not (NodeClass.Object or NodeClass.Variable or NodeClass.Method or
                    NodeClass.ObjectType or NodeClass.VariableType or NodeClass.DataType or
                    NodeClass.ReferenceType or NodeClass.View) || !knownMembership.Add(source.NodeId))
                {
                    throw new FormatException("The canonical source catalogue has invalid or duplicate facts.");
                }
            }
            foreach (CanonicalViewDefinition definition in dto.Definitions)
            {
                RequireCanonicalIdentity(definition.ResourceNodeId);
                RequireCanonicalIdentity(definition.ViewNodeId);
                WotCanonicalViewGraphContext.RequireUri(definition.Scenario, nameof(definition.Scenario));
                if (string.IsNullOrEmpty(definition.ResourceXid) ||
                    !definitions.TryAdd(definition.ResourceXid, definition) ||
                    (definition.DocumentKind != (int)WotDocumentKind.ThingDescription &&
                    definition.DocumentKind != (int)WotDocumentKind.ThingModel))
                {
                    throw new FormatException("A canonical definition has an invalid identity or kind.");
                }
                RequireCanonicalSet(definition.Members);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (WotCanonicalViewLink link in definition.Links)
                {
                    if (string.IsNullOrEmpty(link.ResourceXid) || string.IsNullOrEmpty(link.Key) || !keys.Add(link.Key))
                    {
                        throw new FormatException("A canonical organizing link is invalid or duplicated.");
                    }
                }
            }
            foreach (CanonicalViewDefinition definition in definitions.Values)
            {
                foreach (WotCanonicalViewLink link in definition.Links)
                {
                    if (!definitions.TryGetValue(link.ResourceXid, out CanonicalViewDefinition? child))
                    {
                        throw new FormatException("A canonical organizing target is missing.");
                    }
                    knownMembership.Add(WotProjectionViewBuilder.CanonicalRoleId(
                        dto.AllocationNamespaceUri, "group", child.ViewNodeId, definition.ViewNodeId, link.Key));
                    knownMembership.Add(WotProjectionViewBuilder.CanonicalRoleId(
                        dto.AllocationNamespaceUri, "projection-root", child.ViewNodeId, definition.ViewNodeId, link.Key));
                }
            }
            var viewIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CanonicalViewDto view in dto.Views)
            {
                RequireCanonicalSet(view.Membership);
                if (!viewIds.Add(view.ResourceXid) ||
                    !definitions.TryGetValue(view.ResourceXid, out CanonicalViewDefinition? definition) ||
                    view.ResourceNodeId != definition.ResourceNodeId || view.ViewNodeId != definition.ViewNodeId ||
                    view.ViewVersion == 0 || view.MembershipDigest.Length != 32 ||
                    view.Membership.Any(member => !knownMembership.Contains(member)) ||
                    !WotPortableIdentity.ProjectionMembershipDigest(view.Membership.ToArrayOf()).Span
                        .SequenceEqual(view.MembershipDigest))
                {
                    throw new FormatException("Canonical View history, membership or fingerprint is inconsistent.");
                }
            }
            if (viewIds.Count != definitions.Count ||
                dto.Nodes.Select(node => node.NodeId).Distinct(StringComparer.Ordinal).Count() != dto.Nodes.Length ||
                dto.References.Distinct().Count() != dto.References.Length)
            {
                throw new FormatException("Canonical publication maps are incomplete or duplicated.");
            }
        }

        private static void RequireCanonicalSet(string[] values)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                RequireCanonicalIdentity(value);
                if (!distinct.Add(value))
                {
                    throw new FormatException("Canonical membership is not a set.");
                }
            }
        }

        private static void RequireCanonicalIdentity(string value)
        {
            if (!string.Equals(value, WotPortableIdentity.CanonicalNodeId(value), StringComparison.Ordinal))
            {
                throw new FormatException("A persisted identity is not canonical and portable.");
            }
        }

        private static void ValidateJsonShape(JsonElement root)
        {
            RequireMembers(root, "schemaVersion", "logicalServerUri", "allocationNamespaceUri",
                "definitions", "sources", "views", "nodes", "references");
            foreach (JsonElement definition in ArrayElements(root, "definitions"))
            {
                RequireMembers(definition, "resourceXid", "resourceNodeId", "viewNodeId", "documentKind", "scenario",
                    "requested", "members", "links", "omissions");
                RequireStringArray(definition, "members");
                RequireStringArray(definition, "omissions");
                foreach (JsonElement link in ArrayElements(definition, "links"))
                {
                    RequireMembers(link, "resourceXid", "key");
                }
            }
            foreach (JsonElement source in ArrayElements(root, "sources"))
            {
                RequireMembers(source, "nodeId", "nodeClass", "available");
            }
            foreach (JsonElement view in ArrayElements(root, "views"))
            {
                RequireMembers(view, "resourceXid", "resourceNodeId", "viewNodeId", "requested", "active",
                    "viewVersion", "membershipDigest", "membership", "omissions", "materializedNodeCount");
                RequireStringArray(view, "membership");
                RequireStringArray(view, "omissions");
                if (view.GetProperty("membershipDigest").ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("A canonical membership fingerprint is required.");
                }
            }
            foreach (JsonElement node in ArrayElements(root, "nodes"))
            {
                RequireMembers(node, "nodeId", "resourceXid", "role", "nodeClass", "browseNamespaceUri",
                    "browseName", "typeDefinition", "dataType", "nodeIdValue", "versionValue", "valueRank", "accessLevel");
            }
            foreach (JsonElement reference in ArrayElements(root, "references"))
            {
                RequireMembers(reference, "sourceId", "referenceTypeId", "targetId", "isInverse");
            }
        }

        private static JsonElement.ArrayEnumerator ArrayElements(JsonElement element, string name)
        {
            JsonElement array = element.GetProperty(name);
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("A complete canonical publication map is required.");
            }
            return array.EnumerateArray();
        }

        private static void RequireStringArray(JsonElement element, string name)
        {
            foreach (JsonElement value in ArrayElements(element, name))
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("Canonical publication sequences must contain strings.");
                }
            }
        }

        private static void RequireMembers(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("A canonical publication record must be an object.");
            }
            var required = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!required.Remove(property.Name))
                {
                    throw new FormatException("A canonical publication record has an unknown or duplicate member.");
                }
            }
            if (required.Count != 0)
            {
                throw new FormatException("A canonical publication record is incomplete.");
            }
        }

        private const int kMaxPayloadBytes = 16 * 1024 * 1024;
    }

    internal sealed record CanonicalGraphDto(
        int SchemaVersion,
        string LogicalServerUri,
        string AllocationNamespaceUri,
        CanonicalViewDefinition[] Definitions,
        CanonicalViewSource[] Sources,
        CanonicalViewDto[] Views,
        CanonicalNodeDto[] Nodes,
        CanonicalReferenceDto[] References);

    internal sealed record CanonicalViewDto(
        string ResourceXid, string ResourceNodeId, string ViewNodeId, bool Requested, bool Active,
        uint ViewVersion, byte[] MembershipDigest, string[] Membership, string[] Omissions, int MaterializedNodeCount);

    internal sealed record CanonicalNodeDto(
        string NodeId, string ResourceXid, WotCanonicalViewNodeRole Role, NodeClass NodeClass,
        string? BrowseNamespaceUri, string BrowseName, string? TypeDefinition, string? DataType,
        string? NodeIdValue, uint VersionValue, int ValueRank, byte AccessLevel);

    internal sealed record CanonicalReferenceDto(
        string SourceId, string ReferenceTypeId, string TargetId, bool IsInverse);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 128)]
    [JsonSerializable(typeof(CanonicalGraphDto))]
    internal sealed partial class WotCanonicalViewJsonContext : JsonSerializerContext
    {
    }
}
