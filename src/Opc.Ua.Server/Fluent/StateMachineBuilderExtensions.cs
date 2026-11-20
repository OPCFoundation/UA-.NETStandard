// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        /// <exception cref="ArgumentNullException"><paramref name="nodeBuilder"/> is <c>null</c>.</exception>
        public static IStateMachineBuilder<TState> AsStateMachine<TState>(
            this INodeBuilder<TState> nodeBuilder)
            where TState : FiniteStateMachineState
        {
            if (nodeBuilder == null)
            {
                throw new ArgumentNullException(nameof(nodeBuilder));
            }
            return new StateMachineBuilder<TState>(nodeBuilder);
        }

        /// <summary>
        /// Returns control to the owning node builder so subsequent
        /// fluent calls operate on the state machine's parent again.
        /// </summary>
        /// <typeparam name="TState">Concrete finite state machine state type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static INodeBuilder Done<TState>(this IStateMachineBuilder<TState> builder)
            where TState : FiniteStateMachineState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
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
                AttachStateMachine(
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
                AttachStateMachine(
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
                AttachStateMachine(
                    parent, browseName, p => new ExclusiveLimitStateMachineState(p));
            return new StateMachineBuilder<ExclusiveLimitStateMachineState>(nb);
        }

        private static NodeBuilder<TState> AttachStateMachine<TState>(
            INodeBuilder parent,
            QualifiedName browseName,
            Func<NodeState, TState> factory)
            where TState : FiniteStateMachineState
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentNullException(nameof(browseName));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            string symbolicName = browseName.Name ?? string.Empty;
            TState machine = factory(parent.Node);
            machine.SymbolicName = symbolicName;
            machine.BrowseName = browseName;
            machine.DisplayName = new LocalizedText(symbolicName);

            FluentNodeRegistration.AssignNodeId(parent.Builder, machine);

            machine.Create(
                parent.Builder.Context,
                machine.NodeId,
                browseName,
                displayName: new LocalizedText(symbolicName),
                assignNodeIds: false);

            parent.Builder.Context.AssignInstanceChildNodeIds(machine);
            machine.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            parent.Node.AddChild(machine);
            FluentNodeRegistration.RegisterCreatedNode(parent.Builder, machine);

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
