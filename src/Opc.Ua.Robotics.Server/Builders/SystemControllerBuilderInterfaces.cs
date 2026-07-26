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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Builds one <see cref="MotionDeviceSystemState"/> below the DI DeviceSet.
    /// </summary>
    public interface IMotionDeviceSystemBuilder :
        IRoboticsNodeBuilder<MotionDeviceSystemState>
    {
        /// <summary>
        /// Sets the optional system component name.
        /// </summary>
        IMotionDeviceSystemBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the optional localized system component name.
        /// </summary>
        IMotionDeviceSystemBuilder WithComponentName(LocalizedText componentName);

        /// <summary>
        /// Adds a controller to the mandatory Controllers folder.
        /// </summary>
        IControllerBuilder AddController(
            string browseName,
            Action<IControllerBuilder>? configure = null);

        /// <summary>
        /// Adds a controller to the mandatory Controllers folder.
        /// </summary>
        IControllerBuilder AddController(
            QualifiedName browseName,
            Action<IControllerBuilder>? configure = null);

        /// <summary>
        /// Adds a motion device to the mandatory MotionDevices folder.
        /// </summary>
        IMotionDeviceBuilder AddMotionDevice(
            string browseName,
            Action<IMotionDeviceBuilder>? configure = null);

        /// <summary>
        /// Adds a motion device to the mandatory MotionDevices folder.
        /// </summary>
        IMotionDeviceBuilder AddMotionDevice(
            QualifiedName browseName,
            Action<IMotionDeviceBuilder>? configure = null);

        /// <summary>
        /// Adds a safety-state instance to the mandatory SafetyStates folder.
        /// </summary>
        ISafetyStateBuilder AddSafetyState(
            string browseName,
            Action<ISafetyStateBuilder>? configure = null);

        /// <summary>
        /// Adds a safety-state instance to the mandatory SafetyStates folder.
        /// </summary>
        ISafetyStateBuilder AddSafetyState(
            QualifiedName browseName,
            Action<ISafetyStateBuilder>? configure = null);
    }

    /// <summary>
    /// Builds a Robotics controller and its contained components.
    /// </summary>
    public interface IControllerBuilder : IRoboticsNodeBuilder<ControllerState>
    {
        /// <summary>
        /// Sets inherited DI identification fields.
        /// </summary>
        IControllerBuilder WithIdentification(Action<DeviceIdentificationData> configure);

        /// <summary>
        /// Sets the optional controller component name.
        /// </summary>
        IControllerBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the optional localized controller component name.
        /// </summary>
        IControllerBuilder WithComponentName(LocalizedText componentName);


        /// <summary>
        /// Adds the optional standard SystemOperation facet.
        /// </summary>
        ISystemOperationBuilder AddSystemOperation(
            Action<ISystemOperationBuilder>? configure = null);

        /// <summary>
        /// Adds the optional standard Programs directory and backs it with a
        /// file-system provider. The directory is a Part 5
        /// <c>FileDirectoryType</c>, so a client reads and writes the
        /// controller's programs with the standard file services.
        /// </summary>
        /// <param name="configure">
        /// Selects the provider and the binding options.
        /// </param>
        IProgramsBuilder AddPrograms(Action<IProgramsBuilder> configure);

        /// <summary>
        /// Configures the mandatory CurrentUser child.
        /// </summary>
        IControllerBuilder WithCurrentUser(Action<IRoboticsUserBuilder> configure);

        /// <summary>
        /// Adds software to the mandatory Software folder.
        /// </summary>
        IRoboticsSoftwareBuilder AddSoftware(
            string browseName,
            Action<IRoboticsSoftwareBuilder>? configure = null);

        /// <summary>
        /// Adds software to the mandatory Software folder.
        /// </summary>
        IRoboticsSoftwareBuilder AddSoftware(
            QualifiedName browseName,
            Action<IRoboticsSoftwareBuilder>? configure = null);

        /// <summary>
        /// Adds a task control to the mandatory TaskControls folder.
        /// </summary>
        ITaskControlBuilder AddTaskControl(
            string browseName,
            Action<ITaskControlBuilder>? configure = null);

        /// <summary>
        /// Adds a task control to the mandatory TaskControls folder.
        /// </summary>
        ITaskControlBuilder AddTaskControl(
            QualifiedName browseName,
            Action<ITaskControlBuilder>? configure = null);

        /// <summary>
        /// Adds an auxiliary component to the optional Components folder.
        /// </summary>
        IAuxiliaryComponentBuilder AddAuxiliaryComponent(
            string browseName,
            Action<IAuxiliaryComponentBuilder>? configure = null);

        /// <summary>
        /// Adds an auxiliary component to the optional Components folder.
        /// </summary>
        IAuxiliaryComponentBuilder AddAuxiliaryComponent(
            QualifiedName browseName,
            Action<IAuxiliaryComponentBuilder>? configure = null);

        /// <summary>
        /// Adds a drive to the optional Components folder.
        /// </summary>
        IDriveBuilder AddDrive(
            string browseName,
            Action<IDriveBuilder>? configure = null);

        /// <summary>
        /// Adds a drive to the optional Components folder.
        /// </summary>
        IDriveBuilder AddDrive(
            QualifiedName browseName,
            Action<IDriveBuilder>? configure = null);

        /// <summary>
        /// Adds the optional standard Controls relationship to a motion device.
        /// </summary>
        IControllerBuilder Controls(IMotionDeviceBuilder motionDevice);

        /// <summary>
        /// Adds the standard HasSafetyStates relationship.
        /// </summary>
        IControllerBuilder UsesSafetyState(ISafetyStateBuilder safetyState);

        /// <summary>
        /// Adds a symmetric Robotics IsConnectedTo relationship.
        /// </summary>
        /// <typeparam name="TState">The generated target state type.</typeparam>
        IControllerBuilder IsConnectedTo<TState>(IRoboticsNodeBuilder<TState> other)
            where TState : NodeState;
    }


    /// <summary>
    /// Builds a standard Robotics user descriptor.
    /// </summary>
    public interface IRoboticsUserBuilder : IRoboticsNodeBuilder<UserState>
    {
        /// <summary>
        /// Sets the mandatory user level.
        /// </summary>
        IRoboticsUserBuilder WithLevel(string level);

        /// <summary>
        /// Sets the optional user name.
        /// </summary>
        IRoboticsUserBuilder WithName(string name);
    }

    /// <summary>
    /// Builds the standard ControllerType SystemOperation facet.
    /// </summary>
    public interface ISystemOperationBuilder : IRoboticsNodeBuilder<SystemOperationState>
    {
        /// <summary>
        /// Sets the initial operation state.
        /// </summary>
        ISystemOperationBuilder WithInitialState(RoboticsOperationState state);

        /// <summary>
        /// Registers the optional GetReady method handler.
        /// </summary>
        ISystemOperationBuilder OnGetReady(
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler);

        /// <summary>
        /// Registers the optional Start method handler.
        /// </summary>
        ISystemOperationBuilder OnStart(
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler);

        /// <summary>
        /// Registers the optional Stop method handler.
        /// </summary>
        ISystemOperationBuilder OnStop(
            Func<RoboticsStopRequest, CancellationToken, ValueTask<ServiceResult>> handler);

        /// <summary>
        /// Registers the optional StandDown method handler.
        /// </summary>
        ISystemOperationBuilder OnStandDown(
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler);

        /// <summary>
        /// Sets possible and default stop modes.
        /// </summary>
        ISystemOperationBuilder WithStopModes(
            ArrayOf<RoboticsStopMode> modes,
            RoboticsStopMode defaultMode);

        /// <summary>
        /// Registers a transition notification handler.
        /// </summary>
        ISystemOperationBuilder OnTransition(
            Func<RoboticsOperationTransition, CancellationToken, ValueTask> handler);

        /// <summary>
        /// Sets the LastTransitionReason value written on every move.
        /// </summary>
        ISystemOperationBuilder WithTransitionReason(short reason);
    }

    /// <summary>
    /// Builds one DI SoftwareType instance below a Robotics controller.
    /// </summary>
    public interface IRoboticsSoftwareBuilder : IRoboticsNodeBuilder<SoftwareState>
    {
        /// <summary>
        /// Sets inherited DI identification fields.
        /// </summary>
        IRoboticsSoftwareBuilder WithIdentification(
            Action<DeviceIdentificationData> configure);

        /// <summary>
        /// Applies focused low-level state configuration before registration.
        /// </summary>
        new IRoboticsSoftwareBuilder Configure(
            Action<SoftwareState, ISystemContext> configure);
    }
}
