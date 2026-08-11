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
                "number",
                "Pa",
                "0",
                "1000000",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.DifferentialPressure");
            AddProperty(
                properties,
                "FluidTemperature",
                "number",
                "K",
                "233.15",
                "473.15",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.FluidTemperature");
            AddProperty(
                properties,
                "BearingTemperature",
                "number",
                "K",
                "233.15",
                "473.15",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.BearingTemperature");
            AddProperty(
                properties,
                "PumpPowerInput",
                "number",
                "W",
                "0",
                "50000",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpPowerInput");
            AddProperty(
                properties,
                "MassFlow",
                "number",
                "kg/s",
                "0",
                "1",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.MassFlow");
            AddProperty(
                properties,
                "PumpEfficiency",
                "number",
                "%",
                "0",
                "100",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpEfficiency");
            AddProperty(
                properties,
                "Level",
                "number",
                "m",
                "0",
                "10",
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.Level");
            AddProperty(
                properties,
                "NumberOfStarts",
                "integer",
                null,
                "0",
                "4294967295",
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.NumberOfStarts");
            AddProperty(
                properties,
                "Cavitation",
                "boolean",
                null,
                null,
                null,
                "SOURCE_A_ENDPOINT",
                "Events.SupervisionProcessFluid.Cavitation");
            AddProperty(
                properties,
                "MotorOverheat",
                "boolean",
                null,
                null,
                null,
                "SOURCE_B_ENDPOINT",
                "Events.SupervisionPumpOperation.MotorOverheat");
            return properties;
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
                ["uav:conditionTypeId"] = "i=2915",
                ["uav:isEvent"] = true
            };
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
                        ["uav:id"] = $"nsu={sourceNamespace};s=Pump1.{path}"
                    }
                },
                ["observable"] = true,
                ["readOnly"] = true,
                ["title"] = name,
                ["type"] = type,
                ["uav:mapToNodeId"] = $"nsu={PumpInstanceNamespace};s=Pump1.{path}"
            };

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
    }
}
