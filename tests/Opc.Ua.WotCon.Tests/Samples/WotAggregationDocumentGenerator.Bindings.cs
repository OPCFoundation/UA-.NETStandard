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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    internal static partial class WotAggregationDocumentGenerator
    {
        public static ArrayOf<PropertyBinding> PropertyBindings => s_propertyBindings;

        /// <summary>
        /// Enriches only source-declared affordances, found by portable local
        /// identity. A missing declaration is an error, never an implicit node.
        /// </summary>
        public static async Task<ArrayOf<SampleDocument>> BindPumpDocumentsAsync(
            ArrayOf<SampleDocument> documents,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, JsonObject> roots = ParseRoots(documents);
            Dictionary<string, NativeBindingNode> nativeNodes = ReadNativeBindingNodes(documents);
            foreach (string pumpName in s_pumpNames)
            {
                for (int bindingIndex = 0; bindingIndex < PropertyBindings.Count; bindingIndex++)
                {
                    PropertyBinding binding = PropertyBindings[bindingIndex];
                    AffordanceLocation location = FindAffordance(
                        roots, "properties", LocalNodeId(pumpName, binding.LocalPath));
                    RequireBindingOwner(location, nativeNodes, WotAffordanceKind.Property);
                    JsonObject property = location.Affordance;
                    property["uav:mapToNodeId"] = LocalNodeId(pumpName, binding.LocalPath);
                    property["forms"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["href"] = SourceEndpoint(binding.Source),
                            ["op"] = binding.IsIdentity
                                ? new JsonArray("readproperty")
                                : new JsonArray("readproperty", "observeproperty"),
                            ["uav:id"] = $"nsu={SourceNamespace(binding.Source)};s={pumpName}.{binding.SourcePath}"
                        }
                    };
                    AddSourceSecurity(location.Root);
                }

                foreach (string source in s_sourceNames)
                {
                    foreach (string operation in s_controlOperations)
                    {
                        AffordanceLocation location = FindAffordance(
                            roots, "actions", LocalNodeId(pumpName, source + operation));
                        RequireBindingOwner(location, nativeNodes, WotAffordanceKind.Action);
                        location.Affordance["forms"] = new JsonArray
                        {
                            CreateOwnedActionForm(source, pumpName, operation)
                        };
                        AddSourceSecurity(location.Root);
                    }
                }

                foreach ((string alarm, string source) in s_alarmSources)
                {
                    AffordanceLocation location = FindAffordance(
                        roots, "events", LocalNodeId(pumpName, alarm + "Alarm"));
                    RequireBindingOwner(location, nativeNodes, WotAffordanceKind.Event);
                    await BindAlarmEventAsync(
                        documents, location, pumpName, alarm, source, cancellationToken).ConfigureAwait(false);
                    AddSourceSecurity(location.Root);
                    foreach (string operation in s_conditionOperations)
                    {
                        AffordanceLocation action = FindAffordance(
                            roots, "actions", LocalNodeId(pumpName, alarm + operation));
                        RequireBindingOwner(action, nativeNodes, WotAffordanceKind.Action);
                        if (!string.Equals(action.ResourceId, location.ResourceId, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"'{action.ResourceId}' does not contain its '{location.Name}' Condition event.");
                        }
                        JsonObject input = action.Affordance["input"]?.AsObject()
                            ?? throw new InvalidOperationException("The source Condition method has no arguments.");
                        input["uav:fieldOrder"] = new JsonArray("EventId", "Comment");
                        input["required"] = new JsonArray("EventId");
                        action.Affordance["uav:conditionAction"] = operation;
                        action.Affordance["uav:actsOn"] = location.Name;
                        action.Affordance["forms"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["href"] = SourceEndpoint(source),
                                ["op"] = new JsonArray("invokeaction"),
                                ["uav:componentOf"] =
                                    $"nsu={SourceNamespace(source)};s={pumpName}.{AlarmConditionPath(alarm)}",
                                ["uav:id"] = operation == "Acknowledge" ? "i=9111" : "i=9113"
                            }
                        };
                    }
                }
            }

            return documents.ToArrayOf(document => document with
            {
                Json = CanonicalJson(roots[document.ResourceId])
            });
        }

        public static string AffordanceReference(
            ArrayOf<SampleDocument> documents,
            string mapName,
            string localNodeId)
        {
            AffordanceLocation location = FindAffordance(ParseRoots(documents), mapName, localNodeId);
            return location.ResourceId + "#" + location.Pointer;
        }

        private static async Task BindAlarmEventAsync(
            ArrayOf<SampleDocument> documents,
            AffordanceLocation location,
            string pumpName,
            string alarm,
            string source,
            CancellationToken cancellationToken)
        {
            SampleDocument original = documents.ToList().Single(
                document => document.ResourceId == location.ResourceId);
            using var generated = WotDocument.Parse(original.Json.ToArray(), CreateLargeDocumentOptions());
            var resolver = new WotEventSelectionResolver(
                new SampleThingResolver(documents), CreateLargeDocumentOptions());
            WotEventSelectionCatalog catalog = RequireValue(
                await resolver.ResolveAsync(generated, cancellationToken: cancellationToken).ConfigureAwait(false),
                location.ResourceId);
            if (!catalog.TryGetSelection(location.Name, out ArrayOf<WotResolvedEventSelectClause> clauses))
            {
                throw new InvalidOperationException($"'{location.Name}' has no generated event selection.");
            }

            var paths = new HashSet<string>(
                clauses.ToList().Select(clause => clause.BrowsePath), StringComparer.Ordinal);
            foreach (string required in s_requiredAlarmPaths)
            {
                if (!paths.Contains(required))
                {
                    throw new InvalidOperationException(
                        $"'{location.Name}' omits required Condition field '{required}'.");
                }
            }

            JsonNode data = location.Affordance["data"]?.DeepClone()
                ?? (catalog.TryGetLinkedData(location.Name, out ReadOnlyMemory<byte> linkedData)
                    ? JsonNode.Parse(linkedData.Span)!
                    : throw new InvalidOperationException($"'{location.Name}' has no generated event schema."));
            RequireSelectedAlarmStates(data.AsObject());
            string definitionName = alarm + "ConditionType";
            if (location.Root["schemaDefinitions"] is not JsonObject definitions)
            {
                definitions = new JsonObject();
                location.Root["schemaDefinitions"] = definitions;
            }
            // These local EventTypes declare no additional fields. Their
            // inherited schema also describes the upstream AlarmConditionType;
            // using that standard anchor avoids selecting against a local-only
            // EventType NodeId that the upstream AddressSpace does not contain.
            definitions[definitionName] = new JsonObject
            {
                ["@type"] = new JsonArray("tm:ThingModel", "uav:eventType"),
                ["uav:id"] = "i=2915",
                ["uav:browseName"] = "AlarmConditionType",
                ["data"] = data
            };
            var selection = new JsonArray();
            for (int clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
            {
                WotResolvedEventSelectClause clause = clauses[clauseIndex];
                selection.Add(new JsonObject
                {
                    ["tm:ref"] = location.ResourceId + "#/schemaDefinitions/" + definitionName,
                    ["uav:browsePath"] = string.Join("/", clause.PathElements.ToList()
                        .Select(element => "ua:" + element))
                });
            }
            location.Affordance["uav:eventSelectClauses"] = selection;
            location.Affordance["uav:conditionType"] = "ua:AlarmConditionType";
            location.Affordance["uav:conditionTypeId"] = "i=2915";
            location.Affordance["forms"] = new JsonArray
            {
                new JsonObject
                {
                    ["href"] = SourceEndpoint(source),
                    ["op"] = new JsonArray("subscribeevent", "unsubscribeevent"),
                    ["uav:id"] = $"nsu={SourceNamespace(source)};s={pumpName}"
                }
            };
        }

        private static void RequireSelectedAlarmStates(JsonObject data)
        {
            IEnumerable<string> required = data["required"] is JsonArray declared
                ? declared.Select(value => value!.GetValue<string>())
                : [];
            data["required"] = StringArray(required.Concat(s_requiredAlarmPaths
                .Where(path => !path.Contains('/', StringComparison.Ordinal))
                .Select(path => path.Length == 0 ? "ConditionId" : path))
                .Distinct(StringComparer.Ordinal));
            JsonObject properties = data["properties"]!.AsObject();
            foreach (string state in s_alarmStateNames)
            {
                JsonObject schema = properties[state]?.AsObject()
                    ?? throw new InvalidOperationException($"The generated alarm schema has no '{state}'.");
                schema["required"] = new JsonArray("Id", "Name");
            }
        }

        private static JsonObject CreateOwnedActionForm(string source, string pumpName, string operation)
        {
            return new JsonObject
            {
                ["href"] = SourceEndpoint(source),
                ["op"] = new JsonArray("invokeaction"),
                ["uav:componentOf"] = $"nsu={SourceNamespace(source)};s={pumpName}",
                ["uav:id"] = $"nsu={SourceNamespace(source)};s={pumpName}.{operation}"
            };
        }

        private static void AddSourceSecurity(JsonObject root)
        {
            root["securityDefinitions"] ??= new JsonObject
            {
                ["nosec_sc"] = new JsonObject { ["scheme"] = "nosec" }
            };
            root["security"] ??= "nosec_sc";
        }

        private static Dictionary<string, JsonObject> ParseRoots(ArrayOf<SampleDocument> documents)
        {
            return documents.ToList().ToDictionary(
                document => document.ResourceId,
                document => JsonNode.Parse(document.Json.Span)!.AsObject(),
                StringComparer.Ordinal);
        }

        private static AffordanceLocation FindAffordance(
            Dictionary<string, JsonObject> roots,
            string mapName,
            string nodeId)
        {
            var matches = new List<AffordanceLocation>();
            foreach ((string resourceId, JsonObject root) in roots)
            {
                if (root[mapName] is JsonObject map)
                {
                    VisitMap(map, "/" + mapName, resourceId, root);
                }
            }
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one '{mapName}' declaration for '{nodeId}'; found {matches.Count}.");
            }
            return matches[0];

            void VisitMap(JsonObject map, string pointer, string resourceId, JsonObject root)
            {
                foreach ((string name, JsonNode? value) in map)
                {
                    if (value is not JsonObject affordance)
                    {
                        continue;
                    }
                    string memberPointer = pointer + "/" + name.Replace("~", "~0", StringComparison.Ordinal)
                        .Replace("/", "~1", StringComparison.Ordinal);
                    if (string.Equals(
                        affordance["uav:id"]?.GetValue<string>(), nodeId, StringComparison.Ordinal))
                    {
                        matches.Add(new AffordanceLocation(resourceId, root, name, affordance, memberPointer));
                    }
                    if (mapName == "properties" && affordance["properties"] is JsonObject nested)
                    {
                        VisitMap(nested, memberPointer + "/properties", resourceId, root);
                    }
                }
            }
        }

        private static Dictionary<string, NativeBindingNode> ReadNativeBindingNodes(ArrayOf<SampleDocument> documents)
        {
            var nodes = new Dictionary<string, NativeBindingNode>(StringComparer.Ordinal);
            foreach (SampleDocument document in documents)
            {
                using var parsed = WotDocument.Parse(document.Json.Memory, CreateLargeDocumentOptions());
                if (!parsed.RootElement.TryGetProperty("uav:nodes", out _) &&
                    !parsed.RootElement.TryGetProperty("uav:nodeSet", out _))
                {
                    continue;
                }
                UANodeSet partition = RequireValue(
                    WotNodeSetConverter.ToNodeSetResult(parsed, CreateLargeDocumentOptions()), document.ResourceId);
                var namespaceUris = new NamespaceTable();
                foreach (string uri in partition.NamespaceUris ?? [])
                {
                    namespaceUris.Append(uri);
                }
                foreach (UANode node in partition.Items ?? [])
                {
                    string id = NodeId.ToExpandedNodeId(
                        NodeId.Parse(node.NodeId ?? throw new InvalidOperationException("A native NodeId is missing.")),
                        namespaceUris).ToString();
                    if (!nodes.TryAdd(id, new NativeBindingNode(document.ResourceId, node)))
                    {
                        throw new InvalidOperationException($"Multiple native partitions own '{id}'.");
                    }
                }
            }
            return nodes;
        }

        private static void RequireBindingOwner(
            AffordanceLocation location,
            Dictionary<string, NativeBindingNode> nativeNodes,
            WotAffordanceKind kind)
        {
            if (!location.Root.ContainsKey("uav:nodes") && !location.Root.ContainsKey("uav:nodeSet"))
            {
                return;
            }
            string id = location.Affordance["uav:id"]!.GetValue<string>();
            if (!nativeNodes.TryGetValue(id, out NativeBindingNode? native) ||
                (kind != WotAffordanceKind.Event && native.ResourceId != location.ResourceId) ||
                !(kind switch
                {
                    WotAffordanceKind.Property => native.Node is UAVariable,
                    WotAffordanceKind.Action => native.Node is UAMethod,
                    WotAffordanceKind.Event => native.Node is UAObjectType,
                    _ => false
                }))
            {
                throw new InvalidOperationException(
                    $"'{location.ResourceId}' cannot overlay undeclared {kind} '{id}' onto a native partition.");
            }
        }

        private static string AlarmConditionPath(string alarm)
        {
            return alarm switch
            {
                "Cavitation" => "Events.SupervisionProcessFluid.Cavitation.Alarm",
                "MotorOverheat" => "Events.SupervisionPumpOperation.MotorOverheat.Alarm",
                _ => throw new ArgumentOutOfRangeException(nameof(alarm), alarm, null)
            };
        }

        internal sealed record PropertyBinding(
            string Name,
            string LocalPath,
            string Source,
            string SourcePath,
            bool IsIdentity = false);

        private sealed record AffordanceLocation(
            string ResourceId,
            JsonObject Root,
            string Name,
            JsonObject Affordance,
            string Pointer);

        private sealed record NativeBindingNode(string ResourceId, UANode Node);

        private static readonly string[] s_pumpNames = ["Pump1", "Pump2"];
        private static readonly string[] s_conditionOperations = ["Acknowledge", "Confirm"];
        private static readonly string[] s_alarmStateNames =
        [
            "EnabledState", "AckedState", "ConfirmedState", "ActiveState"
        ];
        private static readonly string[] s_requiredAlarmPaths =
        [
            "EventId", "EventType", "SourceNode", "SourceName", "Time", "ReceiveTime", "Message", "Severity",
            string.Empty, "ConditionName", "BranchId", "Retain", "EnabledState", "EnabledState/Id",
            "AckedState", "AckedState/Id", "ConfirmedState", "ConfirmedState/Id", "ActiveState", "ActiveState/Id"
        ];

        private static readonly ArrayOf<PropertyBinding> s_propertyBindings = new PropertyBinding[]
        {
            new("DifferentialPressure", "Operational.Measurements.DifferentialPressure", "SourceA",
                "Operational.Measurements.DifferentialPressure"),
            new("FluidTemperature", "Operational.Measurements.FluidTemperature", "SourceA",
                "Operational.Measurements.FluidTemperature"),
            new("MassFlow", "Operational.Measurements.MassFlow", "SourceA", "Operational.Measurements.MassFlow"),
            new("Level", "Operational.Measurements.Level", "SourceA", "Operational.Measurements.Level"),
            new("BearingTemperature", "Operational.Measurements.BearingTemperature", "SourceB",
                "Operational.Measurements.BearingTemperature"),
            new("PumpPowerInput", "Operational.Measurements.PumpPowerInput", "SourceB",
                "Operational.Measurements.PumpPowerInput"),
            new("PumpEfficiency", "Operational.Measurements.PumpEfficiency", "SourceB",
                "Operational.Measurements.PumpEfficiency"),
            new("NumberOfStarts", "Operational.Measurements.NumberOfStarts", "SourceB",
                "Operational.Measurements.NumberOfStarts"),
            new("Cavitation", "Events.SupervisionProcessFluid.Cavitation", "SourceA",
                "Events.SupervisionProcessFluid.Cavitation"),
            new("MotorOverheat", "Events.SupervisionPumpOperation.MotorOverheat", "SourceB",
                "Events.SupervisionPumpOperation.MotorOverheat"),
            new("Manufacturer", "Identification.Manufacturer", "SourceA", "Identification.Manufacturer", true),
            new("SerialNumber", "Identification.SerialNumber", "SourceA", "Identification.SerialNumber", true),
            new("ProductInstanceUri", "Identification.ProductInstanceUri", "SourceA",
                "Identification.ProductInstanceUri", true),
            new("SourceARunning", "SourceARunning", "SourceA", "Running"),
            new("SourceBRunning", "SourceBRunning", "SourceB", "Running")
        };
    }
}
