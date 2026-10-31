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

using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    internal sealed partial class XRegistryBaseOpcUaEndpoint
    {
        private async ValueTask ReadMappedAttributesAsync(
            NodeId owner, ArrayOf<ReferenceDescription> children, EntityKind kind,
            JsonObject metadata, JsonElement definition, string path, CancellationToken ct)
        {
            await ReadLabelsAsync(children, kind, metadata, path, ct).ConfigureAwait(false);
            var pending = options.AttributeMappings.ToList().Where(mapping => mapping.Matches(path)).ToList();
            while (pending.Count != 0)
            {
                JsonElement current = XRegistryNativeJson.Element(metadata);
                XRegistryNativeAttributeMapping[] ready = [.. pending.Where(mapping =>
                    XRegistryNativeAttributePlan.TryActiveRule(mapping, definition, current, out _))
                    .OrderByDescending(mapping => XRegistryNativeAttributePlan.Rule(mapping, definition, current)
                        .TryGetProperty("ifvalues", out _))];
                if (ready.Length == 0)
                {
                    ready = [.. pending];
                }
                foreach (XRegistryNativeAttributeMapping mapping in ready)
                {
                    pending.Remove(mapping);
                    bool active = XRegistryNativeAttributePlan.TryActiveRule(
                        mapping, definition, XRegistryNativeJson.Element(metadata), out _);
                    JsonElement rule = XRegistryNativeAttributePlan.Rule(
                        mapping, definition, XRegistryNativeJson.Element(metadata));
                    NodeId node = await ResolveAttributeNodeAsync(owner, mapping, ct).ConfigureAwait(false);
                    if (node.IsNull)
                    {
                        if (active && XRegistryNativeAttributePlan.Required(rule))
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                                "A required mapped native property is absent.");
                        }
                        continue;
                    }
                    ReadResponse response = await session.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        [
                            new ReadValueId { NodeId = node, AttributeId = Attributes.Value },
                            new ReadValueId { NodeId = node, AttributeId = Attributes.DataType },
                            new ReadValueId { NodeId = node, AttributeId = Attributes.ValueRank }
                        ],
                        ct).ConfigureAwait(false);
                    if (response.Results.Count != 3)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError);
                    }
                    DataValue data = response.Results[0];
                    if (data.StatusCode == StatusCodes.BadNoData &&
                        (!active || !XRegistryNativeAttributePlan.Required(rule)))
                    {
                        RemoveMappedLabel(metadata, mapping);
                        continue;
                    }
                    foreach (DataValue result in response.Results)
                    {
                        if (StatusCode.IsBad(result.StatusCode))
                        {
                            throw new ServiceResultException(result.StatusCode);
                        }
                    }
                    if (!active)
                    {
                        throw new InvalidDataException(
                            "An inactive conditional property still exposes an unmappable native value.");
                    }
                    if (!response.Results[1].WrappedValue.TryGetValue(out NodeId dataType) ||
                        !response.Results[2].WrappedValue.TryGetValue(out int rank) ||
                        !(mapping.StructureType is not null
                            ? dataType == ExpandedNodeId.ToNodeId(mapping.StructureTypeId, session.NamespaceUris)
                            : dataType.NamespaceIndex == 0 &&
                                dataType.TryGetValue(out uint type) &&
                                ((data.WrappedValue.IsNull &&
                                    type is >= 1 and <= 13 &&
                                    (mapping.NativeType == BuiltInType.Null || type == (uint)mapping.NativeType)) ||
                                    type == (uint)data.WrappedValue.TypeInfo.BuiltInType ||
                                    (type == (uint)BuiltInType.Enumeration &&
                                        data.WrappedValue.TypeInfo.BuiltInType == BuiltInType.Int32))) ||
                        !ValueRanks.IsValid(data.WrappedValue.TypeInfo.ValueRank, rank))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "The property value does not agree with its declared native type and rank.");
                    }
                    if (data.WrappedValue.TryGetValue(out string encoded) &&
                        Encoding.UTF8.GetByteCount(encoded) > options.MaxMessageBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                    JsonElement value = XRegistryNativeAttributeCodec.Decode(data.WrappedValue, rule, mapping);
                    RemoveMappedLabel(metadata, mapping);
                    XRegistryNativeAttributePlan.SetValue(metadata, mapping.AttributePath, value);
                }
            }
            foreach (XRegistryNativeAttributeMapping mapping in options.AttributeMappings)
            {
                if (!mapping.Matches(path))
                {
                    continue;
                }
                JsonElement mapped = XRegistryNativeJson.Element(metadata);
                ArrayOf<string> top = [mapping.AttributePath[0]];
                if (XRegistryAttributeModel.TryResolve(
                    XRegistryNativeAttributePlan.Attributes(definition, mapping.Scope),
                    mapped, top, out JsonElement rule) &&
                    metadata.TryGetPropertyValue(mapping.AttributePath[0], out JsonNode? value))
                {
                    using var document = JsonDocument.Parse(value?.ToJsonString() ?? "null");
                    XRegistryAttributeModel.Validate(document.RootElement, rule);
                }
            }
        }

        private async ValueTask<NodeId> ResolveAttributeNodeAsync(
            NodeId owner, XRegistryNativeAttributeMapping mapping, CancellationToken ct)
        {
            NodeId current = owner;
            for (int index = 0; index < mapping.BrowsePath.Count; index++)
            {
                XRegistryNativeBrowseName part = mapping.BrowsePath[index];
                ArrayOf<ReferenceDescription> children = await ChildrenAsync(current, ct).ConfigureAwait(false);
                ReferenceDescription[] matching = [.. children.ToList().Where(child =>
                    child.BrowseName.Name == part.Name &&
                    session.NamespaceUris.GetString(child.BrowseName.NamespaceIndex) == part.NamespaceUri)];
                if (matching.Length == 0)
                {
                    return NodeId.Null;
                }
                if (matching.Length != 1 ||
                    (index == mapping.BrowsePath.Count - 1 && matching[0].NodeClass != NodeClass.Variable))
                {
                    throw new InvalidDataException(
                        "The configured native attribute path is ambiguous or not a Variable.");
                }
                current = Local(matching[0]);
            }
            return current;
        }

        private async ValueTask ReadLabelsAsync(
            ArrayOf<ReferenceDescription> children, EntityKind kind,
            JsonObject metadata, string path, CancellationToken ct)
        {
            ReferenceDescription? labels = Child(children, kind == EntityKind.Meta ? "MetaLabels" : "Labels");
            if (labels is null)
            {
                return;
            }
            var values = new JsonObject();
            ArrayOf<ReferenceDescription> labelNodes = await ChildrenAsync(Local(labels), ct).ConfigureAwait(false);
            foreach (ReferenceDescription label in labelNodes.ToList())
            {
                if (label.NodeClass != NodeClass.Variable ||
                    options.AttributeMappings.ToList().Any(mapping => mapping.Matches(path) &&
                        mapping.BrowsePath.Count == 2 &&
                        MatchesBrowseName(mapping.BrowsePath[0], labels.BrowseName) &&
                        MatchesBrowseName(mapping.BrowsePath[1], label.BrowseName)))
                {
                    continue;
                }
                DataValue data = await session.ReadValueAsync(Local(label), ct).ConfigureAwait(false);
                if (StatusCode.IsBad(data.StatusCode))
                {
                    throw new ServiceResultException(data.StatusCode);
                }
                if (!data.WrappedValue.TryGetValue(out string value))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "A label is not a string; no JSON string coercion is permitted.");
                }
                string name = label.BrowseName.Name
                    ?? throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
                values[name] = value;
            }
            metadata["labels"] = values;
        }

        private bool MatchesBrowseName(XRegistryNativeBrowseName mapping, QualifiedName name)
        {
            return mapping.Name == name.Name &&
                mapping.NamespaceUri == session.NamespaceUris.GetString(name.NamespaceIndex);
        }

        private static void RemoveMappedLabel(JsonObject metadata, XRegistryNativeAttributeMapping mapping)
        {
            if (mapping.AttributePath[0] != "labels" &&
                mapping.BrowsePath.Count == 2 &&
                mapping.BrowsePath[0].NamespaceUri == XRegistryWellKnown.XRegistryNamespaceUri &&
                mapping.BrowsePath[0].Name is "Labels" or "MetaLabels" &&
                metadata["labels"] is JsonObject labels)
            {
                labels.Remove(mapping.BrowsePath[1].Name);
            }
        }
    }
}
