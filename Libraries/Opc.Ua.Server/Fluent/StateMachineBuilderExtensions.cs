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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Extension methods that turn an <see cref="INodeBuilder{TState}"/>
    /// into a <see cref="IStateMachineBuilder{TState}"/> for any
    /// <see cref="FiniteStateMachineState"/> subclass.
    /// </summary>
    public static class StateMachineBuilderExtensions
    {
        /// <summary>
        /// Returns a fluent builder for the supplied state machine. The
        /// returned builder installs composed
        /// <c>OnBeforeTransition</c> / <c>OnAfterTransition</c>
        /// coordinators that preserve any pre-existing handler and
        /// fan out to user-registered enter/exit/transition callbacks.
        /// </summary>
        /// <typeparam name="TState">
        /// Concrete <see cref="FiniteStateMachineState"/> subclass.
        /// </typeparam>
        /// <param name="nodeBuilder">
        /// Strongly-typed node builder resolved via
        /// <see cref="INodeBuilder.As{TState}"/> or
        /// <see cref="INodeManagerBuilder.Node{TState}(string)"/>.
        /// </param>
        public static IStateMachineBuilder<TState> AsStateMachine<TState>(
            this INodeBuilder<TState> nodeBuilder)
            where TState : FiniteStateMachineState
        {
            if (nodeBuilder == null) { throw new ArgumentNullException(nameof(nodeBuilder)); }
            return new StateMachineBuilder<TState>(nodeBuilder);
        }

        /// <summary>
        /// Returns control to the owning node builder so subsequent
        /// fluent calls operate on the state machine's parent again.
        /// </summary>
        public static INodeBuilder Done<TState>(this IStateMachineBuilder<TState> builder)
            where TState : FiniteStateMachineState
        {
            if (builder == null) { throw new ArgumentNullException(nameof(builder)); }
            return builder.Builder;
        }

        /// <summary>
        /// Materialises a new <see cref="ProgramStateMachineState"/>
        /// child under <paramref name="parent"/> and returns a fluent
        /// builder over it. The machine is created with stack-default
        /// state/transition/cause tables; its initial state is
        /// <c>ProgramStateMachineType_Ready</c>.
        /// </summary>
        public static IStateMachineBuilder<ProgramStateMachineState>
            CreateProgramStateMachine(
                this INodeBuilder parent,
                QualifiedName browseName)
        {
            NodeBuilder<ProgramStateMachineState> nb =
                AttachStateMachine<ProgramStateMachineState>(
                    parent, browseName, p => new ProgramStateMachineState(p));
            return new StateMachineBuilder<ProgramStateMachineState>(nb);
        }

        /// <summary>
        /// Materialises a new <see cref="ShelvedStateMachineState"/>
        /// child under <paramref name="parent"/> and returns a fluent
        /// builder over it.
        /// </summary>
        public static IStateMachineBuilder<ShelvedStateMachineState>
            CreateShelvedStateMachine(
                this INodeBuilder parent,
                QualifiedName browseName)
        {
            NodeBuilder<ShelvedStateMachineState> nb =
                AttachStateMachine<ShelvedStateMachineState>(
                    parent, browseName, p => new ShelvedStateMachineState(p));
            return new StateMachineBuilder<ShelvedStateMachineState>(nb);
        }

        /// <summary>
        /// Materialises a new <see cref="ExclusiveLimitStateMachineState"/>
        /// child under <paramref name="parent"/> and returns a fluent
        /// builder over it.
        /// </summary>
        public static IStateMachineBuilder<ExclusiveLimitStateMachineState>
            CreateExclusiveLimitStateMachine(
                this INodeBuilder parent,
                QualifiedName browseName)
        {
            NodeBuilder<ExclusiveLimitStateMachineState> nb =
                AttachStateMachine<ExclusiveLimitStateMachineState>(
                    parent, browseName, p => new ExclusiveLimitStateMachineState(p));
            return new StateMachineBuilder<ExclusiveLimitStateMachineState>(nb);
        }

        private static NodeBuilder<TState> AttachStateMachine<TState>(
            INodeBuilder parent,
            QualifiedName browseName,
            Func<NodeState, TState> factory)
            where TState : FiniteStateMachineState
        {
            if (parent == null) { throw new ArgumentNullException(nameof(parent)); }
            if (browseName.IsNull) { throw new ArgumentNullException(nameof(browseName)); }
            if (factory == null) { throw new ArgumentNullException(nameof(factory)); }

            string symbolicName = browseName.Name ?? string.Empty;
            TState machine = factory(parent.Node);
            machine.SymbolicName = symbolicName;
            machine.BrowseName = browseName;
            machine.DisplayName = new LocalizedText(symbolicName);

            string parentIdentifier = parent.Node.NodeId.IdentifierAsString;
            machine.NodeId = new NodeId(
                string.Concat(parentIdentifier, "_", symbolicName),
                parent.Node.NodeId.NamespaceIndex);

            machine.Create(
                parent.Builder.Context,
                machine.NodeId,
                browseName,
                displayName: new LocalizedText(symbolicName),
                assignNodeIds: false);

            parent.Node.AddChild(machine);

            if (parent.Builder is not NodeManagerBuilder root)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Create*StateMachine helpers require a NodeManagerBuilder host.");
            }
            return new NodeBuilder<TState>(root, machine);
        }
    }
}
