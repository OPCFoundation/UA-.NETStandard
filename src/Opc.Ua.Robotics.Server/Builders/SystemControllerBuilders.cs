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
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Robotics.Server.Builders
{
    internal sealed class MotionDeviceSystemBuilder :
        RoboticsNodeBuilder<MotionDeviceSystemState>,
        IMotionDeviceSystemBuilder
    {
        public MotionDeviceSystemBuilder(
            RoboticsBuildScope scope,
            MotionDeviceSystemState state)
            : base(scope, state)
        {
        }

        public IMotionDeviceSystemBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public IMotionDeviceSystemBuilder WithComponentName(LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public IControllerBuilder AddController(
            string browseName,
            Action<IControllerBuilder>? configure = null)
        {
            return AddController(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IControllerBuilder AddController(
            QualifiedName browseName,
            Action<IControllerBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            FolderState parent = State.Controllers ??
                throw MissingMandatoryContainer(BrowseNames.Controllers);
            ControllerState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                parent,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfControllerType(container, name));
            var builder = new ControllerBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public IMotionDeviceBuilder AddMotionDevice(
            string browseName,
            Action<IMotionDeviceBuilder>? configure = null)
        {
            return AddMotionDevice(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IMotionDeviceBuilder AddMotionDevice(
            QualifiedName browseName,
            Action<IMotionDeviceBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            FolderState parent = State.MotionDevices ??
                throw MissingMandatoryContainer(BrowseNames.MotionDevices);
            MotionDeviceState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                parent,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfMotionDeviceType(container, name));
            var builder = new MotionDeviceBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public ISafetyStateBuilder AddSafetyState(
            string browseName,
            Action<ISafetyStateBuilder>? configure = null)
        {
            return AddSafetyState(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public ISafetyStateBuilder AddSafetyState(
            QualifiedName browseName,
            Action<ISafetyStateBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            FolderState parent = State.SafetyStates ??
                throw MissingMandatoryContainer(BrowseNames.SafetyStates);
            SafetyStateState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                parent,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfSafetyStateType(container, name));
            var builder = new SafetyStateBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        private ServiceResultException MissingMandatoryContainer(string browseName)
        {
            return ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Generated mandatory container '{0}' is missing below '{1}'.",
                browseName,
                State.BrowseName);
        }
    }

    internal sealed class ControllerBuilder :
        RoboticsNodeBuilder<ControllerState>,
        IControllerBuilder
    {
        public ControllerBuilder(RoboticsBuildScope scope, ControllerState state)
            : base(scope, state)
        {
            scope.Controllers.Add(this);
        }

        internal List<RoboticsSoftwareBuilder> Software { get; } = [];

        internal List<TaskControlBuilder> TaskControls { get; } = [];

        public IControllerBuilder WithIdentification(
            Action<Di.Server.Builders.DeviceIdentificationData> configure)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.ApplyIdentification(
                State,
                Scope.Context,
                configure);
            return this;
        }

        public IControllerBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public IControllerBuilder WithComponentName(LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public IRoboticsSoftwareBuilder AddSoftware(
            string browseName,
            Action<IRoboticsSoftwareBuilder>? configure = null)
        {
            return AddSoftware(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IRoboticsSoftwareBuilder AddSoftware(
            QualifiedName browseName,
            Action<IRoboticsSoftwareBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            FolderState parent = State.Software ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Generated mandatory Software container is missing below '{0}'.",
                    State.BrowseName);
            SoftwareState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                parent,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfSoftwareType(container, name));
            var builder = new RoboticsSoftwareBuilder(Scope, state);
            Software.Add(builder);
            configure?.Invoke(builder);
            return builder;
        }

        public ITaskControlBuilder AddTaskControl(
            string browseName,
            Action<ITaskControlBuilder>? configure = null)
        {
            return AddTaskControl(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public ITaskControlBuilder AddTaskControl(
            QualifiedName browseName,
            Action<ITaskControlBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            FolderState parent = State.TaskControls ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Generated mandatory TaskControls container is missing below '{0}'.",
                    State.BrowseName);
            TaskControlState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                parent,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfTaskControlType(container, name));
            var builder = new TaskControlBuilder(Scope, state);
            TaskControls.Add(builder);
            configure?.Invoke(builder);
            return builder;
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
            State.AddComponents(Scope.Context);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            AuxiliaryComponentState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State.Components!,
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
            State.AddComponents(Scope.Context);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            DriveState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State.Components!,
                normalized,
                (container, name) =>
                    Scope.Context.CreateInstanceOfDriveType(container, name));
            var builder = new DriveBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public IControllerBuilder Controls(IMotionDeviceBuilder motionDevice)
        {
            MotionDeviceBuilder target =
                RequireSameScope<MotionDeviceBuilder, MotionDeviceState>(
                    motionDevice,
                    nameof(motionDevice));
            Scope.AddSemanticReference(
                RoboticsSemanticReference.Controls,
                this,
                target);
            return this;
        }

        public IControllerBuilder UsesSafetyState(ISafetyStateBuilder safetyState)
        {
            SafetyStateBuilder target =
                RequireSameScope<SafetyStateBuilder, SafetyStateState>(
                    safetyState,
                    nameof(safetyState));
            Scope.AddSemanticReference(
                RoboticsSemanticReference.HasSafetyStates,
                this,
                target);
            return this;
        }

        public IControllerBuilder IsConnectedTo<TState>(
            IRoboticsNodeBuilder<TState> other)
            where TState : NodeState
        {
            return IsConnectedTo<ControllerBuilder, TState>(other);
        }
    }

    internal sealed class RoboticsSoftwareBuilder :
        RoboticsNodeBuilder<SoftwareState>,
        IRoboticsSoftwareBuilder
    {
        public RoboticsSoftwareBuilder(
            RoboticsBuildScope scope,
            SoftwareState state)
            : base(scope, state)
        {
        }

        public IRoboticsSoftwareBuilder WithIdentification(
            Action<DeviceIdentificationData> configure)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.ApplyIdentification(
                State,
                Scope.Context,
                configure);
            return this;
        }

        public new IRoboticsSoftwareBuilder Configure(
            Action<SoftwareState, ISystemContext> configure)
        {
            base.Configure(configure);
            return this;
        }
    }
}
