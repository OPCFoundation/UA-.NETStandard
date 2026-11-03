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
using Opc.Ua.Server;

namespace Opc.Ua.PADIM.Server
{
    public sealed partial class PadimDevice : IConformanceContributor
    {
        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => GetCapabilities().Units;

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => GetCapabilities().Profiles;

        /// <summary>
        /// Derives the contribution from registered instances and provider-bound capabilities, not loaded types.
        /// </summary>
        private (ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles) GetCapabilities()
        {
            PadimValueBinding[] bindings;
            lock (m_bindingGate)
            {
                if (m_closed)
                {
                    return (ArrayOf<QualifiedName>.Empty, ArrayOf<string>.Empty);
                }
                bindings = m_bindings.ToArray();
            }
            if (!ReferenceEquals(Manager.FindPredefinedNode(State.NodeId), State))
            {
                return (ArrayOf<QualifiedName>.Empty, ArrayOf<string>.Empty);
            }
            var units = new HashSet<string>(StringComparer.Ordinal);
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            var signals = new HashSet<SignalState>(RefEqualityComparer.Default);
            var bound = new HashSet<BaseVariableState>(RefEqualityComparer.Default);
            foreach (PadimValueBinding binding in bindings)
            {
                bound.Add(binding.Variable);
            }
            bool health =
                FindMember(State, "DeviceHealth", Di.Namespaces.OpcUaDi) is BaseVariableState healthVariable &&
                bound.Contains(healthVariable);
            foreach (PadimValueBinding binding in bindings)
            {
                BaseVariableState variable = binding.Variable;
                if (ReferenceEquals(variable, State.DisplayLanguage))
                {
                    Add("Admin", "IAdmin DisplayLanguage");
                }
                if (ReferenceEquals(variable, State.DateOfLastChange))
                {
                    Add("Admin", "IAdmin DateOfChange");
                }
                if (binding.SupportsSimulation)
                {
                    string family = variable switch
                    {
                        AnalogSignalVariableState => "AnalogSignalVariable",
                        DiscreteSignalVariableState => "DiscreteVariable",
                        TwoStateDiscreteSignalVariableState => "TwoStateDiscreteVariable",
                        MultiStateDiscreteSignalVariableState => "MultiStateDiscreteVariable",
                        _ => throw new InvalidOperationException("The configured simulation family is unsupported.")
                    };
                    Add("Simulation", $"{family} Simulation");
                }
                NodeState? ancestor = variable.Parent;
                bool calibrationPoint = false;
                bool componentConditions = false;
                while (ancestor is BaseInstanceState instance && !ReferenceEquals(instance, State))
                {
                    calibrationPoint |= instance.BrowseName.Name == "CalibrationPointSet";
                    componentConditions |= instance.BrowseName.Name == "DeviceComponentConditions";
                    if (instance is SignalState signal && ReferenceEquals(variable.Parent, signal) &&
                        variable.BrowseName.Name is
                            "AnalogSignal" or "ControlSignal" or "DiscreteSignal" or
                            "TwoStateDiscreteSignal" or "MultiStateDiscreteSignal" &&
                        ReferenceEquals(FindMember(signal, variable.BrowseName.Name), variable))
                    {
                        signals.Add(signal);
                    }
                    if (instance is BaseObjectState group && Implements(group, ObjectTypeIds.ICalibrationType))
                    {
                        Add("BaseSignalCalibration", "ICalibration");
                        if (ReferenceEquals(variable.Parent, group) &&
                            variable.BrowseName.Name is "CalibrationTimestamp" or "TypeOfCalibration")
                        {
                            Add("BaseSignalCalibration", $"ICalibration {variable.BrowseName.Name}");
                        }
                        if (calibrationPoint && variable.Parent is BaseObjectState point &&
                            HasBoundFields(point, "CalibrationSetpoint", "CalibrationActualValue"))
                        {
                            Add("BaseSignalCalibration", "ICalibration CalibrationPointSet");
                        }
                    }
                    if (HasType(instance, ObjectTypeIds.GeneralDeviceConditionSetType) &&
                        variable.BrowseName.Name is "InternalTemperature" or "OperationCycleCounter" or
                            "OperationDuration" or "PowerOnDuration" or "ResidualLife")
                    {
                        Add("BaseDeviceCondition", $"GeneralDeviceConditionSetType {variable.BrowseName.Name}");
                    }
                    if (instance.BrowseName.Name == BrowseNames.DeviceConditionSet)
                    {
                        Add("BaseDeviceCondition", "Device Condition Set");
                        if (FindMember(instance, "GeneralDeviceConditions") is BaseObjectState general &&
                            HasType(general, ObjectTypeIds.GeneralDeviceConditionSetType))
                        {
                            Add("BaseDeviceCondition", "IGeneralDeviceConditionSet");
                        }
                        if (componentConditions && variable.Parent is BaseObjectState component &&
                            HasType(component, ObjectTypeIds.GeneralDeviceConditionSetType))
                        {
                            Add("BaseDeviceCondition", "IGeneralDeviceConditionSet DeviceComponentConditions");
                        }
                    }
                    ancestor = instance.Parent;
                }
                AddMeasurement(variable);
            }
            bool baseDeviceSupported = health && HasIdentity();
            bool nanoEmbeddedSupported = baseDeviceSupported && SupportsNanoEmbeddedProfile();
            if (baseDeviceSupported)
            {
                units.Add("PA-DIM Base");
                if (nanoEmbeddedSupported)
                {
                    Add("Core", "Nano Embedded");
                }
                Add("BaseDeviceCondition");
                foreach ((PadimDeviceKind kind, string suffix) in s_deviceFacets)
                {
                    if (HasType(State, PadimFactories.DeviceType(kind)) &&
                        (kind != PadimDeviceKind.TocAnalyser ||
                            SupportsGroup(State, "DeviceConditionSet", ObjectTypeIds.ITocDeviceConditionSetType,
                                "ReactorTemperature", "CoolerTemperature",
                                "CarrierGasVolumeFlow", "ReferenceInjectionVolume")))
                    {
                        Add(suffix, "ProcessAnalyserType", $"{kind}Type");
                        AddOptionalMembers(suffix, $"{kind}Type", FindMember(State, "DeviceConditionSet"));
                    }
                }
                if (nanoEmbeddedSupported && signals.Count != 0)
                {
                    Add("Core", "ISignal", "SignalSet");
                }
                if (nanoEmbeddedSupported && State.SubDevices != null)
                {
                    var children = new List<BaseInstanceState>();
                    State.SubDevices.GetChildren(Context, children);
                    foreach (BaseInstanceState child in children)
                    {
                        if (child is PADIMState && ReferenceEquals(Manager.FindPredefinedNode(child.NodeId), child))
                        {
                            Add("Core", "SubDevices");
                            break;
                        }
                    }
                }
                if (nanoEmbeddedSupported &&
                    FindMember(State, "DeviceRevision", Di.Namespaces.OpcUaDi) is BaseVariableState revision &&
                    revision.WrappedValue.TryGetValue(out string deviceRevision) &&
                    !string.IsNullOrWhiteSpace(deviceRevision))
                {
                    Add("Core", "DeviceRevision");
                }
            }
            if (m_healthAlarms != null)
            {
                Add("Alarming", "Base Alarming", "Browsable Alarms");
            }
            if (State.FactoryReset?.OnCallMethod2Async != null)
            {
                Add("Admin", "IAdmin FactoryReset");
            }
            foreach (SignalState signal in signals)
            {
                foreach ((ExpandedNodeId type, string suffix, string unit, bool analytical) in s_signalFacets)
                {
                    bool requiresPat = analytical && suffix is not ("AmperometricGasDetectorSignal" or
                        "GasChromatographSignal" or "InfraredSensorSignal");
                    if (!HasType(signal, type) || !HasRequiredSignalMembers(signal, unit, requiresPat))
                    {
                        continue;
                    }
                    AddOptionalMembers(suffix, unit, FindMember(signal, "SignalConditionSet"));
                    AddSignalOptions(suffix, signal);
                    if (signal is AnalogSignalObjectState analog)
                    {
                        AddProcessOptions(suffix, analog.AnalogSignal);
                        var children = new List<BaseInstanceState>();
                        signal.GetChildren(Context, children);
                        foreach (BaseInstanceState child in children)
                        {
                            if (child is BaseObjectState calibration &&
                                Implements(calibration, ObjectTypeIds.ICalibrationType))
                            {
                                string calibrationPrefix = unit switch
                                {
                                    "AmperometricSignalType" => "IAmperometricCalibration",
                                    "OpticalFluorescenseQuenchingSignalType" =>
                                        "IOpticalFluorescenseQuenchingCalibration",
                                    "PhSignalType" => "IPhCalibration",
                                    "GasChromatographSignalType" => "GasChromatograph",
                                    _ => string.Empty
                                };
                                AddOptionalMembers(suffix, calibrationPrefix, calibration);
                            }
                        }
                    }
                    Add(suffix, unit);
                    if (analytical)
                    {
                        Add(suffix, "Analog Signal", "AnalyticalSignalType");
                        if (suffix is not ("AmperometricGasDetectorSignal" or
                            "GasChromatographSignal" or "InfraredSensorSignal"))
                        {
                            Add(suffix, "AnalogSignalVariableType", "AnalyticalMeasurementVariable",
                                "PatMeasurementVariableType");
                            Add("BaseSignalCalibration");
                        }
                    }
                    else
                    {
                        switch (suffix)
                        {
                            case "ControlSignal":
                                Add(suffix, "AnalogSignalVariableType", "ControlVariable");
                                Add("BaseSignalCalibration");
                                break;
                            case "TwoStateControlSignal":
                                Add(suffix, "TwoStateDiscreteControlVariable", "TwoStateDiscreteVariable");
                                break;
                            case "MultiStateControlSignal":
                                Add(suffix, "MultiStateDiscreteControlVariable", "MultiStateDiscreteVariable");
                                break;
                            case "DiscreteSignal":
                                Add(suffix, "DiscreteVariable");
                                break;
                            case "TwoStateDiscreteSignal":
                                Add(suffix, "TwoStateDiscreteVariable");
                                break;
                            case "MultiStateDiscreteSignal":
                                Add(suffix, "MultiStateDiscreteVariable");
                                break;
                        }
                    }
                }
            }
            var resultUnits = new List<QualifiedName>(units.Count);
            foreach (string unit in units)
            {
                resultUnits.Add(new QualifiedName(unit));
            }
            return (resultUnits.ToArrayOf(), new List<string>(profiles).ToArrayOf());

            void Add(string facet, params string[] enabledUnits)
            {
                profiles.Add($"http://opcfoundation.org/UA-Profile/PADIM/Server/{facet}");
                foreach (string unit in enabledUnits)
                {
                    units.Add($"PA-DIM {unit}");
                }
            }

            void AddMeasurement(BaseVariableState variable)
            {
                if (variable.Parent is not AnalogSignalObjectState signal ||
                    !ReferenceEquals(FindMember(signal, "AnalogSignal"), variable))
                {
                    return;
                }
                foreach ((ExpandedNodeId type, string suffix, string unit) in s_measurementFacets)
                {
                    string[] required = unit switch
                    {
                        "TemperatureMeasurementVariable" => ["SensorType"],
                        "FlowMeasurementVariable" or "MassFlowRateVariable" => ["LowFlowCutOff"],
                        "TotalizerVariable" => ["PulseValue", "PulseWidth"],
                        _ => []
                    };
                    if (HasType(variable, type) && HasBoundFields(variable, required))
                    {
                        Add(suffix, "Analog Signal", "AnalogSignalVariableType", unit);
                        Add("BaseSignalCalibration");
                        AddOptionalMembers(suffix, unit, variable);
                        AddProcessOptions(suffix, variable);
                        AddSignalOptions(suffix, signal);
                        if (HasType(variable, VariableTypeIds.ActualVolumeFlowRateVariableType))
                        {
                            Add(suffix, "ActualVolumeFlowRateVariable");
                        }
                        if (HasType(variable, VariableTypeIds.NormalizedVolumeFlowRateVariableType))
                        {
                            Add(suffix, "NormalizedVolumeFlowRateVariable");
                        }
                    }
                }
            }

            void AddSignalOptions(string facet, SignalState signal)
            {
                if (FindMember(signal, "SignalConditionSet") is BaseObjectState conditions)
                {
                    var children = new List<BaseInstanceState>();
                    conditions.GetChildren(Context, children);
                    foreach (BaseInstanceState child in children)
                    {
                        if (child is BaseVariableState field && bound.Contains(field))
                        {
                            Add(facet, "Signal Condition Set");
                            break;
                        }
                    }
                }
                if (FindMember(signal, "ControlSignal") is BaseVariableState control)
                {
                    AddProcessOptions(facet, control);
                }
                if (FindMember(signal, "ZeroPointAdjustment") is MethodState { OnCallMethod2Async: not null })
                {
                    Add(facet, "ZeroPointAdjustment method");
                }
                if (FindMember(signal, "AutoAdjustPositioner") is MethodState { OnCallMethod2Async: not null })
                {
                    Add(facet, "AutoAdjustPositioner method");
                }
            }

            bool HasBoundFields(NodeState scope, params string[] fields)
            {
                foreach (string field in fields)
                {
                    if (FindMember(scope, field) is not BaseVariableState variable || !bound.Contains(variable))
                    {
                        return false;
                    }
                }
                return true;
            }

            bool SupportsGroup(
                NodeState owner, string name, ExpandedNodeId requiredInterface, params string[] fields)
            {
                return FindMember(owner, name) is BaseObjectState group &&
                    Implements(group, requiredInterface) && HasBoundFields(group, fields);
            }

            bool HasRequiredSignalMembers(SignalState signal, string unit, bool requiresPat)
            {
                string[] requiredControlFields = unit switch
                {
                    "Control Signal" => ["Setpoint", "OperatingDirection", "ActuatorType"],
                    "TwoStateDiscreteControl Signal" => ["Setpoint", "OperatingDirection"],
                    "MultiStateDiscreteControl Signal" => ["Setpoint"],
                    _ => []
                };
                if (requiredControlFields.Length != 0 &&
                    (FindMember(signal, "ControlSignal") is not BaseVariableState control ||
                        !HasBoundFields(control, requiredControlFields)))
                {
                    return false;
                }
                (ExpandedNodeId ConditionInterface, string[] Conditions,
                    ExpandedNodeId CalibrationInterface, string[] CalibrationFields) requirements = unit switch
                {
                    "AmperometricSignalType" => (ObjectTypeIds.IAmperometricSignalConditionSetType,
                        ["SensingElementTemperature"], ObjectTypeIds.IAmperometricCalibrationType,
                        ["AmperometricSensorSlope", "AmperometricSensorZeroPoint"]),
                    "ConductivitySignalType" => (ObjectTypeIds.IConductivitySignalConditionSetType,
                        ["ConductivityMeasuringMethod", "TemperatureCompensationStyle", "SensingElementTemperature"],
                        ObjectTypeIds.IConductivityCalibrationType, ["ConductivityCellConstant"]),
                    "OpticalFluorescenseQuenchingSignalType" =>
                        (ObjectTypeIds.IOpticalFluorescenseQuenchingSignalConditionSetType,
                            ["SensingElementTemperature"], ObjectTypeIds.IOpticalFluorescenseQuenchingCalibrationType,
                            ["OpticalFluorescenseQuenchingSensorSlope", "OpticalFluorescenseQuenchingSensorZeroPoint"]),
                    "PhSignalType" => (ObjectTypeIds.IPhSignalConditionSetType,
                        ["PhMeasuringMethod", "SensingElementTemperature"], ObjectTypeIds.IPhCalibrationType,
                        ["SensorSlope", "SensorAsymmetryPotential"]),
                    "NonDispersiveInfraredSignalType" => (ObjectTypeIds.INonDispersiveInfraredSignalConditionSetType,
                        ["SourceResidualLife", "SampleCellTemperature"], ExpandedNodeId.Null, []),
                    "ParamagneticSignalType" => (ObjectTypeIds.IParamagneticSignalConditionSetType,
                        ["SampleTemperature", "SensingElementTemperature"], ExpandedNodeId.Null, []),
                    "ThermalConductivitySignalType" => (ObjectTypeIds.IThermalConductivitySignalConditionSetType,
                        ["SampleTemperature"], ExpandedNodeId.Null, []),
                    "TocSignalType" => (ObjectTypeIds.ITocSignalConditionSetType,
                        ["DetectorZeroSignal", "SourceResidualLife", "SampleCellTemperature"], ExpandedNodeId.Null, []),
                    "TunableDiodeLaserSignalType" => (ObjectTypeIds.ITunableDiodeLaserSignalConditionSetType,
                        ["SampleTemperature"], ExpandedNodeId.Null, []),
                    "ZirconiumDioxideSignalType" => (ObjectTypeIds.IZirconiumDioxideSignalConditionSetType,
                        ["SensingElementTemperature"], ExpandedNodeId.Null, []),
                    "DiodeArraySignalType" => (ObjectTypeIds.IDiodeArraySignalConditionSetType,
                        ["SensingElementTemperature"], ExpandedNodeId.Null, []),
                    "FtnirOrFtirSignalType" => (ObjectTypeIds.IFtnirOrFtirSignalConditionSetType,
                        ["SensingElementTemperature"], ExpandedNodeId.Null, []),
                    "AmperometricGasDetectorSignalType" =>
                        (ObjectTypeIds.IAmperometricGasDetectorSignalConditionSetType, [], ExpandedNodeId.Null, []),
                    "GasChromatographSignalType" => (ObjectTypeIds.IGasChromatographSignalConditionSetType,
                        [], ExpandedNodeId.Null, []),
                    "InfraredSignalType" => (ObjectTypeIds.IInfraredSignalConditionSetType,
                        [], ExpandedNodeId.Null, []),
                    "RamanSignalType" => (ObjectTypeIds.IRamanSignalConditionSetType,
                        [], ExpandedNodeId.Null, []),
                    "CatalyticBeadSignalType" => (ObjectTypeIds.ICatalyticBeadSignalConditionSetType,
                        [], ExpandedNodeId.Null, []),
                    _ => (ExpandedNodeId.Null, [], ExpandedNodeId.Null, [])
                };
                var (conditionInterface, conditions, calibrationInterface, calibrationFields) = requirements;
                if (!conditionInterface.IsNull &&
                    !SupportsGroup(signal, "SignalConditionSet", conditionInterface, conditions))
                {
                    return false;
                }
                if (!calibrationInterface.IsNull)
                {
                    var children = new List<BaseInstanceState>();
                    signal.GetChildren(Context, children);
                    bool found = false;
                    foreach (BaseInstanceState child in children)
                    {
                        if (child is BaseObjectState calibration && Implements(calibration, calibrationInterface) &&
                            HasBoundFields(calibration, calibrationFields))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        return false;
                    }
                }
                return !requiresPat || signal is AnalogSignalObjectState analog &&
                    analog.AnalogSignal != null &&
                    HasType(analog.AnalogSignal, VariableTypeIds.PatMeasurementVariableType) &&
                    HasBoundFields(analog.AnalogSignal, "PatMeasurandDescription");
            }

            void AddOptionalMembers(string facet, string prefix, NodeState? scope)
            {
                if (scope == null || prefix.Length == 0)
                {
                    return;
                }
                foreach (string unit in s_optionalMemberUnits)
                {
                    if (!unit.StartsWith($"{prefix} ", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string suffix = unit.Substring(prefix.Length + 1);
                    string[] fields = suffix switch
                    {
                        "Peak" => ["PeakWidth", "PeakHeight", "PeakArea", "TailingFactor"],
                        "RetentionTime" => ["ExpectedRetentionTime", "ActualRetentionTime"],
                        "RangeExceedance" => ["RangeExceedancePeakValue", "RangeExceedanceDuration"],
                        "Valve" => ["ValveName", "ValveSwitchingCyclesCounter"],
                        "CalibrationRange1" or "CalibrationRange2" or "CalibrationRange3" =>
                            [$"{suffix}ResponseFactor", $"{suffix}LowerRangeValue", $"{suffix}UpperRangeValue"],
                        "Connection" => ["SensorConnection"],
                        "Reference" => ["SensorReference"],
                        _ => [suffix]
                    };
                    if (HasBoundFields(scope, fields))
                    {
                        Add(facet, unit);
                    }
                }
            }

            void AddProcessOptions(string facet, BaseVariableState? variable)
            {
                if (variable == null)
                {
                    return;
                }
                if (HasBoundFields(variable, "Damping"))
                {
                    Add(facet, "AnalogSignalVariable Damping");
                }
                if (HasBoundFields(variable, "PatMeasurandDescription"))
                {
                    units.Add("PA-DIM PatMeasurementVariableType");
                }
                if (HasBoundFields(variable, "PatMatrixDescription"))
                {
                    Add(facet, "PatMeasurementVariableType PatMatrixDescription");
                }
                AddOptionalMembers(facet, variable is MultiStateDiscreteControlVariableState
                    ? "MultiStateDiscreteControlVariable" : "TwoStateDiscreteControlVariable", variable);
            }
        }

        /// <summary>
        /// Resolves a declared PA-DIM member by its namespace URI rather than the source NodeSet index.
        /// </summary>
        private BaseInstanceState? FindMember(NodeState owner, string name)
        {
            return FindMember(owner, name, Namespaces.PADIM);
        }

        private BaseInstanceState? FindMember(NodeState owner, string name, string namespaceUri)
        {
            int namespaceIndex = Context.NamespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex < 0)
            {
                return null;
            }
            var browseName = new QualifiedName(name, (ushort)namespaceIndex);
            var children = new List<BaseInstanceState>();
            owner.GetChildren(Context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child.BrowseName == browseName)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks the configured vendor or published subtype using URI-remapped model identifiers.
        /// </summary>
        private bool HasType(BaseInstanceState instance, ExpandedNodeId required)
        {
            return Context.TypeTable.IsTypeOf(
                instance.TypeDefinitionId, ExpandedNodeId.ToNodeId(required, Context.NamespaceUris));
        }

        /// <summary>
        /// Checks instance and inherited type interfaces, including specialised calibration interfaces.
        /// </summary>
        private bool Implements(BaseObjectState instance, ExpandedNodeId required)
        {
            NodeId type = ExpandedNodeId.ToNodeId(required, Context.NamespaceUris);
            var visited = new HashSet<NodeId>();
            NodeState? node = instance;
            NodeId typeId = instance.TypeDefinitionId;
            while (node != null)
            {
                var references = new List<IReference>();
                node.GetReferences(Context, references);
                foreach (IReference reference in references)
                {
                    if (!reference.IsInverse &&
                        reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.HasInterface &&
                        Context.TypeTable.IsTypeOf(
                            ExpandedNodeId.ToNodeId(reference.TargetId, Context.NamespaceUris), type))
                    {
                        return true;
                    }
                }
                if (typeId.IsNull || !visited.Add(typeId))
                {
                    break;
                }
                node = Manager.FindPredefinedNode(typeId);
                typeId = Context.TypeTable.FindSuperType(typeId);
            }
            return false;
        }

        /// <summary>
        /// Checks the populated identity and mandatory base values of a PA-DIM device.
        /// </summary>
        private bool HasIdentity()
        {
            return HasLocalizedText(State, "Manufacturer") &&
                HasStringValue(State, "ManufacturerUri") &&
                HasLocalizedText(State, "Model") &&
                HasStringValue(State, "SerialNumber") &&
                HasStringValue(State, "SoftwareRevision") &&
                HasStringValue(State, "HardwareRevision") &&
                HasStringValue(State, "ProductCode") &&
                HasStringValue(State, "ProductInstanceUri") &&
                HasStringValue(State, "AssetId") &&
                HasInt32Value(State, "RevisionCounter");
        }

        /// <summary>
        /// Checks the host's configured profile declaration for a PA-DIM-supported Nano Embedded profile.
        /// </summary>
        private bool SupportsNanoEmbeddedProfile()
        {
            BaseVariableState? profileArray =
                Manager.Server.DiagnosticsNodeManager.FindPredefinedNode<BaseVariableState>(
                    Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray);
            if (profileArray == null ||
                !profileArray.Value.TryGetValue(out ArrayOf<string> profiles) ||
                profiles.IsNull)
            {
                return false;
            }
            for (int ii = 0; ii < profiles.Count; ii++)
            {
                if (profiles[ii] is
                    "http://opcfoundation.org/UA-Profile/Server/NanoEmbeddedDevice2017" or
                    "http://opcfoundation.org/UA-Profile/Server/NanoEmbeddedDevice2022" or
                    "http://opcfoundation.org/UA-Profile/Server/MicroEmbeddedDevice2017" or
                    "http://opcfoundation.org/UA-Profile/Server/MicroEmbeddedDevice2022" or
                    "http://opcfoundation.org/UA-Profile/Server/EmbeddedUA2017" or
                    "http://opcfoundation.org/UA-Profile/Server/EmbeddedUA2022" or
                    "http://opcfoundation.org/UA-Profile/Server/StandardUA2017" or
                    "http://opcfoundation.org/UA-Profile/Server/StandardUA2022")
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasLocalizedText(NodeState owner, string name)
        {
            return FindMember(owner, name, Di.Namespaces.OpcUaDi) is BaseVariableState variable &&
                variable.WrappedValue.TryGetValue(out LocalizedText value) &&
                !string.IsNullOrWhiteSpace(value.Text);
        }

        private bool HasStringValue(NodeState owner, string name)
        {
            return FindMember(owner, name, Di.Namespaces.OpcUaDi) is BaseVariableState variable &&
                variable.WrappedValue.TryGetValue(out string value) &&
                !string.IsNullOrWhiteSpace(value);
        }

        private bool HasInt32Value(NodeState owner, string name)
        {
            return FindMember(owner, name, Di.Namespaces.OpcUaDi) is BaseVariableState variable &&
                variable.WrappedValue.TryGetValue(out int _);
        }

        /// <summary>Published optional member units with names relative to their owning condition or value.</summary>
        private static readonly string[] s_optionalMemberUnits =
        [
            "AmperometricGasDetectorSignalType ConsumedSensorCapacity",
            "AmperometricGasDetectorSignalType PowerOnDurationSensor",
            "AmperometricGasDetectorSignalType RangeExceedance",
            "AmperometricGasDetectorSignalType RelativeGasFlowRate",
            "AmperometricGasDetectorSignalType SensingElementResidualLife",
            "AmperometricGasDetectorSignalType SensingElementResidualSensitivity",
            "AmperometricGasDetectorSignalType SensorNextCalibrationDynamic",
            "AmperometricGasDetectorSignalType SensorNextCalibrationFixed",
            "AmperometricSignalType SensorCleaningsCounter",
            "AmperometricSignalType SensorNextCalibration",
            "AmperometricSignalType SensorSterilisationsCounter",
            "CatalyticBeadSignalType PowerOnDurationSensor",
            "CatalyticBeadSignalType RangeExceedance",
            "CatalyticBeadSignalType RelativeGasFlowRate",
            "CatalyticBeadSignalType SensingElementResidualLife",
            "CatalyticBeadSignalType SensingElementResidualSensitivity",
            "CatalyticBeadSignalType SensorNextCalibrationDynamic",
            "CatalyticBeadSignalType SensorNextCalibrationFixed",
            "CatalyticBeadSignalType SensorValue",
            "ConductivitySignalType SensorCleaningsCounter",
            "ConductivitySignalType SensorSterilisationsCounter",
            "DiodeArraySignalType ElectronicsReadNoise",
            "DiodeArraySignalType MahalanobisDistance",
            "DiodeArraySignalType SourceResidualLife",
            "DiodeArraySignalType SpectralResidual",
            "DiodeArraySpectrometerType RemainingDataStorageCapacity",
            "DiodeArraySpectrometerType Watchdog",
            "FlameIonisationDetectorType BlockTemperature",
            "FlameIonisationDetectorType CatalystTemperature",
            "FlameIonisationDetectorType CombustionAirPressure",
            "FlameIonisationDetectorType FuelGasPressure",
            "FlowMeasurementVariable FlowDirection",
            "FtnirOrFtirSignalType ElectronicsReadNoise",
            "FtnirOrFtirSignalType LaserResidualLife",
            "FtnirOrFtirSignalType MahalanobisDistance",
            "FtnirOrFtirSignalType SpectralResidual",
            "FtnirOrFtirSignalType TransmissionRatio",
            "FtnirOrFtirSpectrometerType RemainingDataStorageCapacity",
            "FtnirOrFtirSpectrometerType Watchdog",
            "GasChromatograph CalibrationRange1",
            "GasChromatograph CalibrationRange2",
            "GasChromatograph CalibrationRange3",
            "GasChromatographSignalType ComponentName",
            "GasChromatographSignalType InjectionTime",
            "GasChromatographSignalType Peak",
            "GasChromatographSignalType RetentionTime",
            "GasChromatographType BaselineNoise",
            "GasChromatographType TotalAreaMeasuredPeaks",
            "GasChromatographType Valve",
            "IAmperometricCalibration AbsoluteAirPressure",
            "IAmperometricCalibration SensorT90",
            "InfraredSignalType PowerOnDurationSensor",
            "InfraredSignalType SensingElementResidualLife",
            "InfraredSignalType SensorNextCalibrationDynamic",
            "InfraredSignalType SensorNextCalibrationFixed",
            "InfraredSignalType SourceResidualLife",
            "InfraredSignalType TransmissionRatio",
            "IOpticalFluorescenseQuenchingCalibration AbsoluteAirPressure",
            "IOpticalFluorescenseQuenchingCalibration SensorT90",
            "IPhCalibration SensorT90",
            "MultiStateDiscreteControlVariable FaultState",
            "MultiStateDiscreteControlVariable OperatingDirection",
            "NonDispersiveInfraredSignalType AbsoluteSampleGasPressure",
            "NonDispersiveInfraredSignalType ChopperFrequencyDeviation",
            "OpticalFluorescenseQuenchingSignalType SensorCleaningsCounter",
            "OpticalFluorescenseQuenchingSignalType SensorNextCalibration",
            "OpticalFluorescenseQuenchingSignalType SensorSterilisationsCounter",
            "PhSignalType SensingElementImpedance",
            "PhSignalType SensorCleaningsCounter",
            "PhSignalType SensorNextCalibration",
            "PhSignalType SensorReferenceImpedance",
            "PhSignalType SensorSterilisationsCounter",
            "RamanSignalType ElectronicsReadNoise",
            "RamanSignalType MahalanobisDistance",
            "RamanSignalType SensingElementTemperature",
            "RamanSignalType SourceResidualLife",
            "RamanSignalType SpectralResidual",
            "RamanSpectrometerType RemainingDataStorageCapacity",
            "RamanSpectrometerType Watchdog",
            "TemperatureMeasurementVariable Connection",
            "TemperatureMeasurementVariable Reference",
            "TemperatureMeasurementVariable SensorClass",
            "TocAnalyserType ActualInjectedVolume",
            "TocAnalyserType CarrierGasGaugePressure",
            "TocAnalyserType SampleWaterVolumeFlow",
            "TocSignalType AbsoluteSampleGasPressure",
            "TocSignalType ChopperFrequencyDeviation",
            "TocSignalType RelativeReagentLevel",
            "TocSignalType SampleGasVolumeFlow",
            "TunableDiodeLaserSignalType AbsoluteSampleGasPressure",
            "TunableDiodeLaserSignalType LaserTemperature",
            "TunableDiodeLaserSignalType SignalFitQuality",
            "TunableDiodeLaserSignalType SignalNoiseRatio",
            "TunableDiodeLaserSignalType TransmissionRatio",
            "TwoStateDiscreteControlVariable FaultState",
            "ZirconiumDioxideSignalType CellResistance",
            "ZirconiumDioxideSignalType RelativeHeatOutput",
            "ZirconiumDioxideSignalType SampleGasVolumeFlow",
            "ZirconiumDioxideSignalType SensingElementResidualLife"
        ];

        /// <summary>Device-family facet suffixes from the released PADIM 1.02 catalogue.</summary>
        private static readonly (PadimDeviceKind Kind, string Facet)[] s_deviceFacets =
        [
            (PadimDeviceKind.AmperometricAnalyser, "AmperometricAnalyser"),
            (PadimDeviceKind.ConductivityMeter, "ConductivityMeter"),
            (PadimDeviceKind.FlameIonisationDetector, "FlameIonisationDetector"),
            (PadimDeviceKind.NonDispersiveInfraredGasAnalyser, "NDIRAnalyser"),
            (PadimDeviceKind.OpticalFluorescenseQuenchingSensor, "FluorescenseSensor"),
            (PadimDeviceKind.ParamagneticGasAnalyser, "ParamagneticAnalyser"),
            (PadimDeviceKind.PhMeter, "pHMeter"),
            (PadimDeviceKind.ThermalConductivityGasAnalyser, "ConductivityAnalyser"),
            (PadimDeviceKind.TocAnalyser, "TOCAnalyser"),
            (PadimDeviceKind.TunableDiodeLaserSpectrometer, "TDLSpectrometer"),
            (PadimDeviceKind.ZirconiumDioxideAnalyser, "ZrO2Analyser"),
            (PadimDeviceKind.GasChromatograph, "GasChromatograph"),
            (PadimDeviceKind.InfraredSensor, "InfraredSensor"),
            (PadimDeviceKind.AmperometricGasDetector, "AmperometricGasDetector"),
            (PadimDeviceKind.CatalyticBeadSensor, "CatalyticBeadSensor"),
            (PadimDeviceKind.DiodeArraySpectrometer, "DiodeArraySpectrometer"),
            (PadimDeviceKind.RamanSpectrometer, "RamanSpectrometer"),
            (PadimDeviceKind.FtnirOrFtirSpectrometer, "FtnirOrFtirSpectrometer")
        ];

        /// <summary>Published signal-family facets and their distinguishing mandatory unit.</summary>
        private static readonly (ExpandedNodeId Type, string Facet, string Unit, bool Analytical)[] s_signalFacets =
        [
            (ObjectTypeIds.ControlSignalType, "ControlSignal", "Control Signal", false),
            (ObjectTypeIds.DiscreteSignalType, "DiscreteSignal", "Discrete Signal", false),
            (ObjectTypeIds.TwoStateDiscreteSignalType, "TwoStateDiscreteSignal", "Two State Discrete Signal", false),
            (ObjectTypeIds.MultiStateDiscreteSignalType, "MultiStateDiscreteSignal",
                "Multi State Discrete Signal", false),
            (ObjectTypeIds.TwoStateDiscreteControlSignalType, "TwoStateControlSignal",
                "TwoStateDiscreteControl Signal", false),
            (ObjectTypeIds.MultiStateDiscreteControlSignalType, "MultiStateControlSignal",
                "MultiStateDiscreteControl Signal", false),
            (ObjectTypeIds.AmperometricSignalType, "AmperometricSignal", "AmperometricSignalType", true),
            (ObjectTypeIds.ConductivitySignalType, "ConductivitySignal", "ConductivitySignalType", true),
            (ObjectTypeIds.FlameIonisationSignalType, "FlameIonisationSIgnal", "FlameIonisationSignalType", true),
            (ObjectTypeIds.NonDispersiveInfraredSignalType, "NDIRSignal", "NonDispersiveInfraredSignalType", true),
            (ObjectTypeIds.OpticalFluorescenseQuenchingSignalType, "FluorescenseSignal",
                "OpticalFluorescenseQuenchingSignalType", true),
            (ObjectTypeIds.ParamagneticSignalType, "ParamagneticSignal", "ParamagneticSignalType", true),
            (ObjectTypeIds.PhSignalType, "pHSignal", "PhSignalType", true),
            (ObjectTypeIds.ThermalConductivitySignalType, "ThermalConductivitySignal",
                "ThermalConductivitySignalType", true),
            (ObjectTypeIds.TocSignalType, "TOCSignal", "TocSignalType", true),
            (ObjectTypeIds.TunableDiodeLaserSignalType, "TDLSignal", "TunableDiodeLaserSignalType", true),
            (ObjectTypeIds.ZirconiumDioxideSignalType, "ZrO2Signal", "ZirconiumDioxideSignalType", true),
            (ObjectTypeIds.AmperometricGasDetectorSignalType, "AmperometricGasDetectorSignal",
                "AmperometricGasDetectorSignalType", true),
            (ObjectTypeIds.GasChromatographSignalType, "GasChromatographSignal", "GasChromatographSignalType", true),
            (ObjectTypeIds.FtnirOrFtirSignalType, "FtnirOrFtirSignal", "FtnirOrFtirSignalType", true),
            (ObjectTypeIds.DiodeArraySignalType, "DiodeArraySignal", "DiodeArraySignalType", true),
            (ObjectTypeIds.RamanSignalType, "RamanSignal", "RamanSignalType", true),
            (ObjectTypeIds.InfraredSignalType, "InfraredSignal", "InfraredSignalType", true),
            (ObjectTypeIds.InfraredSignalType, "InfraredSensorSignal", "InfraredSignalType", true),
            (ObjectTypeIds.CatalyticBeadSignalType, "CatalyticBeadSignal", "CatalyticBeadSignalType", true)
        ];

        /// <summary>Measurement specialisations associated with the released process-signal facets.</summary>
        private static readonly (ExpandedNodeId Type, string Facet, string Unit)[] s_measurementFacets =
        [
            (VariableTypeIds.TemperatureMeasurementVariableType, "TemperatureSignal", "TemperatureMeasurementVariable"),
            (VariableTypeIds.PressureMeasurementVariableType, "PressureSignal", "PressureMeasurementVariable"),
            (VariableTypeIds.LevelMeasurementVariableType, "LevelSignal", "LevelMeasurementVariable"),
            (VariableTypeIds.ActualDensityVariableType, "DensitySignal", "ActualDensityVariable"),
            (VariableTypeIds.TotalizerVariableType, "TotalizerSignal", "TotalizerVariable"),
            (VariableTypeIds.FlowMeasurementVariableType, "VolumeFlowSignal", "FlowMeasurementVariable"),
            (VariableTypeIds.MassFlowRateVariableType, "MassFlowSignal", "MassFlowRateVariable")
        ];
    }
}
