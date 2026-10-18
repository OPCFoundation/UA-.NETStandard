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
