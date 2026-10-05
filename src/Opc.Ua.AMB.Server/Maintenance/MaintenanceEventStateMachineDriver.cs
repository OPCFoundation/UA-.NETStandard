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

namespace Opc.Ua.AMB.Server.Maintenance
{
    /// <summary>
    /// The states and transitions of the <c>MaintenanceEventStateMachineType</c>
    /// (OPC 10000-110 §12.3), built from the generated identifiers so a model
    /// revision that changes them fails the build.
    /// </summary>
    /// <remarks>
    /// The generated state machine classes do not override
    /// <c>StateTable</c>/<c>TransitionTable</c>, so the base class cannot
    /// drive them; the driver below writes the state variables itself.
    /// </remarks>
    internal static class MaintenanceEventStateMachineTables
    {
        /// <summary>The states.</summary>
        public static readonly StateDefinition[] States =
        [
            new(
                MaintenanceEventStateMachineTypeIds.StateIds.Planned,
                BrowseNames.Planned,
                MaintenanceEventStateMachineTypeIds.StateNumbers.Planned),
            new(
                MaintenanceEventStateMachineTypeIds.StateIds.Executing,
                BrowseNames.Executing,
                MaintenanceEventStateMachineTypeIds.StateNumbers.Executing),
            new(
                MaintenanceEventStateMachineTypeIds.StateIds.Finished,
                BrowseNames.Finished,
                MaintenanceEventStateMachineTypeIds.StateNumbers.Finished)
        ];

        /// <summary>The transitions.</summary>
        public static readonly TransitionDefinition[] Transitions =
        [
            new(
                MaintenanceEventStateMachineTypeIds.TransitionIds.FromPlannedToExecuting,
                BrowseNames.FromPlannedToExecuting,
                MaintenanceEventStateMachineTypeIds.TransitionNumbers.FromPlannedToExecuting,
                MaintenanceEventStateMachineTypeIds.StateIds.Planned,
                MaintenanceEventStateMachineTypeIds.StateIds.Executing),
            new(
                MaintenanceEventStateMachineTypeIds.TransitionIds.FromExecutingToFinished,
                BrowseNames.FromExecutingToFinished,
                MaintenanceEventStateMachineTypeIds.TransitionNumbers.FromExecutingToFinished,
                MaintenanceEventStateMachineTypeIds.StateIds.Executing,
                MaintenanceEventStateMachineTypeIds.StateIds.Finished),
            new(
                MaintenanceEventStateMachineTypeIds.TransitionIds.FromFinishedToPlanned,
                BrowseNames.FromFinishedToPlanned,
                MaintenanceEventStateMachineTypeIds.TransitionNumbers.FromFinishedToPlanned,
                MaintenanceEventStateMachineTypeIds.StateIds.Finished,
                MaintenanceEventStateMachineTypeIds.StateIds.Planned)
        ];

        /// <summary>
        /// Finds the state with a state number.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a state of the machine.</exception>
        public static StateDefinition StateOf(MaintenanceStateKind kind)
        {
            foreach (StateDefinition state in States)
            {
                if (state.Number == (uint)kind)
                {
                    return state;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        /// <summary>
        /// Finds the state with the numeric id of its node.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">No state node has the <paramref name="id"/>.</exception>
        public static StateDefinition StateById(uint id)
        {
            foreach (StateDefinition state in States)
            {
                if (state.Id == id)
                {
                    return state;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(id), id, null);
        }

        /// <summary>A state of the machine.</summary>
        /// <param name="Id">The numeric id of the state node in the AMB namespace.</param>
        /// <param name="Name">The browse name of the state.</param>
        /// <param name="Number">The state number.</param>
        internal readonly record struct StateDefinition(uint Id, string Name, uint Number);

        /// <summary>A transition of the machine.</summary>
        /// <param name="Id">The numeric id of the transition node in the AMB namespace.</param>
        /// <param name="Name">The browse name of the transition.</param>
        /// <param name="Number">The transition number.</param>
        /// <param name="FromStateId">The id of the state the transition leaves.</param>
        /// <param name="ToStateId">The id of the state the transition enters.</param>
        internal readonly record struct TransitionDefinition(
            uint Id,
            string Name,
            uint Number,
            uint FromStateId,
            uint ToStateId);
    }

    /// <summary>
    /// Moves the <c>MaintenanceState</c> of a maintenance condition along the
    /// transitions of the model, after the pattern of the Machinery driver.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A transition is taken in steps: <see cref="Plan"/> finds it from the
    /// current state, <see cref="Guard"/> asks the handler the application
    /// set on <see cref="FiniteStateMachineState.OnBeforeTransition"/>,
    /// <see cref="Commit"/> writes the state variables and
    /// <see cref="NotifyCommitted"/> tells
    /// <see cref="FiniteStateMachineState.OnAfterTransition"/>.
    /// </para>
    /// <para>
    /// The caller serializes <see cref="Plan"/> and <see cref="Commit"/> and
    /// calls the two handlers without holding its lock, because they run
    /// code of the application.
    /// </para>
    /// </remarks>
    internal sealed class MaintenanceEventStateMachineDriver
    {
        public MaintenanceEventStateMachineDriver(
            FiniteStateMachineState stateMachine,
            ISystemContext context,
            ushort ambNamespaceIndex)
        {
            m_stateMachine = stateMachine;
            m_context = context;
            m_ambNamespaceIndex = ambNamespaceIndex;
        }

        /// <summary>
        /// Gets the current state.
        /// </summary>
        public MaintenanceStateKind State => (MaintenanceStateKind)m_current.Number;

        /// <summary>
        /// Gets the numeric id of the node of the current state.
        /// </summary>
        public uint CurrentStateId => m_current.Id;

        /// <summary>
        /// Puts the machine into a state without a transition.
        /// </summary>
        public void SetInitialState(MaintenanceStateKind kind)
        {
            m_current = MaintenanceEventStateMachineTables.StateOf(kind);
            WriteState(m_current);
            m_stateMachine.ClearChangeMasks(m_context, true);
        }

        /// <summary>
        /// Finds the transition from the current state into a state.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the model has no
        /// transition from the current state into <paramref name="target"/>.
        /// </exception>
        public MaintenanceEventStateMachineTables.TransitionDefinition Plan(MaintenanceStateKind target)
        {
            MaintenanceEventStateMachineTables.StateDefinition to = MaintenanceEventStateMachineTables.StateOf(target);
            return FindTransition(m_current.Id, to.Id);
        }

        /// <summary>
        /// Asks the guard of the application whether a transition may be
        /// taken.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the guard refused it.
        /// </exception>
        public void Guard(MaintenanceEventStateMachineTables.TransitionDefinition transition)
        {
            ServiceResult guard = Invoke(m_stateMachine.OnBeforeTransition, transition.Id);
            if (ServiceResult.IsBad(guard))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The transition {0} was refused: {1}",
                    transition.Name,
                    guard.StatusCode);
            }
        }

        /// <summary>
        /// Takes a planned transition: writes the current state and the last
        /// transition.
        /// </summary>
        public void Commit(MaintenanceEventStateMachineTables.TransitionDefinition transition)
        {
            MaintenanceEventStateMachineTables.StateDefinition to =
                MaintenanceEventStateMachineTables.StateById(transition.ToStateId);
            m_current = to;
            WriteState(to);
            WriteTransition(transition);
            m_stateMachine.ClearChangeMasks(m_context, true);
        }

        /// <summary>
        /// Tells the application about a committed transition.
        /// </summary>
        public void NotifyCommitted(MaintenanceEventStateMachineTables.TransitionDefinition transition)
        {
            Invoke(m_stateMachine.OnAfterTransition, transition.Id);
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
                    m_stateMachine,
                    transitionId,
                    causeId: 0,
                    inputArguments: default,
                    outputArguments: null) ??
                    ServiceResult.Good;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return ServiceResult.Create(
                    ex,
                    StatusCodes.BadInternalError,
                    "A maintenance state machine transition handler failed.");
            }
        }

        private void WriteState(MaintenanceEventStateMachineTables.StateDefinition state)
        {
            FiniteStateVariableState? currentState = m_stateMachine.CurrentState;
            if (currentState == null)
            {
                return;
            }
            currentState.Value = new LocalizedText(state.Name);
            currentState.Id?.Value = new NodeId(state.Id, m_ambNamespaceIndex);
            currentState.Number?.Value = state.Number;
            currentState.Timestamp = DateTimeUtc.Now;
        }

        private void WriteTransition(MaintenanceEventStateMachineTables.TransitionDefinition transition)
        {
            FiniteTransitionVariableState? lastTransition = m_stateMachine.LastTransition;
            if (lastTransition == null)
            {
                return;
            }
            lastTransition.Value = new LocalizedText(transition.Name);
            lastTransition.Id?.Value = new NodeId(transition.Id, m_ambNamespaceIndex);
            lastTransition.Number?.Value = transition.Number;
            lastTransition.TransitionTime?.Value = DateTimeUtc.Now;
        }

        private MaintenanceEventStateMachineTables.TransitionDefinition FindTransition(uint fromStateId, uint toStateId)
        {
            foreach (MaintenanceEventStateMachineTables.TransitionDefinition candidate in
                MaintenanceEventStateMachineTables.Transitions)
            {
                if (candidate.FromStateId == fromStateId && candidate.ToStateId == toStateId)
                {
                    return candidate;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "The maintenance activity '{0}' cannot move from {1} to the state {2}.",
                m_stateMachine.Parent?.BrowseName ?? m_stateMachine.BrowseName,
                m_current.Name,
                toStateId);
        }

        private readonly FiniteStateMachineState m_stateMachine;
        private readonly ISystemContext m_context;
        private readonly ushort m_ambNamespaceIndex;
        private MaintenanceEventStateMachineTables.StateDefinition m_current;
    }
}
