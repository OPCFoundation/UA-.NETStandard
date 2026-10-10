/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Untyped base of the Machinery node builders. Each one registers itself
    /// with its <see cref="MachineryBuildScope"/>, which caches the builder's
    /// fluent node builder once the containing machine is registered.
    /// </summary>
    internal abstract class MachineryNodeBuilder
    {
        protected MachineryNodeBuilder(MachineryBuildScope scope, NodeState state)
        {
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            UntypedState = state ?? throw new ArgumentNullException(nameof(state));
            scope.RegisterBuilder(this);
        }

        internal MachineryBuildScope Scope { get; }

        internal NodeState UntypedState { get; }

        internal abstract void CacheNodeBuilder();
    }

    /// <summary>
    /// Implements <see cref="IMachineryNodeBuilder{TState}"/> for a staged
    /// Machinery node: applies low-level state configuration while the build
    /// is still mutable, and resolves the registered node through the fluent
    /// node builder cached once the containing machine is registered.
    /// </summary>
    /// <typeparam name="TState">The generated Machinery state type.</typeparam>
    internal abstract class MachineryNodeBuilder<TState> :
        MachineryNodeBuilder,
        IMachineryNodeBuilder<TState>
        where TState : NodeState
    {
        private INodeBuilder<TState>? m_nodeBuilder;

        protected MachineryNodeBuilder(MachineryBuildScope scope, TState state)
            : base(scope, state)
        {
            State = state;
        }

        public TState State { get; }

        public IMachineryBuildContext BuildContext => Scope.BuildContext;

        public IMachineryNodeBuilder<TState> Configure(
            Action<TState, ISystemContext> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scope.EnsureMutable();
            configure(State, Scope.Context);
            return this;
        }

        public INodeBuilder<TState> AsNode()
        {
            if (!Scope.IsRegistered)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Machinery node '{0}' is unavailable through the fluent node surface " +
                    "until its machine has been registered.",
                    State.BrowseName);
            }
            return m_nodeBuilder ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Machinery node '{0}' was registered without caching its fluent " +
                    "node builder.",
                    State.BrowseName);
        }

        internal override void CacheNodeBuilder()
        {
            m_nodeBuilder ??= BuildContext.Nodes.Node<TState>(State.NodeId);
        }
    }
}
