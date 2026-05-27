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
using System.Collections.Generic;

namespace Opc.Ua.Server.StateMachines
{
    /// <summary>
    /// Immutable definition of a Part 16 finite state machine.
    /// Produced by <see cref="StateMachineBuilder"/> and consumed by
    /// <see cref="FluentFiniteStateMachineState"/>.
    /// </summary>
    public sealed class StateMachineDefinition
    {
        internal StateMachineDefinition(
            IReadOnlyList<StateMachineStateDefinition> states,
            IReadOnlyList<StateMachineTransitionDefinition> transitions,
            IReadOnlyList<StateMachineCauseMapping> causeMappings,
            uint? initialStateId,
            string elementNamespaceUri)
        {
            States = states;
            Transitions = transitions;
            CauseMappings = causeMappings;
            InitialStateId = initialStateId;
            ElementNamespaceUri = elementNamespaceUri;
        }

        /// <summary>
        /// The set of states declared on the state machine.
        /// </summary>
        public IReadOnlyList<StateMachineStateDefinition> States { get; }

        /// <summary>
        /// The set of transitions declared on the state machine.
        /// </summary>
        public IReadOnlyList<StateMachineTransitionDefinition> Transitions { get; }

        /// <summary>
        /// The list of (cause method id, from state, transition) tuples
        /// used by the runtime to resolve a transition for an inbound
        /// method call.
        /// </summary>
        public IReadOnlyList<StateMachineCauseMapping> CauseMappings { get; }

        /// <summary>
        /// The initial state's numeric id, or <c>null</c> when none was
        /// designated. Concrete state machines apply this on activation.
        /// </summary>
        public uint? InitialStateId { get; }

        /// <summary>
        /// The namespace URI that qualifies the state and transition
        /// browse names + node ids in the OPC UA address space.
        /// Defaults to <c>http://opcfoundation.org/UA/</c>.
        /// </summary>
        public string ElementNamespaceUri { get; }
    }

    /// <summary>
    /// Definition of a single state in a Part 16 state machine.
    /// </summary>
    /// <param name="Id">The state's numeric id (used as the
    /// <c>StateNumber</c> property and the <c>NodeId</c> identifier
    /// when applicable).</param>
    /// <param name="BrowseName">The state's localized browse name.</param>
    /// <param name="IsInitial">Whether this state is the starting
    /// state for the machine.</param>
    public sealed record StateMachineStateDefinition(
        uint Id,
        string BrowseName,
        bool IsInitial = false);

    /// <summary>
    /// Definition of a transition between two states.
    /// </summary>
    /// <param name="Id">The transition's numeric id.</param>
    /// <param name="BrowseName">The transition's browse name.</param>
    /// <param name="FromStateId">The source state id.</param>
    /// <param name="ToStateId">The destination state id.</param>
    /// <param name="HasEffect">Whether the transition fires a
    /// <c>TransitionEventType</c>. Defaults to <c>true</c>.</param>
    public sealed record StateMachineTransitionDefinition(
        uint Id,
        string BrowseName,
        uint FromStateId,
        uint ToStateId,
        bool HasEffect = true);

    /// <summary>
    /// Maps a cause (method NodeId) plus current state to the
    /// transition that should fire. Used by
    /// <see cref="FiniteStateMachineState.GetTransitionForCause"/> at
    /// runtime.
    /// </summary>
    /// <param name="CauseId">The cause (method) numeric id.</param>
    /// <param name="FromStateId">The current state id.</param>
    /// <param name="TransitionId">The transition that fires.</param>
    public sealed record StateMachineCauseMapping(
        uint CauseId,
        uint FromStateId,
        uint TransitionId);
}
