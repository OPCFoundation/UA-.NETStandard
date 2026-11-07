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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryBridgeNodeManager
    {
        private async ValueTask ReconcileMappedPropertiesAsync(XRegistryNativeSnapshot snapshot, CancellationToken ct)
        {
            if (m_options.AttributeMappings.Count == 0)
            {
                return;
            }
            var byPath = m_entityNodes.Values.ToDictionary(node => m_entities[node.NodeId], StringComparer.Ordinal);
            foreach ((string path, JsonElement metadata, JsonElement definition) in
                XRegistryNativeAttributePlan.Entities(snapshot))
            {
                foreach (XRegistryNativeAttributeMapping mapping in m_options.AttributeMappings.ToList())
                {
                    if (!mapping.Matches(path))
                    {
                        continue;
                    }
                    string ownerPath = mapping.Scope == XRegistryNativeAttributeScope.Meta ? path[..^5] : path;
                    if (!byPath.TryGetValue(ownerPath, out BaseObjectState? owner))
                    {
                        throw new InvalidDataException(
                            "The native attribute owner was absent from the complete projection.");
                    }
                    JsonElement rule = XRegistryNativeAttributePlan.Rule(mapping, definition, metadata);
                    bool active = XRegistryNativeAttributePlan.TryActiveRule(mapping, definition, metadata, out _);
                    PropertyState property = await GetMappedPropertyAsync(owner, mapping, ct).ConfigureAwait(false);
                    property.DataType = mapping.StructureType is not null
                        ? ExpandedNodeId.ToNodeId(mapping.StructureTypeId, Server.NamespaceUris)
                        : new NodeId((uint)XRegistryNativeAttributeCodec.DataType(
                            rule, mapping.Encoding, mapping.NativeType));
                    if (property.DataType.IsNull)
                    {
                        throw new InvalidDataException(
                            "The registered structure namespace is not present in this server.");
                    }
                    property.ValueRank = mapping.Encoding == XRegistryNativeAttributeEncoding.Typed &&
                        rule.GetProperty("type").GetString() == "array" ? ValueRanks.OneDimension : ValueRanks.Scalar;
                    JsonElement value = XRegistryNativeAttributePlan.Value(metadata, mapping.AttributePath);
                    property.Value = value.ValueKind == JsonValueKind.Undefined
                        ? Variant.Null : XRegistryNativeAttributeCodec.Encode(value, rule, mapping);
                    property.StatusCode = value.ValueKind == JsonValueKind.Undefined
                        ? StatusCodes.BadNoData : StatusCodes.Good;
                    bool writable = active &&
                        mapping.Writable &&
                        !(rule.TryGetProperty("readonly", out JsonElement readOnly) &&
                            readOnly.ValueKind == JsonValueKind.True) &&
                        !(rule.TryGetProperty("immutable", out JsonElement immutable) &&
                            immutable.ValueKind == JsonValueKind.True);
                    property.AccessLevel = writable ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;
                    property.UserAccessLevel = property.AccessLevel;
                    property.OnSimpleReadValueAsync = async (context, _, token) =>
                    {
                        JsonElement current = await ReadMetadataAsync(context, path, token).ConfigureAwait(false);
                        JsonElement actual = XRegistryNativeAttributePlan.Value(current, mapping.AttributePath);
                        if (!XRegistryNativeAttributePlan.TryActiveRule(
                            mapping, definition, current, out JsonElement currentRule))
                        {
                            return new AttributeSimpleReadResult(actual.ValueKind == JsonValueKind.Undefined
                                ? StatusCodes.BadNoData : StatusCodes.BadTypeMismatch, Variant.Null);
                        }
                        return actual.ValueKind == JsonValueKind.Undefined
                            ? new AttributeSimpleReadResult(StatusCodes.BadNoData, Variant.Null)
                            : new AttributeSimpleReadResult(ServiceResult.Good,
                                XRegistryNativeAttributeCodec.Encode(actual, currentRule, mapping));
                    };
                    property.OnSimpleWriteValueAsync = async (context, node, native, token) =>
                    {
                        if (!writable)
                        {
                            return new AttributeWriteResult(StatusCodes.BadNotWritable);
                        }
                        _ = await AuthorizeAsync(context, true, token).ConfigureAwait(false);
                        JsonElement current = await ReadMetadataAsync(context, path, token).ConfigureAwait(false);
                        if (!XRegistryNativeAttributePlan.TryActiveRule(
                            mapping, definition, current, out JsonElement currentRule))
                        {
                            return new AttributeWriteResult(StatusCodes.BadNotWritable);
                        }
                        JsonElement logical;
                        try
                        {
                            logical = XRegistryNativeAttributeCodec.Decode(native, currentRule, mapping);
                        }
                        catch (Exception exception) when (
                            exception is InvalidDataException or JsonException or FormatException)
                        {
                            return new AttributeWriteResult(
                                ServiceResult.Create(StatusCodes.BadTypeMismatch, exception.Message));
                        }
                        var body = new JsonObject { ["epoch"] = XRegistryNativeJson.EpochGuard(current) };
                        XRegistryNativeAttributePlan.SetValue(body, mapping.AttributePath, logical);
                        XRegistryResponse response = await ExecuteAsync(
                            context,
                            new XRegistryRequest(XRegistryAction.Merge, path)
                            {
                                View = XRegistryView.Metadata,
                                Metadata = XRegistryNativeJson.Element(body)
                            },
                            token).ConfigureAwait(false);
                        return new AttributeWriteResult(XRegistryNativeJson.Status(response));
                    };
                    await property.ClearChangeMasksAsync(SystemContext, false, ct).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<PropertyState> GetMappedPropertyAsync(
            BaseObjectState owner, XRegistryNativeAttributeMapping mapping, CancellationToken ct)
        {
            BaseInstanceState parent = owner;
            string key = m_options.RootIdentifier + "/mapped/" + Uri.EscapeDataString(m_entities[owner.NodeId]);
            for (int index = 0; index < mapping.BrowsePath.Count; index++)
            {
                XRegistryNativeBrowseName part = mapping.BrowsePath[index];
                var name = new QualifiedName(part.Name, Server.NamespaceUris.GetIndexOrAppend(part.NamespaceUri));
                key += "/" + Uri.EscapeDataString(part.NamespaceUri) + ":" + Uri.EscapeDataString(part.Name);
                BaseInstanceState? child = parent.FindChild(SystemContext, name);
                bool last = index == mapping.BrowsePath.Count - 1;
                if (child is null)
                {
                    if (last && m_mappedNodes.Count >= m_options.MaxMappedProperties)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The mapped-property quota is exhausted.");
                    }
                    child = last ? new PropertyState(parent) : new BaseObjectState(parent);
                    child.NodeId = new NodeId(key, InstanceNamespaceIndex);
                    child.BrowseName = name;
                    child.DisplayName = new LocalizedText(part.Name);
                    child.SymbolicName = part.Name;
                    child.ReferenceTypeId = last ? ReferenceTypeIds.HasProperty : ReferenceTypeIds.HasComponent;
                    child.TypeDefinitionId = last ? VariableTypeIds.PropertyType : Ua.ObjectTypeIds.BaseObjectType;
                    parent.AddChild(child);
                    await AddPredefinedNodeAsync(SystemContext, child, ct).ConfigureAwait(false);
                    if (last)
                    {
                        m_mappedNodes.Add(child.NodeId);
                    }
                }
                if (last)
                {
                    if (child is not PropertyState variable || !m_mappedNodes.Contains(child.NodeId))
                    {
                        throw new InvalidDataException(
                            "The native mapping collides with an existing non-mapped property.");
                    }
                    return variable;
                }
                if (child.NodeClass != NodeClass.Object)
                {
                    throw new InvalidDataException("A native mapping container is not an Object.");
                }
                parent = child;
            }
            throw new InvalidDataException("The native attribute mapping omitted its property name.");
        }

        private async ValueTask<ServiceResult> UpdateMappedLabelAsync(
            ISystemContext context, string path, XRegistryNativeAttributeMapping mapping,
            string? value, uint epoch, CancellationToken ct)
        {
            _ = await AuthorizeAsync(context, true, ct).ConfigureAwait(false);
            JsonElement metadata = await ReadMetadataAsync(context, path, ct).ConfigureAwait(false);
            JsonElement definition = XRegistryNativeAttributePlan.Entities(m_strategy!.Snapshot)
                .Single(entity => entity.Path == path).Definition;
            if (!XRegistryNativeAttributePlan.TryActiveRule(mapping, definition, metadata, out JsonElement rule) ||
                !mapping.Writable ||
                (rule.TryGetProperty("readonly", out JsonElement readOnly) &&
                    readOnly.ValueKind == JsonValueKind.True) ||
                (rule.TryGetProperty("immutable", out JsonElement immutable) &&
                    immutable.ValueKind == JsonValueKind.True))
            {
                return StatusCodes.BadNotWritable;
            }
            JsonElement logical;
            try
            {
                if (value is null)
                {
                    using var absent = JsonDocument.Parse("null");
                    logical = absent.RootElement.Clone();
                }
                else
                {
                    var native = Variant.From(value);
                    logical = XRegistryNativeAttributeCodec.Decode(native, rule, mapping.Encoding, mapping.NativeType);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException)
            {
                return ServiceResult.Create(StatusCodes.BadTypeMismatch, exception.Message);
            }
            var body = new JsonObject();
            if (epoch != 0)
            {
                body["epoch"] = epoch;
            }
            XRegistryNativeAttributePlan.SetValue(body, mapping.AttributePath, logical);
            XRegistryResponse response = await ExecuteAsync(context, new XRegistryRequest(XRegistryAction.Merge, path)
            {
                View = XRegistryView.Metadata,
                Metadata = XRegistryNativeJson.Element(body)
            }, ct).ConfigureAwait(false);
            return XRegistryNativeJson.Status(response);
        }

        private readonly HashSet<NodeId> m_mappedNodes = [];
    }
}
