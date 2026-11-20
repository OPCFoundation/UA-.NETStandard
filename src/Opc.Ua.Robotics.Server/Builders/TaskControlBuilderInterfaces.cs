/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Builds a task control without the operation state machine.
    /// </summary>
    public interface ITaskControlBuilder : IRoboticsNodeBuilder<TaskControlState>
    {
        /// <summary>
        /// Sets the mandatory component name.
        /// </summary>
        ITaskControlBuilder WithComponentName(string componentName);

        /// <summary>
        /// Sets the mandatory localized component name.
        /// </summary>
        ITaskControlBuilder WithComponentName(LocalizedText componentName);

        /// <summary>
        /// Materializes and sets optional ExecutionMode.
        /// </summary>
        ITaskControlBuilder WithExecutionMode(
            ExecutionModeEnumeration value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Sets mandatory TaskProgramLoaded.
        /// </summary>
        ITaskControlBuilder WithTaskProgramLoaded(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Sets mandatory TaskProgramName.
        /// </summary>
        ITaskControlBuilder WithTaskProgramName(
            string value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default);

        /// <summary>
        /// Materializes ExecutionMode and binds asynchronous reads.
        /// </summary>
        ITaskControlBuilder BindExecutionMode(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Binds asynchronous TaskProgramLoaded reads.
        /// </summary>
        ITaskControlBuilder BindTaskProgramLoaded(
            Func<CancellationToken, ValueTask<DataValue>> read);

        /// <summary>
        /// Binds asynchronous TaskProgramName reads.
        /// </summary>
        ITaskControlBuilder BindTaskProgramName(
            Func<CancellationToken, ValueTask<DataValue>> read);


        /// <summary>
        /// Adds the optional standard TaskControlOperation facet.
        /// </summary>
        ITaskControlOperationBuilder AddTaskControlOperation(
            Action<ITaskControlOperationBuilder>? configure = null);

        /// <summary>
        /// Adds a task module to the optional TaskModules folder.
        /// </summary>
        ITaskModuleBuilder AddTaskModule(
            string browseName,
            Action<ITaskModuleBuilder>? configure = null);


        /// <summary>
        /// Adds a task module to the optional TaskModules folder.
        /// </summary>
        ITaskModuleBuilder AddTaskModule(
            QualifiedName browseName,
            Action<ITaskModuleBuilder>? configure = null);

        /// <summary>
        /// Adds the standard Controls relationship to a motion device.
        /// </summary>
        ITaskControlBuilder Controls(IMotionDeviceBuilder motionDevice);
    }


    /// <summary>
    /// Builds the standard TaskControlType TaskControlOperation facet.
    /// </summary>
    public interface ITaskControlOperationBuilder : IRoboticsNodeBuilder<TaskControlOperationState>
    {
        /// <summary>
        /// Registers the optional Start method handler.
        /// </summary>
        ITaskControlOperationBuilder OnStart(
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler);

        /// <summary>
        /// Registers the optional Stop method handler.
        /// </summary>
        ITaskControlOperationBuilder OnStop(
            Func<RoboticsStopRequest, CancellationToken, ValueTask<ServiceResult>> handler);

        /// <summary>
        /// Registers the optional LoadByName method handler.
        /// </summary>
        ITaskControlOperationBuilder OnLoadByName(
            Func<string, CancellationToken, ValueTask<RoboticsProgramResult>> handler);

        /// <summary>
        /// Registers the optional LoadByNodeId method handler.
        /// </summary>
        ITaskControlOperationBuilder OnLoadByNodeId(
            Func<NodeId, CancellationToken, ValueTask<RoboticsProgramResult>> handler);

        /// <summary>
        /// Registers the optional UnloadByName method handler.
        /// </summary>
        ITaskControlOperationBuilder OnUnloadByName(
            Func<string, CancellationToken, ValueTask<RoboticsProgramResult>> handler);

        /// <summary>
        /// Registers the optional UnloadByNodeId method handler.
        /// </summary>
        ITaskControlOperationBuilder OnUnloadByNodeId(
            Func<NodeId, CancellationToken, ValueTask<RoboticsProgramResult>> handler);

        /// <summary>
        /// Registers the optional UnloadProgram method handler.
        /// </summary>
        ITaskControlOperationBuilder OnUnloadProgram(
            Func<CancellationToken, ValueTask<RoboticsProgramResult>> handler);

        /// <summary>
        /// Registers the optional ResetToProgramStart method handler.
        /// </summary>
        ITaskControlOperationBuilder OnResetToProgramStart(
            Func<CancellationToken, ValueTask<RoboticsProgramResult>> handler);

        /// <summary>
        /// Sets the optional MotionDevicesUnderControl property.
        /// </summary>
        ITaskControlOperationBuilder WithMotionDevicesUnderControl(
            ArrayOf<NodeId> motionDevices);

        /// <summary>
        /// Registers a transition notification handler.
        /// </summary>
        ITaskControlOperationBuilder OnTransition(
            Func<RoboticsOperationTransition, CancellationToken, ValueTask> handler);
    }

    /// <summary>
    /// Builds a task module.
    /// </summary>
    public interface ITaskModuleBuilder : IRoboticsNodeBuilder<TaskModuleState>
    {
        /// <summary>
        /// Sets the required task-module name.
        /// </summary>
        ITaskModuleBuilder WithName(string name);

        /// <summary>
        /// Materializes and sets Version.
        /// </summary>
        ITaskModuleBuilder WithVersion(string version);

        /// <summary>
        /// Materializes and sets IsReferenced.
        /// </summary>
        ITaskModuleBuilder WithIsReferenced(bool isReferenced);
    }
}
