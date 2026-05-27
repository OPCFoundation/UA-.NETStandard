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

namespace Opc.Ua.Server.StateMachines
{
    /// <summary>
    /// A generic concrete <see cref="FiniteStateMachineState"/> whose
    /// state, transition and cause tables are sourced from a mutable
    /// definition holder managed by the unified
    /// <see cref="StateMachineBuilder{TState}"/>. Construct via
    /// <see cref="StateMachineBuilder.Create"/>:
    /// </summary>
    /// <remarks>
    /// <code language="csharp">
    /// FluentFiniteStateMachineState sm = StateMachineBuilder
    ///     .Create(parent, context, nodeId, browseName)
    ///     .AddState(1, "Off", isInitial: true)
    ///     .AddState(2, "On")
    ///     .AddTransition(10, "OffToOn", from: 1, to: 2)
    ///     .OnCause(causeId: 100, from: 1, transition: 10)
    ///     .WithInitialState(1)
    ///     .StateMachine;
    /// </code>
    /// <para>
    /// Vendors who need additional properties or methods on top of the
    /// standard finite state-machine surface can subclass
    /// <see cref="FluentFiniteStateMachineState"/> and use the
    /// (protected) <see cref="MutableDefinition"/> property to feed
    /// the same builder-driven tables.
    /// </para>
    /// </remarks>
    public class FluentFiniteStateMachineState : FiniteStateMachineState
    {
        /// <summary>
        /// Cached projections of the mutable holder. Invalidated by
        /// version counter.
        /// </summary>
        private ElementInfo[]? m_stateTable;
        private ElementInfo[]? m_transitionTable;
        private uint[,]? m_transitionMappings;
        private uint[,]? m_causeMappings;
        private int m_cacheVersion = -1;

        /// <summary>
        /// Initializes a new state machine instance from the given
        /// immutable definition. This overload exists for callers that
        /// construct definitions directly; the recommended path is the
        /// unified <see cref="StateMachineBuilder"/>.
        /// </summary>
        /// <param name="parent">The parent node (may be <c>null</c>).</param>
        /// <param name="definition">The fluent definition snapshot.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="definition"/> is <c>null</c>.
        /// </exception>
        public FluentFiniteStateMachineState(
            NodeState parent,
            StateMachineDefinition definition)
            : this(parent, FromSnapshot(definition), useHolder: true)
        {
        }

        /// <summary>
        /// Internal factory used by the unified builder so it can mutate
        /// the definition holder incrementally.
        /// </summary>
        internal static FluentFiniteStateMachineState CreateWithHolder(
            NodeState parent,
            MutableStateMachineDefinition holder)
        {
            return new FluentFiniteStateMachineState(parent, holder, useHolder: true);
        }

        private FluentFiniteStateMachineState(
            NodeState parent,
            MutableStateMachineDefinition holder,
            bool useHolder)
            : base(parent)
        {
            _ = useHolder;
            MutableDefinition = holder ?? throw new ArgumentNullException(nameof(holder));
        }

        /// <summary>
        /// The mutable definition holder backing this state machine.
        /// Visible to assembly-internal callers (the unified
        /// <see cref="StateMachineBuilder{TState}"/>) so they can
        /// populate the definition incrementally.
        /// </summary>
        internal MutableStateMachineDefinition MutableDefinition { get; }

        /// <summary>
        /// The immutable definition snapshot this state machine reads
        /// from. Allocates a fresh snapshot on each access — use
        /// sparingly (for introspection / testing).
        /// </summary>
        public StateMachineDefinition Definition => MutableDefinition.Snapshot();

        /// <inheritdoc/>
        protected override string ElementNamespaceUri
            => MutableDefinition.ElementNamespaceUri;

        /// <inheritdoc/>
        protected override ElementInfo[]? StateTable
        {
            get
            {
                RefreshCache();
                return m_stateTable;
            }
        }

        /// <inheritdoc/>
        protected override ElementInfo[]? TransitionTable
        {
            get
            {
                RefreshCache();
                return m_transitionTable;
            }
        }

        /// <inheritdoc/>
        protected override uint[,]? TransitionMappings
        {
            get
            {
                RefreshCache();
                return m_transitionMappings;
            }
        }

        /// <inheritdoc/>
        protected override uint[,]? CauseMappings
        {
            get
            {
                RefreshCache();
                return m_causeMappings;
            }
        }

        private void RefreshCache()
        {
            int currentVersion = MutableDefinition.Version;
            if (m_cacheVersion == currentVersion)
            {
                return;
            }

            int stateCount = MutableDefinition.States.Count;
            var stateTable = new ElementInfo[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                StateMachineStateDefinition s = MutableDefinition.States[i];
                stateTable[i] = new ElementInfo(s.Id, s.BrowseName, s.Id);
            }

            int transitionCount = MutableDefinition.Transitions.Count;
            var transitionTable = new ElementInfo[transitionCount];
            uint[,] transitionMappings = new uint[transitionCount, 4];
            for (int i = 0; i < transitionCount; i++)
            {
                StateMachineTransitionDefinition t = MutableDefinition.Transitions[i];
                transitionTable[i] = new ElementInfo(t.Id, t.BrowseName, t.Id);
                transitionMappings[i, 0] = t.Id;
                transitionMappings[i, 1] = t.FromStateId;
                transitionMappings[i, 2] = t.ToStateId;
                transitionMappings[i, 3] = t.HasEffect ? 1u : 0u;
            }

            int causeCount = MutableDefinition.CauseMappings.Count;
            uint[,] causeMappings = new uint[causeCount, 3];
            for (int i = 0; i < causeCount; i++)
            {
                StateMachineCauseMapping c = MutableDefinition.CauseMappings[i];
                causeMappings[i, 0] = c.CauseId;
                causeMappings[i, 1] = c.FromStateId;
                causeMappings[i, 2] = c.TransitionId;
            }

            m_stateTable = stateTable;
            m_transitionTable = transitionTable;
            m_transitionMappings = transitionMappings;
            m_causeMappings = causeMappings;
            m_cacheVersion = currentVersion;
        }

        private static MutableStateMachineDefinition FromSnapshot(
            StateMachineDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var holder = new MutableStateMachineDefinition
            {
                InitialStateId = definition.InitialStateId,
                ElementNamespaceUri = definition.ElementNamespaceUri
            };
            holder.States.AddRange(definition.States);
            holder.Transitions.AddRange(definition.Transitions);
            holder.CauseMappings.AddRange(definition.CauseMappings);
            holder.Version = 1;
            holder.Frozen = true;
            return holder;
        }
    }
}
