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
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Injects the existing OPC 40083 provider interfaces without replacing their protocols.
    /// The drying authority's ReadAsync must expose their committed effects and newer revision.
    /// </summary>
    public sealed record GranulateDryingCommonProviders
    {
        /// <summary>
        /// Gets the typed monitored/controlled parameter provider.
        /// </summary>
        public IMonitoredParameterProvider? Parameters { get; init; }

        /// <summary>
        /// Gets the typed closed-loop and tuning provider.
        /// </summary>
        public IClosedLoopControlProvider? ClosedLoopControl { get; init; }

        /// <summary>
        /// Gets the typed device configuration, clock, and page-image provider.
        /// </summary>
        public IMachineConfigurationProvider? Configuration { get; init; }
    }

    public sealed partial class GranulateDryingDevice
    {
        /// <summary>
        /// Reuses common controllers on the already selected generated instances.
        /// </summary>
        private ValueTask BindCommonAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var access = new CommonAccess(this);
            var parameterProvider = new ParameterProvider(this, m_commonProviders.Parameters);
            var controlProvider = new ControlProvider(this, m_commonProviders.ClosedLoopControl);
            MachineConfigurationState configuration = State.MachineConfiguration!;
            ClearCommonWriteHandlers(configuration);
            m_resources.Add(new MachineConfigurationBinding(configuration, Context,
                new ConfigurationProvider(this, m_commonProviders.Configuration), access,
                externallyPublishedValues: true));
            m_boundMethods.Add(configuration.SetMachineTime!);
            if (configuration.GetPage != null)
            {
                m_boundMethods.Add(configuration.GetPage);
            }
            if (configuration.GetCurrentPage != null)
            {
                m_boundMethods.Add(configuration.GetCurrentPage);
            }
            foreach (NodeState node in GranulateDryingNodes.Walk(Context, State))
            {
                if (node is MonitoredParameterState parameter)
                {
                    ClearCommonWriteHandlers(parameter);
                    var binding = new MonitoredParameterBinding(
                        parameter, Context, parameterProvider, access,
                        () => ReadParameterSettings(parameter, Volatile.Read(ref m_current).Values));
                    m_parameters.Add(binding);
                    m_resources.Add(binding);
                    if (parameter.ResetMonitoring != null)
                    {
                        m_boundMethods.Add(parameter.ResetMonitoring);
                    }
                }
                else if (node is ClosedLoopControlState control)
                {
                    ClearCommonWriteHandlers(control);
                    m_resources.Add(new ClosedLoopControlBinding(
                        control, Context, controlProvider, access,
                        () => ReadCommittedBoolean(control.AutomaticControllerMode)));
                    if (control.AutoTuningOn != null)
                    {
                        m_boundMethods.Add(control.AutoTuningOn);
                    }
                    if (control.AutoTuningOff != null)
                    {
                        m_boundMethods.Add(control.AutoTuningOff);
                    }
                }
                else if (node is GDDMaintenanceState maintenance)
                {
                    m_resources.Add(new MaintenanceBinding(
                        maintenance, Context, new AttributedMaintenanceOnly(), access));
                    m_boundMethods.Add(maintenance.Reset!);
                }
            }
            return default;
        }

        /// <summary>
        /// Removes only the generic drying write hook from direct members owned by a common binder.
        /// </summary>
        private void ClearCommonWriteHandlers(NodeState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(Context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseVariableState variable)
                {
                    variable.OnSimpleWriteValueAsync = null;
                }
            }
        }

        /// <summary>
        /// Serializes a typed common mutation with GDD operations and refreshes its authoritative committed effects.
        /// </summary>
        /// <typeparam name="T">The common provider operation's result type.</typeparam>
        private async ValueTask<T> ExecuteCommonAsync<T>(
            PlasticsRubberRequestContext request,
            string member,
            Func<CancellationToken, ValueTask<T>> invoke,
            CancellationToken cancellationToken)
        {
            using GranulateDryingOperationLifetime.Lease pending = Pending(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                GranulateDryingCommand command = CommonCommand(request, member);
                WatchControlSession(command.SessionId);
                await AuthorizeAsync(request.SystemContext, command, pending.Token).ConfigureAwait(false);
                T result = await invoke(pending.Token).ConfigureAwait(false);
                GranulateDryingCommit commit = await m_provider.ReadAsync(pending.StoppingToken).ConfigureAwait(false);
                if (commit == null || commit.Revision <= m_current.Revision)
                {
                    throw Error(StatusCodes.BadInvalidState,
                        "A completed common operation must be visible in a newer authoritative drying commit.");
                }
                await ApplyCommitAsync(commit, null, pending.StoppingToken).ConfigureAwait(false);
                ClaimControl(request.SystemContext, command.SessionId);
                return result;
            }
            finally
            {
                EndControlSession();
                m_gate.Release();
            }
        }

        /// <summary>
        /// Adapts a parameterless common provider completion without introducing boxed return values.
        /// </summary>
        private async ValueTask ExecuteCommonAsync(
            PlasticsRubberRequestContext request,
            string member,
            Func<CancellationToken, ValueTask> invoke,
            CancellationToken cancellationToken)
        {
            await ExecuteCommonAsync(request, member, async ct =>
            {
                await invoke(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a common page without claiming a control lease or requiring a new process revision.
        /// </summary>
        /// <typeparam name="T">The provider's page result type.</typeparam>
        private async ValueTask<T> ReadCommonAsync<T>(
            PlasticsRubberRequestContext request,
            string member,
            Func<CancellationToken, ValueTask<T>> read,
            CancellationToken cancellationToken)
        {
            using GranulateDryingOperationLifetime.Lease pending = Pending(cancellationToken);
            await AuthorizeReadOnlyAsync(
                request.SystemContext, CommonCommand(request, member), pending.Token).ConfigureAwait(false);
            return await read(pending.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Authorizes a read-only method without treating an observer as a control owner.
        /// </summary>
        private async ValueTask AuthorizeReadOnlyAsync(
            ISystemContext context,
            GranulateDryingCommand command,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!m_current.RemoteControlEnabled)
            {
                throw Error(StatusCodes.BadNotExecutable, "RemoteControlOpcUa is disabled.");
            }
            StatusCode permission = await m_authorization.AuthorizeAsync(context, command, cancellationToken)
                .ConfigureAwait(false);
            if (!StatusCode.IsGood(permission))
            {
                throw new ServiceResultException(
                    StatusCode.IsBad(permission) ? permission : StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Preserves the common target's real generated position when authorizing it.
        /// </summary>
        private GranulateDryingCommand CommonCommand(PlasticsRubberRequestContext request, string member)
        {
            NodeState? target = null;
            foreach (NodeState node in GranulateDryingNodes.Walk(Context, State))
            {
                if (node.NodeId == request.TargetId)
                {
                    target = node;
                    break;
                }
            }
            if (target == null)
            {
                throw Error(StatusCodes.BadNodeIdInvalid, "The common capability does not belong to this device.");
            }
            (GranulateDryingTarget scope, string path) = Address(target);
            return new GranulateDryingCommand(GranulateDryingOperation.CommonOperation, scope)
            {
                SessionId = SessionId(request.SystemContext),
                Member = path.Length == 0 ? member : path + "/" + member
            };
        }

        /// <summary>
        /// Resolves the nearest standard placeholder owner of an existing generated member.
        /// </summary>
        private (GranulateDryingTarget Target, string Path) Address(NodeState node)
        {
            var names = new Stack<string>();
            for (NodeState? current = node; current != null; current = (current as BaseInstanceState)?.Parent)
            {
                foreach (KeyValuePair<GranulateDryingTarget, BaseObjectState> target in m_targets)
                {
                    if (ReferenceEquals(current, target.Value))
                    {
                        return (target.Key, string.Join("/", names));
                    }
                }
                names.Push(current.BrowseName.Name ?? string.Empty);
            }
            throw Error(StatusCodes.BadNodeIdInvalid, "The generated member is outside the device graph.");
        }

        /// <summary>
        /// Evaluates common monitored-parameter rules against one staged GDD commit, using the shared evaluator.
        /// </summary>
        private void ValidateParameterValues(Dictionary<NodeId, DataValue> values)
        {
            foreach (MonitoredParameterBinding binding in m_parameters)
            {
                MonitoredParameterLimits limits = ReadParameterSettings(binding.Node, values).Limits;
                try
                {
                    limits.Validate();
                }
                catch (ArgumentException exception)
                {
                    throw ServiceResultException.Create(StatusCodes.BadOutOfRange, exception,
                        "The committed monitored-parameter limits are inconsistent.");
                }
                if (binding.Node.Status != null)
                {
                    DataValue actual = values[binding.Node.ActualValue!.NodeId];
                    double number = actual.WrappedValue.TryGetValue(out double measured) ? measured : double.NaN;
                    ushort status = limits.Evaluate(number, actual.StatusCode);
                    values[binding.Node.Status.NodeId] = new DataValue(
                        Variant.From(status), StatusCodes.Good, actual.SourceTimestamp);
                }
            }
        }

        /// <summary>
        /// Captures parameter validation inputs from one committed or staged drying image.
        /// </summary>
        private static (MonitoredParameterLimits Limits, bool AutomaticMonitoring) ReadParameterSettings(
            MonitoredParameterState parameter, Dictionary<NodeId, DataValue> values)
        {
            var limits = new MonitoredParameterLimits
            {
                SetValue = Number(parameter.SetValue),
                Minimum = Number(parameter.MinValue),
                Maximum = Number(parameter.MaxValue),
                LowerTolerance = Number(parameter.LowerTolerance),
                UpperTolerance = Number(parameter.UpperTolerance),
                LowerTolerance2 = Number(parameter.LowerTolerance2),
                UpperTolerance2 = Number(parameter.UpperTolerance2)
            };
            bool automatic = parameter.AutomaticMonitoring != null &&
                values[parameter.AutomaticMonitoring.NodeId].WrappedValue.TryGetValue(out bool enabled) && enabled;
            return (limits, automatic);

            double? Number(BaseVariableState? variable)
            {
                return variable != null && values[variable.NodeId].WrappedValue.TryGetValue(out double value)
                    ? value : null;
            }
        }

        /// <summary>
        /// Reads an optional controller mode without consulting the UA write-through cache.
        /// </summary>
        private bool ReadCommittedBoolean(BaseVariableState? variable)
        {
            return variable != null &&
                Volatile.Read(ref m_current).Values[variable.NodeId].WrappedValue.TryGetValue(out bool enabled) &&
                enabled;
        }

        /// <summary>
        /// Shares original authenticated context and device-wide remote/ownership policy with common binders.
        /// </summary>
        private sealed class CommonAccess : IPlasticsRubberAccessPolicy
        {
            /// <summary>
            /// Creates an access adapter for one physical device.
            /// </summary>
            internal CommonAccess(GranulateDryingDevice owner)
            {
                m_owner = owner;
            }

            /// <inheritdoc/>
            public ValueTask AuthorizeAsync(
                PlasticsRubberRequestContext context,
                NodeId memberId,
                CancellationToken cancellationToken = default)
            {
                NodeState? member = m_owner.m_runtime.Manager.FindPredefinedNode(memberId);
                string name = member?.BrowseName.Name ?? memberId.ToString();
                return name is "GetPage" or "GetCurrentPage"
                    ? m_owner.AuthorizeReadOnlyAsync(
                        context.SystemContext, m_owner.CommonCommand(context, name), cancellationToken)
                    : m_owner.AuthorizeAsync(
                    context.SystemContext, m_owner.CommonCommand(context, name), cancellationToken);
            }

            /// <summary>
            /// The physical-device runtime.
            /// </summary>
            private readonly GranulateDryingDevice m_owner;
        }

        /// <summary>
        /// Adapts common parameter providers to one committed GDD state authority.
        /// </summary>
        private sealed class ParameterProvider : IMonitoredParameterProvider
        {
            /// <summary>
            /// Creates the provider adapter without taking ownership of hardware services.
            /// </summary>
            internal ParameterProvider(GranulateDryingDevice owner, IMonitoredParameterProvider? provider)
            {
                m_owner = owner;
                m_provider = provider;
            }

            /// <summary>
            /// Gets an explicitly provided hardware implementation.
            /// </summary>
            private IMonitoredParameterProvider Provider => m_provider ??
                throw Error(StatusCodes.BadNotSupported, "No common parameter provider is configured.");

            /// <inheritdoc/>
            public ValueTask WriteSettingAsync(
                PlasticsRubberRequestContext context,
                MonitoredParameterSetting setting,
                double value,
                CancellationToken cancellationToken = default)
            {
                string member = setting switch
                {
                    MonitoredParameterSetting.Minimum => PlasticsRubber.BrowseNames.MinValue,
                    MonitoredParameterSetting.Maximum => PlasticsRubber.BrowseNames.MaxValue,
                    MonitoredParameterSetting.RampUp => PlasticsRubber.BrowseNames.SetRampUp,
                    MonitoredParameterSetting.RampDown => PlasticsRubber.BrowseNames.SetRampDown,
                    _ => setting.ToString()
                };
                return m_owner.ExecuteCommonAsync(context, member,
                    ct => Provider.WriteSettingAsync(context, setting, value, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SetAutomaticMonitoringAsync(
                PlasticsRubberRequestContext context, bool automatic, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "AutomaticMonitoring",
                    ct => Provider.SetAutomaticMonitoringAsync(context, automatic, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SetSensitivityAsync(
                PlasticsRubberRequestContext context, ushort sensitivity, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "MonitoringSensitivity",
                    ct => Provider.SetSensitivityAsync(context, sensitivity, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SetAlarmSuppressionAsync(
                PlasticsRubberRequestContext context, ushort suppression, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "AlarmSuppression",
                    ct => Provider.SetAlarmSuppressionAsync(context, suppression, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<MonitoredParameterLimits> ResetMonitoringAsync(
                PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "ResetMonitoring",
                    ct => Provider.ResetMonitoringAsync(context, ct), cancellationToken);
            }

            /// <summary>
            /// The GDD publication and authorization module.
            /// </summary>
            private readonly GranulateDryingDevice m_owner;

            /// <summary>
            /// The externally owned common provider.
            /// </summary>
            private readonly IMonitoredParameterProvider? m_provider;
        }

        /// <summary>
        /// Adapts closed-loop providers without implementing a second controller or tuning algorithm.
        /// </summary>
        private sealed class ControlProvider : IClosedLoopControlProvider
        {
            /// <summary>
            /// Creates an adapter for one device's common controls.
            /// </summary>
            internal ControlProvider(GranulateDryingDevice owner, IClosedLoopControlProvider? provider)
            {
                m_owner = owner;
                m_provider = provider;
            }

            /// <summary>
            /// Gets the explicitly provided controller.
            /// </summary>
            private IClosedLoopControlProvider Provider => m_provider ??
                throw Error(StatusCodes.BadNotSupported, "No closed-loop provider is configured.");

            /// <inheritdoc/>
            public ValueTask SetParametersAsync(
                PlasticsRubberRequestContext context,
                ArrayOf<PIDParametersDataType> parameters,
                CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "PIDParameters",
                    ct => Provider.SetParametersAsync(context, parameters, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SetAutomaticModeAsync(
                PlasticsRubberRequestContext context, bool automatic, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "AutomaticControllerMode",
                    ct => Provider.SetAutomaticModeAsync(context, automatic, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask StartTuningAsync(
                PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "AutoTuningOn",
                    ct => Provider.StartTuningAsync(context, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask StopTuningAsync(
                PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "AutoTuningOff",
                    ct => Provider.StopTuningAsync(context, ct), cancellationToken);
            }

            /// <summary>
            /// The GDD publication and authorization module.
            /// </summary>
            private readonly GranulateDryingDevice m_owner;

            /// <summary>
            /// The externally owned common controller provider.
            /// </summary>
            private readonly IClosedLoopControlProvider? m_provider;
        }

        /// <summary>
        /// Enforces OPC 40082-6's requirement to use the attributed GDD reset instead of common Reset.
        /// </summary>
        private sealed class AttributedMaintenanceOnly : IMaintenanceProvider
        {
            /// <inheritdoc/>
            public ValueTask ResetAsync(
                PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
            {
                throw Error(StatusCodes.BadNotSupported,
                    "Use GDDMaintenanceReset with operator and machine attribution.");
            }
        }

        /// <summary>
        /// Adapts the existing common configuration binder to the drying authority.
        /// </summary>
        private sealed class ConfigurationProvider : IMachineConfigurationProvider
        {
            /// <summary>
            /// Creates the configuration adapter.
            /// </summary>
            internal ConfigurationProvider(GranulateDryingDevice owner, IMachineConfigurationProvider? provider)
            {
                m_owner = owner;
                m_provider = provider;
            }

            /// <summary>
            /// Gets the explicitly supplied physical-device implementation.
            /// </summary>
            private IMachineConfigurationProvider Provider => m_provider ??
                throw Error(StatusCodes.BadNotSupported, "No device configuration provider is configured.");

            /// <inheritdoc/>
            public ValueTask SetMachineNameAsync(
                PlasticsRubberRequestContext context, string? name, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "UserMachineName",
                    ct => Provider.SetMachineNameAsync(context, name, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SetLocationNameAsync(
                PlasticsRubberRequestContext context, string? name, CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "LocationName",
                    ct => Provider.SetLocationNameAsync(context, name, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SetTimeZoneAsync(
                PlasticsRubberRequestContext context,
                TimeZoneDataType timeZone,
                CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "TimeZoneOffset",
                    ct => Provider.SetTimeZoneAsync(context, timeZone, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SetMachineTimeAsync(
                PlasticsRubberRequestContext context,
                DateTimeUtc utcTime,
                TimeZoneDataType timeZone,
                CancellationToken cancellationToken = default)
            {
                return m_owner.ExecuteCommonAsync(context, "SetMachineTime",
                    ct => Provider.SetMachineTimeAsync(context, utcTime, timeZone, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<ByteString> GetPageAsync(
                PlasticsRubberRequestContext context, string id, CancellationToken cancellationToken = default)
            {
                return m_owner.ReadCommonAsync(context, "GetPage",
                    ct => Provider.GetPageAsync(context, id, ct), cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<ByteString> GetCurrentPageAsync(
                PlasticsRubberRequestContext context,
                uint visualizationUnit,
                CancellationToken cancellationToken = default)
            {
                return m_owner.ReadCommonAsync(context, "GetCurrentPage",
                    ct => Provider.GetCurrentPageAsync(context, visualizationUnit, ct), cancellationToken);
            }

            /// <summary>
            /// The GDD publication and authorization module.
            /// </summary>
            private readonly GranulateDryingDevice m_owner;

            /// <summary>
            /// The externally owned device configuration provider.
            /// </summary>
            private readonly IMachineConfigurationProvider? m_provider;
        }

        /// <summary>
        /// The reused common parameter bindings.
        /// </summary>
        private readonly List<MonitoredParameterBinding> m_parameters = [];
    }
}
