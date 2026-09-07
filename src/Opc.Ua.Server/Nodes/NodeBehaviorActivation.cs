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
    /// <remarks>
    /// <para>
    /// One instance owns one activation pass. Activation runs deepest node first, and
    /// on each node from the base-registered behavior to the derived-registered one.
    /// Manager-scoped behaviors, which own no node, run last so they may drive whatever
    /// the node behaviors prepared.
    /// </para>
    /// <para>
    /// Any failure — including cancellation — unwinds every lease recorded so far in
    /// exact reverse order and rethrows. Cleanup failures are collected and aggregated
    /// with the original failure rather than replacing it.
    /// </para>
    /// </remarks>
    internal sealed class NodeBehaviorActivation
    {
        /// <summary>
        /// Initializes an activation pass.
        /// </summary>
        /// <param name="registry">Resolves type-keyed registrations.</param>
        /// <param name="addressSpace">Resolves nodes for behaviors.</param>
        /// <param name="systemContext">The node manager's system context.</param>
        /// <param name="telemetry">The server telemetry context.</param>
        /// <param name="timeProvider">The server time provider.</param>
        /// <param name="pinned">Registrations bound to an already-resolved node.</param>
        /// <param name="managerScoped">Registrations that own no node.</param>
        /// <param name="factoriesRequiringMatch">
        /// Factories that must match at least one node. Planning fails before any lease
        /// is created when one of them matches nothing.
        /// </param>
        public NodeBehaviorActivation(
            NodeBehaviorRegistry registry,
            NodeBehaviorAddressSpace addressSpace,
            ISystemContext systemContext,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            IReadOnlyList<NodeBehaviorPinnedRegistration>? pinned = null,
            IReadOnlyList<INodeBehaviorFactory>? managerScoped = null,
            IReadOnlyCollection<INodeBehaviorFactory>? factoriesRequiringMatch = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_addressSpace = addressSpace ??
                throw new ArgumentNullException(nameof(addressSpace));
            m_systemContext = systemContext ??
                throw new ArgumentNullException(nameof(systemContext));
            m_telemetry = telemetry ??
                throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider ??
                throw new ArgumentNullException(nameof(timeProvider));
            m_pinned = pinned ?? [];
            m_managerScoped = managerScoped ?? [];
            m_factoriesRequiringMatch = factoriesRequiringMatch;
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
            Task cleanupTask;
            Task? activationTask = null;
            TaskCompletionSource<bool>? completion = null;

            lock (m_gate)
            {
                if (m_state == ActivationState.Cleaned)
                {
                    return default;
                }
                if (m_cleanupTask is null)
                {
                    // Publish the shared task under the gate but start the work outside
                    // it, so no cleanup ever runs while the gate is held.
                    completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    m_cleanupTask = completion.Task;
                    activationTask = m_activationTask;
                }
                cleanupTask = m_cleanupTask;
            }

            if (completion is not null)
            {
                _ = CompleteCleanupAsync(activationTask, completion);
            }
            return new ValueTask(cleanupTask);
        }

        /// <summary>
        /// Trips every owned behavior's shutdown signal without waiting for release.
        /// </summary>
        /// <remarks>
        /// Used by synchronous disposal, which must never block: it stops background
        /// loops promptly and leaves the awaited release to the async teardown path.
        /// </remarks>
        public void SignalShutdown()
        {
            lock (m_gate)
            {
                for (int i = 0; i < m_leases.Count; i++)
                {
                    if (m_leases[i].Lease is INodeBehaviorShutdownSignal signal)
                    {
                        try
                        {
                            signal.SignalShutdown();
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            // Best effort: a behavior that cannot be signalled is still
                            // released by the async teardown path.
                        }
                    }
                }
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
                        m_telemetry,
                        m_timeProvider);

                    for (int factoryIndex = 0;
                        factoryIndex < plan.Factories.Count;
                        factoryIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        INodeBehaviorLease? lease = await plan.Factories[factoryIndex]
                            .CreateAsync(context, cancellationToken)
                            .ConfigureAwait(false);
                        if (lease is null)
                        {
                            // The factory declined this node. Nothing is recorded, so
                            // nothing is unwound for it later.
                            continue;
                        }
                        if (!m_leaseSet.Add(lease))
                        {
                            throw new InvalidOperationException(
                                "A node behavior lease was returned more than once " +
                                $"for '{DescribeTarget(plan.Node)}'.");
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

        private async Task CompleteCleanupAsync(
            Task? activationTask,
            TaskCompletionSource<bool> completion)
        {
            try
            {
                await DeactivateAndDisposeCoreAsync(activationTask)
                    .ConfigureAwait(false);
                completion.TrySetResult(true);
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

                    // ActivateCoreAsync owns and surfaces this failure after completing
                    // its own rollback.
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

            List<Exception> failures = await CleanupCoreAsync().ConfigureAwait(false);
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
            HashSet<INodeBehaviorFactory>? matched = m_factoriesRequiringMatch is null
                ? null
                : new HashSet<INodeBehaviorFactory>(ReferenceComparer.FactoryInstance);

            // 1. Type-keyed registrations, fanned out over every matching instance.
            if (!nodes.IsNull && !m_registry.IsEmpty)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    NodeState node = nodes[i];
                    if (!IsTypeMatchCandidate(node, out BaseInstanceState? instance))
                    {
                        continue;
                    }

                    ArrayOf<INodeBehaviorFactory> factories =
                        m_registry.ResolveFactories(instance!.TypeDefinitionId);
                    if (factories.IsNull || factories.Count == 0)
                    {
                        continue;
                    }

                    if (matched is not null)
                    {
                        for (int f = 0; f < factories.Count; f++)
                        {
                            matched.Add(factories[f]);
                        }
                    }

                    plans.Add(new ActivationPlan(
                        node,
                        factories,
                        GetHierarchyDepth(instance)));
                }
            }

            // 2. Registrations pinned to one already-resolved node.
            for (int i = 0; i < m_pinned.Count; i++)
            {
                NodeBehaviorPinnedRegistration pin = m_pinned[i];
                if (pin.Node is null || pin.Factory is null)
                {
                    throw new InvalidOperationException(
                        "A pinned node behavior registration is incomplete.");
                }

                matched?.Add(pin.Factory);
                plans.Add(new ActivationPlan(
                    pin.Node,
                    new ArrayOf<INodeBehaviorFactory>(new[] { pin.Factory }),
                    pin.Node is BaseInstanceState pinnedInstance
                        ? GetHierarchyDepth(pinnedInstance)
                        : 0));
            }

            // 3. Manager-scoped registrations own no node. They sort last, so they
            //    activate after every node behavior and unwind before them.
            for (int i = 0; i < m_managerScoped.Count; i++)
            {
                INodeBehaviorFactory factory = m_managerScoped[i] ??
                    throw new InvalidOperationException(
                        "A manager-scoped node behavior registration is null.");
                matched?.Add(factory);
                plans.Add(new ActivationPlan(
                    node: null,
                    new ArrayOf<INodeBehaviorFactory>(new[] { factory }),
                    ManagerScopedDepth));
            }

            if (matched is not null)
            {
                foreach (INodeBehaviorFactory required in m_factoriesRequiringMatch!)
                {
                    if (!matched.Contains(required))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "No node matched the behavior registered for type " +
                            "definition '{0}'. Set AllowZeroMatches when an empty " +
                            "match is expected.",
                            required.TypeDefinitionId);
                    }
                }
            }

            plans.Sort(static (left, right) =>
            {
                int depth = right.Depth.CompareTo(left.Depth);
                if (depth != 0)
                {
                    return depth;
                }
                if (left.Node is null || right.Node is null)
                {
                    return left.Node is null && right.Node is null
                        ? 0
                        : left.Node is null ? 1 : -1;
                }
                return left.Node.NodeId.CompareTo(right.Node.NodeId);
            });
            return plans;
        }

        /// <summary>
        /// Decides whether a node participates in type-keyed matching.
        /// </summary>
        /// <remarks>
        /// Method nodes are excluded on purpose. <see cref="MethodState"/> aliases its
        /// MethodDeclarationId onto TypeDefinitionId, so every method would otherwise
        /// match a registration for its declaration — a surprise nobody asked for.
        /// Method behavior belongs on the method's own call handler.
        /// </remarks>
        private static bool IsTypeMatchCandidate(
            NodeState node,
            out BaseInstanceState? instance)
        {
            instance = null;
            if (node is MethodState || node is not BaseInstanceState candidate)
            {
                return false;
            }
            if (candidate.TypeDefinitionId.IsNull)
            {
                return false;
            }

            instance = candidate;
            return true;
        }

        private static string DescribeTarget(NodeState? node)
        {
            return node is null ? "the node manager" : node.NodeId.ToString();
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
                NodeState? node,
                ArrayOf<INodeBehaviorFactory> factories,
                int depth)
            {
                Node = node;
                Factories = factories;
                Depth = depth;
            }

            public NodeState? Node { get; }

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

        private sealed class ReferenceComparer :
            IEqualityComparer<INodeBehaviorLease>,
            IEqualityComparer<INodeBehaviorFactory>
        {
            public static ReferenceComparer Instance { get; } = new();

            public static IEqualityComparer<INodeBehaviorFactory> FactoryInstance
                => Instance;

            public bool Equals(INodeBehaviorLease? left, INodeBehaviorLease? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(INodeBehaviorLease lease)
            {
                return RuntimeHelpers.GetHashCode(lease);
            }

            public bool Equals(INodeBehaviorFactory? left, INodeBehaviorFactory? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(INodeBehaviorFactory factory)
            {
                return RuntimeHelpers.GetHashCode(factory);
            }
        }

        private const int ManagerScopedDepth = -1;

        private readonly NodeBehaviorRegistry m_registry;
        private readonly NodeBehaviorAddressSpace m_addressSpace;
        private readonly ISystemContext m_systemContext;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly IReadOnlyList<NodeBehaviorPinnedRegistration> m_pinned;
        private readonly IReadOnlyList<INodeBehaviorFactory> m_managerScoped;
        private readonly IReadOnlyCollection<INodeBehaviorFactory>? m_factoriesRequiringMatch;
        private readonly Lock m_gate = new();
        private readonly List<LeaseEntry> m_leases = [];
        private readonly HashSet<INodeBehaviorLease> m_leaseSet =
            new(ReferenceComparer.Instance);
        private Task? m_activationTask;
        private Task? m_cleanupTask;
        private ActivationState m_state;
    }

    /// <summary>
    /// Implemented by leases that can stop their work without being awaited.
    /// </summary>
    internal interface INodeBehaviorShutdownSignal
    {
        /// <summary>
        /// Signals that the behavior should stop. Must not block.
        /// </summary>
        void SignalShutdown();
    }
}
