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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Strong active/passive historian backed by immutable protected segments.
    /// </summary>
    /// <remarks>
    /// Each update writes immutable bounded segments and publishes them with one
    /// compare/exchange of the protected manifest. Read tokens pin an immutable
    /// manifest generation, so a page boundary is stable while concurrent
    /// writers publish later generations.
    /// </remarks>
    public sealed class SharedKeyValueHistorianProvider :
        HistorianProviderBase,
        IHistorianDataProvider,
        IHistorianModifiedProvider,
        IHistorianAtTimeProvider,
        IHistorianProcessedProvider,
        IHistorianAnnotationProvider,
        IHistorianEventProvider,
        IHistorianStructuredDataProvider,
        IHistorianBulkInsertProvider,
        IHistorianTransactionalProvider,
        IHistorianProviderIdentity,
        IAsyncDisposable
    {
        /// <summary>
        /// Creates an initialized provider for direct construction.
        /// </summary>
        public SharedKeyValueHistorianProvider(
            ISharedKeyValueStore store,
            IServiceMessageContext messageContext,
            IRecordProtector protector,
            ILeaderElection election,
            SharedKeyValueHistorianOptions? options = null,
            TimeProvider? timeProvider = null,
            IHistorianFencingAuthority? fencingAuthority = null)
            : this(
                store,
                protector,
                election,
                options,
                timeProvider,
                fencingAuthority)
        {
            Initialize(messageContext);
        }

        internal SharedKeyValueHistorianProvider(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            ILeaderElection election,
            SharedKeyValueHistorianOptions? options = null,
            TimeProvider? timeProvider = null,
            IHistorianFencingAuthority? fencingAuthority = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (store is not ISharedKeyValueStoreConsistency consistency ||
                !consistency.IsLinearizable(kPrefix) ||
                consistency.IsProcessLocal(kPrefix))
            {
                throw new InvalidOperationException(
                    "The distributed historian requires a cross-process, key-level linearizable shared store.");
            }
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            if (protector is NullRecordProtector)
            {
                throw new InvalidOperationException(
                    "The distributed historian requires authenticated record protection.");
            }
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_options = options ?? new SharedKeyValueHistorianOptions();
            m_options.Validate();
            foreach (SharedKeyValueStructuredHistorianNode node in
                m_options.StructuredNodes)
            {
                m_structuredKeySelectors[node.NodeId] = node.KeySelector;
            }
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_fencingAuthority = fencingAuthority ??
                new SharedKeyValueHistorianFencingAuthority(
                    m_store,
                    m_protector,
                    m_election,
                    m_options.WriterFenceLeaseDuration,
                    m_timeProvider);
            m_election.LeadershipChanged += OnLeadershipChanged;
            m_cleanupChannel = Channel.CreateUnbounded<CleanupItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            m_cleanupTask = Task.Run(() => DrainCleanupAsync(m_disposeCts.Token));
        }

        /// <inheritdoc/>
        public string ProviderId => m_options.ProviderId;

        /// <summary>
        /// Last permanent background garbage-collection failure, if any.
        /// </summary>
        public Exception? GarbageCollectionFailure
        {
            get
            {
                lock (m_cleanupFailureLock)
                {
                    return m_cleanupFailure;
                }
            }
        }

        /// <inheritdoc/>
        public override ValueTask<bool> IsHistorizingAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            return new(!nodeId.IsNull);
        }

        /// <inheritdoc/>
        public override ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            bool structured = m_structuredKeySelectors.ContainsKey(nodeId);
            return new(m_options.Capabilities with
            {
                MaxReturnDataValues = m_options.MaxValuesPerPage,
                MaxReturnEventValues = m_options.MaxValuesPerPage,
                PortableResumeTokens = true,
                ReadStructuredData = structured,
                ReadModifiedStructuredData = structured,
                ReadAtTimeStructuredData = structured,
                InsertStructuredData = structured,
                ReplaceStructuredData = structured,
                UpdateStructuredData = structured,
                DeleteStructuredData = structured
            });
        }

        /// <inheritdoc/>
        public ValueTask<IHistorianStructuredDataKeySelector> GetKeySelectorAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            return new(m_structuredKeySelectors.TryGetValue(
                nodeId,
                out IHistorianStructuredDataKeySelector? selector)
                ? selector
                : TimestampStructuredDataKeySelector.Instance);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>>
            InsertStructuredDataAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
        {
            return ApplyStructuredUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Insert,
                remove: false,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>>
            ReplaceStructuredDataAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
        {
            return ApplyStructuredUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Replace,
                remove: false,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>>
            UpdateStructuredDataAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
        {
            return ApplyStructuredUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Update,
                remove: false,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>>
            RemoveStructuredDataAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
        {
            return ApplyStructuredUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Delete,
                remove: true,
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
            HistorianOperationContext context,
            HistorianRawReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ArchiveSnapshot snapshot = await LoadSnapshotForReadAsync(
                resumeToken,
                ReadKind.Raw,
                ct).ConfigureAwait(false);
            List<HistoricalDataValue> values = BuildRawValues(
                snapshot.State,
                request);
            HistorianPage<HistoricalDataValue> page = CreatePage(
                values,
                snapshot.Manifest.Generation,
                ReadKind.Raw,
                resumeToken,
                request.MaxValues);
            await PinPageGenerationAsync(
                page,
                snapshot.Manifest.Generation,
                ct).ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> InsertAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return ApplyDataUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Insert,
                transactional: false,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return ApplyDataUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Replace,
                transactional: false,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> UpdateAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return ApplyDataUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Update,
                transactional: false,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> InsertAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return ApplyDataUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Insert,
                transactional: true,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return ApplyDataUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Replace,
                transactional: true,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> UpdateAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return ApplyDataUpdateAsync(
                context,
                nodeId,
                values,
                HistoryUpdateType.Update,
                transactional: true,
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask<
            ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
            HistorianOperationContext context,
            ArrayOf<HistorianDataBatch> batch,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (batch.IsNull)
            {
                throw new ArgumentNullException(nameof(batch));
            }
            ThrowIfGarbageCollectionFailed();
            for (int attempt = 0; attempt < kMaxPublishAttempts; attempt++)
            {
                if (!await EnsureWriterFenceAsync(ct).ConfigureAwait(false))
                {
                    return RejectBatch(batch);
                }
                StoredManifest stored = await LoadCurrentManifestAsync(ct)
                    .ConfigureAwait(false);
                if (!IsCurrentWriter(stored.Manifest))
                {
                    return RejectBatch(batch);
                }
                ArchiveState state = await LoadStateAsync(stored.Manifest, ct)
                    .ConfigureAwait(false);
                var mutations = new List<Mutation>();
                var outcomes =
                    new HistorianUpdateOutcome<DataValue>[batch.Count];
                for (int i = 0; i < batch.Count; i++)
                {
                    HistorianDataBatch entry = batch[i];
                    ArrayOf<DataValue> values = entry.Values.IsNull
                        ? []
                        : entry.Values;
                    UpdatePlan<DataValue> plan = PlanDataUpdate(
                        state,
                        entry.NodeId,
                        values,
                        HistoryUpdateType.Insert,
                        context.DefaultModificationInfo,
                        transactional: false,
                        stored.Manifest.NextSequence);
                    stored.Manifest.NextSequence = plan.NextSequence;
                    mutations.AddRange(plan.Mutations);
                    outcomes[i] = plan.Outcome;
                }
                if (mutations.Count == 0)
                {
                    return outcomes;
                }
                if (!m_election.IsLeader)
                {
                    return RejectBatch(batch);
                }
                if (await TryPublishAsync(
                    stored,
                    state,
                    mutations,
                    ct).ConfigureAwait(false))
                {
                    return outcomes;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadTransactionFailed,
                "The historian manifest changed too frequently to publish the batch.");
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> DeleteRawAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            bool isDeleteModified,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanRangeDelete(
                    state,
                    nodeId,
                    startTime,
                    endTime,
                    isDeleteModified,
                    context.DefaultModificationInfo,
                    nextSequence),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> DeleteAtTimeAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DateTimeUtc> timestamps,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (timestamps.IsNull)
            {
                throw new ArgumentNullException(nameof(timestamps));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanPointDelete(
                    state,
                    nodeId,
                    timestamps,
                    context.DefaultModificationInfo,
                    nextSequence),
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask<HistorianPage<ModifiedDataValue>> ReadModifiedAsync(
            HistorianOperationContext context,
            HistorianModifiedReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ArchiveSnapshot snapshot = await LoadSnapshotForReadAsync(
                resumeToken,
                ReadKind.Modified,
                ct).ConfigureAwait(false);
            List<ModifiedDataValue> values = BuildModifiedValues(
                snapshot.State,
                request);
            HistorianPage<ModifiedDataValue> page = CreatePage(
                values,
                snapshot.Manifest.Generation,
                ReadKind.Modified,
                resumeToken,
                request.MaxValues);
            await PinPageGenerationAsync(
                page,
                snapshot.Manifest.Generation,
                ct).ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DataValue>> ReadAtTimeAsync(
            HistorianOperationContext context,
            HistorianAtTimeReadRequest request,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            StoredManifest stored = await LoadCurrentManifestAsync(ct)
                .ConfigureAwait(false);
            ArchiveState state = await LoadStateAsync(stored.Manifest, ct)
                .ConfigureAwait(false);
            NodeArchive? archive = state.TryGetArchive(request.NodeId);
            var result = new List<DataValue>(request.RequestedTimes.Count);
            foreach (DateTimeUtc requestedTime in request.RequestedTimes)
            {
                result.Add(InterpolateAtTime(
                    archive?.Raw.Values,
                    requestedTime,
                    request.UseSimpleBounds));
            }
            return result.ToArrayOf();
        }

        /// <inheritdoc/>
        public async ValueTask<HistorianPage<DataValue>> ReadProcessedAsync(
            HistorianOperationContext context,
            HistorianProcessedReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ArchiveSnapshot snapshot = await LoadSnapshotForReadAsync(
                resumeToken,
                ReadKind.Processed,
                ct).ConfigureAwait(false);
            List<DataValue> values = request.AggregateId ==
                ObjectIds.AggregateFunction_AnnotationCount
                ? BuildAnnotationCounts(snapshot.State, request, ct)
                : BuildProcessedValues(context, snapshot.State, request);
            HistorianPage<DataValue> page = CreatePage(
                values,
                snapshot.Manifest.Generation,
                ReadKind.Processed,
                resumeToken,
                request.MaxValues);
            await PinPageGenerationAsync(
                page,
                snapshot.Manifest.Generation,
                ct).ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public async ValueTask<HistorianPage<Annotation>> ReadAnnotationsAsync(
            HistorianOperationContext context,
            HistorianAnnotationReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ArchiveSnapshot snapshot = await LoadSnapshotForReadAsync(
                resumeToken,
                ReadKind.Annotation,
                ct).ConfigureAwait(false);
            List<Annotation> values = BuildAnnotationValues(
                snapshot.State,
                request);
            HistorianPage<Annotation> page = CreatePage(
                values,
                snapshot.Manifest.Generation,
                ReadKind.Annotation,
                resumeToken,
                request.MaxValues);
            await PinPageGenerationAsync(
                page,
                snapshot.Manifest.Generation,
                ct).ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<Annotation>> InsertAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            CancellationToken ct)
        {
            return ApplyAnnotationUpdateAsync(
                context,
                nodeId,
                annotations,
                HistoryUpdateType.Insert,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<Annotation>> ReplaceAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            CancellationToken ct)
        {
            return ApplyAnnotationUpdateAsync(
                context,
                nodeId,
                annotations,
                HistoryUpdateType.Replace,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<Annotation>> UpdateAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            CancellationToken ct)
        {
            return ApplyAnnotationUpdateAsync(
                context,
                nodeId,
                annotations,
                HistoryUpdateType.Update,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<Annotation>> DeleteAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DateTimeUtc> annotationTimes,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (annotationTimes.IsNull)
            {
                throw new ArgumentNullException(nameof(annotationTimes));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanAnnotationDelete(
                    state,
                    nodeId,
                    annotationTimes,
                    nextSequence),
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask<HistorianPage<HistorianEventRecord>> ReadEventsAsync(
            HistorianOperationContext context,
            HistorianEventReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ArchiveSnapshot snapshot = await LoadSnapshotForReadAsync(
                resumeToken,
                ReadKind.Event,
                ct).ConfigureAwait(false);
            List<HistorianEventRecord> values = BuildEventValues(
                snapshot.State,
                request);
            HistorianPage<HistorianEventRecord> page = CreatePage(
                values,
                snapshot.Manifest.Generation,
                ReadKind.Event,
                resumeToken,
                request.MaxValues);
            await PinPageGenerationAsync(
                page,
                snapshot.Manifest.Generation,
                ct).ConfigureAwait(false);
            return page;
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> InsertEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return ApplyEventUpdateAsync(
                context,
                nodeId,
                events,
                HistoryUpdateType.Insert,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> ReplaceEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return ApplyEventUpdateAsync(
                context,
                nodeId,
                events,
                HistoryUpdateType.Replace,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> UpdateEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return ApplyEventUpdateAsync(
                context,
                nodeId,
                events,
                HistoryUpdateType.Update,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<
            HistorianUpdateOutcome<HistorianEventRecord>> DeleteEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<ByteString> eventIds,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (eventIds.IsNull)
            {
                throw new ArgumentNullException(nameof(eventIds));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanEventDelete(
                    state,
                    nodeId,
                    eventIds,
                    nextSequence),
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            m_cleanupChannel.Writer.TryComplete();
            m_disposeCts.Cancel();
            try
            {
                await m_cleanupTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            m_election.LeadershipChanged -= OnLeadershipChanged;
            m_disposeCts.Dispose();
            m_writerFenceSemaphore.Dispose();
        }

        internal void Initialize(IServiceMessageContext messageContext)
        {
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }
            lock (m_contextLock)
            {
                if (m_messageContext != null &&
                    !ReferenceEquals(m_messageContext, messageContext))
                {
                    throw new InvalidOperationException(
                        "The historian provider is already initialized with another message context.");
                }
                m_messageContext = messageContext;
            }
        }

        internal async ValueTask RecoverGarbageCollectionAsync(
            CancellationToken ct)
        {
            try
            {
                StoredManifest current = await LoadCurrentManifestAsync(ct)
                    .ConfigureAwait(false);
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                var reachableSegments = new HashSet<string>(
                    current.Manifest.Segments,
                    StringComparer.Ordinal);
                var retainedGenerations = new HashSet<Guid>
                {
                    current.Manifest.Generation
                };

                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(kManifestPrefix, ct).ConfigureAwait(false))
                {
                    Manifest manifest = DecodeManifest(Unprotect(entry.Value));
                    foreach (string segment in manifest.Segments)
                    {
                        reachableSegments.Add(segment);
                    }
                    DateTimeOffset? pinExpiry =
                        await TryGetGenerationPinExpirationAsync(
                            manifest.Generation,
                            ct).ConfigureAwait(false);
                    bool retained = manifest.Generation ==
                        current.Manifest.Generation ||
                        manifest.CreatedAt.Add(
                            m_options.GenerationRetentionTime) > now ||
                        pinExpiry > now;
                    if (retained)
                    {
                        retainedGenerations.Add(manifest.Generation);
                    }
                    else
                    {
                        ScheduleCleanup([entry.Key], TimeSpan.Zero);
                    }
                }

                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(kSegmentPrefix, ct).ConfigureAwait(false))
                {
                    string id = entry.Key[kSegmentPrefix.Length..];
                    if (!reachableSegments.Contains(id) &&
                        DecodeSegmentCreatedAt(entry.Value).Add(
                            m_options.GarbageCollectionGraceTime) <= now)
                    {
                        await DeleteWithRetriesAsync(
                            entry.Key,
                            throwOnFailure: true,
                            ct).ConfigureAwait(false);
                    }
                }

                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(kGenerationPinPrefix, ct).ConfigureAwait(false))
                {
                    if (!Guid.TryParseExact(
                        entry.Key[kGenerationPinPrefix.Length..],
                        "N",
                        out Guid generation) ||
                        retainedGenerations.Contains(generation))
                    {
                        continue;
                    }
                    if (DecodeGenerationPin(entry.Value) <= now)
                    {
                        await DeleteWithRetriesAsync(
                            entry.Key,
                            throwOnFailure: true,
                            ct).ConfigureAwait(false);
                    }
                }

                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(kGenerationGcIntentPrefix, ct)
                    .ConfigureAwait(false))
                {
                    if (!Guid.TryParseExact(
                        entry.Key[kGenerationGcIntentPrefix.Length..],
                        "N",
                        out Guid generation))
                    {
                        continue;
                    }
                    string manifestKey = ManifestGenerationKey(generation);
                    if (generation == current.Manifest.Generation)
                    {
                        await DeleteWithRetriesAsync(
                            entry.Key,
                            throwOnFailure: true,
                            ct).ConfigureAwait(false);
                        continue;
                    }
                    (bool manifestFound, _) = await m_store.TryGetAsync(
                        manifestKey,
                        ct).ConfigureAwait(false);
                    if (!manifestFound)
                    {
                        await DeleteWithRetriesAsync(
                            entry.Key,
                            throwOnFailure: true,
                            ct).ConfigureAwait(false);
                    }
                    else
                    {
                        ScheduleCleanup([manifestKey], TimeSpan.Zero);
                    }
                }
                ClearGarbageCollectionFailure();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (!IsRetryableGarbageCollectionException(exception))
                {
                    SetGarbageCollectionFailure(exception);
                }
                throw;
            }
        }

        internal static string CurrentManifestKey => kCurrentManifestKey;

        private ValueTask<HistorianUpdateOutcome<DataValue>> ApplyDataUpdateAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            HistoryUpdateType updateType,
            bool transactional,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (values.IsNull)
            {
                throw new ArgumentNullException(nameof(values));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanDataUpdate(
                    state,
                    nodeId,
                    values,
                    updateType,
                    context.DefaultModificationInfo,
                    transactional,
                    nextSequence),
                ct);
        }

        private ValueTask<HistorianUpdateOutcome<DataValue>>
            ApplyStructuredUpdateAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                HistoryUpdateType updateType,
                bool remove,
                CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!m_structuredKeySelectors.TryGetValue(
                nodeId,
                out IHistorianStructuredDataKeySelector? selector))
            {
                return new(Rejected<DataValue>(
                    values.Count,
                    StatusCodes.BadHistoryOperationUnsupported));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanStructuredUpdate(
                    state,
                    nodeId,
                    values,
                    selector,
                    updateType,
                    remove,
                    context.DefaultModificationInfo,
                    nextSequence),
                ct);
        }

        private ValueTask<HistorianUpdateOutcome<Annotation>>
            ApplyAnnotationUpdateAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<Annotation> annotations,
                HistoryUpdateType updateType,
                CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (annotations.IsNull)
            {
                throw new ArgumentNullException(nameof(annotations));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanAnnotationUpdate(
                    state,
                    nodeId,
                    annotations,
                    updateType,
                    nextSequence),
                ct);
        }

        private ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>
            ApplyEventUpdateAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<HistorianEventRecord> events,
                HistoryUpdateType updateType,
                CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (events.IsNull)
            {
                throw new ArgumentNullException(nameof(events));
            }
            return PublishUpdateAsync(
                (state, nextSequence) => PlanEventUpdate(
                    state,
                    nodeId,
                    events,
                    updateType,
                    nextSequence),
                ct);
        }

        private async ValueTask<HistorianUpdateOutcome<T>> PublishUpdateAsync<T>(
            Func<ArchiveState, long, UpdatePlan<T>> planner,
            CancellationToken ct)
        {
            ThrowIfGarbageCollectionFailed();
            for (int attempt = 0; attempt < kMaxPublishAttempts; attempt++)
            {
                if (!await EnsureWriterFenceAsync(ct).ConfigureAwait(false))
                {
                    UpdatePlan<T> rejectedPlan = planner(
                        new ArchiveState(),
                        0);
                    return Rejected<T>(
                        rejectedPlan.Outcome.OperationResults.Count,
                        StatusCodes.BadNotWritable);
                }
                StoredManifest stored = await LoadCurrentManifestAsync(ct)
                    .ConfigureAwait(false);
                if (!IsCurrentWriter(stored.Manifest))
                {
                    UpdatePlan<T> rejectedPlan = planner(
                        new ArchiveState(),
                        0);
                    return Rejected<T>(
                        rejectedPlan.Outcome.OperationResults.Count,
                        StatusCodes.BadNotWritable);
                }
                ArchiveState state = await LoadStateAsync(stored.Manifest, ct)
                    .ConfigureAwait(false);
                UpdatePlan<T> plan = planner(
                    state,
                    stored.Manifest.NextSequence);
                stored.Manifest.NextSequence = plan.NextSequence;
                if (plan.Mutations.Count == 0)
                {
                    return plan.Outcome;
                }
                if (!m_election.IsLeader)
                {
                    return Rejected<T>(
                        plan.Outcome.OperationResults.Count,
                        StatusCodes.BadNotWritable);
                }
                if (await TryPublishAsync(
                    stored,
                    state,
                    plan.Mutations,
                    ct).ConfigureAwait(false))
                {
                    return plan.Outcome;
                }
            }

            throw new ServiceResultException(
                StatusCodes.BadTransactionFailed,
                "The historian manifest changed too frequently to publish the update.");
        }

        private UpdatePlan<DataValue> PlanDataUpdate(
            ArchiveState state,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            HistoryUpdateType updateType,
            ModificationInfo defaultInfo,
            bool transactional,
            long nextSequence)
        {
            if (nodeId.IsNull)
            {
                return InvalidPlan<DataValue>(values.Count, nextSequence);
            }

            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            var statuses = new StatusCode[values.Count];
            var oldValues = new List<DataValue>();
            var mutations = new List<Mutation>();
            IHistorianStructuredDataKeySelector selector =
                m_structuredKeySelectors.TryGetValue(
                    nodeId,
                    out IHistorianStructuredDataKeySelector? configured)
                ? configured
                : TimestampStructuredDataKeySelector.Instance;

            for (int i = 0; i < values.Count; i++)
            {
                DataValue value = values[i];
                if (value.IsNull ||
                    !selector.TryGetUniquenessKey(
                        in value,
                        out ByteString uniquenessKey))
                {
                    StatusCode failure = value.IsNull
                        ? StatusCodes.BadInvalidArgument
                        : StatusCodes.BadTypeMismatch;
                    if (transactional)
                    {
                        return RollbackPlan<DataValue>(
                            values.Count,
                            i,
                            failure,
                            nextSequence);
                    }
                    statuses[i] = failure;
                    continue;
                }
                if (uniquenessKey.Length > kMaxDiscriminatorBytes)
                {
                    if (transactional)
                    {
                        return RollbackPlan<DataValue>(
                            values.Count,
                            i,
                            StatusCodes.BadEncodingLimitsExceeded,
                            nextSequence);
                    }
                    statuses[i] = StatusCodes.BadEncodingLimitsExceeded;
                    continue;
                }

                var key = new HistoricalValueKey(
                    value.SourceTimestamp,
                    uniquenessKey);
                bool exists = archive.Raw.TryGetValue(key, out DataValue prior);
                StatusCode status = updateType switch
                {
                    HistoryUpdateType.Insert => exists
                        ? StatusCodes.BadEntryExists
                        : StatusCodes.GoodEntryInserted,
                    HistoryUpdateType.Replace => exists
                        ? StatusCodes.GoodEntryReplaced
                        : StatusCodes.BadNoEntryExists,
                    HistoryUpdateType.Update => exists
                        ? StatusCodes.GoodEntryReplaced
                        : StatusCodes.GoodEntryInserted,
                    _ => StatusCodes.BadInvalidArgument
                };
                if (StatusCode.IsBad(status))
                {
                    if (transactional)
                    {
                        return RollbackPlan<DataValue>(
                            values.Count,
                            i,
                            status,
                            nextSequence);
                    }
                    statuses[i] = status;
                    continue;
                }

                if (exists)
                {
                    oldValues.Add(prior);
                    var info = new ModificationInfo
                    {
                        ModificationTime = defaultInfo.ModificationTime,
                        UpdateType = updateType,
                        UserName = defaultInfo.UserName
                    };
                    var modified = new ModifiedEntry(
                        prior,
                        info,
                        ++nextSequence);
                    archive.Modified.Add(modified);
                    mutations.Add(Mutation.AddModified(
                        nodeId,
                        modified));
                }
                archive.Raw[key] = value;
                mutations.Add(Mutation.SetRaw(nodeId, key, value));
                statuses[i] = status;
            }

            return new UpdatePlan<DataValue>(
                Outcome(statuses, oldValues),
                mutations,
                nextSequence);
        }

        private UpdatePlan<DataValue> PlanRangeDelete(
            ArchiveState state,
            NodeId nodeId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            bool isDeleteModified,
            ModificationInfo defaultInfo,
            long nextSequence)
        {
            if (nodeId.IsNull)
            {
                return InvalidPlan<DataValue>(1, nextSequence);
            }
            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            DateTimeUtc lower = startTime <= endTime ? startTime : endTime;
            DateTimeUtc upper = startTime <= endTime ? endTime : startTime;
            var oldValues = new List<DataValue>();
            var mutations = new List<Mutation>();

            if (isDeleteModified)
            {
                for (int i = archive.Modified.Count - 1; i >= 0; i--)
                {
                    ModifiedEntry entry = archive.Modified[i];
                    DateTimeUtc timestamp = entry.Value.SourceTimestamp;
                    if (timestamp >= lower && timestamp < upper)
                    {
                        oldValues.Add(entry.Value);
                        archive.Modified.RemoveAt(i);
                    }
                }
                if (oldValues.Count > 0)
                {
                    mutations.Add(Mutation.DeleteModifiedRange(
                        nodeId,
                        lower,
                        upper));
                }
            }
            else
            {
                List<HistoricalValueKey> keys = [.. archive.Raw.Keys.Where(
                    key => key.SourceTimestamp >= lower &&
                        key.SourceTimestamp < upper)];
                foreach (HistoricalValueKey key in keys)
                {
                    DataValue prior = archive.Raw[key];
                    archive.Raw.Remove(key);
                    oldValues.Add(prior);
                    var info = new ModificationInfo
                    {
                        ModificationTime = defaultInfo.ModificationTime,
                        UpdateType = HistoryUpdateType.Delete,
                        UserName = defaultInfo.UserName
                    };
                    var modified = new ModifiedEntry(
                        prior,
                        info,
                        ++nextSequence);
                    archive.Modified.Add(modified);
                    mutations.Add(Mutation.AddModified(nodeId, modified));
                    mutations.Add(Mutation.DeleteRaw(nodeId, key));
                }
            }

            return new UpdatePlan<DataValue>(
                Outcome(
                    [oldValues.Count > 0 ? StatusCodes.Good : StatusCodes.GoodNoData],
                    oldValues),
                mutations,
                nextSequence);
        }

        private static UpdatePlan<DataValue> PlanStructuredUpdate(
            ArchiveState state,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            IHistorianStructuredDataKeySelector selector,
            HistoryUpdateType updateType,
            bool remove,
            ModificationInfo defaultInfo,
            long nextSequence)
        {
            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            var statuses = new StatusCode[values.Count];
            var oldValues = new List<DataValue>();
            var mutations = new List<Mutation>();
            for (int i = 0; i < values.Count; i++)
            {
                DataValue value = values[i];
                if (value.IsNull ||
                    !selector.TryGetUniquenessKey(
                        in value,
                        out ByteString uniquenessKey))
                {
                    statuses[i] = value.IsNull
                        ? StatusCodes.BadInvalidArgument
                        : StatusCodes.BadTypeMismatch;
                    continue;
                }
                if (uniquenessKey.Length > kMaxDiscriminatorBytes)
                {
                    statuses[i] = StatusCodes.BadEncodingLimitsExceeded;
                    continue;
                }
                var key = new HistoricalValueKey(
                    value.SourceTimestamp,
                    uniquenessKey);
                bool exists = archive.Raw.TryGetValue(
                    key,
                    out DataValue prior);
                StatusCode status = remove
                    ? exists
                        ? StatusCodes.Good
                        : StatusCodes.BadNoEntryExists
                    : updateType switch
                    {
                        HistoryUpdateType.Insert => exists
                            ? StatusCodes.BadEntryExists
                            : StatusCodes.GoodEntryInserted,
                        HistoryUpdateType.Replace => exists
                            ? StatusCodes.GoodEntryReplaced
                            : StatusCodes.BadNoEntryExists,
                        HistoryUpdateType.Update => exists
                            ? StatusCodes.GoodEntryReplaced
                            : StatusCodes.GoodEntryInserted,
                        _ => StatusCodes.BadInvalidArgument
                    };
                if (StatusCode.IsBad(status))
                {
                    statuses[i] = status;
                    continue;
                }
                if (exists)
                {
                    oldValues.Add(prior);
                    var info = new ModificationInfo
                    {
                        ModificationTime = defaultInfo.ModificationTime,
                        UpdateType = remove
                            ? HistoryUpdateType.Delete
                            : updateType,
                        UserName = defaultInfo.UserName
                    };
                    var modified = new ModifiedEntry(
                        prior,
                        info,
                        ++nextSequence);
                    archive.Modified.Add(modified);
                    mutations.Add(Mutation.AddModified(nodeId, modified));
                }
                if (remove)
                {
                    archive.Raw.Remove(key);
                    mutations.Add(Mutation.DeleteRaw(nodeId, key));
                }
                else
                {
                    archive.Raw[key] = value;
                    mutations.Add(Mutation.SetRaw(nodeId, key, value));
                }
                statuses[i] = status;
            }
            return new UpdatePlan<DataValue>(
                Outcome(statuses, oldValues),
                mutations,
                nextSequence);
        }

        private UpdatePlan<DataValue> PlanPointDelete(
            ArchiveState state,
            NodeId nodeId,
            ArrayOf<DateTimeUtc> timestamps,
            ModificationInfo defaultInfo,
            long nextSequence)
        {
            if (nodeId.IsNull)
            {
                return InvalidPlan<DataValue>(timestamps.Count, nextSequence);
            }
            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            var statuses = new StatusCode[timestamps.Count];
            var oldValues = new List<DataValue>();
            var mutations = new List<Mutation>();
            for (int i = 0; i < timestamps.Count; i++)
            {
                var key =
                    HistoricalValueKey.FromTimestamp(timestamps[i]);
                if (!archive.Raw.TryGetValue(key, out DataValue prior))
                {
                    statuses[i] = StatusCodes.BadNoEntryExists;
                    continue;
                }
                archive.Raw.Remove(key);
                oldValues.Add(prior);
                var info = new ModificationInfo
                {
                    ModificationTime = defaultInfo.ModificationTime,
                    UpdateType = HistoryUpdateType.Delete,
                    UserName = defaultInfo.UserName
                };
                var modified = new ModifiedEntry(prior, info, ++nextSequence);
                archive.Modified.Add(modified);
                mutations.Add(Mutation.AddModified(nodeId, modified));
                mutations.Add(Mutation.DeleteRaw(nodeId, key));
                statuses[i] = StatusCodes.Good;
            }
            return new UpdatePlan<DataValue>(
                Outcome(statuses, oldValues),
                mutations,
                nextSequence);
        }

        private static UpdatePlan<Annotation> PlanAnnotationUpdate(
            ArchiveState state,
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            HistoryUpdateType updateType,
            long nextSequence)
        {
            if (nodeId.IsNull)
            {
                return InvalidPlan<Annotation>(annotations.Count, nextSequence);
            }
            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            var statuses = new StatusCode[annotations.Count];
            var oldValues = new List<Annotation>();
            var mutations = new List<Mutation>();
            for (int i = 0; i < annotations.Count; i++)
            {
                Annotation annotation = annotations[i];
                if (annotation == null)
                {
                    statuses[i] = StatusCodes.BadInvalidArgument;
                    continue;
                }
                DateTimeUtc key = annotation.AnnotationTime;
                bool exists = archive.Annotations.TryGetValue(
                    key,
                    out Annotation? prior);
                StatusCode status = updateType switch
                {
                    HistoryUpdateType.Insert => exists
                        ? StatusCodes.BadEntryExists
                        : StatusCodes.GoodEntryInserted,
                    HistoryUpdateType.Replace => exists
                        ? StatusCodes.GoodEntryReplaced
                        : StatusCodes.BadNoEntryExists,
                    HistoryUpdateType.Update => exists
                        ? StatusCodes.GoodEntryReplaced
                        : StatusCodes.GoodEntryInserted,
                    _ => StatusCodes.BadInvalidArgument
                };
                if (StatusCode.IsBad(status))
                {
                    statuses[i] = status;
                    continue;
                }
                if (exists)
                {
                    oldValues.Add(CloneAnnotation(prior!));
                }
                Annotation clone = CloneAnnotation(annotation);
                archive.Annotations[key] = clone;
                mutations.Add(Mutation.SetAnnotation(nodeId, clone));
                statuses[i] = status;
            }
            return new UpdatePlan<Annotation>(
                Outcome(statuses, oldValues),
                mutations,
                nextSequence);
        }

        private static UpdatePlan<Annotation> PlanAnnotationDelete(
            ArchiveState state,
            NodeId nodeId,
            ArrayOf<DateTimeUtc> annotationTimes,
            long nextSequence)
        {
            if (nodeId.IsNull)
            {
                return InvalidPlan<Annotation>(
                    annotationTimes.Count,
                    nextSequence);
            }
            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            var statuses = new StatusCode[annotationTimes.Count];
            var oldValues = new List<Annotation>();
            var mutations = new List<Mutation>();
            for (int i = 0; i < annotationTimes.Count; i++)
            {
                DateTimeUtc key = annotationTimes[i];
                if (!archive.Annotations.TryGetValue(
                    key,
                    out Annotation? prior))
                {
                    statuses[i] = StatusCodes.BadNoEntryExists;
                    continue;
                }
                archive.Annotations.Remove(key);
                oldValues.Add(CloneAnnotation(prior));
                mutations.Add(Mutation.DeleteAnnotation(nodeId, key));
                statuses[i] = StatusCodes.Good;
            }
            return new UpdatePlan<Annotation>(
                Outcome(statuses, oldValues),
                mutations,
                nextSequence);
        }

        private static UpdatePlan<HistorianEventRecord> PlanEventUpdate(
            ArchiveState state,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            HistoryUpdateType updateType,
            long nextSequence)
        {
            if (nodeId.IsNull)
            {
                return InvalidPlan<HistorianEventRecord>(
                    events.Count,
                    nextSequence);
            }
            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            var statuses = new StatusCode[events.Count];
            var oldValues = new List<HistorianEventRecord>();
            var mutations = new List<Mutation>();
            for (int i = 0; i < events.Count; i++)
            {
                HistorianEventRecord record = events[i];
                if (record == null || record.EventId.IsEmpty)
                {
                    statuses[i] = StatusCodes.BadInvalidArgument;
                    continue;
                }
                int index = archive.Events.FindIndex(
                    entry => entry.Record.EventId == record.EventId);
                bool exists = index >= 0;
                StatusCode status = updateType switch
                {
                    HistoryUpdateType.Insert => exists
                        ? StatusCodes.BadEntryExists
                        : StatusCodes.GoodEntryInserted,
                    HistoryUpdateType.Replace => exists
                        ? StatusCodes.GoodEntryReplaced
                        : StatusCodes.BadNoEntryExists,
                    HistoryUpdateType.Update => exists
                        ? StatusCodes.GoodEntryReplaced
                        : StatusCodes.GoodEntryInserted,
                    _ => StatusCodes.BadInvalidArgument
                };
                if (StatusCode.IsBad(status))
                {
                    statuses[i] = status;
                    continue;
                }
                EventEntry entry;
                if (exists)
                {
                    EventEntry prior = archive.Events[index];
                    if (!TryMergeEvent(
                        prior.Record,
                        record,
                        out HistorianEventRecord merged,
                        out StatusCode mergeStatus))
                    {
                        statuses[i] = mergeStatus;
                        continue;
                    }
                    oldValues.Add(CloneEvent(prior.Record));
                    entry = prior with
                    {
                        Record = merged
                    };
                    archive.Events[index] = entry;
                }
                else
                {
                    entry = new EventEntry(CloneEvent(record), ++nextSequence);
                    archive.Events.Add(entry);
                }
                mutations.Add(Mutation.SetEvent(nodeId, entry));
                statuses[i] = status;
            }
            return new UpdatePlan<HistorianEventRecord>(
                Outcome(statuses, oldValues),
                mutations,
                nextSequence);
        }

        private static UpdatePlan<HistorianEventRecord> PlanEventDelete(
            ArchiveState state,
            NodeId nodeId,
            ArrayOf<ByteString> eventIds,
            long nextSequence)
        {
            if (nodeId.IsNull)
            {
                return InvalidPlan<HistorianEventRecord>(
                    eventIds.Count,
                    nextSequence);
            }
            NodeArchive archive = state.GetOrCreateArchive(nodeId);
            var statuses = new StatusCode[eventIds.Count];
            var oldValues = new List<HistorianEventRecord>();
            var mutations = new List<Mutation>();
            for (int i = 0; i < eventIds.Count; i++)
            {
                int index = archive.Events.FindIndex(
                    entry => entry.Record.EventId == eventIds[i]);
                if (index < 0)
                {
                    statuses[i] = StatusCodes.BadNoEntryExists;
                    continue;
                }
                EventEntry prior = archive.Events[index];
                archive.Events.RemoveAt(index);
                oldValues.Add(CloneEvent(prior.Record));
                mutations.Add(Mutation.DeleteEvent(nodeId, eventIds[i]));
                statuses[i] = StatusCodes.Good;
            }
            return new UpdatePlan<HistorianEventRecord>(
                Outcome(statuses, oldValues),
                mutations,
                nextSequence);
        }

        private async ValueTask<bool> TryPublishAsync(
            StoredManifest stored,
            ArchiveState state,
            List<Mutation> mutations,
            CancellationToken ct)
        {
            bool compact = stored.Manifest.Segments.Count > 0 &&
                stored.Manifest.Segments.Count +
                SegmentCountFor(mutations.Count) >
                m_options.CompactionSegmentThreshold;
            List<Mutation> records = compact
                ? BuildSnapshotMutations(state)
                : mutations;
            List<string> newSegmentIds = await WriteSegmentsAsync(records, ct)
                .ConfigureAwait(false);
            string? generationKey = null;
            try
            {
                List<string> publishedSegments = compact
                    ? newSegmentIds
                    : [.. stored.Manifest.Segments, .. newSegmentIds];
                if (publishedSegments.Count > m_options.MaxSegments)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "The historian manifest exceeds the configured segment limit.");
                }

                var manifest = new Manifest
                {
                    WriterId = stored.Manifest.WriterId,
                    WriterEpoch = stored.Manifest.WriterEpoch,
                    Generation = Guid.NewGuid(),
                    GenerationNumber = stored.Manifest.GenerationNumber + 1,
                    NextSequence = stored.Manifest.NextSequence,
                    CreatedAt = m_timeProvider.GetUtcNow().UtcDateTime,
                    Segments = publishedSegments
                };
                ByteString manifestRecord = Protect(EncodeManifest(manifest));
                generationKey = ManifestGenerationKey(manifest.Generation);
                bool generationStored = await CompareAndSwapResolvedAsync(
                    generationKey,
                    default,
                    manifestRecord,
                    ct).ConfigureAwait(false);
                if (!generationStored)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "A historian generation identifier collision occurred.");
                }

                HistorianWriterFence localFence = GetLocalWriterFence();
                if (!await m_fencingAuthority.IsCurrentAsync(
                    localFence,
                    ct).ConfigureAwait(false))
                {
                    ClearLocalWriterFence();
                    ScheduleCleanup(
                        [generationKey, .. newSegmentIds],
                        TimeSpan.Zero);
                    return false;
                }
                bool published = await CompareAndSwapResolvedAsync(
                    kCurrentManifestKey,
                    stored.Record,
                    manifestRecord,
                    ct).ConfigureAwait(false);
                if (!published)
                {
                    ScheduleCleanup(
                        [generationKey, .. newSegmentIds],
                        TimeSpan.Zero);
                    return false;
                }

                if (stored.Manifest.Generation != Guid.Empty)
                {
                    ScheduleCleanup(
                        [ManifestGenerationKey(stored.Manifest.Generation)],
                        m_options.GenerationRetentionTime);
                }
                if (compact)
                {
                    ScheduleCleanup(
                        stored.Manifest.Segments,
                        m_options.GenerationRetentionTime);
                }
                return true;
            }
            catch (ServiceResultException exception)
                when (IsIndeterminateStoreOperation(exception))
            {
                throw;
            }
            catch
            {
                if (generationKey == null)
                {
                    ScheduleCleanup(newSegmentIds, TimeSpan.Zero);
                }
                else
                {
                    ScheduleCleanup(
                        [generationKey, .. newSegmentIds],
                        TimeSpan.Zero);
                }
                throw;
            }
        }

        private async ValueTask<bool> EnsureWriterFenceAsync(
            CancellationToken ct)
        {
            if (!m_election.IsLeader)
            {
                return false;
            }
            await m_writerFenceSemaphore.WaitAsync(ct)
                .ConfigureAwait(false);
            try
            {
                if (!m_election.IsLeader)
                {
                    return false;
                }
                HistorianWriterFence? acquired =
                    await m_fencingAuthority.TryAcquireOrRenewAsync(ct)
                        .ConfigureAwait(false);
                if (!acquired.HasValue)
                {
                    ClearLocalWriterFence();
                    return false;
                }
                HistorianWriterFence fence = acquired.Value;
                for (int attempt = 0;
                    attempt < kMaxPublishAttempts;
                    attempt++)
                {
                    StoredManifest current =
                        await LoadCurrentManifestAsync(ct)
                            .ConfigureAwait(false);
                    if (current.Manifest.WriterId == fence.WriterId &&
                        current.Manifest.WriterEpoch == fence.Epoch)
                    {
                        SetLocalWriterFence(fence);
                        return true;
                    }
                    var fenced = new Manifest
                    {
                        WriterId = fence.WriterId,
                        WriterEpoch = fence.Epoch,
                        Generation = Guid.NewGuid(),
                        GenerationNumber =
                            current.Manifest.GenerationNumber + 1,
                        NextSequence =
                            current.Manifest.NextSequence,
                        CreatedAt =
                            m_timeProvider.GetUtcNow().UtcDateTime,
                        Segments =
                            [.. current.Manifest.Segments]
                    };
                    ByteString record = Protect(
                        EncodeManifest(fenced));
                    string generationKey =
                        ManifestGenerationKey(fenced.Generation);
                    bool generationStored =
                        await CompareAndSwapResolvedAsync(
                            generationKey,
                            default,
                            record,
                            ct).ConfigureAwait(false);
                    if (!generationStored)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "A historian writer-fence generation identifier collision occurred.");
                    }
                    bool published;
                    try
                    {
                        if (!await m_fencingAuthority.IsCurrentAsync(
                            fence,
                            ct).ConfigureAwait(false))
                        {
                            ScheduleCleanup(
                                [generationKey],
                                TimeSpan.Zero);
                            ClearLocalWriterFence();
                            return false;
                        }
                        published =
                            await CompareAndSwapResolvedAsync(
                                kCurrentManifestKey,
                                current.Record,
                                record,
                                ct).ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception)
                        when (IsIndeterminateStoreOperation(exception))
                    {
                        throw;
                    }
                    catch
                    {
                        ScheduleCleanup(
                            [generationKey],
                            TimeSpan.Zero);
                        throw;
                    }
                    if (published)
                    {
                        if (current.Manifest.Generation !=
                            Guid.Empty)
                        {
                            ScheduleCleanup(
                                [
                                    ManifestGenerationKey(
                                        current.Manifest.Generation)
                                ],
                                m_options.GenerationRetentionTime);
                        }
                        SetLocalWriterFence(fence);
                        return true;
                    }
                    ScheduleCleanup(
                        [generationKey],
                        TimeSpan.Zero);
                    if (!await m_fencingAuthority.IsCurrentAsync(
                        fence,
                        ct).ConfigureAwait(false))
                    {
                        ClearLocalWriterFence();
                        return false;
                    }
                }
                return false;
            }
            finally
            {
                m_writerFenceSemaphore.Release();
            }
        }

        private bool IsCurrentWriter(Manifest manifest)
        {
            long localEpoch = Volatile.Read(ref m_writerEpoch);
            return m_election.IsLeader &&
                localEpoch > 0 &&
                manifest.WriterId == m_writerId &&
                manifest.WriterEpoch == localEpoch;
        }

        private async ValueTask<bool> CompareAndSwapResolvedAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct)
        {
            try
            {
                return await m_store.CompareAndSwapAsync(
                    key,
                    expected,
                    value,
                    ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                try
                {
                    (bool found, ByteString current) =
                        await m_store.TryGetAsync(
                            key,
                            CancellationToken.None)
                            .ConfigureAwait(false);
                    if (found && current == value)
                    {
                        return true;
                    }
                }
                catch (Exception readException)
                {
                    throw CreateIndeterminateStoreException(
                        "The historian compare/exchange result could not be resolved.",
                        new AggregateException(exception, readException));
                }
                throw CreateIndeterminateStoreException(
                    "The historian compare/exchange may have committed.",
                    exception);
            }
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            if (!isLeader)
            {
                ClearLocalWriterFence();
            }
        }

        private void SetLocalWriterFence(HistorianWriterFence fence)
        {
            m_writerId = fence.WriterId;
            Volatile.Write(ref m_writerEpoch, fence.Epoch);
        }

        private void ClearLocalWriterFence()
        {
            Volatile.Write(ref m_writerEpoch, 0);
            m_writerId = Guid.Empty;
        }

        private HistorianWriterFence GetLocalWriterFence()
        {
            long epoch = Volatile.Read(ref m_writerEpoch);
            return new HistorianWriterFence(m_writerId, epoch);
        }

        private async ValueTask<List<string>> WriteSegmentsAsync(
            List<Mutation> records,
            CancellationToken ct)
        {
            var ids = new List<string>(SegmentCountFor(records.Count));
            try
            {
                for (int offset = 0; offset < records.Count;
                    offset += m_options.MaxRecordsPerSegment)
                {
                    int count = Math.Min(
                        m_options.MaxRecordsPerSegment,
                        records.Count - offset);
                    ByteString plaintext = EncodeSegment(
                        records,
                        offset,
                        count);
                    ByteString protectedRecord = Protect(plaintext);
                    string id = Guid.NewGuid().ToString("N");
                    string key = SegmentKey(id);
                    if (!await m_store.CompareAndSwapAsync(
                        key,
                        default,
                        protectedRecord,
                        ct).ConfigureAwait(false))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "A historian segment identifier collision occurred.");
                    }
                    ids.Add(id);
                }
                return ids;
            }
            catch
            {
                ScheduleCleanup(ids, TimeSpan.Zero);
                throw;
            }
        }

        private int SegmentCountFor(int recordCount)
        {
            return (recordCount + m_options.MaxRecordsPerSegment - 1) /
                m_options.MaxRecordsPerSegment;
        }

        private async ValueTask<ArchiveSnapshot> LoadSnapshotForReadAsync(
            HistorianResumeToken token,
            ReadKind expectedKind,
            CancellationToken ct)
        {
            Manifest manifest;
            if (token.IsEmpty)
            {
                manifest = (await LoadCurrentManifestAsync(ct)
                    .ConfigureAwait(false)).Manifest;
            }
            else
            {
                PageCursor cursor = DecodePageCursor(token, expectedKind);
                manifest = await LoadManifestGenerationAsync(
                    cursor.Generation,
                    ct).ConfigureAwait(false);
            }
            ArchiveState state = await LoadStateAsync(manifest, ct)
                .ConfigureAwait(false);
            return new ArchiveSnapshot(manifest, state);
        }

        private async ValueTask<StoredManifest> LoadCurrentManifestAsync(
            CancellationToken ct)
        {
            (bool found, ByteString record) = await m_store.TryGetAsync(
                kCurrentManifestKey,
                ct).ConfigureAwait(false);
            if (!found)
            {
                return new StoredManifest(new Manifest(), default);
            }
            Manifest manifest = DecodeManifest(Unprotect(record));
            return new StoredManifest(manifest, record);
        }

        private async ValueTask<Manifest> LoadManifestGenerationAsync(
            Guid generation,
            CancellationToken ct)
        {
            (bool found, ByteString record) = await m_store.TryGetAsync(
                ManifestGenerationKey(generation),
                ct).ConfigureAwait(false);
            if (!found)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid,
                    "The pinned historian generation has expired.");
            }
            Manifest manifest = DecodeManifest(Unprotect(record));
            if (manifest.Generation != generation)
            {
                throw CorruptRecord("The historian generation identity is invalid.");
            }
            return manifest;
        }

        private async ValueTask<ArchiveState> LoadStateAsync(
            Manifest manifest,
            CancellationToken ct)
        {
            var state = new ArchiveState();
            foreach (string id in manifest.Segments)
            {
                (bool found, ByteString record) = await m_store.TryGetAsync(
                    SegmentKey(id),
                    ct).ConfigureAwait(false);
                if (!found)
                {
                    throw CorruptRecord(
                        "A segment referenced by the historian manifest is missing.");
                }
                DecodeAndApplySegment(Unprotect(record), state);
            }
            return state;
        }

        private List<HistoricalDataValue> BuildRawValues(
            ArchiveState state,
            HistorianRawReadRequest request)
        {
            NodeArchive? archive = state.TryGetArchive(request.NodeId);
            if (archive == null)
            {
                return [];
            }
            DateTimeUtc lower = request.StartTime <= request.EndTime
                ? request.StartTime
                : request.EndTime;
            DateTimeUtc upper = request.StartTime <= request.EndTime
                ? request.EndTime
                : request.StartTime;
            var entries = archive.Raw
                .Where(entry =>
                    entry.Key.SourceTimestamp >= lower &&
                    (lower == upper
                        ? entry.Key.SourceTimestamp == lower
                        : entry.Key.SourceTimestamp < upper))
                .ToList();
            if (!request.IsForward)
            {
                entries.Reverse();
            }

            var result = new List<HistoricalDataValue>();
            if (request.ReturnBounds)
            {
                KeyValuePair<HistoricalValueKey, DataValue>? leading =
                    request.IsForward
                    ? archive.Raw.LastOrDefault(entry =>
                        entry.Key.SourceTimestamp < lower)
                    : archive.Raw.FirstOrDefault(entry =>
                        entry.Key.SourceTimestamp >= upper);
                if (leading.HasValue &&
                    !leading.Value.Value.IsNull)
                {
                    result.Add(new HistoricalDataValue(
                        leading.Value.Value,
                        IsBound: true));
                }
                else if (lower != DateTimeUtc.MinValue &&
                    upper != DateTimeUtc.MaxValue)
                {
                    result.Add(MissingBound(
                        request.IsForward ? lower : upper));
                }
            }
            foreach (KeyValuePair<HistoricalValueKey, DataValue> entry in
                entries)
            {
                result.Add(new HistoricalDataValue(entry.Value));
            }
            if (request.ReturnBounds && lower != upper)
            {
                KeyValuePair<HistoricalValueKey, DataValue>? trailing =
                    request.IsForward
                    ? archive.Raw.FirstOrDefault(entry =>
                        entry.Key.SourceTimestamp >= upper)
                    : archive.Raw.LastOrDefault(entry =>
                        entry.Key.SourceTimestamp < lower);
                if (trailing.HasValue &&
                    !trailing.Value.Value.IsNull)
                {
                    result.Add(new HistoricalDataValue(
                        trailing.Value.Value,
                        IsBound: true));
                }
                else if (lower != DateTimeUtc.MinValue &&
                    upper != DateTimeUtc.MaxValue)
                {
                    result.Add(MissingBound(
                        request.IsForward ? upper : lower));
                }
            }
            return result;
        }

        private static List<ModifiedDataValue> BuildModifiedValues(
            ArchiveState state,
            HistorianModifiedReadRequest request)
        {
            NodeArchive? archive = state.TryGetArchive(request.NodeId);
            if (archive == null)
            {
                return [];
            }
            DateTimeUtc lower = request.StartTime <= request.EndTime
                ? request.StartTime
                : request.EndTime;
            DateTimeUtc upper = request.StartTime <= request.EndTime
                ? request.EndTime
                : request.StartTime;
            IEnumerable<ModifiedEntry> source = request.IsForward
                ? archive.Modified
                    .Where(entry =>
                        entry.Value.SourceTimestamp >= lower &&
                        entry.Value.SourceTimestamp < upper)
                    .OrderBy(entry => entry.Value.SourceTimestamp)
                    .ThenByDescending(entry => entry.Info.ModificationTime)
                    .ThenByDescending(entry => entry.Sequence)
                : archive.Modified
                    .Where(entry =>
                        entry.Value.SourceTimestamp >= lower &&
                        entry.Value.SourceTimestamp < upper)
                    .OrderByDescending(entry => entry.Value.SourceTimestamp)
                    .ThenBy(entry => entry.Info.ModificationTime)
                    .ThenBy(entry => entry.Sequence);
            return [.. source.Select(entry =>
                new ModifiedDataValue(entry.Value, CloneInfo(entry.Info)))];
        }

        private static List<Annotation> BuildAnnotationValues(
            ArchiveState state,
            HistorianAnnotationReadRequest request)
        {
            NodeArchive? archive = state.TryGetArchive(request.NodeId);
            if (archive == null)
            {
                return [];
            }
            DateTimeUtc lower = request.StartTime <= request.EndTime
                ? request.StartTime
                : request.EndTime;
            DateTimeUtc upper = request.StartTime <= request.EndTime
                ? request.EndTime
                : request.StartTime;
            IEnumerable<KeyValuePair<DateTimeUtc, Annotation>> source =
                archive.Annotations.Where(entry =>
                    entry.Key >= lower && entry.Key < upper);
            if (!request.IsForward)
            {
                source = source.Reverse();
            }
            return [.. source.Select(entry => CloneAnnotation(entry.Value))];
        }

        private static List<HistorianEventRecord> BuildEventValues(
            ArchiveState state,
            HistorianEventReadRequest request)
        {
            NodeArchive? archive = state.TryGetArchive(request.NodeId);
            if (archive == null)
            {
                return [];
            }
            DateTimeUtc lower = request.StartTime <= request.EndTime
                ? request.StartTime
                : request.EndTime;
            DateTimeUtc upper = request.StartTime <= request.EndTime
                ? request.EndTime
                : request.StartTime;
            IEnumerable<EventEntry> source = request.IsForward
                ? archive.Events
                    .Where(entry =>
                        entry.Record.SourceTimestamp >= lower &&
                        entry.Record.SourceTimestamp < upper)
                    .OrderBy(entry => entry.Record.SourceTimestamp)
                    .ThenBy(entry => entry.Sequence)
                : archive.Events
                    .Where(entry =>
                        entry.Record.SourceTimestamp >= lower &&
                        entry.Record.SourceTimestamp < upper)
                    .OrderByDescending(entry => entry.Record.SourceTimestamp)
                    .ThenByDescending(entry => entry.Sequence);
            return [.. source.Select(entry => CloneEvent(entry.Record))];
        }

        private static DataValue InterpolateAtTime(
            IEnumerable<DataValue>? values,
            DateTimeUtc requestedTime,
            bool useSimpleBounds)
        {
            DataValue before = DataValue.Null;
            DataValue after = DataValue.Null;
            if (values != null)
            {
                foreach (DataValue value in values)
                {
                    int comparison = value.SourceTimestamp.CompareTo(
                        requestedTime);
                    if (comparison == 0)
                    {
                        return value;
                    }
                    if (comparison < 0)
                    {
                        before = value;
                    }
                    else
                    {
                        after = value;
                        break;
                    }
                }
            }
            if (useSimpleBounds || before.IsNull || after.IsNull)
            {
                DataValue closest = !before.IsNull ? before : after;
                if (closest.IsNull)
                {
                    return new DataValue(
                        Variant.Null,
                        StatusCodes.BadNoData,
                        requestedTime,
                        DateTimeUtc.MinValue);
                }
                return new DataValue(
                    closest.WrappedValue,
                    StatusCodes.UncertainNoCommunicationLastUsableValue,
                    requestedTime,
                    DateTimeUtc.MinValue);
            }
            return AggregateCalculator.SlopedInterpolate(
                requestedTime,
                before,
                after);
        }

        private List<DataValue> BuildProcessedValues(
            HistorianOperationContext context,
            ArchiveState state,
            HistorianProcessedReadRequest request)
        {
            IServerInternal? server = context.SystemContext.Server;
            IAggregateCalculator? calculator = (server?.AggregateManager
                .CreateCalculator(
                    request.AggregateId,
                    request.StartTime,
                    request.EndTime,
                    request.ProcessingInterval,
                    m_options.Capabilities.Stepped,
                    request.Configuration)) ??
                throw new ServiceResultException(
                    StatusCodes.BadAggregateNotSupported);

            NodeArchive? archive = state.TryGetArchive(request.NodeId);
            var result = new List<DataValue>();
            if (archive != null)
            {
                foreach (DataValue value in archive.Raw.Values)
                {
                    if (!calculator.QueueRawValue(value))
                    {
                        FlushCalculator(calculator, result, partial: false);
                    }
                }
            }
            FlushCalculator(calculator, result, partial: true);
            return result;
        }

        private static List<DataValue> BuildAnnotationCounts(
            ArchiveState state,
            HistorianProcessedReadRequest request,
            CancellationToken ct)
        {
            if (double.IsNaN(request.ProcessingInterval) ||
                double.IsInfinity(request.ProcessingInterval) ||
                request.ProcessingInterval <= 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadAggregateInvalidInputs);
            }
            NodeArchive? archive = state.TryGetArchive(request.NodeId);
            bool forward = request.StartTime <= request.EndTime;
            DateTimeUtc cursor = request.StartTime;
            var values = new List<DataValue>();
            while (forward ? cursor < request.EndTime : cursor > request.EndTime)
            {
                ct.ThrowIfCancellationRequested();
                if (values.Count >= kMaxProcessedValues)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations);
                }
                DateTimeUtc next;
                try
                {
                    next = cursor.AddMilliseconds(
                        forward
                            ? request.ProcessingInterval
                            : -request.ProcessingInterval);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadAggregateInvalidInputs,
                        "The annotation-count interval does not produce a valid timestamp.",
                        exception);
                }
                if (next == cursor)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadAggregateInvalidInputs,
                        "The annotation-count interval does not advance.");
                }
                if ((forward && next > request.EndTime) ||
                    (!forward && next < request.EndTime))
                {
                    next = request.EndTime;
                }
                DateTimeUtc lower = forward ? cursor : next;
                DateTimeUtc upper = forward ? next : cursor;
                int count = archive?.Annotations.Count(entry =>
                    entry.Key >= lower && entry.Key < upper) ??
                    0;
                values.Add(new DataValue(
                    Variant.From(count),
                    StatusCodes.Good,
                    cursor,
                    DateTimeUtc.MinValue));
                cursor = next;
            }
            return values;
        }

        private static void FlushCalculator(
            IAggregateCalculator calculator,
            List<DataValue> output,
            bool partial)
        {
            while (calculator.TryGetProcessedValue(
                partial,
                out DataValue computed))
            {
                if (output.Count >= kMaxProcessedValues)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations);
                }
                output.Add(computed);
            }
        }

        private HistorianPage<T> CreatePage<T>(
            List<T> values,
            Guid generation,
            ReadKind kind,
            HistorianResumeToken token,
            uint requestedMax)
        {
            int offset = token.IsEmpty
                ? 0
                : DecodePageCursor(token, kind).Offset;
            if (offset < 0 || offset > values.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid);
            }
            uint requestLimit = requestedMax == 0
                ? m_options.MaxValuesPerPage
                : requestedMax;
            uint configuredLimit = Math.Min(
                requestLimit,
                m_options.MaxValuesPerPage);
            int count = Math.Min(
                values.Count - offset,
                (int)configuredLimit);
            var page = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                page.Add(values[offset + i]);
            }
            int nextOffset = offset + count;
            HistorianResumeToken next = nextOffset < values.Count
                ? EncodePageCursor(new PageCursor(generation, kind, nextOffset))
                : default;
            return new HistorianPage<T>(page, next);
        }

        private async ValueTask PinPageGenerationAsync<T>(
            HistorianPage<T> page,
            Guid generation,
            CancellationToken ct)
        {
            if (!page.IsFinal && generation != Guid.Empty)
            {
                await PinGenerationAsync(generation, ct)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask PinGenerationAsync(
            Guid generation,
            CancellationToken ct)
        {
            string key = GenerationPinKey(generation);
            if ((await m_store.TryGetAsync(
                GenerationGcIntentKey(generation),
                ct).ConfigureAwait(false)).Found)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid,
                    "The historian generation is being collected.");
            }
            DateTimeOffset expiresAt = m_timeProvider.GetUtcNow()
                .Add(m_options.ContinuationRetentionTime);
            ByteString record = EncodeGenerationPin(expiresAt);
            bool pinned = false;
            for (int attempt = 0; attempt < kMaxPublishAttempts; attempt++)
            {
                (bool found, ByteString current) = await m_store.TryGetAsync(
                    key,
                    ct).ConfigureAwait(false);
                if (found &&
                    DecodeGenerationPin(current) >= expiresAt)
                {
                    pinned = true;
                    break;
                }
                if (await CompareAndSwapResolvedAsync(
                    key,
                    found ? current : default,
                    record,
                    ct).ConfigureAwait(false))
                {
                    pinned = true;
                    break;
                }
            }
            if (!pinned)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyOperations,
                    "The historian generation pin could not be renewed.");
            }
            (bool collecting, _) = await m_store.TryGetAsync(
                GenerationGcIntentKey(generation),
                ct).ConfigureAwait(false);
            if (collecting)
            {
                _ = await m_store.DeleteAsync(key, CancellationToken.None)
                    .ConfigureAwait(false);
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid,
                    "The historian generation began collection while it was being pinned.");
            }
        }

        private ByteString EncodeGenerationPin(DateTimeOffset expiresAt)
        {
            byte[] payload = new byte[sizeof(int) + sizeof(long)];
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(),
                kGenerationPinFormatVersion);
            BinaryPrimitives.WriteInt64LittleEndian(
                payload.AsSpan(sizeof(int)),
                expiresAt.UtcTicks);
            return Protect(ByteString.From(payload));
        }

        private DateTimeOffset DecodeGenerationPin(ByteString record)
        {
            ByteString plaintext = Unprotect(record);
            byte[] payload = plaintext.ToArray();
            if (payload.Length != sizeof(int) + sizeof(long) ||
                BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan()) !=
                    kGenerationPinFormatVersion)
            {
                throw CorruptRecord(
                    "The historian generation pin is invalid.");
            }
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(
                payload.AsSpan(sizeof(int)));
            if (ticks <= DateTimeOffset.MinValue.UtcTicks ||
                ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                throw CorruptRecord(
                    "The historian generation pin expiry is invalid.");
            }
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        private static HistoricalDataValue MissingBound(DateTimeUtc timestamp)
        {
            return new HistoricalDataValue(
                new DataValue(
                    Variant.Null,
                    StatusCodes.BadBoundNotFound,
                    timestamp,
                    DateTimeUtc.MinValue),
                IsBound: true);
        }

        private List<Mutation> BuildSnapshotMutations(ArchiveState state)
        {
            var mutations = new List<Mutation>();
            foreach (KeyValuePair<NodeId, NodeArchive> node in state.Nodes)
            {
                foreach (KeyValuePair<HistoricalValueKey, DataValue> value in
                    node.Value.Raw)
                {
                    mutations.Add(Mutation.SetRaw(
                        node.Key,
                        value.Key,
                        value.Value));
                }
                foreach (ModifiedEntry modified in node.Value.Modified)
                {
                    mutations.Add(Mutation.AddModified(
                        node.Key,
                        modified));
                }
                foreach (Annotation annotation in
                    node.Value.Annotations.Values)
                {
                    mutations.Add(Mutation.SetAnnotation(
                        node.Key,
                        annotation));
                }
                foreach (EventEntry entry in node.Value.Events)
                {
                    mutations.Add(Mutation.SetEvent(node.Key, entry));
                }
            }
            return mutations;
        }

        private ByteString EncodeManifest(Manifest manifest)
        {
            IServiceMessageContext context = GetMessageContext();
            using var encoder = new BinaryEncoder(context);
            encoder.WriteInt32(null, kManifestFormatVersion);
            encoder.WriteString(null, ProviderId);
            encoder.WriteByteString(
                null,
                manifest.WriterId == Guid.Empty
                    ? ByteString.Empty
                    : ByteString.From(
                        manifest.WriterId.ToByteArray()));
            encoder.WriteInt64(null, manifest.WriterEpoch);
            encoder.WriteByteString(
                null,
                ByteString.From(manifest.Generation.ToByteArray()));
            encoder.WriteInt64(null, manifest.GenerationNumber);
            encoder.WriteInt64(null, manifest.NextSequence);
            encoder.WriteDateTime(null, manifest.CreatedAt);
            encoder.WriteInt32(null, manifest.Segments.Count);
            foreach (string segment in manifest.Segments)
            {
                encoder.WriteString(null, segment);
            }
            return CloseEncoder(encoder);
        }

        private Manifest DecodeManifest(ByteString plaintext)
        {
            try
            {
                using var decoder = new BinaryDecoder(
                    plaintext.ToArray(),
                    GetMessageContext());
                if (decoder.ReadInt32(null) != kManifestFormatVersion)
                {
                    throw CorruptRecord(
                        "The historian manifest version is unsupported.");
                }
                string? providerId = decoder.ReadString(null);
                if (!string.Equals(
                    providerId,
                    ProviderId,
                    StringComparison.Ordinal))
                {
                    throw CorruptRecord(
                        "The historian manifest belongs to another archive identity.");
                }
                ByteString writerBytes =
                    decoder.ReadByteString(null);
                long writerEpoch = decoder.ReadInt64(null);
                if (writerEpoch < 0 ||
                    (writerEpoch == 0 && !writerBytes.IsEmpty) ||
                    (writerEpoch > 0 && writerBytes.Length != 16))
                {
                    throw CorruptRecord(
                        "The historian writer fence is invalid.");
                }
                ByteString generationBytes = decoder.ReadByteString(null);
                if (generationBytes.Length != 16)
                {
                    throw CorruptRecord(
                        "The historian manifest generation is invalid.");
                }
                long generationNumber = decoder.ReadInt64(null);
                long nextSequence = decoder.ReadInt64(null);
                DateTimeUtc createdAt = decoder.ReadDateTime(null);
                int count = decoder.ReadInt32(null);
                if (generationNumber < 0 ||
                    nextSequence < 0 ||
                    count < 0 ||
                    count > m_options.MaxSegments)
                {
                    throw CorruptRecord(
                        "The historian manifest exceeds configured limits.");
                }
                var segments = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    string? segment = decoder.ReadString(null);
                    if (segment == null ||
                        segment.Length != 32 ||
                        !Guid.TryParseExact(segment, "N", out _))
                    {
                        throw CorruptRecord(
                            "The historian manifest contains an invalid segment.");
                    }
                    segments.Add(segment);
                }
                return new Manifest
                {
                    WriterId = writerEpoch == 0
                        ? Guid.Empty
                        : new Guid(writerBytes.ToArray()),
                    WriterEpoch = writerEpoch,
                    Generation = new Guid(generationBytes.ToArray()),
                    GenerationNumber = generationNumber,
                    NextSequence = nextSequence,
                    CreatedAt = createdAt,
                    Segments = segments
                };
            }
            catch (ServiceResultException)
            {
                throw;
            }
            catch (Exception exception) when (IsDecodeException(exception))
            {
                throw CorruptRecord(
                    "The historian manifest could not be decoded: " +
                    $"{exception.GetType().Name}: {exception.Message}",
                    exception);
            }
        }

        private ByteString EncodeSegment(
            List<Mutation> records,
            int offset,
            int count)
        {
            IServiceMessageContext context = GetMessageContext();
            using var encoder = new BinaryEncoder(context);
            encoder.WriteInt32(null, kSegmentFormatVersion);
            encoder.WriteDateTime(
                null,
                m_timeProvider.GetUtcNow().UtcDateTime);
            encoder.WriteInt32(null, count);
            for (int i = 0; i < count; i++)
            {
                WriteMutation(encoder, records[offset + i]);
            }
            ByteString payload = CloseEncoder(encoder);
            if (payload.Length > m_options.MaxRecordBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "A historian segment exceeds the configured byte limit.");
            }
            return payload;
        }

        private void DecodeAndApplySegment(
            ByteString plaintext,
            ArchiveState state)
        {
            try
            {
                using var decoder = new BinaryDecoder(
                    plaintext.ToArray(),
                    GetMessageContext());
                if (decoder.ReadInt32(null) != kSegmentFormatVersion)
                {
                    throw CorruptRecord(
                        "The historian segment version is unsupported.");
                }
                _ = decoder.ReadDateTime(null);
                int count = decoder.ReadInt32(null);
                if (count < 0 || count > m_options.MaxRecordsPerSegment)
                {
                    throw CorruptRecord(
                        "The historian segment exceeds configured limits.");
                }
                for (int i = 0; i < count; i++)
                {
                    ApplyMutation(state, ReadMutation(decoder));
                }
            }
            catch (ServiceResultException)
            {
                throw;
            }
            catch (Exception exception) when (IsDecodeException(exception))
            {
                throw CorruptRecord(
                    "A historian segment could not be decoded.",
                    exception);
            }
        }

        private static void WriteMutation(
            BinaryEncoder encoder,
            Mutation mutation)
        {
            encoder.WriteInt32(null, (int)mutation.Kind);
            encoder.WriteNodeId(null, mutation.NodeId);
            switch (mutation.Kind)
            {
                case MutationKind.SetRaw:
                    WriteRecordKey(encoder, mutation.Key);
                    DataValue rawValue = mutation.DataValue;
                    encoder.WriteDataValue(null, in rawValue);
                    break;
                case MutationKind.DeleteRaw:
                    WriteRecordKey(encoder, mutation.Key);
                    break;
                case MutationKind.AddModified:
                    encoder.WriteInt64(null, mutation.Sequence);
                    DataValue modifiedValue = mutation.DataValue;
                    encoder.WriteDataValue(null, in modifiedValue);
                    WriteModificationInfo(encoder, mutation.ModificationInfo!);
                    break;
                case MutationKind.DeleteModifiedRange:
                    encoder.WriteDateTime(null, mutation.StartTime);
                    encoder.WriteDateTime(null, mutation.EndTime);
                    break;
                case MutationKind.SetAnnotation:
                    WriteAnnotation(encoder, mutation.Annotation!);
                    break;
                case MutationKind.DeleteAnnotation:
                    encoder.WriteDateTime(null, mutation.StartTime);
                    break;
                case MutationKind.SetEvent:
                    encoder.WriteInt64(null, mutation.Sequence);
                    WriteEvent(encoder, mutation.Event!);
                    break;
                case MutationKind.DeleteEvent:
                    encoder.WriteByteString(null, mutation.EventId);
                    break;
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError);
            }
        }

        private static Mutation ReadMutation(BinaryDecoder decoder)
        {
            var kind = (MutationKind)decoder.ReadInt32(null);
            NodeId nodeId = decoder.ReadNodeId(null);
            if (nodeId.IsNull)
            {
                throw CorruptRecord("A historian mutation has no NodeId.");
            }
            return kind switch
            {
                MutationKind.SetRaw => Mutation.SetRaw(
                    nodeId,
                    ReadRecordKey(decoder),
                    decoder.ReadDataValue(null)),
                MutationKind.DeleteRaw => Mutation.DeleteRaw(
                    nodeId,
                    ReadRecordKey(decoder)),
                MutationKind.AddModified => Mutation.AddModified(
                    nodeId,
                    ReadModifiedEntry(decoder)),
                MutationKind.DeleteModifiedRange =>
                    Mutation.DeleteModifiedRange(
                        nodeId,
                        decoder.ReadDateTime(null),
                        decoder.ReadDateTime(null)),
                MutationKind.SetAnnotation => Mutation.SetAnnotation(
                    nodeId,
                    ReadAnnotation(decoder)),
                MutationKind.DeleteAnnotation => Mutation.DeleteAnnotation(
                    nodeId,
                    decoder.ReadDateTime(null)),
                MutationKind.SetEvent => Mutation.SetEvent(
                    nodeId,
                    ReadEventEntry(decoder)),
                MutationKind.DeleteEvent => Mutation.DeleteEvent(
                    nodeId,
                    decoder.ReadByteString(null)),
                _ => throw CorruptRecord(
                    "The historian mutation kind is invalid.")
            };
        }

        private static void ApplyMutation(
            ArchiveState state,
            Mutation mutation)
        {
            NodeArchive archive = state.GetOrCreateArchive(mutation.NodeId);
            switch (mutation.Kind)
            {
                case MutationKind.SetRaw:
                    archive.Raw[mutation.Key] = mutation.DataValue;
                    break;
                case MutationKind.DeleteRaw:
                    archive.Raw.Remove(mutation.Key);
                    break;
                case MutationKind.AddModified:
                    archive.Modified.Add(new ModifiedEntry(
                        mutation.DataValue,
                        CloneInfo(mutation.ModificationInfo!),
                        mutation.Sequence));
                    break;
                case MutationKind.DeleteModifiedRange:
                    archive.Modified.RemoveAll(entry =>
                        entry.Value.SourceTimestamp >= mutation.StartTime &&
                        entry.Value.SourceTimestamp < mutation.EndTime);
                    break;
                case MutationKind.SetAnnotation:
                    Annotation annotation = CloneAnnotation(
                        mutation.Annotation!);
                    archive.Annotations[annotation.AnnotationTime] = annotation;
                    break;
                case MutationKind.DeleteAnnotation:
                    archive.Annotations.Remove(mutation.StartTime);
                    break;
                case MutationKind.SetEvent:
                    HistorianEventRecord record = CloneEvent(mutation.Event!);
                    int index = archive.Events.FindIndex(
                        entry => entry.Record.EventId == record.EventId);
                    var eventEntry = new EventEntry(record, mutation.Sequence);
                    if (index >= 0)
                    {
                        archive.Events[index] = eventEntry;
                    }
                    else
                    {
                        archive.Events.Add(eventEntry);
                    }
                    break;
                case MutationKind.DeleteEvent:
                    archive.Events.RemoveAll(
                        entry => entry.Record.EventId == mutation.EventId);
                    break;
                default:
                    throw CorruptRecord(
                        "The historian mutation kind is invalid.");
            }
        }

        private static void WriteRecordKey(
            BinaryEncoder encoder,
            HistoricalValueKey key)
        {
            encoder.WriteDateTime(null, key.SourceTimestamp);
            encoder.WriteByteString(null, key.UniquenessKey);
        }

        private static HistoricalValueKey ReadRecordKey(BinaryDecoder decoder)
        {
            DateTimeUtc timestamp = decoder.ReadDateTime(null);
            ByteString discriminator = decoder.ReadByteString(null);
            if (discriminator.Length > kMaxDiscriminatorBytes)
            {
                throw CorruptRecord(
                    "The historian record discriminator is too large.");
            }
            return new HistoricalValueKey(timestamp, discriminator);
        }

        private static void WriteModificationInfo(
            BinaryEncoder encoder,
            ModificationInfo info)
        {
            encoder.WriteDateTime(null, info.ModificationTime);
            encoder.WriteInt32(null, (int)info.UpdateType);
            encoder.WriteString(null, info.UserName);
        }

        private static ModifiedEntry ReadModifiedEntry(BinaryDecoder decoder)
        {
            long sequence = decoder.ReadInt64(null);
            if (sequence <= 0)
            {
                throw CorruptRecord(
                    "The historian modification sequence is invalid.");
            }
            DataValue value = decoder.ReadDataValue(null);
            var info = new ModificationInfo
            {
                ModificationTime = decoder.ReadDateTime(null),
                UpdateType = (HistoryUpdateType)decoder.ReadInt32(null),
                UserName = decoder.ReadString(null)
            };
            return new ModifiedEntry(value, info, sequence);
        }

        private static void WriteAnnotation(
            BinaryEncoder encoder,
            Annotation annotation)
        {
            encoder.WriteString(null, annotation.Message);
            encoder.WriteString(null, annotation.UserName);
            encoder.WriteDateTime(null, annotation.AnnotationTime);
        }

        private static Annotation ReadAnnotation(BinaryDecoder decoder)
        {
            return new Annotation
            {
                Message = decoder.ReadString(null),
                UserName = decoder.ReadString(null),
                AnnotationTime = decoder.ReadDateTime(null)
            };
        }

        private static void WriteEvent(
            BinaryEncoder encoder,
            HistorianEventRecord record)
        {
            encoder.WriteByteString(null, record.EventId);
            encoder.WriteNodeId(null, record.EventType);
            encoder.WriteDateTime(null, record.SourceTimestamp);
            if (record.Fields.Count > kMaxEventFields)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded);
            }
            encoder.WriteInt32(null, record.Fields.Count);
            KeyValuePair<string, Variant>[] fields =
                record.Fields.ToArray() ?? [];
            Array.Sort(
                fields,
                static (left, right) => string.CompareOrdinal(
                    left.Key,
                    right.Key));
            foreach (KeyValuePair<string, Variant> field in fields)
            {
                encoder.WriteString(null, field.Key);
                Variant value = field.Value;
                encoder.WriteVariant(null, in value);
            }
            if (record.QualifiedFields.Count > kMaxEventFields)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded);
            }
            encoder.WriteInt32(null, record.QualifiedFields.Count);
            KeyValuePair<HistorianEventFieldKey, Variant>[] qualifiedFields =
                record.QualifiedFields.ToArray() ?? [];
            Array.Sort(
                qualifiedFields,
                static (left, right) => string.CompareOrdinal(
                    EventFieldSortKey(left.Key),
                    EventFieldSortKey(right.Key)));
            foreach (KeyValuePair<HistorianEventFieldKey, Variant> field in
                qualifiedFields)
            {
                encoder.WriteNodeId(null, field.Key.TypeDefinitionId);
                encoder.WriteInt32(null, field.Key.BrowsePath.Count);
                foreach (QualifiedName browseName in field.Key.BrowsePath)
                {
                    encoder.WriteQualifiedName(null, browseName);
                }
                encoder.WriteUInt32(null, field.Key.AttributeId);
                encoder.WriteString(null, field.Key.IndexRange);
                Variant value = field.Value;
                encoder.WriteVariant(null, in value);
            }
        }

        private static HistorianEventRecord ReadEvent(BinaryDecoder decoder)
        {
            ByteString eventId = decoder.ReadByteString(null);
            NodeId eventType = decoder.ReadNodeId(null);
            DateTimeUtc sourceTimestamp = decoder.ReadDateTime(null);
            int fieldCount = decoder.ReadInt32(null);
            if (eventId.IsEmpty ||
                fieldCount < 0 ||
                fieldCount > kMaxEventFields)
            {
                throw CorruptRecord(
                    "The historian event record is invalid.");
            }
            var fields = new Dictionary<string, Variant>(
                fieldCount,
                StringComparer.Ordinal);
            for (int i = 0; i < fieldCount; i++)
            {
                string? name = decoder.ReadString(null);
                if (string.IsNullOrEmpty(name) ||
                    !fields.TryAdd(name, decoder.ReadVariant(null)))
                {
                    throw CorruptRecord(
                        "The historian event contains duplicate fields.");
                }
            }
            int qualifiedCount = decoder.ReadInt32(null);
            if (qualifiedCount is < 0 or > kMaxEventFields)
            {
                throw CorruptRecord(
                    "The historian event field count is invalid.");
            }
            var qualified =
                new Dictionary<HistorianEventFieldKey, Variant>(
                    qualifiedCount);
            for (int i = 0; i < qualifiedCount; i++)
            {
                NodeId typeDefinitionId = decoder.ReadNodeId(null);
                int pathCount = decoder.ReadInt32(null);
                if (pathCount is < 0 or > kMaxBrowsePathElements)
                {
                    throw CorruptRecord(
                        "The historian event browse path is invalid.");
                }
                var browsePath = new QualifiedName[pathCount];
                for (int j = 0; j < pathCount; j++)
                {
                    browsePath[j] = decoder.ReadQualifiedName(null);
                }
                var key = new HistorianEventFieldKey(
                    typeDefinitionId,
                    browsePath.ToArrayOf(),
                    decoder.ReadUInt32(null),
                    decoder.ReadString(null));
                if (!qualified.TryAdd(key, decoder.ReadVariant(null)))
                {
                    throw CorruptRecord(
                        "The historian event contains duplicate qualified fields.");
                }
            }
            return new HistorianEventRecord(
                eventId,
                eventType,
                sourceTimestamp,
                fields.ToArrayOf())
            {
                QualifiedFields = qualified.ToArrayOf()
            };
        }

        private static EventEntry ReadEventEntry(BinaryDecoder decoder)
        {
            long sequence = decoder.ReadInt64(null);
            if (sequence <= 0)
            {
                throw CorruptRecord(
                    "The historian event sequence is invalid.");
            }
            return new EventEntry(ReadEvent(decoder), sequence);
        }

        private static string EventFieldSortKey(HistorianEventFieldKey key)
        {
            return string.Concat(
                key.TypeDefinitionId,
                "|",
                HistorianEventFieldKey.BuildPath(key.BrowsePath),
                "|",
                key.AttributeId,
                "|",
                key.IndexRange);
        }

        private ByteString Protect(ByteString plaintext)
        {
            if (plaintext.IsEmpty ||
                plaintext.Length > m_options.MaxRecordBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded);
            }
            ByteString record = m_protector.Protect(plaintext);
            if (record.IsEmpty || record.Length > m_options.MaxRecordBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The protected historian record is invalid or too large.");
            }
            return record;
        }

        private ByteString Unprotect(ByteString record)
        {
            if (record.IsEmpty ||
                record.Length > m_options.MaxRecordBytes ||
                !m_protector.TryUnprotect(record, out ByteString plaintext) ||
                plaintext.IsEmpty ||
                plaintext.Length > m_options.MaxRecordBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Historian record authentication failed.");
            }
            return plaintext;
        }

        private IServiceMessageContext GetMessageContext()
        {
            lock (m_contextLock)
            {
                return m_messageContext ??
                    throw new InvalidOperationException(
                        "The historian provider has not been initialized with the server message context.");
            }
        }

        private static ByteString CloseEncoder(BinaryEncoder encoder)
        {
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer == null ? ByteString.Empty : ByteString.From(buffer);
        }

        private static HistorianResumeToken EncodePageCursor(PageCursor cursor)
        {
            byte[] buffer = new byte[
                sizeof(int) +
                sizeof(int) +
                16 +
                sizeof(int)];
            Span<byte> span = buffer;
            BinaryPrimitives.WriteInt32LittleEndian(
                span,
                kPageCursorFormatVersion);
            BinaryPrimitives.WriteInt32LittleEndian(
                span[sizeof(int)..],
                (int)cursor.Kind);
            cursor.Generation.ToByteArray().AsSpan().CopyTo(
                span[(2 * sizeof(int))..]);
            BinaryPrimitives.WriteInt32LittleEndian(
                span[((2 * sizeof(int)) + 16)..],
                cursor.Offset);
            return new HistorianResumeToken(ByteString.From(buffer));
        }

        private static PageCursor DecodePageCursor(
            HistorianResumeToken token,
            ReadKind expectedKind)
        {
            ReadOnlySpan<byte> span = token.State.Span;
            const int expectedLength = (3 * sizeof(int)) + 16;
            if (span.Length != expectedLength ||
                BinaryPrimitives.ReadInt32LittleEndian(span) !=
                    kPageCursorFormatVersion)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid);
            }
            var kind = (ReadKind)BinaryPrimitives.ReadInt32LittleEndian(
                span[sizeof(int)..]);
            var generation = new Guid(
                span.Slice(2 * sizeof(int), 16).ToArray());
            int offset = BinaryPrimitives.ReadInt32LittleEndian(
                span[((2 * sizeof(int)) + 16)..]);
            if (kind != expectedKind ||
                generation == Guid.Empty ||
                offset <= 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid);
            }
            return new PageCursor(generation, kind, offset);
        }

        private void ScheduleCleanup(
            IEnumerable<string> keysOrSegmentIds,
            TimeSpan delay)
        {
            DateTimeOffset due = m_timeProvider.GetUtcNow().Add(delay);
            foreach (string value in keysOrSegmentIds)
            {
                string key = value.StartsWith(kPrefix, StringComparison.Ordinal)
                    ? value
                    : SegmentKey(value);
                if (!m_cleanupChannel.Writer.TryWrite(
                    new CleanupItem(key, due, 0)))
                {
                    return;
                }
            }
        }

        private async Task DrainCleanupAsync(CancellationToken ct)
        {
            var pending = new List<CleanupItem>();
            while (!ct.IsCancellationRequested)
            {
                while (m_cleanupChannel.Reader.TryRead(out CleanupItem item))
                {
                    pending.Add(item);
                }
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (pending[i].DueAt <= now)
                    {
                        CleanupItem due = pending[i];
                        pending.RemoveAt(i);
                        try
                        {
                            await DeleteCleanupItemAsync(due, ct)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            if (IsRetryableGarbageCollectionException(
                                exception))
                            {
                                pending.Add(due with
                                {
                                    DueAt = now.Add(
                                        GetGarbageCollectionRetryDelay(
                                            due.Attempt)),
                                    Attempt = due.Attempt + 1
                                });
                            }
                            else
                            {
                                SetGarbageCollectionFailure(exception);
                            }
                        }
                    }
                }
                if (m_cleanupChannel.Reader.Completion.IsCompleted &&
                    pending.Count == 0)
                {
                    return;
                }
                if (pending.Count == 0)
                {
                    if (!await m_cleanupChannel.Reader.WaitToReadAsync(ct)
                        .ConfigureAwait(false))
                    {
                        return;
                    }
                    continue;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), ct)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask DeleteCleanupItemAsync(
            CleanupItem item,
            CancellationToken ct)
        {
            if (item.Key.StartsWith(
                kManifestPrefix,
                StringComparison.Ordinal) &&
                Guid.TryParseExact(
                    item.Key[kManifestPrefix.Length..],
                    "N",
                    out Guid generation))
            {
                StoredManifest current =
                    await LoadCurrentManifestAsync(ct).ConfigureAwait(false);
                if (generation == current.Manifest.Generation)
                {
                    _ = await m_store.DeleteAsync(
                        GenerationGcIntentKey(generation),
                        ct).ConfigureAwait(false);
                    return;
                }
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                DateTimeOffset? pinExpiry =
                    await TryGetGenerationPinExpirationAsync(
                        generation,
                        ct).ConfigureAwait(false);
                if (pinExpiry > now)
                {
                    _ = await m_store.DeleteAsync(
                        GenerationGcIntentKey(generation),
                        ct).ConfigureAwait(false);
                    ScheduleCleanup(
                        [item.Key],
                        pinExpiry.Value - now);
                    return;
                }
                string intentKey = GenerationGcIntentKey(generation);
                (bool intentFound, ByteString intentRecord) =
                    await m_store.TryGetAsync(intentKey, ct)
                        .ConfigureAwait(false);
                TimeSpan intentGrace =
                    m_options.GarbageCollectionGraceTime >
                        TimeSpan.FromSeconds(1)
                    ? m_options.GarbageCollectionGraceTime
                    : TimeSpan.FromSeconds(1);
                if (!intentFound)
                {
                    ByteString created = EncodeGenerationPin(now);
                    _ = await CompareAndSwapResolvedAsync(
                        intentKey,
                        default,
                        created,
                        ct).ConfigureAwait(false);
                    ScheduleCleanup([item.Key], intentGrace);
                    return;
                }
                DateTimeOffset intentCreated =
                    DecodeGenerationPin(intentRecord);
                if (intentCreated.Add(intentGrace) > now)
                {
                    ScheduleCleanup(
                        [item.Key],
                        intentCreated.Add(intentGrace) - now);
                    return;
                }
                pinExpiry = await TryGetGenerationPinExpirationAsync(
                    generation,
                    ct).ConfigureAwait(false);
                if (pinExpiry > now)
                {
                    _ = await m_store.DeleteAsync(intentKey, ct)
                        .ConfigureAwait(false);
                    ScheduleCleanup([item.Key], pinExpiry.Value - now);
                    return;
                }

                if (!await DeleteWithRetriesAsync(
                        item.Key,
                        throwOnFailure: false,
                        ct).ConfigureAwait(false) ||
                    !await DeleteWithRetriesAsync(
                        GenerationPinKey(generation),
                        throwOnFailure: false,
                        ct).ConfigureAwait(false) ||
                    !await DeleteWithRetriesAsync(
                        intentKey,
                        throwOnFailure: false,
                        ct).ConfigureAwait(false))
                {
                    throw new TimeoutException(
                        "Historian generation cleanup will be retried.");
                }
                return;
            }
            else if (item.Key.StartsWith(
                kSegmentPrefix,
                StringComparison.Ordinal))
            {
                string id = item.Key[kSegmentPrefix.Length..];
                if (await IsSegmentReachableAsync(id, ct)
                    .ConfigureAwait(false))
                {
                    ScheduleCleanup(
                        [item.Key],
                        TimeSpan.FromMinutes(1));
                    return;
                }
            }

            if (!await DeleteWithRetriesAsync(
                    item.Key,
                    throwOnFailure: false,
                    ct).ConfigureAwait(false))
            {
                throw new TimeoutException(
                    "Historian cleanup will be retried.");
            }
        }

        private async ValueTask<bool> IsSegmentReachableAsync(
            string segmentId,
            CancellationToken ct)
        {
            StoredManifest current = await LoadCurrentManifestAsync(ct)
                .ConfigureAwait(false);
            if (current.Manifest.Segments.Contains(segmentId))
            {
                return true;
            }
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(kManifestPrefix, ct).ConfigureAwait(false))
            {
                Manifest manifest = DecodeManifest(Unprotect(entry.Value));
                if (!manifest.Segments.Contains(segmentId))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        private async ValueTask<DateTimeOffset?>
            TryGetGenerationPinExpirationAsync(
                Guid generation,
                CancellationToken ct)
        {
            if (generation == Guid.Empty)
            {
                return null;
            }
            (bool found, ByteString record) = await m_store.TryGetAsync(
                GenerationPinKey(generation),
                ct).ConfigureAwait(false);
            return found ? DecodeGenerationPin(record) : null;
        }

        private async ValueTask<bool> DeleteWithRetriesAsync(
            string key,
            bool throwOnFailure,
            CancellationToken ct)
        {
            Exception? failure = null;
            for (int attempt = 0; attempt < kGarbageCollectionAttempts; attempt++)
            {
                try
                {
                    _ = await m_store.DeleteAsync(key, ct)
                        .ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    if (attempt + 1 < kGarbageCollectionAttempts)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(100),
                            ct).ConfigureAwait(false);
                    }
                }
            }
            var surfaced = new InvalidOperationException(
                $"Historian garbage collection could not delete '{key}'.",
                failure);
            if (throwOnFailure)
            {
                throw surfaced;
            }
            return false;
        }

        private void SetGarbageCollectionFailure(Exception exception)
        {
            lock (m_cleanupFailureLock)
            {
                m_cleanupFailure = exception;
            }
        }

        private void ClearGarbageCollectionFailure()
        {
            lock (m_cleanupFailureLock)
            {
                m_cleanupFailure = null;
            }
        }

        private static bool IsRetryableGarbageCollectionException(
            Exception exception)
        {
            if (exception is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    if (!IsRetryableGarbageCollectionException(inner))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (exception is ServiceResultException serviceException)
            {
                return serviceException.StatusCode !=
                        StatusCodes.BadDecodingError &&
                    serviceException.StatusCode !=
                        StatusCodes.BadSecurityChecksFailed &&
                    serviceException.StatusCode !=
                        StatusCodes.BadEncodingLimitsExceeded;
            }
            return exception is
                TimeoutException or
                IOException or
                InvalidOperationException;
        }

        private static TimeSpan GetGarbageCollectionRetryDelay(int attempt)
        {
            int seconds = 1 << Math.Min(attempt, 6);
            return TimeSpan.FromSeconds(seconds);
        }

        private void ThrowIfGarbageCollectionFailed()
        {
            Exception? failure = GarbageCollectionFailure;
            if (failure != null)
            {
                throw new InvalidOperationException(
                    "Historian garbage collection has a permanent failure.",
                    failure);
            }
        }

        private DateTimeOffset DecodeSegmentCreatedAt(ByteString record)
        {
            ByteString plaintext = Unprotect(record);
            try
            {
                using var decoder = new BinaryDecoder(
                    plaintext.ToArray(),
                    GetMessageContext());
                if (decoder.ReadInt32(null) != kSegmentFormatVersion)
                {
                    throw CorruptRecord(
                        "The historian segment version is unsupported.");
                }
                DateTimeUtc createdAt = decoder.ReadDateTime(null);
                return new DateTimeOffset(
                    createdAt.ToDateTime(),
                    TimeSpan.Zero);
            }
            catch (ServiceResultException)
            {
                throw;
            }
            catch (Exception exception) when (IsDecodeException(exception))
            {
                throw CorruptRecord(
                    "The historian segment header could not be decoded.",
                    exception);
            }
        }

        private static HistorianUpdateOutcome<T> Outcome<T>(
            StatusCode[] statuses,
            List<T>? oldValues = null,
            bool rolledBack = false)
        {
            return new HistorianUpdateOutcome<T>(
                statuses.ToArrayOf(),
                oldValues == null
                    ? []
                    : oldValues.ToArrayOf(),
                transactionRolledBack: rolledBack);
        }

        private static HistorianUpdateOutcome<T> Rejected<T>(
            int count,
            StatusCode status)
        {
            var statuses = new StatusCode[count];
            for (int i = 0; i < statuses.Length; i++)
            {
                statuses[i] = status;
            }
            return Outcome<T>(statuses);
        }

        private static UpdatePlan<T> InvalidPlan<T>(
            int count,
            long nextSequence)
        {
            var statuses = new StatusCode[count];
            for (int i = 0; i < statuses.Length; i++)
            {
                statuses[i] = StatusCodes.BadInvalidArgument;
            }
            return new UpdatePlan<T>(
                Outcome<T>(statuses),
                [],
                nextSequence);
        }

        private static UpdatePlan<T> RollbackPlan<T>(
            int count,
            int failedIndex,
            StatusCode failure,
            long nextSequence)
        {
            var statuses = new StatusCode[count];
            for (int i = 0; i < statuses.Length; i++)
            {
                statuses[i] = StatusCodes.BadTransactionFailed;
            }
            statuses[failedIndex] = failure;
            return new UpdatePlan<T>(
                Outcome<T>(
                    statuses,
                    rolledBack: true),
                [],
                nextSequence);
        }

        private static Annotation CloneAnnotation(Annotation source)
        {
            return new Annotation
            {
                Message = source.Message,
                UserName = source.UserName,
                AnnotationTime = source.AnnotationTime
            };
        }

        private static ModificationInfo CloneInfo(ModificationInfo source)
        {
            return new ModificationInfo
            {
                ModificationTime = source.ModificationTime,
                UpdateType = source.UpdateType,
                UserName = source.UserName
            };
        }

        private static HistorianEventRecord CloneEvent(
            HistorianEventRecord source)
        {
            return new HistorianEventRecord(
                source.EventId,
                source.EventType,
                source.SourceTimestamp,
                source.Fields.ToArray() ?? [])
            {
                QualifiedFields =
                    source.QualifiedFields.ToArray() ?? []
            };
        }

        private static bool TryMergeEvent(
            HistorianEventRecord prior,
            HistorianEventRecord update,
            out HistorianEventRecord merged,
            out StatusCode statusCode)
        {
            var fields = new Dictionary<string, Variant>(
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, Variant> field in prior.Fields)
            {
                fields[field.Key] = field.Value;
            }
            foreach (KeyValuePair<string, Variant> field in update.Fields)
            {
                if (!string.Equals(
                    field.Key,
                    BrowseNames.EventId,
                    StringComparison.Ordinal) &&
                    !HasQualifiedFieldPath(
                        update.QualifiedFields,
                        field.Key))
                {
                    fields[field.Key] = field.Value;
                }
            }
            var qualified =
                new Dictionary<HistorianEventFieldKey, Variant>();
            foreach (KeyValuePair<HistorianEventFieldKey, Variant> field in
                prior.QualifiedFields)
            {
                qualified[field.Key] = field.Value;
            }
            foreach (KeyValuePair<HistorianEventFieldKey, Variant> field in
                update.QualifiedFields)
            {
                if (IsEventIdField(field.Key))
                {
                    continue;
                }
                string path = HistorianEventFieldKey.BuildPath(
                    field.Key.BrowsePath);
                if (string.IsNullOrEmpty(field.Key.IndexRange))
                {
                    qualified[field.Key] = field.Value;
                    fields[path] = field.Value;
                    continue;
                }
                ServiceResult validation = NumericRange.Validate(
                    field.Key.IndexRange,
                    out NumericRange range);
                if (ServiceResult.IsBad(validation))
                {
                    merged = prior;
                    statusCode = StatusCodes.BadIndexRangeInvalid;
                    return false;
                }
                HistorianEventFieldKey targetKey = field.Key with
                {
                    IndexRange = null
                };
                if (!qualified.TryGetValue(
                        targetKey,
                        out Variant target))
                {
                    merged = prior;
                    statusCode = StatusCodes.BadIndexRangeNoData;
                    return false;
                }
                StatusCode updateStatus = range.UpdateRange(
                    ref target,
                    field.Value);
                if (StatusCode.IsBad(updateStatus))
                {
                    merged = prior;
                    statusCode = updateStatus;
                    return false;
                }
                qualified[targetKey] = target;
                fields[path] = target;
            }
            fields[BrowseNames.EventId] = new Variant(prior.EventId);
            merged = new HistorianEventRecord(
                prior.EventId,
                update.EventType.IsNull ? prior.EventType : update.EventType,
                update.SourceTimestamp == DateTimeUtc.MinValue
                    ? prior.SourceTimestamp
                    : update.SourceTimestamp,
                fields.ToArrayOf())
            {
                QualifiedFields = qualified.ToArrayOf()
            };
            statusCode = StatusCodes.Good;
            return true;
        }

        private static bool HasQualifiedFieldPath(
            ArrayOf<KeyValuePair<HistorianEventFieldKey, Variant>> fields,
            string path)
        {
            foreach (KeyValuePair<HistorianEventFieldKey, Variant> field in
                fields)
            {
                if (string.Equals(
                    HistorianEventFieldKey.BuildPath(
                        field.Key.BrowsePath),
                    path,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsEventIdField(
            HistorianEventFieldKey key)
        {
            return key.TypeDefinitionId == ObjectTypeIds.BaseEventType &&
                key.AttributeId == Attributes.Value &&
                key.BrowsePath.Count == 1 &&
                key.BrowsePath[0].NamespaceIndex == 0 &&
                string.Equals(
                    key.BrowsePath[0].Name,
                    BrowseNames.EventId,
                    StringComparison.Ordinal);
        }

        private ArrayOf<HistorianUpdateOutcome<DataValue>>
            RejectBatch(ArrayOf<HistorianDataBatch> batch)
        {
            var result =
                new HistorianUpdateOutcome<DataValue>[batch.Count];
            for (int i = 0; i < batch.Count; i++)
            {
                result[i] = Rejected<DataValue>(
                    batch[i].Values.Count,
                    StatusCodes.BadNotWritable);
            }
            return result;
        }

        private static ServiceResultException CorruptRecord(
            string message,
            Exception? inner = null)
        {
            return inner == null
                ? new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    message)
                : new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    message,
                    inner);
        }

        private static ServiceResultException
            CreateIndeterminateStoreException(
                string message,
                Exception inner)
        {
            return new ServiceResultException(
                StatusCodes.BadCommunicationError,
                message,
                inner);
        }

        private static bool IsIndeterminateStoreOperation(
            ServiceResultException exception)
        {
            return exception.StatusCode == StatusCodes.BadCommunicationError &&
                exception.InnerException != null;
        }

        private static bool IsDecodeException(Exception exception)
        {
            return exception is
                ArgumentException or
                InvalidOperationException or
                EndOfStreamException or
                IOException or
                OverflowException or
                IndexOutOfRangeException;
        }

        private static string SegmentKey(string id)
        {
            return kSegmentPrefix + id;
        }

        private static string ManifestGenerationKey(Guid generation)
        {
            return kManifestPrefix + generation.ToString("N");
        }

        private static string GenerationPinKey(Guid generation)
        {
            return kGenerationPinPrefix + generation.ToString("N");
        }

        private static string GenerationGcIntentKey(Guid generation)
        {
            return kGenerationGcIntentPrefix + generation.ToString("N");
        }

        private const string kPrefix = "historian/v1/";
        private const string kCurrentManifestKey = kPrefix + "manifest/current";
        private const string kManifestPrefix = kPrefix + "manifest/generations/";
        private const string kSegmentPrefix = kPrefix + "segments/";
        private const string kGenerationPinPrefix = kPrefix + "pins/";

        private const string kGenerationGcIntentPrefix =
            kPrefix + "gc/generations/";

        private const int kManifestFormatVersion = 2;
        private const int kSegmentFormatVersion = 2;
        private const int kPageCursorFormatVersion = 1;
        private const int kGenerationPinFormatVersion = 1;
        private const int kMaxPublishAttempts = 8;
        private const int kGarbageCollectionAttempts = 3;
        private const int kMaxDiscriminatorBytes = 64 * 1024;
        private const int kMaxEventFields = 4_096;
        private const int kMaxBrowsePathElements = 128;
        private const int kMaxProcessedValues = 100_000;

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly ILeaderElection m_election;
        private readonly SharedKeyValueHistorianOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly IHistorianFencingAuthority m_fencingAuthority;
        private readonly Lock m_contextLock = new();
        private readonly Lock m_cleanupFailureLock = new();

        private readonly SemaphoreSlim m_writerFenceSemaphore =
            new(1, 1);

        private readonly NodeIdDictionary<IHistorianStructuredDataKeySelector>
            m_structuredKeySelectors = [];

        private readonly Channel<CleanupItem> m_cleanupChannel;
        private readonly CancellationTokenSource m_disposeCts = new();
        private readonly Task m_cleanupTask;
        private int m_disposed;
        private Guid m_writerId;
        private long m_writerEpoch;
        private IServiceMessageContext? m_messageContext;
        private Exception? m_cleanupFailure;

        private enum ReadKind
        {
            Raw = 1,
            Modified = 2,
            Processed = 3,
            Annotation = 4,
            Event = 5
        }

        private enum MutationKind
        {
            SetRaw = 1,
            DeleteRaw = 2,
            AddModified = 3,
            DeleteModifiedRange = 4,
            SetAnnotation = 5,
            DeleteAnnotation = 6,
            SetEvent = 7,
            DeleteEvent = 8
        }

        private sealed class Manifest
        {
            public Guid WriterId { get; init; }

            public long WriterEpoch { get; init; }

            public Guid Generation { get; init; }

            public long GenerationNumber { get; init; }

            public long NextSequence { get; set; }

            public DateTimeUtc CreatedAt { get; init; } = DateTimeUtc.MinValue;

            public List<string> Segments { get; init; } = [];
        }

        private sealed class ArchiveState
        {
            public NodeIdDictionary<NodeArchive> Nodes { get; } = [];

            public NodeArchive GetOrCreateArchive(NodeId nodeId)
            {
                if (!Nodes.TryGetValue(nodeId, out NodeArchive? archive))
                {
                    archive = new NodeArchive();
                    Nodes[nodeId] = archive;
                }
                return archive;
            }

            public NodeArchive? TryGetArchive(NodeId nodeId)
            {
                return Nodes.TryGetValue(nodeId, out NodeArchive? archive)
                    ? archive
                    : null;
            }
        }

        private sealed class NodeArchive
        {
            public SortedDictionary<HistoricalValueKey, DataValue> Raw { get; }
                = new(HistoricalValueKeyComparer.Instance);

            public List<ModifiedEntry> Modified { get; } = [];

            public SortedDictionary<DateTimeUtc, Annotation> Annotations { get; }
                = [];

            public List<EventEntry> Events { get; } = [];
        }

        private sealed record ModifiedEntry(
            DataValue Value,
            ModificationInfo Info,
            long Sequence);

        private sealed record EventEntry(
            HistorianEventRecord Record,
            long Sequence);

        private sealed record Mutation
        {
            public required MutationKind Kind { get; init; }

            public required NodeId NodeId { get; init; }

            public HistoricalValueKey Key { get; init; }

            public DataValue DataValue { get; init; }

            public ModificationInfo? ModificationInfo { get; init; }

            public Annotation? Annotation { get; init; }

            public HistorianEventRecord? Event { get; init; }

            public ByteString EventId { get; init; }

            public DateTimeUtc StartTime { get; init; }

            public DateTimeUtc EndTime { get; init; }

            public long Sequence { get; init; }

            public static Mutation SetRaw(
                NodeId nodeId,
                HistoricalValueKey key,
                DataValue value)
            {
                return new Mutation
                {
                    Kind = MutationKind.SetRaw,
                    NodeId = nodeId,
                    Key = key,
                    DataValue = value
                };
            }

            public static Mutation DeleteRaw(
                NodeId nodeId,
                HistoricalValueKey key)
            {
                return new Mutation
                {
                    Kind = MutationKind.DeleteRaw,
                    NodeId = nodeId,
                    Key = key
                };
            }

            public static Mutation AddModified(
                NodeId nodeId,
                ModifiedEntry entry)
            {
                return new Mutation
                {
                    Kind = MutationKind.AddModified,
                    NodeId = nodeId,
                    DataValue = entry.Value,
                    ModificationInfo = CloneInfo(entry.Info),
                    Sequence = entry.Sequence
                };
            }

            public static Mutation DeleteModifiedRange(
                NodeId nodeId,
                DateTimeUtc startTime,
                DateTimeUtc endTime)
            {
                return new Mutation
                {
                    Kind = MutationKind.DeleteModifiedRange,
                    NodeId = nodeId,
                    StartTime = startTime,
                    EndTime = endTime
                };
            }

            public static Mutation SetAnnotation(
                NodeId nodeId,
                Annotation annotation)
            {
                return new Mutation
                {
                    Kind = MutationKind.SetAnnotation,
                    NodeId = nodeId,
                    Annotation = CloneAnnotation(annotation)
                };
            }

            public static Mutation DeleteAnnotation(
                NodeId nodeId,
                DateTimeUtc annotationTime)
            {
                return new Mutation
                {
                    Kind = MutationKind.DeleteAnnotation,
                    NodeId = nodeId,
                    StartTime = annotationTime
                };
            }

            public static Mutation SetEvent(
                NodeId nodeId,
                EventEntry entry)
            {
                return new Mutation
                {
                    Kind = MutationKind.SetEvent,
                    NodeId = nodeId,
                    Event = CloneEvent(entry.Record),
                    Sequence = entry.Sequence
                };
            }

            public static Mutation DeleteEvent(
                NodeId nodeId,
                ByteString eventId)
            {
                return new Mutation
                {
                    Kind = MutationKind.DeleteEvent,
                    NodeId = nodeId,
                    EventId = eventId
                };
            }
        }

        private readonly record struct StoredManifest(
            Manifest Manifest,
            ByteString Record);

        private readonly record struct ArchiveSnapshot(
            Manifest Manifest,
            ArchiveState State);

        private readonly record struct PageCursor(
            Guid Generation,
            ReadKind Kind,
            int Offset);

        private readonly record struct CleanupItem(
            string Key,
            DateTimeOffset DueAt,
            int Attempt);

        private readonly record struct UpdatePlan<T>(
            HistorianUpdateOutcome<T> Outcome,
            List<Mutation> Mutations,
            long NextSequence);
    }
}
