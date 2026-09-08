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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class WotProjectionBindingRuntime
    {
        private async ValueTask WireProjectedEventsAsync(
            ArrayOf<WotBindingPlan> plans, CancellationToken cancellationToken)
        {
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                WotBindingPlan plan = plans[planIndex];
                if (plan.IsDeclarationContext)
                {
                    continue;
                }
                for (int affordanceIndex = 0; affordanceIndex < plan.ProjectedAffordances.Count; affordanceIndex++)
                {
                    WotProjectedAffordance local = plan.ProjectedAffordances[affordanceIndex];
                    if (local.Kind != WotAffordanceKind.Event)
                    {
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    WotCompiledForm form = SelectForm(plan, local, WoTBindingCapabilityEnum.SubscribeEvent);
                    NodeId eventTypeId = ResolveLocalNodeId(local.NodeId, plan.ResourceXid, local.JsonPointer);
                    _ = m_builder.Node<BaseObjectTypeState>(eventTypeId);
                    NodeId notifierId = ResolveLocalNodeId(local.OwnerNodeId, plan.ResourceXid, local.JsonPointer);
                    BaseObjectState notifier = m_builder.Node<BaseObjectState>(notifierId).Node;
                    ExpandedNodeId sourceCondition = FindSourceCondition(plan, local, form);
                    ConditionState? condition = local.ConditionTypeId is null
                        ? null
                        : await m_conditionFactory.CreateAsync(
                            m_builder, notifier, local, eventTypeId, cancellationToken).ConfigureAwait(false);
                    WotProjectedEventSource? source = m_eventSources
                        .FirstOrDefault(candidate => candidate.CanShare(form));
                    if (source is null)
                    {
                        source = new WotProjectedEventSource(form, GetOrCreateSlot(form));
                        m_eventSources.Add(source);
                    }
                    TimeProvider timeProvider = m_builder.NodeManager is AsyncCustomNodeManager manager &&
                        manager.Server is ITimeProviderProvider provider
                        ? provider.TimeProvider : TimeProvider.System;
                    var binding = new WotProjectedEventBinding(
                        m_builder.Context, source, notifier, eventTypeId, condition,
                        sourceCondition, m_options.MaxEventRoutes, timeProvider,
                        plan.ResourceXid, local.JsonPointer, m_eventRoutes);
                    m_events.Add((plan.ResourceXid, local.JsonPointer), binding);
                    m_eventRoutes.Add(binding);
                }
            }
        }

        private static ExpandedNodeId FindSourceCondition(
            WotBindingPlan plan, WotProjectedAffordance local, WotCompiledForm eventForm)
        {
            ExpandedNodeId target = default;
            foreach (WotProjectedAffordance action in plan.ProjectedAffordances)
            {
                if (action.Kind != WotAffordanceKind.Action || action.ActsOn != local.Name)
                {
                    continue;
                }
                WotCompiledForm form = SelectForm(plan, action, WoTBindingCapabilityEnum.InvokeAction);
                if (!WotProjectedEventSource.SameEndpoint(eventForm, form) ||
                    !form.Addressing.Metadata.TryGetValue("componentOf", out string? owner) ||
                    string.IsNullOrEmpty(owner))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Condition action '{0}' must have a static owner at the event's selected source.",
                        action.JsonPointer);
                }
                ExpandedNodeId candidate = ExpandedNodeId.Parse(owner);
                if (candidate.IsNull || candidate.ServerIndex != 0 ||
                    (candidate.NamespaceIndex != 0 && string.IsNullOrEmpty(candidate.NamespaceUri)) ||
                    (!target.IsNull && target != candidate))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "Condition actions disagree on the portable source Condition identity.");
                }
                target = candidate;
            }
            return target;
        }

        private void RegisterEventPublishers()
        {
            var notifiers = new Dictionary<NodeId, BaseObjectState>();
            var roots = new HashSet<NodeId>();
            foreach (WotProjectedEventBinding binding in m_events.Values)
            {
                notifiers[binding.Notifier.NodeId] = binding.Notifier;
                CollectNotifierRoots(binding.Notifier, notifiers, roots);
            }
            foreach (BaseObjectState notifier in notifiers.Values)
            {
                ArrayOf<WotProjectedEventBinding> bindings = m_events.Values
                    .Where(binding => binding.Notifier.NodeId == notifier.NodeId).ToArrayOf();
                m_eventPublisher.Register(
                    m_builder, notifier, _ => new ReadyEventStream(this, bindings), roots.Contains(notifier.NodeId));
            }
        }

        private void CollectNotifierRoots(
            BaseObjectState source, Dictionary<NodeId, BaseObjectState> notifiers, HashSet<NodeId> roots)
        {
            var visited = new HashSet<NodeId>();
            var active = new HashSet<NodeId>();
            var pending = new Stack<(NodeState Node, BaseObjectState Root, bool Exit)>();
            pending.Push((source, source, false));
            while (pending.Count > 0)
            {
                (NodeState node, BaseObjectState root, bool exit) = pending.Pop();
                if (exit)
                {
                    active.Remove(node.NodeId);
                    visited.Add(node.NodeId);
                    continue;
                }
                if (visited.Contains(node.NodeId))
                {
                    continue;
                }
                if (!active.Add(node.NodeId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The projected notifier graph contains a cycle.");
                }
                pending.Push((node, root, true));
                if (node is BaseObjectState notifier)
                {
                    notifier.EventNotifier |= EventNotifiers.SubscribeToEvents;
                    root = notifier;
                }
                ArrayOf<NodeState> parents = ResolveNotifierParents(node);
                if (parents.Count == 0)
                {
                    notifiers[root.NodeId] = root;
                    roots.Add(root.NodeId);
                }
                foreach (NodeState parent in parents)
                {
                    pending.Push((parent, root, false));
                }
            }
        }

        private ArrayOf<NodeState> ResolveNotifierParents(NodeState node)
        {
            var parents = new Dictionary<NodeId, NodeState>();
            if (node is BaseInstanceState { Parent: { } parent })
            {
                parents[parent.NodeId] = parent;
            }
            var declared = new List<NodeState.Notifier>();
            node.GetNotifiers(m_builder.Context, declared);
            foreach (NodeState.Notifier notifier in declared)
            {
                if (notifier.IsInverse && notifier.Node is { } target && target.NodeId != Ua.ObjectIds.Server)
                {
                    parents[target.NodeId] = target;
                }
            }
            var references = new List<IReference>();
            node.GetReferences(m_builder.Context, references);
            foreach (IReference reference in references)
            {
                if (!reference.IsInverse || reference.ReferenceTypeId != Ua.ReferenceTypeIds.HasNotifier)
                {
                    continue;
                }
                NodeId targetId = ExpandedNodeId.ToNodeId(reference.TargetId, m_builder.Context.NamespaceUris);
                if (targetId == Ua.ObjectIds.Server || parents.ContainsKey(targetId))
                {
                    continue;
                }
                NodeState target = m_builder.Node(targetId).Node;
                bool hierarchicalAncestor = false;
                var hierarchy = new HashSet<NodeId>();
                for (NodeState? ancestor = node is BaseInstanceState instance ? instance.Parent : null;
                    ancestor is not null && hierarchy.Add(ancestor.NodeId);
                    ancestor = ancestor is BaseInstanceState next ? next.Parent : null)
                {
                    if (ancestor.NodeId == targetId)
                    {
                        hierarchicalAncestor = true;
                        break;
                    }
                }
                if (!hierarchicalAncestor)
                {
                    node.AddNotifier(m_builder.Context, Ua.ReferenceTypeIds.HasNotifier, true, target);
                    target.AddNotifier(m_builder.Context, Ua.ReferenceTypeIds.HasNotifier, false, node);
                    parents[targetId] = target;
                }
            }
            return parents.Values.ToArrayOf();
        }

        private async IAsyncEnumerable<BaseEventState> StreamEventsAsync(
            ArrayOf<WotProjectedEventBinding> bindings,
            TaskCompletionSource<bool> ready,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_generationToken);
            CancellationToken token = lifetime.Token;
            var queue = Channel.CreateBounded<BaseEventState>(new BoundedChannelOptions(m_options.MaxQueuedEvents)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            var leases = new List<IAsyncDisposable>();
            var groups = new Dictionary<WotProjectedEventSource, List<WotProjectedEventBinding>>();
            for (int i = 0; i < bindings.Count; i++)
            {
                WotProjectedEventBinding binding = bindings[i];
                if (!groups.TryGetValue(binding.Source, out List<WotProjectedEventBinding>? targets))
                {
                    targets = [];
                    groups.Add(binding.Source, targets);
                }
                targets.Add(binding);
            }
            try
            {
                try
                {
                    foreach (KeyValuePair<WotProjectedEventSource, List<WotProjectedEventBinding>> group in groups)
                    {
                        ArrayOf<WotProjectedEventBinding> targets = group.Value.ToArrayOf();
                        leases.Add(await group.Key.AttachAsync(notification =>
                        {
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }
                            try
                            {
                                foreach (WotProjectedEventBinding binding in targets)
                                {
                                    BaseEventState? projected = binding.Project(notification);
                                    if (projected is not null && !queue.Writer.TryWrite(projected))
                                    {
                                        queue.Writer.TryComplete(new ServiceResultException(
                                            StatusCodes.BadTooManyOperations, "The projected event queue is full."));
                                        return;
                                    }
                                }
                            }
                            catch (Exception exception) when (exception is not OutOfMemoryException)
                            {
                                queue.Writer.TryComplete(exception);
                            }
                        }, token).ConfigureAwait(false));
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    ready.TrySetException(exception);
                    _ = ready.Task.Exception;
                    throw;
                }
                ready.TrySetResult(true);
                await foreach (BaseEventState notification in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    yield return notification;
                }
            }
            finally
            {
                ready.TrySetCanceled(token);
                queue.Writer.TryComplete();
                await DisposeEventLeasesAsync(leases).ConfigureAwait(false);
            }
        }

        private sealed class ReadyEventStream(
            WotProjectionBindingRuntime owner,
            ArrayOf<WotProjectedEventBinding> bindings) : IAsyncEnumerable<BaseEventState>, IEventSourceReadiness
        {
            public IAsyncEnumerator<BaseEventState> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                if (Interlocked.Exchange(ref m_enumerated, 1) != 0)
                {
                    throw new InvalidOperationException("A projected event activation can only be enumerated once.");
                }
                return owner.StreamEventsAsync(bindings, m_ready, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }

            public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask(m_ready.Task.WaitAsync(cancellationToken));
            }

            private readonly TaskCompletionSource<bool> m_ready =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_enumerated;
        }

        private static async ValueTask DisposeEventLeasesAsync(List<IAsyncDisposable> leases)
        {
            List<Exception>? errors = null;
            foreach (IAsyncDisposable lease in leases)
            {
                try
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    (errors ??= []).Add(exception);
                }
            }
            if (errors is not null)
            {
                throw new AggregateException("Projected event subscriptions failed to stop.", errors);
            }
        }

        private readonly List<WotProjectedEventSource> m_eventSources = [];
        private readonly Dictionary<(string ResourceXid, string Pointer), WotProjectedEventBinding> m_events = [];
    }
}
