// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.StateMachines
{
    /// <summary>
    /// Bridges the fluent <see cref="INodeBuilder{TState}"/> surface
    /// with the unified <see cref="StateMachineBuilder{TState}"/>.
    /// </summary>
    public static class StateMachineBuilderExtensions
    {
        /// <summary>
        /// Returns a lifecycle-mode
        /// <see cref="StateMachineBuilder{TState}"/> bound to the
        /// already-resolved state-machine node. Use this from inside
        /// fluent node-manager build pipelines to attach
        /// <see cref="StateMachineBuilder{TState}.OnEnterState"/> /
        /// <see cref="StateMachineBuilder{TState}.WithCause"/> /
        /// <see cref="StateMachineBuilder{TState}.WithTimedTransition"/>
        /// behavior on top of a generator-emitted or stack-shipped
        /// state machine subclass.
        /// </summary>
        /// <typeparam name="TState">The concrete state-machine type
        /// the node was resolved to (e.g.
        /// <c>ShelvedStateMachineState</c>).</typeparam>
        /// <param name="nodeBuilder">The node builder produced by
        /// <see cref="INodeBuilder.As{TState}"/> or one of the
        /// typed-child helpers.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static StateMachineBuilder<TState> AsStateMachine<TState>(
            this INodeBuilder<TState> nodeBuilder)
            where TState : FiniteStateMachineState
        {
            if (nodeBuilder is null)
            {
                throw new ArgumentNullException(nameof(nodeBuilder));
            }
            return StateMachineBuilder.For(
                nodeBuilder.Node, nodeBuilder.Builder.Context);
        }
    }
}
