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

namespace Opc.Ua.Robotics.Server.Builders
{
    internal sealed class SafetyStateBuilder :
        RoboticsNodeBuilder<SafetyStateState>,
        ISafetyStateBuilder
    {
        public SafetyStateBuilder(RoboticsBuildScope scope, SafetyStateState state)
            : base(scope, state)
        {
            scope.SafetyStates.Add(this);
        }

        public ISafetyStateBuilder WithComponentName(string componentName)
        {
            return WithComponentName(new LocalizedText(componentName));
        }

        public ISafetyStateBuilder WithComponentName(LocalizedText componentName)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetComponentName(State, Scope.Context, componentName);
            return this;
        }

        public ISafetyStateBuilder WithEmergencyStop(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                EmergencyStop,
                value,
                statusCode,
                timestamp);
            return this;
        }

        public ISafetyStateBuilder WithOperationalMode(
            OperationalModeEnumeration value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                OperationalMode,
                value,
                statusCode,
                timestamp);
            return this;
        }

        public ISafetyStateBuilder WithProtectiveStop(
            bool value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                ProtectiveStop,
                value,
                statusCode,
                timestamp);
            return this;
        }

        public ISafetyStateBuilder BindEmergencyStop(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(EmergencyStop, read);
            return this;
        }

        public ISafetyStateBuilder BindOperationalMode(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(OperationalMode, read);
            return this;
        }

        public ISafetyStateBuilder BindProtectiveStop(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(ProtectiveStop, read);
            return this;
        }

        public IEmergencyStopBuilder AddEmergencyStop(
            string browseName,
            Action<IEmergencyStopBuilder>? configure = null)
        {
            return AddEmergencyStop(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IEmergencyStopBuilder AddEmergencyStop(
            string browseName,
            string name,
            Action<IEmergencyStopBuilder>? configure = null)
        {
            IEmergencyStopBuilder builder = AddEmergencyStop(browseName);
            builder.WithName(name);
            configure?.Invoke(builder);
            return builder;
        }

        public IEmergencyStopBuilder AddEmergencyStop(
            QualifiedName browseName,
            Action<IEmergencyStopBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            State.AddEmergencyStopFunctions(Scope.Context);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            EmergencyStopFunctionState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State.EmergencyStopFunctions!,
                normalized,
                (parent, name) =>
                    Scope.Context.CreateInstanceOfEmergencyStopFunctionType(parent, name));
            var builder = new EmergencyStopBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        public IProtectiveStopBuilder AddProtectiveStop(
            string browseName,
            Action<IProtectiveStopBuilder>? configure = null)
        {
            return AddProtectiveStop(
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName),
                configure);
        }

        public IProtectiveStopBuilder AddProtectiveStop(
            string browseName,
            string name,
            Action<IProtectiveStopBuilder>? configure = null)
        {
            IProtectiveStopBuilder builder = AddProtectiveStop(browseName);
            builder.WithName(name);
            configure?.Invoke(builder);
            return builder;
        }

        public IProtectiveStopBuilder AddProtectiveStop(
            QualifiedName browseName,
            Action<IProtectiveStopBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            State.AddProtectiveStopFunctions(Scope.Context);
            QualifiedName normalized =
                RoboticsBuilderUtilities.NormalizeBrowseName(BuildContext, browseName);
            ProtectiveStopFunctionState state = RoboticsBuilderUtilities.AddContained(
                Scope.Context,
                State.ProtectiveStopFunctions!,
                normalized,
                (parent, name) =>
                    Scope.Context.CreateInstanceOfProtectiveStopFunctionType(parent, name));
            var builder = new ProtectiveStopBuilder(Scope, state);
            configure?.Invoke(builder);
            return builder;
        }

        private BaseObjectState ParameterSet => State.ParameterSet ??
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Generated mandatory ParameterSet is missing below safety state '{0}'.",
                State.BrowseName);

        private BaseDataVariableState<bool> EmergencyStop =>
            RoboticsBuilderUtilities.FindRequiredChild<BaseDataVariableState<bool>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.EmergencyStop);

        private BaseDataVariableState<OperationalModeEnumeration> OperationalMode =>
            RoboticsBuilderUtilities.FindRequiredChild<
                BaseDataVariableState<OperationalModeEnumeration>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.OperationalMode);

        private BaseDataVariableState<bool> ProtectiveStop =>
            RoboticsBuilderUtilities.FindRequiredChild<BaseDataVariableState<bool>>(
                Scope.Context,
                ParameterSet,
                BrowseNames.ProtectiveStop);
    }

    internal sealed class EmergencyStopBuilder :
        RoboticsNodeBuilder<EmergencyStopFunctionState>,
        IEmergencyStopBuilder
    {
        public EmergencyStopBuilder(
            RoboticsBuildScope scope,
            EmergencyStopFunctionState state)
            : base(scope, state)
        {
            scope.EmergencyStops.Add(this);
        }

        internal bool HasName { get; private set; }

        public IEmergencyStopBuilder WithName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "A non-empty emergency-stop name is required.",
                    nameof(name));
            }
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(State.Name!, name);
            HasName = true;
            return this;
        }

        public IEmergencyStopBuilder WithActive(
            bool active,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                State.Active!,
                active,
                statusCode,
                timestamp);
            return this;
        }

        public IEmergencyStopBuilder BindActive(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(State.Active!, read);
            return this;
        }
    }

    internal sealed class ProtectiveStopBuilder :
        RoboticsNodeBuilder<ProtectiveStopFunctionState>,
        IProtectiveStopBuilder
    {
        public ProtectiveStopBuilder(
            RoboticsBuildScope scope,
            ProtectiveStopFunctionState state)
            : base(scope, state)
        {
            scope.ProtectiveStops.Add(this);
        }

        internal bool HasName { get; private set; }

        public IProtectiveStopBuilder WithName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "A non-empty protective-stop name is required.",
                    nameof(name));
            }
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(State.Name!, name);
            HasName = true;
            return this;
        }

        public IProtectiveStopBuilder WithActive(
            bool active,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                State.Active!,
                active,
                statusCode,
                timestamp);
            return this;
        }

        public IProtectiveStopBuilder WithEnabled(
            bool enabled,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(
                State.Enabled!,
                enabled,
                statusCode,
                timestamp);
            return this;
        }

        public IProtectiveStopBuilder BindActive(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(State.Active!, read);
            return this;
        }

        public IProtectiveStopBuilder BindEnabled(
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            Scope.EnsureMutable();
            RoboticsBuilderUtilities.BindRead(State.Enabled!, read);
            return this;
        }
    }
}
