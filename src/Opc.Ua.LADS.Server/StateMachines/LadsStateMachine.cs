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

namespace Opc.Ua.LADS.Server.StateMachines
{
    /// <summary>
    /// Drives one LADS, AMB or Machinery finite state machine instance from its <see cref="LadsStateTable"/>.
    /// </summary>
    /// <remarks>
    /// The generated state machine classes do not override the stack's state and transition tables, so the
    /// stack's <c>DoTransition</c> cannot be used. The driver writes <c>CurrentState</c> and, when present,
    /// <c>LastTransition</c>, and runs the <see cref="FiniteStateMachineState.OnBeforeTransition"/> guard and
    /// the <see cref="FiniteStateMachineState.OnAfterTransition"/> observer around each committed move.
    /// </remarks>
    internal sealed class LadsStateMachine
    {
        /// <summary>
        /// Creates a driver for a state machine instance.
        /// </summary>
        /// <param name="stateMachine">The state machine instance.</param>
        /// <param name="context">The system context of the owning node manager.</param>
        /// <param name="table">The states and transitions of the state machine type.</param>
        public LadsStateMachine(FiniteStateMachineState stateMachine, ISystemContext context, LadsStateTable table)
        {
            StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            Table = table ?? throw new ArgumentNullException(nameof(table));
            int index = context.NamespaceUris.GetIndex(table.NamespaceUri);
            m_namespaceIndex = index < 0 ? (ushort)0 : (ushort)index;
        }

        /// <summary>
        /// Gets the driven state machine instance.
        /// </summary>
        public FiniteStateMachineState StateMachine { get; }

        /// <summary>
        /// Gets the table of the state machine type.
        /// </summary>
        public LadsStateTable Table { get; }

        /// <summary>
        /// Gets the identifier of the current state.
        /// </summary>
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
        /// Gets the name of the current state.
        /// </summary>
        public string CurrentStateName => Table.FindState(CurrentStateId).Name;

        /// <summary>
        /// Raised after a transition was committed, with the new state identifier.
        /// </summary>
        public event Action<uint>? StateChanged;

        /// <summary>
        /// Writes a state without a transition, the way a freshly created or reset machine starts.
        /// </summary>
        /// <param name="stateId">The state identifier.</param>
        public void SetInitialState(uint stateId)
        {
            LadsStateDefinition state = Table.FindState(stateId);
            lock (m_lock)
            {
                WriteStateLocked(state);
                m_currentStateId = state.Id;
            }
            StateMachine.ClearChangeMasks(m_context, true);
            StateChanged?.Invoke(state.Id);
        }

        /// <summary>
        /// Returns whether the table declares a transition from the current state to the target state.
        /// </summary>
        /// <param name="targetStateId">The target state identifier.</param>
        /// <returns><see langword="true"/> when the transition exists.</returns>
        public bool CanTransitionTo(uint targetStateId)
        {
            return Table.TryFindTransition(CurrentStateId, targetStateId, out _);
        }

        /// <summary>
        /// Applies the transition from the current state to <paramref name="targetStateId"/>.
        /// </summary>
        /// <param name="targetStateId">The target state identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><see langword="false"/> when a guard vetoed the transition.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when no transition leads to the target state.
        /// </exception>
        public ValueTask<bool> TransitionToAsync(uint targetStateId, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(TransitionTo(targetStateId, cancellationToken));
        }

        /// <summary>
        /// Applies the transition from the current state to <paramref name="targetStateId"/> synchronously.
        /// The guards of the state machine are synchronous, so no work is deferred.
        /// </summary>
        /// <param name="targetStateId">The target state identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><see langword="false"/> when a guard vetoed the transition.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when no transition leads to the target state.
        /// </exception>
        public bool TransitionTo(uint targetStateId, CancellationToken cancellationToken = default)
        {
            LadsStateDefinition target = Table.FindState(targetStateId);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LadsTransitionDefinition transition;
                lock (m_lock)
                {
                    if (!Table.TryFindTransition(m_currentStateId, target.Id, out transition))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "The state machine '{0}' cannot move from '{1}' to '{2}'.",
                            StateMachine.BrowseName,
                            Table.FindState(m_currentStateId).Name,
                            target.Name);
                    }
                }
                ServiceResult guard = Invoke(StateMachine.OnBeforeTransition, transition.Id);
                if (ServiceResult.IsBad(guard))
                {
                    return false;
                }
                bool committed = false;
                lock (m_lock)
                {
                    if (m_currentStateId == transition.FromStateId)
                    {
                        WriteStateLocked(target);
                        WriteTransitionLocked(transition);
                        m_currentStateId = target.Id;
                        committed = true;
                    }
                }
                if (!committed)
                {
                    continue;
                }
                StateMachine.ClearChangeMasks(m_context, true);
                Invoke(StateMachine.OnAfterTransition, transition.Id);
                StateChanged?.Invoke(target.Id);
                return true;
            }
        }

        /// <summary>
        /// Applies a sequence of transitions, for example through a transient state.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="targetStateIds">The states to visit in order.</param>
        /// <returns><see langword="false"/> when a guard vetoed one of the transitions.</returns>
        public async ValueTask<bool> TransitionThroughAsync(
            CancellationToken cancellationToken,
            params uint[] targetStateIds)
        {
            foreach (uint target in targetStateIds)
            {
                if (!await TransitionToAsync(target, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
            }
            return true;
        }

        private ServiceResult Invoke(StateMachineTransitionHandler? handler, uint transitionId)
        {
            if (handler == null)
            {
                return ServiceResult.Good;
            }
            try
            {
                return handler(
                    m_context,
                    StateMachine,
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
                    "A LADS state machine transition handler failed.");
            }
        }

        private void WriteStateLocked(LadsStateDefinition state)
        {
            FiniteStateVariableState? currentState = StateMachine.CurrentState;
            if (currentState == null)
            {
                return;
            }
            currentState.Value = new LocalizedText(state.Name);
            if (currentState.Id != null)
            {
                currentState.Id.Value = new NodeId(state.Id, m_namespaceIndex);
            }
            if (currentState.Number != null)
            {
                currentState.Number.Value = state.Number;
            }
        }

        private void WriteTransitionLocked(LadsTransitionDefinition transition)
        {
            FiniteTransitionVariableState? lastTransition = StateMachine.LastTransition;
            if (lastTransition == null)
            {
                return;
            }
            lastTransition.Value = new LocalizedText(transition.Name);
            if (lastTransition.Id != null)
            {
                lastTransition.Id.Value = new NodeId(transition.Id, m_namespaceIndex);
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

        private readonly ISystemContext m_context;
        private readonly ushort m_namespaceIndex;
        private readonly Lock m_lock = new();
        private uint m_currentStateId;
    }

    /// <summary>
    /// One state of a state machine type.
    /// </summary>
    /// <param name="Id">The numeric identifier of the state object in the model namespace.</param>
    /// <param name="Name">The state name, written as the value of <c>CurrentState</c>.</param>
    /// <param name="Number">The state number, written to <c>CurrentState/Number</c>.</param>
    internal readonly record struct LadsStateDefinition(uint Id, string Name, uint Number);

    /// <summary>
    /// One transition of a state machine type.
    /// </summary>
    /// <param name="Id">The numeric identifier of the transition object in the model namespace.</param>
    /// <param name="Name">The transition name, written as the value of <c>LastTransition</c>.</param>
    /// <param name="Number">The transition number, written to <c>LastTransition/Number</c>.</param>
    /// <param name="FromStateId">The identifier of the start state.</param>
    /// <param name="ToStateId">The identifier of the target state.</param>
    internal readonly record struct LadsTransitionDefinition(
        uint Id,
        string Name,
        uint Number,
        uint FromStateId,
        uint ToStateId);

    /// <summary>
    /// The states and transitions of one state machine type.
    /// </summary>
    /// <param name="NamespaceUri">The namespace of the state and transition objects.</param>
    /// <param name="States">The states.</param>
    /// <param name="Transitions">The transitions.</param>
    internal sealed record LadsStateTable(
        string NamespaceUri,
        LadsStateDefinition[] States,
        LadsTransitionDefinition[] Transitions)
    {
        /// <summary>
        /// Finds a state by identifier.
        /// </summary>
        /// <param name="stateId">The state identifier.</param>
        /// <returns>The state.</returns>
        /// <exception cref="ServiceResultException">The state is not part of the table.</exception>
        public LadsStateDefinition FindState(uint stateId)
        {
            foreach (LadsStateDefinition state in States)
            {
                if (state.Id == stateId)
                {
                    return state;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidArgument,
                "'{0}' is not a state of the state machine type.",
                stateId);
        }

        /// <summary>
        /// Finds a state by name.
        /// </summary>
        /// <param name="name">The state name.</param>
        /// <returns>The state.</returns>
        /// <exception cref="ServiceResultException">The state is not part of the table.</exception>
        public LadsStateDefinition FindState(string name)
        {
            foreach (LadsStateDefinition state in States)
            {
                if (string.Equals(state.Name, name, StringComparison.Ordinal))
                {
                    return state;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidArgument,
                "'{0}' is not a state of the state machine type.",
                name);
        }

        /// <summary>
        /// Finds the transition between two states.
        /// </summary>
        /// <param name="fromStateId">The start state identifier.</param>
        /// <param name="toStateId">The target state identifier.</param>
        /// <param name="transition">The transition when found.</param>
        /// <returns><see langword="true"/> when the transition exists.</returns>
        public bool TryFindTransition(uint fromStateId, uint toStateId, out LadsTransitionDefinition transition)
        {
            foreach (LadsTransitionDefinition candidate in Transitions)
            {
                if (candidate.FromStateId == fromStateId && candidate.ToStateId == toStateId)
                {
                    transition = candidate;
                    return true;
                }
            }
            transition = default;
            return false;
        }
    }
}
