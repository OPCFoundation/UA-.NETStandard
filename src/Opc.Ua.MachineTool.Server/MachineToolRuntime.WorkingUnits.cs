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
using Opc.Ua.IA;
using Opc.Ua.Machinery;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolRuntime<TState>
    {
        /// <summary>
        /// Validates optional monitoring observations and fixed inventories before publication.
        /// </summary>
        private void ValidateExtendedMonitoring(MachineToolSnapshot snapshot, bool initializing)
        {
            ValidateStacklights(snapshot, initializing);
            MachineToolOperation operation = snapshot.Operation;
            if (Has(MachineToolFeatures.LegacyPowerOnDuration | MachineToolFeatures.OperationCounters))
            {
                if (operation.PowerOnDurationMilliseconds is not double duration || !IsFinite(duration) || duration < 0 ||
                    (Has(MachineToolFeatures.LegacyPowerOnDuration) && duration / 3_600_000 > uint.MaxValue))
                {
                    throw Configuration("Selected power-on counters require a valid equipment lifetime duration.");
                }
                if (!initializing && m_lastPowerOnDuration is double previous && duration < previous)
                {
                    throw Configuration("Power-on duration cannot decrease during a machine's lifetime.");
                }
            }
            if (Has(MachineToolFeatures.PartsProducedInLifetime))
            {
                if (!Has(MachineToolFeatures.OperationCounters) || operation.PartsProducedInLifetime == null ||
                    (!initializing && operation.PartsProducedInLifetime < m_lastLifetimeParts))
                {
                    throw Configuration("Lifetime parts require OperationCounters and a nondecreasing equipment counter.");
                }
            }
            if (Has(MachineToolFeatures.Obligation) &&
                (operation.Obligation == null || operation.Obligation is { EndUser: true, MachineBuilder: true }))
            {
                throw Configuration("Selected obligation requires an unambiguous responsibility observation.");
            }
            if (Has(MachineToolFeatures.MachineryState))
            {
                if (operation.ItemState == null || operation.MachineryMode == null ||
                    (int)operation.ItemState is < 0 or > 3 || (int)operation.MachineryMode is < 0 or > 3 ||
                    (operation.FeedOverride is double feed && !ValidOverride(feed, 200)) ||
                    (operation.IsWarmUp == true && (operation.ItemState != MachineToolItemState.Executing ||
                        operation.MachineryMode != MachineToolMachineryMode.Setup)))
                {
                    throw Configuration("Selected Machinery states require consistent item/mode and warm-up observations.");
                }
            }
            if (Has(MachineToolFeatures.MaintenanceMode) &&
                (!Has(MachineToolFeatures.MachineryState) || operation.MaintenanceMode == null ||
                    (int)operation.MaintenanceMode is < 0 or > 4))
            {
                throw Configuration("Maintenance mode requires a Machinery parent mode and a valid maintenance observation.");
            }
            if (Has(MachineToolFeatures.SoftwareIdentification))
            {
                if (snapshot.Software.Count is 0 or > 256)
                {
                    throw Configuration("SoftwareIdentification requires between one and 256 software components.");
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (MachineToolSoftware software in snapshot.Software)
                {
                    if (software == null || string.IsNullOrWhiteSpace(software.Identifier) ||
                        string.IsNullOrWhiteSpace(software.Revision) || !names.Add(software.Identifier) ||
                        (!initializing && (!m_software.TryGetValue(software.Identifier, out SoftwareIdentificationState? node) ||
                            software.Manufacturer.IsNull != (node.Manufacturer == null))))
                    {
                        throw Configuration("Invalid or changed software-component inventory.");
                    }
                }
                if (!initializing && names.Count != m_software.Count)
                {
                    throw Configuration("Software-component inventory is fixed for the instance lifetime.");
                }
            }
            if (Has(MachineToolFeatures.WorkingUnits))
            {
                int count = snapshot.Spindles.Count + snapshot.Lasers.Count + snapshot.EdmGenerators.Count;
                if (count > 256 || (!initializing && count != m_workingUnits.Count))
                {
                    throw Configuration("Working-unit inventory is fixed and limited to 256 entries.");
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (MachineToolSpindle spindle in snapshot.Spindles)
                {
                    ValidateUnitName(spindle.Name, names, initializing);
                    ValidateSample(spindle.Sample);
                    if (!IsFinite(spindle.MaximumOverride) || spindle.MaximumOverride <= 0 ||
                        (spindle.Override is double value && !ValidOverride(value, spindle.MaximumOverride)))
                    {
                        throw Configuration("Invalid spindle override.");
                    }
                    if (!initializing && (m_workingUnits[spindle.Name] is not SpindleMonitoringState state ||
                        (state.Override != null) != spindle.Override.HasValue ||
                        (state.IsUsedAsAxis != null) != spindle.IsUsedAsAxis.HasValue))
                    {
                        throw Configuration("A refresh cannot change spindle type or optional-member selection.");
                    }
                }
                foreach (MachineToolLaser laser in snapshot.Lasers)
                {
                    ValidateUnitName(laser.Name, names, initializing);
                    ValidateSample(laser.Sample);
                    if ((int)laser.State is < 0 or > 3 ||
                        (!initializing && m_workingUnits[laser.Name] is not LaserMonitoringState))
                    {
                        throw Configuration("Invalid laser observation.");
                    }
                }
                foreach (MachineToolEdmGenerator generator in snapshot.EdmGenerators)
                {
                    ValidateUnitName(generator.Name, names, initializing);
                    ValidateSample(generator.Sample);
                    if ((int)generator.State is < 0 or > 4 ||
                        (!initializing && m_workingUnits[generator.Name] is not EDMGeneratorMonitoringState))
                    {
                        throw Configuration("Invalid EDM-generator observation.");
                    }
                }
                foreach (MachineToolChannel channel in snapshot.Channels)
                {
                    if (Has(MachineToolFeatures.Channels) && names.Contains(channel.Name))
                    {
                        throw Configuration("Channels and working units need distinct application browse names.");
                    }
                }
            }
        }

        /// <summary>
        /// Stages selected nameplate, working-unit and operation-monitoring nodes.
        /// </summary>
        private void CreateExtendedMonitoring(MachineToolSnapshot snapshot)
        {
            ISystemContext context = m_context.Context;
            MachineOperationMonitoringState operation = State.Monitoring!.MachineTool!;
            if (Has(MachineToolFeatures.SoftwareIdentification))
            {
                State.Identification!.AddSoftwareIdentification(context);
                foreach (MachineToolSoftware software in snapshot.Software)
                {
                    SoftwareIdentificationState state = context.CreateInstanceOfSoftwareIdentificationType(
                        State.Identification.SoftwareIdentification!,
                        new QualifiedName(software.Identifier, m_context.InstanceNamespaceIndex));
                    if (!software.Manufacturer.IsNull)
                    {
                        state.AddManufacturer(context);
                    }
                    State.Identification.SoftwareIdentification!.AddChild(state);
                    m_software.Add(software.Identifier, state);
                }
            }
            if (Has(MachineToolFeatures.WorkingUnits))
            {
                foreach (MachineToolSpindle spindle in snapshot.Spindles)
                {
                    SpindleMonitoringState state = context.CreateInstanceOfSpindleMonitoringType(
                        State.Monitoring, new QualifiedName(spindle.Name, m_context.InstanceNamespaceIndex));
                    if (spindle.Override.HasValue)
                    {
                        state.AddOverride(context);
                        ConfigureOverride(state.Override!, spindle.MaximumOverride);
                    }
                    if (spindle.IsUsedAsAxis.HasValue)
                    {
                        state.AddIsUsedAsAxis(context);
                    }
                    State.Monitoring.AddChild(state);
                    m_workingUnits.Add(spindle.Name, state);
                }
                foreach (MachineToolLaser laser in snapshot.Lasers)
                {
                    LaserMonitoringState state = context.CreateInstanceOfLaserMonitoringType(
                        State.Monitoring, new QualifiedName(laser.Name, m_context.InstanceNamespaceIndex));
                    State.Monitoring.AddChild(state);
                    m_workingUnits.Add(laser.Name, state);
                }
                foreach (MachineToolEdmGenerator generator in snapshot.EdmGenerators)
                {
                    EDMGeneratorMonitoringState state = context.CreateInstanceOfEDMGeneratorMonitoringType(
                        State.Monitoring, new QualifiedName(generator.Name, m_context.InstanceNamespaceIndex));
                    State.Monitoring.AddChild(state);
                    m_workingUnits.Add(generator.Name, state);
                }
            }
            if (Has(MachineToolFeatures.LegacyPowerOnDuration))
            {
                operation.AddPowerOnDuration(context);
            }
            if (Has(MachineToolFeatures.OperationCounters))
            {
                operation.AddOperationCounters(context);
                if (operation.OperationCounters!.PowerOnDuration == null)
                {
                    operation.OperationCounters.AddPowerOnDuration(context);
                }
                LinkBuildingBlock(operation.OperationCounters);
                if (Has(MachineToolFeatures.PartsProducedInLifetime))
                {
                    var counter = BaseDataVariableState<ulong>.With<VariantBuilder>(operation.OperationCounters);
                    var name = new QualifiedName(BrowseNames.PartsProducedInLifetime,
                        (ushort)context.NamespaceUris.GetIndex(Namespaces.MachineTool));
                    counter.Create(context, NodeId.Null, name, new LocalizedText(name.Name), true);
                    counter.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                    operation.OperationCounters.AddChild(counter);
                    m_lifetimeParts = counter;
                }
            }
            if (Has(MachineToolFeatures.Obligation))
            {
                operation.AddObligation(context);
            }
            if (Has(MachineToolFeatures.MachineryState))
            {
                operation.AddMachineryItemState(context);
                operation.AddMachineryOperationMode(context);
                LinkBuildingBlock(operation.MachineryItemState!);
                LinkBuildingBlock(operation.MachineryOperationMode!);
                if (snapshot.Operation.FeedOverride.HasValue)
                {
                    operation.AddFeedOverride(context);
                    ConfigureOverride(operation.FeedOverride!, 200);
                }
                if (snapshot.Operation.IsWarmUp.HasValue)
                {
                    operation.AddIsWarmUp(context);
                }
                if (Has(MachineToolFeatures.MaintenanceMode))
                {
                    operation.MachineryOperationMode!.AddMaintenanceMode(context);
                }
            }
            CreateStacklights(snapshot);
        }

        /// <summary>
        /// Publishes validated equipment observations on their original generated nodes.
        /// </summary>
        private void ApplyExtendedMonitoring(MachineToolSnapshot snapshot)
        {
            MachineToolOperation data = snapshot.Operation;
            MachineOperationMonitoringState operation = State.Monitoring!.MachineTool!;
            MachineToolSampleInfo sample = data.Sample;
            if (Has(MachineToolFeatures.SoftwareIdentification))
            {
                foreach (MachineToolSoftware software in snapshot.Software)
                {
                    SoftwareIdentificationState state = m_software[software.Identifier];
                    state.Identifier!.Value = software.Identifier;
                    state.SoftwareRevision!.Value = software.Revision;
                    if (state.Manufacturer != null)
                    {
                        state.Manufacturer.Value = software.Manufacturer;
                    }
                }
            }
            if (Has(MachineToolFeatures.WorkingUnits))
            {
                foreach (MachineToolSpindle spindle in snapshot.Spindles)
                {
                    var state = (SpindleMonitoringState)m_workingUnits[spindle.Name];
                    SetSample(state.Name!, Variant.From(spindle.Name), spindle.Sample);
                    MachineToolSampleInfo rotationSample = spindle.IsUsedAsAxis == true
                        ? spindle.Sample with { StatusCode = StatusCodes.BadStateNotActive } : spindle.Sample;
                    SetSample(state.IsRotating!, Variant.From(spindle.IsRotating), rotationSample);
                    if (spindle.Override is double value)
                    {
                        SetSample(state.Override!, Variant.From(value), rotationSample);
                        ConfigureOverride(state.Override!, spindle.MaximumOverride);
                    }
                    if (spindle.IsUsedAsAxis is bool usedAsAxis)
                    {
                        SetSample(state.IsUsedAsAxis!, Variant.From(usedAsAxis), spindle.Sample);
                    }
                }
                foreach (MachineToolLaser laser in snapshot.Lasers)
                {
                    var state = (LaserMonitoringState)m_workingUnits[laser.Name];
                    SetSample(state.Name!, Variant.From(laser.Name), laser.Sample);
                    SetSample(state.ControllerIsOn!, Variant.From(laser.ControllerIsOn), laser.Sample);
                    SetSample(state.LaserState!, Variant.From(laser.State), laser.Sample);
                }
                foreach (MachineToolEdmGenerator generator in snapshot.EdmGenerators)
                {
                    var state = (EDMGeneratorMonitoringState)m_workingUnits[generator.Name];
                    SetSample(state.Name!, Variant.From(generator.Name), generator.Sample);
                    SetSample(state.IsOn!, Variant.From(generator.IsOn), generator.Sample);
                    SetSample(state.EDMGeneratorState!, Variant.From(generator.State), generator.Sample);
                }
            }
            if (Has(MachineToolFeatures.LegacyPowerOnDuration))
            {
                SetSample(operation.PowerOnDuration!,
                    Variant.From((uint)(data.PowerOnDurationMilliseconds!.Value / 3_600_000)), sample);
            }
            if (Has(MachineToolFeatures.OperationCounters))
            {
                SetSample(operation.OperationCounters!.PowerOnDuration!,
                    Variant.From(data.PowerOnDurationMilliseconds!.Value), sample);
            }
            if (Has(MachineToolFeatures.LegacyPowerOnDuration | MachineToolFeatures.OperationCounters))
            {
                m_lastPowerOnDuration = data.PowerOnDurationMilliseconds;
            }
            if (m_lifetimeParts != null)
            {
                SetSample(m_lifetimeParts, Variant.From(data.PartsProducedInLifetime!.Value), sample);
                m_lastLifetimeParts = data.PartsProducedInLifetime;
            }
            if (Has(MachineToolFeatures.Obligation))
            {
                MachineToolObligation obligation = data.Obligation!.Value;
                SetSample(operation.Obligation!.EndUserObligated!, Variant.From(obligation.EndUser), sample);
                SetSample(operation.Obligation.MachineBuilderObligated!, Variant.From(obligation.MachineBuilder), sample);
            }
            if (Has(MachineToolFeatures.MachineryState))
            {
                SetObservedState(operation.MachineryItemState!, data.ItemState!.Value.ToString(), sample);
                SetObservedState(operation.MachineryOperationMode!, data.MachineryMode!.Value.ToString(), sample);
                if (operation.FeedOverride != null && data.FeedOverride is double feed)
                {
                    SetSample(operation.FeedOverride, Variant.From(feed), sample);
                }
                if (operation.IsWarmUp != null && data.IsWarmUp is bool warmUp)
                {
                    SetSample(operation.IsWarmUp, Variant.From(warmUp), sample);
                }
                if (Has(MachineToolFeatures.MaintenanceMode))
                {
                    MachineToolSampleInfo maintenance = data.MachineryMode == MachineToolMachineryMode.Maintenance
                        ? sample : sample with { StatusCode = StatusCodes.BadStateNotActive };
                    SetObservedState(operation.MachineryOperationMode!.MaintenanceMode!,
                        data.MaintenanceMode!.Value.ToString(), maintenance);
                }
            }
            ApplyStacklights(snapshot);
        }

        /// <summary>
        /// Validates the fixed physical stacklight topology and every reported segment.
        /// </summary>
        private void ValidateStacklights(MachineToolSnapshot snapshot, bool initializing)
        {
            if (!Has(MachineToolFeatures.Stacklights))
            {
                return;
            }
            if (snapshot.Stacklights.Count is 0 or > 16 ||
                (!initializing && snapshot.Stacklights.Count != m_stacklights.Count))
            {
                throw Configuration("Selected stacklights require a fixed inventory of one to sixteen lights.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MachineToolStacklight light in snapshot.Stacklights)
            {
                if (light == null || string.IsNullOrWhiteSpace(light.Name) || !names.Add(light.Name) ||
                    light.Lights.Count is 0 or > 32 ||
                    (!initializing && (!m_stacklights.TryGetValue(light.Name, out ArrayOf<StackElementLightState> nodes) ||
                        nodes.Count != light.Lights.Count)))
                {
                    throw Configuration("A segmented stacklight requires a stable name and one to 32 light elements.");
                }
                foreach (MachineToolLight segment in light.Lights)
                {
                    if (segment == null || (int)segment.Color is < 0 or > 7 || (int)segment.Mode is < 0 or > 3)
                    {
                        throw Configuration("Invalid IA light color or mode.");
                    }
                    ValidateSample(segment.Sample);
                }
            }
        }

        /// <summary>
        /// Creates the declared stacklight and any additional application-named physical stacklights.
        /// </summary>
        private void CreateStacklights(MachineToolSnapshot snapshot)
        {
            if (!Has(MachineToolFeatures.Stacklights))
            {
                return;
            }
            ISystemContext context = m_context.Context;
            foreach (MachineToolStacklight light in snapshot.Stacklights)
            {
                BasicStacklightState stack;
                if (m_stacklights.Count == 0)
                {
                    State.Monitoring!.AddStacklight(context);
                    stack = State.Monitoring.Stacklight!;
                }
                else
                {
                    stack = context.CreateInstanceOfBasicStacklightType(State.Monitoring!,
                        new QualifiedName(light.Name, m_context.InstanceNamespaceIndex));
                    State.Monitoring!.AddChild(stack);
                }
                stack.DisplayName = new LocalizedText(light.Name);
                stack.StacklightMode!.Value = StacklightOperationMode.Segmented;
                var segments = new List<StackElementLightState>(light.Lights.Count);
                for (ushort index = 0; index < light.Lights.Count; index++)
                {
                    StackElementLightState element = context.CreateInstanceOfStackElementLightType(
                        stack, new QualifiedName("Light" + index, m_context.InstanceNamespaceIndex));
                    element.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasOrderedComponent;
                    element.NumberInList!.Value = index;
                    Child<BaseVariableState>(element, IA.BrowseNames.SignalOn, IA.Namespaces.IA, create: true);
                    Child<BaseVariableState>(element, IA.BrowseNames.SignalColor, IA.Namespaces.IA, create: true);
                    Child<BaseVariableState>(element, IA.BrowseNames.SignalMode, IA.Namespaces.IA, create: true);
                    stack.AddChild(element);
                    segments.Add(element);
                }
                m_stacklights.Add(light.Name, segments.ToArrayOf());
            }
        }

        /// <summary>
        /// Publishes physical light values while retaining their acquisition quality.
        /// </summary>
        private void ApplyStacklights(MachineToolSnapshot snapshot)
        {
            if (!Has(MachineToolFeatures.Stacklights))
            {
                return;
            }
            foreach (MachineToolStacklight light in snapshot.Stacklights)
            {
                ArrayOf<StackElementLightState> nodes = m_stacklights[light.Name];
                for (int index = 0; index < nodes.Count; index++)
                {
                    MachineToolLight segment = light.Lights[index];
                    StackElementLightState node = nodes[index];
                    SetSample(node.SignalOn!, Variant.From(segment.IsOn), segment.Sample);
                    SetSample(node.SignalColor!, Variant.From(segment.Color), segment.Sample);
                    SetSample(node.SignalMode!, Variant.From(segment.Mode), segment.Sample);
                }
            }
        }

        /// <summary>
        /// Links an existing nested block into the same Machinery organizer as the nameplate.
        /// </summary>
        private void LinkBuildingBlock(BaseInstanceState block)
        {
            FolderState organizer = Child<FolderState>(
                State, "MachineryBuildingBlocks", Opc.Ua.Machinery.Namespaces.Machinery, create: true);
            if (!organizer.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasAddIn, false, block.NodeId))
            {
                organizer.AddReference(Opc.Ua.ReferenceTypeIds.HasAddIn, false, block.NodeId);
                block.AddReference(Opc.Ua.ReferenceTypeIds.HasAddIn, true, organizer.NodeId);
            }
        }

        /// <summary>
        /// Validates a unique, stable working-unit name.
        /// </summary>
        private void ValidateUnitName(string name, HashSet<string> names, bool initializing)
        {
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name) ||
                (!initializing && !m_workingUnits.ContainsKey(name)))
            {
                throw Configuration("Invalid or changed working-unit inventory.");
            }
        }

        /// <summary>
        /// Tests whether any feature in the supplied mask was selected.
        /// </summary>
        private bool Has(MachineToolFeatures features)
        {
            return (Features & features) != 0;
        }

        /// <summary>
        /// Creates a configuration error without publishing partial observations.
        /// </summary>
        private static ServiceResultException Configuration(string message)
        {
            return new ServiceResultException(StatusCodes.BadConfigurationError, message);
        }

        /// <summary>Per-instance software identity nodes.</summary>
        private readonly Dictionary<string, SoftwareIdentificationState> m_software = new(StringComparer.Ordinal);

        /// <summary>Per-instance working-unit nodes.</summary>
        private readonly Dictionary<string, WorkingUnitMonitoringState> m_workingUnits = new(StringComparer.Ordinal);

        /// <summary>Per-instance ordered light elements keyed by physical stacklight name.</summary>
        private readonly Dictionary<string, ArrayOf<StackElementLightState>> m_stacklights = new(StringComparer.Ordinal);

        /// <summary>The selected lifetime-parts counter.</summary>
        private BaseDataVariableState<ulong>? m_lifetimeParts;

        /// <summary>The previous duration, used to enforce nondecreasing lifetime observations.</summary>
        private double? m_lastPowerOnDuration;

        /// <summary>The previous parts count, used to enforce nondecreasing lifetime observations.</summary>
        private ulong? m_lastLifetimeParts;
    }
}
