/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Robotics.Server.Builders
{
    internal sealed class PowerTrainBuilder :
        RoboticsNodeBuilder<PowerTrainState>,
        IPowerTrainBuilder
    {
        public PowerTrainBuilder(RoboticsBuildScope scope, PowerTrainState state)
            : base(scope, state)
        {
            scope.PowerTrains.Add(this);
        }

        internal List<MotorBuilder> Motors { get; } = [];

        public IPowerTrainBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public IPowerTrainBuilder WithComponentName(LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public IMotorBuilder AddMotor(
            string browseName,
            Action<IMotorBuilder>? configure = null)
        {
            return AddMotor(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IMotorBuilder AddMotor(
            QualifiedName browseName,
            Action<IMotorBuilder>? configure = null)
        {
            return AddMotor(
                browseName,
                static (context, parent, name) => context.CreateInstanceOfMotorType(parent, name),
                configure);
        }

        /// <summary>
        /// Stages a derived motor through the shared topology builder.
        /// </summary>
        /// <typeparam name="TState">The generated motor subtype.</typeparam>
        internal IMotorBuilder AddMotor<TState>(
            QualifiedName browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> createMotor,
            Action<IMotorBuilder>? configure)
            where TState : MotorState
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            MotorState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State,
                normalized,
                (parent, name) => RoboticsBuilderUtilities.CreateCompanionInstance(
                    Scope.Context,
                    parent,
                    name,
                    createMotor,
                    ObjectTypes.MotorType));
            var builder = new MotorBuilder(Scope, state);
            Motors.Add(builder);
            configure?.Invoke(builder);
            return builder;
        }

        public IGearBuilder AddGear(
            string browseName,
            Action<IGearBuilder>? configure = null)
        {
            return AddGear(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IGearBuilder AddGear(
            QualifiedName browseName,
            Action<IGearBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            GearState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State,
                normalized,
                (parent, name) =>
                    Scope.Context.CreateInstanceOfGearType(parent, name));
            var builder = new GearBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public IPowerTrainBuilder Moves(IAxisBuilder axis)
        {
            AxisBuilder target = RequireSameScope<AxisBuilder, AxisState>(
                axis,
                nameof(axis));
            Scope.AddSemanticReference(
                RoboticsSemanticReference.Moves,
                this,
                target);
            return this;
        }

        public IPowerTrainBuilder HasSlave(IPowerTrainBuilder slave)
        {
            PowerTrainBuilder target =
                RequireSameScope<PowerTrainBuilder, PowerTrainState>(
                    slave,
                    nameof(slave));
            Scope.AddSemanticReference(
                RoboticsSemanticReference.HasSlave,
                this,
                target);
            return this;
        }

        public IPowerTrainBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<PowerTrainBuilder, TState>(other);
        }
    }

    internal sealed class MotorBuilder :
        RoboticsNodeBuilder<MotorState>,
        IMotorBuilder
    {
        public MotorBuilder(RoboticsBuildScope scope, MotorState state)
            : base(scope, state)
        {
        }

        public IMotorBuilder WithIdentification(
            Action<Di.Server.Builders.DeviceIdentificationData> configure)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.ApplyIdentification(
                State,
                Scope.Context,
                configure);
            return this;
        }

        public IMotorBuilder WithMotorTemperature(
            double value,
            EUInformation? engineeringUnits = null,
            global::Opc.Ua.Range? range = null,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetAnalogValue(
                MotorTemperature,
                Scope.Context,
                value,
                engineeringUnits,
                range,
                statusCode,
                timestamp);
            return this;
        }

        public IMotorBuilder WithBrakeReleased(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                EnsureBrakeReleased(),
                value,
                statusCode,
                timestamp);
            return this;
        }

        public IMotorBuilder WithEffectiveLoadRate(
            ushort value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                EnsureEffectiveLoadRate(),
                value,
                statusCode,
                timestamp);
            return this;
        }

        public IMotorBuilder BindMotorTemperature(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(MotorTemperature, read);
            return this;
        }

        public IMotorBuilder BindBrakeReleased(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(EnsureBrakeReleased(), read);
            return this;
        }

        public IMotorBuilder BindEffectiveLoadRate(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(EnsureEffectiveLoadRate(), read);
            return this;
        }

        public IMotorBuilder IsDrivenBy(IDriveBuilder drive)
        {
            DriveBuilder target = RequireSameScope<DriveBuilder, DriveState>(
                drive,
                nameof(drive));
            Scope.AddSemanticReference(
                RoboticsSemanticReference.IsDrivenBy,
                this,
                target);
            return this;
        }

        public IMotorBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<MotorBuilder, TState>(other);
        }

        private BaseObjectState ParameterSet => State.ParameterSet ??
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Generated mandatory ParameterSet is missing below motor '{0}'.",
                State.BrowseName);

        private AnalogUnitState<double> MotorTemperature =>
            RoboticsBuilderUtilities.FindRequiredChild<AnalogUnitState<double>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.MotorTemperature);

        private BaseDataVariableState<bool> EnsureBrakeReleased()
        {
            BaseDataVariableState<bool>? variable = RoboticsBuilderUtilities.FindChild<BaseDataVariableState<bool>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.BrakeReleased);
            if (variable == null)
            {
                variable = RoboticsBuilderUtilities.AddGeneratedChild(
                    Scope.Context,
                    ParameterSet,
                    parent =>
                        OpcUaRoboticsExtensions.CreateMotorType_ParameterSet_BrakeReleased(
                            Scope.Context,
                            parent,
                            true));
            }
            return variable;
        }

        private BaseDataVariableState<ushort> EnsureEffectiveLoadRate()
        {
            BaseDataVariableState<ushort>? variable = RoboticsBuilderUtilities.FindChild<BaseDataVariableState<ushort>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.EffectiveLoadRate);
            if (variable == null)
            {
                variable = RoboticsBuilderUtilities.AddGeneratedChild(
                    Scope.Context,
                    ParameterSet,
                    parent =>
                        OpcUaRoboticsExtensions
                            .CreateMotorType_ParameterSet_EffectiveLoadRate(
                                Scope.Context,
                                parent,
                                true));
            }
            return variable;
        }
    }

    internal sealed class GearBuilder :
        RoboticsNodeBuilder<GearState>,
        IGearBuilder
    {
        public GearBuilder(RoboticsBuildScope scope, GearState state)
            : base(scope, state)
        {
            scope.Gears.Add(this);
        }

        internal bool HasGearRatio { get; private set; }

        public IGearBuilder WithIdentification(
            Action<Di.Server.Builders.DeviceIdentificationData> configure)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.ApplyIdentification(
                State,
                Scope.Context,
                configure);
            return this;
        }

        public IGearBuilder WithGearRatio(int numerator, uint denominator)
        {
            if (denominator == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(denominator),
                    denominator,
                    "The gear-ratio denominator must not be zero.");
            }

            Scope.EnsureMutable();
            RationalNumberState ratio = State.GearRatio ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Generated mandatory GearRatio is missing below '{0}'.",
                    State.BrowseName);
            var value = new RationalNumber
            {
                Numerator = numerator,
                Denominator = denominator
            };
            RoboticsBuilderUtilities.SetValue(ratio, value);
            RoboticsBuilderUtilities.SetValue(ratio.Numerator!, value.Numerator);
            RoboticsBuilderUtilities.SetValue(ratio.Denominator!, value.Denominator);
            HasGearRatio = true;
            return this;
        }

        public IGearBuilder WithPitch(
            double millimetresPerRevolution,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            if (double.IsNaN(millimetresPerRevolution) ||
                double.IsInfinity(millimetresPerRevolution))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(millimetresPerRevolution),
                    millimetresPerRevolution,
                    "Pitch must be a finite number of millimetres per output-side revolution.");
            }

            Scope.EnsureMutable();
            State.AddPitch(Scope.Context);
            RoboticsBuilderUtilities.SetValue(
                State.Pitch!,
                millimetresPerRevolution,
                statusCode,
                timestamp);
            return this;
        }

        public IGearBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<GearBuilder, TState>(other);
        }
    }

    internal sealed class DriveBuilder :
        RoboticsNodeBuilder<DriveState>,
        IDriveBuilder
    {
        public DriveBuilder(RoboticsBuildScope scope, DriveState state)
            : base(scope, state)
        {
        }

        public IDriveBuilder WithProductCode(string productCode)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetProductCode(
                State,
                Scope.Context,
                productCode);
            return this;
        }

        public IDriveBuilder WithAssetId(string assetId)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetAssetId(State, Scope.Context, assetId);
            return this;
        }

        public IDriveBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public IDriveBuilder WithComponentName(LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public IDriveBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<DriveBuilder, TState>(other);
        }
    }

    internal sealed class AuxiliaryComponentBuilder :
        RoboticsNodeBuilder<AuxiliaryComponentState>,
        IAuxiliaryComponentBuilder
    {
        public AuxiliaryComponentBuilder(
            RoboticsBuildScope scope,
            AuxiliaryComponentState state)
            : base(scope, state)
        {
        }

        public IAuxiliaryComponentBuilder WithProductCode(string productCode)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetProductCode(
                State,
                Scope.Context,
                productCode);
            return this;
        }

        public IAuxiliaryComponentBuilder WithAssetId(string assetId)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetAssetId(State, Scope.Context, assetId);
            return this;
        }

        public IAuxiliaryComponentBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public IAuxiliaryComponentBuilder WithComponentName(
            LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public IAuxiliaryComponentBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<AuxiliaryComponentBuilder, TState>(other);
        }
    }
}
