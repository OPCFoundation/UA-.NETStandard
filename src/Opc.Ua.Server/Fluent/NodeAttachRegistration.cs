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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Invokes a user attach callback with the node it applies to.
    /// </summary>
    /// <remarks>
    /// The registration keeps the user delegate boxed in <c>state</c> so that the
    /// trampoline itself can stay a static lambda, which keeps the registration
    /// allocation-free of a closure per call site.
    /// </remarks>
    internal delegate ValueTask<IAsyncDisposable?> NodeAttachInvoker(
        NodeState node,
        INodeAttachContext context,
        CancellationToken cancellationToken,
        object state);

    /// <summary>
    /// How a behavior registration selects the nodes it applies to.
    /// </summary>
    internal enum NodeAttachKind
    {
        /// <summary>Every instance of a type definition, and optionally its subtypes.</summary>
        Type,

        /// <summary>One node the caller already resolved.</summary>
        Node,

        /// <summary>The node manager itself; the behavior owns no node.</summary>
        Manager
    }

    /// <summary>
    /// One pending behavior registration recorded by the fluent builder.
    /// </summary>
    /// <remarks>
    /// Registrations are inert until the owning node manager drains them, which happens
    /// once the address space is fully indexed. Draining turns each one into a behavior
    /// factory the activation engine owns.
    /// </remarks>
    internal sealed class NodeAttachRegistration
    {
        private NodeAttachRegistration(
            NodeAttachKind kind,
            NodeId typeDefinitionId,
            NodeState? node,
            NodeAttachInvoker invoker,
            object state,
            NodeAttachOptions options)
        {
            Kind = kind;
            TypeDefinitionId = typeDefinitionId;
            Node = node;
            Invoker = invoker;
            State = state;
            Options = options;
        }

        public NodeAttachKind Kind { get; }

        public NodeId TypeDefinitionId { get; }

        public NodeState? Node { get; }

        public NodeAttachInvoker Invoker { get; }

        public object State { get; }

        public NodeAttachOptions Options { get; }

        public static NodeAttachRegistration ForType(
            NodeId typeDefinitionId,
            NodeAttachInvoker invoker,
            object state,
            NodeAttachOptions options)
        {
            return new NodeAttachRegistration(
                NodeAttachKind.Type,
                typeDefinitionId,
                node: null,
                invoker,
                state,
                options);
        }

        public static NodeAttachRegistration ForNode(
            NodeState node,
            NodeAttachInvoker invoker,
            object state)
        {
            return new NodeAttachRegistration(
                NodeAttachKind.Node,
                NodeId.Null,
                node ?? throw new ArgumentNullException(nameof(node)),
                invoker,
                state,
                new NodeAttachOptions { AllowZeroMatches = true });
        }

        public static NodeAttachRegistration ForManager(
            NodeAttachInvoker invoker,
            object state)
        {
            return new NodeAttachRegistration(
                NodeAttachKind.Manager,
                NodeId.Null,
                node: null,
                invoker,
                state,
                new NodeAttachOptions { AllowZeroMatches = true });
        }
    }

    /// <summary>
    /// Adapts one fluent registration to the activation engine's factory contract.
    /// </summary>
    internal sealed class NodeAttachFactory : INodeBehaviorFactory
    {
        public NodeAttachFactory(
            NodeAttachRegistration registration,
            ExpandedNodeId typeDefinitionId,
            FluentNodeManagerBase nodeManager,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            m_registration = registration;
            TypeDefinitionId = typeDefinitionId;
            m_nodeManager = nodeManager;
            m_telemetry = telemetry;
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public ExpandedNodeId TypeDefinitionId { get; }

        /// <inheritdoc/>
        public ValueTask<INodeBehaviorLease?> CreateAsync(
            NodeBehaviorContext context,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;

            // Creation only captures the inputs. The user delegate runs during
            // activation, so a failure there unwinds through the normal ledger.
            return new ValueTask<INodeBehaviorLease?>(
                new NodeAttachLease(
                    m_registration,
                    context.Node,
                    context.SystemContext,
                    m_nodeManager,
                    m_telemetry,
                    m_timeProvider));
        }

        private readonly NodeAttachRegistration m_registration;
        private readonly FluentNodeManagerBase m_nodeManager;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Owns one activated fluent behavior and the handle it returned.
    /// </summary>
    internal sealed class NodeAttachLease :
        INodeBehaviorLease,
        INodeAttachContext,
        INodeBehaviorShutdownSignal
    {
        public NodeAttachLease(
            NodeAttachRegistration registration,
            NodeState? node,
            ISystemContext systemContext,
            FluentNodeManagerBase nodeManager,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            m_registration = registration;
            m_node = node;
            SystemContext = systemContext;
            NodeManager = nodeManager;
            Telemetry = telemetry;
            TimeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public ISystemContext SystemContext { get; }

        /// <inheritdoc/>
        public FluentNodeManagerBase NodeManager { get; }

        /// <inheritdoc/>
        public TimeProvider TimeProvider { get; }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; }

        /// <inheritdoc/>
        public CancellationToken Lifetime => m_lifetime.Token;

        /// <inheritdoc/>
        public NodeState? Find(NodeId nodeId)
        {
            return NodeManager.Find(nodeId);
        }

        /// <inheritdoc/>
        public async ValueTask ActivateAsync(CancellationToken cancellationToken)
        {
            m_handle = await m_registration
                .Invoker(m_node!, this, cancellationToken, m_registration.State)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void SignalShutdown()
        {
            try
            {
                m_lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already released; the signal has nothing left to reach.
            }
        }

        /// <inheritdoc/>
        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            // Release happens in DisposeAsync so that a behavior which never finished
            // activating still gets exactly one release attempt.
            _ = cancellationToken;
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;

            // Trip the lifetime token first so background loops stop before the
            // handle they belong to is released.
            try
            {
                await m_lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The source was already disposed; nothing left to signal.
            }

            try
            {
                if (m_handle is not null)
                {
                    await m_handle.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_handle = null;
                m_lifetime.Dispose();
            }
        }

        private readonly NodeAttachRegistration m_registration;
        private readonly NodeState? m_node;
        private readonly CancellationTokenSource m_lifetime = new();
        private IAsyncDisposable? m_handle;
        private bool m_disposed;
    }
}
