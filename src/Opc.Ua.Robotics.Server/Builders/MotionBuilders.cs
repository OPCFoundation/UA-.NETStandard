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

namespace Opc.Ua.Robotics.Server.Builders
{
    internal sealed class MotionDeviceBuilder :
        RoboticsNodeBuilder<MotionDeviceState>,
        IMotionDeviceBuilder
    {
        private LoadBuilder? m_flangeLoad;

        public MotionDeviceBuilder(RoboticsBuildScope scope, MotionDeviceState state)
            : base(scope, state)
        {
            scope.MotionDevices.Add(this);
        }

        internal List<AxisBuilder> Axes { get; } = [];

        internal List<PowerTrainBuilder> PowerTrains { get; } = [];

        public IMotionDeviceBuilder WithIdentification(
            Action<Di.Server.Builders.DeviceIdentificationData> configure)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.ApplyIdentification(
                State,
                Scope.Context,
                configure);
            return this;
        }

        public IMotionDeviceBuilder WithCategory(
            MotionDeviceCategoryEnumeration category)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(State.MotionDeviceCategory!, category);
            return this;
        }

        public IMotionDeviceBuilder WithMotionDeviceCategory(
            MotionDeviceCategoryEnumeration category)
        {
            return WithCategory(category);
        }

        public IMotionDeviceBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public IMotionDeviceBuilder WithComponentName(LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public IAxisBuilder AddAxis(
            string browseName,
            Action<IAxisBuilder>? configure = null)
        {
            return AddAxis(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IAxisBuilder AddAxis(
            QualifiedName browseName,
            Action<IAxisBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            FolderState parent = State.Axes ??
                throw MissingMandatoryContainer(BrowseNames.Axes);
            AxisState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                parent,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfAxisType(container, name));
            var builder = new AxisBuilder(Scope, state);
            Axes.Add(builder);
            configure?.Invoke(builder);
            return builder;
        }

        public IPowerTrainBuilder AddPowerTrain(
            string browseName,
            Action<IPowerTrainBuilder>? configure = null)
        {
            return AddPowerTrain(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IPowerTrainBuilder AddPowerTrain(
            QualifiedName browseName,
            Action<IPowerTrainBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            FolderState parent = State.PowerTrains ??
                throw MissingMandatoryContainer(BrowseNames.PowerTrains);
            PowerTrainState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                parent,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfPowerTrainType(container, name));
            var builder = new PowerTrainBuilder(Scope, state);
            PowerTrains.Add(builder);
            configure?.Invoke(builder);
            return builder;
        }

        public ILoadBuilder WithFlangeLoad()
        {
            Scope.EnsureMutable();
            State.AddFlangeLoad(Scope.Context);
            return m_flangeLoad ??= new LoadBuilder(Scope, State.FlangeLoad!);
        }

        public IMotionDeviceBuilder WithFlangeLoad(Action<ILoadBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(WithFlangeLoad());
            return this;
        }

        public IAuxiliaryComponentBuilder AddAuxiliaryComponent(
            string browseName,
            Action<IAuxiliaryComponentBuilder>? configure = null)
        {
            return AddAuxiliaryComponent(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IAuxiliaryComponentBuilder AddAuxiliaryComponent(
            QualifiedName browseName,
            Action<IAuxiliaryComponentBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            State.AddAdditionalComponents(Scope.Context);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            AuxiliaryComponentState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State.AdditionalComponents!,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfAuxiliaryComponentType(container, name));
            var builder = new AuxiliaryComponentBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public IDriveBuilder AddDrive(
            string browseName,
            Action<IDriveBuilder>? configure = null)
        {
            return AddDrive(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IDriveBuilder AddDrive(
            QualifiedName browseName,
            Action<IDriveBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            State.AddAdditionalComponents(Scope.Context);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            DriveState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State.AdditionalComponents!,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfDriveType(container, name));
            var builder = new DriveBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public IMotionDeviceBuilder WithSpeedOverride(
            double value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            EnsureValidSpeedOverride(value, nameof(value));
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                SpeedOverride,
                value,
                statusCode,
                timestamp);
            return this;
        }

        public IRoboticsOperationsBuilder AddOperations(
            string browseName,
            ushort applicationNamespaceIndex,
            Action<IRoboticsOperationsBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scope.EnsureMutable();
            var builder = new RoboticsOperationsBuilder(
                Scope,
                State,
                browseName,
                applicationNamespaceIndex);
            configure(builder);
            return builder;
        }

        public IMotionDeviceBuilder BindSpeedOverrideRead(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(
                SpeedOverride,
                async cancellationToken =>
                {
                    DataValue value = await read(cancellationToken).ConfigureAwait(false);
                    return ValidateSpeedOverrideRead(value);
                });
            return this;
        }

        public IMotionDeviceBuilder BindSpeedOverrideWrite(
            Func<Variant, CancellationToken, ValueTask<ServiceResult>> write)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindWrite(
                SpeedOverride,
                (value, cancellationToken) =>
                {
                    ServiceResult validation = ValidateSpeedOverrideWrite(value);
                    return ServiceResult.IsGood(validation)
                        ? write(value, cancellationToken)
                        : new ValueTask<ServiceResult>(validation);
                });
            return this;
        }

        public IMotionDeviceBuilder BindSpeedOverride(
            Func<CancellationToken, ValueTask<DataValue>> read,
            Func<Variant, CancellationToken, ValueTask<ServiceResult>> write)
        {
            BindSpeedOverrideRead(read);
            BindSpeedOverrideWrite(write);
            return this;
        }

        public IMotionDeviceBuilder UsesTaskControl(ITaskControlBuilder taskControl)
        {
            TaskControlBuilder target =
                RequireSameScope<TaskControlBuilder, TaskControlState>(
                    taskControl,
                    nameof(taskControl));
            Scope.AddTaskControlRelation(target, this);
            return this;
        }

        public IMotionDeviceBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<MotionDeviceBuilder, TState>(other);
        }

        private BaseDataVariableState<double> SpeedOverride
        {
            get
            {
                BaseObjectState parameterSet = State.ParameterSet ??
                    throw MissingMandatoryContainer("ParameterSet");
                return RoboticsBuilderUtilities.FindRequiredChild<
                    BaseDataVariableState<double>>(
                    Scope.Context,
                    parameterSet,
                    BrowseNames.SpeedOverride);
            }
        }

        internal void SetTaskControlReference(
            TaskControlOperationState taskControlOperation)
        {
            if (taskControlOperation == null)
            {
                throw new ArgumentNullException(nameof(taskControlOperation));
            }
            if (State.NodeId.IsNull || taskControlOperation.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "TaskControlReference can only be set after the motion device and " +
                    "TaskControlOperation have final NodeIds.");
            }

            State.AddTaskControlReference(Scope.Context);
            RoboticsBuilderUtilities.SetValue(
                State.TaskControlReference!,
                taskControlOperation.NodeId);
        }

        private static DataValue ValidateSpeedOverrideRead(in DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return value;
            }
            if (!value.WrappedValue.TryGetValue(out double speedOverride))
            {
                return new DataValue(
                    value.WrappedValue,
                    StatusCodes.BadTypeMismatch,
                    value.SourceTimestamp,
                    value.ServerTimestamp);
            }
            if (!IsValidSpeedOverride(speedOverride))
            {
                return new DataValue(
                    value.WrappedValue,
                    StatusCodes.BadOutOfRange,
                    value.SourceTimestamp,
                    value.ServerTimestamp);
            }
            return value;
        }

        private static ServiceResult ValidateSpeedOverrideWrite(in Variant value)
        {
            if (!value.TryGetValue(out double speedOverride))
            {
                return new ServiceResult(StatusCodes.BadTypeMismatch);
            }
            return IsValidSpeedOverride(speedOverride)
                ? ServiceResult.Good
                : new ServiceResult(StatusCodes.BadOutOfRange);
        }

        private static void EnsureValidSpeedOverride(double value, string parameterName)
        {
            if (!IsValidSpeedOverride(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "SpeedOverride must be finite and in the inclusive range [0, 100].");
            }
        }

        private static bool IsValidSpeedOverride(double value)
        {
            return !double.IsNaN(value) &&
                !double.IsInfinity(value) &&
                value >= 0 &&
                value <= 100;
        }

        private ServiceResultException MissingMandatoryContainer(string browseName)
        {
            return ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Generated mandatory child '{0}' is missing below '{1}'.",
                browseName,
                State.BrowseName);
        }
    }

    internal sealed class AxisBuilder :
        RoboticsNodeBuilder<AxisState>,
        IAxisBuilder
    {
        private LoadBuilder? m_additionalLoad;

        public AxisBuilder(RoboticsBuildScope scope, AxisState state)
            : base(scope, state)
        {
            scope.Axes.Add(this);
        }

        internal bool IsVirtual { get; private set; }

        internal HashSet<PowerTrainBuilder> RequiredPowerTrains { get; } = [];

        public IAxisBuilder WithMotionProfile(AxisMotionProfileEnumeration profile)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(State.MotionProfile!, profile);
            return this;
        }

        public IAxisBuilder AsVirtual(bool isVirtual = true)
        {
            Scope.EnsureMutable();
            IsVirtual = isVirtual;
            return this;
        }

        public IAxisBuilder WithActualPosition(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetAnalogValue(
                ActualPosition,
                Scope.Context,
                value,
                engineeringUnits,
                range,
                statusCode,
                timestamp);
            return this;
        }

        public IAxisBuilder WithActualSpeed(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetAnalogValue(
                EnsureActualSpeed(),
                Scope.Context,
                value,
                engineeringUnits,
                range,
                statusCode,
                timestamp);
            return this;
        }

        public IAxisBuilder WithActualAcceleration(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetAnalogValue(
                EnsureActualAcceleration(),
                Scope.Context,
                value,
                engineeringUnits,
                range,
                statusCode,
                timestamp);
            return this;
        }

        public IAxisBuilder BindActualPosition(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(ActualPosition, read);
            return this;
        }

        public IAxisBuilder BindActualSpeed(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(EnsureActualSpeed(), read);
            return this;
        }

        public IAxisBuilder BindActualAcceleration(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(EnsureActualAcceleration(), read);
            return this;
        }

        public ILoadBuilder WithAdditionalLoad()
        {
            Scope.EnsureMutable();
            State.AddAdditionalLoad(Scope.Context);
            return m_additionalLoad ??= new LoadBuilder(Scope, State.AdditionalLoad!);
        }

        public IAxisBuilder WithAdditionalLoad(Action<ILoadBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(WithAdditionalLoad());
            return this;
        }

        public IAxisBuilder Requires(IPowerTrainBuilder powerTrain)
        {
            PowerTrainBuilder target =
                RequireSameScope<PowerTrainBuilder, PowerTrainState>(
                    powerTrain,
                    nameof(powerTrain));
            RequiredPowerTrains.Add(target);
            Scope.AddSemanticReference(
                RoboticsSemanticReference.Requires,
                this,
                target);
            return this;
        }

        public IAxisBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<AxisBuilder, TState>(other);
        }

        private BaseObjectState ParameterSet => State.ParameterSet ??
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Generated mandatory ParameterSet is missing below axis '{0}'.",
                State.BrowseName);

        private AnalogUnitState<double> ActualPosition =>
            RoboticsBuilderUtilities.FindRequiredChild<AnalogUnitState<double>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.ActualPosition);

        private AnalogUnitState<double> EnsureActualSpeed()
        {
            AnalogUnitState<double>? variable = RoboticsBuilderUtilities.FindChild<AnalogUnitState<double>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.ActualSpeed);
            if (variable == null)
            {
                variable = RoboticsBuilderUtilities.AddGeneratedChild(
                    Scope.Context,
                    ParameterSet,
                    parent =>
                        OpcUaRoboticsExtensions.CreateAxisType_ParameterSet_ActualSpeed(
                            Scope.Context,
                            parent,
                            true));
            }
            return variable;
        }

        private AnalogUnitState<double> EnsureActualAcceleration()
        {
            AnalogUnitState<double>? variable = RoboticsBuilderUtilities.FindChild<AnalogUnitState<double>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.ActualAcceleration);
            if (variable == null)
            {
                variable = RoboticsBuilderUtilities.AddGeneratedChild(
                    Scope.Context,
                    ParameterSet,
                    parent =>
                        OpcUaRoboticsExtensions
                            .CreateAxisType_ParameterSet_ActualAcceleration(
                                Scope.Context,
                                parent,
                                true));
            }
            return variable;
        }
    }

    internal sealed class LoadBuilder :
        RoboticsNodeBuilder<LoadState>,
        ILoadBuilder
    {
        public LoadBuilder(RoboticsBuildScope scope, LoadState state)
            : base(scope, state)
        {
        }

        public ILoadBuilder WithMass(
            double mass,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetAnalogValue(
                State.Mass!,
                Scope.Context,
                mass,
                engineeringUnits,
                range,
                statusCode,
                timestamp);
            return this;
        }

        public ILoadBuilder WithCenterOfMass(ThreeDFrame centerOfMass)
        {
            if (centerOfMass == null)
            {
                throw new ArgumentNullException(nameof(centerOfMass));
            }
            Scope.EnsureMutable();
            State.AddCenterOfMass(Scope.Context);
            RoboticsBuilderUtilities.SetValue(State.CenterOfMass!, centerOfMass);
            return this;
        }

        public ILoadBuilder WithInertia(ThreeDVector inertia)
        {
            if (inertia == null)
            {
                throw new ArgumentNullException(nameof(inertia));
            }
            Scope.EnsureMutable();
            State.AddInertia(Scope.Context);
            RoboticsBuilderUtilities.SetValue(State.Inertia!, inertia);
            return this;
        }
    }
}
