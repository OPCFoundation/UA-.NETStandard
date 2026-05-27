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
        public static StateMachineBuilder<TState> AsStateMachine<TState>(
            this INodeBuilder<TState> nodeBuilder)
            where TState : FiniteStateMachineState
        {
            ArgumentNullException.ThrowIfNull(nodeBuilder);
            return StateMachineBuilder.For(
                nodeBuilder.Node, nodeBuilder.Builder.Context);
        }
    }
}
