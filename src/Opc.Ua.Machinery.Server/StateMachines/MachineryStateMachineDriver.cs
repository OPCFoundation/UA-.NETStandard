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

namespace Opc.Ua.Machinery.Server.StateMachines
{
    /// <summary>
    /// Applies a transition to one of the two OPC 40001-1 state machines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source generator emits <c>MachineryItemState_StateMachineState</c>
    /// and <c>MachineryOperationModeStateMachineState</c> as plain
    /// <see cref="FiniteStateMachineState"/> subclasses: it does not override
    /// <c>StateTable</c> / <c>TransitionTable</c> / <c>TransitionMappings</c>,
    /// so the stack's <c>SetState</c> and <c>DoTransition</c> have nothing to
    /// look the state up in and write nothing. The tables live here instead,
    /// built from the generated
    /// <c>MachineryItemState_StateMachineTypeIds</c> /
    /// <c>MachineryOperationModeStateMachineTypeIds</c> constants so they
    /// cannot drift from the model.
    /// </para>
    /// <para>
    /// The driver still composes with the repository's state-machine
    /// lifecycle surface: it invokes the machine's
    /// <see cref="FiniteStateMachineState.OnBeforeTransition"/> and
    /// <see cref="FiniteStateMachineState.OnAfterTransition"/> delegates around
    /// the variable update, which is exactly where
    /// <c>StateMachineBuilder.For(...)</c> installs its guards and observers.
    /// </para>
    /// </remarks>
    internal sealed class MachineryStateMachineDriver
    {
        public MachineryStateMachineDriver(
            FiniteStateMachineState stateMachine,
            ISystemContext context,
            ushort modelNamespaceIndex,
            IReadOnlyList<StateDefinition> states,
            IReadOnlyList<TransitionDefinition> transitions)
        {
            m_stateMachine = stateMachine ??
                throw new ArgumentNullException(nameof(stateMachine));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_modelNamespaceIndex = modelNamespaceIndex;
            m_states = states ?? throw new ArgumentNullException(nameof(states));
            m_transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        }

        public NodeId NodeId => m_stateMachine.NodeId;

        public uint CurrentStateId
        {
            get
            {
                lock (m_lock)
                {
                    return m_currentStateId;
                }
            }
        }

        /// <summary>
        /// Writes the initial state without firing any lifecycle handler, the
        /// way <c>SetState</c> does for a machine that has just been created.
        /// </summary>
        public void SetInitialState(uint stateId)
        {
            StateDefinition state = FindState(stateId);
            lock (m_lock)
            {
                WriteStateLocked(state);
                m_currentStateId = state.Id;
            }
            m_stateMachine.ClearChangeMasks(m_context, true);
        }

        /// <summary>
        /// Applies the transition from the current state to
        /// <paramref name="targetStateId"/>.
        /// </summary>
        /// <returns>
        /// <see langword="false"/> when a pre-transition guard vetoed the move.
        /// </returns>
        /// <remarks>
        /// Completes synchronously; the <see cref="ValueTask{TResult}"/> shape
        /// is that of the public state controller interfaces this backs.
        /// </remarks>
        public ValueTask<bool> TransitionToAsync(
            uint targetStateId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            StateDefinition target = FindState(targetStateId);
            TransitionDefinition transition;
            lock (m_lock)
            {
                transition = FindTransition(m_currentStateId, target.Id);
            }

            ServiceResult guard = Invoke(
                m_stateMachine.OnBeforeTransition,
                transition.Id);
            if (ServiceResult.IsBad(guard))
            {
                return new ValueTask<bool>(false);
            }

            lock (m_lock)
            {
                WriteStateLocked(target);
                WriteTransitionLocked(transition);
                m_currentStateId = target.Id;
            }
            m_stateMachine.ClearChangeMasks(m_context, true);

            Invoke(m_stateMachine.OnAfterTransition, transition.Id);
            return new ValueTask<bool>(true);
        }

        private ServiceResult Invoke(
            StateMachineTransitionHandler? handler,
            uint transitionId)
        {
            if (handler == null)
            {
                return ServiceResult.Good;
            }
            try
            {
                return handler(
                    m_context,
                    m_stateMachine,
                    transitionId,
                    causeId: 0,
                    inputArguments: default,
                    outputArguments: null);
            }
            catch (Exception ex)
            {
                return ServiceResult.Create(
                    ex,
                    StatusCodes.BadInternalError,
                    "A Machinery state-machine transition handler failed.");
            }
        }

        private void WriteStateLocked(StateDefinition state)
        {
            FiniteStateVariableState? currentState = m_stateMachine.CurrentState;
            if (currentState == null)
            {
                return;
            }
            currentState.Value = new LocalizedText(state.Name);
            if (currentState.Id != null)
            {
                currentState.Id.Value = new NodeId(state.Id, m_modelNamespaceIndex);
            }
            if (currentState.Number != null)
            {
                currentState.Number.Value = state.Number;
            }
        }

        private void WriteTransitionLocked(TransitionDefinition transition)
        {
            FiniteTransitionVariableState? lastTransition = m_stateMachine.LastTransition;
            if (lastTransition == null)
            {
                return;
            }
            lastTransition.Value = new LocalizedText(transition.Name);
            if (lastTransition.Id != null)
            {
                lastTransition.Id.Value = new NodeId(transition.Id, m_modelNamespaceIndex);
            }
            if (lastTransition.Number != null)
            {
                lastTransition.Number.Value = transition.Number;
            }
            if (lastTransition.TransitionTime != null)
            {
                lastTransition.TransitionTime.Value = DateTime.UtcNow;
            }
        }

        private StateDefinition FindState(uint stateId)
        {
            for (int ii = 0; ii < m_states.Count; ii++)
            {
                if (m_states[ii].Id == stateId)
                {
                    return m_states[ii];
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidArgument,
                "'{0}' is not a state of the Machinery state machine '{1}'.",
                stateId,
                m_stateMachine.BrowseName);
        }

        private TransitionDefinition FindTransition(uint fromStateId, uint toStateId)
        {
            for (int ii = 0; ii < m_transitions.Count; ii++)
            {
                TransitionDefinition candidate = m_transitions[ii];
                if (candidate.FromStateId == fromStateId &&
                    candidate.ToStateId == toStateId)
                {
                    return candidate;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "The Machinery state machine '{0}' declares no transition from state " +
                "'{1}' to state '{2}'.",
                m_stateMachine.BrowseName,
                fromStateId,
                toStateId);
        }

        /// <summary>
        /// One state of an OPC 40001-1 state machine, in the form the driver
        /// writes it to <c>CurrentState</c>.
        /// </summary>
        /// <param name="Id">
        /// The numeric identifier of the state object in the model namespace,
        /// written to <c>CurrentState/Id</c>.
        /// </param>
        /// <param name="Name">
        /// The state name, written as the value of <c>CurrentState</c>.
        /// </param>
        /// <param name="Number">
        /// The state number, written to <c>CurrentState/Number</c>.
        /// </param>
        internal readonly record struct StateDefinition(uint Id, string Name, uint Number);

        /// <summary>
        /// One transition of an OPC 40001-1 state machine — the move from one
        /// state to another, or a self-transition — in the form the driver
        /// writes it to <c>LastTransition</c>.
        /// </summary>
        /// <param name="Id">
        /// The numeric identifier of the transition object in the model
        /// namespace, written to <c>LastTransition/Id</c>.
        /// </param>
        /// <param name="Name">
        /// The transition name, written as the value of <c>LastTransition</c>.
        /// </param>
        /// <param name="Number">
        /// The transition number, written to <c>LastTransition/Number</c>.
        /// </param>
        /// <param name="FromStateId">The identifier of the start state.</param>
        /// <param name="ToStateId">The identifier of the target state.</param>
        internal readonly record struct TransitionDefinition(
            uint Id,
            string Name,
            uint Number,
            uint FromStateId,
            uint ToStateId);

        private readonly FiniteStateMachineState m_stateMachine;
        private readonly ISystemContext m_context;
        private readonly ushort m_modelNamespaceIndex;
        private readonly IReadOnlyList<StateDefinition> m_states;
        private readonly IReadOnlyList<TransitionDefinition> m_transitions;
        private readonly Lock m_lock = new();
        private uint m_currentStateId;
    }
}
