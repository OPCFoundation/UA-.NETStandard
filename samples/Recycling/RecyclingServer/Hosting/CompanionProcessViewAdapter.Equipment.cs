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
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.PackML;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;
using Automatic = Opc.Ua.Vision.MachineVision.VisionAutomaticModeStateMachineTypeIds;
using Control = Opc.Ua.Vision.MachineVision.VisionStateMachineTypeIds;

namespace Opc.Ua.Recycling
{
    public sealed partial class CompanionProcessViewAdapter
    {
        /// <summary>
        /// Composes actual vision systems and cameras after the mechanical station assets were created.
        /// Called by ConfigureVisionFor on the very same Machinery owner.
        /// </summary>
        public async ValueTask InitializeVisionAsync(IVisionBuildContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            IMachineryBuildContext machinery = RequireContext();
            if (!ReferenceEquals(context.Manager, machinery.Manager))
            {
                throw new InvalidOperationException("Vision must share the process view's Machinery owner.");
            }
            foreach (StageBinding binding in m_stages.Values.Where(static stage => stage.Index is 4 or 5))
            {
                string name = binding.Machine.State.BrowseName.Name!;
                MachineVisionSystemHandle system = await context.AddMachineVisionSystemAsync(
                    name + "-vision-control", new ProjectionVisionProvider(),
                    new MachineVisionSystemOptions
                    {
                        Features = MachineVisionFeatures.Results,
                        InitiallySimulated = true,
                        InitiallyReady = true
                    },
                    new InMemoryMachineVisionStore(maximumDocuments: 1),
                    m_clock,
                    cancellationToken).ConfigureAwait(false);
                await machinery.AddMachine(system.State)
                    .WithIdentification(identity => Identify(identity, name + "-vision"))
                    .BuildAsync(cancellationToken).ConfigureAwait(false);
                var identity = new MachineryIdentificationData();
                Identify(identity, name + "-vision");
                MachineVisionAssetBuilder asset = context.AddMachineVisionAsset(name + "-vision-asset", m_clock)
                    .WithIdentification(identity)
                    .WithComponents()
                    .RepresentsSameSystem(system.State);
                var cameraIdentity = new MachineryIdentificationData();
                Identify(cameraIdentity, name + "-camera");
                MachineVisionNodeBuilder camera = asset.AddItem(MachineVisionItemKind.ImageSensor, name + "-camera")
                    .WithIdentification(cameraIdentity)
                    .WithHealth(health => health.Set("Temperature", Variant.From(m_defaults.AmbientTemperatureC)));
                camera.Child("Health").Child("Temperature").Child("EngineeringUnits")
                    .Value(Variant.FromStructure(ScaleUnits.Create("CEL", "degC", "degree Celsius")));
                MachineVisionAssetHandle built = await asset.BuildAsync(cancellationToken).ConfigureAwait(false);
                await RegisterAssetAsync(built.State, cancellationToken).ConfigureAwait(false);
                await RegisterAssetAsync((BaseObjectState)camera.State, cancellationToken).ConfigureAwait(false);
                Link(binding.Machine.State.Equipment!, system.State);
                Link(binding.Machine.State.Equipment!, built.State);
                binding.Vision = system;
                binding.CameraTemperature = (BaseVariableState)camera.Child("Health").Child("Temperature").State;
                Track(binding.CameraTemperature);
                TrackStateMachines(system.State);
                Protect(system.State);
                Protect(built.State);
            }
        }

        private async ValueTask BuildInstrumentsAsync(StageBinding binding, CancellationToken cancellationToken)
        {
            IMachineryBuildContext context = RequireContext();
            string name = binding.Machine.State.BrowseName.Name!;
            if (binding.Index is 1 or 2 or 20)
            {
                ScaleKind kind = binding.Index switch
                {
                    1 => ScaleKind.Vehicle,
                    2 => ScaleKind.Simple,
                    _ => ScaleKind.AutomaticFilling
                };
                m_scalesRuntime ??= context.Manager.CreateScalesRuntime();
                ScaleHandle scale = await m_scalesRuntime.CreateScaleAsync(
                    new QualifiedName(name + "-weighing", context.InstanceNamespaceIndex), kind,
                    builder =>
                    {
                        builder.WithIdentification(new ScaleIdentification
                        {
                            Manufacturer = new LocalizedText("OPC Foundation sample"),
                            SerialNumber = m_options.LineId + "-" + name + "-weighing",
                            ProductInstanceUri = AssetUri(name + "-weighing"),
                            Model = new LocalizedText("Committed synthetic " + kind + " instrument")
                        })
                            .WithWeighingRange(new WeighingRangeDefinition(
                                0, m_options.Simulation.Stages[binding.Index].CapacityMilligrams / 1_000_000.0,
                                0.000001, 0.000001))
                            .WithWeightDetails()
                            .WithMachineryBuildingBlocks();
                        if (kind == ScaleKind.AutomaticFilling)
                        {
                            builder.WithPackMLState().WithProductionOutput()
                                .With<AutomaticFillingScaleState>((ctx, state) =>
                                {
                                    state.ProductionOutput!.LastItem!.AddItemId(ctx);
                                    state.ProductionOutput.LastItem.MeasuredWeight!.EngineeringUnits!.Value =
                                        ScaleUnits.Kilogram;
                                });
                        }
                    },
                    binding.Machine.State.Equipment,
                    cancellationToken).ConfigureAwait(false);
                scale.CommandInterceptor = static _ => RefuseLocalCommand();
                binding.Scale = scale;
                await RegisterAssetAsync(scale.Scale, cancellationToken).ConfigureAwait(false);
                Link(context.DeviceSet, scale.Scale);
                Link(context.MachinesFolder, scale.Scale);
                TrackWeight(scale.Scale.CurrentWeight!);
                TrackStateMachines(scale.Scale);
                if (scale.Scale.ProductionOutput != null)
                {
                    foreach (BaseVariableState value in Descendants(scale.Scale.ProductionOutput)
                        .OfType<BaseVariableState>())
                    {
                        if (value.BrowseName.Name is not (
                            "EngineeringUnits" or "EURange" or "InstrumentRange" or "Weighed"))
                        {
                            Track(value);
                        }
                    }
                }
                Protect(scale.Scale);
            }
            if (binding.Index is >= 8 and <= 12)
            {
                await BuildPumpAsync(binding, name + "-circulation-pump", cancellationToken).ConfigureAwait(false);
                if (binding.Index != 12)
                {
                    await BuildPumpAsync(binding, name + "-drain-pump", cancellationToken).ConfigureAwait(false);
                }
            }
            await BuildNativeProcessAsync(binding, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask BuildPumpAsync(
            StageBinding binding, string name, CancellationToken cancellationToken)
        {
            IMachineryBuildContext context = RequireContext();
            m_pumpsRuntime ??= context.Manager.CreatePumpsRuntime();
            IPumpBuilder pump = await m_pumpsRuntime.CreatePumpAsync(
                new QualifiedName(name, context.InstanceNamespaceIndex), binding.Machine.State.Equipment,
                cancellationToken).ConfigureAwait(false);
            pump.WithNameplate(new PumpNameplate
            {
                NodeId = NodeId.Null,
                Manufacturer = new LocalizedText("OPC Foundation sample"),
                SerialNumber = m_options.LineId + "-" + name,
                ProductInstanceUri = AssetUri(name),
                Model = new LocalizedText("Committed synthetic water pump")
            });
            pump.PumpActuation.SetDiscrete(Pumps.BrowseNames.Status, false);
            pump.Measurements.SetAnalog(Pumps.BrowseNames.FluidTemperature, m_defaults.AmbientTemperatureC,
                ScaleUnits.Create("CEL", "degC", "degree Celsius"), new Range(-50, 100));
            var running = (BaseVariableState)pump.PumpActuation.Child(Pumps.BrowseNames.Status)!;
            var temperature = (BaseVariableState)pump.Measurements.Child(Pumps.BrowseNames.FluidTemperature)!;
            running.Description = new LocalizedText("Synthetic actual pump status from committed electrical demand.");
            temperature.Description = new LocalizedText(
                "Synthetic fluid temperature from committed wash thermal state.");
            binding.Pumps.Add((running, temperature));
            Track(running);
            Track(temperature);
            await RegisterAssetAsync(pump.Pump, cancellationToken).ConfigureAwait(false);
            Link(context.DeviceSet, pump.Pump);
            Link(context.MachinesFolder, pump.Pump);
            Protect(pump.Pump);
        }

        private async ValueTask ProjectInstrumentsAsync(
            StageBinding binding,
            StageSnapshot stage,
            CommittedSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            bool powered = stage.ElectricalDemandWatts > 0 && stage.State is not (
                EquipmentState.ElectricalConstraint or EquipmentState.Unavailable or EquipmentState.Stopped);
            foreach ((BaseVariableState running, BaseVariableState temperature) in binding.Pumps)
            {
                Set(running, Variant.From(powered));
                Number(temperature, stage.Index == 10
                    ? ThermalTemperature(stage, m_defaults.BathTemperatureC) : m_defaults.AmbientTemperatureC);
            }
            if (binding.Scale is { } scale)
            {
                MaterialPackage? package = binding.Index == 20
                    ? snapshot.Packages.Span.ToArray().LastOrDefault() : null;
                double mass = binding.Index == 20
                    ? (package?.Material.Mass.Total ?? 0) / 1_000_000.0
                    : stage.Inventory.Total / 1_000_000.0;
                scale.PublishLoad(mass);
                if (binding.Index == 20 && package == null)
                {
                    Set(scale.Scale.CurrentWeight, Variant.Null, StatusCodes.BadNoData);
                }
                scale.SetItemState(stage.State switch
                {
                    EquipmentState.Executing => ScaleItemState.Executing,
                    EquipmentState.Unavailable => ScaleItemState.NotAvailable,
                    EquipmentState.Stopped => ScaleItemState.OutOfService,
                    _ => ScaleItemState.NotExecuting
                });
                if (scale.PackML is { } pack)
                {
                    uint desired = stage.State == EquipmentState.Executing ? PackMLStateNumbers.Execute
                        : stage.Enabled ? PackMLStateNumbers.Idle : PackMLStateNumbers.Stopped;
                    if (pack.CurrentState != desired)
                    {
                        pack.Initialize(desired == PackMLStateNumbers.Stopped
                            ? PackMLInitialState.Stopped : PackMLInitialState.Idle);
                        if (desired == PackMLStateNumbers.Execute)
                        {
                            ServiceResult result = pack.Execute(PackMLCommand.Start);
                            if (ServiceResult.IsBad(result))
                            {
                                throw new ServiceResultException(result);
                            }
                        }
                    }
                }
                if (scale.Scale.ProductionOutput is { } statistics)
                {
                    scale.Update(() => ProjectStatistics(statistics, snapshot, package));
                }
                Protect(scale.Scale);
            }
            if (binding.Vision is { } vision)
            {
                Number(binding.CameraTemperature, m_defaults.AmbientTemperatureC + (powered ? 5 : 0));
                uint control = stage.Enabled ? Control.StateIds.Operational : Control.StateIds.Halted;
                uint automatic = stage.State == EquipmentState.Executing
                    ? Automatic.StateIds.ContinuousExecution : Automatic.StateIds.Ready;
                if (vision.ControlState.GetStateId(vision.ControlState.CurrentState!.Id!.Value) != control)
                {
                    vision.ControlState.SetState(RequireContext().Context, control);
                }
                if (vision.AutomaticState.GetStateId(vision.AutomaticState.CurrentState!.Id!.Value) != automatic)
                {
                    vision.AutomaticState.SetState(RequireContext().Context, stage.Enabled ? automatic : 0);
                }
                MaterialExecution? execution = snapshot.History.Span.ToArray()
                    .LastOrDefault(item => item.Stage == stage.Index);
                string key = execution == null ? string.Empty :
                    snapshot.RunId + ":" + execution.Id.ToString(CultureInfo.InvariantCulture);
                if (key.Length == 0 && binding.LastVisionResult.Length != 0)
                {
                    await vision.Store.DeleteAsync(MachineVisionDocumentKind.Result, "latest", cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (execution != null && key != binding.LastVisionResult)
                {
                    await vision.PublishResultAsync(new ResultDataType
                    {
                        ResultId = new ResultIdDataType { Id = "latest" },
                        JobId = new JobIdDataType { Id = key },
                        ResultState = 1,
                        IsSimulated = true,
                        CreationTime = snapshot.SourceTimestampUtc,
                        ResultContent = [Variant.FromStructure(RecyclingModelMapping.ToWire([execution])[0])],
                        EncodingMask = (uint)(ResultDataTypeFields.ResultContent | ResultDataTypeFields.IsSimulated)
                    }, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                binding.LastVisionResult = key;
                Protect(vision.State);
            }
        }

        private void TrackWeight(WeightItemState weight)
        {
            foreach (BaseVariableState variable in Descendants(weight).OfType<BaseVariableState>())
            {
                if (variable.BrowseName.Name is not ("EngineeringUnits" or "EURange" or "InstrumentRange"))
                {
                    Track(variable);
                }
            }
        }

        private static void ProjectStatistics(
            StatisticState statistics, CommittedSnapshot snapshot, MaterialPackage? package)
        {
            double[] weights = snapshot.Packages.Span.ToArray()
                .Select(static item => item.Material.Mass.Total / 1_000_000.0)
                .ToArray();
            double sum = weights.Sum();
            double mean = weights.Length > 0 ? sum / weights.Length : 0;
            double variance = weights.Length > 0
                ? weights.Sum(weight => (weight - mean) * (weight - mean)) / weights.Length : 0;
            foreach (StatisticCounterState? counter in new[]
            {
                statistics.TotalPackages,
                statistics.TotalPackagesWeighed
            })
            {
                if (counter == null)
                {
                    continue;
                }
                Set(counter.ItemCount, Variant.From((ulong)weights.Length));
                Number(counter.SumWeight, sum);
                Number(counter.MeanValue, mean, weights.Length > 0);
                Number(counter.MinValue, weights.Length > 0 ? weights.Min() : 0, weights.Length > 0);
                Number(counter.MaxValue, weights.Length > 0 ? weights.Max() : 0, weights.Length > 0);
                Number(counter.StandardDeviation, Math.Sqrt(variance), weights.Length > 0);
            }
            if (statistics.StartTime != null)
            {
                Set(statistics.StartTime, Variant.Null, StatusCodes.BadNotSupported);
            }
            if (statistics.LastItem is { } last)
            {
                double mass = (package?.Material.Mass.Total ?? 0) / 1_000_000.0;
                Set(last.MeasuredWeight, package == null ? Variant.Null :
                    Variant.FromStructure(new WeightType { Gross = mass, Net = mass, Tare = 0 }),
                    package == null ? StatusCodes.BadNoData : StatusCodes.Good);
                Set(last.ItemId, package == null ? Variant.Null :
                    Variant.From(snapshot.RunId + ":parcel-" + package.Id.ToString(CultureInfo.InvariantCulture)),
                    package == null ? StatusCodes.BadNoData : StatusCodes.Good);
            }
        }

        /// <summary>
        /// No native method can become a second authority for the sorting process.
        /// Read-result methods are provided by the package runtime, not this provider.
        /// </summary>
        private sealed class ProjectionVisionProvider : IMachineVisionOperationsProvider
        {
            /// <inheritdoc/>
            public ValueTask<MachineVisionOperationResult> ControlAsync(
                MachineVisionControlAction action,
                int cause,
                string causeDescription,
                CancellationToken cancellationToken)
            {
                return Refused(cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<MachineVisionOperationResult> PrepareRecipeAsync(
                MachineVisionDocument recipe, bool prepare, CancellationToken cancellationToken)
            {
                return Refused(cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<MachineVisionOperationResult> ActivateConfigurationAsync(
                MachineVisionDocument configuration, CancellationToken cancellationToken)
            {
                return Refused(cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<MachineVisionOperationResult> ValidateDocumentAsync(
                MachineVisionDocument document, CancellationToken cancellationToken)
            {
                return Refused(cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<MachineVisionOperationResult> StartJobAsync(
                MachineVisionJob job, CancellationToken cancellationToken)
            {
                return Refused(cancellationToken);
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<MachineVisionAcquisition> ExecuteAsync(
                MachineVisionJob job, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                yield break;
            }

            /// <inheritdoc/>
            public ValueTask<MachineVisionStepResult> ExecuteStepAsync(
                MachineVisionStepRequest request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<MachineVisionStepResult>(
                    new MachineVisionStepResult(new MachineVisionOperationResult(RefuseLocalCommand()), false));
            }

            private static ValueTask<MachineVisionOperationResult> Refused(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<MachineVisionOperationResult>(
                    new MachineVisionOperationResult(RefuseLocalCommand()));
            }
        }

        /// <summary>The owner's shared Scales runtime; disposal is idempotent with owner cleanup.</summary>
        private ScalesRuntime? m_scalesRuntime;
        /// <summary>The owner's shared Pumps runtime; disposal is idempotent with owner cleanup.</summary>
        private PumpsRuntime? m_pumpsRuntime;
    }
}
