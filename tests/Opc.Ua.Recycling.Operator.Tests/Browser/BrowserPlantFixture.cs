/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Opc.Ua.Recycling.Operator.Tests
{
    /// <summary>
    /// Deterministic browser-only fixtures. Production has no simulated browser data path.
    /// </summary>
    internal static class BrowserPlantFixture
    {
        /// <summary>
        /// Creates all ten roles and their real 21 substep names with explicit test observations.
        /// </summary>
        internal static JsonObject CreateState()
        {
            var processes = new JsonArray();
            int stageIndex = 0;
            foreach ((string process, string[] names) in ProcessStages())
            {
                var stages = new JsonArray();
                foreach (string name in names)
                {
                    int index = stageIndex++;
                    stages.Add(new JsonObject
                    {
                        ["index"] = index,
                        ["name"] = name,
                        ["state"] = index == 7 ? "Jammed" : "Executing",
                        ["enabled"] = true,
                        ["jamAlarm"] = index == 7,
                        ["strandBreakAlarm"] = false,
                        ["inventoryMg"] = "4200000",
                        ["transferredMg"] = "8400000",
                        ["electricalJoules"] = "3600000",
                        ["thermalJoules"] = "0",
                        ["executions"] = "2",
                        ["ph"] = index == 10 ? JsonValue.Create(8.5) : null,
                        ["phSourceUtc"] = index == 10 ? "2026-10-15T12:00:00Z" : null,
                        ["origin"] = "Synthetic sample"
                    });
                }
                processes.Add(new JsonObject
                {
                    ["name"] = process,
                    ["initialized"] = true,
                    ["leaseExpiresUtc"] = "2099-10-15T12:00:00Z",
                    ["nativeConfigured"] = true,
                    ["stages"] = stages
                });
            }
            return new JsonObject
            {
                ["connection"] = "Connected",
                ["detail"] = string.Empty,
                ["available"] = true,
                ["receivedUtc"] = "2026-10-15T12:00:00Z",
                ["sourceUtc"] = "2026-10-15T12:00:00Z",
                ["quality"] = "Good",
                ["lineId"] = "browser-test-line",
                ["runId"] = "browser-test-run",
                ["scenario"] = "combined",
                ["configurationId"] = "browser-test-configuration",
                ["revision"] = "9007199254740993",
                ["tick"] = "42",
                ["logicalSeconds"] = "1260",
                ["lifecycle"] = "Running",
                ["blockingReason"] = string.Empty,
                ["summary"] = new JsonObject
                {
                    ["inputMg"] = "126000000",
                    ["wipMg"] = "8000000",
                    ["packagedMg"] = "96000000",
                    ["electricalJoules"] = "3600000",
                    ["thermalJoules"] = "7200000",
                    ["packageCount"] = 24,
                    ["heldCount"] = 1
                },
                ["processes"] = processes,
                ["holds"] = new JsonArray(new JsonObject
                {
                    ["id"] = "9007199254740995",
                    ["tick"] = "40",
                    ["stage"] = 5,
                    ["reason"] = "Color threshold exceeded",
                    ["disposition"] = "Held",
                    ["disposedBy"] = string.Empty,
                    ["dispositionRevision"] = "0"
                }),
                ["trends"] = CreateTrends(),
                ["commands"] = new JsonArray()
            };
        }

        /// <summary>
        /// Creates independent native readings including zero, false and explicit unavailable data.
        /// </summary>
        internal static JsonObject CreateInspector(JsonObject state, string process)
        {
            JsonNode selected = null!;
            foreach (JsonNode? item in state["processes"]!.AsArray())
            {
                if (item!["name"]!.GetValue<string>() == process)
                {
                    selected = item;
                    break;
                }
            }
            return new JsonObject
            {
                ["process"] = selected.DeepClone(),
                ["runId"] = state["runId"]!.DeepClone(),
                ["configurationId"] = state["configurationId"]!.DeepClone(),
                ["tick"] = state["tick"]!.DeepClone(),
                ["nativeAvailable"] = true,
                ["nativeDetail"] = string.Empty,
                ["readings"] = new JsonArray(
                    Reading("DriveSpeed", "0", "rpm", "Good", state),
                    Reading("PumpStatus", "False", string.Empty, "UncertainLastUsableValue", state),
                    Reading("OutletMoisture", null, "%", "BadNoData", state))
            };
        }

        private static JsonObject Reading(
            string name, string? value, string unit, string quality, JsonObject state)
        {
            return new JsonObject
            {
                ["name"] = name,
                ["value"] = value,
                ["unit"] = unit,
                ["quality"] = quality,
                ["sourceUtc"] = "2026-10-15T12:00:00Z",
                ["origin"] = "Native companion test observation",
                ["runId"] = state["runId"]!.DeepClone(),
                ["tick"] = state["tick"]!.DeepClone(),
                ["projectionLagTicks"] = "0"
            };
        }

        private static JsonArray CreateTrends()
        {
            var trends = new JsonArray();
            for (int tick = 39; tick <= 42; tick++)
            {
                trends.Add(new JsonObject
                {
                    ["runId"] = "browser-test-run",
                    ["tick"] = tick.ToString(CultureInfo.InvariantCulture),
                    ["logicalSeconds"] = (tick * 30).ToString(CultureInfo.InvariantCulture),
                    ["receivedUtc"] = "2026-10-15T12:00:00Z",
                    ["gap"] = tick == 41,
                    ["wipMg"] = "8000000",
                    ["packagedMg"] = ((tick - 38) * 4000000L).ToString(CultureInfo.InvariantCulture),
                    ["electricalJoules"] = "3600000",
                    ["thermalJoules"] = "7200000"
                });
            }
            return trends;
        }

        private static IEnumerable<(string Process, string[] Stages)> ProcessStages()
        {
            yield return ("Collection", ["collection-bin", "collection-transport"]);
            yield return ("Sorting", ["inbound-identification", "feed-conveyor", "optical-sorter-one",
                "optical-sorter-two"]);
            yield return ("Shredding", ["primary-shredding", "secondary-granulation"]);
            yield return ("Washing", ["pre-wash", "friction-wash", "hot-wash", "rinse"]);
            yield return ("DensitySeparation", ["density-separation"]);
            yield return ("Drying", ["mechanical-dewatering", "desiccant-thermal-drying", "post-dry-buffer"]);
            yield return ("Extrusion", ["extrusion"]);
            yield return ("MeltFiltration", ["melt-filtration"]);
            yield return ("Pelletizing", ["pelletizing", "pellet-screening"]);
            yield return ("Bagging", ["bagging"]);
        }
    }
}
