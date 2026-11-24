// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class AsyncCustomNodeManager
    {
        private async ValueTask<(AllEventsSubscription Subscription, AllEventsOwner Owner, NodeState[] Sources)>
            PrepareAllEventsChangeAsync(
                IEventMonitoredItem item,
                bool unsubscribe,
                CancellationToken cancellationToken)
        {
            AllEventsSubscription subscription = m_allEventsSubscriptions.GetValue(
                item, static _ => new AllEventsSubscription());
            AllEventsOwner owner = subscription.Owners.GetValue(
                (object?)GenerationReconciler.GetDispatchBinding(item, unsubscribe) ?? s_directAllEventsOwner,
                static _ => new AllEventsOwner());
            using (await AcquireSemaphoreAsync(m_monitoredItemSemaphore, cancellationToken).ConfigureAwait(false))
            {
                if (!unsubscribe && !owner.Active)
                {
                    owner.Active = true;
                    owner.ReleaseMode = AllEventsReleaseMode.None;
                    subscription.ActiveOwnerCount++;
                }
                else if (unsubscribe && owner.Active)
                {
                    owner.Active = false;
                    subscription.ActiveOwnerCount--;
                    owner.ReleaseMode = subscription.ActiveOwnerCount == 0
                        ? AllEventsReleaseMode.Last
                        : AllEventsReleaseMode.Shared;
                }
                else if (unsubscribe && owner.ReleaseMode == AllEventsReleaseMode.None &&
                    subscription.ActiveOwnerCount == 0)
                {
                    // A dynamically registered root can have attached an existing Server item.
                    owner.ReleaseMode = AllEventsReleaseMode.Last;
                }
                var roots = new List<NodeState>(RootNotifiers.Values);
                if (unsubscribe)
                {
                    foreach (PendingEventProviderRelease pending in owner.PendingReleases)
                    {
                        if (!roots.Exists(root => ReferenceEquals(root, pending.Source)))
                        {
                            roots.Add(pending.Source);
                        }
                    }
                }
                return (subscription, owner, [.. roots]);
            }
        }

        private async ValueTask<ServiceResult> ChangeAllEventsRootAsync(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem item,
            AllEventsSubscription? subscription,
            AllEventsOwner? owner,
            bool unsubscribe,
            CancellationToken cancellationToken)
        {
            var change = new AllEventsRootChange(source, item, subscription, owner);
            using var scope = new NodeManagerAmbientScope<(
                WeakReference<AllEventsRootChange>? Change, bool Unsubscribe)>(
                    m_allEventsRootChange, (new WeakReference<AllEventsRootChange>(change), unsubscribe));
            try
            {
                return await SubscribeToEventsAsync(context, source, item, unsubscribe, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                GC.KeepAlive(change);
            }
        }

        private AllEventsRootChange? GetAllEventsRootChange(
            NodeState source,
            IEventMonitoredItem item,
            bool unsubscribe)
        {
            var scope = NodeManagerAmbientScope<(
                WeakReference<AllEventsRootChange>? Change, bool Unsubscribe)>.GetValue(m_allEventsRootChange);
            return scope.Unsubscribe == unsubscribe &&
                scope.Change?.TryGetTarget(out AllEventsRootChange? change) == true &&
                change is not null && ReferenceEquals(change.Source, source) && ReferenceEquals(change.Item, item)
                    ? change
                    : null;
        }

        private async ValueTask<ServiceResult> ReleaseAllEventsRootAsync(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem item,
            AllEventsRootChange change,
            CancellationToken cancellationToken)
        {
            AllEventsOwner owner = change.Owner!;
            PendingEventProviderRelease? pending;
            MonitoredNode2? monitored;
            using (await AcquireSemaphoreAsync(m_monitoredItemSemaphore, cancellationToken).ConfigureAwait(false))
            {
                pending = owner.PendingReleases.Find(release => ReferenceEquals(release.Source, source));
                if (pending is not null)
                {
                    if (pending.Executing)
                    {
                        return ServiceResult.Good;
                    }
                    monitored = pending.MonitoredNode;
                }
                else
                {
                    if (owner.ReleaseMode == AllEventsReleaseMode.None ||
                        !IsCurrentRootNotifier(source) ||
                        !m_monitoredItemManager.MonitoredNodes.TryGetValue(source.NodeId, out monitored) ||
                        !ReferenceEquals(monitored.Node, source) ||
                        !monitored.TryGetEventItem(item.Id, out IEventMonitoredItem? registered))
                    {
                        return ServiceResult.Good;
                    }
                    if (!ReferenceEquals(registered, item))
                    {
                        return StatusCodes.BadMonitoredItemIdInvalid;
                    }

                    bool anotherOwner = change.Subscription!.ActiveOwnerCount != 0;
                    if (owner.ReleaseMode == AllEventsReleaseMode.Last && !anotherOwner)
                    {
                        (_, ServiceResult result) = m_monitoredItemManager.SubscribeToEvents(
                            context, source, item, unsubscribe: true);
                        if (ServiceResult.IsBad(result))
                        {
                            return result;
                        }
                        source.SetAreEventsMonitored(context, false, true);
                        // Keep the exact source even after removing the native link or replacing the root.
                        pending = new PendingEventProviderRelease(source, monitored);
                        owner.PendingReleases.Add(pending);
                    }
                    else if (owner.ReleaseMode != AllEventsReleaseMode.Shared || !anotherOwner)
                    {
                        return ServiceResult.Good;
                    }
                }
                if (pending is not null)
                {
                    pending.Executing = true;
                }
            }

            bool released = false;
            try
            {
                await OnSubscribeToEventsAsync(context, monitored, unsubscribe: true, cancellationToken)
                    .ConfigureAwait(false);
                released = true;
                return ServiceResult.Good;
            }
            finally
            {
                if (pending is not null)
                {
                    await m_monitoredItemSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        pending.Executing = false;
                        if (released)
                        {
                            owner.PendingReleases.Remove(pending);
                        }
                    }
                    finally
                    {
                        m_monitoredItemSemaphore.Release();
                    }
                }
            }
        }

        /// <summary>
        /// Captures the forwarded call to the existing virtual per-root subscription hook.
        /// </summary>
        private sealed record AllEventsRootChange(
            NodeState Source,
            IEventMonitoredItem Item,
            AllEventsSubscription? Subscription,
            AllEventsOwner? Owner);

        /// <summary>
        /// Tracks the logical owners of one exact item without retaining removed binding records.
        /// Mutable state is protected by the monitored-item semaphore.
        /// </summary>
        private sealed class AllEventsSubscription
        {
            /// <summary>
            /// Logical owner state keyed by the exact generation binding, or the direct-call token.
            /// </summary>
            public ConditionalWeakTable<object, AllEventsOwner> Owners { get; } = new();

            /// <summary>
            /// The number of owners that still need the physical links.
            /// </summary>
            public int ActiveOwnerCount { get; set; }
        }

        /// <summary>
        /// Tracks one logical owner and the provider releases it still owes.
        /// </summary>
        private sealed class AllEventsOwner
        {
            /// <summary>
            /// Whether this owner still needs the physical links.
            /// </summary>
            public bool Active { get; set; }

            /// <summary>
            /// How repeated release callbacks are handled for this owner.
            /// </summary>
            public AllEventsReleaseMode ReleaseMode { get; set; }

            /// <summary>
            /// Provider releases still outstanding after native detachment.
            /// </summary>
            public List<PendingEventProviderRelease> PendingReleases { get; } = [];
        }

        /// <summary>
        /// Captures a detached physical link whose provider callback has not yet succeeded.
        /// </summary>
        private sealed class PendingEventProviderRelease(NodeState source, MonitoredNode2 monitoredNode)
        {
            /// <summary>
            /// The original source, not a later registration with the same NodeId.
            /// </summary>
            public NodeState Source { get; } = source;

            /// <summary>
            /// The detached monitored node to present to the provider.
            /// </summary>
            public MonitoredNode2 MonitoredNode { get; } = monitoredNode;

            /// <summary>
            /// Whether a release attempt is already executing for this checkpoint.
            /// </summary>
            public bool Executing { get; set; }
        }

        /// <summary>
        /// Determines whether an owner release may detach a physical link.
        /// </summary>
        private enum AllEventsReleaseMode
        {
            None,
            Shared,
            Last
        }

        /// <summary>Identifies unscoped all-events calls.</summary>
        private static readonly object s_directAllEventsOwner = new();

        /// <summary>Tracks physical subscription ownership for each exact Server item.</summary>
        private readonly ConditionalWeakTable<IEventMonitoredItem, AllEventsSubscription> m_allEventsSubscriptions =
            new();

        /// <summary>Carries per-root state through the virtual event subscription hook.</summary>
        private readonly AsyncLocal<NodeManagerAmbientScope<(
            WeakReference<AllEventsRootChange>? Change, bool Unsubscribe)>?> m_allEventsRootChange = new();
    }
}
