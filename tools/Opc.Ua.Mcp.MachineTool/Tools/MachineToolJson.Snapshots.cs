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

using System.Globalization;
using System.Text.Json.Nodes;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;

namespace Opc.Ua.Mcp.Tools
{
    internal static partial class MachineToolJson
    {
        /// <summary>
        /// Retains raw declaration samples and their field names instead of inferring optional conformance.
        /// </summary>
        internal static JsonObject Capabilities(MachineToolClient client, MachineToolCapabilityReadSnapshot value)
        {
            var samples = new JsonObject();
            for (int index = 0; index < value.Samples.Count; index++)
            {
                samples[MachineToolCapabilityNames.Fields[index]] = Reading(client, value.Samples[index]);
            }
            return new JsonObject
            {
                ["declarationAvailable"] = value.DeclarationAvailable,
                ["declarationNamespace"] = MachineToolCapabilityNames.NamespaceUri,
                ["samples"] = samples
            };
        }

        /// <summary>
        /// Preserves the entire equipment inventory and optional container presence.
        /// </summary>
        internal static JsonObject Equipment(MachineToolClient client, MachineToolEquipmentReadSnapshot value)
        {
            return new JsonObject
            {
                ["machineNodeId"] = value.MachineId.ToString(),
                ["toolsNodeId"] = OptionalNode(value.ToolsNodeId),
                ["nodeVersion"] = Reading(client, value.NodeVersion),
                ["tools"] = Array(value.Tools, tool => Tool(client, tool))
            };
        }

        /// <summary>
        /// Preserves all standard prognosis fields, actual subtypes and original affected-entity references.
        /// </summary>
        internal static JsonObject Prognoses(MachineToolClient client, MachineToolPrognosisReadSnapshot value)
        {
            return new JsonObject
            {
                ["machineNodeId"] = value.MachineId.ToString(),
                ["listNodeId"] = OptionalNode(value.ListNodeId),
                ["nodeVersion"] = Reading(client, value.NodeVersion),
                ["entries"] = Array(value.Entries, entry => new JsonObject
                {
                    ["nodeId"] = entry.NodeId.ToString(),
                    ["browseName"] = entry.BrowseName.ToString(),
                    ["typeDefinitionId"] = entry.TypeDefinitionId.ToString(),
                    ["kind"] = entry.Kind.ToString(),
                    ["readings"] = Readings(client, ("predictedTime", entry.PredictedTime),
                        ("activity", entry.Activity), ("location", entry.Location),
                        ("toolIdentifier", entry.ToolIdentifier), ("toolName", entry.ToolName),
                        ("toolNodeId", entry.ToolNodeId), ("partIdentifier", entry.PartIdentifier),
                        ("partName", entry.PartName), ("partNodeId", entry.PartNodeId),
                        ("sourceIdentifier", entry.SourceIdentifier), ("jobNodeId", entry.JobNodeId),
                        ("utilityName", entry.UtilityName))
                })
            };
        }

        /// <summary>
        /// Retains one original value plus server publication order; count-bound observations are not history.
        /// </summary>
        internal static JsonObject Observation(MachineToolClient client, MachineToolDataObservation value)
        {
            return new JsonObject
            {
                ["limitReached"] = value.LimitReached,
                ["droppedNotifications"] = value.DroppedNotifications,
                ["completionReason"] = value.LimitReached ? "LimitReached" : "WindowElapsed",
                ["changes"] = Array(value.Changes, change => new JsonObject
                {
                    ["nodeId"] = change.NodeId.ToString(),
                    ["reading"] = Reading(client, change.Value),
                    ["publishTime"] = change.PublishTime.ToString("O", CultureInfo.InvariantCulture),
                    ["sequenceNumber"] = change.SequenceNumber
                })
            };
        }

        /// <summary>
        /// Encodes raw namespace-qualified fields with the UA codec, preserving native IDs and condition state.
        /// </summary>
        internal static JsonObject Events(MachineToolClient client, MachineToolEventObservation value)
        {
            return new JsonObject
            {
                ["limitReached"] = value.LimitReached,
                ["droppedNotifications"] = value.DroppedNotifications,
                ["refreshCompleted"] = value.RefreshCompleted,
                ["events"] = Array(value.Events, entry => new JsonObject
                {
                    ["notifierNodeId"] = entry.NotifierId.ToString(),
                    ["publishTime"] = entry.PublishTime.ToString("O", CultureInfo.InvariantCulture),
                    ["sequenceNumber"] = entry.SequenceNumber,
                    ["fields"] = Array(entry.Fields, field =>
                    {
                        using var encoder = new JsonEncoder(client.Session.MessageContext);
                        encoder.WriteVariant("Value", field.Value);
                        return new JsonObject
                        {
                            ["browsePath"] = Array(field.BrowsePath, name => JsonValue.Create(name.ToString())),
                            ["value"] = JsonNode.Parse(encoder.CloseAndReturnText())
                        };
                    })
                })
            };
        }

        /// <summary>
        /// Preserves absence instead of encoding a null NodeId as an existing node.
        /// </summary>
        internal static string? OptionalNode(NodeId value)
        {
            return value.IsNull ? null : value.ToString();
        }

        /// <summary>
        /// Maps a native tool and bounded constituent inventory without merging independent life measurements.
        /// </summary>
        private static JsonObject Tool(MachineToolClient client, MachineToolToolReadSnapshot value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["kind"] = value.Kind.ToString(),
                ["locationNodeId"] = OptionalNode(value.LocationNodeId),
                ["toolLifeNodeId"] = OptionalNode(value.ToolLifeNodeId),
                ["readings"] = Readings(client, ("identifier", value.Identifier), ("name", value.Name),
                    ("locationName", value.LocationName), ("placeNumber", value.PlaceNumber),
                    ("controlIdentifier1", value.ControlIdentifier1), ("controlIdentifier2", value.ControlIdentifier2),
                    ("controlIdentifierInterpretation", value.ControlIdentifierInterpretation),
                    ("locked", value.Locked), ("reasonForLocking", value.ReasonForLocking),
                    ("lastUsage", value.LastUsage), ("plannedForOperating", value.PlannedForOperating)),
                ["life"] = Array(value.Life, entry => new JsonObject
                {
                    ["nodeId"] = entry.NodeId.ToString(),
                    ["browseName"] = entry.BrowseName.ToString(),
                    ["typeDefinitionId"] = entry.TypeDefinitionId.ToString(),
                    ["dataType"] = entry.DataType.ToString(),
                    ["readings"] = Readings(client, ("value", entry.Value),
                        ("engineeringUnits", entry.EngineeringUnits),
                        ("indication", entry.Indication), ("isCountingUp", entry.IsCountingUp),
                        ("startValue", entry.StartValue), ("warningValue", entry.WarningValue),
                        ("limitValue", entry.LimitValue))
                }),
                ["tools"] = Array(value.Tools, tool => Tool(client, tool))
            };
        }
    }
}
