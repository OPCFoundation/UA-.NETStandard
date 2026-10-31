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

namespace Opc.Ua.LADS.Client
{
    public sealed partial class LadsClient
    {
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
            NodeId running = await RequireAsync(
                functionalUnitId,
                cancellationToken,
                Lads(BrowseNames.FunctionalUnitState),
                Lads(BrowseNames.RunningStateMachine)).ConfigureAwait(false);
            return new RunningStateMachineTypeClient(Session, running, Telemetry);
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
