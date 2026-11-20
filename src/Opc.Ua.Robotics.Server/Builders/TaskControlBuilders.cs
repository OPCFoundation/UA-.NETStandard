/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Robotics.Server.Builders
{
    internal sealed class TaskControlBuilder :
        RoboticsNodeBuilder<TaskControlState>,
        ITaskControlBuilder
    {
        public TaskControlBuilder(RoboticsBuildScope scope, TaskControlState state)
            : base(scope, state)
        {
        }

        internal TaskControlOperationBuilder? TaskControlOperation { get; private set; }

        public ITaskControlBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public ITaskControlBuilder WithComponentName(LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public ITaskControlBuilder WithExecutionMode(
            ExecutionModeEnumeration value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                EnsureExecutionMode(),
                value,
                statusCode,
                timestamp);
            return this;
        }

        public ITaskControlBuilder WithTaskProgramLoaded(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                TaskProgramLoaded,
                value,
                statusCode,
                timestamp);
            return this;
        }

        public ITaskControlBuilder WithTaskProgramName(
            string value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                TaskProgramName,
                value,
                statusCode,
                timestamp);
            return this;
        }

        public ITaskControlBuilder BindExecutionMode(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(EnsureExecutionMode(), read);
            return this;
        }

        public ITaskControlBuilder BindTaskProgramLoaded(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(TaskProgramLoaded, read);
            return this;
        }

        public ITaskControlBuilder BindTaskProgramName(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(TaskProgramName, read);
            return this;
        }

        public ITaskModuleBuilder AddTaskModule(
            string browseName,
            Action<ITaskModuleBuilder>? configure = null)
        {
            return AddTaskModule(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public ITaskControlOperationBuilder AddTaskControlOperation(
            Action<ITaskControlOperationBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            State.AddTaskControlOperation(Scope.Context);
            TaskControlOperation ??= new TaskControlOperationBuilder(
                Scope,
                State.TaskControlOperation!);
            configure?.Invoke(TaskControlOperation);
            return TaskControlOperation;
        }

        public ITaskModuleBuilder AddTaskModule(
            QualifiedName browseName,
            Action<ITaskModuleBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            State.AddTaskModules(Scope.Context);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            TaskModuleState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State.TaskModules!,
                normalized,
                global::Opc.Ua.ReferenceTypeIds.Organizes,
                (parent, name) =>
                    Scope.Context.CreateInstanceOfTaskModuleType(parent, name));
            var builder = new TaskModuleBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public ITaskControlBuilder Controls(IMotionDeviceBuilder motionDevice)
        {
            MotionDeviceBuilder target =
                RequireSameScope<MotionDeviceBuilder, MotionDeviceState>(
                    motionDevice,
                    nameof(motionDevice));
            Scope.AddTaskControlRelation(this, target);
            return this;
        }

        private BaseObjectState ParameterSet => State.ParameterSet ??
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Generated mandatory ParameterSet is missing below task control '{0}'.",
                State.BrowseName);

        private BaseDataVariableState<bool> TaskProgramLoaded =>
            RoboticsBuilderUtilities.FindRequiredChild<BaseDataVariableState<bool>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.TaskProgramLoaded);

        private BaseDataVariableState<string> TaskProgramName =>
            RoboticsBuilderUtilities.FindRequiredChild<BaseDataVariableState<string>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.TaskProgramName);

        private BaseDataVariableState<ExecutionModeEnumeration> EnsureExecutionMode()
        {
            BaseDataVariableState<ExecutionModeEnumeration>? variable =
                RoboticsBuilderUtilities.FindChild<
                    BaseDataVariableState<ExecutionModeEnumeration>>(
                    Scope.Context,
                    ParameterSet,
                    BrowseNames.ExecutionMode);
            if (variable == null)
            {
                variable = RoboticsBuilderUtilities.AddGeneratedChild(
                    Scope.Context,
                    ParameterSet,
                    parent =>
                        OpcUaRoboticsExtensions
                            .CreateTaskControlType_ParameterSet_ExecutionMode(
                                Scope.Context,
                                parent,
                                true));
            }
            return variable;
        }
    }

    internal sealed class TaskModuleBuilder :
        RoboticsNodeBuilder<TaskModuleState>,
        ITaskModuleBuilder
    {
        public TaskModuleBuilder(RoboticsBuildScope scope, TaskModuleState state)
            : base(scope, state)
        {
            scope.TaskModules.Add(this);
        }

        internal bool HasName { get; private set; }

        public ITaskModuleBuilder WithName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "A non-empty task-module name is required.",
                    nameof(name));
            }
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(State.Name!, name);
            HasName = true;
            return this;
        }

        public ITaskModuleBuilder WithVersion(string version)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }
            Scope.EnsureMutable();
            State.AddVersion(Scope.Context);
            RoboticsBuilderUtilities.SetValue(State.Version!, version);
            return this;
        }

        public ITaskModuleBuilder WithIsReferenced(bool isReferenced)
        {
            Scope.EnsureMutable();
            State.AddIsReferenced(Scope.Context);
            RoboticsBuilderUtilities.SetValue(State.IsReferenced!, isReferenced);
            return this;
        }
    }
}
