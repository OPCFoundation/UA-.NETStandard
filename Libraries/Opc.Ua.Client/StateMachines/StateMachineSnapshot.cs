/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        StatusCode Status);

    /// <summary>
    /// Describes a state node defined on a finite state machine type.
    /// Returned by
    /// <c>FiniteStateMachineTypeClient.GetAvailableStatesAsync</c>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the <c>StateType</c> instance.</param>
    /// <param name="BrowseName">The browse name (e.g. <c>Unshelved</c>).</param>
    /// <param name="StateNumber">The state number declared by the
    /// type (Part 16 §B.4.3). Zero when the server does not expose
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
