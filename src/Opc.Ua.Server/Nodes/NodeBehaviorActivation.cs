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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Nodes
{
    /// <summary>
    /// Transactionally creates, activates, deactivates, and disposes node behaviors.
    /// </summary>
    internal sealed class NodeBehaviorActivation
    {
        /// <summary>
        /// Initializes an activation owner for one prepared source generation.
        /// </summary>
        public NodeBehaviorActivation(
            NodeBehaviorRegistry registry,
            NodeBehaviorAddressSpace addressSpace,
            ISystemContext systemContext,
            IServiceProvider? services,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            INodeSource source,
            NodeBehaviorGenerationIdentity generation)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_addressSpace = addressSpace ??
                throw new ArgumentNullException(nameof(addressSpace));
            m_systemContext = systemContext ??
                throw new ArgumentNullException(nameof(systemContext));
            m_services = services;
            m_telemetry = telemetry ??
                throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider ??
                throw new ArgumentNullException(nameof(timeProvider));
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            m_generation = generation ??
                throw new ArgumentNullException(nameof(generation));
        }

        /// <summary>
        /// Activates matching behaviors child-first and base-to-derived.
        /// </summary>
        public ValueTask ActivateAsync(
            ArrayOf<NodeState> nodes,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completion;
            lock (m_gate)
            {
                if (m_state != ActivationState.Created)
                {
                    throw new InvalidOperationException(
                        "Node behaviors can only be activated once.");
                }

                m_state = ActivationState.Activating;
                completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                m_activationTask = completion.Task;
            }

            _ = CompleteActivationAsync(nodes, completion, cancellationToken);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Deactivates and disposes every owned lease exactly once.
        /// </summary>
        public ValueTask DeactivateAndDisposeAsync()
        {
            lock (m_gate)
            {
                if (m_state == ActivationState.Cleaned)
                {
                    return default;
                }
                if (m_cleanupTask is null)
                {
                    m_cleanupTask = DeactivateAndDisposeCoreAsync(m_activationTask);
                }
                return new ValueTask(m_cleanupTask);
            }
        }

        private async Task ActivateCoreAsync(
            ArrayOf<NodeState> nodes,
            CancellationToken cancellationToken)
        {
            try
            {
                List<ActivationPlan> plans = CreatePlans(nodes);
                for (int planIndex = 0; planIndex < plans.Count; planIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ActivationPlan plan = plans[planIndex];
                    int firstLeaseIndex = m_leases.Count;
                    var context = new NodeBehaviorContext(
                        plan.Node,
                        m_systemContext,
                        m_addressSpace,
                        m_services,
                        m_telemetry,
                        m_timeProvider,
                        m_source,
                        m_generation);

                    for (int factoryIndex = 0;
                        factoryIndex < plan.Factories.Count;
                        factoryIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        INodeBehaviorLease lease = await plan.Factories[factoryIndex]
                            .CreateAsync(context, cancellationToken)
                            .ConfigureAwait(false);
                        if (lease is null)
                        {
                            throw new InvalidOperationException(
                                $"Node behavior factory for '{plan.Node.NodeId}' " +
                                "returned a null lease.");
                        }
                        if (!m_leaseSet.Add(lease))
                        {
                            throw new InvalidOperationException(
                                $"A node behavior lease was returned more than once for " +
                                $"'{plan.Node.NodeId}'.");
                        }

                        m_leases.Add(new LeaseEntry(lease));
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    for (int leaseIndex = firstLeaseIndex;
                        leaseIndex < m_leases.Count;
                        leaseIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        LeaseEntry entry = m_leases[leaseIndex];
                        await entry.Lease
                            .ActivateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        entry.Activated = true;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    m_state = ActivationState.Active;
                }
            }
            catch (Exception activationException) when (
                activationException is not OutOfMemoryException)
            {
                List<Exception> cleanupFailures =
                    await CleanupCoreAsync().ConfigureAwait(false);
                lock (m_gate)
                {
                    m_state = ActivationState.Cleaned;
                }
                if (cleanupFailures.Count == 0)
                {
                    ExceptionDispatchInfo.Capture(activationException).Throw();
                }

                cleanupFailures.Insert(0, activationException);
                if (activationException is OperationCanceledException canceled)
                {
                    throw new OperationCanceledException(
                        "Node behavior activation was canceled and rollback failed.",
                        new AggregateException(cleanupFailures),
                        canceled.CancellationToken);
                }
                throw new AggregateException(
                    "Node behavior activation and rollback both failed.",
                    cleanupFailures);
            }
        }

        private async Task CompleteActivationAsync(
            ArrayOf<NodeState> nodes,
            TaskCompletionSource<bool> completion,
            CancellationToken cancellationToken)
        {
            try
            {
                await ActivateCoreAsync(nodes, cancellationToken).ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException exception)
            {
                if (exception.InnerException is null)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                else
                {
                    completion.TrySetException(exception);
                }
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task DeactivateAndDisposeCoreAsync(Task? activationTask)
        {
            if (activationTask is not null)
            {
                try
                {
                    await activationTask.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    lock (m_gate)
                    {
                        if (m_state != ActivationState.Cleaned)
                        {
                            throw;
                        }
                    }

                    // ActivateCoreAsync owns and surfaces this failure after completing rollback.
                    return;
                }
            }

            lock (m_gate)
            {
                if (m_state == ActivationState.Cleaned)
                {
                    return;
                }
                m_state = ActivationState.Cleaning;
            }

            List<Exception> failures =
                await CleanupCoreAsync().ConfigureAwait(false);
            lock (m_gate)
            {
                m_state = ActivationState.Cleaned;
            }
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "One or more node behaviors failed during cleanup.",
                    failures);
            }
        }

        private List<ActivationPlan> CreatePlans(ArrayOf<NodeState> nodes)
        {
            var plans = new List<ActivationPlan>();
            if (nodes.IsNull || m_registry.IsEmpty)
            {
                return plans;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                if (node is not BaseInstanceState instance ||
                    instance.TypeDefinitionId.IsNull)
                {
                    continue;
                }

                ArrayOf<INodeBehaviorFactory> factories =
                    m_registry.ResolveFactories(instance.TypeDefinitionId);
                if (!factories.IsNull && factories.Count > 0)
                {
                    plans.Add(new ActivationPlan(
                        node,
                        factories,
                        GetHierarchyDepth(instance)));
                }
            }

            plans.Sort(static (left, right) =>
            {
                int depth = right.Depth.CompareTo(left.Depth);
                return depth != 0
                    ? depth
                    : left.Node.NodeId.CompareTo(right.Node.NodeId);
            });
            return plans;
        }

        private static int GetHierarchyDepth(BaseInstanceState node)
        {
            int depth = 0;
            var visited = new HashSet<NodeId>();
            for (NodeState? current = node.Parent;
                current is not null;
                current = current is BaseInstanceState instance
                    ? instance.Parent
                    : null)
            {
                if (!current.NodeId.IsNull && !visited.Add(current.NodeId))
                {
                    throw new InvalidOperationException(
                        $"The node hierarchy contains a cycle at '{current.NodeId}'.");
                }
                depth++;
            }
            return depth;
        }

        private async ValueTask<List<Exception>> CleanupCoreAsync()
        {
            var failures = new List<Exception>();
            for (int i = m_leases.Count - 1; i >= 0; i--)
            {
                LeaseEntry entry = m_leases[i];
                if (!entry.Activated || entry.DeactivationAttempted)
                {
                    continue;
                }

                entry.DeactivationAttempted = true;
                try
                {
                    await entry.Lease
                        .DeactivateAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
            }

            for (int i = m_leases.Count - 1; i >= 0; i--)
            {
                LeaseEntry entry = m_leases[i];
                if (entry.DisposalAttempted)
                {
                    continue;
                }

                entry.DisposalAttempted = true;
                try
                {
                    await entry.Lease.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
            }

            m_leases.Clear();
            m_leaseSet.Clear();
            return failures;
        }

        private sealed class ActivationPlan
        {
            public ActivationPlan(
                NodeState node,
                ArrayOf<INodeBehaviorFactory> factories,
                int depth)
            {
                Node = node;
                Factories = factories;
                Depth = depth;
            }

            public NodeState Node { get; }

            public ArrayOf<INodeBehaviorFactory> Factories { get; }

            public int Depth { get; }
        }

        private sealed class LeaseEntry
        {
            public LeaseEntry(INodeBehaviorLease lease)
            {
                Lease = lease;
            }

            public INodeBehaviorLease Lease { get; }

            public bool Activated { get; set; }

            public bool DeactivationAttempted { get; set; }

            public bool DisposalAttempted { get; set; }
        }

        private enum ActivationState
        {
            Created,
            Activating,
            Active,
            Cleaning,
            Cleaned
        }

        private readonly NodeBehaviorRegistry m_registry;
        private readonly NodeBehaviorAddressSpace m_addressSpace;
        private readonly ISystemContext m_systemContext;
        private readonly IServiceProvider? m_services;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly INodeSource m_source;
        private readonly NodeBehaviorGenerationIdentity m_generation;
        private readonly Lock m_gate = new();
        private readonly List<LeaseEntry> m_leases = [];
        private readonly HashSet<INodeBehaviorLease> m_leaseSet =
            new(LeaseReferenceComparer.Instance);
        private Task? m_activationTask;
        private Task? m_cleanupTask;
        private ActivationState m_state;

        private sealed class LeaseReferenceComparer :
            IEqualityComparer<INodeBehaviorLease>
        {
            public static LeaseReferenceComparer Instance { get; } = new();

            public bool Equals(
                INodeBehaviorLease? left,
                INodeBehaviorLease? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(INodeBehaviorLease lease)
            {
                return RuntimeHelpers.GetHashCode(lease);
            }
        }
    }
}
