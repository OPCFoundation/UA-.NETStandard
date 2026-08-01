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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.Robotics.Client
{
    /// <summary>
    /// High-level client for a TaskControl object and its operation state machine.
    /// </summary>
    public sealed class TaskControlClient
    {
        private readonly TaskControlTypeClient m_taskControl;
        private TaskControlOperationTypeClient? m_operation;
        private TaskControlStateMachineTypeClient? m_stateMachine;

        /// <summary>
        /// Creates a TaskControl client.
        /// </summary>
        public TaskControlClient(ISession session, NodeId taskControlNodeId, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            TaskControlNodeId = taskControlNodeId.IsNull
                ? throw new ArgumentException("A task-control NodeId is required.", nameof(taskControlNodeId))
                : taskControlNodeId;
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_taskControl = new TaskControlTypeClient(Session, TaskControlNodeId, Telemetry);
        }

        /// <summary>
        /// Gets the connected session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the TaskControl object NodeId.
        /// </summary>
        public NodeId TaskControlNodeId { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Loads a program by name.
        /// </summary>
        public async ValueTask<RoboticsProgramResult> LoadByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            int status = await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .LoadByNameAsync(name ?? string.Empty, cancellationToken).ConfigureAwait(false);
            return new RoboticsProgramResult { Status = status };
        }

        /// <summary>
        /// Loads a program by NodeId.
        /// </summary>
        public async ValueTask<RoboticsProgramResult> LoadByNodeIdAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            int status = await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .LoadByNodeIdAsync(nodeId, cancellationToken).ConfigureAwait(false);
            return new RoboticsProgramResult { Status = status };
        }

        /// <summary>
        /// Unloads a program by name.
        /// </summary>
        public async ValueTask<RoboticsProgramResult> UnloadByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            int status = await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .UnloadByNameAsync(name ?? string.Empty, cancellationToken).ConfigureAwait(false);
            return new RoboticsProgramResult { Status = status };
        }

        /// <summary>
        /// Unloads a program by NodeId.
        /// </summary>
        public async ValueTask<RoboticsProgramResult> UnloadByNodeIdAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            int status = await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .UnloadByNodeIdAsync(nodeId, cancellationToken).ConfigureAwait(false);
            return new RoboticsProgramResult { Status = status };
        }

        /// <summary>
        /// Unloads the current program.
        /// </summary>
        public async ValueTask<RoboticsProgramResult> UnloadProgramAsync(
            CancellationToken cancellationToken = default)
        {
            int status = await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .UnloadProgramAsync(cancellationToken).ConfigureAwait(false);
            return new RoboticsProgramResult { Status = status };
        }

        /// <summary>
        /// Resets execution to the start of the loaded program.
        /// </summary>
        public async ValueTask<int> ResetToProgramStartAsync(CancellationToken cancellationToken = default)
        {
            TaskControlStateMachineTypeClient stateMachine = await StateMachineAsync(cancellationToken)
                .ConfigureAwait(false);
            StateTypeClient? ready = await stateMachine.GetReadyAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            if (ready == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"TaskControl '{TaskControlNodeId}' does not expose Ready.");
            }
            ReadySubstateMachineTypeClient readySubstate = new(Session, ready.ObjectId, Telemetry);
            return await readySubstate.ResetToProgramStartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts task execution.
        /// </summary>
        public async ValueTask<int> StartAsync(CancellationToken cancellationToken = default)
        {
            return await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops task execution.
        /// </summary>
        public async ValueTask<int> StopAsync(
            RoboticsStopMode stopMode,
            CancellationToken cancellationToken = default)
        {
            return await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .StopAsync((long)stopMode, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current task-control operation state.
        /// </summary>
        public async ValueTask<RoboticsOperationState> ReadStateAsync(
            CancellationToken cancellationToken = default)
        {
            FiniteStateSnapshot snapshot = await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .GetCurrentFiniteStateAsync(cancellationToken).ConfigureAwait(false);
            return ToOperationState(snapshot.CurrentState);
        }

        /// <summary>
        /// Streams task-control operation-state changes using the managed session default subscription.
        /// </summary>
        public IAsyncEnumerable<RoboticsOperationState> ObserveStateAsync(
            CancellationToken cancellationToken = default)
        {
            return ObserveStateAsync(RoboticsClient.GetDefaultStreaming(Session), cancellationToken);
        }

        /// <summary>
        /// Streams task-control operation-state changes over the supplied subscription.
        /// </summary>
        public async IAsyncEnumerable<RoboticsOperationState> ObserveStateAsync(
            IStreamingSubscription streaming,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (FiniteStateSnapshot snapshot in (await StateMachineAsync(cancellationToken)
                .ConfigureAwait(false)).ObserveFiniteTransitionsAsync(streaming, null, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return ToOperationState(snapshot.CurrentState);
            }
        }

        private async ValueTask<TaskControlOperationTypeClient> OperationAsync(CancellationToken cancellationToken)
        {
            if (m_operation != null)
            {
                return m_operation;
            }
            TaskControlOperationTypeClient? operation = await m_taskControl
                .GetTaskControlOperationAsync(Telemetry, cancellationToken).ConfigureAwait(false);
            if (operation == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"TaskControl '{TaskControlNodeId}' does not expose TaskControlOperation.");
            }
            m_operation = operation;
            return operation;
        }

        private async ValueTask<TaskControlStateMachineTypeClient> StateMachineAsync(
            CancellationToken cancellationToken)
        {
            if (m_stateMachine != null)
            {
                return m_stateMachine;
            }
            TaskControlStateMachineTypeClient? stateMachine = await (await OperationAsync(cancellationToken)
                .ConfigureAwait(false)).GetTaskControlStateMachineAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            if (stateMachine == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"TaskControl '{TaskControlNodeId}' does not expose TaskControlStateMachine.");
            }
            m_stateMachine = stateMachine;
            return stateMachine;
        }

        private static RoboticsOperationState ToOperationState(LocalizedText state)
        {
            string? text = state.Text;
            return text switch
            {
                BrowseNames.Ready => RoboticsOperationState.Ready,
                BrowseNames.Executing => RoboticsOperationState.Executing,
                _ => RoboticsOperationState.Idle
            };
        }
    }
}
