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
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Recreates the checked-in WoT aggregation documents from their canonical
    /// NodeSet2 sources.
    /// </summary>
    internal static class WotAggregationDocumentGenerator
    {
        public const string PumpInstanceNamespace =
            "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";

        private const string SourceANamespace =
            "urn:opcfoundation.org:UA:WotAggregation:SourceA";
        private const string SourceBNamespace =
            "urn:opcfoundation.org:UA:WotAggregation:SourceB";

        public static byte[] GenerateThingModel(string sourcePath, string title)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                ReadNodeSet(sourcePath),
                title);
            return document.ToCanonicalUtf8();
        }

        /// <summary>
        /// Generates a companion model as the set of linked documents §9.1
        /// describes, one per Node that roots a document.
        /// </summary>
        /// <remarks>
        /// A companion model states many type definitions side by side and has
        /// no single root, so converting it to one document leaves everything
        /// but the first unreachable and forces the whole model into the
        /// <c>uav:nodes</c> projection. The set is what lets it be stated
        /// readably.
        /// </remarks>
        public static IReadOnlyList<GeneratedDocument> GenerateThingModelSet(
            string sourcePath,
            string modelPrefix,
            string title)
        {
            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(
                    ReadNodeSet(sourcePath),
                    modelPrefix,
                    title,
                    CreateLargeDocumentOptions());
            using WotDocumentSet set = result.Value
                ?? throw new InvalidOperationException(
                    $"'{sourcePath}' produced no document set.");

            var generated = new List<GeneratedDocument>(set.Entries.Count);
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                generated.Add(new GeneratedDocument(
                    entry.Href,
                    entry.Document.ToCanonicalUtf8()));
            }
            return generated;
        }

        /// <summary>
        /// One document of a generated set, named by the href that identifies
        /// it within the set.
        /// </summary>
        internal sealed record GeneratedDocument(string Href, byte[] Json);

        /// <summary>
        /// Writes a generated set into its own directory and returns the
        /// manifest entries that name the files.
        /// </summary>
        /// <remarks>
        /// One file per document, in a directory named for the model. The set
        /// is emitted parent before child, so chaining each entry to the one
        /// before it is already a valid load order — a document that names its
        /// parent is never loaded before that parent exists.
        /// </remarks>
        public static IReadOnlyList<ManifestEntry> WriteThingModelSet(
            string documentsDirectory,
            string modelDirectory,
            IReadOnlyList<GeneratedDocument> documents,
            string? firstDependsOn)
        {
            string target = Path.Combine(documentsDirectory, modelDirectory);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            Directory.CreateDirectory(target);

            var entries = new List<ManifestEntry>(documents.Count);
            string? previous = firstDependsOn;
            foreach (GeneratedDocument document in documents)
            {
                string fileName = document.Href + ".json";
                File.WriteAllBytes(Path.Combine(target, fileName), document.Json);
                entries.Add(new ManifestEntry(
                    document.Href,
                    modelDirectory + "/" + fileName,
                    previous));
                previous = document.Href;
            }
            return entries;
        }

        /// <summary>
        /// One <c>documents.json</c> entry.
        /// </summary>
        internal sealed record ManifestEntry(string ResourceId, string Path, string? DependsOn);

        public static byte[] GeneratePumpThingDescription(string sourcePath)
        {
            using WotDocument generated = WotNodeSetConverter.FromNodeSet(
                ReadNodeSet(sourcePath),
                "Sample Pump Aggregate");
            JsonObject root = JsonNode.Parse(generated.Utf8Json.Span)!.AsObject();
            root["actions"] = CreateActions();
            root["events"] = CreateEvents();
            root["properties"] = CreateProperties();

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(root);
            using var document = WotDocument.Parse(json, CreateLargeDocumentOptions());
            return document.ToCanonicalUtf8();
        }

        public static byte[] GeneratePumpAssetProjectionDocument(string fileName)
        {
            JsonObject root = fileName switch
            {
                "Pump1.Members.td.json" => CreateMembersProjection("Pump1"),
                "Pump1.Asset.td.json" => CreateAssetProjection("Pump1"),
                "Pump1.ProcessData.td.json" => CreateDataSetProjection(
                    "Pump1",
                    "ProcessData",
                    s_processDataPoints),
                "Pump1.ConditionData.td.json" => CreateDataSetProjection(
                    "Pump1",
                    "ConditionData",
                    s_conditionDataPoints),
                "Pump1.Supervision.td.json" => CreateSupervisionProjection("Pump1"),
                "Pump1.Management.td.json" => CreateManagementProjection("Pump1"),
                "Pump2.Members.td.json" => CreateMembersProjection("Pump2"),
                "Pump2.Asset.td.json" => CreateAssetProjection("Pump2"),
                "Pump2.ProcessData.td.json" => CreateDataSetProjection(
                    "Pump2",
                    "ProcessData",
                    s_processDataPoints),
                "Pump2.ConditionData.td.json" => CreateDataSetProjection(
                    "Pump2",
                    "ConditionData",
                    s_conditionDataPoints),
                "Pump2.Supervision.td.json" => CreateSupervisionProjection("Pump2"),
                "Pump2.Management.td.json" => CreateManagementProjection("Pump2"),
                _ => throw new ArgumentOutOfRangeException(nameof(fileName), fileName, null)
            };

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(root);
            using var document = WotDocument.Parse(json, CreateLargeDocumentOptions());
            return document.ToCanonicalUtf8();
        }

        public static WotNodeSetConverterOptions CreateLargeDocumentOptions()
        {
            return new WotNodeSetConverterOptions
            {
                MaxJsonDocumentSize = 64 * 1024 * 1024
            };
        }

        public static UANodeSet ReadNodeSet(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return UANodeSet.Read(stream)
                ?? throw new InvalidOperationException($"Could not read '{path}'.");
        }

        private static JsonObject CreateProperties()
        {
            var properties = new JsonObject();
            AddProperty(
                properties,
                "DifferentialPressure",
                "Pump1",
                "number",
                "Pa",
                "0",
                "1000000",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.DifferentialPressure");
            AddProperty(
                properties,
                "FluidTemperature",
                "Pump1",
                "number",
                "K",
                "233.15",
                "473.15",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.FluidTemperature");
            AddProperty(
                properties,
                "BearingTemperature",
                "Pump1",
                "number",
                "K",
                "233.15",
                "473.15",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.BearingTemperature");
            AddProperty(
                properties,
                "PumpPowerInput",
                "Pump1",
                "number",
                "W",
                "0",
                "50000",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpPowerInput");
            AddProperty(
                properties,
                "MassFlow",
                "Pump1",
                "number",
                "kg/s",
                "0",
                "1",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.MassFlow");
            AddProperty(
                properties,
                "PumpEfficiency",
                "Pump1",
                "number",
                "%",
                "0",
                "100",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpEfficiency");
            AddProperty(
                properties,
                "Level",
                "Pump1",
                "number",
                "m",
                "0",
                "10",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.Level");
            AddProperty(
                properties,
                "NumberOfStarts",
                "Pump1",
                "integer",
                null,
                "0",
                "4294967295",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.NumberOfStarts");
            AddProperty(
                properties,
                "Cavitation",
                "Pump1",
                "boolean",
                null,
                null,
                null,
                "SOURCE_A_ENDPOINT",
                "Events.SupervisionProcessFluid.Cavitation");
            AddProperty(
                properties,
                "MotorOverheat",
                "Pump1",
                "boolean",
                null,
                null,
                null,
                "SOURCE_B_ENDPOINT",
                "Events.SupervisionPumpOperation.MotorOverheat");
            AddPump2Properties(properties);
            AddIdentityProperties(properties, "Pump1");
            AddIdentityProperties(properties, "Pump2");
            return properties;
        }

        private static void AddPump2Properties(JsonObject properties)
        {
            AddProperty(
                properties,
                "Pump2DifferentialPressure",
                "Pump2",
                "number",
                "Pa",
                "0",
                "1000000",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.DifferentialPressure");
            AddProperty(
                properties,
                "Pump2FluidTemperature",
                "Pump2",
                "number",
                "K",
                "233.15",
                "473.15",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.FluidTemperature");
            AddProperty(
                properties,
                "Pump2BearingTemperature",
                "Pump2",
                "number",
                "K",
                "233.15",
                "473.15",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.BearingTemperature");
            AddProperty(
                properties,
                "Pump2PumpPowerInput",
                "Pump2",
                "number",
                "W",
                "0",
                "50000",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpPowerInput");
            AddProperty(
                properties,
                "Pump2MassFlow",
                "Pump2",
                "number",
                "kg/s",
                "0",
                "1",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.MassFlow");
            AddProperty(
                properties,
                "Pump2PumpEfficiency",
                "Pump2",
                "number",
                "%",
                "0",
                "100",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpEfficiency");
            AddProperty(
                properties,
                "Pump2Level",
                "Pump2",
                "number",
                "m",
                "0",
                "10",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.Level");
            AddProperty(
                properties,
                "Pump2NumberOfStarts",
                "Pump2",
                "integer",
                null,
                "0",
                "4294967295",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.NumberOfStarts");
            AddProperty(
                properties,
                "Pump2Cavitation",
                "Pump2",
                "boolean",
                null,
                null,
                null,
                "SOURCE_A_ENDPOINT",
                "Events.SupervisionProcessFluid.Cavitation");
            AddProperty(
                properties,
                "Pump2MotorOverheat",
                "Pump2",
                "boolean",
                null,
                null,
                null,
                "SOURCE_B_ENDPOINT",
                "Events.SupervisionPumpOperation.MotorOverheat");
        }

        private static JsonObject CreateActions()
        {
            var actions = new JsonObject();
            AddAction(actions, "resetPump1", "Reset Pump1", "Pump1", "Reset");
            AddAction(actions, "resetPump2", "Reset Pump2", "Pump2", "Reset");
            AddAction(actions, "startPump1", "Start Pump1", "Pump1", "Start");
            AddAction(actions, "startPump2", "Start Pump2", "Pump2", "Start");
            AddAction(actions, "stopPump1", "Stop Pump1", "Pump1", "Stop");
            AddAction(actions, "stopPump2", "Stop Pump2", "Pump2", "Stop");
            return actions;
        }

        private static void AddAction(
            JsonObject actions,
            string name,
            string title,
            string pumpNodeId,
            string methodName)
        {
            actions[name] = new JsonObject
            {
                ["@type"] = "uav:method",
                ["forms"] = new JsonArray
                {
                    CreateActionForm("SOURCE_A_ENDPOINT", SourceANamespace, pumpNodeId, methodName),
                    CreateActionForm("SOURCE_B_ENDPOINT", SourceBNamespace, pumpNodeId, methodName)
                },
                ["title"] = title
            };
        }

        private static JsonObject CreateActionForm(
            string source,
            string sourceNamespace,
            string pumpNodeId,
            string methodName)
        {
            return new JsonObject
            {
                ["href"] = "${" + source + "}",
                ["op"] = new JsonArray("invokeaction"),
                ["uav:componentOf"] = $"nsu={sourceNamespace};s={pumpNodeId}",
                ["uav:id"] = $"nsu={sourceNamespace};s={pumpNodeId}.{methodName}"
            };
        }

        private static JsonObject CreateEvents()
        {
            var events = new JsonObject();
            AddAlarmEvent(
                events,
                "pump1CavitationAlarm",
                "Pump1 Cavitation Alarm",
                "SOURCE_A_ENDPOINT",
                SourceANamespace,
                "Pump1");
            AddAlarmEvent(
                events,
                "pump1MotorOverheatAlarm",
                "Pump1 Motor Overheat Alarm",
                "SOURCE_B_ENDPOINT",
                SourceBNamespace,
                "Pump1");
            AddAlarmEvent(
                events,
                "pump2CavitationAlarm",
                "Pump2 Cavitation Alarm",
                "SOURCE_A_ENDPOINT",
                SourceANamespace,
                "Pump2");
            AddAlarmEvent(
                events,
                "pump2MotorOverheatAlarm",
                "Pump2 Motor Overheat Alarm",
                "SOURCE_B_ENDPOINT",
                SourceBNamespace,
                "Pump2");
            return events;
        }

        private static void AddAlarmEvent(
            JsonObject events,
            string name,
            string title,
            string source,
            string sourceNamespace,
            string pumpNodeId)
        {
            events[name] = new JsonObject
            {
                ["@type"] = "uav:eventType",
                ["data"] = CreateAlarmDataSchema(),
                ["forms"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["href"] = "${" + source + "}",
                        ["op"] = new JsonArray("subscribeevent", "unsubscribeevent"),
                        ["uav:id"] = $"nsu={sourceNamespace};s={pumpNodeId}"
                    }
                },
                ["title"] = title,
                ["uav:conditionType"] = "ua:AlarmConditionType",
                ["uav:conditionTypeId"] = "i=2915"
            };
        }

        private static JsonObject CreateMembersProjection(string pumpName)
        {
            JsonObject root = CreateProjectionRoot(
                $"urn:opcfoundation.org:UA:WotAggregation:Asset:{pumpName}:Members",
                $"{pumpName} members",
                "The member set projected for one Asset.",
                "sample-pump");
            root["properties"] = CreateMemberProperties(pumpName);
            root["actions"] = CreateMemberActions(pumpName);
            root["events"] = CreateMemberEvents(pumpName);
            return root;
        }

        private static JsonObject CreateAssetProjection(string pumpName)
        {
            JsonObject root = CreateProjectionRoot(
                $"urn:opcfoundation.org:UA:WotAggregation:Asset:{pumpName}",
                $"{pumpName} Asset",
                "An Asset projection. It selects identity data and organizes datasets, an event group and " +
                "a management group.",
                MemberResourceId(pumpName));
            root["@type"] = CreateTypeArray("Thing", "uav:projection", "Asset");
            root["properties"] = CreateIdentitySelection(pumpName);
            root["links"] = new JsonArray
            {
                CreateOrganizesLink(GroupResourceId(pumpName, "ProcessData"), "ProcessData"),
                CreateOrganizesLink(GroupResourceId(pumpName, "ConditionData"), "ConditionData"),
                CreateOrganizesLink(GroupResourceId(pumpName, "Supervision"), "Supervision"),
                CreateOrganizesLink(GroupResourceId(pumpName, "Management"), "Management")
            };
            return root;
        }

        private static JsonObject CreateDataSetProjection(
            string pumpName,
            string groupName,
            IReadOnlyList<string> dataPoints)
        {
            JsonObject root = CreateProjectionRoot(
                $"urn:opcfoundation.org:UA:WotAggregation:Asset:{pumpName}:{groupName}",
                groupName,
                $"{groupName} dataset. Its selected members are dataPoints.",
                MemberResourceId(pumpName));
            root["@type"] = CreateTypeArray("Thing", "uav:projection", "dataset");

            var properties = new JsonObject();
            foreach (string dataPoint in dataPoints)
            {
                properties[dataPoint] = new JsonObject
                {
                    ["@type"] = "dataPoint",
                    ["tm:ref"] = $"{MemberResourceId(pumpName)}#/properties/{dataPoint}"
                };
            }
            root["properties"] = properties;
            return root;
        }

        private static JsonObject CreateSupervisionProjection(string pumpName)
        {
            JsonObject root = CreateProjectionRoot(
                $"urn:opcfoundation.org:UA:WotAggregation:Asset:{pumpName}:Supervision",
                "Supervision",
                "An event group selected from event affordances.",
                MemberResourceId(pumpName));
            root["@type"] = CreateTypeArray("Thing", "uav:projection", "eventGroup");
            root["uav:projects"] = CreateProjects(
                MemberResourceId(pumpName),
                new JsonArray
                {
                    new JsonObject
                    {
                        ["@type"] = "uav:eventType",
                        ["uav:affordanceKind"] = "event"
                    }
                });
            return root;
        }

        private static JsonObject CreateManagementProjection(string pumpName)
        {
            JsonObject root = CreateProjectionRoot(
                $"urn:opcfoundation.org:UA:WotAggregation:Asset:{pumpName}:Management",
                "Management",
                "A management group selected from action affordances.",
                MemberResourceId(pumpName));
            root["@type"] = CreateTypeArray("Thing", "uav:projection", "managementGroup");
            root["uav:projects"] = CreateProjects(
                MemberResourceId(pumpName),
                new JsonArray
                {
                    new JsonObject { ["uav:affordanceKind"] = "action" }
                });
            return root;
        }

        private static JsonObject CreateProjectionRoot(
            string id,
            string title,
            string description,
            string sourcePath)
        {
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
                ["@type"] = CreateTypeArray("Thing", "uav:projection"),
                ["id"] = id,
                ["title"] = title,
                ["description"] = description,
                ["uav:scenario"] = "urn:opcfoundation.org:UA:WotAggregation:AssetManagement",
                ["securityDefinitions"] = new JsonObject
                {
                    ["nosec_sc"] = new JsonObject { ["scheme"] = "nosec" }
                },
                ["security"] = "nosec_sc",
                ["uav:projects"] = CreateProjects(sourcePath)
            };
        }

        private static JsonArray CreateProjects(string sourcePath, JsonArray? select = null)
        {
            var source = new JsonObject
            {
                ["uav:sourceName"] = "members",
                ["href"] = sourcePath,
                ["type"] = "application/td+json",
                ["uav:routing"] = "source"
            };
            if (select is not null)
            {
                source["uav:select"] = select;
            }
            return new JsonArray { source };
        }

        private static JsonArray CreateTypeArray(params string[] types)
        {
            var array = new JsonArray();
            foreach (string type in types)
            {
                array.Add(type);
            }
            return array;
        }

        private static JsonObject CreateIdentitySelection(string pumpName)
        {
            var properties = new JsonObject();
            foreach (string property in s_identityProperties)
            {
                properties[property] = new JsonObject
                {
                    ["tm:ref"] = $"{MemberResourceId(pumpName)}#/properties/{property}"
                };
            }
            return properties;
        }

        private static JsonObject CreateMemberProperties(string pumpName)
        {
            var properties = new JsonObject();
            AddMemberProperty(properties, "Manufacturer", $"sample-pump#/properties/{pumpName}Manufacturer");
            AddMemberProperty(properties, "SerialNumber", $"sample-pump#/properties/{pumpName}SerialNumber");
            AddMemberProperty(
                properties,
                "ProductInstanceUri",
                $"sample-pump#/properties/{pumpName}ProductInstanceUri");

            foreach (string property in s_measurementProperties)
            {
                string sourceName = pumpName == "Pump1"
                    ? property
                    : pumpName + property;
                AddMemberProperty(
                    properties,
                    property,
                    $"sample-pump#/properties/{sourceName}");
            }
            return properties;
        }

        private static JsonObject CreateMemberActions(string pumpName)
        {
            var actions = new JsonObject();
            AddMemberAction(actions, "reset", $"sample-pump#/actions/reset{pumpName}");
            AddMemberAction(actions, "start", $"sample-pump#/actions/start{pumpName}");
            AddMemberAction(actions, "stop", $"sample-pump#/actions/stop{pumpName}");
            return actions;
        }

        private static JsonObject CreateMemberEvents(string pumpName)
        {
            string lowerName = pumpName.ToLowerInvariant();
            var events = new JsonObject();
            AddMemberEvent(
                events,
                "CavitationAlarm",
                $"sample-pump#/events/{lowerName}CavitationAlarm");
            AddMemberEvent(
                events,
                "MotorOverheatAlarm",
                $"sample-pump#/events/{lowerName}MotorOverheatAlarm");
            return events;
        }

        private static void AddMemberProperty(JsonObject properties, string name, string reference)
        {
            properties[name] = new JsonObject { ["tm:ref"] = reference };
        }

        private static void AddMemberAction(JsonObject actions, string name, string reference)
        {
            actions[name] = new JsonObject { ["tm:ref"] = reference };
        }

        private static void AddMemberEvent(JsonObject events, string name, string reference)
        {
            events[name] = new JsonObject { ["tm:ref"] = reference };
        }

        private static JsonObject CreateOrganizesLink(string href, string refName)
        {
            return new JsonObject
            {
                ["rel"] = "ua:Organizes",
                ["href"] = href,
                ["uav:refName"] = refName,
                ["type"] = "application/td+json"
            };
        }

        private static string MemberResourceId(string pumpName)
        {
            return pumpName.ToLowerInvariant() + "-members";
        }

        private static string GroupResourceId(string pumpName, string groupName)
        {
            return pumpName.ToLowerInvariant() + "-" + groupName.ToLowerInvariant();
        }

        private static JsonObject CreateAlarmDataSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray(
                    "EventId",
                    "EventType",
                    "SourceNode",
                    "SourceName",
                    "Time",
                    "ReceiveTime",
                    "Message",
                    "Severity",
                    "ConditionId",
                    "ConditionName",
                    "Retain",
                    "EnabledState",
                    "AckedState",
                    "ConfirmedState",
                    "ActiveState"),
                ["properties"] = new JsonObject
                {
                    ["EventId"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["contentEncoding"] = "base64"
                    },
                    ["EventType"] = new JsonObject { ["type"] = "string" },
                    ["SourceNode"] = new JsonObject { ["type"] = "string" },
                    ["SourceName"] = new JsonObject { ["type"] = "string" },
                    ["Time"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                    ["ReceiveTime"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                    ["Message"] = new JsonObject { ["type"] = "string" },
                    ["Severity"] = CreateSeveritySchema(),
                    ["ConditionId"] = new JsonObject { ["type"] = "string" },
                    ["ConditionName"] = new JsonObject { ["type"] = "string" },
                    ["BranchId"] = new JsonObject { ["type"] = "string" },
                    ["Retain"] = new JsonObject { ["type"] = "boolean" },
                    ["ConditionClassId"] = new JsonObject { ["type"] = "string" },
                    ["ConditionClassName"] = new JsonObject { ["type"] = "string" },
                    ["Quality"] = new JsonObject { ["type"] = "string" },
                    ["LastSeverity"] = CreateSeveritySchema(),
                    ["Comment"] = new JsonObject { ["type"] = "string" },
                    ["ClientUserId"] = new JsonObject { ["type"] = "string" },
                    ["EnabledState"] = CreateTwoStateSchema(),
                    ["AckedState"] = CreateTwoStateSchema(),
                    ["ConfirmedState"] = CreateTwoStateSchema(),
                    ["ActiveState"] = CreateTwoStateSchema()
                }
            };
        }

        private static JsonObject CreateSeveritySchema()
        {
            return new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = 1000
            };
        }

        private static JsonObject CreateTwoStateSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["Id"] = new JsonObject { ["type"] = "boolean" },
                    ["Name"] = new JsonObject { ["type"] = "string" }
                }
            };
        }

        private static void AddProperty(
            JsonObject properties,
            string name,
            string pumpNodeId,
            string type,
            string? unit,
            string? minimum,
            string? maximum,
            string source,
            string path)
        {
            string sourceNamespace = source == "SOURCE_A_ENDPOINT"
                ? SourceANamespace
                : SourceBNamespace;
            var property = new JsonObject
            {
                ["@type"] = "uav:variable",
                ["forms"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["href"] = "${" + source + "}",
                        ["op"] = new JsonArray("readproperty", "observeproperty"),
                        ["uav:id"] = $"nsu={sourceNamespace};s={pumpNodeId}.{path}"
                    }
                },
                ["observable"] = true,
                ["readOnly"] = true,
                ["title"] = name,
                ["type"] = type
            };

            if (pumpNodeId == "Pump1")
            {
                // uav:mapToNodeId binds the runtime value; uav:id states which
                // Node this affordance *is* (WoT Binding §5.1.1 vocabulary),
                // which is what a projection View resolves a selected affordance
                // to. Without it a projection can only guess at the Node and
                // ends up organizing references that resolve to nothing.
                string localId = $"nsu={PumpInstanceNamespace};s={pumpNodeId}.{path}";
                property["uav:id"] = localId;
                property["uav:mapToNodeId"] = localId;
            }

            if (unit is not null)
            {
                property["unit"] = unit;
            }
            if (minimum is not null)
            {
                property["minimum"] = ParseNumber(minimum);
            }
            if (maximum is not null)
            {
                property["maximum"] = ParseNumber(maximum);
            }

            properties[name] = property;
        }

        private static void AddIdentityProperties(JsonObject properties, string pumpNodeId)
        {
            AddIdentityProperty(properties, pumpNodeId, "Manufacturer", "LocalizedText", "SOURCE_A_ENDPOINT");
            AddIdentityProperty(properties, pumpNodeId, "SerialNumber", "String", "SOURCE_A_ENDPOINT");
            AddIdentityProperty(properties, pumpNodeId, "ProductInstanceUri", "String", "SOURCE_A_ENDPOINT");
        }

        private static void AddIdentityProperty(
            JsonObject properties,
            string pumpNodeId,
            string name,
            string dataType,
            string source)
        {
            string sourceNamespace = source == "SOURCE_A_ENDPOINT"
                ? SourceANamespace
                : SourceBNamespace;
            string path = $"Identification.{name}";
            var property = new JsonObject
            {
                ["@type"] = "uav:variable",
                ["forms"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["href"] = "${" + source + "}",
                        ["op"] = new JsonArray("readproperty"),
                        ["uav:id"] = $"nsu={sourceNamespace};s={pumpNodeId}.{path}"
                    }
                },
                ["readOnly"] = true,
                ["title"] = name,
                ["type"] = "string",
                ["uav:dataType"] = dataType
            };
            if (pumpNodeId == "Pump1")
            {
                string localId = $"nsu={PumpInstanceNamespace};s={pumpNodeId}.{path}";
                property["uav:id"] = localId;
                property["uav:mapToNodeId"] = localId;
            }
            properties[pumpNodeId + name] = property;
        }

        /// <summary>
        /// Materializes a JSON number from its canonical text. The bounds are
        /// expressed as text rather than as <see cref="double"/> because
        /// <c>System.Text.Json</c> does not format doubles identically on every
        /// target framework: .NET Framework has no shortest-round-trippable
        /// double formatting and falls back to <c>G17</c>, which would render
        /// <c>473.15</c> as <c>473.14999999999998</c> and make the regenerated
        /// document differ from the checked-in canonical bytes. A node parsed
        /// from text keeps its raw representation and therefore serializes
        /// identically everywhere.
        /// </summary>
        private static JsonNode ParseNumber(string canonicalJsonNumber)
        {
            return JsonNode.Parse(canonicalJsonNumber)
                ?? throw new InvalidOperationException(
                    $"'{canonicalJsonNumber}' is not a JSON number.");
        }

        private static readonly string[] s_identityProperties =
        [
            "Manufacturer",
            "ProductInstanceUri",
            "SerialNumber"
        ];

        private static readonly string[] s_processDataPoints =
        [
            "DifferentialPressure",
            "FluidTemperature",
            "MassFlow",
            "Level"
        ];

        private static readonly string[] s_conditionDataPoints =
        [
            "BearingTemperature",
            "PumpPowerInput",
            "PumpEfficiency",
            "NumberOfStarts"
        ];

        private static readonly string[] s_measurementProperties =
        [
            "DifferentialPressure",
            "FluidTemperature",
            "BearingTemperature",
            "PumpPowerInput",
            "MassFlow",
            "PumpEfficiency",
            "Level",
            "NumberOfStarts"
        ];
    }
}
