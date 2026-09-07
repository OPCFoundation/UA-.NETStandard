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
    /// High-level client for a controller SystemOperation object.
    /// </summary>
    public sealed class SystemOperationClient
    {
        private readonly SystemOperationTypeClient? m_operation;
        private readonly NodeId m_controllerNodeId;
        private SystemOperationStateMachineTypeClient? m_stateMachine;

        /// <summary>
        /// Creates a SystemOperation client.
        /// </summary>
        public SystemOperationClient(ISession session, NodeId operationNodeId, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            OperationNodeId = operationNodeId.IsNull
                ? throw new ArgumentException("An operation NodeId is required.", nameof(operationNodeId))
                : operationNodeId;
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_operation = new SystemOperationTypeClient(Session, OperationNodeId, Telemetry);
            m_controllerNodeId = NodeId.Null;
        }

        internal SystemOperationClient(
            ISession session,
            NodeId controllerNodeId,
            ITelemetryContext telemetry,
            bool resolveFromController)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            m_controllerNodeId = controllerNodeId.IsNull
                ? throw new ArgumentException("A controller NodeId is required.", nameof(controllerNodeId))
                : controllerNodeId;
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            OperationNodeId = NodeId.Null;
            m_operation = null!;
        }

        /// <summary>
        /// Gets the connected session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the SystemOperation object NodeId.
        /// </summary>
        public NodeId OperationNodeId { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Calls GetReady on the generated SystemOperation state-machine proxy.
        /// </summary>
        public async ValueTask<int> GetReadyAsync(CancellationToken cancellationToken = default)
        {
            return await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .GetReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls Start on the generated SystemOperation state-machine proxy.
        /// </summary>
        public async ValueTask<int> StartAsync(CancellationToken cancellationToken = default)
        {
            return await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls Stop on the generated SystemOperation state-machine proxy.
        /// </summary>
        public async ValueTask<int> StopAsync(
            RoboticsStopMode stopMode,
            CancellationToken cancellationToken = default)
        {
            return await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .StopAsync((long)stopMode, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls StandDown on the generated SystemOperation state-machine proxy.
        /// </summary>
        public async ValueTask<int> StandDownAsync(CancellationToken cancellationToken = default)
        {
            return await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .StandDownAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current operation state.
        /// </summary>
        public async ValueTask<RoboticsOperationState> ReadStateAsync(
            CancellationToken cancellationToken = default)
        {
            FiniteStateSnapshot snapshot = await (await StateMachineAsync(cancellationToken).ConfigureAwait(false))
                .GetCurrentFiniteStateAsync(cancellationToken).ConfigureAwait(false);
            return ToOperationState(snapshot.CurrentState);
        }

        /// <summary>
        /// Streams current operation-state changes using the managed session default subscription.
        /// </summary>
        public IAsyncEnumerable<RoboticsOperationState> ObserveStateAsync(
            CancellationToken cancellationToken = default)
        {
            return ObserveStateAsync(RoboticsClient.GetDefaultStreaming(Session), cancellationToken);
        }

        /// <summary>
        /// Streams current operation-state changes over the supplied subscription.
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

        private async ValueTask<SystemOperationStateMachineTypeClient> StateMachineAsync(
            CancellationToken cancellationToken)
        {
            if (m_stateMachine != null)
            {
                return m_stateMachine;
            }
            SystemOperationTypeClient? operation = m_operation;
            if (operation == null)
            {
                ControllerTypeClient controller = new(Session, m_controllerNodeId, Telemetry);
                operation = await controller.GetSystemOperationAsync(Telemetry, cancellationToken)
                    .ConfigureAwait(false) ?? throw new ServiceResultException(
                        StatusCodes.BadNotFound,
                        $"Controller '{m_controllerNodeId}' does not expose SystemOperation.");
            }
            SystemOperationStateMachineTypeClient? stateMachine = await operation
                .GetSystemOperationStateMachineAsync(Telemetry, cancellationToken).ConfigureAwait(false);
            if (stateMachine == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"SystemOperation '{OperationNodeId}' does not expose SystemOperationStateMachine.");
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
