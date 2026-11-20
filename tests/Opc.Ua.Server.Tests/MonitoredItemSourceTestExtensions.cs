/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Commits explicit source changes for deterministic item-state tests without a native manager.
    /// </summary>
    internal static class MonitoredItemSourceTestExtensions
    {
        /// <summary>
        /// Starts a test adoption with the source incarnation captured at the call site.
        /// </summary>
        internal static ValueTask<ServiceResult> AdoptSourceAsync(
            this INodeManagerMonitoredItemSource source,
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            var binding = (IMonitoredItemGenerationBinding)monitoredItem;
            return source.AdoptSourceAsync(
                monitoredItem, new MonitoredItemAdoption(binding.SourceRevision), cancellationToken);
        }

        /// <summary>
        /// Releases the source incarnation captured at the test call site.
        /// </summary>
        internal static ValueTask<ServiceResult> ReleaseSourceAsync(
            this INodeManagerMonitoredItemSource source,
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            return source.ReleaseSourceAsync(
                monitoredItem, ((IMonitoredItemGenerationBinding)monitoredItem).SourceRevision, cancellationToken);
        }

        /// <summary>
        /// Releases native test registration with the incarnation captured at the call site.
        /// </summary>
        internal static (ServiceResult Result, bool Changed) ReleaseSource(
            this IMonitoredItemSourceManager manager,
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache)
        {
            return manager.ReleaseSource(
                context, monitoredItem, ((IMonitoredItemGenerationBinding)monitoredItem).SourceRevision,
                removeNodeFromComponentCache);
        }

        /// <summary>
        /// Starts a native test adoption with an explicitly captured source incarnation.
        /// </summary>
        internal static (ServiceResult Result, bool Changed) AdoptSource(
            this IMonitoredItemSourceManager manager,
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache)
        {
            return manager.AdoptSource(
                context, handle, monitoredItem, addNodeToComponentCache, removeNodeFromComponentCache,
                ((IMonitoredItemGenerationBinding)monitoredItem).SourceRevision);
        }

        /// <summary>
        /// Requires the captured source to be released without erasing another incarnation.
        /// </summary>
        internal static void ReleaseSource(this IMonitoredItemGenerationBinding binding)
        {
            Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);
        }

        /// <summary>
        /// Requires the prepared source to bind without replacing an intervening incarnation.
        /// </summary>
        internal static void BindSource(
            this IMonitoredItemGenerationBinding binding,
            IAsyncNodeManager nodeManager,
            object managerHandle)
        {
            Assert.That(binding.TryBindSource(binding.SourceRevision, nodeManager, managerHandle, out _), Is.True);
        }
    }
}
