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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryBridgeNodeManager
    {
        /// <summary>
        /// Reports whether the native projection currently needs authoritative repair.
        /// </summary>
        public bool IsProjectionDegraded => m_projectionDegraded;

        /// <summary>
        /// Reports a notification delivery failure; full reads and polling can repair
        /// consumer state without inventing a replayable native event history.
        /// </summary>
        public bool IsNotificationDegraded => m_notificationDegraded || m_eventEncodingDegraded;

        /// <inheritdoc/>
        public override async ValueTask<ServiceResult> ValidateRolePermissionsAsync(
            OperationContext operationContext, NodeId nodeId, PermissionType requestedPermission,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.NamespaceIndex == InstanceNamespaceIndex &&
                (requestedPermission & PermissionType.ReceiveEvents) != 0)
            {
                try
                {
                    _ = await AuthorizeAsync(SystemContext.Copy(operationContext), false, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    return ServiceResult.Create(exception.StatusCode, "The event receiver is not authorized.");
                }
            }
            return await base.ValidateRolePermissionsAsync(operationContext, nodeId, requestedPermission,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Browses only a complete published projection, never a candidate being staged.
        /// </summary>
        /// <exception cref="ServiceResultException">The projection changed or is unavailable.</exception>
        public new async ValueTask<ContinuationPoint?> BrowseAsync(
            OperationContext context, ContinuationPoint continuationPoint, IList<ReferenceDescription> references,
            CancellationToken cancellationToken = default)
        {
            bool projected = continuationPoint.NodeToBrowse is NodeHandle handle && IsProjectionNode(handle.NodeId);
            if (projected)
            {
                EnsureProjectionAvailable();
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            long revision = Volatile.Read(ref m_projectionRevision);
            int count = references.Count;
            ContinuationPoint
                ? result = await base.BrowseAsync(context, continuationPoint, references, cancellationToken)
                .ConfigureAwait(false);
            if (projected && (m_projectionPending || revision != Volatile.Read(ref m_projectionRevision)))
            {
                while (references.Count > count)
                {
                    references.RemoveAt(references.Count - 1);
                }
                result?.Dispose();
                throw new ServiceResultException(StatusCodes.BadWaitingForInitialData,
                    "The native projection changed while browsing.");
            }
            return result;
        }

        /// <inheritdoc/>
        protected override async ValueTask<ServiceResult> SubscribeToEventsAsync(
            ServerSystemContext context, NodeState source, IEventMonitoredItem monitoredItem,
            bool unsubscribe, CancellationToken cancellationToken = default)
        {
            if (!unsubscribe && source.NodeId.NamespaceIndex == InstanceNamespaceIndex)
            {
                try
                {
                    _ = await AuthorizeAsync(context, false, cancellationToken).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    return ServiceResult.Create(exception.StatusCode, "The event subscription is not authorized.");
                }
            }
            return await base.SubscribeToEventsAsync(context, source, monitoredItem, unsubscribe, cancellationToken)
                .ConfigureAwait(false);
        }

        async ValueTask ITranslateBrowsePathAsyncNodeManager.TranslateBrowsePathAsync(
            OperationContext context, object sourceHandle, RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds, IList<NodeId> unresolvedTargetIds,
            CancellationToken cancellationToken)
        {
            bool projected = sourceHandle is NodeHandle handle && IsProjectionNode(handle.NodeId);
            if (projected)
            {
                EnsureProjectionAvailable();
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            long revision = Volatile.Read(ref m_projectionRevision);
            int targets = targetIds.Count;
            int unresolved = unresolvedTargetIds.Count;
            await TranslateBrowsePathAsync(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds,
                cancellationToken).ConfigureAwait(false);
            if (projected && (m_projectionPending || revision != Volatile.Read(ref m_projectionRevision)))
            {
                while (targetIds.Count > targets)
                {
                    targetIds.RemoveAt(targetIds.Count - 1);
                }
                while (unresolvedTargetIds.Count > unresolved)
                {
                    unresolvedTargetIds.RemoveAt(unresolvedTargetIds.Count - 1);
                }
                throw new ServiceResultException(StatusCodes.BadWaitingForInitialData,
                    "The native projection changed during browse-path translation.");
            }
        }

        private async ValueTask<IXRegistryPreparedOperation> PrepareProjectionAsync(
            IXRegistryPreparedOperation operation, CancellationToken ct)
        {
            if (operation is not IXRegistryPreparedSnapshot { HasCandidate: true } candidate)
            {
                return operation;
            }
            bool retained = false;
            try
            {
                XRegistryNativeSnapshot snapshot = await XRegistryNativeSnapshot.LoadAsync(
                    new XRegistryPreparedCandidateEndpoint(candidate), m_options, ct).ConfigureAwait(false);
                int bytes = MeasureSnapshot(snapshot);
                m_budget.ReserveBytes(bytes);
                retained = true;
                return new ProjectedPreparation(operation, snapshot, m_budget, bytes);
            }
            finally
            {
                if (!retained)
                {
                    await ReleasePreparedAfterOperationAsync(operation).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<XRegistryResponse> CommitProjectedAsync(
            ProjectedPreparation operation, CancellationToken ct)
        {
            await m_projectionGate.WaitAsync(ct).ConfigureAwait(false);
            XRegistryNativeSnapshot? previous = m_strategy?.Snapshot;
            bool commitStarted = false;
            XRegistryPreparedEventBatch? events = null;
            XRegistryResponse response;
            using LocalAddressSpaceNotificationBatch notifications =
                ((ILocalAddressSpaceNotifications)((ILocalAddressSpaceSource)this).CreateLocalAddressSpace())
                    .BeginNotificationBatch();
            m_projectionPending = true;
            Interlocked.Increment(ref m_projectionRevision);
            try
            {
                if (previous is null || m_projection is null || m_transportClosed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                await ApplySnapshotAsync(operation.Snapshot, ct).ConfigureAwait(false);
                string? correlationId = operation.Response.CorrelationId;
                events = PrepareNativeEvents(previous, operation.Snapshot, correlationId);
                ct.ThrowIfCancellationRequested();
                commitStarted = true;
                response = await operation.CommitAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    await ApplySnapshotAsync(previous, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    if (response.CorrelationId != correlationId)
                    {
                        events.Dispose();
                        events = null;
                        m_notificationDegraded = true;
                        Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().CommittedEventsFailed(
                            new InvalidDataException("The committed response changed its prepared correlation."));
                    }
                    PublishAddressSpaceNotifications(notifications);
                }
                m_projectionDegraded = false;
            }
            catch (Exception exception) when (exception is ServiceResultException or IOException
                or InvalidDataException or JsonException or OperationCanceledException or InvalidOperationException)
            {
                m_projectionDegraded = true;
                if (!commitStarted && previous is not null)
                {
                    try
                    {
                        await ApplySnapshotAsync(previous, CancellationToken.None).ConfigureAwait(false);
                        m_projectionDegraded = false;
                    }
                    catch (Exception recovery) when (recovery is ServiceResultException or IOException
                        or InvalidDataException or JsonException or OperationCanceledException
                        or InvalidOperationException)
                    {
                        Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().ProjectionRollbackFailed(recovery);
                    }
                }
                events?.Dispose();
                throw;
            }
            finally
            {
                if (!m_projectionDegraded)
                {
                    RetireUnpublishedFiles();
                }
                Interlocked.Increment(ref m_projectionRevision);
                m_projectionPending = false;
                m_projectionGate.Release();
            }
            if (response.IsSuccess && events is not null)
            {
                await PublishCommittedEventsAsync(events).ConfigureAwait(false);
            }
            else
            {
                events?.Dispose();
            }
            return response;
        }

        private async ValueTask PublishCommittedEventsAsync(XRegistryPreparedEventBatch events)
        {
            if (events.Count == 0)
            {
                events.Dispose();
                return;
            }
            try
            {
                await AwaitTransferCleanupAsync(async () =>
                {
                    using (events)
                    {
                        await InvalidateEventReceiverPermissionsAsync().ConfigureAwait(false);
                        await events.ReportAsync().ConfigureAwait(false);
                    }
                }, CancellationToken.None).ConfigureAwait(false);
                m_notificationDegraded = false;
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException or ArgumentException)
            {
                m_notificationDegraded = true;
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().CommittedEventsFailed(exception);
            }
        }

        private void PublishAddressSpaceNotifications(LocalAddressSpaceNotificationBatch notifications)
        {
            try
            {
                notifications.Publish();
            }
            catch (AggregateException exception)
            {
                m_notificationDegraded = true;
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().CommittedEventsFailed(exception);
            }
        }

        private XRegistryPreparedEventBatch PrepareNativeEvents(XRegistryNativeSnapshot previous,
            XRegistryNativeSnapshot current, string? correlationId = null)
        {
            m_eventEncodingDegraded = current.HasUnrepresentableEpochs;
            if (previous.HasUnrepresentableEpochs || current.HasUnrepresentableEpochs)
            {
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().UnrepresentableEventEpoch();
                XRegistryProjectionEventSnapshot unchanged = current.Events();
                return m_projection!.PrepareEvents(unchanged, unchanged);
            }
            return m_projection!.PrepareEvents(previous.Events(), current.Events(), correlationId);
        }

        private async ValueTask InvalidateEventReceiverPermissionsAsync()
        {
            await m_monitoredItemSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (MonitoredNode2 node in m_monitoredItemManager.MonitoredNodes.Values)
                {
                    foreach (IEventMonitoredItem item in node.EventMonitoredItems.Values)
                    {
                        if (item.Session is { Id.IsNull: false } session)
                        {
                            node.InvalidatePermissionCacheForSession(session.Id);
                        }
                    }
                }
            }
            finally
            {
                m_monitoredItemSemaphore.Release();
            }
        }

        private async ValueTask ApplySnapshotAsync(XRegistryNativeSnapshot snapshot, CancellationToken ct)
        {
            m_strategy!.Snapshot = snapshot;
            ConfigureRegistry(snapshot);
            await m_projection!.ReconcileProjectionAsync(ct).ConfigureAwait(false);
            await ReconcileMappedPropertiesAsync(snapshot, ct).ConfigureAwait(false);
            RebindFiles();
        }

        private bool IsProjectionNode(NodeId id)
        {
            return id.NamespaceIndex == InstanceNamespaceIndex &&
                id.TryGetValue(out string path) &&
                !path.StartsWith(m_options.RootIdentifier + "/transfer/", StringComparison.Ordinal) &&
                path != m_options.RootIdentifier + "/Bridge" &&
                !path.StartsWith(m_options.RootIdentifier + "/Bridge/", StringComparison.Ordinal);
        }

        private void EnsureProjectionAvailable()
        {
            if (m_projectionPending || m_transportClosed)
            {
                throw new ServiceResultException(StatusCodes.BadWaitingForInitialData,
                    "The native projection is not currently published.");
            }
        }

        private bool DeferProjectionFileDisposal()
        {
            return m_projectionPending && !m_projectionRetiring && !m_transportClosed;
        }

        private void RetireUnpublishedFiles()
        {
            m_projectionRetiring = true;
            try
            {
                foreach (KeyValuePair<NodeId, XRegistryNativeFile> entry in m_files.ToArray())
                {
                    if (!PredefinedNodes.ContainsKey(entry.Value.NodeId) &&
                        m_files.TryRemove(entry.Key, out XRegistryNativeFile? file))
                    {
                        file.Dispose();
                    }
                }
            }
            finally
            {
                m_projectionRetiring = false;
            }
        }

        private static int MeasureSnapshot(XRegistryNativeSnapshot snapshot)
        {
            long bytes = Encoding.UTF8.GetByteCount(snapshot.Root.GetRawText()) +
                Encoding.UTF8.GetByteCount(snapshot.Description.Model.GetRawText()) +
                Encoding.UTF8.GetByteCount(snapshot.Description.Capabilities.GetRawText());
            if (snapshot.ModelSource.ValueKind != JsonValueKind.Undefined)
            {
                bytes += Encoding.UTF8.GetByteCount(snapshot.ModelSource.GetRawText());
            }
            foreach (XRegistryNativeGroup group in snapshot.NativeGroups)
            {
                bytes += Encoding.UTF8.GetByteCount(group.Metadata.GetRawText());
                foreach (XRegistryNativeResource resource in group.NativeResources)
                {
                    bytes += Encoding.UTF8.GetByteCount(resource.Metadata.GetRawText()) +
                        Encoding.UTF8.GetByteCount(resource.Meta.GetRawText());
                }
            }
            return checked((int)bytes);
        }

        private sealed class ProjectedPreparation(
            IXRegistryPreparedOperation operation, XRegistryNativeSnapshot snapshot,
            XRegistryFileBudget budget, int bytes) : IXRegistryPreparedOperation
        {
            public XRegistryResponse Response => operation.Response;

            public XRegistryNativeSnapshot Snapshot => m_snapshot
                ?? throw new ObjectDisposedException(nameof(ProjectedPreparation));

            public ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
            {
                return operation.CommitAsync(cancellationToken);
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                try
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_snapshot = null;
                    budget.ReleaseBytes(bytes);
                }
            }

            private XRegistryNativeSnapshot? m_snapshot = snapshot;
            private int m_disposed;
        }

        private volatile bool m_projectionPending;
        private volatile bool m_projectionDegraded;
        private volatile bool m_notificationDegraded;
        private volatile bool m_eventEncodingDegraded;
        private bool m_projectionRetiring;
        private long m_projectionRevision;
    }

    internal static partial class XRegistryBridgeNodeManagerLog
    {
        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 5, Level = LogLevel.Error,
            Message = "The native projection could not restore its previous generation and requires repair.")]
        public static partial void ProjectionRollbackFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 6, Level = LogLevel.Error,
            Message =
                "Registry publication succeeded but native notification delivery failed; polling must repair it.")]
        public static partial void CommittedEventsFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 7, Level = LogLevel.Warning,
            Message =
                "A companion UInt32 epoch is unavailable or out of range; native events require full-read repair.")]
        public static partial void UnrepresentableEventEpoch(this ILogger logger);
    }
}
