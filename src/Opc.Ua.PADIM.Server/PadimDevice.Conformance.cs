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
            bool health = false;
            var signals = new HashSet<SignalState>();
            foreach (PadimValueBinding binding in bindings)
            {
                BaseVariableState variable = binding.Variable;
                health |= ReferenceEquals(variable, State.DeviceHealth);
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
                            "TwoStateDiscreteSignal" or "MultiStateDiscreteSignal")
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
                        if (calibrationPoint)
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
                        Add("BaseDeviceCondition", "Device Condition Set", "IGeneralDeviceConditionSet");
                        if (componentConditions)
                        {
                            Add("BaseDeviceCondition", "IGeneralDeviceConditionSet DeviceComponentConditions");
                        }
                    }
                    ancestor = instance.Parent;
                }
                AddMeasurement(variable);
            }
            if (health && HasIdentity())
            {
                Add("Core", "Base", "Nano Embedded");
                Add("BaseDeviceCondition");
                foreach ((PadimDeviceKind kind, string suffix) in s_deviceFacets)
                {
                    if (HasType(State, PadimFactories.DeviceType(kind)))
                    {
                        Add(suffix, "ProcessAnalyserType", $"{kind}Type");
                    }
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
                    if (!HasType(signal, type))
                    {
                        continue;
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
                if (variable.Parent is not AnalogSignalObjectState)
                {
                    return;
                }
                foreach ((ExpandedNodeId type, string suffix, string unit) in s_measurementFacets)
                {
                    if (HasType(variable, type))
                    {
                        Add(suffix, "Analog Signal", "AnalogSignalVariableType", unit);
                        Add("BaseSignalCalibration");
                    }
                }
            }
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
        /// Requires actual manufacturer, model and serial identity before advertising the device core facet.
        /// </summary>
        private bool HasIdentity()
        {
            return !string.IsNullOrWhiteSpace(State.Manufacturer?.Value.Text) &&
                !string.IsNullOrWhiteSpace(State.Model?.Value.Text) &&
                !string.IsNullOrWhiteSpace(State.SerialNumber?.Value);
        }

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
