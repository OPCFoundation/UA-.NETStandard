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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Optional file capability for a write reservation whose initial staged bytes are the
    /// committed Version, with position zero and no implicit erase or append.
    /// </summary>
    public interface IXRegistryProjectedPreservingResourceFile
    {
        /// <summary>
        /// Reserves and initializes a session-owned write handle without changing committed content.
        /// </summary>
        ValueTask<(ServiceResult Status, uint FileHandle)> OpenPreservingWriteAsync(
            ISystemContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Optional preserving-write preparation using provider-owned state carried by the resource.
    /// This lets an atomic owner transfer its reservation without reacquiring the owner operation.
    /// </summary>
    public interface IXRegistryPreparedResourceFile : IXRegistryProjectedPreservingResourceFile
    {
        /// <summary>
        /// Prepares a preserving writer against the supplied candidate or existing resource.
        /// </summary>
        ValueTask<(ServiceResult Status, uint FileHandle)> OpenPreservingWriteAsync(
            IXRegistryProjectionResource resource,
            ISystemContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// An unpublished or existing exact-Version file reservation owned by the projection engine.
    /// Disposal abandons the clean reservation unless the caller transfers it after durable commit.
    /// </summary>
    public sealed class XRegistryResourceFileReservation : IAsyncDisposable
    {
        internal XRegistryResourceFileReservation(
            uint fileHandle,
            Func<ValueTask> release,
            Action<XRegistryResourceFileReservation> untrack)
        {
            FileHandle = fileHandle;
            m_release = release;
            m_untrack = untrack;
            SessionClosedToken = m_sessionClosed.Token;
        }

        /// <summary>
        /// Gets the session-owned exact-Version write handle.
        /// </summary>
        public uint FileHandle { get; }

        /// <summary>
        /// Gets a token cancelled when Session discard invalidates the prepared handle.
        /// Link it to the creation transaction before entering its store commit.
        /// </summary>
        public CancellationToken SessionClosedToken { get; }

        /// <summary>
        /// Transfers the reserved handle after commit and projection reconciliation.
        /// </summary>
        public void Complete()
        {
            End(complete: true);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Func<ValueTask>? release = End(complete: false);
            return release is null ? default : release();
        }

        internal void InvalidateSession()
        {
            lock (m_lifetimeGate)
            {
                if (m_release is not null)
                {
                    m_sessionClosed.Cancel();
                }
            }
        }

        private Func<ValueTask>? End(bool complete)
        {
            lock (m_lifetimeGate)
            {
                Func<ValueTask>? release = m_release;
                if (release is null)
                {
                    return null;
                }
                m_release = null;
                m_untrack(this);
                m_sessionClosed.Dispose();
                return complete ? null : release;
            }
        }

        private readonly Lock m_lifetimeGate = new();
        private readonly CancellationTokenSource m_sessionClosed = new();
        private readonly Action<XRegistryResourceFileReservation> m_untrack;
        private Func<ValueTask>? m_release;
    }

    public sealed partial class XRegistryProjectionEngine
    {
        /// <summary>
        /// Prepares a real exact-Version file and write handle before structural persistence.
        /// Reconcile the previously committed generation first. New entries are not registered
        /// until normal reconciliation adopts them after the provider's durable commit.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<XRegistryResourceFileReservation> ReserveResourceWriteAsync(
            IXRegistryProjectionResource resource,
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (resource is null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            ServiceResult access = m_context.CheckManagementAccess(context, "OpenWrite");
            if (ServiceResult.IsBad(access))
            {
                throw new ServiceResultException(access);
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsSessionClosing(context))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }
                if (m_versionedStrategy is null ||
                    !m_groups.TryGetValue(resource.GroupId, out GroupEntry? group))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The owning group is not projected.");
                }
                NodeId nodeId = VersionNodeId(resource.GroupId, resource.ResourceId, resource.VersionId);
                ResourceEntry? entry = null;
                if (group.LogicalResources.TryGetValue(resource.ResourceId, out LogicalResourceEntry? logical))
                {
                    logical.Versions.TryGetValue(resource.VersionId, out entry);
                }
                bool preparedHere = false;
                if (entry is null && !m_preparedVersions.TryGetValue(nodeId, out entry))
                {
                    entry = CreateVersionEntry(group.Node, resource, eventSnapshot: null);
                    m_preparedVersions.Add(nodeId, entry);
                    preparedHere = true;
                }
                try
                {
                    if (entry.File is not IXRegistryProjectedPreservingResourceFile file)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The file provider cannot prepare a preserving write.");
                    }
                    (ServiceResult status, uint handle) = file is IXRegistryPreparedResourceFile prepared
                        ? await prepared.OpenPreservingWriteAsync(resource, context, cancellationToken)
                            .ConfigureAwait(false)
                        : await file.OpenPreservingWriteAsync(context, cancellationToken).ConfigureAwait(false);
                    if (ServiceResult.IsBad(status))
                    {
                        throw new ServiceResultException(status);
                    }
                    if (handle == 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError, "The file provider returned an invalid zero handle.");
                    }
                    ResourceEntry reserved = entry;
                    var reservation = new XRegistryResourceFileReservation(
                        handle,
                        () => ReleasePreparedResourceAsync(reserved, context, handle),
                        completed => m_resourceReservations.TryRemove(completed, out _));
                    m_resourceReservations.TryAdd(reservation, SessionIdOf(context));
                    return reservation;
                }
                catch
                {
                    if (preparedHere)
                    {
                        m_preparedVersions.Remove(nodeId);
                        entry.File?.Dispose();
                    }
                    throw;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        private async ValueTask ReleasePreparedResourceAsync(ResourceEntry entry, ISystemContext context, uint handle)
        {
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_preparedVersions.TryGetValue(entry.Node.NodeId, out ResourceEntry? pending) &&
                    ReferenceEquals(entry, pending))
                {
                    m_preparedVersions.Remove(entry.Node.NodeId);
                    entry.File?.Dispose();
                }
                else if (entry.File is IXRegistryProjectedResourceFileHandleForwarder file)
                {
                    ServiceResult status = await file.ForwardCloseAsync(
                        context, entry.Node.Close!, entry.Node.NodeId, handle, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(status) && status.StatusCode != StatusCodes.BadInvalidArgument)
                    {
                        throw new ServiceResultException(status);
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        private readonly Dictionary<NodeId, ResourceEntry> m_preparedVersions = [];
        private readonly ConcurrentDictionary<XRegistryResourceFileReservation, NodeId> m_resourceReservations = new();
    }
}
