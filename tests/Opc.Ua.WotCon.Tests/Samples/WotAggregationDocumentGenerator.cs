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
                0,
                1_000_000,
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.DifferentialPressure");
            AddProperty(
                properties,
                "FluidTemperature",
                "number",
                "K",
                233.15,
                473.15,
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.FluidTemperature");
            AddProperty(
                properties,
                "BearingTemperature",
                "number",
                "K",
                233.15,
                473.15,
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.BearingTemperature");
            AddProperty(
                properties,
                "PumpPowerInput",
                "number",
                "W",
                0,
                50_000,
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpPowerInput");
            AddProperty(
                properties,
                "MassFlow",
                "number",
                "kg/s",
                0,
                1,
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.MassFlow");
            AddProperty(
                properties,
                "PumpEfficiency",
                "number",
                "%",
                0,
                100,
                "SOURCE_B_ENDPOINT",
                "Operational.Measurements.PumpEfficiency");
            AddProperty(
                properties,
                "Level",
                "number",
                "m",
                0,
                10,
                "SOURCE_A_ENDPOINT",
                "Operational.Measurements.Level");
            AddProperty(
                properties,
                "NumberOfStarts",
                "integer",
                null,
                0,
                uint.MaxValue,
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

        private static void AddProperty(
            JsonObject properties,
            string name,
            string type,
            string? unit,
            double? minimum,
            double? maximum,
            string source,
            string path)
        {
            string sourceNamespace = source == "SOURCE_A_ENDPOINT"
                ? "urn:opcfoundation.org:UA:WotAggregation:SourceA"
                : "urn:opcfoundation.org:UA:WotAggregation:SourceB";
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
            if (minimum.HasValue)
            {
                property["minimum"] = minimum.Value;
            }
            if (maximum.HasValue)
            {
                property["maximum"] = maximum.Value;
            }

            properties[name] = property;
        }
    }
}
