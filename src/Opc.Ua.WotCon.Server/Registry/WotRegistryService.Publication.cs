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
using Opc.Ua.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    public sealed partial class WotRegistryService : IWotRegistryRecoveryResolver
    {
        /// <inheritdoc/>
        public bool SupportsPreparedPublication =>
            m_store is IWotRegistryPreparedStore { SupportsPreparedCommits: true };

        /// <inheritdoc/>
        public async ValueTask<bool> ResolveRecoveryAsync(CancellationToken cancellationToken = default)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!m_reloadRequired)
                {
                    return m_runtimeRecoveryRequired;
                }
                await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IWotRegistryRecoveryPublication> BeginRecoveryPublicationAsync(
            CancellationToken cancellationToken = default)
        {
            if (!SupportsPreparedPublication)
            {
                throw new NotSupportedException("The registry store cannot isolate recovery.");
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureReadableGeneration();
                if (m_recoverySynchronizationActive)
                {
                    throw new InvalidOperationException("A recovered projection is still being synchronized.");
                }
                return new RegistryPublicationInvocation(this, recoveryOnly: true);
            }
            catch
            {
                m_mutex.Release();
                throw;
            }
        }

        /// <inheritdoc/>
        public IDisposable RegisterRecoveryProjection(IWotRegistryRecoveryProjection projection)
        {
            _ = projection ?? throw new ArgumentNullException(nameof(projection));
            var registration = new RecoveryProjectionRegistration(this, projection);
            lock (m_recoveryProjectionGate)
            {
                m_recoveryProjections.Add(registration);
            }
            return registration;
        }

        private async ValueTask SynchronizeRecoveredProjectionsAsync(WotRegistrySnapshot snapshot)
        {
            RecoveryProjectionRegistration[] projections;
            lock (m_recoveryProjectionGate)
            {
                projections = [.. m_recoveryProjections];
            }
            foreach (RecoveryProjectionRegistration projection in projections)
            {
                await projection.SynchronizeAsync(snapshot).ConfigureAwait(false);
            }
        }

        private ArrayOf<INodeManagerReadImage> PrepareReadImages(
            WotRegistrySnapshot previous, WotRegistrySnapshot intended)
        {
            RecoveryProjectionRegistration[] projections;
            lock (m_recoveryProjectionGate)
            {
                projections = [.. m_recoveryProjections];
            }
            var images = new List<INodeManagerReadImage>();
            foreach (RecoveryProjectionRegistration projection in projections)
            {
                if (projection.PrepareReadImage(previous, intended) is { } image)
                {
                    images.Add(image);
                }
            }
            return [.. images];
        }

        /// <inheritdoc/>
        public async ValueTask<IWotRegistryPublication> BeginPublicationAsync(
            CancellationToken cancellationToken = default)
        {
            if (!SupportsPreparedPublication)
            {
                throw new NotSupportedException("The registry store cannot isolate publication.");
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                return new RegistryPublicationInvocation(this);
            }
            catch
            {
                m_mutex.Release();
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IWotPreparedRegistryPublication> PreparePublicationAsync(
            WotRegistrySnapshot expectedSnapshot,
            ArrayOf<WotResourceProjection> projections,
            uint refreshGeneration,
            ByteString canonicalViewGraphState = default,
            CancellationToken cancellationToken = default)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await PreparePublicationCoreAsync(
                    expectedSnapshot, projections, refreshGeneration, canonicalViewGraphState, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<IWotPreparedRegistryPublication> PreparePublicationCoreAsync(
            WotRegistrySnapshot expectedSnapshot,
            ArrayOf<WotResourceProjection> projections,
            uint refreshGeneration,
            ByteString canonicalViewGraphState,
            RegistryPublicationInvocation? invocation,
            CancellationToken cancellationToken,
            WotRegistryMutationImage? mutation = null)
        {
            _ = expectedSnapshot ?? throw new ArgumentNullException(nameof(expectedSnapshot));
            if (m_store is not IWotRegistryPreparedStore { SupportsPreparedCommits: true } store)
            {
                throw new NotSupportedException("The registry store cannot prepare isolated publication.");
            }
            EnsureMutationAllowed();
            if (!ReferenceEquals(m_snapshot, expectedSnapshot))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The captured registry changed.");
            }
            if (refreshGeneration < expectedSnapshot.RefreshGeneration)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Publication generation regressed.");
            }
            if (mutation is not null && !ReferenceEquals(mutation.Previous, expectedSnapshot))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The mutation input image changed.");
            }
            (WotRegistrySnapshot next, ArrayOf<string> changed) =
                BuildProjectionSnapshot(mutation?.Desired ?? expectedSnapshot, projections.ToList());
            if (mutation is not null)
            {
                changed = mutation.ChangedResourceXids.ToList().Concat(changed.ToList())
                    .Distinct(StringComparer.Ordinal).ToArrayOf();
            }
            next = next.WithPublicationState(
                checked(expectedSnapshot.Generation + 1), refreshGeneration, canonicalViewGraphState);
            using IWotRegistryValidatedGeneration captured = await store
                .CaptureValidatedGenerationAsync(cancellationToken).ConfigureAwait(false);
            IWotRegistryPreparedCommit? decision = await store.PrepareCommitAsync(
                next, captured, mutation is null
                    ? WotRegistryCommitScope.ProjectionMetadata : WotRegistryCommitScope.Full, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var publication = new PreparedRegistryPublication(
                    expectedSnapshot, next, PrepareReadImages(expectedSnapshot, next), changed, decision,
                    DecidePublicationAsync, PublishPublication, ReleasePublication, invocation, mutation is null);
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
                if (!projection.RetainPreviousActiveVersion && activeVersionId is not null &&
                    projection.LoadState == WoTLoadStateEnum.Active)
                {
                    updated = updated.WithCommittedVersion(updated.FindVersion(activeVersionId)
                        ?? throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Publication has no exact active Version."))
                        .WithCommittedInputs(projection.CommittedInputs);
                }
                WotResourceGroup group = next.FindGroup(projection.GroupId)!;
                next = ReplaceResource(next, group, updated, generation, bumpGroupEpoch: false);
                changed.Add(updated.Xid);
            }
            return (next, changed.ToArrayOf());
        }

        private async ValueTask<IWotPreparedRegistryRecovery> PrepareRecoveryCoreAsync(
            WotRegistrySnapshot expectedSnapshot,
            WotRegistrySnapshot runtimeSnapshot,
            RegistryPublicationInvocation invocation,
            CancellationToken cancellationToken)
        {
            _ = expectedSnapshot ?? throw new ArgumentNullException(nameof(expectedSnapshot));
            _ = runtimeSnapshot ?? throw new ArgumentNullException(nameof(runtimeSnapshot));
            EnsureReadableGeneration();
            if (!ReferenceEquals(m_snapshot, expectedSnapshot))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The recovery image changed.");
            }
            if (m_store is not IWotRegistryRecoveryStore { SupportsPreparedCommits: true } store)
            {
                throw new NotSupportedException("The registry store cannot retain authoritative recovery evidence.");
            }
            FileWotRegistryStore.ValidateRecoveryMetadata(expectedSnapshot, runtimeSnapshot);
            IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                FileWotRegistryStore.ValidateRecoveryMetadata(captured.Snapshot, expectedSnapshot);
                return new PreparedRegistryRecovery(
                    this, invocation, store, captured, expectedSnapshot, runtimeSnapshot,
                    PrepareReadImages(expectedSnapshot, runtimeSnapshot));
            }
            catch
            {
                captured.Dispose();
                throw;
            }
        }

        private async ValueTask DecidePublicationAsync(
            PreparedRegistryPublication publication,
            CancellationToken cancellationToken)
        {
            if (publication.Invocation is null)
            {
                await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                publication.Invocation.RequireActive(publication);
                cancellationToken.ThrowIfCancellationRequested();
            }
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
                    m_runtimeRecoveryRequired = true;
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
                    m_runtimeRecoveryRequired |= m_reloadRequired;
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
                publication.Changed.ToList(), publication.ProjectionOnly,
                publication.DurabilityWarning?.PersistenceFailure,
                materializationHandled: !publication.ProjectionOnly);
        }

        private void ReleasePublication(PreparedRegistryPublication publication)
        {
            if (publication.OwnsMutation)
            {
                publication.OwnsMutation = false;
                if (publication.Invocation is null)
                {
                    m_mutex.Release();
                }
            }
            publication.Invocation?.ReleaseUnit(publication);
        }

        private sealed class PreparedRegistryPublication(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot intended,
            ArrayOf<INodeManagerReadImage> readImages,
            ArrayOf<string> changed,
            IWotRegistryPreparedCommit decision,
            Func<PreparedRegistryPublication, CancellationToken, ValueTask> decide,
            Action<PreparedRegistryPublication> publish,
            Action<PreparedRegistryPublication> release,
            RegistryPublicationInvocation? invocation,
            bool projectionOnly) : IWotPreparedRegistryPublication
        {
            public WotRegistrySnapshot PreviousSnapshot { get; } = previous;
            public WotRegistrySnapshot IntendedSnapshot { get; } = intended;
            public ArrayOf<INodeManagerReadImage> ReadImages { get; } = readImages;
            public bool ProjectionOnly { get; } = projectionOnly;
            public ArrayOf<string> Changed { get; } = changed;
            public IWotRegistryPreparedCommit Decision { get; } = decision;
            public RegistryPublicationInvocation? Invocation { get; } = invocation;
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

        private sealed class PreparedRegistryRecovery(
            WotRegistryService owner,
            RegistryPublicationInvocation invocation,
            IWotRegistryRecoveryStore store,
            IWotRegistryValidatedGeneration captured,
            WotRegistrySnapshot expected,
            WotRegistrySnapshot runtime,
            ArrayOf<INodeManagerReadImage> readImages) : IWotPreparedRegistryRecovery
        {
            public WotRegistrySnapshot RuntimeSnapshot => runtime;
            public ArrayOf<INodeManagerReadImage> ReadImages => readImages;

            public async ValueTask ValidateAsync(CancellationToken cancellationToken = default)
            {
                if (Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The recovery validation has already been consumed.");
                }
                try
                {
                    invocation.RequireActive(this);
                    owner.EnsureReadableGeneration();
                    if (!ReferenceEquals(owner.Current, expected))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState, "The recovery image changed.");
                    }
                    m_validation = await store.ValidatePublicationAsync(captured, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref m_state, 2);
                    m_finished.TrySetResult(true);
                }
            }

            public void Publish()
            {
                invocation.RequireActive(this);
                lock (m_completionGate)
                {
                    if (Volatile.Read(ref m_state) != 2 || m_validation is null || m_published || m_disposed != 0)
                    {
                        throw new InvalidOperationException("The recovery has no unpublished validated image.");
                    }
                    Volatile.Write(ref owner.m_snapshot, runtime);
                    owner.m_runtimeRecoveryRequired = true;
                    m_published = true;
                }
            }

            public async ValueTask CompleteAsync()
            {
                invocation.RequireActive(this);
                lock (m_completionGate)
                {
                    if (!m_published || m_validation is null || m_completionStarted != 0 || m_disposed != 0)
                    {
                        throw new InvalidOperationException("The recovery has no pending published image to complete.");
                    }
                    m_completionStarted = 1;
                }
                try
                {
                    await invocation.SynchronizeRecoveryAsync(this, runtime).ConfigureAwait(false);
                    if (!ReferenceEquals(owner.Current, runtime))
                    {
                        throw new InvalidOperationException("The registry image changed during recovered synchronization.");
                    }
                    Interlocked.Exchange(ref m_validation, null)?.Dispose();
                    owner.m_runtimeRecoveryRequired = false;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    throw new WotRegistryCommitDurabilityUncertainException(runtime, failure);
                }
                finally
                {
                    try
                    {
                        Interlocked.Exchange(ref m_validation, null)?.Dispose();
                    }
                    finally
                    {
                        invocation.ReleaseUnit(this);
                        m_completed.TrySetResult(true);
                    }
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref m_state, 3, 0) == 1)
                {
                    await m_finished.Task.ConfigureAwait(false);
                }
                Task? completing = null;
                bool incomplete = false;
                bool ownsDisposal;
                lock (m_completionGate)
                {
                    ownsDisposal = m_disposed == 0;
                    if (ownsDisposal)
                    {
                        m_disposed = 1;
                        completing = m_completionStarted != 0 ? m_completed.Task : null;
                        incomplete = m_published && completing is null;
                    }
                }
                if (!ownsDisposal)
                {
                    await m_disposeCompleted.Task.ConfigureAwait(false);
                    return;
                }
                try
                {
                    if (completing is not null)
                    {
                        await completing.ConfigureAwait(false);
                    }
                    try
                    {
                        Interlocked.Exchange(ref m_validation, null)?.Dispose();
                    }
                    finally
                    {
                        captured.Dispose();
                        invocation.ReleaseUnit(this);
                    }
                    if (incomplete)
                    {
                        throw new WotRegistryCommitDurabilityUncertainException(runtime,
                            new InvalidOperationException("Recovered metadata was disposed before projection completion."));
                    }
                }
                finally
                {
                    m_disposeCompleted.TrySetResult(true);
                }
            }

            private readonly Lock m_completionGate = new();
            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_completed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_disposeCompleted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private IWotRegistryPublicationValidation? m_validation;
            private int m_state;
            private int m_disposed;
            private int m_completionStarted;
            private bool m_published;
        }

        private sealed class RecoveryProjectionRegistration(
            WotRegistryService owner, IWotRegistryRecoveryProjection projection) : IDisposable
        {
            public INodeManagerReadImage? PrepareReadImage(
                WotRegistrySnapshot previous, WotRegistrySnapshot intended)
            {
                return Volatile.Read(ref m_projection) is IWotRegistryReadImageProjection current
                    ? current.PrepareReadImage(previous, intended)
                    : null;
            }

            public ValueTask SynchronizeAsync(WotRegistrySnapshot snapshot)
            {
                IWotRegistryRecoveryProjection? current = Volatile.Read(ref m_projection);
                return current is null ? default : current.SynchronizeAsync(snapshot, CancellationToken.None);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_projection, null) is not null)
                {
                    lock (owner.m_recoveryProjectionGate)
                    {
                        owner.m_recoveryProjections.Remove(this);
                    }
                }
            }

            private IWotRegistryRecoveryProjection? m_projection = projection;
        }

        private sealed class RegistryPublicationInvocation(WotRegistryService owner, bool recoveryOnly = false)
            : IWotRegistryRecoveryPublication, IWotRegistryMutationPublication
        {
            public WotRegistrySnapshot Current => owner.Current;

            public async ValueTask<IWotPreparedRegistryPublication> PrepareAsync(
                WotRegistrySnapshot expectedSnapshot,
                ArrayOf<WotResourceProjection> projections,
                uint refreshGeneration,
                ByteString canonicalViewGraphState = default,
                CancellationToken cancellationToken = default)
            {
                if (recoveryOnly)
                {
                    throw new InvalidOperationException("A recovery invocation cannot decide a new publication.");
                }
                BeginPreparation();
                try
                {
                    IWotPreparedRegistryPublication publication = await owner.PreparePublicationCoreAsync(
                        expectedSnapshot, projections, refreshGeneration, canonicalViewGraphState, this, cancellationToken)
                        .ConfigureAwait(false);
                    RegisterPublication(publication);
                    return publication;
                }
                finally
                {
                    FinishPreparation();
                }
            }

            public async ValueTask<IWotPreparedRegistryRecovery> PrepareRecoveryAsync(
                WotRegistrySnapshot expectedSnapshot,
                WotRegistrySnapshot runtimeSnapshot,
                CancellationToken cancellationToken = default)
            {
                BeginPreparation();
                try
                {
                    IWotPreparedRegistryRecovery recovery = await owner.PrepareRecoveryCoreAsync(
                        expectedSnapshot, runtimeSnapshot, this, cancellationToken).ConfigureAwait(false);
                    RegisterPublication(recovery);
                    return recovery;
                }
                finally
                {
                    FinishPreparation();
                }
            }

            public async ValueTask<IWotPreparedRegistryPublication> PrepareMutationAsync(
                WotRegistryMutationImage mutation,
                ArrayOf<WotResourceProjection> projections,
                uint refreshGeneration,
                ByteString canonicalViewGraphState = default,
                CancellationToken cancellationToken = default)
            {
                _ = mutation ?? throw new ArgumentNullException(nameof(mutation));
                if (recoveryOnly)
                {
                    throw new InvalidOperationException("A recovery invocation cannot decide a lifecycle mutation.");
                }
                BeginPreparation();
                try
                {
                    IWotPreparedRegistryPublication publication = await owner.PreparePublicationCoreAsync(
                        mutation.Previous, projections, refreshGeneration, canonicalViewGraphState,
                        this, cancellationToken, mutation).ConfigureAwait(false);
                    RegisterPublication(publication);
                    return publication;
                }
                finally
                {
                    FinishPreparation();
                }
            }

            public void RequireActive(IAsyncDisposable publication)
            {
                lock (m_lifetime)
                {
                    if (m_closing || !ReferenceEquals(m_unit, publication))
                    {
                        throw new InvalidOperationException("The registry invocation no longer owns this unit.");
                    }
                }
            }

            public void ReleaseUnit(IAsyncDisposable publication)
            {
                lock (m_lifetime)
                {
                    if (ReferenceEquals(m_unit, publication))
                    {
                        m_unit = null;
                    }
                }
            }

            public async ValueTask SynchronizeRecoveryAsync(
                PreparedRegistryRecovery publication, WotRegistrySnapshot snapshot)
            {
                RequireActive(publication);
                owner.m_recoverySynchronizationActive = true;
                owner.m_mutex.Release();
                try
                {
                    await owner.SynchronizeRecoveredProjectionsAsync(snapshot).ConfigureAwait(false);
                }
                finally
                {
                    await owner.m_mutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    owner.m_recoverySynchronizationActive = false;
                }
            }

            public async ValueTask DisposeAsync()
            {
                Task? prepared;
                bool ownsDisposal;
                lock (m_lifetime)
                {
                    ownsDisposal = !m_closing;
                    m_closing = true;
                    prepared = m_preparing ? m_prepared.Task : null;
                }
                if (!ownsDisposal)
                {
                    await m_finished.Task.ConfigureAwait(false);
                    return;
                }
                try
                {
                    if (prepared is not null)
                    {
                        await prepared.ConfigureAwait(false);
                    }
                    var failures = new List<Exception>();
                    foreach (IAsyncDisposable publication in m_publications)
                    {
                        try
                        {
                            await publication.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            failures.Add(failure);
                        }
                    }
                    if (failures.Count != 0)
                    {
                        throw new AggregateException("Registry invocation cleanup failed.", failures);
                    }
                }
                finally
                {
                    owner.m_mutex.Release();
                    m_finished.TrySetResult(true);
                }
            }

            private void BeginPreparation()
            {
                lock (m_lifetime)
                {
                    if (m_closing || m_preparing || m_unit is not null)
                    {
                        throw new InvalidOperationException("The registry invocation is closed or has an unfinished unit.");
                    }
                    m_preparing = true;
                    m_prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            private void RegisterPublication(IAsyncDisposable publication)
            {
                lock (m_lifetime)
                {
                    m_unit = publication;
                    m_publications.Add(publication);
                }
            }

            private void FinishPreparation()
            {
                lock (m_lifetime)
                {
                    m_preparing = false;
                    m_prepared.TrySetResult(true);
                }
            }

            private readonly Lock m_lifetime = new();
            private readonly List<IAsyncDisposable> m_publications = [];
            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TaskCompletionSource<bool> m_prepared =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private IAsyncDisposable? m_unit;
            private bool m_preparing;
            private bool m_closing;
        }

        private readonly Lock m_recoveryProjectionGate = new();
        private readonly List<RecoveryProjectionRegistration> m_recoveryProjections = [];
        private bool m_recoverySynchronizationActive;
    }
}
