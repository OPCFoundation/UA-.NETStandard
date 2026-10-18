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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;
using Model = Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class WmtpPeripheral
    {
        /// <summary>
        /// Gets only the released conformance units backed by this committed peripheral's actual bindings and nodes.
        /// Bad or stale observations do not remove an otherwise implemented equipment capability.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits => ComputeConformance().Units;

        /// <summary>
        /// Gets the exact OPC 40505 table 58 facets whose mandatory units this individual peripheral implements.
        /// No secure variant or separate MCP profile is inferred.
        /// </summary>
        public ArrayOf<string> ServerProfiles => ComputeConformance().Profiles;

        /// <summary>
        /// Gets whether this successfully committed runtime still owns its published root.
        /// </summary>
        internal bool IsConformanceActive
        {
            get
            {
                lock (m_lifetime)
                {
                    if (!m_conformanceCommitted || m_stopping)
                    {
                        return false;
                    }
                }
                return Present(State);
            }
        }

        /// <summary>
        /// Registers only after the full build and resource initialization have succeeded.
        /// During server startup the shared catalog is published later by StandardServer.
        /// </summary>
        internal async ValueTask CommitConformanceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lifetime)
            {
                if (m_stopping)
                {
                    throw new ObjectDisposedException(nameof(WmtpPeripheral));
                }
                m_conformanceCommitted = true;
            }
            WmtpConformanceRegistration.Register(m_context.Manager, this);
            await PublishConformanceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the existing catalog when it is already running, preserving all other contributors.
        /// </summary>
        private ValueTask PublishConformanceAsync(CancellationToken cancellationToken)
        {
            return m_context.Manager.Server is ServerInternalData
            {
                ConformanceUnitsManager: { } catalog,
                CurrentState: not ServerState.Shutdown
            } ? catalog.PublishAsync(cancellationToken) : default;
        }

        /// <summary>
        /// Computes one coherent per-instance capability set rather than combining incomplete instances into a facet.
        /// </summary>
        private (ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles) ComputeConformance()
        {
            if (!IsConformanceActive || m_configuration.Device == null ||
                !HasType(State, Model.ObjectTypes.WirelessMachineToolPeripheralType) ||
                !Child(State, State.DeviceInformation) || !Child(State, State.Measurements) ||
                !Child(State, State.MachineryBuildingBlocks) ||
                !Child(State.DeviceInformation!, Nodes.Identification) ||
                !Child(Nodes.Identification, Nodes.Identification.Manufacturer) ||
                !Child(Nodes.Identification, Nodes.Identification.SerialNumber) ||
                Nodes.Identification.Manufacturer!.Value.IsNull ||
                string.IsNullOrWhiteSpace(Nodes.Identification.SerialNumber!.Value) ||
                !BuildingBlock(Nodes.Identification))
            {
                return ([], []);
            }

            var units = new HashSet<string>(StringComparer.Ordinal) { "WMTP Basic" };
            var profiles = new List<string> { kProfilePrefix + "WMTPBasicServerFacet" };
            bool authorized = m_configuration.Authorization != null;
            FolderState? configuration = State.DeviceConfiguration;
            bool hasConfiguration = Child(State, configuration);
            AddUnit(units, "WMTP DeviceConfiguration Calibration Capabilities",
                Selected(WmtpFeatures.Calibration) && authorized && m_configuration.Calibration != null &&
                hasConfiguration && Child(configuration!, Nodes.CalibrationMode) &&
                Method(configuration!, Nodes.SwitchCalibrationMode, Nodes.SwitchCalibrationMode?.OnCallAsync != null,
                    1, 0));
            AddUnit(units, "WMTP DeviceConfiguration Time Capabilities",
                Selected(WmtpFeatures.DeviceTime) && authorized && m_configuration.DeviceTime != null &&
                hasConfiguration && Method(configuration!, Nodes.SetDeviceTime,
                    Nodes.SetDeviceTime?.OnCallAsync != null, 2, 0));
            AddUnit(units, "WMTP Firmware Services",
                Selected(WmtpFeatures.Firmware) && authorized && m_configuration.Transfers != null &&
                hasConfiguration && Method(configuration!, Nodes.UpdateFirmware,
                    Nodes.UpdateFirmware?.OnCallAsync != null, 1, 0));
            AddUnit(units, "WMTP License Services",
                Selected(WmtpFeatures.License) && authorized && m_configuration.Transfers != null &&
                hasConfiguration && Method(configuration!, Nodes.SendLicenseData,
                    Nodes.SendLicenseData?.OnCallAsync != null, 1, 0) &&
                Child(State.DeviceInformation!, Nodes.LicenseInformation) &&
                Child(State, State.LifetimeCounters) && BuildingBlock(State.LifetimeCounters!) &&
                Lifetime(State.LifetimeCounters!, Nodes.LicenseValidity));
            AddUnit(units, "WMTP BatteryLevel",
                Selected(WmtpFeatures.Battery) && m_battery != null &&
                Child(State.DeviceInformation!, Nodes.Battery) && ProcessValue(Nodes.Battery!));

            bool service = (Selected(WmtpFeatures.ServiceInformation) ||
                Selected(WmtpFeatures.AdvancedServiceInformation)) &&
                Child(State.DeviceInformation!, Nodes.NextServiceDate) &&
                Child(State.DeviceInformation!, Nodes.LastService) &&
                Child(State.DeviceInformation!, Nodes.LifeCycleStatus) &&
                Child(Nodes.LifeCycleStatus!, Nodes.LifeCycleStatus!.EnumValues) &&
                Child(Nodes.LifeCycleStatus, Nodes.LifeCycleStatus.ValueAsText);
            AddFacet(units, profiles, "WMTP Service and LifeCycle Information Basic",
                "WMTPServiceAndLifeCycleBasicFacet", service);
            AddFacet(units, profiles, "WMTP Service and LifeCycle Information Advanced",
                "WMTPServiceAndLifeCycleAdvancedFacet",
                service && Selected(WmtpFeatures.AdvancedServiceInformation) &&
                Child(State, State.LifetimeCounters) && BuildingBlock(State.LifetimeCounters!) &&
                Lifetime(State.LifetimeCounters!, Nodes.RemainingLifetime) &&
                Lifetime(State.LifetimeCounters!, Nodes.RemainingCycles) &&
                Lifetime(State.LifetimeCounters!, Nodes.NextService) &&
                Child(State, State.OperationCounters) && BuildingBlock(State.OperationCounters!) &&
                Child(State.OperationCounters!, State.OperationCounters!.OperationDuration) &&
                Child(State.OperationCounters, Nodes.OperationSinceService));

            if (authorized && m_configuration.Measurements != null)
            {
                foreach (WmtpMeasurement measurement in Measurements)
                {
                    AddMeasurementConformance(measurement, units, profiles);
                }
            }
            if (Selected(WmtpFeatures.WorkRecords) && authorized && m_configuration.WorkRecords != null &&
                WorkRecords != null && Child(State, State.WMTPWorkCycleData) &&
                HasType(State.WMTPWorkCycleData!, Model.ObjectTypes.WMTPWorkCycleDataType))
            {
                WMTPWorkCycleDataState work = State.WMTPWorkCycleData!;
                AddRecordsConformance(work, "WorkCycleData", m_configuration.WorkFeatures, units, profiles,
                    Method(work, work.AbortOperation, work.AbortOperation?.OnCallMethod2Async != null, 0, 0) &&
                    Method(work, work.ReportNumberOfStoredRecords,
                        work.ReportNumberOfStoredRecords?.OnCallAsync != null, 0, 1) &&
                    Method(work, work.CombinedReportAll, work.CombinedReportAll?.OnCallAsync != null, 0, 1),
                    work.DeleteAllStoredRecords, work.DeleteStoredRecordsIndex, work.DeleteStoredRecordsTime,
                    work.ReportNumberOfStoredRecordsTime,
                    Method(work, work.CombinedReportIndex, work.CombinedReportIndex?.OnCallAsync != null, 2, 1),
                    Method(work, work.CombinedReportTime, work.CombinedReportTime?.OnCallAsync != null, 2, 1),
                    Method(work, work.CombinedReportFirstValue,
                        work.CombinedReportFirstValue?.OnCallAsync != null, 0, 1),
                    Method(work, work.CombinedReportLastValue,
                        work.CombinedReportLastValue?.OnCallAsync != null, 0, 1));
            }
            if (Selected(WmtpFeatures.ServiceRecords) && authorized && m_configuration.ServiceRecords != null &&
                ServiceRecords != null && Child(State, State.WMTPServiceCycleData) &&
                HasType(State.WMTPServiceCycleData!, Model.ObjectTypes.WMTPServiceCycleDataType))
            {
                WMTPServiceCycleDataState data = State.WMTPServiceCycleData!;
                AddRecordsConformance(data, "ServiceCycleData", m_configuration.ServiceFeatures, units, profiles,
                    Method(data, data.AbortOperation, data.AbortOperation?.OnCallMethod2Async != null, 0, 0) &&
                    Method(data, data.ReportNumberOfStoredRecords,
                        data.ReportNumberOfStoredRecords?.OnCallAsync != null, 0, 1) &&
                    Method(data, data.CombinedReportAll, data.CombinedReportAll?.OnCallAsync != null, 0, 1),
                    data.DeleteAllStoredRecords, data.DeleteStoredRecordsIndex, data.DeleteStoredRecordsTime,
                    data.ReportNumberOfStoredRecordsTime,
                    Method(data, data.CombinedReportIndex, data.CombinedReportIndex?.OnCallAsync != null, 2, 1),
                    Method(data, data.CombinedReportTime, data.CombinedReportTime?.OnCallAsync != null, 2, 1),
                    Method(data, data.CombinedReportFirstValue,
                        data.CombinedReportFirstValue?.OnCallAsync != null, 0, 1),
                    Method(data, data.CombinedReportLastValue,
                        data.CombinedReportLastValue?.OnCallAsync != null, 0, 1));
            }

            ushort ns = (ushort)Context.NamespaceUris.GetIndex(Model.Namespaces.WMTP);
            var qualified = new List<QualifiedName>(units.Count);
            foreach (string unit in units)
            {
                qualified.Add(new QualifiedName(unit, ns));
            }
            qualified.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
            return (qualified.ToArrayOf(), profiles.ToArrayOf());
        }

        /// <summary>
        /// Adds only capabilities backed by a single valid configured sensor and its actual method bindings.
        /// </summary>
        private void AddMeasurementConformance(
            WmtpMeasurement measurement, HashSet<string> units, List<string> profiles)
        {
            WMTPMeasurementState state = measurement.State;
            WmtpMeasurementDefinition configured = measurement.Configuration;
            if (!Child(State.Measurements!, state) || !HasType(state, Model.ObjectTypes.WMTPMeasurementType) ||
                !ProcessValue(state) || !Child(state, state.TypeOfMeasurement) ||
                !Child(state.TypeOfMeasurement!, state.TypeOfMeasurement!.EnumValues) ||
                !Child(state.TypeOfMeasurement, state.TypeOfMeasurement.ValueAsText) ||
                state.TypeOfMeasurement.Value != (uint)configured.Kind ||
                !Child(state, state.TypeOfSample) ||
                !Child(state.TypeOfSample!, state.TypeOfSample!.EnumValues) ||
                !Child(state.TypeOfSample, state.TypeOfSample.ValueAsText) ||
                state.TypeOfSample.Value != (uint)configured.SampleKind ||
                !Child(state, state.TriggerSettings) ||
                !Child(state.TriggerSettings!, state.TriggerSettings!.EngineeringUnits) ||
                !Child(state, state.DeltaCondition) ||
                !Child(state.DeltaCondition!, state.DeltaCondition!.EngineeringUnits) ||
                !Method(state, state.SetTriggerSettings, state.SetTriggerSettings?.OnCallAsync != null, 1, 0) ||
                !Method(state, state.SetDeltaCondition, state.SetDeltaCondition?.OnCallAsync != null, 1, 0))
            {
                return;
            }
            (string unit, string profile) = configured.Kind switch
            {
                WmtpMeasurementKind.Temperature => ("WMTP Temperature Measurement", "WMTPTemperatureFacet"),
                WmtpMeasurementKind.RotationSpeed => ("WMTP RotationSpeed Measurement", "WMTPRotationSpeedFacet"),
                WmtpMeasurementKind.Humidity => ("WMTP Humidity Measurement", "WMTPHumidityFacet"),
                WmtpMeasurementKind.Acceleration => ("WMTP Acceleration Measurement", "WMTPAccelerationFacet"),
                WmtpMeasurementKind.Force => ("WMTP Force Measurement", "WMTPForceFacet"),
                WmtpMeasurementKind.Torque => ("WMTP Torque Measurement", "WMTPTorqueFacet"),
                WmtpMeasurementKind.Position => ("WMTP Position Measurement", "WMTPPositionFacet"),
                _ => ("WMTP Dimension Measurement", "WMTPDimensionFacet")
            };
            AddFacet(units, profiles, unit, profile, true);
            WmtpMeasurementFeatures features = configured.Features;
            bool counters = (features & WmtpMeasurementFeatures.LimitCounters) != 0 &&
                Child(state, state.LimitCounters) && BuildingBlock(state.LimitCounters!) &&
                Child(state, state.Status) &&
                Child(state.Status!, state.Status!.EnumValues) && Child(state.Status, state.Status.ValueAsText);
            if (counters)
            {
                ushort ns = (ushort)Context.NamespaceUris.GetIndex(Model.Namespaces.WMTP);
                BaseInstanceState? high = state.Status!.FindChild(Context, new QualifiedName("OperationAboveHigh", ns));
                BaseInstanceState? highHigh = state.Status.FindChild(
                    Context, new QualifiedName("OperationAboveHighHigh", ns));
                counters = Child(state.Status, high) && Child(state.Status, highHigh) &&
                    state.LimitCounters!.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasProperty, false, high!.NodeId) &&
                    state.LimitCounters.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasProperty, false, highHigh!.NodeId);
            }
            AddUnit(units, "WMTP Measurement LimitCounters", counters);
            AddUnit(units, "WMTP Measurement InternalUpdateInterval",
                (features & WmtpMeasurementFeatures.InternalUpdateInterval) != 0 &&
                Child(state, state.InternalUpdateInterval));
            AddUnit(units, "WMTP Measurement Information for WorkCycleData",
                (features & WmtpMeasurementFeatures.WorkCycleInformation) != 0 &&
                Child(state, state.Timestamp) && Child(state, state.Index) && Child(state, state.MeasurementPeriod));
        }

        /// <summary>
        /// Adds dataset capabilities without combining incomplete optional method groups across peripherals.
        /// </summary>
        private void AddRecordsConformance(
            BaseObjectState state, string dataset, WmtpRecordFeatures features,
            HashSet<string> units, List<string> profiles, bool mandatory,
            DeleteAllStoredRecordsMethodState? deleteAll,
            DeleteStoredRecordsIndexMethodState? deleteIndex,
            DeleteStoredRecordsTimeMethodState? deleteTime,
            ReportNumberOfStoredRecordsTimeMethodState? timeCount,
            bool indexReport, bool timeReport, bool firstReport, bool lastReport)
        {
            if (!mandatory)
            {
                return;
            }
            string prefix = "WMTP " + dataset;
            AddFacet(units, profiles, prefix, "WMTP" + dataset + "Facet", true);
            bool all = (features & WmtpRecordFeatures.DeleteAll) != 0 &&
                Method(state, deleteAll, deleteAll?.OnCallAsync != null, 0, 1);
            AddUnit(units, prefix + " Deleting All", all);
            AddUnit(units, prefix + " Deleting Advanced",
                all && (features & (WmtpRecordFeatures.DeleteIndex | WmtpRecordFeatures.DeleteTime)) ==
                    (WmtpRecordFeatures.DeleteIndex | WmtpRecordFeatures.DeleteTime) &&
                Method(state, deleteIndex, deleteIndex?.OnCallAsync != null, 2, 0) &&
                Method(state, deleteTime, deleteTime?.OnCallAsync != null, 2, 0));
            AddUnit(units, prefix + " ReportNumberOfStoredRecordsTime",
                (features & WmtpRecordFeatures.TimeCount) != 0 &&
                Method(state, timeCount, timeCount?.OnCallAsync != null, 2, 1));
            bool index = (features & WmtpRecordFeatures.ReportIndex) != 0 && indexReport;
            bool time = (features & WmtpRecordFeatures.ReportTime) != 0 && timeReport;
            AddUnit(units, prefix + " CombinedReport Index", index);
            AddUnit(units, prefix + " CombinedReport Time", time);
            AddUnit(units, prefix + " CombinedReport Advanced",
                index && time && (features & (WmtpRecordFeatures.ReportFirst | WmtpRecordFeatures.ReportLast)) ==
                    (WmtpRecordFeatures.ReportFirst | WmtpRecordFeatures.ReportLast) && firstReport && lastReport);
        }

        /// <summary>
        /// Checks that an advertised method is indexed, enabled, bound, and has its required generated arguments.
        /// </summary>
        private bool Method(NodeState parent, MethodState? method, bool bound, int inputs, int outputs)
        {
            return bound && Child(parent, method) && method!.Executable &&
                (inputs == 0 ||
                    (Child(method, method.InputArguments) && method.InputArguments!.Value.Count == inputs)) &&
                (outputs == 0 ||
                    (Child(method, method.OutputArguments) && method.OutputArguments!.Value.Count == outputs));
        }

        /// <summary>
        /// Checks the actual shared ProcessValues signal and required inherited metadata.
        /// </summary>
        private bool ProcessValue(ProcessValueState state)
        {
            return Child(state, state.SignalTag) && state.AnalogSignal is ProcessValueVariableState signal &&
                Child(state, signal) && Child(signal, signal.EngineeringUnits) && Child(signal, signal.EURange);
        }

        /// <summary>
        /// Checks the complete standard lifetime variable, not only an allocated optional slot.
        /// </summary>
        private bool Lifetime(NodeState parent, LifetimeVariableState? lifetime)
        {
            return Child(parent, lifetime) && Child(lifetime!, lifetime!.EngineeringUnits) &&
                Child(lifetime, lifetime.StartValue) && Child(lifetime, lifetime.LimitValue);
        }

        /// <summary>
        /// Checks the mandatory Machinery discovery reference for an implemented building block.
        /// </summary>
        private bool BuildingBlock(BaseObjectState block)
        {
            return State.MachineryBuildingBlocks!.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasAddIn, false, block.NodeId);
        }

        /// <summary>
        /// Checks exact publication identity without recursively reading the owner's conformance contribution.
        /// </summary>
        private bool Present(NodeState? node)
        {
            return node != null && !node.NodeId.IsNull &&
                ReferenceEquals(m_context.Manager.FindPredefinedNode(node.NodeId), node);
        }

        /// <summary>
        /// Rejects detached, missing, replaced or unpublished children.
        /// </summary>
        private bool Child(NodeState parent, NodeState? child)
        {
            return Present(child) && ReferenceEquals(parent.FindChild(Context, child!.BrowseName), child);
        }

        /// <summary>
        /// Retains the real standard type relationship, including permitted subtypes.
        /// </summary>
        private bool HasType(BaseObjectState state, uint type)
        {
            return Context.TypeTable.IsTypeOf(state.TypeDefinitionId,
                Opc.Ua.NodeId.Create(type, Model.Namespaces.WMTP, Context.NamespaceUris));
        }

        /// <summary>
        /// Tests the immutable instance feature selection.
        /// </summary>
        private bool Selected(WmtpFeatures feature)
        {
            return (Features & feature) != 0;
        }

        /// <summary>
        /// Adds one implemented unit, deduplicating repeated sensors.
        /// </summary>
        private static void AddUnit(HashSet<string> units, string name, bool available)
        {
            if (available)
            {
                units.Add(name);
            }
        }

        /// <summary>
        /// Adds a facet only when this instance supplies its own mandatory additional unit and WMTP Basic.
        /// </summary>
        private static void AddFacet(
            HashSet<string> units, List<string> profiles, string unit, string profile, bool available)
        {
            if (available && units.Add(unit))
            {
                profiles.Add(kProfilePrefix + profile);
            }
        }

        /// <summary>The exact OPC 40505 table 58 namespace; no secure variant is specified.</summary>
        private const string kProfilePrefix = "http://opcfoundation.org/UA-Profile/WMTP/Server/";

        /// <summary>Whether all build, binding and resource-initialization phases completed successfully.</summary>
        private bool m_conformanceCommitted;
    }
}
