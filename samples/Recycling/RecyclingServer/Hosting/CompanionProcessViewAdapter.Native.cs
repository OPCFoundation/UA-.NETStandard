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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Extrusion;
using Opc.Ua.Extrusion.Extruder;
using Opc.Ua.Extrusion.Filter;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Extrusion.Pelletizer;
using Opc.Ua.Extrusion.Server;
using Opc.Ua.GranulateDrying;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Recycling
{
    public sealed partial class CompanionProcessViewAdapter
    {
        private async ValueTask BuildNativeProcessAsync(StageBinding stage, CancellationToken cancellationToken)
        {
            if (stage.Index == 14)
            {
                await BuildDryerAsync(stage, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (stage.Index is not (16 or 17 or 18))
            {
                return;
            }
            m_extrusionRuntime ??= ExtrusionRuntime.Create(RequireContext().Manager);
            if (stage.Index == 16)
            {
                ExtrusionDeviceOptions lineOptions = NativeOptions("extrusion-line");
                lineOptions.Features.Include("ProductionParameters/Throughput");
                ExtrusionDevice<ExtrusionLine_InterfaceState> line = await m_extrusionRuntime
                    .CreateLineAsync(lineOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
                await AdoptExtrusionAsync(stage, line, cancellationToken).ConfigureAwait(false);

                ExtrusionDeviceOptions options = NativeOptions("extrusion-extruder");
                options.Features
                    .WithInstances("TemperatureZones/TemperatureZone_<Nr>", 1)
                    .Include("TemperatureZones/TemperatureZone_<Nr>/SetValue")
                    .WithInstances("MeltTemperatureZones/<Name>_<Nr>", 1)
                    .WithInstances("MeltPressureZones/<Name>_<Nr>", 1)
                    .Include("Throughput/ActualValue");
                ExtrusionDevice<Extruder_InterfaceState> extruder = await m_extrusionRuntime
                    .CreateExtruderAsync(options, cancellationToken: cancellationToken).ConfigureAwait(false);
                await AdoptExtrusionAsync(stage, extruder, cancellationToken).ConfigureAwait(false);
                Link(line.Node, extruder.Node);
                await RegisterNativePartAsync(
                    extruder.Find<BaseObjectState>("TemperatureZones/TemperatureZone_001"),
                    "extrusion-temperature-zone", cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(
                    extruder.Find<BaseObjectState>("MeltTemperatureZones/MeltTemperatureZone_001"),
                    "extrusion-melt-temperature", cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(
                    extruder.Find<BaseObjectState>("MeltPressureZones/MeltPressureZone_001"),
                    "extrusion-melt-pressure", cancellationToken).ConfigureAwait(false);
            }
            else if (stage.Index == 17)
            {
                ExtrusionDeviceOptions options = NativeOptions("melt-filtration-filter");
                options.Features.WithInstances("TemperatureZones/TemperatureZone_<Nr>", 1)
                    .Include("TemperatureZones/TemperatureZone_<Nr>/SetValue")
                    .WithInstances("FilterPackage_<Nr>", 1)
                    .WithInstances("FilterPackage_<Nr>/ScreenPackage_<Nr>", 1);
                ExtrusionDevice<Filter_InterfaceState> filter = await m_extrusionRuntime
                    .CreateFilterAsync(options, cancellationToken: cancellationToken).ConfigureAwait(false);
                await AdoptExtrusionAsync(stage, filter, cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(filter.Find<FilterPackageState>("FilterPackage_001"),
                    "melt-filtration-package", cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(filter.Find<ScreenPackageState>("FilterPackage_001/ScreenPackage_001"),
                    "melt-filtration-screen", cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(
                    filter.Find<BaseObjectState>("TemperatureZones/TemperatureZone_001"),
                    "melt-filtration-temperature-zone", cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ExtrusionDeviceOptions options = NativeOptions("pelletizing-pelletizer");
                options.Features.WithInstances("KnifePackage_<Nr>", 1).WithInstances("DiePlate_<Nr>", 1)
                    .Include("Drive/Speed/ActualValue");
                ExtrusionDevice<Pelletizer_InterfaceState> pelletizer = await m_extrusionRuntime
                    .CreatePelletizerAsync(options, node => ExtrusionUnits.Configure(
                        RequireContext().Context, node.Drive!.Speed!.ActualValue!,
                        ScaleUnits.Create("RPM", "rpm", "revolutions per minute")), cancellationToken)
                    .ConfigureAwait(false);
                await AdoptExtrusionAsync(stage, pelletizer, cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(pelletizer.Find<BaseObjectState>("Drive"),
                    "pelletizing-drive", cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(pelletizer.Find<KnifePackageState>("KnifePackage_001"),
                    "pelletizing-knives", cancellationToken).ConfigureAwait(false);
                await RegisterNativePartAsync(pelletizer.Find<DiePlateState>("DiePlate_001"),
                    "pelletizing-die-plate", cancellationToken).ConfigureAwait(false);
            }
            stage.Machine.State.BindingStatus!.Value =
                "Native OPC 40084 instances with committed readings; production controls are deliberately refused.";
        }

        private ExtrusionDeviceOptions NativeOptions(string name)
        {
            var options = new ExtrusionDeviceOptions(new ExtrusionIdentity(
                "OPCFoundationSample", m_options.LineId + "-" + name,
                new LocalizedText("Synthetic recycling " + name), AssetUri(name), "Committed recycling coordinator"))
            {
                LineId = m_options.LineId,
                Commands = m_nativeCommands
            };
            options.Common.AccessPolicy = m_nativeCommands;
            options.Common.StartDevices = m_nativeCommands;
            options.Common.Parameters = m_nativeCommands;
            options.Common.ClosedLoopControl = m_nativeCommands;
            options.Common.MachineConfiguration = m_nativeCommands;
            return options;
        }

        private async ValueTask AdoptExtrusionAsync<TState>(
            StageBinding stage, ExtrusionDevice<TState> device, CancellationToken cancellationToken)
            where TState : ExtrusionDeviceState
        {
            await RequireContext().GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                device.AsNode(), cancellationToken: cancellationToken).ConfigureAwait(false);
            Link(stage.Machine.State.Equipment!, device.Node);
            stage.Extrusion.Add(new NativeExtrusionProjection(device.Node, device.Find, device.State.ApplyAsync));
            TrackNativeStateMachines(device.Node);
            Protect(device.Node);
        }

        private async ValueTask BuildDryerAsync(StageBinding stage, CancellationToken cancellationToken)
        {
            IMachineryBuildContext context = RequireContext();
            m_dryingRuntime ??= context.Manager.CreateGranulateDryingRuntime();
            var options = new GranulateDryingDeviceOptions
            {
                BrowseName = "desiccant-thermal-drying-device",
                Manufacturer = new LocalizedText("OPC Foundation sample"),
                Model = new LocalizedText("Synthetic desiccant-based granulate dryer"),
                SerialNumber = m_options.LineId + "-thermal-dryer",
                ProductInstanceUri = AssetUri("desiccant-thermal-drying-device"),
                Features = GranulateDryingDeviceFeatures.TotalEnergy | GranulateDryingDeviceFeatures.OperationMode,
                Bins =
                [
                    new GranulateDryingBinOptions
                    {
                        Features = GranulateDryingBinFeatures.MaterialIdentification |
                            GranulateDryingBinFeatures.BatchNumber | GranulateDryingBinFeatures.MaterialDryingTime |
                            GranulateDryingBinFeatures.RemainingMaterialDryingTime |
                            GranulateDryingBinFeatures.ExpiredDryingTime | GranulateDryingBinFeatures.AirFlow |
                            GranulateDryingBinFeatures.DewPoint | GranulateDryingBinFeatures.DryAirTemperature |
                            GranulateDryingBinFeatures.AirSource | GranulateDryingBinFeatures.TemperatureController
                    }
                ],
                DesiccantBeds =
                [
                    new GranulateDryingBedOptions
                    {
                        Features = GranulateDryingBedFeatures.AirFlow |
                            GranulateDryingBedFeatures.TemperatureOfDesiccantBed |
                            GranulateDryingBedFeatures.DewPoint | GranulateDryingBedFeatures.Controller
                    }
                ],
                AnalogMetadata =
                [
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Bin(1), "Temperature/ActualValue",
                        GranulateDryingUnits.Celsius, new Range(-50, 200)),
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Bin(1), "AirFlow/ActualValue",
                        GranulateDryingUnits.CubicMetrePerHour, new Range(0, 10000)),
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Bed(1), "AirFlow/ActualValue",
                        GranulateDryingUnits.LitrePerSecond, new Range(0, 10000 / 3.6))
                ]
            };
            stage.Drying = await m_dryingRuntime.CreateDeviceAsync(
                options, new ReadOnlyDryingAuthority(), m_nativeCommands, cancellationToken).ConfigureAwait(false);
            await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                stage.Drying.AsNode(), cancellationToken: cancellationToken).ConfigureAwait(false);
            Link(stage.Machine.State.Equipment!, stage.Drying.State);
            await RegisterNativePartAsync(stage.Drying.State.Dryer!, "thermal-dryer", cancellationToken)
                .ConfigureAwait(false);
            await RegisterNativePartAsync(stage.Drying.Bin(1), "thermal-drying-bin", cancellationToken)
                .ConfigureAwait(false);
            await RegisterNativePartAsync(stage.Drying.Bed(1), "thermal-desiccant-bed", cancellationToken)
                .ConfigureAwait(false);
            TrackNativeStateMachines(stage.Drying.State);
            Protect(stage.Drying.State);
            stage.Machine.State.BindingStatus!.Value =
                "Native OPC 40082-6 desiccant dryer, bin and bed with committed readings; remote control disabled.";
        }

        private async ValueTask RegisterNativePartAsync(
            BaseObjectState part, string name, CancellationToken cancellationToken)
        {
            IMachineryBuildContext context = RequireContext();
            var browseName = new QualifiedName(Di.BrowseNames.Identification,
                context.Context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi));
            if (part.FindChildWithQualifiedName(context.Context, browseName) == null)
            {
                MachineryComponentIdentificationState identity = context.Context
                    .CreateInstanceOfMachineryComponentIdentificationType(part, browseName);
                identity.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
                identity.Manufacturer!.Value = new LocalizedText("OPC Foundation sample");
                identity.SerialNumber!.Value = m_options.LineId + "-" + name;
                identity.AddProductInstanceUri(context.Context, value => value.Value = AssetUri(name));
                part.AddChild(identity);
                await context.Manager.AddPredefinedNodeAsync(identity, cancellationToken).ConfigureAwait(false);
            }
            await RegisterAssetAsync(part, cancellationToken).ConfigureAwait(false);
            Protect(part);
        }

        private async ValueTask ProjectNativeProcessAsync(
            StageBinding binding, StageSnapshot stage, CommittedSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (binding.Drying is { } dryer)
            {
                await ProjectDryerAsync(dryer, stage, snapshot, cancellationToken).ConfigureAwait(false);
                Protect(dryer.State);
            }
            foreach (NativeExtrusionProjection device in binding.Extrusion)
            {
                var values = new List<ExtrusionValue>();
                AddExtrusionValue(device, values, "IsPresent", Variant.From(true), snapshot);
                if (device.Node is ExtrusionLine_InterfaceState)
                {
                    AddExtrusionValue(device, values, "ProductionParameters/Throughput",
                        Variant.From(Rate(stage.Transferred.Total / 1_000_000.0, snapshot)), snapshot);
                    bool knownQuality = stage.Transferred.Total > 0;
                    bool held = snapshot.Holds.Contains(hold => hold.Disposition != HoldDisposition.Released);
                    AddExtrusionValue(device, values, "ProductionParameters/GoodProduct",
                        knownQuality ? Variant.From(!held) : Variant.Null, snapshot,
                        knownQuality ? StatusCodes.Good : StatusCodes.BadNoData);
                }
                else if (device.Node is Extruder_InterfaceState)
                {
                    double temperature = ThermalTemperature(stage, m_defaults.MeltTemperatureC);
                    AddExtrusionValue(device, values, "MeltTemperatureZones/MeltTemperatureZone_001/ActualValue",
                        Variant.From(temperature), snapshot);
                    AddExtrusionValue(device, values, "MeltPressureZones/MeltPressureZone_001/ActualValue",
                        Variant.From(stage.State == EquipmentState.Executing ? m_defaults.MeltPressureBar : 0),
                        snapshot);
                    AddExtrusionValue(device, values, "Throughput/ActualValue",
                        Variant.From(Rate(stage.Transferred.Total / 1_000_000.0, snapshot)), snapshot);
                    AddTemperatureZone(device, values, temperature, snapshot);
                }
                else if (device.Node is Filter_InterfaceState)
                {
                    double loading = (stage.Rejected.Dirt + stage.Rejected.Labels) /
                        (m_defaults.FilterCapacityKg * 1_000_000);
                    FilterPackageStatusEnumeration status = loading >= 1
                        ? FilterPackageStatusEnumeration.CHANGE_REQUIRED
                        : stage.State == EquipmentState.Executing
                            ? FilterPackageStatusEnumeration.ACTIVE : FilterPackageStatusEnumeration.NOT_ACTIVE;
                    AddExtrusionValue(device, values, "FiltrationFineness",
                        Variant.From(m_nativeOptions.FiltrationFinenessMicrometres), snapshot);
                    AddExtrusionValue(device, values, "FilterPackage_001/Name",
                        Variant.From(new LocalizedText("Synthetic filter package")), snapshot);
                    AddExtrusionValue(device, values, "FilterPackage_001/Status", Variant.From(status), snapshot);
                    AddExtrusionValue(device, values, "FilterPackage_001/ScreenPackage_001/Name",
                        Variant.From(new LocalizedText("Synthetic screen package")), snapshot);
                    AddTemperatureZone(device, values,
                        ThermalTemperature(snapshot.Stages[16], m_defaults.MeltTemperatureC), snapshot);
                }
                else if (device.Node is Pelletizer_InterfaceState)
                {
                    AddExtrusionValue(device, values, "Drive/Speed/ActualValue",
                        Variant.From(stage.State == EquipmentState.Executing ? m_nativeOptions.PelletizerSpeedRpm : 0),
                        snapshot);
                    AddExtrusionValue(device, values, "KnifePackage_001/Name",
                        Variant.From(new LocalizedText("Synthetic knife package")), snapshot);
                    AddExtrusionValue(device, values, "KnifePackage_001/Amount", Variant.From((ushort)4), snapshot);
                    AddExtrusionValue(device, values, "DiePlate_001/Name",
                        Variant.From(new LocalizedText("Synthetic die plate")), snapshot);
                }
                await device.Publish(new ExtrusionOperatingSnapshot(
                    NativeRevision(snapshot), NativeItemState(stage), MachineryOperationModeValue.Processing,
                    ExtrusionExecutingState.ControlledRun, snapshot.SourceTimestampUtc),
                    values.ToArrayOf(), cancellationToken).ConfigureAwait(false);
                Protect(device.Node);
            }
        }

        private void AddTemperatureZone(
            NativeExtrusionProjection device, List<ExtrusionValue> values, double actual, CommittedSnapshot snapshot)
        {
            AddExtrusionValue(device, values, "TemperatureZones/TemperatureZone_001/IsPresent",
                Variant.From(true), snapshot);
            AddExtrusionValue(device, values, "TemperatureZones/TemperatureZone_001/ActualValue",
                Variant.From(actual), snapshot);
            AddExtrusionValue(device, values, "TemperatureZones/TemperatureZone_001/SetValue",
                Variant.From(m_defaults.MeltTemperatureC), snapshot);
            // Lower deviations, if selected by a future profile, must be signed negative offsets.
        }

        private void AddExtrusionValue(
            NativeExtrusionProjection device,
            List<ExtrusionValue> values,
            string path,
            Variant value,
            CommittedSnapshot snapshot,
            StatusCode status = default)
        {
            BaseVariableState variable = device.Find(path) as BaseVariableState ??
                throw new InvalidOperationException("The selected native member is not a variable: " + path);
            TrackNativeValue(variable);
            values.Add(new ExtrusionValue(variable.NodeId, new DataValue(value, status, snapshot.SourceTimestampUtc)));
        }

        private async ValueTask ProjectDryerAsync(
            GranulateDryingDevice dryer, StageSnapshot stage, CommittedSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            StageRecipe recipe = m_options.Simulation.Stages[stage.Index];
            MaterialParcel? oldest = snapshot.Parcels.Span.ToArray().Where(parcel => parcel.Stage == stage.Index)
                .OrderBy(static parcel => parcel.EnteredTick).ThenBy(static parcel => parcel.Id).FirstOrDefault();
            double tickSeconds = snapshot.Tick > 0 ? (double)snapshot.LogicalSeconds / snapshot.Tick :
                m_options.Simulation.TickSeconds;
            double remaining = oldest?.ResidenceRemaining ?? recipe.ResidenceTicks;
            bool powered = IsPowered(stage);
            double progress = oldest == null ? 0 : Math.Clamp(1 - (remaining / recipe.ResidenceTicks), 0, 1);
            double temperature = powered
                ? m_defaults.AmbientTemperatureC +
                    ((m_nativeOptions.DryingTemperatureC - m_defaults.AmbientTemperatureC) * progress)
                : m_defaults.AmbientTemperatureC;
            double air = powered ? m_nativeOptions.DryingAirFlowM3Hr : 0;
            double dewpoint = powered ? m_nativeOptions.DryDewPointC : m_nativeOptions.AmbientDewPointC;
            FunctionStatusEnumeration function = powered ? FunctionStatusEnumeration.ON : FunctionStatusEnumeration.OFF;
            var values = new List<GranulateDryingValue>();
            GranulateDryingTarget bin = GranulateDryingTarget.Bin(1);
            GranulateDryingTarget bed = GranulateDryingTarget.Bed(1);
            AddDryingValue(dryer, values, bin, "Temperature/ActualValue", Variant.From(temperature), snapshot);
            AddDryingValue(dryer, values, bin, "DryAirTemperature/ActualValue", Variant.From(temperature), snapshot);
            AddDryingValue(dryer, values, bin, "AirFlow/ActualValue", Variant.From(air), snapshot);
            AddDryingValue(dryer, values, bin, "DewPoint", Variant.From(dewpoint), snapshot);
            AddDryingValue(dryer, values, bin, "AirSource", Variant.From(AirSourceEnumeration.RETURN_AIR), snapshot);
            AddDryingValue(dryer, values, bin, "DryingMode", Variant.From(DryingModeEnumeration.CONTINUOUS), snapshot);
            AddDryingValue(dryer, values, bin, "TemperatureControllerStatus", Variant.From(function), snapshot);
            AddDryingValue(dryer, values, bin, "MaterialDryingTime",
                Variant.From(recipe.ResidenceTicks * tickSeconds * 1000), snapshot);
            AddDryingValue(dryer, values, bin, "RemainingMaterialDryingTime",
                oldest == null ? Variant.Null : Variant.From(remaining * tickSeconds * 1000), snapshot,
                oldest == null ? StatusCodes.BadNoData : StatusCodes.Good);
            AddDryingValue(dryer, values, bin, "ExpiredDryingTime",
                oldest == null ? Variant.Null :
                    Variant.From((recipe.ResidenceTicks - remaining) * tickSeconds * 1000), snapshot,
                oldest == null ? StatusCodes.BadNoData : StatusCodes.Good);
            AddDryingValue(dryer, values, bin, "MaterialIdentification",
                oldest == null ? Variant.Null : Variant.From(oldest.Form.ToString()), snapshot,
                oldest == null ? StatusCodes.BadNoData : StatusCodes.Good);
            AddDryingValue(dryer, values, bin, "BatchNumber",
                oldest == null ? Variant.Null : Variant.From(snapshot.RunId + ":parcel-" +
                    oldest.Id.ToString(CultureInfo.InvariantCulture)), snapshot,
                oldest == null ? StatusCodes.BadNoData : StatusCodes.Good);
            AddDryingValue(dryer, values, bed, "TemperatureOfDesiccantBed/ActualValue",
                Variant.From(temperature), snapshot);
            AddDryingValue(dryer, values, bed, "AirFlow/ActualValue", Variant.From(air / 3.6), snapshot);
            AddDryingValue(dryer, values, bed, "DewPoint/ActualValue", Variant.From(dewpoint), snapshot);
            AddDryingValue(dryer, values, bed, "DesiccantBedControllerStatus", Variant.From(function), snapshot);
            AddDryingValue(dryer, values, GranulateDryingTarget.Device, "Operation/TotalEnergy/ActualPower",
                Variant.From(powered ? stage.ElectricalDemandWatts / 1000.0 : 0), snapshot);
            AddDryingValue(dryer, values, GranulateDryingTarget.Device, "Operation/TotalEnergy/PowerConsumption",
                Variant.From(stage.ElectricalJoules / 3_600_000.0), snapshot);
            await dryer.PublishAsync(new GranulateDryingCommit(NativeRevision(snapshot), snapshot.SourceTimestampUtc)
            {
                Values = values.ToArrayOf(),
                RemoteControlEnabled = false,
                Errors = [],
                MachineState = NativeItemState(stage),
                OperationMode = MachineryOperationModeValue.Processing
            }, cancellationToken).ConfigureAwait(false);
        }

        private void AddDryingValue(
            GranulateDryingDevice dryer,
            List<GranulateDryingValue> values,
            GranulateDryingTarget target,
            string path,
            Variant value,
            CommittedSnapshot snapshot,
            StatusCode status = default)
        {
            NodeState owner = target.Kind switch
            {
                GranulateDryingTargetKind.Bin => dryer.Bin(target.Number),
                GranulateDryingTargetKind.DesiccantBed => dryer.Bed(target.Number),
                _ => dryer.State
            };
            BaseVariableState variable = NativeChild(owner, path) as BaseVariableState ??
                throw new InvalidOperationException("The native drying member is not a variable: " + path);
            TrackNativeValue(variable);
            values.Add(new GranulateDryingValue(
                target, path, new DataValue(value, status, snapshot.SourceTimestampUtc)));
        }

        private NodeState NativeChild(NodeState owner, string path)
        {
            foreach (string name in path.Split('/'))
            {
                var children = new List<BaseInstanceState>();
                owner.GetChildren(RequireContext().Context, children);
                owner = children.Single(child => child.BrowseName.Name == name);
            }
            return owner;
        }

        private void TrackNativeValue(BaseVariableState variable)
        {
            m_nativeTimestampNodes.Add(variable.NodeId);
            Track(variable);
        }

        private void TrackNativeStateMachines(NodeState root)
        {
            TrackStateMachines(root);
            foreach (FiniteStateMachineState machine in Descendants(root).OfType<FiniteStateMachineState>())
            {
                foreach (NodeState value in Descendants(machine))
                {
                    if (value is BaseVariableState variable && m_projected.ContainsKey(variable.NodeId))
                    {
                        m_nativeTimestampNodes.Add(variable.NodeId);
                    }
                }
            }
        }

        private static ulong NativeRevision(CommittedSnapshot snapshot) => checked((ulong)snapshot.Revision + 1);

        private static MachineryItemStateValue NativeItemState(StageSnapshot stage)
        {
            return stage.State switch
            {
                EquipmentState.Unavailable => MachineryItemStateValue.NotAvailable,
                EquipmentState.Stopped => MachineryItemStateValue.OutOfService,
                EquipmentState.Executing => MachineryItemStateValue.Executing,
                _ => MachineryItemStateValue.NotExecuting
            };
        }

        /// <summary>
        /// Retains the typed runtime's publication delegate without replacing its implementation.
        /// </summary>
        private sealed record NativeExtrusionProjection(
            ExtrusionDeviceState Node,
            Func<string, BaseInstanceState> Find,
            Func<ExtrusionOperatingSnapshot, ArrayOf<ExtrusionValue>, CancellationToken, ValueTask> Publish);

        /// <summary>
        /// The sample's fixed disabled remote-control policy is known at startup; measurements are not.
        /// Only the coordinator adapter publishes subsequent absolute commits.
        /// </summary>
        private sealed class ReadOnlyDryingAuthority : IGranulateDryingProvider
        {
            /// <inheritdoc/>
            public ValueTask<GranulateDryingCommit> ReadAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<GranulateDryingCommit>(new GranulateDryingCommit(0, DateTime.UnixEpoch)
                {
                    RemoteControlEnabled = false,
                    Errors = []
                });
            }

            /// <inheritdoc/>
            public ValueTask<GranulateDryingResult> ExecuteAsync(
                GranulateDryingCommand command, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<GranulateDryingResult>(new GranulateDryingResult(StatusCodes.BadNotSupported));
            }
        }

        /// <summary>The existing owner's shared extrusion runtime.</summary>
        private ExtrusionRuntime? m_extrusionRuntime;
        /// <summary>The existing owner's shared GDD runtime.</summary>
        private GranulateDryingRuntime? m_dryingRuntime;
        /// <summary>Native binders own these committed source timestamps.</summary>
        private readonly HashSet<NodeId> m_nativeTimestampNodes = [];
    }
}
