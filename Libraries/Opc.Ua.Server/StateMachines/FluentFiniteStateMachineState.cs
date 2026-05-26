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
    /// A generic concrete <see cref="FiniteStateMachineState"/> whose
    /// state, transition and cause tables are sourced from a
    /// caller-supplied <see cref="StateMachineDefinition"/>. Use the
    /// <see cref="StateMachineBuilder"/> to compose a definition
    /// declaratively, then construct an instance:
    /// </summary>
    /// <remarks>
    /// <code language="csharp">
    /// var def = new StateMachineBuilder()
    ///     .AddState(1, "Off", isInitial: true)
    ///     .AddState(2, "On")
    ///     .AddTransition(10, "OffToOn", from: 1, to: 2)
    ///     .OnCause(100, from: 1, transition: 10)
    ///     .Build();
    /// var sm = new FluentFiniteStateMachineState(parent, def);
    /// sm.Create(systemContext, ...);
    /// </code>
    /// <para>
    /// Vendors who need additional properties or methods on top of
    /// the standard finite state-machine surface can subclass
    /// <see cref="FluentFiniteStateMachineState"/> and add their
    /// extensions on the derived class.
    /// </para>
    /// </remarks>
    public class FluentFiniteStateMachineState : FiniteStateMachineState
    {
        private readonly ElementInfo[] m_stateTable;
        private readonly ElementInfo[] m_transitionTable;
        private readonly uint[,] m_transitionMappings;
        private readonly uint[,] m_causeMappings;
        private readonly string m_elementNamespaceUri;

        /// <summary>
        /// Initializes a new state machine instance from the given
        /// definition.
        /// </summary>
        /// <param name="parent">The parent node (may be <c>null</c>).</param>
        /// <param name="definition">The fluent definition.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="definition"/> is <c>null</c>.
        /// </exception>
        public FluentFiniteStateMachineState(
            NodeState parent,
            StateMachineDefinition definition)
            : base(parent)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            Definition = definition;
            m_elementNamespaceUri = definition.ElementNamespaceUri;

            // Build StateTable.
            var stateInfos = new List<ElementInfo>(definition.States.Count);
            foreach (StateMachineStateDefinition s in definition.States)
            {
                stateInfos.Add(new ElementInfo(s.Id, s.BrowseName, s.Id));
            }
            m_stateTable = stateInfos.ToArray();

            // Build TransitionTable + TransitionMappings.
            var transitionInfos = new List<ElementInfo>(definition.Transitions.Count);
            m_transitionMappings = new uint[definition.Transitions.Count, 4];
            for (int i = 0; i < definition.Transitions.Count; i++)
            {
                StateMachineTransitionDefinition t = definition.Transitions[i];
                transitionInfos.Add(new ElementInfo(t.Id, t.BrowseName, t.Id));
                m_transitionMappings[i, 0] = t.Id;
                m_transitionMappings[i, 1] = t.FromStateId;
                m_transitionMappings[i, 2] = t.ToStateId;
                m_transitionMappings[i, 3] = t.HasEffect ? 1u : 0u;
            }
            m_transitionTable = transitionInfos.ToArray();

            // Build CauseMappings.
            m_causeMappings = new uint[definition.CauseMappings.Count, 3];
            for (int i = 0; i < definition.CauseMappings.Count; i++)
            {
                StateMachineCauseMapping c = definition.CauseMappings[i];
                m_causeMappings[i, 0] = c.CauseId;
                m_causeMappings[i, 1] = c.FromStateId;
                m_causeMappings[i, 2] = c.TransitionId;
            }
        }

        /// <summary>
        /// The definition this state machine was built from. Exposed
        /// for introspection; do not mutate.
        /// </summary>
        public StateMachineDefinition Definition { get; }

        /// <inheritdoc/>
        protected override string ElementNamespaceUri => m_elementNamespaceUri;

        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => m_stateTable;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => m_transitionTable;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => m_transitionMappings;

        /// <inheritdoc/>
        protected override uint[,] CauseMappings => m_causeMappings;
    }
}
