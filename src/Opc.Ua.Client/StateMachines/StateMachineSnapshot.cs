// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.StateMachines
{
    /// <summary>
    /// A point-in-time snapshot of an OPC UA Part 16 state machine.
    /// Returned by <c>StateMachineTypeClient.GetCurrentStateAsync</c>
    /// and yielded by <c>StateMachineTypeClient.ObserveStateChangesAsync</c>.
    /// </summary>
    /// <remarks>
    /// The non-finite variant only carries the
    /// <c>StateVariableType</c> value
    /// (<see cref="CurrentState"/> as <see cref="LocalizedText"/>).
    /// Finite state machines yield the richer
    /// <see cref="FiniteStateSnapshot"/> with the typed state and
    /// transition NodeIds.
    /// </remarks>
    /// <param name="StateMachineId">The NodeId of the state machine instance.</param>
    /// <param name="CurrentState">The current state's localized name.</param>
    /// <param name="Timestamp">The source timestamp of the observation.</param>
    /// <param name="Status">The status code reported with the observation.</param>
    public sealed record StateMachineSnapshot(
        NodeId StateMachineId,
        LocalizedText CurrentState,
        DateTime Timestamp,
        StatusCode Status);

    /// <summary>
    /// A point-in-time snapshot of an OPC UA Part 16
    /// <c>FiniteStateMachineType</c> instance. Adds the typed state
    /// and transition NodeIds on top of <see cref="StateMachineSnapshot"/>.
    /// </summary>
    /// <param name="StateMachineId">The NodeId of the state machine instance.</param>
    /// <param name="CurrentState">The current state's localized name.</param>
    /// <param name="CurrentStateId">The current state's NodeId (the
    /// <c>StateType</c> instance the machine is currently in).</param>
    /// <param name="LastTransition">The last transition's localized
    /// name. May be <c>LocalizedText.Null</c> if no transition has
    /// occurred since the state machine was created.</param>
    /// <param name="LastTransitionId">The last transition's NodeId.
    /// May be <c>NodeId.Null</c> if no transition has occurred.</param>
    /// <param name="Timestamp">The source timestamp of the observation.</param>
    /// <param name="Status">The status code reported with the observation.</param>
    public sealed record FiniteStateSnapshot(
        NodeId StateMachineId,
        LocalizedText CurrentState,
        NodeId CurrentStateId,
        LocalizedText LastTransition,
        NodeId LastTransitionId,
        DateTime Timestamp,
        StatusCode Status)
    {
        /// <summary>
        /// When the FSM has a sub-state-machine attached to its
        /// current state via <c>HasSubStateMachine</c>, this holds
        /// the sub-SM's snapshot. <c>null</c> for state machines
        /// without sub-SMs or when the current state has no
        /// attached sub-SM.
        /// </summary>
        public FiniteStateSnapshot? SubMachine { get; init; }
    }

    /// <summary>
    /// Describes a state node defined on a finite state machine type.
    /// Returned by
    /// <c>FiniteStateMachineTypeClient.GetAvailableStatesAsync</c>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the <c>StateType</c> instance.</param>
    /// <param name="BrowseName">The browse name (e.g. <c>Unshelved</c>).</param>
    /// <param name="StateNumber">The state number declared by the
    /// type (Part 16 §4.4.8). Zero when the server does not expose
    /// the <c>StateNumber</c> property.</param>
    public sealed record FiniteStateInfo(
        NodeId NodeId,
        QualifiedName BrowseName,
        uint StateNumber);

    /// <summary>
    /// Describes a transition node defined on a finite state machine
    /// type. Returned by
    /// <c>FiniteStateMachineTypeClient.GetAvailableTransitionsAsync</c>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the <c>TransitionType</c> instance.</param>
    /// <param name="BrowseName">The browse name (e.g.
    /// <c>UnshelvedToTimedShelved</c>).</param>
    /// <param name="TransitionNumber">The transition number declared
    /// by the type. Zero when the server does not expose the
    /// <c>TransitionNumber</c> property.</param>
    public sealed record FiniteTransitionInfo(
        NodeId NodeId,
        QualifiedName BrowseName,
        uint TransitionNumber);
}
