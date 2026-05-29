/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian.InMemory
{
    /// <summary>
    /// Reference-quality, in-memory implementation of the
    /// <see cref="IHistorianProvider"/> capability bundle. Intended for
    /// tests, samples and demonstration servers. <strong>Not</strong>
    /// suitable for production use: storage is non-persistent and
    /// per-process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider stores per-NodeId archives in
    /// <see cref="SortedDictionary{TKey,TValue}"/> structures keyed by
    /// <c>SourceTimestamp</c> (Part 11 §5.2.4). Each insert/replace is
    /// also logged into a per-NodeId modification list so
    /// <see cref="IHistorianModifiedProvider.ReadModifiedAsync"/> returns
    /// the audit trail.
    /// </para>
    /// <para>
    /// Annotations live in a separate per-NodeId archive keyed by
    /// <see cref="Annotation.AnnotationTime"/> (Part 11 §5.2.7).
    /// </para>
    /// <para>
    /// Concurrency: every operation takes a per-NodeId lock for the
    /// duration of the read or write to keep the data structure
    /// invariants. Reads release the lock once a snapshot of the page
    /// has been built — paginated reads do not hold the lock between
    /// pages.
    /// </para>
    /// <para>
    /// Capabilities: every registered node advertises
    /// <see cref="HistorianNodeCapabilities.ReadWrite"/> unless the
    /// caller supplied an override via
    /// <see cref="SetCapabilities(NodeId, HistorianNodeCapabilities)"/>.
    /// </para>
    /// </remarks>
    public sealed class InMemoryHistorianProvider :
        HistorianProviderBase,
        IHistorianDataProvider,
        IHistorianModifiedProvider,
        IHistorianAnnotationProvider,
        IHistorianTransactionalProvider,
        IHistorianBulkInsertProvider,
        IHistorianEventProvider,
        IDisposable
    {
        /// <summary>Creates a provider with default options.</summary>
        public InMemoryHistorianProvider()
            : this(new InMemoryHistorianOptions())
        {
        }

        /// <summary>Creates a provider with the supplied options.</summary>
        public InMemoryHistorianProvider(InMemoryHistorianOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Disposes the provider, clearing all archived data.</summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_archives.Clear();
                m_capabilities.Clear();
                m_events.Clear();
            }
        }

        /// <summary>
        /// Pre-registers a variable. Equivalent to setting the default
        /// capability set; the archive is also created so the provider
        /// reports <c>true</c> from <see cref="IsHistorizingAsync"/> for
        /// the node before any value is inserted.
        /// </summary>
        public void Register(NodeId nodeId, HistorianNodeCapabilities? capabilities = null)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("NodeId must not be null.", nameof(nodeId));
            }

            lock (m_lock)
            {
                _ = GetOrCreateArchive(nodeId);
                m_capabilities[nodeId] = capabilities ?? m_options.DefaultCapabilities;
            }
        }

        /// <summary>
        /// Overrides the capability set advertised for a node. Subsequent
        /// reads of <see cref="GetCapabilitiesAsync"/> return this set.
        /// </summary>
        public void SetCapabilities(NodeId nodeId, HistorianNodeCapabilities capabilities)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }
            if (nodeId.IsNull)
            {
                throw new ArgumentException("NodeId must not be null.", nameof(nodeId));
            }

            lock (m_lock)
            {
                m_capabilities[nodeId] = capabilities;
            }
        }

        /// <summary>
        /// Removes a node's archive (raw + modified + annotations) along
        /// with any capability override.
        /// </summary>
        public bool Forget(NodeId nodeId)
        {
            lock (m_lock)
            {
                m_capabilities.Remove(nodeId);
                return m_archives.TryRemove(nodeId, out _);
            }
        }

        /// <inheritdoc/>
        public override ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct)
        {
            lock (m_lock)
            {
                return new ValueTask<bool>(m_archives.ContainsKey(nodeId) || m_capabilities.ContainsKey(nodeId));
            }
        }

        /// <inheritdoc/>
        public override ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct)
        {
            lock (m_lock)
            {
                HistorianNodeCapabilities caps = m_capabilities.TryGetValue(nodeId, out HistorianNodeCapabilities? value)
                    ? value
                    : m_options.DefaultCapabilities;
                return new ValueTask<HistorianNodeCapabilities>(caps);
            }
        }

        /// <inheritdoc/>
        public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
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

            lock (m_lock)
            {
                if (!m_archives.TryGetValue(request.NodeId, out NodeArchive? archive))
                {
                    return new ValueTask<HistorianPage<HistoricalDataValue>>(HistorianPage<HistoricalDataValue>.Empty);
                }

                DateTime resumeAt = DecodeTimestamp(resumeToken);
                return new ValueTask<HistorianPage<HistoricalDataValue>>(
                    ReadRawPage(archive, request, resumeAt));
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyUpdate(context, nodeId, values, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Bulk path used by the framework's auto-capture pipeline: acquires
        /// <see cref="m_lock"/> once for the entire <paramref name="batch"/>
        /// rather than once per <see cref="InsertAsync"/> call. Status
        /// semantics match the per-node <see cref="InsertAsync"/> contract.
        /// </remarks>
        public ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>> InsertBatchAsync(
            HistorianOperationContext context,
            IReadOnlyDictionary<NodeId, IList<DataValue>> batch,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (batch == null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            var result = new Dictionary<NodeId, IList<StatusCode>>(batch.Count);
            lock (m_lock)
            {
                foreach (KeyValuePair<NodeId, IList<DataValue>> entry in batch)
                {
                    if (entry.Value == null)
                    {
                        result[entry.Key] = [];
                        continue;
                    }
                    NodeArchive archive = GetOrCreateArchive(entry.Key);
                    var statuses = new StatusCode[entry.Value.Count];
                    for (int i = 0; i < entry.Value.Count; i++)
                    {
                        DataValue value = entry.Value[i];
                        if (value.IsNull)
                        {
                            statuses[i] = StatusCodes.BadInvalidArgument;
                            continue;
                        }

                        var key = value.SourceTimestamp.ToDateTime();
                        if (archive.Raw.ContainsKey(key))
                        {
                            statuses[i] = StatusCodes.BadEntryExists;
                            continue;
                        }
                        archive.Raw[key] = CloneValue(value);
                        statuses[i] = StatusCodes.GoodEntryInserted;
                        EvictIfNeeded(archive);
                    }
                    result[entry.Key] = statuses;
                }
            }
            return new ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>>(result);
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyUpdate(context, nodeId, values, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyUpdate(context, nodeId, values, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyTransactionalUpdate(context, nodeId, values, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyTransactionalUpdate(context, nodeId, values, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyTransactionalUpdate(context, nodeId, values, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> DeleteRawAsync(
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

            lock (m_lock)
            {
                if (!m_archives.TryGetValue(nodeId, out NodeArchive? archive))
                {
                    return new ValueTask<StatusCode>(StatusCodes.GoodNoData);
                }

                var start = startTime.ToDateTime();
                var end = endTime.ToDateTime();
                if (start > end)
                {
                    (start, end) = (end, start);
                }

                int removed = 0;
                if (isDeleteModified)
                {
                    removed = archive.ModifiedLog.RemoveAll(m =>
                        m.Value.SourceTimestamp.ToDateTime() >= start &&
                        m.Value.SourceTimestamp.ToDateTime() < end);
                }
                else
                {
                    List<DateTime> toRemove = [.. archive.Raw.Keys.Where(k => k >= start && k < end)];
                    foreach (DateTime key in toRemove)
                    {
                        DataValue prior = archive.Raw[key];
                        archive.Raw.Remove(key);
                        LogModification(archive, prior, HistoryUpdateType.Delete, context.DefaultModificationInfo);
                        removed++;
                    }
                }

                return new ValueTask<StatusCode>(removed > 0
                    ? StatusCodes.Good
                    : StatusCodes.GoodNoData);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DateTimeUtc> timestamps,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (timestamps == null)
            {
                throw new ArgumentNullException(nameof(timestamps));
            }

            var statuses = new StatusCode[timestamps.Count];
            lock (m_lock)
            {
                if (!m_archives.TryGetValue(nodeId, out NodeArchive? archive))
                {
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                    return new ValueTask<IList<StatusCode>>(statuses);
                }

                for (int i = 0; i < timestamps.Count; i++)
                {
                    var key = timestamps[i].ToDateTime();
                    if (archive.Raw.TryGetValue(key, out DataValue prior))
                    {
                        archive.Raw.Remove(key);
                        LogModification(archive, prior, HistoryUpdateType.Delete, context.DefaultModificationInfo);
                        statuses[i] = StatusCodes.Good;
                    }
                    else
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                }
            }
            return new ValueTask<IList<StatusCode>>(statuses);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianPage<ModifiedDataValue>> ReadModifiedAsync(
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

            lock (m_lock)
            {
                if (!m_archives.TryGetValue(request.NodeId, out NodeArchive? archive))
                {
                    return new ValueTask<HistorianPage<ModifiedDataValue>>(HistorianPage<ModifiedDataValue>.Empty);
                }

                DateTime resumeAt = DecodeTimestamp(resumeToken);
                return new ValueTask<HistorianPage<ModifiedDataValue>>(
                    ReadModifiedPage(archive, request, resumeAt));
            }
        }

        /// <inheritdoc/>
        public ValueTask<HistorianPage<Annotation>> ReadAnnotationsAsync(
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

            lock (m_lock)
            {
                if (!m_archives.TryGetValue(request.NodeId, out NodeArchive? archive))
                {
                    return new ValueTask<HistorianPage<Annotation>>(HistorianPage<Annotation>.Empty);
                }

                DateTime resumeAt = DecodeTimestamp(resumeToken);
                return new ValueTask<HistorianPage<Annotation>>(
                    ReadAnnotationsPage(archive, request, resumeAt));
            }
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<Annotation> annotations,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyAnnotation(nodeId, annotations, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<Annotation> annotations,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyAnnotation(nodeId, annotations, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<Annotation> annotations,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(
                ApplyAnnotation(nodeId, annotations, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> DeleteAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DateTimeUtc> annotationTimes,
            CancellationToken ct)
        {
            if (annotationTimes == null)
            {
                throw new ArgumentNullException(nameof(annotationTimes));
            }

            var statuses = new StatusCode[annotationTimes.Count];
            lock (m_lock)
            {
                if (!m_archives.TryGetValue(nodeId, out NodeArchive? archive))
                {
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                    return new ValueTask<IList<StatusCode>>(statuses);
                }

                for (int i = 0; i < annotationTimes.Count; i++)
                {
                    statuses[i] = archive.Annotations.Remove(annotationTimes[i].ToDateTime())
                        ? StatusCodes.Good
                        : StatusCodes.BadNoEntryExists;
                }
            }
            return new ValueTask<IList<StatusCode>>(statuses);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianPage<HistorianEventRecord>> ReadEventsAsync(
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

            // Snapshot the event list under the lock — record instances are
            // immutable so the pointer copy is sufficient to detach from
            // concurrent mutation. Filtering and paging then happen outside
            // the lock so unrelated nodes are not blocked for the duration
            // of a large iteration.
            HistorianEventRecord[] snapshot;
            lock (m_lock)
            {
                if (!m_events.TryGetValue(request.NodeId, out List<HistorianEventRecord>? list))
                {
                    return new ValueTask<HistorianPage<HistorianEventRecord>>(
                        HistorianPage<HistorianEventRecord>.Empty);
                }
                snapshot = [.. list];
            }

            var start = request.StartTime.ToDateTime();
            var end = request.EndTime.ToDateTime();
            DateTime lo = start <= end ? start : end;
            DateTime hi = start <= end ? end : start;

            uint cap = request.MaxValues > 0 ? request.MaxValues : (uint)snapshot.Length;
            var page = new List<HistorianEventRecord>((int)Math.Min(cap, kMaxValuesPerPage));

            if (request.IsForward)
            {
                for (int i = 0; i < snapshot.Length && page.Count < cap; i++)
                {
                    HistorianEventRecord rec = snapshot[i];
                    var ts = rec.SourceTimestamp.ToDateTime();
                    if (ts < lo || ts >= hi)
                    {
                        continue;
                    }
                    page.Add(rec);
                }
            }
            else
            {
                for (int i = snapshot.Length - 1; i >= 0 && page.Count < cap; i--)
                {
                    HistorianEventRecord rec = snapshot[i];
                    var ts = rec.SourceTimestamp.ToDateTime();
                    if (ts < lo || ts >= hi)
                    {
                        continue;
                    }
                    page.Add(rec);
                }
            }
            return new ValueTask<HistorianPage<HistorianEventRecord>>(
                new HistorianPage<HistorianEventRecord>(page));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> InsertEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(ApplyEventUpdate(nodeId, events, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> ReplaceEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(ApplyEventUpdate(nodeId, events, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> UpdateEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return new ValueTask<IList<StatusCode>>(ApplyEventUpdate(nodeId, events, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<IList<StatusCode>> DeleteEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<ByteString> eventIds,
            CancellationToken ct)
        {
            if (eventIds == null)
            {
                throw new ArgumentNullException(nameof(eventIds));
            }
            var statuses = new StatusCode[eventIds.Count];
            lock (m_lock)
            {
                if (!m_events.TryGetValue(nodeId, out List<HistorianEventRecord>? list))
                {
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                    return new ValueTask<IList<StatusCode>>(statuses);
                }
                for (int i = 0; i < eventIds.Count; i++)
                {
                    ByteString id = eventIds[i];
                    int idx = list.FindIndex(r => r.EventId == id);
                    if (idx >= 0)
                    {
                        list.RemoveAt(idx);
                        statuses[i] = StatusCodes.Good;
                    }
                    else
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                }
            }
            return new ValueTask<IList<StatusCode>>(statuses);
        }

        private StatusCode[] ApplyEventUpdate(
            NodeId nodeId,
            IList<HistorianEventRecord> events,
            HistoryUpdateType updateType)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }
            var statuses = new StatusCode[events.Count];

            lock (m_lock)
            {
                if (!m_events.TryGetValue(nodeId, out List<HistorianEventRecord>? list))
                {
                    list = [];
                    m_events[nodeId] = list;
                }

                for (int i = 0; i < events.Count; i++)
                {
                    HistorianEventRecord rec = events[i];
                    if (rec == null)
                    {
                        statuses[i] = StatusCodes.BadInvalidArgument;
                        continue;
                    }
                    int idx = !rec.EventId.IsEmpty
                        ? list.FindIndex(r => r.EventId == rec.EventId)
                        : -1;
                    switch (updateType)
                    {
                        case HistoryUpdateType.Insert:
                            if (idx >= 0)
                            {
                                statuses[i] = StatusCodes.BadEntryExists;
                            }
                            else
                            {
                                list.Add(rec);
                                statuses[i] = StatusCodes.GoodEntryInserted;
                            }
                            break;
                        case HistoryUpdateType.Replace:
                            if (idx < 0)
                            {
                                statuses[i] = StatusCodes.BadNoEntryExists;
                            }
                            else
                            {
                                list[idx] = rec;
                                statuses[i] = StatusCodes.GoodEntryReplaced;
                            }
                            break;
                        case HistoryUpdateType.Update:
                            if (idx >= 0)
                            {
                                list[idx] = rec;
                                statuses[i] = StatusCodes.GoodEntryReplaced;
                            }
                            else
                            {
                                list.Add(rec);
                                statuses[i] = StatusCodes.GoodEntryInserted;
                            }
                            break;
                        default:
                            statuses[i] = StatusCodes.BadInvalidArgument;
                            break;
                    }
                }
            }
            return statuses;
        }

        private StatusCode[] ApplyUpdate(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            HistoryUpdateType updateType)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var statuses = new StatusCode[values.Count];
            lock (m_lock)
            {
                NodeArchive archive = GetOrCreateArchive(nodeId);

                for (int i = 0; i < values.Count; i++)
                {
                    DataValue value = values[i];
                    if (value.IsNull)
                    {
                        statuses[i] = StatusCodes.BadInvalidArgument;
                        continue;
                    }

                    var key = value.SourceTimestamp.ToDateTime();
                    bool exists = archive.Raw.TryGetValue(key, out DataValue prior);

                    switch (updateType)
                    {
                        case HistoryUpdateType.Insert:
                            if (exists)
                            {
                                statuses[i] = StatusCodes.BadEntryExists;
                            }
                            else
                            {
                                archive.Raw[key] = CloneValue(value);
                                statuses[i] = StatusCodes.GoodEntryInserted;
                                EvictIfNeeded(archive);
                            }
                            break;
                        case HistoryUpdateType.Replace:
                            if (!exists)
                            {
                                statuses[i] = StatusCodes.BadNoEntryExists;
                            }
                            else
                            {
                                LogModification(archive, prior, HistoryUpdateType.Replace, context.DefaultModificationInfo);
                                archive.Raw[key] = CloneValue(value);
                                statuses[i] = StatusCodes.GoodEntryReplaced;
                            }
                            break;
                        case HistoryUpdateType.Update:
                            if (exists)
                            {
                                LogModification(archive, prior, HistoryUpdateType.Update, context.DefaultModificationInfo);
                                archive.Raw[key] = CloneValue(value);
                                statuses[i] = StatusCodes.GoodEntryReplaced;
                            }
                            else
                            {
                                archive.Raw[key] = CloneValue(value);
                                statuses[i] = StatusCodes.GoodEntryInserted;
                                EvictIfNeeded(archive);
                            }
                            break;
                        default:
                            statuses[i] = StatusCodes.BadInvalidArgument;
                            break;
                    }
                }
            }
            return statuses;
        }

        private StatusCode[] ApplyTransactionalUpdate(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            HistoryUpdateType updateType)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var statuses = new StatusCode[values.Count];

            // Per the IHistorianTransactionalProvider contract: pre-flight
            // every value and only commit if every value would succeed.
            // Hold the lock for the entire pre-flight + commit pair so
            // concurrent writers cannot squeeze in.
            lock (m_lock)
            {
                NodeArchive archive = GetOrCreateArchive(nodeId);

                // Pre-flight pass
                for (int i = 0; i < values.Count; i++)
                {
                    DataValue value = values[i];
                    if (value.IsNull)
                    {
                        statuses[i] = StatusCodes.BadInvalidArgument;
                        FillRollback(statuses, i, StatusCodes.BadInvalidArgument);
                        return statuses;
                    }

                    var key = value.SourceTimestamp.ToDateTime();
                    bool exists = archive.Raw.ContainsKey(key);

                    StatusCode preflightResult = updateType switch
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

                    if (StatusCode.IsBad(preflightResult))
                    {
                        // Rollback: the entry that fails reports the actual
                        // failure code; every other value reports
                        // BadHistoryOperationUnsupported to indicate that the
                        // transaction did not commit them.
                        FillRollback(statuses, -1, StatusCodes.BadHistoryOperationUnsupported);
                        statuses[i] = preflightResult;
                        return statuses;
                    }
                    statuses[i] = preflightResult;
                }

                // Commit pass: at this point we know every value will succeed.
                for (int i = 0; i < values.Count; i++)
                {
                    DataValue value = values[i];
                    var key = value.SourceTimestamp.ToDateTime();
                    if (archive.Raw.TryGetValue(key, out DataValue prior))
                    {
                        LogModification(archive, prior, updateType, context.DefaultModificationInfo);
                    }
                    archive.Raw[key] = CloneValue(value);
                    if (!archive.Raw.ContainsKey(key))
                    {
                        EvictIfNeeded(archive);
                    }
                }
            }
            return statuses;
        }

        private static void FillRollback(StatusCode[] statuses, int skipIndex, StatusCode code)
        {
            for (int j = 0; j < statuses.Length; j++)
            {
                if (j == skipIndex)
                {
                    continue;
                }
                statuses[j] = code;
            }
        }

        private StatusCode[] ApplyAnnotation(
            NodeId nodeId,
            IList<Annotation> annotations,
            HistoryUpdateType updateType)
        {
            if (annotations == null)
            {
                throw new ArgumentNullException(nameof(annotations));
            }

            var statuses = new StatusCode[annotations.Count];
            lock (m_lock)
            {
                NodeArchive archive = GetOrCreateArchive(nodeId);

                for (int i = 0; i < annotations.Count; i++)
                {
                    Annotation annotation = annotations[i];
                    if (annotation == null)
                    {
                        statuses[i] = StatusCodes.BadInvalidArgument;
                        continue;
                    }

                    var key = annotation.AnnotationTime.ToDateTime();
                    bool exists = archive.Annotations.ContainsKey(key);

                    switch (updateType)
                    {
                        case HistoryUpdateType.Insert:
                            if (exists)
                            {
                                statuses[i] = StatusCodes.BadEntryExists;
                            }
                            else
                            {
                                archive.Annotations[key] = CloneAnnotation(annotation);
                                statuses[i] = StatusCodes.GoodEntryInserted;
                                EvictAnnotationsIfNeeded(archive);
                            }
                            break;
                        case HistoryUpdateType.Replace:
                            if (!exists)
                            {
                                statuses[i] = StatusCodes.BadNoEntryExists;
                            }
                            else
                            {
                                archive.Annotations[key] = CloneAnnotation(annotation);
                                statuses[i] = StatusCodes.GoodEntryReplaced;
                            }
                            break;
                        case HistoryUpdateType.Update:
                            archive.Annotations[key] = CloneAnnotation(annotation);
                            statuses[i] = exists
                                ? StatusCodes.GoodEntryReplaced
                                : StatusCodes.GoodEntryInserted;
                            if (!exists)
                            {
                                EvictAnnotationsIfNeeded(archive);
                            }
                            break;
                        default:
                            statuses[i] = StatusCodes.BadInvalidArgument;
                            break;
                    }
                }
            }
            return statuses;
        }

        private static HistorianPage<HistoricalDataValue> ReadRawPage(
            NodeArchive archive,
            HistorianRawReadRequest request,
            DateTime resumeAt)
        {
            var start = request.StartTime.ToDateTime();
            var end = request.EndTime.ToDateTime();
            DateTime lo = request.IsForward ? start : end;
            DateTime hi = request.IsForward ? end : start;
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }

            uint cap = request.MaxValues > 0 ? request.MaxValues : kMaxValuesPerPage;
            var output = new List<HistoricalDataValue>((int)Math.Min(cap, kMaxValuesPerPage));

            IEnumerable<KeyValuePair<DateTime, DataValue>> source = request.IsForward
                ? archive.Raw
                : archive.Raw.Reverse();

            DateTime windowMin = lo;
            DateTime windowMax = hi;

            // Optional leading bound: latest sample strictly before windowMin (forward) or first sample strictly after windowMax (reverse).
            if (request.ReturnBounds && resumeAt == DateTime.MinValue)
            {
                HistoricalDataValue? leadingBound = ComputeLeadingBound(archive, request.IsForward, windowMin, windowMax);
                if (leadingBound.HasValue)
                {
                    output.Add(leadingBound.Value);
                }
            }

            DateTime lastEmitted = DateTime.MinValue;
            DateTime resumeLocal = resumeAt;
            foreach (KeyValuePair<DateTime, DataValue> entry in source)
            {
                if (request.IsForward)
                {
                    if (entry.Key < windowMin || (resumeLocal != DateTime.MinValue && entry.Key <= resumeLocal))
                    {
                        continue;
                    }
                    if (entry.Key >= windowMax)
                    {
                        if (request.ReturnBounds)
                        {
                            output.Add(new HistoricalDataValue(CloneValue(entry.Value), IsBound: true));
                        }
                        return new HistorianPage<HistoricalDataValue>(output);
                    }
                }
                else
                {
                    if (entry.Key > windowMax || (resumeLocal != DateTime.MinValue && entry.Key >= resumeLocal))
                    {
                        continue;
                    }
                    if (entry.Key <= windowMin)
                    {
                        if (request.ReturnBounds)
                        {
                            output.Add(new HistoricalDataValue(CloneValue(entry.Value), IsBound: true));
                        }
                        return new HistorianPage<HistoricalDataValue>(output);
                    }
                }

                output.Add(new HistoricalDataValue(CloneValue(entry.Value)));
                lastEmitted = entry.Key;

                if (output.Count >= cap)
                {
                    return new HistorianPage<HistoricalDataValue>(output, EncodeTimestamp(lastEmitted));
                }
            }

            return new HistorianPage<HistoricalDataValue>(output);
        }

        private static HistoricalDataValue? ComputeLeadingBound(
            NodeArchive archive,
            bool isForward,
            DateTime windowMin,
            DateTime windowMax)
        {
            if (isForward)
            {
                DataValue candidate = DataValue.Null;
                foreach (KeyValuePair<DateTime, DataValue> entry in archive.Raw)
                {
                    if (entry.Key >= windowMin)
                    {
                        break;
                    }
                    candidate = entry.Value;
                }
                return !candidate.IsNull
                    ? new HistoricalDataValue(CloneValue(candidate), IsBound: true)
                    : null;
            }
            else
            {
                foreach (KeyValuePair<DateTime, DataValue> entry in archive.Raw)
                {
                    if (entry.Key > windowMax)
                    {
                        return new HistoricalDataValue(CloneValue(entry.Value), IsBound: true);
                    }
                }
                return null;
            }
        }

        private static HistorianPage<ModifiedDataValue> ReadModifiedPage(
            NodeArchive archive,
            HistorianModifiedReadRequest request,
            DateTime resumeAt)
        {
            var start = request.StartTime.ToDateTime();
            var end = request.EndTime.ToDateTime();
            DateTime lo = start <= end ? start : end;
            DateTime hi = start <= end ? end : start;

            uint cap = request.MaxValues > 0 ? request.MaxValues : kMaxValuesPerPage;
            var output = new List<ModifiedDataValue>((int)Math.Min(cap, kMaxValuesPerPage));

            IEnumerable<ModificationEntry> source = request.IsForward
                ? archive.ModifiedLog
                : Enumerable.Reverse(archive.ModifiedLog);

            DateTime lastEmittedKey = DateTime.MinValue;
            int lastEmittedSequence = -1;

            foreach (ModificationEntry entry in source)
            {
                var sourceTs = entry.Value.SourceTimestamp.ToDateTime();
                if (sourceTs < lo || sourceTs >= hi)
                {
                    continue;
                }
                if (resumeAt != DateTime.MinValue)
                {
                    if (request.IsForward && sourceTs <= resumeAt)
                    {
                        continue;
                    }
                    if (!request.IsForward && sourceTs >= resumeAt)
                    {
                        continue;
                    }
                }

                output.Add(new ModifiedDataValue(CloneValue(entry.Value), CloneInfo(entry.Info)));
                lastEmittedKey = sourceTs;
                lastEmittedSequence = entry.Sequence;

                if (output.Count >= cap)
                {
                    return new HistorianPage<ModifiedDataValue>(output, EncodeTimestamp(lastEmittedKey));
                }
            }

            _ = lastEmittedSequence;
            return new HistorianPage<ModifiedDataValue>(output);
        }

        private static HistorianPage<Annotation> ReadAnnotationsPage(
            NodeArchive archive,
            HistorianAnnotationReadRequest request,
            DateTime resumeAt)
        {
            var start = request.StartTime.ToDateTime();
            var end = request.EndTime.ToDateTime();
            DateTime lo = start <= end ? start : end;
            DateTime hi = start <= end ? end : start;

            uint cap = request.MaxValues > 0 ? request.MaxValues : kMaxValuesPerPage;
            var output = new List<Annotation>((int)Math.Min(cap, kMaxValuesPerPage));

            IEnumerable<KeyValuePair<DateTime, Annotation>> source = request.IsForward
                ? archive.Annotations
                : archive.Annotations.Reverse();

            DateTime lastEmittedKey = DateTime.MinValue;
            foreach (KeyValuePair<DateTime, Annotation> entry in source)
            {
                if (entry.Key < lo || entry.Key >= hi)
                {
                    continue;
                }
                if (resumeAt != DateTime.MinValue)
                {
                    if (request.IsForward && entry.Key <= resumeAt)
                    {
                        continue;
                    }
                    if (!request.IsForward && entry.Key >= resumeAt)
                    {
                        continue;
                    }
                }

                output.Add(CloneAnnotation(entry.Value));
                lastEmittedKey = entry.Key;

                if (output.Count >= cap)
                {
                    return new HistorianPage<Annotation>(output, EncodeTimestamp(lastEmittedKey));
                }
            }

            return new HistorianPage<Annotation>(output);
        }

        private NodeArchive GetOrCreateArchive(NodeId nodeId)
        {
            if (!m_archives.TryGetValue(nodeId, out NodeArchive? archive))
            {
                archive = new NodeArchive();
                m_archives[nodeId] = archive;
            }
            return archive;
        }

        private void EvictIfNeeded(NodeArchive archive)
        {
            if (m_options.MaxSamplesPerNode == 0 || archive.Raw.Count <= m_options.MaxSamplesPerNode)
            {
                return;
            }

            while (archive.Raw.Count > m_options.MaxSamplesPerNode)
            {
                DateTime oldest = archive.Raw.Keys.First();
                archive.Raw.Remove(oldest);
            }
        }

        private void EvictAnnotationsIfNeeded(NodeArchive archive)
        {
            if (m_options.MaxAnnotationsPerNode == 0 || archive.Annotations.Count <= m_options.MaxAnnotationsPerNode)
            {
                return;
            }

            while (archive.Annotations.Count > m_options.MaxAnnotationsPerNode)
            {
                DateTime oldest = archive.Annotations.Keys.First();
                archive.Annotations.Remove(oldest);
            }
        }

        private void LogModification(
            NodeArchive archive,
            DataValue prior,
            HistoryUpdateType updateType,
            ModificationInfo defaultInfo)
        {
            var info = new ModificationInfo
            {
                ModificationTime = defaultInfo.ModificationTime,
                UpdateType = updateType,
                UserName = defaultInfo.UserName
            };
            archive.ModifiedLog.Add(new ModificationEntry(CloneValue(prior), info, ++archive.SequenceCounter));

            if (m_options.MaxModifiedEntriesPerNode > 0 &&
                archive.ModifiedLog.Count > m_options.MaxModifiedEntriesPerNode)
            {
                archive.ModifiedLog.RemoveAt(0);
            }
        }

        private static DataValue CloneValue(DataValue source)
        {
            // DataValue is a readonly struct; copy is by value.
            return source;
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

        private static HistorianResumeToken EncodeTimestamp(DateTime timestamp)
        {
            byte[] buffer = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, timestamp.ToBinary());
            return new HistorianResumeToken(buffer);
        }

        private static DateTime DecodeTimestamp(HistorianResumeToken token)
        {
            if (token.IsEmpty)
            {
                return DateTime.MinValue;
            }
            if (token.State.Length < sizeof(long))
            {
                throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
            }
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(token.State.Span);
            return DateTime.FromBinary(ticks);
        }

        private const int kMaxValuesPerPage = 1000;

        private readonly Lock m_lock = new();
        private readonly InMemoryHistorianOptions m_options;
        private readonly NodeIdDictionary<NodeArchive> m_archives = [];
        private readonly NodeIdDictionary<HistorianNodeCapabilities> m_capabilities = [];
        private readonly NodeIdDictionary<List<HistorianEventRecord>> m_events = [];

        private sealed class NodeArchive
        {
            public SortedDictionary<DateTime, DataValue> Raw { get; } = [];
            public List<ModificationEntry> ModifiedLog { get; } = [];
            public SortedDictionary<DateTime, Annotation> Annotations { get; } = [];
            public int SequenceCounter;
        }

        private sealed record ModificationEntry(DataValue Value, ModificationInfo Info, int Sequence);
    }
}
