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
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Gets bounded application-side history of accepted calibration observations, not a new standard service.
        /// </summary>
        internal ArrayOf<GmsCalibrationObservation> CalibrationHistory => m_calibrationHistory;

        /// <summary>
        /// Creates only explicitly selected native monitoring and identification descendants before publication.
        /// </summary>
        private void CreateSystemStructure(GMSState state, GmsSystemSnapshot snapshot)
        {
            if (snapshot.Identification is { } identification)
            {
                SelectFields(state.Identification!, IdentificationFields(identification));
            }
            if (snapshot.Loading is { } loading)
            {
                state.Monitoring!.AddLoadingMonitoring(m_context.Context);
                LoadingMonitoringState node = state.Monitoring.LoadingMonitoring!;
                SelectFields(node, LoadingFields(loading));
                SelectName(node);
            }
            if (snapshot.ToolMonitoring is { } tools)
            {
                state.Monitoring!.AddToolMonitoring(m_context.Context);
                ToolMonitoringState node = state.Monitoring.ToolMonitoring!;
                SelectFields(node, [(BrowseNames.ActiveTool, ActiveToolShape(tools))]);
                SelectName(node);
            }
        }

        /// <summary>
        /// Requires a fully initialized generated child; a bare CreateChild would lose its mandatory descendants.
        /// </summary>
        /// <typeparam name="T">The required native generated node type.</typeparam>
        private T Child<T>(NodeState parent, QualifiedName name) where T : BaseInstanceState
        {
            return parent.FindChild(m_context.Context, name) as T ??
                throw Invalid($"The native generated model does not declare the expected child '{name}'.");
        }

        /// <summary>
        /// Selects the inherited ElementMonitoring Name from its original MachineTool declaration.
        /// </summary>
        private void SelectName(NodeState node)
        {
            BaseVariableState name = Child<BaseVariableState>(node, MachineName(MachineTool.BrowseNames.Name));
            name.AccessLevel = AccessLevels.CurrentRead;
            name.UserAccessLevel = AccessLevels.CurrentRead;
        }

        /// <summary>
        /// Plans derived assignments and cross-references against the actual prospective tool inventory.
        /// </summary>
        private ArrayOf<GmsCalibrationObservation> PlanSystems(
            GMSState state, GmsSystemSnapshot snapshot, Dictionary<string, BaseToolState> sensors, List<Action> writes)
        {
            if (snapshot.Identification is { } identification)
            {
                PlanFields(state.Identification!, IdentificationFields(identification), writes);
            }
            if (snapshot.Loading is { } loading)
            {
                LoadingMonitoringState node = state.Monitoring!.LoadingMonitoring!;
                PlanFields(node, LoadingFields(loading), writes);
                PlanName(node, loading.Name, loading.LoadStatus, writes);
            }
            if (snapshot.ToolMonitoring is { } monitoring)
            {
                ToolMonitoringState node = state.Monitoring!.ToolMonitoring!;
                DataValue active = ResolveActiveTools(monitoring, sensors);
                PlanFields(node, [(BrowseNames.ActiveTool, active)], writes);
                PlanName(node, monitoring.Name, new DataValue(Variant.From(monitoring.Name),
                    monitoring.Sample.StatusCode, monitoring.Sample.SourceTimestamp), writes);
            }
            ArrayOf<GmsCalibrationObservation> history = m_calibrationHistory;
            if (snapshot.Calibration is { } calibration)
            {
                if (m_calibration == null)
                {
                    throw Invalid("The shared prognosis factory did not supply the native Calibration descendant.");
                }
                PlanFields(m_calibration, CalibrationFields(calibration), writes);
                if (Observation?.Calibration != calibration)
                {
                    var updated = new List<GmsCalibrationObservation>(history.ToArray()!);
                    if (updated.Count == m_options.MaximumCalibrationHistory)
                    {
                        updated.RemoveAt(0);
                    }
                    updated.Add(calibration);
                    history = updated.ToArrayOf();
                }
            }
            return history;
        }

        /// <summary>
        /// Plans a read-only inherited name without replacing its acquisition metadata.
        /// </summary>
        private void PlanName(NodeState parent, string name, in DataValue metadata, List<Action> writes)
        {
            var node = (BaseVariableState)parent.FindChild(
                m_context.Context, MachineName(MachineTool.BrowseNames.Name))!;
            var value = new DataValue(Variant.From(name), metadata.StatusCode, metadata.SourceTimestamp);
            writes.Add(() => Set(node, value));
        }

        /// <summary>
        /// Resolves keys or validates actual NodeIds; references cannot escape the selected GMS instance.
        /// </summary>
        private DataValue ResolveActiveTools(
            GmsToolMonitoringObservation value, Dictionary<string, BaseToolState> sensors)
        {
            var seen = new HashSet<NodeId>();
            if (!value.ActiveSensorKeys.IsNull)
            {
                var identifiers = new NodeId[value.ActiveSensorKeys.Count];
                for (int i = 0; i < identifiers.Length; i++)
                {
                    if (!sensors.TryGetValue(value.ActiveSensorKeys[i], out BaseToolState? sensor) ||
                        !seen.Add(sensor.NodeId))
                    {
                        throw Invalid("Every active sensor key must identify one distinct tool in this GMS.");
                    }
                    identifiers[i] = sensor.NodeId;
                }
                return new DataValue(Variant.From(identifiers.ToArrayOf()),
                    value.Sample.StatusCode, value.Sample.SourceTimestamp);
            }
            if (value.ActiveToolIds.WrappedValue.TryGetValue(out ArrayOf<NodeId> nodes))
            {
                var known = new HashSet<NodeId>();
                foreach (BaseToolState sensor in sensors.Values)
                {
                    known.Add(sensor.NodeId);
                }
                foreach (NodeId node in nodes)
                {
                    if (node.IsNull || !known.Contains(node) || !seen.Add(node))
                    {
                        throw Invalid("ActiveTool NodeIds must reference distinct actual sensors of this GMS.");
                    }
                }
            }
            return value.ActiveToolIds;
        }

        /// <summary>
        /// Appends the provider's native calibration to explicitly supplied generic forecasts.
        /// </summary>
        private static MachineToolPrognosisObservation? ConvertPrognoses(GmsSystemSnapshot snapshot)
        {
            if (snapshot.Calibration is not { } calibration)
            {
                return snapshot.MachineTool.Prognoses;
            }
            var entries = new List<MachineToolPrognosis>();
            if (snapshot.MachineTool.Prognoses is { } generic)
            {
                foreach (MachineToolPrognosis entry in generic.Entries)
                {
                    if (entry.Key == BrowseNames.Calibration)
                    {
                        throw Invalid("The GMS Calibration forecast is supplied exactly once.");
                    }
                    entries.Add(entry);
                }
            }
            entries.Add(new MachineToolPrognosis
            {
                Key = BrowseNames.Calibration,
                NamespaceUri = GmsModel.NamespaceUri,
                Kind = MachineToolPrognosisKind.Companion,
                CompanionTypeId = ObjectTypeIds.CalibrationPrognosisType,
                PredictedTime = calibration.PredictedTime,
                Sample = new MachineToolSampleInfo(
                    calibration.Calibrated.SourceTimestamp, calibration.Calibrated.StatusCode)
            });
            return new MachineToolPrognosisObservation { Entries = entries.ToArrayOf() };
        }

        /// <summary>
        /// Selects the existing generic prognosis module with a native CalibrationPrognosisType factory.
        /// </summary>
        internal MachineToolPrognosisOptions PrognosisOptions(MachineToolPrognosisOptions options)
        {
            if (!Has(GmsFeatures.Calibration))
            {
                return options;
            }
            var kinds = new List<MachineToolPrognosisKind>();
            bool companion = false;
            foreach (MachineToolPrognosisKind kind in options.SupportedKinds)
            {
                kinds.Add(kind);
                companion |= kind == MachineToolPrognosisKind.Companion;
            }
            if (!companion)
            {
                kinds.Add(MachineToolPrognosisKind.Companion);
            }
            return options with
            {
                Features = options.Features | MachineToolFeatures.Prognoses,
                SupportedKinds = kinds.ToArrayOf(),
                CreatePrognosisAsync = (context, parent, name, value, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (value.Kind != MachineToolPrognosisKind.Companion ||
                        value.CompanionTypeId != ObjectTypeIds.CalibrationPrognosisType)
                    {
                        return options.CreatePrognosisAsync != null
                            ? options.CreatePrognosisAsync(context, parent, name, value, cancellationToken)
                            : new ValueTask<MachineToolEquipmentNode<PrognosisState>>(
                                new MachineToolEquipmentNode<PrognosisState>(
                                    CreateGenericPrognosis(context, parent, name, value.Kind)));
                    }
                    CalibrationPrognosisState state = context.CreateInstanceOfCalibrationPrognosisType(parent, name);
                    SelectFields(state, CalibrationFields(m_input!.Calibration!));
                    m_calibration = state;
                    return new ValueTask<MachineToolEquipmentNode<PrognosisState>>(
                        new MachineToolEquipmentNode<PrognosisState>(state));
                }
            };
        }

        /// <summary>
        /// Selects the standard generated factory; all generic prognosis semantics remain in the shared controller.
        /// </summary>
        private static PrognosisState CreateGenericPrognosis(
            ISystemContext context, NodeState parent, QualifiedName name, MachineToolPrognosisKind kind) => kind switch
            {
                MachineToolPrognosisKind.Maintenance => context.CreateInstanceOfMaintenancePrognosisType(parent, name),
                MachineToolPrognosisKind.ManualActivity =>
                    context.CreateInstanceOfManualActivityPrognosisType(parent, name),
                MachineToolPrognosisKind.PartLoad => context.CreateInstanceOfPartLoadPrognosisType(parent, name),
                MachineToolPrognosisKind.PartUnload => context.CreateInstanceOfPartUnloadPrognosisType(parent, name),
                MachineToolPrognosisKind.ProcessChangeover =>
                    context.CreateInstanceOfProcessChangeoverPrognosisType(parent, name),
                MachineToolPrognosisKind.ProductionJobEnd =>
                    context.CreateInstanceOfProductionJobEndPrognosisType(parent, name),
                MachineToolPrognosisKind.ToolChange => context.CreateInstanceOfToolChangePrognosisType(parent, name),
                MachineToolPrognosisKind.ToolLoad => context.CreateInstanceOfToolLoadPrognosisType(parent, name),
                MachineToolPrognosisKind.ToolUnload => context.CreateInstanceOfToolUnloadPrognosisType(parent, name),
                MachineToolPrognosisKind.UtilityChange =>
                    context.CreateInstanceOfUtilityChangePrognosisType(parent, name),
                _ => throw Invalid("A nonstandard prognosis requires its explicitly configured actual type factory.")
            };

        /// <summary>
        /// Gets native field names without translating namespaces or wire values.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> IdentificationFields(
            GmsIdentificationObservation value) =>
            [(BrowseNames.SubDeviceClass, value.SubDeviceClass), (BrowseNames.Workspace, value.Workspace)];

        /// <summary>
        /// Gets native loading fields with explicit optional presence.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> LoadingFields(GmsLoadingObservation value) =>
            [(BrowseNames.LoadStatus, value.LoadStatus), (BrowseNames.IsInLoadingPosition, value.IsInLoadingPosition)];

        /// <summary>
        /// Gets native calibration fields; PredictedTime remains owned by the shared prognosis writer.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> CalibrationFields(GmsCalibrationObservation value) =>
        [
            (BrowseNames.Calibrated, value.Calibrated), (BrowseNames.DateOfCalibration, value.DateOfCalibration),
            (BrowseNames.CalibrationInterval, value.CalibrationInterval),
            (BrowseNames.CalibrationPreptime, value.CalibrationPreptime),
            (BrowseNames.CalibrationCertificate, value.CalibrationCertificate)
        ];

        /// <summary>
        /// Selects ActiveTool presence before the shared inventory has allocated actual sensor identifiers.
        /// </summary>
        private static DataValue ActiveToolShape(GmsToolMonitoringObservation value) =>
            value.ActiveSensorKeys.IsNull ? value.ActiveToolIds :
                new DataValue(Variant.From((ArrayOf<NodeId>)[]), value.Sample.StatusCode, value.Sample.SourceTimestamp);

        /// <summary>
        /// Resolves inherited MachineTool-qualified names using the current namespace table.
        /// </summary>
        private QualifiedName MachineName(string name) =>
            new(name, (ushort)m_context.Context.NamespaceUris.GetIndex(MachineTool.Namespaces.MachineTool));

        /// <summary>The actual native calibration forecast owned by the shared prognosis module.</summary>
        private CalibrationPrognosisState? m_calibration;

        /// <summary>The immutable bounded sequence of accepted native calibration observations.</summary>
        private ArrayOf<GmsCalibrationObservation> m_calibrationHistory = [];
    }
}
