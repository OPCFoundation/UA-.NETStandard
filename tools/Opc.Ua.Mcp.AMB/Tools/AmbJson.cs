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
using System.Globalization;
using System.Text.Json.Nodes;
using Opc.Ua.AMB.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit, reflection-free projections of the AMB client's records.
    /// </summary>
    internal static class AmbJson
    {
        /// <summary>
        /// Rejects undefined finite selectors before a server operation.
        /// </summary>
        /// <typeparam name="T">The finite selector type.</typeparam>
        public static void RequireDefined<T>(T value, string parameterName)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Unknown AMB selector.");
            }
        }

        /// <summary>
        /// Parses a required local NodeId without interpreting remote ExpandedNodeIds.
        /// </summary>
        public static NodeId ParseNodeId(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            NodeId node = OpcUaJsonHelper.ParseNodeId(value);
            return node.IsNull ? throw new ArgumentException("A non-null NodeId is required.", nameof(value)) : node;
        }

        /// <summary>
        /// Converts the validated MCP alias selector to the typed client selector.
        /// </summary>
        public static AssetAliasCategory Category(AmbAssetCategory category)
        {
            RequireDefined(category, nameof(category));
            return category switch
            {
                AmbAssetCategory.Assets => AssetAliasCategory.Assets,
                AmbAssetCategory.ByProductInstanceUri => AssetAliasCategory.ByProductInstanceUri,
                AmbAssetCategory.ByAssetId => AssetAliasCategory.ByAssetId,
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
        }

        /// <summary>
        /// Projects one local asset identity.
        /// </summary>
        public static JsonObject Node(NodeId node)
        {
            return new JsonObject { ["nodeId"] = node.ToString() };
        }

        /// <summary>
        /// Projects an alias without following targets on another server.
        /// </summary>
        public static JsonObject Alias(AssetAlias alias)
        {
            return new JsonObject
            {
                ["category"] = alias.Category.ToString(),
                ["name"] = alias.Name,
                ["referencedNodes"] = Nodes(alias.ReferencedNodes)
            };
        }

        /// <summary>
        /// Projects identification without losing localized names or unset values.
        /// </summary>
        public static JsonObject Identification(AssetIdentificationRecord value)
        {
            return new JsonObject
            {
                ["assetNodeId"] = value.Asset.ToString(),
                ["productInstanceUri"] = value.ProductInstanceUri,
                ["assetId"] = value.AssetId,
                ["manufacturer"] = Text(value.Manufacturer),
                ["manufacturerUri"] = value.ManufacturerUri,
                ["model"] = Text(value.Model),
                ["productCode"] = value.ProductCode,
                ["serialNumber"] = value.SerialNumber,
                ["hardwareRevision"] = value.HardwareRevision,
                ["softwareRevision"] = value.SoftwareRevision,
                ["revisionCounter"] = value.RevisionCounter
            };
        }

        /// <summary>
        /// Projects the condition identity, acknowledgement token and full alarm state.
        /// </summary>
        public static JsonObject Alarm(AssetAlarmRecord value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["conditionId"] = value.ConditionId.ToString(),
                ["eventId"] = value.EventId.IsNull ? null : value.EventId.ToBase64(),
                ["eventType"] = value.EventType.ToString(),
                ["sourceNode"] = value.SourceNode.ToString(),
                ["sourceName"] = value.SourceName,
                ["conditionName"] = value.ConditionName,
                ["time"] = Timestamp(value.Time),
                ["message"] = Text(value.Message),
                ["severity"] = value.Severity,
                ["faultCategory"] = value.FaultCategory?.ToString(),
                ["conditionClassId"] = value.ConditionClassId.ToString(),
                ["conditionClassName"] = Text(value.ConditionClassName),
                ["conditionClass"] = value.ConditionClass?.Name,
                ["isActive"] = value.IsActive,
                ["isAcknowledged"] = value.IsAcknowledged,
                ["retain"] = value.Retain,
                ["comment"] = Text(value.Comment),
                ["potentialRootCauses"] = EncodeArray(value.PotentialRootCauses, context)
            };
        }

        /// <summary>
        /// Projects a maintenance activity, including its state and planned work.
        /// </summary>
        public static JsonObject Maintenance(MaintenanceActivityRecord value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["conditionId"] = value.ConditionId.ToString(),
                ["eventId"] = value.EventId.IsNull ? null : value.EventId.ToBase64(),
                ["eventType"] = value.EventType.ToString(),
                ["sourceNode"] = value.SourceNode.ToString(),
                ["conditionName"] = value.ConditionName,
                ["time"] = Timestamp(value.Time),
                ["message"] = Text(value.Message),
                ["conditionClassId"] = value.ConditionClassId.ToString(),
                ["conditionClass"] = value.ConditionClass?.Name,
                ["isActive"] = value.IsActive,
                ["isAcknowledged"] = value.IsAcknowledged,
                ["retain"] = value.Retain,
                ["comment"] = Text(value.Comment),
                ["state"] = value.State?.ToString(),
                ["plannedDate"] = Timestamp(value.PlannedDate),
                ["estimatedDowntimeSeconds"] = value.EstimatedDowntime?.TotalSeconds,
                ["maintenanceSupplier"] = value.MaintenanceSupplier is null
                    ? null
                    : McpCompanionJson.Encode(value.MaintenanceSupplier, context),
                ["qualificationOfPersonnel"] = value.QualificationOfPersonnel is null
                    ? null
                    : McpCompanionJson.Encode(value.QualificationOfPersonnel, context),
                ["partsOfAssetReplaced"] = EncodeArray(value.PartsOfAssetReplaced, context),
                ["partsOfAssetServiced"] = EncodeArray(value.PartsOfAssetServiced, context),
                ["maintenanceMethod"] = value.MaintenanceMethod?.ToString(),
                ["configurationChanged"] = value.ConfigurationChanged
            };
        }

        /// <summary>
        /// Projects an editable or manufacturer documentation link.
        /// </summary>
        public static JsonObject Link(DocumentationLinkRecord value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = Name(value.BrowseName),
                ["displayName"] = Text(value.DisplayName),
                ["uri"] = value.Uri,
                ["isWritable"] = value.IsWritable,
                ["isUserLink"] = value.IsUserLink
            };
        }

        /// <summary>
        /// Projects location and classification properties.
        /// </summary>
        public static JsonObject Context(AssetContextRecord value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["hierarchicalLocation"] = value.HierarchicalLocation,
                ["operationalLocation"] = value.OperationalLocation,
                ["digitalLocation"] = value.DigitalLocation,
                ["localTime"] = value.LocalTime is null ? null : McpCompanionJson.Encode(value.LocalTime, context),
                ["classifications"] = Nodes(value.Classifications)
            };
        }

        /// <summary>
        /// Projects one location in a location tree.
        /// </summary>
        public static JsonObject LocationNode(AssetLocationNode value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["path"] = value.Path,
                ["assets"] = Nodes(value.Assets)
            };
        }

        /// <summary>
        /// Projects the membership of an asset in a location object.
        /// </summary>
        public static JsonObject Location(AssetLocationRecord value)
        {
            return new JsonObject { ["kind"] = value.Kind.ToString(), ["nodeId"] = value.Location.ToString() };
        }

        /// <summary>
        /// Projects a requirement or capability with the UA Variant type intact.
        /// </summary>
        public static JsonObject Entry(AssetEntryRecord value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["browseName"] = Name(value.BrowseName),
                ["value"] = McpCompanionJson.Variant(value.Value, context),
                ["dictionaryEntries"] = Nodes(value.DictionaryEntries)
            };
        }

        /// <summary>
        /// Projects a relation, preserving direction and remote target identity.
        /// </summary>
        public static JsonObject Relation(AssetRelation value)
        {
            return new JsonObject
            {
                ["referenceTypeId"] = value.ReferenceTypeId.ToString(),
                ["isInverse"] = value.IsInverse,
                ["target"] = value.Target.ToString()
            };
        }

        /// <summary>
        /// Projects a snapshot with separately paginated collection facets.
        /// </summary>
        public static JsonObject Snapshot(
            AssetSnapshot value,
            IServiceMessageContext context,
            int offset,
            int maxResults)
        {
            return new JsonObject
            {
                ["identification"] = Identification(value.Identification),
                ["deviceHealth"] = value.DeviceHealth?.ToString(),
                ["deviceHealthValue"] = (int?)value.DeviceHealth,
                ["healthAlarms"] = McpCompanionTools.Page(
                    value.HealthAlarms, alarm => Alarm(alarm, context), offset, maxResults),
                ["maintenanceActivities"] = McpCompanionTools.Page(
                    value.MaintenanceActivities, activity => Maintenance(activity, context), offset, maxResults),
                ["documentationLinks"] = McpCompanionTools.Page(value.DocumentationLinks, Link, offset, maxResults),
                ["context"] = Context(value.Context, context),
                ["locations"] = McpCompanionTools.Page(value.Locations, Location, offset, maxResults)
            };
        }

        /// <summary>
        /// Projects a localized text as data, rather than a display-only string.
        /// </summary>
        private static JsonObject? Text(LocalizedText value)
        {
            return value.IsNull ? null : new JsonObject { ["text"] = value.Text, ["locale"] = value.Locale };
        }

        /// <summary>
        /// Projects a qualified browse name.
        /// </summary>
        private static JsonObject Name(QualifiedName value)
        {
            return new JsonObject { ["name"] = value.Name, ["namespaceIndex"] = value.NamespaceIndex };
        }

        /// <summary>
        /// Projects expanded identities without resolving another server.
        /// </summary>
        private static JsonArray Nodes(ArrayOf<ExpandedNodeId> values)
        {
            var result = new JsonArray();
            foreach (ExpandedNodeId value in values)
            {
                result.Add(value.ToString());
            }
            return result;
        }

        /// <summary>
        /// Encodes model-defined structures through their generated encoder.
        /// </summary>
        /// <typeparam name="T">The encodeable model type.</typeparam>
        private static JsonArray EncodeArray<T>(ArrayOf<T> values, IServiceMessageContext context)
            where T : IEncodeable
        {
            var result = new JsonArray();
            foreach (T value in values)
            {
                result.Add(McpCompanionJson.Encode(value, context));
            }
            return result;
        }

        /// <summary>
        /// Formats optional UA timestamps without substituting a current time.
        /// </summary>
        private static string? Timestamp(DateTimeUtc value)
        {
            return value.IsNull ? null : value.ToString("O", CultureInfo.InvariantCulture);
        }
    }
}
