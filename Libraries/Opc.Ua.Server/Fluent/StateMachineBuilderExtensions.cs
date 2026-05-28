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
    }
}
