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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    public sealed partial class WotRegistryService
    {
        /// <inheritdoc/>
        public bool SupportsPreparedPublication =>
            m_store is IWotRegistryPreparedStore { SupportsPreparedCommits: true };

        /// <inheritdoc/>
        public async ValueTask<IWotPreparedRegistryPublication> PreparePublicationAsync(
            WotRegistrySnapshot expectedSnapshot,
            ArrayOf<WotResourceProjection> projections,
            uint refreshGeneration,
            ByteString canonicalViewGraphState = default,
            CancellationToken cancellationToken = default)
        {
            _ = expectedSnapshot ?? throw new ArgumentNullException(nameof(expectedSnapshot));
            if (m_store is not IWotRegistryPreparedStore { SupportsPreparedCommits: true } store)
            {
                throw new NotSupportedException("The registry store cannot prepare isolated publication.");
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                if (!ReferenceEquals(m_snapshot, expectedSnapshot))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The captured registry changed.");
                }
                if (refreshGeneration < expectedSnapshot.RefreshGeneration)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "Publication generation regressed.");
                }
                (WotRegistrySnapshot next, ArrayOf<string> changed) =
                    BuildProjectionSnapshot(expectedSnapshot, projections.ToList());
                next = next.WithPublicationState(
                    checked(expectedSnapshot.Generation + 1), refreshGeneration, canonicalViewGraphState);
                using IWotRegistryValidatedGeneration captured = await store
                    .CaptureValidatedGenerationAsync(cancellationToken).ConfigureAwait(false);
                IWotRegistryPreparedCommit? decision = await store.PrepareCommitAsync(
                    next, captured, WotRegistryCommitScope.ProjectionMetadata, cancellationToken).ConfigureAwait(false);
                try
                {
                    var publication = new PreparedRegistryPublication(
                        expectedSnapshot, next, changed, decision,
                        DecidePublicationAsync, PublishPublication, ReleasePublication);
                    decision = null;
                    return publication;
                }
                finally
                {
                    if (decision is not null)
                    {
                        await decision.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private static (WotRegistrySnapshot Snapshot, ArrayOf<string> Changed) BuildProjectionSnapshot(
            WotRegistrySnapshot snapshot,
            IEnumerable<WotResourceProjection> projections)
        {
            long generation = checked(snapshot.Generation + 1);
            var changed = new List<string>();
            WotRegistrySnapshot next = snapshot;
            foreach (WotResourceProjection projection in projections)
            {
                WotResource? resource = next.FindResource(projection.GroupId, projection.ResourceId);
                if (resource is null)
                {
                    continue;
                }
                string? activeVersionId = projection.RetainPreviousActiveVersion
                    ? resource.ActiveVersionId
                    : projection.ActiveVersionId;
                string? validationVersionId = projection.VersionId ?? resource.DefaultVersionId;
                ImmutableArray<WotResourceVersion> versions = resource.Versions;
                WotResourceVersion? validationVersion = resource.FindVersion(validationVersionId);
                if (validationVersion is not null)
                {
                    versions = versions.SetItem(
                        versions.IndexOf(validationVersion),
                        validationVersion.With(
                            validation: projection.Validation,
                            clearValidation: projection.Validation is null,
                            dependencySnapshot: projection.DependencySnapshot,
                            lastDependencyAttempt: projection.LastDependencyAttempt));
                }
                WotResource updated = resource.With(
                    versions: versions,
                    activeVersionId: activeVersionId,
                    loadState: projection.LoadState,
                    validation: projection.Validation,
                    diagnostics: projection.Diagnostics,
                    epoch: resource.Epoch,
                    refreshGeneration: projection.RefreshGeneration,
                    lastRefreshTime: projection.LastRefreshTime,
                    materializedNodeCount: projection.MaterializedNodeCount,
                    rootNodeId: projection.RootNodeId,
                    clearActiveVersion: activeVersionId is null,
                    clearValidation: projection.Validation is null,
                    clearRootNodeId: projection.RootNodeId.IsNull);
                WotResourceGroup group = next.FindGroup(projection.GroupId)!;
                next = ReplaceResource(next, group, updated, generation, bumpGroupEpoch: false);
                changed.Add(updated.Xid);
            }
            return (next, changed.ToArrayOf());
        }

        private async ValueTask DecidePublicationAsync(
            PreparedRegistryPublication publication,
            CancellationToken cancellationToken)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            publication.OwnsMutation = true;
            try
            {
                EnsureMutationAllowed();
                if (!ReferenceEquals(m_snapshot, publication.PreviousSnapshot))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The captured registry changed.");
                }
                try
                {
                    await publication.Decision.CommitAsync(cancellationToken).ConfigureAwait(false);
                    publication.IsCommitted = true;
                }
                catch (WotRegistryCommitDurabilityUncertainException warning)
                {
                    publication.IsCommitted = true;
                    publication.DurabilityWarning = warning;
                }
                catch (WotRegistryCommitIndeterminateException)
                {
                    m_recoverySnapshot = publication.IntendedSnapshot;
                    m_reloadRequired = true;
                    throw;
                }
                try
                {
                    await RefreshValidatedStoreGenerationAfterCommitAsync(
                        publication.IntendedSnapshot, publication.DurabilityWarning?.PersistenceFailure)
                        .ConfigureAwait(false);
                }
                catch (WotRegistryCommitDurabilityUncertainException warning) when (publication.IsCommitted)
                {
                    publication.DurabilityWarning = warning;
                }
            }
            finally
            {
                if (!publication.IsCommitted)
                {
                    ReleasePublication(publication);
                }
            }
        }

        private void PublishPublication(PreparedRegistryPublication publication)
        {
            if (!publication.IsCommitted || !publication.OwnsMutation)
            {
                throw new InvalidOperationException("The registry publication has no committed decision to publish.");
            }
            Volatile.Write(ref m_snapshot, publication.IntendedSnapshot);
            publication.IsPublished = true;
            ReleasePublication(publication);
            RaiseChanged(
                publication.PreviousSnapshot, publication.IntendedSnapshot,
                publication.Changed.ToList(), projectionOnly: true,
                publication.DurabilityWarning?.PersistenceFailure);
        }

        private void ReleasePublication(PreparedRegistryPublication publication)
        {
            if (publication.OwnsMutation)
            {
                publication.OwnsMutation = false;
                m_mutex.Release();
            }
        }

        private sealed class PreparedRegistryPublication(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot intended,
            ArrayOf<string> changed,
            IWotRegistryPreparedCommit decision,
            Func<PreparedRegistryPublication, CancellationToken, ValueTask> decide,
            Action<PreparedRegistryPublication> publish,
            Action<PreparedRegistryPublication> release) : IWotPreparedRegistryPublication
        {
            public WotRegistrySnapshot PreviousSnapshot { get; } = previous;
            public WotRegistrySnapshot IntendedSnapshot { get; } = intended;
            public ArrayOf<string> Changed { get; } = changed;
            public IWotRegistryPreparedCommit Decision { get; } = decision;
            public bool IsCommitted { get; set; }
            public bool IsPublished { get; set; }
            public bool OwnsMutation { get; set; }
            public WotRegistryCommitDurabilityUncertainException? DurabilityWarning { get; set; }

            public async ValueTask DecideAsync(CancellationToken cancellationToken = default)
            {
                if (Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The registry publication has already been consumed.");
                }
                try
                {
                    await decide(this, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref m_state, 2);
                    m_finished.TrySetResult(true);
                }
            }

            public void Publish()
            {
                if (IsPublished)
                {
                    throw new InvalidOperationException("The registry publication is already visible.");
                }
                publish(this);
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref m_state, 3, 0) == 1)
                {
                    await m_finished.Task.ConfigureAwait(false);
                }
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    try
                    {
                        if (IsCommitted && !IsPublished)
                        {
                            publish(this);
                            throw new WotRegistryCommitDurabilityUncertainException(
                                IntendedSnapshot,
                                new InvalidOperationException(
                                    "A committed publication was disposed before publication."));
                        }
                    }
                    finally
                    {
                        release(this);
                        await Decision.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_state;
            private int m_disposed;
        }
    }
}
