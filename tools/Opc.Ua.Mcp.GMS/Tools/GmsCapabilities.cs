/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Reports actual per-instance optional surfaces through native typed reads and generated proxy navigation.
    /// </summary>
    internal static class GmsCapabilities
    {
        /// <summary>
        /// Inspects this GMS, not its namespace, and never equates method presence with user authorization.
        /// </summary>
        internal static async Task<JsonObject> ReadAsync(GmsClient client, NodeId node, CancellationToken ct)
        {
            GmsSystemReadSnapshot system = await client.ReadSnapshotAsync(node, ct).ConfigureAwait(false);
            GmsEquipmentReadSnapshot inventory = await client.ReadEquipmentAsync(node, ct).ConfigureAwait(false);
            GmsCalibrationReadSnapshot? calibration = await client.ReadCalibrationAsync(node, ct).ConfigureAwait(false);
            GmsProductionReadSnapshot production = await client.ReadProductionAsync(node, ct).ConfigureAwait(false);
            GMSTypeClient root = client.GetProxy(node);
            GMSEquipmentTypeClient? equipment = await root.GetEquipmentAsync(client.Telemetry, ct)
                .ConfigureAwait(false);
            GMSResultManagementTypeClient? results =
                await root.GetResultManagementAsync(client.Telemetry, ct).ConfigureAwait(false);
            return new JsonObject
            {
                ["nodeId"] = node.ToString(),
                ["workspace"] = system.Identification.Workspace.IsPresent,
                ["subDeviceClass"] = system.Identification.SubDeviceClass.IsPresent,
                ["loading"] = system.Loading != null,
                ["activeToolMonitoring"] = system.ToolMonitoring != null,
                ["sensorInventory"] = !inventory.Inherited.ToolsNodeId.IsNull,
                ["sensorCount"] = inventory.Sensors.Count,
                ["accessories"] = equipment != null &&
                    await equipment.GetAccessoriesAsync(client.Telemetry, ct).ConfigureAwait(false) != null,
                ["accessoryCount"] = inventory.Accessories.Count,
                ["additionalSensors"] = equipment != null &&
                    await equipment.GetAdditionalSensorAsync(client.Telemetry, ct).ConfigureAwait(false) != null,
                ["additionalSensorCount"] = inventory.AdditionalSensors.Count,
                ["calibration"] = calibration != null,
                ["retainedProductionPlan"] = !production.Inherited.ProductionPlanId.IsNull,
                ["jobCount"] = production.Jobs.Count,
                ["inheritedProductionStatistics"] = !production.Inherited.PartsProducedInLifetime.IsNull,
                ["resultManagement"] = results != null,
                ["publishedResults"] = results != null &&
                    await results.GetResultsAsync(client.Telemetry, ct).ConfigureAwait(false) != null,
                ["corrections"] = results != null &&
                    await results.GetCorrectionsFolderAsync(client.Telemetry, ct).ConfigureAwait(false) != null,
                ["resultFileTransfer"] = results != null &&
                    await results.GetResultTransferAsync(client.Telemetry, ct).ConfigureAwait(false) != null,
                ["methodAuthorizationVerified"] = false,
                ["completeGmsProductionFacetClaimed"] = false,
                ["limitation"] = "GMS_ProductionStatisticsType is undefined upstream; no complete GMS Production " +
                    "facet or external certification is claimed. Presence is not method authorization."
            };
        }
    }
}
