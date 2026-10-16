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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;
using ClientSession = Opc.Ua.Client.ISession;

namespace CompanionSuite
{
    /// <summary>
    /// Reads meaningful, independently acquired state through all typed clients on one real encrypted session.
    /// </summary>
    internal static class WorkcellReader
    {
        /// <summary>
        /// Verifies the sample's actual per-instance contents, optional standard vision execution and cleanup.
        /// </summary>
        internal static async Task ReadAsync(
            ClientSession session, ITelemetryContext telemetry, SuiteOptions options, CancellationToken ct)
        {
            Require(session.ConfiguredEndpoint.Description.SecurityMode == MessageSecurityMode.SignAndEncrypt,
                "SignAndEncrypt was not negotiated.");
            Console.WriteLine($"CONNECTED security={session.ConfiguredEndpoint.Description.SecurityMode}");
            var tools = new MachineToolClient(session, telemetry);
            await using var toolsLifetime = tools.ConfigureAwait(false);
            var gms = new GmsClient(session, telemetry);
            await using var gmsLifetime = gms.ConfigureAwait(false);
            var cranes = new CranesHoistsClient(session, telemetry);
            var vision = new MachineVisionClient(session, telemetry);
            ArrayOf<MachineToolDeviceEntry> allMachines = await tools.DiscoverAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            MachineToolDeviceEntry[] mills = allMachines.ToArray()!.Where(value =>
                value.TypeDefinitionId == ExpandedNodeId.ToNodeId(
                    Opc.Ua.MachineTool.ObjectTypeIds.MachineToolType, session.NamespaceUris))
                .OrderBy(value => value.BrowseName.Name, StringComparer.Ordinal).ToArray();
            MachineToolDeviceEntry[] peripherals = allMachines.ToArray()!.Where(value =>
                value.Kind == MachineToolDeviceKind.WirelessPeripheral)
                .OrderBy(value => value.BrowseName.Name, StringComparer.Ordinal).ToArray();
            GmsDeviceEntry[] systems = (await gms.DiscoverAsync(ct).ConfigureAwait(false))
                .ToArray()!.OrderBy(value => value.BrowseName.Name, StringComparer.Ordinal).ToArray();
            CraneEntry[] craneSystems = (await cranes.DiscoverAsync(ct).ConfigureAwait(false))
                .ToArray()!.OrderBy(value => value.BrowseName.Name, StringComparer.Ordinal).ToArray();
            var inspectionSystems = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in vision.EnumerateSystemsAsync(cancellationToken: ct)
                .ConfigureAwait(false))
            {
                inspectionSystems.Add(entry);
            }
            inspectionSystems.Sort((left, right) =>
                string.Compare(left.BrowseName.Name, right.BrowseName.Name, StringComparison.Ordinal));
            var assets = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in vision.EnumerateAssetsAsync(cancellationToken: ct)
                .ConfigureAwait(false))
            {
                assets.Add(entry);
            }
            assets.Sort((left, right) =>
                string.Compare(left.BrowseName.Name, right.BrowseName.Name, StringComparison.Ordinal));
            Require(mills.Length == options.Instances && peripherals.Length == options.Instances &&
                systems.Length == options.Instances && craneSystems.Length == options.Instances &&
                inspectionSystems.Count == options.Instances && assets.Count == options.Instances,
                "The endpoint did not expose the complete sample workcell.");
            var roots = new HashSet<NodeId>();
            for (int index = 0; index < options.Instances; index++)
            {
                int number = index + 1;
                Require(roots.Add(mills[index].NodeId) && roots.Add(peripherals[index].NodeId) &&
                    roots.Add(systems[index].NodeId) && roots.Add(craneSystems[index].NodeId) &&
                    roots.Add(inspectionSystems[index].NodeId) && roots.Add(assets[index].NodeId),
                    "Independent families share a root identifier.");
                await ReadMachineAsync(tools, mills[index].NodeId, number, ct).ConfigureAwait(false);
                await ReadPeripheralAsync(tools, peripherals[index].NodeId, number, ct).ConfigureAwait(false);
                await ReadGmsAsync(gms, systems[index].NodeId, number, ct).ConfigureAwait(false);
                await ReadCraneAsync(cranes, craneSystems[index].NodeId, number, ct).ConfigureAwait(false);
                await ReadVisionAsync(vision, inspectionSystems[index].NodeId, assets[index].NodeId,
                    number, options.Exercise, ct)
                    .ConfigureAwait(false);
            }
            DataValue conformance = await session.ReadValueAsync(
                Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits, ct).ConfigureAwait(false);
            Require(conformance.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units),
                "The shared conformance catalog is not readable.");
            Require(!units.ToArray()!.Any(value => value.Name == "GMS GMS_ProductionStatisticsType"),
                "The undefined upstream GMS production-statistics unit must not be claimed.");
            Console.WriteLine(
                $"WORKCELL_VERIFIED families=5 instances={options.Instances} conformanceUnits={units.Count}");
        }

        /// <summary>
        /// Reads retained production and actual tooling through generated MachineTool descendants.
        /// </summary>
        private static async Task ReadMachineAsync(
            MachineToolClient client, NodeId node, int number, CancellationToken ct)
        {
            MachineToolReadSnapshot values = await client.ReadSnapshotAsync(node, ct).ConfigureAwait(false);
            PrintValue("MACHINE active program", values.ActiveProgramName);
            Require(values.ActiveProgramName.WrappedValue.TryGetValue(out string program) &&
                program == "CELL" + number + ".NC", "A machine returned another instance's active program.");
            MachineToolProductionReadSnapshot production = await client.ReadProductionAsync(node,
                cancellationToken: ct).ConfigureAwait(false);
            Require(production.Jobs.Count == 1, "The sample production plan was not populated.");
            PrintValue("MACHINE job", production.Jobs[0].Identifier);
            MachineToolEquipmentReadSnapshot equipment = await client.ReadEquipmentAsync(node,
                cancellationToken: ct).ConfigureAwait(false);
            Require(equipment.Tools.Count == 1, "The sample tool inventory was not populated.");
            Require(equipment.Tools[0].Identifier.WrappedValue.TryGetValue(out string tool) &&
                tool == "Cutter" + number,
                "A machine returned another instance's tooling.");
            PrintValue("MACHINE cutter", equipment.Tools[0].Identifier);
        }

        /// <summary>
        /// Reads measurement quality and copied records without consuming or clearing the dataset.
        /// </summary>
        private static async Task ReadPeripheralAsync(
            MachineToolClient client, NodeId node, int number, CancellationToken ct)
        {
            WmtpPeripheralClient peripheral = client.GetWirelessPeripheral(node);
            WmtpPeripheralSnapshot state = await peripheral.ReadSnapshotAsync(ct).ConfigureAwait(false);
            PrintValue("WMTP battery", state.Battery);
            ArrayOf<WmtpMeasurementReading> values = await peripheral.ReadMeasurementsAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            Require(values.Count == 1, "The sample measurement was not exposed.");
            Require(values[0].Sample.WrappedValue.TryGetValue(out double temperature) &&
                temperature == 20.0 + number, "A peripheral returned another instance's acquisition.");
            PrintValue("WMTP temperature", values[0].Sample);
            WmtpRecordClient records = await peripheral.GetWorkRecordsAsync(ct).ConfigureAwait(false);
            ArrayOf<WMTPOutputDataType> first = await records.ReadAsync(new WmtpRecordQuery(), cancellationToken: ct)
                .ConfigureAwait(false);
            ArrayOf<WMTPOutputDataType> second = await records.ReadAsync(new WmtpRecordQuery(), cancellationToken: ct)
                .ConfigureAwait(false);
            Require(first.Count == 1 && second.Count == 1 && first[0].ActualValue == 200.0 + number &&
                second[0].Index == first[0].Index, "A passive record query consumed or confused a dataset.");
            Console.WriteLine($"WMTP copied record index={first[0].Index} value={first[0].ActualValue}");
        }

        /// <summary>
        /// Uses GMS qualification, native active-sensor references, result metadata and the shared file client.
        /// </summary>
        private static async Task ReadGmsAsync(GmsClient client, NodeId node, int number, CancellationToken ct)
        {
            GmsSystemReadSnapshot snapshot = await client.ReadSnapshotAsync(node, ct).ConfigureAwait(false);
            GmsEquipmentReadSnapshot equipment = await client.ReadEquipmentAsync(node, ct).ConfigureAwait(false);
            Require(equipment.Sensors.Count == 1, "The native qualified sensor is missing.");
            MachineToolToolLifeReadSnapshot qualified = equipment.Sensors[0].Inherited.Life.ToArray()!.Single();
            Require(qualified.Value.WrappedValue.TryGetValue(out double hours) && hours == 10.0 + number,
                "The native qualification countdown did not match its provider.");
            PrintValue("GMS qualified hours", qualified.Value);
            Require(snapshot.ToolMonitoring != null &&
                snapshot.ToolMonitoring.ActiveTool.Value.WrappedValue.TryGetValue(out ArrayOf<NodeId> active) &&
                active.Count == 1 && active[0] == equipment.Sensors[0].Inherited.NodeId,
                "The GMS active sensor reference escaped its own inventory.");
            GmsResultClient results = client.GetResults(node);
            var result = await results.GetLatestAsync(ct).ConfigureAwait(false);
            Require(result.ResultMetaData!.ResultId == "Result" + number &&
                result.ResultContent[0].TryGetValue(out double value) && value == 100.0 + number,
                "The GMS result belongs to another instance.");
            ByteString content = await results.DownloadAsync("Result" + number, ct).ConfigureAwait(false);
            Require(content.Span.SequenceEqual(ByteString.From((byte)number, 7, 9).Span),
                "The GMS result file did not match its metadata.");
            Console.WriteLine($"GMS result={result.ResultMetaData.ResultId} payload={content.Length} bytes");
        }

        /// <summary>
        /// Reads position, load and protection from native crane types; there are no invented motion commands.
        /// </summary>
        private static async Task ReadCraneAsync(
            CranesHoistsClient client, NodeId node, int number, CancellationToken ct)
        {
            CraneSystemSnapshot snapshot = await client.ReadSystemAsync(node, ct).ConfigureAwait(false);
            CraneElementSnapshot axis = snapshot.Elements.ToArray()!.Single(value =>
                value.Entry.Kind == CraneElementKind.Axis);
            CraneSignalValue load = axis.Signals.ToArray()!.Single(value => value.Signal == CraneSignal.LoadMass);
            CraneSignalValue position = axis.Signals.ToArray()!
                .Single(value => value.Signal == CraneSignal.ActualPosition);
            Require(load.Value.WrappedValue.TryGetValue(out double mass) && mass == 100.0 + number,
                "The crane load is not this instance's acquisition.");
            PrintValue("CRANE load", load.Value);
            PrintValue("CRANE position", position.Value);
            Require(await client.Axis(axis.Entry.NodeId).GetParameterSetAsync(client.Telemetry, ct)
                .ConfigureAwait(false) != null, "The generated crane-axis proxy could not resolve ParameterSet.");
            CraneElementSnapshot protection = snapshot.Elements.ToArray()!.Single(value =>
                value.Entry.Kind == CraneElementKind.ProtectiveFunction);
            PrintValue("CRANE overload active",
                protection.Signals.ToArray()!.Single(value => value.Signal == CraneSignal.Active).Value);
            Require(protection.Relationships.ToArray()!.Any(value => value.Target == axis.Entry.NodeId),
                "Crane protection did not refer to its own axis.");
        }

        /// <summary>
        /// Reads a meaningful simulated inspection result; standard job execution requires explicit opt-in.
        /// </summary>
        private static async Task ReadVisionAsync(
            MachineVisionClient client, NodeId node, NodeId asset, int number, bool exercise, CancellationToken ct)
        {
            ArrayOf<NodeId> sameEntity = await client.GetSameEntityNodesAsync(asset, ct).ConfigureAwait(false);
            Require(sameEntity.Count == 1 && sameEntity[0] == node,
                "The MachineVision asset refers to another inspection runtime.");
            MachineVisionAssetSnapshot assetState = await client.ReadAssetAsync(asset, cancellationToken: ct)
                .ConfigureAwait(false);
            Require(assetState.IsComplete, "The sample asset read exceeded its explicit bounds.");
            MachineVisionAssetValue temperature = assetState.Values.ToArray()!.Single(value =>
                value.BrowsePath.Count == 2 && value.BrowsePath[0].Name == "Health" &&
                value.BrowsePath[1].Name == "Temperature");
            Require(temperature.Value.WrappedValue.TryGetValue(out double degrees) && degrees == 20.0 + number,
                "The MachineVision asset returned another camera's temperature.");
            PrintValue("VISION asset temperature", temperature.Value);
            MachineVisionSystemClient system = client.System(node);
            VisionStateMachineTypeClient control = await system.ControlAsync(ct).ConfigureAwait(false);
            FiniteStateSnapshot state = await control.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
            Console.WriteLine($"VISION state={state.CurrentState.Text} root={node}");
            ResultManagementTypeClient results = await system.ResultsAsync(ct).ConfigureAwait(false);
            (uint handle, ResultDataType result, int error) =
                await results.GetResultByIdAsync(new ResultIdDataType { Id = "Inspection" + number }, 0, ct)
                    .ConfigureAwait(false);
            Require(error == 0, "The standard vision result query failed.");
            try
            {
                Require(result.ResultContent[0].TryGetValue(out int resultValue) && resultValue == number,
                    "An inspection returned another camera's result.");
                Console.WriteLine($"VISION result={result.ResultId.Id} classification={resultValue}");
            }
            finally
            {
                Require(await results.ReleaseResultHandleAsync(handle, ct).ConfigureAwait(false) == 0,
                    "The vision metadata handle did not release.");
            }
            if (!exercise)
            {
                return;
            }
            Require(await control.SelectModeAutomaticAsync(ct).ConfigureAwait(false) == 0,
                "The simulated provider refused automatic mode.");
            VisionAutomaticModeStateMachineTypeClient automatic = await system.AutomaticAsync(ct).ConfigureAwait(false);
            (JobIdDataType job, int jobError) = await automatic.StartSingleJobAsync(
                new MeasIdDataType { Id = "SelfCheck" + number }, new PartIdDataType { Id = "SamplePart" + number },
                new RecipeIdExternalDataType(), new ProductIdDataType(), [], ct).ConfigureAwait(false);
            Require(jobError == 0, "The simulated provider refused the explicit single inspection.");
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(window.Token, ct);
            while ((await automatic.GetCurrentFiniteStateAsync(linked.Token).ConfigureAwait(false)).CurrentState.Text !=
                "Ready")
            {
                await Task.Delay(20, linked.Token).ConfigureAwait(false);
            }
            (uint jobHandle, ResultDataType jobResult, int resultError) = await results.GetResultByIdAsync(
                new ResultIdDataType { Id = "Job-" + job.Id }, 0, ct).ConfigureAwait(false);
            Require(resultError == 0, "The accepted simulated job produced no accessible result.");
            try
            {
                Require(jobResult.JobId.Id == job.Id, "The vision result is not correlated to the accepted job.");
                Console.WriteLine($"EXPLICIT_SIMULATED_JOB job={job.Id} result={jobResult.ResultId.Id}");
            }
            finally
            {
                Require(await results.ReleaseResultHandleAsync(jobHandle, ct).ConfigureAwait(false) == 0,
                    "The job-result metadata handle did not release.");
            }
        }

        /// <summary>
        /// Reports original quality and acquisition time without equating a raw value with Good quality.
        /// </summary>
        private static void PrintValue(string label, in DataValue value)
        {
            Require(!value.IsNull && !StatusCode.IsBad(value.StatusCode), label + " has no usable acquisition.");
            Console.WriteLine(
                $"{label}: {value.WrappedValue}; quality={value.StatusCode}; source={value.SourceTimestamp:O}");
        }

        /// <summary>
        /// Makes partial failures visible in the process exit code instead of printing a successful model count.
        /// </summary>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
