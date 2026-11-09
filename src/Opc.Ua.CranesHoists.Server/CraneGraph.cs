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
using Opc.Ua.Di;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Server.Builders;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Materializes selected crane declarations inside the owning Robotics registration scope.
    /// </summary>
    internal sealed class CraneGraph
    {
        /// <summary>
        /// Captures the real factory-created root and its shared owner.
        /// </summary>
        internal CraneGraph(CraneSystemBuilder definition, IMotionDeviceSystemBuilder system)
        {
            m_definition = definition;
            m_system = system;
            Context = system.BuildContext.Context;
            State = (CraneMotionDeviceSystemState)system.State;
            Elements.Add(definition.Id, (State, CraneElementKind.System));
        }

        /// <summary>
        /// Gets the system context.
        /// </summary>
        internal ISystemContext Context { get; }

        /// <summary>
        /// Gets the native crane root.
        /// </summary>
        internal CraneMotionDeviceSystemState State { get; }

        /// <summary>
        /// Gets actual generated element states by system-local provider identity.
        /// </summary>
        internal Dictionary<string, (BaseObjectState State, CraneElementKind Kind)> Elements { get; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Gets only materialized signal nodes.
        /// </summary>
        internal Dictionary<CraneSignalAddress, CraneSignalBinding> Signals { get; } = [];

        /// <summary>
        /// Materializes the selected graph without registering any node itself.
        /// </summary>
        internal void Materialize()
        {
            AddIdentification(State.Identification!, m_definition.Identification!);
            AddSlot(m_definition.Id, CraneSignal.OperationalMode,
                Parameter(State.ParameterSet!, CraneElementKind.System, CraneSignal.OperationalMode));
            AddSlot(m_definition.Id, CraneSignal.SystemOn,
                Parameter(State.ParameterSet!, CraneElementKind.System, CraneSignal.SystemOn));
            if (m_definition.ExternalControl)
            {
                AddParameter(m_definition.Id, CraneSignal.ExternalControlRequest, State.ParameterSet!,
                    Context.CreateCraneMotionDeviceSystemType_ParameterSet_ExternalControlRequest);
            }
            if (m_definition.GlobalSpeedLimit)
            {
                AddParameter(m_definition.Id, CraneSignal.SpeedLimitGlobalEnabled, State.ParameterSet!,
                    Context.CreateCraneMotionDeviceSystemType_ParameterSet_SpeedLimitGlobalEnabled);
            }

            var devices = new List<IMotionDeviceBuilder>();
            foreach (CraneDeviceBuilder definition in m_definition.Devices)
            {
                IMotionDeviceBuilder device = m_system.AddMotionDevice(
                    InstanceName(definition.Id),
                    static (context, parent, name) => context.CreateInstanceOfCraneMotionDeviceType(parent, name));
                devices.Add(device);
                var state = (CraneMotionDeviceState)device.State;
                Elements.Add(definition.Id, (state, CraneElementKind.MotionDevice));
                // DWP has two parents in the released NodeSet. The generated ParameterSet
                // retains its declaration-level alias, which must not expose a type-template
                // value as this device's optional runtime measurement.
                state.ParameterSet!.RemoveReference(Opc.Ua.ReferenceTypeIds.HasComponent, false,
                    NodeId.Create(Variables.CraneMotionDeviceType_Operational_DesignedWorkingPeriod,
                        CranesHoistsModel.NamespaceUri, Context.NamespaceUris));
                MachineryIdentificationData identity = AddIdentification(
                    state.Identification!, definition.Identification!);
                device.WithIdentification(data => CopyIdentity(identity, data));
                state.CraneMotionDeviceCategory!.Value = definition.Category;
                AddStatic(definition.Id, CraneSignal.MotionDeviceCategory, state.CraneMotionDeviceCategory,
                    Variant.From(definition.Category));
                AddSlot(definition.Id, CraneSignal.SpeedOverride,
                    Parameter(state.ParameterSet!, CraneElementKind.MotionDevice, CraneSignal.SpeedOverride));
                if (definition.WorkingPeriod)
                {
                    // The released NodeSet declares this projection under Operational; Table 15 also
                    // permits the same variable under ParameterSet, organized by the functional group.
                    BaseVariableState period = AddParameter(
                        definition.Id, CraneSignal.DesignedWorkingPeriod, state.ParameterSet!,
                        Context.CreateCraneMotionDeviceType_Operational_DesignedWorkingPeriod);
                    period.RemoveReference(Opc.Ua.ReferenceTypeIds.HasComponent, true,
                        NodeId.Create(Objects.CraneMotionDeviceType_ParameterSet,
                            CranesHoistsModel.NamespaceUri, Context.NamespaceUris));
                    SetPercentUnits(period);
                }
                foreach (CraneAxisBuilder axis in definition.Axes)
                {
                    AddAxis(device, axis);
                }
                if (definition.Components)
                {
                    state.CreateOrReplaceComponents(
                        Context, Context.CreateCraneMotionDeviceType_Components(state, true));
                    AddComponentsReferences(state.Components!, definition.Id);
                }
            }

            var safeties = new List<ISafetyStateBuilder>();
            foreach (string id in m_definition.SafetyStates)
            {
                ISafetyStateBuilder safety = m_system.AddSafetyState(id);
                safeties.Add(safety);
                foreach (CraneSignal signal in new[]
                {
                    CraneSignal.EmergencyStop, CraneSignal.ProtectiveStop, CraneSignal.SafetyOperationalMode
                })
                {
                    AddSlot(id, signal, Parameter(safety.State.ParameterSet!, CraneElementKind.System, signal));
                }
            }
            foreach ((string id, Action<IControllerBuilder> configure) in m_definition.Controllers)
            {
                IControllerBuilder controller = m_system.AddController(id, configure);
                foreach (IMotionDeviceBuilder device in devices)
                {
                    controller.Controls(device);
                }
                foreach (ISafetyStateBuilder safety in safeties)
                {
                    controller.UsesSafetyState(safety);
                }
            }
            foreach (CraneProtectionBuilder protection in m_definition.Protections)
            {
                AddProtection(protection);
            }
            foreach (CraneDeviceBuilder device in m_definition.Devices)
            {
                foreach (string child in device.PhysicalChildren)
                {
                    Link(Elements[device.Id].State,
                        Opc.Ua.ReferenceTypeIds.HasPhysicalComponent, Elements[child].State);
                }
            }
            if (m_definition.Components)
            {
                State.CreateOrReplaceComponents(
                    Context, Context.CreateCraneMotionDeviceSystemType_Components(State, true));
                foreach (CraneDeviceBuilder device in m_definition.Devices)
                {
                    Link(State.Components!, Opc.Ua.ReferenceTypeIds.HasComponent, Elements[device.Id].State);
                }
            }
            if (m_definition.FunctionalGroups)
            {
                foreach (KeyValuePair<string, (BaseObjectState State, CraneElementKind Kind)> entry in Elements)
                {
                    AddFunctionalGroups(entry.Key, entry.Value.State);
                }
            }
        }

        /// <summary>
        /// Reuses the shared nameplate writer and building-block organization for every selected identity.
        /// </summary>
        internal void BindIdentifications(IMachineBuilder<CraneMotionDeviceSystemState> machine)
        {
            foreach ((MachineryItemIdentificationState state, MachineryIdentificationData data) in m_identifications)
            {
                machine.WithIdentification(state, target => CopyIdentity(data, target));
            }
        }

        /// <summary>
        /// Stages a native axis through Robotics validation and links its standard power train.
        /// </summary>
        private void AddAxis(IMotionDeviceBuilder device, CraneAxisBuilder definition)
        {
            IAxisBuilder builder = device.AddAxis(InstanceName(definition.Id),
                static (context, parent, name) => context.CreateInstanceOfCraneAxisType(parent, name));
            builder.WithMotionProfile(definition.MotionProfile);
            var axis = (CraneAxisState)builder.State;
            Elements.Add(definition.Id, (axis, CraneElementKind.Axis));
            AddStatic(definition.Id, CraneSignal.ActualPositionCapability,
                Parameter(axis.ParameterSet!, CraneElementKind.Axis, CraneSignal.ActualPositionCapability),
                Variant.From(definition.PositionAvailable));
            BaseVariableState position = Parameter(
                axis.ParameterSet!, CraneElementKind.Axis, CraneSignal.ActualPosition);
            if (definition.PositionAvailable)
            {
                AddSlot(definition.Id, CraneSignal.ActualPosition, position);
            }
            else
            {
                AddStatic(definition.Id, CraneSignal.ActualPosition, position, Variant.Null);
                position.StatusCode = StatusCodes.BadNoData;
            }
            if (definition.AxisFunctions)
            {
                AddParameter(definition.Id, CraneSignal.AxisFunction, axis.ParameterSet!,
                    Context.CreateCraneAxisType_ParameterSet_CraneAxisFunction);
            }
            if (definition.Moving)
            {
                AddParameter(definition.Id, CraneSignal.IsMoving, axis.ParameterSet!,
                    Context.CreateCraneAxisType_ParameterSet_IsMoving);
            }
            if (definition.RatedSpeed.HasValue)
            {
                BaseVariableState speed = AddParameter(definition.Id, CraneSignal.RatedSpeed, axis.ParameterSet!,
                    Context.CreateCraneAxisType_ParameterSet_RatedSpeed, telemetry: false);
                speed.WrappedValue = Variant.From(definition.RatedSpeed.Value);
                ((AnalogUnitState<double>)speed).EngineeringUnits!.Value = definition.SpeedUnits!;
            }
            if (definition.SpeedLimits)
            {
                AddAxisLimits(definition.Id, axis.ParameterSet!);
            }
            if (definition.ActualLoad)
            {
                axis.CreateOrReplaceActualLoad(Context, Context.CreateCraneAxisType_ActualLoad(axis, true));
                definition.ConfigureLoad?.Invoke(axis.ActualLoad!, Context);
                AddSlot(definition.Id, CraneSignal.LoadMass, axis.ActualLoad!.Mass!);
                if (axis.ActualLoad.CenterOfMass != null)
                {
                    AddSlot(definition.Id, CraneSignal.LoadCenterOfMass, axis.ActualLoad.CenterOfMass);
                }
                if (axis.ActualLoad.Inertia != null)
                {
                    AddSlot(definition.Id, CraneSignal.LoadInertia, axis.ActualLoad.Inertia);
                }
            }
            IPowerTrainBuilder train = device.AddPowerTrain(definition.Id + "PowerTrain");
            train.Moves(builder);
            builder.Requires(train);
            foreach (CraneMotorBuilder motor in definition.Motors)
            {
                AddMotor(train, motor);
            }
            foreach (CraneBrakeDefinition brake in definition.Brakes)
            {
                BrakeState state = Context.CreateInstanceOfBrakeType(axis, InstanceName(brake.Id));
                state.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                axis.AddChild(state);
                Elements.Add(brake.Id, (state, CraneElementKind.Brake));
                AddSlot(brake.Id, CraneSignal.BrakeReleased,
                    Parameter(state.ParameterSet!, CraneElementKind.Brake, CraneSignal.BrakeReleased));
                if (brake.Identification != null)
                {
                    state.CreateOrReplaceIdentification(
                        Context, Context.CreateBrakeType_Identification(state, true));
                    AddIdentification(state.Identification!, brake.Identification);
                }
            }
        }

        /// <summary>
        /// Creates the six standard directional limit variables.
        /// </summary>
        private void AddAxisLimits(string id, BaseObjectState parameters)
        {
            SetPercentUnits(AddParameter(id, CraneSignal.SpeedLimitDirMinus, parameters,
                Context.CreateCraneAxisType_ParameterSet_SpeedLimitDirMinus));
            SetPercentUnits(AddParameter(id, CraneSignal.SpeedLimitDirPlus, parameters,
                Context.CreateCraneAxisType_ParameterSet_SpeedLimitDirPlus));
            SetPercentUnits(AddParameter(id, CraneSignal.SpeedLimitDirMinusSetpoint, parameters,
                Context.CreateCraneAxisType_ParameterSet_SpeedLimitDirMinusSetpoint));
            SetPercentUnits(AddParameter(id, CraneSignal.SpeedLimitDirPlusSetpoint, parameters,
                Context.CreateCraneAxisType_ParameterSet_SpeedLimitDirPlusSetpoint));
            AddParameter(id, CraneSignal.SpeedLimitEnabledDirMinus, parameters,
                Context.CreateCraneAxisType_ParameterSet_SpeedLimitEnabledDirMinus);
            AddParameter(id, CraneSignal.SpeedLimitEnabledDirPlus, parameters,
                Context.CreateCraneAxisType_ParameterSet_SpeedLimitEnabledDirPlus);
        }

        /// <summary>
        /// Creates a native crane motor while preserving the inherited temperature and optional service brake.
        /// </summary>
        private void AddMotor(IPowerTrainBuilder train, CraneMotorBuilder definition)
        {
            IMotorBuilder builder = train.AddMotor(InstanceName(definition.Id),
                static (context, parent, name) => context.CreateInstanceOfCraneMotorType(parent, name));
            var motor = (CraneMotorState)builder.State;
            Elements.Add(definition.Id, (motor, CraneElementKind.Motor));
            AddSlot(definition.Id, CraneSignal.MotorTemperature,
                Parameter(motor.ParameterSet!, CraneElementKind.Motor, CraneSignal.MotorTemperature));
            if (definition.ServiceBrake)
            {
                builder.WithBrakeReleased(false, StatusCodes.BadWaitingForInitialData);
                AddSlot(definition.Id, CraneSignal.BrakeReleased,
                    Parameter(motor.ParameterSet!, CraneElementKind.Motor, CraneSignal.BrakeReleased));
            }
            if (definition.Overheated)
            {
                AddParameter(definition.Id, CraneSignal.Overheated, motor.ParameterSet!,
                    Context.CreateCraneMotorType_ParameterSet_Overheated);
            }
            if (definition.Identification != null)
            {
                motor.CreateOrReplaceIdentification(Context, Context.CreateCraneMotorType_Identification(motor, true));
                MachineryIdentificationData identity = AddIdentification(
                    motor.Identification!, definition.Identification);
                builder.WithIdentification(data => CopyIdentity(identity, data));
            }
        }

        /// <summary>
        /// Materializes only explicitly selected protection members and direction-correct references.
        /// </summary>
        private void AddProtection(CraneProtectionBuilder definition)
        {
            if (State.ProtectiveFunctions == null)
            {
                State.CreateOrReplaceProtectiveFunctions(
                    Context, Context.CreateCraneMotionDeviceSystemType_ProtectiveFunctions(State, true));
            }
            ProtectiveFunctionState state = Context.CreateInstanceOfProtectiveFunctionType(
                State.ProtectiveFunctions!, InstanceName(definition.Id));
            state.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            State.ProtectiveFunctions!.AddChild(state);
            Elements.Add(definition.Id, (state, CraneElementKind.ProtectiveFunction));
            if (definition.StateMonitoring)
            {
                AddParameter(definition.Id, CraneSignal.Enabled, state.ParameterSet!,
                    Context.CreateProtectiveFunctionType_ParameterSet_Enabled);
                AddParameter(definition.Id, CraneSignal.Active, state.ParameterSet!,
                    Context.CreateProtectiveFunctionType_ParameterSet_Active);
            }
            if (definition.Mode.HasValue)
            {
                state.CreateOrReplaceName(Context, Context.CreateProtectiveFunctionType_Name(state, true));
                state.CreateOrReplaceProtectiveFunctionMode(
                    Context, Context.CreateProtectiveFunctionType_ProtectiveFunctionMode(state, true));
                state.CreateOrReplaceRampDown(Context, Context.CreateProtectiveFunctionType_RampDown(state, true));
                AddStatic(definition.Id, CraneSignal.ProtectionName, state.Name!, Variant.From(definition.Name!));
                AddStatic(definition.Id, CraneSignal.ProtectiveFunctionMode, state.ProtectiveFunctionMode!,
                    Variant.From(definition.Mode.Value));
                AddStatic(definition.Id, CraneSignal.RampDown,
                    state.RampDown!, Variant.From(definition.RampDown!.Value));
            }
            if (!definition.Directions.IsNull)
            {
                AddDirection(CraneSignal.DirMinusSlowdown, definition.Directions[0],
                    Context.CreateProtectiveFunctionType_ParameterSet_DirMinusSlowdown);
                AddDirection(CraneSignal.DirMinusStop, definition.Directions[1],
                    Context.CreateProtectiveFunctionType_ParameterSet_DirMinusStop);
                AddDirection(CraneSignal.DirPlusSlowdown, definition.Directions[2],
                    Context.CreateProtectiveFunctionType_ParameterSet_DirPlusSlowdown);
                AddDirection(CraneSignal.DirPlusStop, definition.Directions[3],
                    Context.CreateProtectiveFunctionType_ParameterSet_DirPlusStop);
            }
            if (definition.Identification != null)
            {
                state.CreateOrReplaceIdentification(
                    Context, Context.CreateProtectiveFunctionType_Identification(state, true));
                AddIdentification(state.Identification!, definition.Identification);
            }
            NodeId protects = NodeId.Create(
                ReferenceTypes.ProtectsAxis, CranesHoistsModel.NamespaceUri, Context.NamespaceUris);
            foreach (string axis in definition.Axes)
            {
                Link(state, protects, Elements[axis].State);
            }

            void AddDirection(CraneSignal signal, bool value, Func<NodeState, bool, BaseVariableState> create)
            {
                BaseVariableState variable = AddParameter(
                    definition.Id, signal, state.ParameterSet!, create, telemetry: false);
                variable.WrappedValue = Variant.From(value);
            }
        }

        /// <summary>
        /// Creates one native optional variable and allocates only its new subtree.
        /// </summary>
        private BaseVariableState AddParameter(
            string id,
            CraneSignal signal,
            BaseObjectState parent,
            Func<NodeState, bool, BaseVariableState> create,
            bool telemetry = true)
        {
            BaseVariableState state = create(parent, true);
            state.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            parent.AddChild(state);
            NodeId previous = Context.AssignInstanceNodeId(state);
            Context.AssignInstanceChildNodeIds(state, previous);
            AddSlot(id, signal, state, telemetry);
            return state;
        }

        /// <summary>
        /// Records a real variable and never marks an unacquired telemetry value Good.
        /// </summary>
        private void AddSlot(string id, CraneSignal signal, BaseVariableState variable, bool telemetry = true)
        {
            bool request = CraneSignals.IsRequest(signal);
            var address = new CraneSignalAddress(id, signal);
            Signals.Add(address, new CraneSignalBinding(address, variable, telemetry && !request, request));
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            if (telemetry || request)
            {
                variable.WrappedValue = Variant.Null;
                variable.StatusCode = StatusCodes.BadWaitingForInitialData;
            }
        }

        /// <summary>
        /// Records configuration-derived metadata, not a simulated measurement.
        /// </summary>
        private void AddStatic(string id, CraneSignal signal, BaseVariableState variable, Variant value)
        {
            AddSlot(id, signal, variable, telemetry: false);
            variable.WrappedValue = value;
            variable.StatusCode = StatusCodes.Good;
        }

        /// <summary>
        /// Resolves mandatory namespace-qualified inherited or crane parameter leaves.
        /// </summary>
        private BaseVariableState Parameter(BaseObjectState parameters, CraneElementKind kind, CraneSignal signal)
        {
            var name = new QualifiedName(CraneSignals.Name(signal),
                CraneSignals.Index(Context.NamespaceUris, CraneSignals.Namespace(kind, signal)));
            return parameters.FindChild(Context, name) as BaseVariableState ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Mandatory parameter '{0}' is missing.", name);
        }

        /// <summary>
        /// Adds Operational/Configuration views that organize the original values.
        /// </summary>
        private void AddFunctionalGroups(string id, BaseObjectState component)
        {
            FunctionalGroupState operational = Context.CreateInstanceOfFunctionalGroupType(
                component, new QualifiedName(Di.BrowseNames.Operational,
                    CraneSignals.Index(Context.NamespaceUris, Di.Namespaces.OpcUaDi)));
            operational.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            switch (component)
            {
                case CraneMotionDeviceSystemState system:
                    system.CreateOrReplaceOperational(Context, operational, assignInstanceNodeIds: false);
                    break;
                case CraneMotionDeviceState device:
                    device.CreateOrReplaceOperational(Context, operational, assignInstanceNodeIds: false);
                    break;
                case CraneAxisState axis:
                    axis.CreateOrReplaceOperational(Context, operational, assignInstanceNodeIds: false);
                    break;
                case CraneMotorState motor:
                    motor.CreateOrReplaceOperational(Context, operational, assignInstanceNodeIds: false);
                    break;
                case BrakeState brake:
                    brake.CreateOrReplaceOperational(Context, operational, assignInstanceNodeIds: false);
                    break;
                case ProtectiveFunctionState protection:
                    protection.CreateOrReplaceOperational(Context, operational, assignInstanceNodeIds: false);
                    break;
            }
            FunctionalGroupState? configuration = null;
            foreach (KeyValuePair<CraneSignalAddress, CraneSignalBinding> pair in Signals)
            {
                if (pair.Key.ComponentId != id || CraneSignals.IsProperty(pair.Key.Signal))
                {
                    continue;
                }
                if (pair.Value.Request)
                {
                    if (configuration == null)
                    {
                        configuration = Context.CreateInstanceOfFunctionalGroupType(component,
                            new QualifiedName(Di.BrowseNames.Configuration,
                                CraneSignals.Index(Context.NamespaceUris, Di.Namespaces.OpcUaDi)));
                        configuration.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                        if (component is CraneMotionDeviceSystemState system)
                        {
                            system.CreateOrReplaceConfiguration(Context, configuration, assignInstanceNodeIds: false);
                        }
                        else if (component is CraneAxisState axis)
                        {
                            axis.CreateOrReplaceConfiguration(Context, configuration, assignInstanceNodeIds: false);
                        }
                    }
                    Link(configuration, Opc.Ua.ReferenceTypeIds.Organizes, pair.Value.Node);
                }
                else if (pair.Key.Signal is not
                    (CraneSignal.LoadMass or CraneSignal.LoadCenterOfMass or CraneSignal.LoadInertia))
                {
                    Link(operational, Opc.Ua.ReferenceTypeIds.Organizes, pair.Value.Node);
                }
            }
        }

        /// <summary>
        /// References the already created motors from a device's optional Components add-in.
        /// </summary>
        private void AddComponentsReferences(MachineComponentsState components, string deviceId)
        {
            CraneDeviceBuilder definition = m_definition.Devices.Find(device => device.Id == deviceId)!;
            foreach (CraneAxisBuilder axis in definition.Axes)
            {
                foreach (CraneMotorBuilder motor in axis.Motors)
                {
                    Link(components, Opc.Ua.ReferenceTypeIds.HasComponent, Elements[motor.Id].State);
                }
            }
        }

        /// <summary>
        /// Captures a configuration delegate once and defers all node writes to shared identification builders.
        /// </summary>
        private MachineryIdentificationData AddIdentification(
            MachineryItemIdentificationState state,
            Action<MachineryIdentificationData> configure)
        {
            // The released crane nameplates alias some shared property declarations.
            // Generated inherited children are already materialized; retain those local
            // values rather than a second browse target in the model's type templates.
            var references = new List<IReference>();
            state.GetReferences(Context, references, Opc.Ua.ReferenceTypeIds.HasProperty, false);
            foreach (IReference reference in references)
            {
                NodeId target = ExpandedNodeId.ToNodeId(reference.TargetId, Context.NamespaceUris);
                NodeState? targetNode = m_system.BuildContext.Manager.FindPredefinedNode(target) ??
                    FindStagedNode(target);
                if (targetNode is BaseVariableState declaration &&
                    state.FindChild(Context, declaration.BrowseName) is BaseVariableState local &&
                    local.NodeId != target)
                {
                    state.RemoveReference(Opc.Ua.ReferenceTypeIds.HasProperty, false, reference.TargetId);
                }
            }
            var data = new MachineryIdentificationData();
            configure(data);
            m_identifications.Add((state, data));
            return data;
        }

        /// <summary>
        /// Resolves aliases to already staged direct DI properties without requiring premature registration.
        /// </summary>
        private NodeState? FindStagedNode(NodeId nodeId)
        {
            var pending = new Queue<NodeState>();
            pending.Enqueue(State);
            while (pending.Count > 0)
            {
                NodeState candidate = pending.Dequeue();
                if (candidate.NodeId == nodeId)
                {
                    return candidate;
                }
                var children = new List<BaseInstanceState>();
                candidate.GetChildren(Context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Enqueue(child);
                }
            }
            return null;
        }

        /// <summary>
        /// Reuses the supplied identity values for the existing Machinery writer.
        /// </summary>
        private static void CopyIdentity(MachineryIdentificationData source, MachineryIdentificationData target)
        {
            target.Manufacturer = source.Manufacturer;
            target.SerialNumber = source.SerialNumber;
            target.ProductInstanceUri = source.ProductInstanceUri;
            target.ManufacturerUri = source.ManufacturerUri;
            target.Model = source.Model;
            target.ProductCode = source.ProductCode;
            target.HardwareRevision = source.HardwareRevision;
            target.SoftwareRevision = source.SoftwareRevision;
            target.DeviceRevision = source.DeviceRevision;
            target.DeviceClass = source.DeviceClass;
            target.AssetId = source.AssetId;
            target.ComponentName = source.ComponentName;
            target.Location = source.Location;
            target.InitialOperationDate = source.InitialOperationDate;
            target.YearOfConstruction = source.YearOfConstruction;
            target.MonthOfConstruction = source.MonthOfConstruction;
            target.Writable = source.Writable;
        }

        /// <summary>
        /// Gives the shared DI writer the same identity as the Machinery nameplate.
        /// </summary>
        private static void CopyIdentity(
            MachineryIdentificationData source,
            Di.Server.Builders.DeviceIdentificationData target)
        {
            target.Manufacturer = source.Manufacturer;
            target.SerialNumber = source.SerialNumber;
            target.Model = source.Model;
            target.ProductCode = source.ProductCode;
            target.HardwareRevision = source.HardwareRevision;
            target.SoftwareRevision = source.SoftwareRevision;
        }

        /// <summary>
        /// Resolves an application-owned browse name.
        /// </summary>
        private QualifiedName InstanceName(string name)
        {
            return new QualifiedName(name, m_system.BuildContext.InstanceNamespaceIndex);
        }

        /// <summary>
        /// Adds both directions of an actual semantic relationship.
        /// </summary>
        private static void Link(NodeState source, NodeId type, NodeState target)
        {
            source.AddReference(type, false, target.NodeId);
            target.AddReference(type, true, source.NodeId);
        }

        /// <summary>
        /// Applies the standard UN/CEFACT percentage unit to limit and working-period values.
        /// </summary>
        private static void SetPercentUnits(BaseVariableState state)
        {
            ((AnalogUnitState<double>)state).EngineeringUnits!.Value = new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 20529,
                DisplayName = new LocalizedText("%"),
                Description = new LocalizedText("percent")
            };
        }

        /// <summary>The existing Machinery publisher, when explicitly selected.</summary>
        internal IMachineryNotificationPublisher? EventPublisher;

        private readonly CraneSystemBuilder m_definition;
        private readonly IMotionDeviceSystemBuilder m_system;
        private readonly List<(MachineryItemIdentificationState State, MachineryIdentificationData Data)>
            m_identifications = [];
    }

    /// <summary>
    /// Binds a provider address to one actual variable, with separate request and telemetry roles.
    /// </summary>
    /// <param name="Address">The system-local address.</param>
    /// <param name="Node">The actual standard variable.</param>
    /// <param name="Telemetry">Whether the equipment provider must report it.</param>
    /// <param name="Request">Whether it accepts explicitly authorized configuration requests.</param>
    internal sealed record CraneSignalBinding(
        CraneSignalAddress Address,
        BaseVariableState Node,
        bool Telemetry,
        bool Request);
}
