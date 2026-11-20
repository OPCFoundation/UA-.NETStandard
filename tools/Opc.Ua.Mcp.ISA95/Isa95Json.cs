// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Opc.Ua.ISA95.Client;
using Opc.Ua.Mcp.Serialization;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit projections for ISA-95 discovery, numeric model status and typed events.
    /// </summary>
    internal static class Isa95Json
    {
        /// <summary>
        /// Parses a required local endpoint identifier.
        /// </summary>
        public static NodeId Node(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            NodeId node = OpcUaJsonHelper.ParseNodeId(text);
            return node.IsNull ? throw new ArgumentException("A non-null NodeId is required.", nameof(text)) : node;
        }

        /// <summary>
        /// Preserves the full Annex B.2 bitmap; only the Success bit alone represents success.
        /// </summary>
        public static JsonObject ReturnStatus(ulong status)
        {
            return new JsonObject
            {
                ["error"] = status != 1UL,
                ["returnStatus"] = status,
                ["returnStatusText"] = status.ToString(CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Projects one common-model discovery result.
        /// </summary>
        public static JsonObject CommonEntry(Isa95CommonObjectEntry value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["kind"] = value.Kind.ToString(),
                ["browseName"] = value.BrowseName
            };
        }

        /// <summary>
        /// Projects a real endpoint and its version and independent role.
        /// </summary>
        public static JsonObject Endpoint(EndpointEntry value)
        {
            return new JsonObject
            {
                ["version"] = value.Version,
                ["nodeId"] = value.Endpoint.NodeId.ToString(),
                ["typeDefinitionId"] = value.Endpoint.TypeDefinitionId.ToString(),
                ["role"] = value.Endpoint.Facet.ToString(),
                ["browseName"] = value.Endpoint.BrowseName
            };
        }

        /// <summary>
        /// Projects base-event attributes and all generated V2 payload structures.
        /// </summary>
        public static JsonObject StatusEvent(
            V2.ISA95JobOrderStatusEventTypeRecord value,
            IServiceMessageContext context)
        {
            var states = new JsonArray();
            foreach (V2.ISA95StateDataType state in value.JobState ?? [])
            {
                states.Add(McpCompanionJson.Encode(state, context));
            }
            return new JsonObject
            {
                ["eventId"] = value.EventId.IsNull ? null : value.EventId.ToBase64(),
                ["eventType"] = value.EventType.ToString(),
                ["sourceNode"] = value.SourceNode.ToString(),
                ["sourceName"] = value.SourceName,
                ["time"] = value.Time?.ToString("O", CultureInfo.InvariantCulture),
                ["severity"] = value.Severity,
                ["message"] = value.Message.IsNull ? null : new JsonObject
                {
                    ["text"] = value.Message.Text,
                    ["locale"] = value.Message.Locale
                },
                ["jobOrder"] = McpCompanionJson.Encode(value.JobOrder, context),
                ["jobResponse"] = McpCompanionJson.Encode(value.JobResponse, context),
                ["jobState"] = states
            };
        }

        /// <summary>
        /// Carries an endpoint's wire version during combined discovery paging.
        /// </summary>
        /// <param name="Version">The wire version label.</param>
        /// <param name="Endpoint">The real discovered endpoint.</param>
        internal sealed record EndpointEntry(string Version, Isa95JobControlEndpoint Endpoint);
    }
}
