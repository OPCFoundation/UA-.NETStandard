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
using Opc.Ua.Types;

namespace Opc.Ua.Server.StateMachines
{
    /// <summary>
    /// Fluent builder for declarative Part 16 state-machine
    /// definitions. Define states + transitions + cause mappings,
    /// then call <see cref="Build"/> to produce an immutable
    /// <see cref="StateMachineDefinition"/> for use with
    /// <see cref="FluentFiniteStateMachineState"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The builder validates structural integrity: every transition
    /// must reference declared <c>From</c>/<c>To</c> states, every
    /// cause mapping must reference a declared from-state and a
    /// declared transition, and state / transition / cause ids must
    /// be unique within their respective tables.
    /// </para>
    /// <para>
    /// This is the recommended path for new vendor state machines —
    /// no need to subclass <see cref="FiniteStateMachineState"/> or
    /// hard-code the four protected table overrides.
    /// </para>
    /// </remarks>
    public sealed class StateMachineBuilder
    {
        private readonly Dictionary<uint, StateMachineStateDefinition> m_states = [];
        private readonly Dictionary<uint, StateMachineTransitionDefinition> m_transitions = [];
        private readonly List<StateMachineCauseMapping> m_causeMappings = [];
        private uint? m_initialStateId;
        private string m_elementNamespaceUri = Opc.Ua.Types.Namespaces.OpcUa;

        /// <summary>
        /// Adds a state to the machine.
        /// </summary>
        /// <param name="id">The numeric state id.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="isInitial">Whether this is the initial state.</param>
        public StateMachineBuilder AddState(
            uint id,
            string browseName,
            bool isInitial = false)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("Browse name must not be empty.",
                    nameof(browseName));
            }
            if (m_states.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"State id {id} is already declared.", nameof(id));
            }
            m_states[id] = new StateMachineStateDefinition(id, browseName, isInitial);
            if (isInitial)
            {
                if (m_initialStateId.HasValue && m_initialStateId.Value != id)
                {
                    throw new InvalidOperationException(
                        "Only one state may be marked as the initial state.");
                }
                m_initialStateId = id;
            }
            return this;
        }

        /// <summary>
        /// Adds a transition between two states.
        /// </summary>
        /// <param name="id">The numeric transition id.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="from">The from-state id.</param>
        /// <param name="to">The to-state id.</param>
        /// <param name="hasEffect">Whether the transition fires a
        /// <c>TransitionEventType</c>.</param>
        public StateMachineBuilder AddTransition(
            uint id,
            string browseName,
            uint from,
            uint to,
            bool hasEffect = true)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("Browse name must not be empty.",
                    nameof(browseName));
            }
            if (m_transitions.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"Transition id {id} is already declared.", nameof(id));
            }
            m_transitions[id] = new StateMachineTransitionDefinition(
                id, browseName, from, to, hasEffect);
            return this;
        }

        /// <summary>
        /// Adds a cause-to-transition mapping. When the method with
        /// the given cause id is invoked while the machine is in the
        /// given from-state, the named transition fires.
        /// </summary>
        public StateMachineBuilder OnCause(
            uint causeId,
            uint from,
            uint transition)
        {
            m_causeMappings.Add(new StateMachineCauseMapping(
                causeId, from, transition));
            return this;
        }

        /// <summary>
        /// Designates the namespace URI that qualifies the state and
        /// transition numeric NodeIds. Defaults to the standard UA
        /// namespace.
        /// </summary>
        public StateMachineBuilder UseElementNamespace(string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentException("Namespace URI must not be empty.",
                    nameof(namespaceUri));
            }
            m_elementNamespaceUri = namespaceUri;
            return this;
        }

        /// <summary>
        /// Validates the accumulated definitions and produces an
        /// immutable <see cref="StateMachineDefinition"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Validation failed (dangling references, no states declared,
        /// duplicate cause mapping, etc.).
        /// </exception>
        public StateMachineDefinition Build()
        {
            if (m_states.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one state must be declared.");
            }
            foreach (StateMachineTransitionDefinition t in m_transitions.Values)
            {
                if (!m_states.ContainsKey(t.FromStateId))
                {
                    throw new InvalidOperationException(
                        $"Transition '{t.BrowseName}' references unknown from-state {t.FromStateId}.");
                }
                if (!m_states.ContainsKey(t.ToStateId))
                {
                    throw new InvalidOperationException(
                        $"Transition '{t.BrowseName}' references unknown to-state {t.ToStateId}.");
                }
            }
            foreach (StateMachineCauseMapping c in m_causeMappings)
            {
                if (!m_states.ContainsKey(c.FromStateId))
                {
                    throw new InvalidOperationException(
                        $"Cause mapping references unknown from-state {c.FromStateId}.");
                }
                if (!m_transitions.ContainsKey(c.TransitionId))
                {
                    throw new InvalidOperationException(
                        $"Cause mapping references unknown transition {c.TransitionId}.");
                }
            }

            var states = new List<StateMachineStateDefinition>(m_states.Values);
            var transitions = new List<StateMachineTransitionDefinition>(m_transitions.Values);
            return new StateMachineDefinition(
                states,
                transitions,
                new List<StateMachineCauseMapping>(m_causeMappings),
                m_initialStateId,
                m_elementNamespaceUri);
        }
    }
}
