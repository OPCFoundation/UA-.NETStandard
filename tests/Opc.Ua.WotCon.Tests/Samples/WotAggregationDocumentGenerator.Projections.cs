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

namespace Opc.Ua.WotCon.Tests.Samples
{
    internal static partial class WotAggregationDocumentGenerator
    {
        public static ArrayOf<string> ProjectionGroups => s_projectionGroups;

        public static ArrayOf<SampleDocument> GenerateAssetProjectionDocuments(
            ArrayOf<SampleDocument> pumpDocuments)
        {
            var documents = new List<SampleDocument>();
            foreach (string pumpName in s_pumpNames)
            {
                foreach (string group in ProjectionGroups)
                {
                    JsonObject root = group == "Members"
                        ? CreateMembersProjection(pumpName, pumpDocuments)
                        : CreateGroupProjection(pumpName, group);
                    documents.Add(new SampleDocument(
                        ProjectionResourceId(pumpName, group),
                        $"{pumpName}.{group}.td.json",
                        WoTDocumentKindEnum.ThingDescription,
                        FormatJson(root)));
                }
            }
            return documents.ToArrayOf();
        }

        public static ByteString GeneratePumpAssetProjectionDocument(
            string fileName,
            ArrayOf<SampleDocument> pumpDocuments)
        {
            return GenerateAssetProjectionDocuments(pumpDocuments)
                .ToList().Single(document => string.Equals(document.Path, fileName, StringComparison.Ordinal)).Json;
        }

        public static string ProjectionResourceId(string pumpName, string group)
        {
            return pumpName.ToLowerInvariant() + "-" + group.ToLowerInvariant();
        }

        private static JsonObject CreateMembersProjection(
            string pumpName,
            ArrayOf<SampleDocument> documents)
        {
            var references = new SortedSet<string>(StringComparer.Ordinal);
            var properties = new JsonObject();
            var actions = new JsonObject();
            var events = new JsonObject();
            foreach (PropertyBinding binding in PropertyBindings)
            {
                properties[binding.Name] = Select(
                    "properties", LocalNodeId(pumpName, binding.LocalPath));
            }
            foreach (string source in s_sourceNames)
            {
                foreach (string operation in s_controlOperations)
                {
                    string name = source + operation;
                    actions[name] = Select("actions", LocalNodeId(pumpName, name));
                }
            }
            foreach ((string alarm, _) in s_alarmSources)
            {
                string eventName = alarm + "Alarm";
                events[eventName] = Select("events", LocalNodeId(pumpName, eventName));
                foreach (string operation in s_conditionOperations)
                {
                    string name = alarm + operation;
                    actions[name] = Select("actions", LocalNodeId(pumpName, name));
                }
            }

            JsonObject root = CreateProjectionRoot(pumpName, "Members", references);
            root["properties"] = properties;
            root["actions"] = actions;
            root["events"] = events;
            return root;

            JsonObject Select(string mapName, string nodeId)
            {
                string reference = AffordanceReference(documents, mapName, nodeId);
                references.Add(reference[..reference.IndexOf('#', StringComparison.Ordinal)]);
                return new JsonObject { ["tm:ref"] = reference };
            }
        }

        private static JsonObject CreateGroupProjection(string pumpName, string group)
        {
            string members = ProjectionResourceId(pumpName, "Members");
            JsonObject root = CreateProjectionRoot(pumpName, group, [members]);
            switch (group)
            {
                case "Asset":
                    root["@type"] = new JsonArray("Thing", "uav:projection", "Asset");
                    root["properties"] = SelectMembers(
                        members, "properties", PropertyBindings.ToList().Where(binding => binding.IsIdentity)
                            .Select(binding => binding.Name));
                    var links = new JsonArray();
                    foreach (string child in s_assetGroups)
                    {
                        links.Add(new JsonObject
                        {
                            ["rel"] = "ua:Organizes",
                            ["href"] = ProjectionResourceId(pumpName, child),
                            ["uav:refName"] = child,
                            ["type"] = "application/td+json"
                        });
                    }
                    root["links"] = links;
                    break;
                case "ProcessData":
                case "ConditionData":
                    root["@type"] = new JsonArray("Thing", "uav:projection", "dataset");
                    root["properties"] = SelectMembers(
                        members, "properties", group == "ProcessData" ? s_processMembers : s_conditionMembers,
                        semanticType: "dataPoint");
                    break;
                case "Supervision":
                    root["@type"] = new JsonArray("Thing", "uav:projection", "eventGroup");
                    root["properties"] = SelectMembers(
                        members, "properties", s_alarmSources.Select(alarm => alarm.Alarm));
                    root["events"] = SelectMembers(
                        members, "events", s_alarmSources.Select(alarm => alarm.Alarm + "Alarm"));
                    break;
                case "Management":
                    root["@type"] = new JsonArray("Thing", "uav:projection", "managementGroup");
                    root["properties"] = SelectMembers(
                        members, "properties", s_sourceNames.Select(source => source + "Running"));
                    root["actions"] = SelectMembers(
                        members, "actions",
                        s_sourceNames.SelectMany(source => s_controlOperations.Select(operation => source + operation))
                            .Concat(s_alarmSources.SelectMany(
                                alarm => s_conditionOperations.Select(operation => alarm.Alarm + operation))));
                    // Condition actions must retain their same-document event
                    // targets even in a management-only projection.
                    root["events"] = SelectMembers(
                        members, "events", s_alarmSources.Select(alarm => alarm.Alarm + "Alarm"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(group), group, null);
            }
            return root;
        }

        private static JsonObject SelectMembers(
            string resourceId,
            string mapName,
            IEnumerable<string> names,
            string? semanticType = null)
        {
            var map = new JsonObject();
            foreach (string name in names)
            {
                var selection = new JsonObject { ["tm:ref"] = $"{resourceId}#/{mapName}/{name}" };
                if (semanticType is not null)
                {
                    selection["@type"] = semanticType;
                }
                map[name] = selection;
            }
            return map;
        }

        private static JsonObject CreateProjectionRoot(
            string pumpName,
            string group,
            IEnumerable<string> sourceResources)
        {
            var sources = new JsonArray();
            foreach (string source in sourceResources)
            {
                sources.Add(new JsonObject
                {
                    ["uav:sourceName"] = source,
                    ["href"] = source,
                    ["type"] = "application/td+json",
                    ["uav:routing"] = "source"
                });
            }
            return new JsonObject
            {
                ["@context"] = new JsonArray
                {
                    "https://www.w3.org/2022/wot/td/v1.1",
                    new JsonObject
                    {
                        ["tm"] = "https://www.w3.org/2019/wot/tm#",
                        ["ua"] = "http://opcfoundation.org/UA/",
                        ["uav"] = "http://opcfoundation.org/UA/WoT-Binding/"
                    }
                },
                ["@type"] = new JsonArray("Thing", "uav:projection"),
                ["id"] = $"urn:opcfoundation.org:UA:WotAggregation:Asset:{pumpName}" +
                    (group == "Asset" ? string.Empty : ":" + group),
                ["title"] = group == "Members" || group == "Asset" ? $"{pumpName} {group}" : group,
                ["uav:scenario"] = "urn:opcfoundation.org:UA:WotAggregation:AssetManagement",
                ["securityDefinitions"] = new JsonObject
                {
                    ["nosec_sc"] = new JsonObject { ["scheme"] = "nosec" }
                },
                ["security"] = "nosec_sc",
                ["uav:projects"] = sources
            };
        }

        private static readonly ArrayOf<string> s_projectionGroups = new[]
        {
            "Members", "ProcessData", "ConditionData", "Supervision", "Management", "Asset"
        };

        private static readonly string[] s_assetGroups =
        [
            "ProcessData", "ConditionData", "Supervision", "Management"
        ];

        private static readonly string[] s_processMembers =
        [
            "DifferentialPressure", "FluidTemperature", "MassFlow", "Level"
        ];

        private static readonly string[] s_conditionMembers =
        [
            "BearingTemperature", "PumpPowerInput", "PumpEfficiency", "NumberOfStarts"
        ];
    }
}
