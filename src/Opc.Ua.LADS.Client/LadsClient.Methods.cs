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

namespace Opc.Ua.LADS.Client
{
    public sealed partial class LadsClient
    {
        /// <summary>
        /// Writes a composed target, mode target or PID tuning member through its UA variable.
        /// Dynamic browse-name segments in the function-relative path must be URI-escaped.
        /// </summary>
        /// <param name="functionId">The function instance.</param>
        /// <param name="parameterPath">The function-relative parameter path.</param>
        /// <param name="value">The parameter value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask SetParameterAsync(
            NodeId functionId, string parameterPath, double value, CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionId, nameof(functionId));
            if (parameterPath == null)
            {
                throw new ArgumentNullException(nameof(parameterPath));
            }
            string[] segments = parameterPath.Split('/');
            NodeId parameter;
            if (segments.Length == 2 && segments[0] == BrowseNames.ControllerTuningParameter &&
                segments[1] is BrowseNames.CtrlP or BrowseNames.CtrlTi or BrowseNames.CtrlTd)
            {
                parameter = await RequireAsync(
                    functionId, cancellationToken, Lads(segments[0]), Lads(segments[1])).ConfigureAwait(false);
            }
            else if ((segments.Length == 2 && segments[0] == BrowseNames.TargetValueSet) ||
                (segments.Length == 3 && segments[0] == BrowseNames.ControllerModeSet &&
                    segments[2] == BrowseNames.TargetValue))
            {
                string name = Uri.UnescapeDataString(segments[1]);
                if (name.Length == 0 || Uri.EscapeDataString(name) != segments[1])
                {
                    throw new ArgumentException("The parameter name must be URI-escaped.", nameof(parameterPath));
                }
                NodeId set = await RequireAsync(functionId, cancellationToken, Lads(segments[0]))
                    .ConfigureAwait(false);
                List<ReferenceDescription> children = await BrowseAsync(
                    set, Ua.ReferenceTypeIds.HasComponent,
                    segments.Length == 2 ? NodeClass.Variable : NodeClass.Object, cancellationToken)
                    .ConfigureAwait(false);
                NodeId member = NodeId.Null;
                foreach (ReferenceDescription child in children)
                {
                    if (!string.Equals(child.BrowseName.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (!member.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                    }
                    member = ToNodeId(child.NodeId);
                    if (member.IsNull)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdUnknown, "The parameter has no local NodeId.");
                    }
                }
                if (member.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotFound, "The function has no parameter '{0}'.", parameterPath);
                }
                parameter = segments.Length == 2
                    ? member
                    : await RequireAsync(member, cancellationToken, Lads(BrowseNames.TargetValue))
                        .ConfigureAwait(false);
            }
            else
            {
                throw new ArgumentException(
                    "The parameter path is not a supported LADS member.", nameof(parameterPath));
            }
            WriteResponse response = await Session.WriteAsync(
                null,
                [new WriteValue
                {
                    NodeId = parameter,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(value))
                }],
                cancellationToken).ConfigureAwait(false);
            ValidateResponse(response.ResponseHeader, response.Results.Count, 1);
            if (StatusCode.IsBad(response.Results[0]))
            {
                throw new ServiceResultException(response.Results[0]);
            }
        }

        /// <summary>
        /// Discovers optional running methods with current state and user executability.
        /// A missing optional method is omitted; a failed attribute read is reported to the caller.
        /// </summary>
        public async ValueTask<ArrayOf<LadsRunningMethod>> GetRunningMethodsAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            NodeId running = await ResolveRunningStateAsync(functionalUnitId, cancellationToken).ConfigureAwait(false);
            if (running.IsNull)
            {
                return default;
            }
            string[] names =
            [
                BrowseNames.Hold, BrowseNames.Unhold, BrowseNames.Suspend,
                BrowseNames.Unsuspend, BrowseNames.Reset, BrowseNames.ToComplete
            ];
            var nodes = new List<NodeId>();
            var found = new List<string>();
            foreach (string name in names)
            {
                NodeId method = await ResolveAsync(running, cancellationToken, Lads(name)).ConfigureAwait(false);
                if (!method.IsNull)
                {
                    nodes.Add(method);
                    found.Add(name);
                }
            }
            NodeId[] nodeIds = nodes.ToArray();
            DataValue[] executable = await ReadAttributesAsync(nodeIds, Attributes.Executable, cancellationToken)
                .ConfigureAwait(false);
            DataValue[] userExecutable = await ReadAttributesAsync(
                nodeIds, Attributes.UserExecutable, cancellationToken)
                .ConfigureAwait(false);
            var methods = new LadsRunningMethod[nodes.Count];
            for (int index = 0; index < methods.Length; index++)
            {
                if (StatusCode.IsBad(executable[index].StatusCode))
                {
                    throw new ServiceResultException(executable[index].StatusCode);
                }
                if (StatusCode.IsBad(userExecutable[index].StatusCode))
                {
                    throw new ServiceResultException(userExecutable[index].StatusCode);
                }
                if (!executable[index].WrappedValue.TryGetValue(out bool canExecute) ||
                    !userExecutable[index].WrappedValue.TryGetValue(out bool userCanExecute))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                }
                methods[index] = new LadsRunningMethod(found[index], nodes[index], canExecute, userCanExecute);
            }
            return methods.ToArrayOf();
        }

        /// <summary>
        /// Stops a functional unit through its <c>FunctionalUnitState</c>.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask StopAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            FunctionalUnitStateMachineTypeClient state = await UnitStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await state.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Aborts a functional unit through its <c>FunctionalUnitState</c>.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask AbortAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            FunctionalUnitStateMachineTypeClient state = await UnitStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await state.AbortAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears a stopped or aborted functional unit through its <c>FunctionalUnitState</c>.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ClearAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            FunctionalUnitStateMachineTypeClient state = await UnitStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await state.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Holds the running program of a functional unit.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask HoldAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            RunningStateMachineTypeClient running = await RunningStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await running.HoldAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resumes the held program of a functional unit.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask UnholdAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            RunningStateMachineTypeClient running = await RunningStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await running.UnholdAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Suspends the running program of a functional unit.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask SuspendAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            RunningStateMachineTypeClient running = await RunningStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await running.SuspendAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resumes the suspended program of a functional unit.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask UnsuspendAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            RunningStateMachineTypeClient running = await RunningStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await running.UnsuspendAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resets the running state machine of a functional unit.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ResetAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            RunningStateMachineTypeClient running = await RunningStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await running.ResetAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asks a functional unit to run its program to completion.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ToCompleteAsync(NodeId functionalUnitId, CancellationToken cancellationToken = default)
        {
            RunningStateMachineTypeClient running = await RunningStateAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await running.ToCompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a control function through its <c>ControlFunctionState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the control function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask StartFunctionAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            ControlFunctionStateMachineTypeClient state = await ControlStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a control function with a new target value.
        /// </summary>
        /// <param name="functionId">The NodeId of the control function.</param>
        /// <param name="targetValue">The target value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask StartFunctionWithTargetValueAsync(
            NodeId functionId,
            Variant targetValue,
            CancellationToken cancellationToken = default)
        {
            ControlFunctionStateMachineTypeClient state = await ControlStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.StartWithTargetValueAsync(targetValue, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops a control function through its <c>ControlFunctionState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the control function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask StopFunctionAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            ControlFunctionStateMachineTypeClient state = await ControlStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Aborts a control function through its <c>ControlFunctionState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the control function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask AbortFunctionAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            ControlFunctionStateMachineTypeClient state = await ControlStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.AbortAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears an aborted control function through its <c>ControlFunctionState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the control function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ClearFunctionAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            ControlFunctionStateMachineTypeClient state = await ControlStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes the target value of an analog control function with a relative
        /// target value by an offset.
        /// </summary>
        /// <param name="functionId">The NodeId of the control function.</param>
        /// <param name="value">The offset to add to the target value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ModifyTargetValueByAsync(
            NodeId functionId,
            double value,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionId, nameof(functionId));
            var proxy = new AnalogControlFunctionWithRelativeTargetValueTypeClient(Session, functionId, Telemetry);
            await proxy.ModifyTargetValueByAsync(value, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resets the totalizer of an analog control function with a totalizer.
        /// </summary>
        /// <param name="functionId">The NodeId of the control function.</param>
        /// <param name="value">The value to reset the totalizer to.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ResetTotalizerAsync(
            NodeId functionId,
            double value = 0,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionId, nameof(functionId));
            var proxy = new AnalogControlFunctionWithTotalizerTypeClient(Session, functionId, Telemetry);
            await proxy.ResetTotalizerAsync(value, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a cover through its <c>CoverState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the cover function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask OpenCoverAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            CoverStateMachineTypeClient state = await CoverStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes a cover through its <c>CoverState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the cover function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask CloseCoverAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            CoverStateMachineTypeClient state = await CoverStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.CloseAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Locks a cover through its <c>CoverState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the cover function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask LockCoverAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            CoverStateMachineTypeClient state = await CoverStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.LockAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Unlocks a cover through its <c>CoverState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the cover function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask UnlockCoverAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            CoverStateMachineTypeClient state = await CoverStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.UnlockAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resets a cover in error through its <c>CoverState</c>.
        /// </summary>
        /// <param name="functionId">The NodeId of the cover function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ResetCoverAsync(NodeId functionId, CancellationToken cancellationToken = default)
        {
            CoverStateMachineTypeClient state = await CoverStateAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            await state.ResetAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves a device to the <c>Operate</c> state through its <c>DeviceState</c>.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask GotoOperateAsync(NodeId deviceId, CancellationToken cancellationToken = default)
        {
            LADSDeviceStateMachineTypeClient state = await DeviceStateAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            await state.GotoOperateAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves a device to the <c>Shutdown</c> state through its <c>DeviceState</c>.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask GotoShutdownAsync(NodeId deviceId, CancellationToken cancellationToken = default)
        {
            LADSDeviceStateMachineTypeClient state = await DeviceStateAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            await state.GotoShutdownAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves a device to the <c>Sleep</c> state through its <c>DeviceState</c>.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask GotoSleepAsync(NodeId deviceId, CancellationToken cancellationToken = default)
        {
            LADSDeviceStateMachineTypeClient state = await DeviceStateAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            await state.GotoSleepAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Switches a device to the <c>Processing</c> operation mode.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask GotoProcessingAsync(NodeId deviceId, CancellationToken cancellationToken = default)
        {
            LADSOperationModeStateMachineTypeClient mode = await OperationModeAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            await mode.GotoProcessingAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Switches a device to the <c>Maintenance</c> operation mode.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask GotoMaintenanceAsync(NodeId deviceId, CancellationToken cancellationToken = default)
        {
            LADSOperationModeStateMachineTypeClient mode = await OperationModeAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            await mode.GotoMaintenanceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Switches a device to the <c>Setup</c> operation mode.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask GotoSetupAsync(NodeId deviceId, CancellationToken cancellationToken = default)
        {
            LADSOperationModeStateMachineTypeClient mode = await OperationModeAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            await mode.GotoSetupAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a maintenance task.
        /// </summary>
        /// <param name="taskId">The NodeId of the maintenance task.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask StartMaintenanceTaskAsync(NodeId taskId, CancellationToken cancellationToken = default)
        {
            ThrowIfNull(taskId, nameof(taskId));
            await new MaintenanceTaskTypeClient(Session, taskId, Telemetry)
                .StartTaskAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops a maintenance task and records its outcome.
        /// </summary>
        /// <param name="taskId">The NodeId of the maintenance task.</param>
        /// <param name="result">The outcome of the task.</param>
        /// <param name="comment">An optional comment on the outcome.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask StopMaintenanceTaskAsync(
            NodeId taskId,
            MaintenanceTaskResultEnum result = MaintenanceTaskResultEnum.Success,
            string? comment = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(taskId, nameof(taskId));
            await new MaintenanceTaskTypeClient(Session, taskId, Telemetry)
                .StopTaskAsync(result, new LocalizedText(comment ?? string.Empty), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Resets a maintenance task.
        /// </summary>
        /// <param name="taskId">The NodeId of the maintenance task.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask ResetMaintenanceTaskAsync(NodeId taskId, CancellationToken cancellationToken = default)
        {
            ThrowIfNull(taskId, nameof(taskId));
            await new MaintenanceTaskTypeClient(Session, taskId, Telemetry)
                .ResetTaskAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a relative path and fails when it does not resolve.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when the node does not expose the path.
        /// </exception>
        internal async ValueTask<NodeId> RequireAsync(
            NodeId start,
            CancellationToken cancellationToken,
            params QualifiedName[] path)
        {
            ThrowIfNull(start, nameof(start));
            NodeId target = await ResolveAsync(start, cancellationToken, path).ConfigureAwait(false);
            if (target.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    $"The node {start} does not expose {string.Join("/", Array.ConvertAll(path, p => p.Name))}.");
            }
            return target;
        }

        private ValueTask<FunctionalUnitStateMachineTypeClient> UnitStateAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            return FunctionalUnitStateAsync(functionalUnitId, cancellationToken);
        }

        private async ValueTask<RunningStateMachineTypeClient> RunningStateAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken)
        {
            NodeId running = await ResolveRunningStateAsync(functionalUnitId, cancellationToken).ConfigureAwait(false);
            if (running.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            return new RunningStateMachineTypeClient(Session, running, Telemetry);
        }

        /// <summary>
        /// Resolves the running submachine of either a functional unit or a control function.
        /// </summary>
        internal async ValueTask<NodeId> ResolveRunningStateAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            ThrowIfNull(nodeId, nameof(nodeId));
            NodeId running = await ResolveAsync(nodeId, cancellationToken,
                Lads(BrowseNames.FunctionalUnitState), Lads(BrowseNames.RunningStateMachine)).ConfigureAwait(false);
            return running.IsNull
                ? await ResolveAsync(nodeId, cancellationToken,
                    Lads(BrowseNames.ControlFunctionState), Lads(BrowseNames.RunningStateMachine)).ConfigureAwait(false)
                : running;
        }

        private async ValueTask<ControlFunctionStateMachineTypeClient> ControlStateAsync(
            NodeId functionId,
            CancellationToken cancellationToken)
        {
            NodeId state = await RequireAsync(functionId, cancellationToken, Lads(BrowseNames.ControlFunctionState))
                .ConfigureAwait(false);
            return new ControlFunctionStateMachineTypeClient(Session, state, Telemetry);
        }

        private async ValueTask<CoverStateMachineTypeClient> CoverStateAsync(
            NodeId functionId,
            CancellationToken cancellationToken)
        {
            NodeId state = await RequireAsync(functionId, cancellationToken, Lads(BrowseNames.CoverState))
                .ConfigureAwait(false);
            return new CoverStateMachineTypeClient(Session, state, Telemetry);
        }

        private async ValueTask<LADSDeviceStateMachineTypeClient> DeviceStateAsync(
            NodeId deviceId,
            CancellationToken cancellationToken)
        {
            NodeId state = await RequireAsync(deviceId, cancellationToken, Lads(BrowseNames.DeviceState))
                .ConfigureAwait(false);
            return new LADSDeviceStateMachineTypeClient(Session, state, Telemetry);
        }

        private async ValueTask<LADSOperationModeStateMachineTypeClient> OperationModeAsync(
            NodeId deviceId,
            CancellationToken cancellationToken)
        {
            NodeId mode = await RequireAsync(
                deviceId,
                cancellationToken,
                Machinery(Opc.Ua.Machinery.BrowseNames.MachineryOperationMode)).ConfigureAwait(false);
            return new LADSOperationModeStateMachineTypeClient(Session, mode, Telemetry);
        }
    }
}
