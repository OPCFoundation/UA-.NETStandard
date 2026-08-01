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

namespace Opc.Ua.Robotics.Server.Builders
{
    internal abstract class RoboticsNodeBuilder
    {
        protected RoboticsNodeBuilder(RoboticsBuildScope scope, NodeState state)
        {
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            UntypedState = state ?? throw new ArgumentNullException(nameof(state));
            scope.RegisterBuilder(this);
        }

        internal RoboticsBuildScope Scope { get; }

        internal NodeState UntypedState { get; }

        internal abstract void CacheNodeBuilder();
    }

    internal abstract class RoboticsNodeBuilder<TState> :
        RoboticsNodeBuilder,
        IRoboticsNodeBuilder<TState>
        where TState : NodeState
    {
        private INodeBuilder<TState>? m_nodeBuilder;

        protected RoboticsNodeBuilder(RoboticsBuildScope scope, TState state)
            : base(scope, state)
        {
            State = state;
        }

        public TState State { get; }

        public IRoboticsBuildContext BuildContext => Scope.BuildContext;

        public IRoboticsNodeBuilder<TState> Configure(
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
                    "Robotics node '{0}' is unavailable through the fluent node surface " +
                    "until its motion-device system has been registered.",
                    State.BrowseName);
            }
            return m_nodeBuilder ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Robotics node '{0}' was registered without caching its fluent node builder.",
                    State.BrowseName);
        }

        internal override void CacheNodeBuilder()
        {
            m_nodeBuilder ??= BuildContext.Nodes.Node<TState>(State.NodeId);
        }

        protected RoboticsNodeBuilder<TOther> RequireSameScope<TOther>(
            IRoboticsNodeBuilder<TOther> other,
            string parameterName)
            where TOther : NodeState
        {
            if (other == null)
            {
                throw new ArgumentNullException(parameterName);
            }
            if (other is not RoboticsNodeBuilder<TOther> builder ||
                !ReferenceEquals(builder.Scope, Scope))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Relationship endpoint '{0}' must belong to the same " +
                    "MotionDeviceSystem build scope as '{1}'.",
                    other.State.BrowseName,
                    State.BrowseName);
            }
            return builder;
        }

        protected TBuilder RequireSameScope<TBuilder, TOther>(
            IRoboticsNodeBuilder<TOther> other,
            string parameterName)
            where TBuilder : RoboticsNodeBuilder<TOther>
            where TOther : NodeState
        {
            RoboticsNodeBuilder<TOther> builder = RequireSameScope(other, parameterName);
            if (builder is not TBuilder typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Relationship endpoint '{0}' is not a {1}.",
                    other.State.BrowseName,
                    typeof(TBuilder).Name);
            }
            return typed;
        }

        protected TBuilder IsConnectedTo<TBuilder, TOther>(
            IRoboticsNodeBuilder<TOther> other)
            where TBuilder : RoboticsNodeBuilder<TState>
            where TOther : NodeState
        {
            RoboticsNodeBuilder<TOther> target = RequireSameScope(other, nameof(other));
            Scope.AddSemanticReference(
                RoboticsSemanticReference.IsConnectedTo,
                this,
                target);
            return (TBuilder)this;
        }
    }
}
