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
    /// <see cref="SortedDictionary{TKey,TValue}"/> structures keyed by the
    /// composite <see cref="HistoricalValueKey"/> (Part 11 §5.2.4). Ordinary
    /// raw variables use an empty uniqueness key so the composite key
    /// degenerates to <c>SourceTimestamp</c>; variables registered for
    /// StructuredHistoryData through
    /// <see cref="RegisterStructured(NodeId, IHistorianStructuredDataKeySelector, HistorianNodeCapabilities?)"/>
    /// use the canonical uniqueness key of their
    /// <see cref="IHistorianStructuredDataKeySelector"/> and can therefore
    /// keep several entries at the same source timestamp. Each
    /// insert/replace is also logged into a per-NodeId modification list so
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
    /// <see cref="InMemoryHistorianOptions.DefaultCapabilities"/> unless
    /// the caller supplied an override via
    /// <see cref="SetCapabilities(NodeId, HistorianNodeCapabilities)"/>.
    /// The provider-wide rollup returned for <see cref="NodeId.Null"/>
    /// is a conservative union of only the capabilities actually
    /// advertised by registered nodes — it does not assume
    /// <see cref="InMemoryHistorianOptions.DefaultCapabilities"/> applies
    /// to nodes that were never registered.
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
        IHistorianStructuredDataProvider,
        IDisposable
    {
        /// <summary>
        /// Creates a provider with default options.
        /// </summary>
        public InMemoryHistorianProvider()
            : this(new InMemoryHistorianOptions())
        {
        }

        /// <summary>
        /// Creates a provider with the supplied options.
        /// </summary>
        public InMemoryHistorianProvider(InMemoryHistorianOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            if (m_options.RawDataRetentionPeriod < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    m_options.RawDataRetentionPeriod,
                    "Raw data retention period must be non-negative.");
            }
        }

        /// <summary>
        /// Disposes the provider, clearing all archived data.
        /// </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_archives.Clear();
                m_capabilities.Clear();
                m_events.Clear();
                m_keySelectors.Clear();
            }
        }

        /// <summary>
        /// Pre-registers a variable. Equivalent to setting the default
        /// capability set; the archive is also created so the provider
        /// reports <c>true</c> from <see cref="IsHistorizingAsync"/> for
        /// the node before any value is inserted.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
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
        /// Pre-registers a variable that stores StructuredHistoryData
        /// (Part 11 §6.8.3). Entries of the node are identified by the
        /// composite <see cref="HistoricalValueKey"/> built from the source
        /// timestamp and the canonical uniqueness key of
        /// <paramref name="keySelector"/>, so the node can hold several
        /// entries at the same source timestamp.
        /// </summary>
        /// <param name="nodeId">The historizing variable.</param>
        /// <param name="keySelector">
        /// The selector that defines entry uniqueness for the structure
        /// stored on the node.
        /// </param>
        /// <param name="capabilities">
        /// Optional capability override. Defaults to
        /// <see cref="HistorianNodeCapabilities.StructuredReadWrite"/>, which
        /// advertises the structured read and update capabilities.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public void RegisterStructured(
            NodeId nodeId,
            IHistorianStructuredDataKeySelector keySelector,
            HistorianNodeCapabilities? capabilities = null)
        {
            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }
            if (nodeId.IsNull)
            {
                throw new ArgumentException("NodeId must not be null.", nameof(nodeId));
            }

            lock (m_lock)
            {
                _ = GetOrCreateArchive(nodeId);
                m_keySelectors[nodeId] = keySelector;
                m_capabilities[nodeId] =
                    capabilities ??
                    HistorianNodeCapabilities.StructuredReadWrite;
            }
        }

        /// <summary>
        /// Overrides the capability set advertised for a node. Subsequent
        /// reads of <see cref="GetCapabilitiesAsync"/> return this set.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
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
                m_keySelectors.Remove(nodeId);
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
                if (nodeId.IsNull)
                {
                    return new ValueTask<HistorianNodeCapabilities>(GetAggregateCapabilities());
                }

                HistorianNodeCapabilities caps = m_capabilities.TryGetValue(nodeId, out HistorianNodeCapabilities? value)
                    ? value
                    : m_options.DefaultCapabilities;
                return new ValueTask<HistorianNodeCapabilities>(caps);
            }
        }

        /// <summary>
        /// Builds the provider-wide capability rollup returned for
        /// <see cref="NodeId.Null"/>: a conservative union of only the
        /// capability flags actually advertised by explicitly registered
        /// nodes (<see cref="Register"/> / <see cref="RegisterStructured"/> /
        /// <see cref="SetCapabilities"/>).
        /// </summary>
        /// <remarks>
        /// This is deliberately <em>not</em>
        /// <see cref="InMemoryHistorianOptions.DefaultCapabilities"/>: that
        /// option only describes the template handed to the
        /// <em>next</em> node registered without an explicit override, it
        /// does not describe what is actually registered today. Blindly
        /// returning it here — as this method previously did — let a
        /// provider with zero (or read-only) registered nodes still
        /// advertise <see cref="HistorianNodeCapabilities.ReadWrite"/> to
        /// the server-wide <c>HistoryServerCapabilities</c> rollup in
        /// <c>DiagnosticsNodeManager</c>, over-advertising capabilities
        /// nothing in the address space actually backs. Must be called
        /// while holding <see cref="m_lock"/>.
        /// </remarks>
        private HistorianNodeCapabilities GetAggregateCapabilities()
        {
            bool readRawData = false;
            bool readModifiedData = false;
            bool readAtTime = false;
            bool readProcessedData = false;
            bool insertData = false;
            bool replaceData = false;
            bool updateData = false;
            bool deleteRaw = false;
            bool deleteAtTime = false;
            bool insertAnnotation = false;
            bool readEventHistory = false;
            bool insertEvent = false;
            bool replaceEvent = false;
            bool updateEvent = false;
            bool deleteEvent = false;
            bool readStructuredData = false;
            bool readModifiedStructuredData = false;
            bool readAtTimeStructuredData = false;
            bool insertStructuredData = false;
            bool replaceStructuredData = false;
            bool updateStructuredData = false;
            bool deleteStructuredData = false;
            bool serverTimestampSupported = false;
            bool portableResumeTokens = false;

            foreach (HistorianNodeCapabilities caps in m_capabilities.Values)
            {
                readRawData |= caps.ReadRawData;
                readModifiedData |= caps.ReadModifiedData;
                readAtTime |= caps.ReadAtTime;
                readProcessedData |= caps.ReadProcessedData;
                insertData |= caps.InsertData;
                replaceData |= caps.ReplaceData;
                updateData |= caps.UpdateData;
                deleteRaw |= caps.DeleteRaw;
                deleteAtTime |= caps.DeleteAtTime;
                insertAnnotation |= caps.InsertAnnotation;
                readEventHistory |= caps.ReadEventHistory;
                insertEvent |= caps.InsertEvent;
                replaceEvent |= caps.ReplaceEvent;
                updateEvent |= caps.UpdateEvent;
                deleteEvent |= caps.DeleteEvent;
                readStructuredData |= caps.ReadStructuredData;
                readModifiedStructuredData |= caps.ReadModifiedStructuredData;
                readAtTimeStructuredData |= caps.ReadAtTimeStructuredData;
                insertStructuredData |= caps.InsertStructuredData;
                replaceStructuredData |= caps.ReplaceStructuredData;
                updateStructuredData |= caps.UpdateStructuredData;
                deleteStructuredData |= caps.DeleteStructuredData;
                serverTimestampSupported |= caps.ServerTimestampSupported;
                portableResumeTokens |= caps.PortableResumeTokens;
            }

            return HistorianNodeCapabilities.None with
            {
                ReadRawData = readRawData,
                ReadModifiedData = readModifiedData,
                ReadAtTime = readAtTime,
                ReadProcessedData = readProcessedData,
                InsertData = insertData,
                ReplaceData = replaceData,
                UpdateData = updateData,
                DeleteRaw = deleteRaw,
                DeleteAtTime = deleteAtTime,
                InsertAnnotation = insertAnnotation,
                ReadEventHistory = readEventHistory,
                InsertEvent = insertEvent,
                ReplaceEvent = replaceEvent,
                UpdateEvent = updateEvent,
                DeleteEvent = deleteEvent,
                ReadStructuredData = readStructuredData,
                ReadModifiedStructuredData = readModifiedStructuredData,
                ReadAtTimeStructuredData = readAtTimeStructuredData,
                InsertStructuredData = insertStructuredData,
                ReplaceStructuredData = replaceStructuredData,
                UpdateStructuredData = updateStructuredData,
                DeleteStructuredData = deleteStructuredData,
                ServerTimestampSupported = serverTimestampSupported,
                PortableResumeTokens = portableResumeTokens
            };
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

                bool hasResume = TryDecodeCursor(resumeToken, out HistoricalValueKey resumeKey);
                return new ValueTask<HistorianPage<HistoricalDataValue>>(
                    ReadRawPage(archive, request, hasResume, resumeKey));
            }
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> InsertAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyUpdate(context, nodeId, values, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Bulk path used by the framework's auto-capture pipeline: acquires
        /// <see cref="m_lock"/> once for the entire <paramref name="batch"/>
        /// rather than once per <see cref="InsertAsync"/> call. Status
        /// semantics match the per-node <see cref="InsertAsync"/> contract.
        /// </remarks>
        public ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
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

            var result = new HistorianUpdateOutcome<DataValue>[batch.Count];
            lock (m_lock)
            {
                for (int batchIndex = 0; batchIndex < batch.Count; batchIndex++)
                {
                    HistorianDataBatch entry = batch[batchIndex];
                    if (entry.Values.IsNull)
                    {
                        result[batchIndex] = CreateOutcome<DataValue>([]);
                        continue;
                    }
                    NodeArchive archive = GetOrCreateArchive(entry.NodeId);
                    IHistorianStructuredDataKeySelector selector =
                        GetKeySelector(entry.NodeId);
                    var statuses = new StatusCode[entry.Values.Count];
                    for (int i = 0; i < entry.Values.Count; i++)
                    {
                        DataValue value = entry.Values[i];
                        if (value.IsNull)
                        {
                            statuses[i] = StatusCodes.BadInvalidArgument;
                            continue;
                        }

                        if (!TryCreateKey(selector, in value, out HistoricalValueKey key))
                        {
                            statuses[i] = StatusCodes.BadTypeMismatch;
                            continue;
                        }
                        if (archive.Raw.ContainsKey(key))
                        {
                            statuses[i] = StatusCodes.BadEntryExists;
                            continue;
                        }
                        archive.Raw[key] = CloneValue(value);
                        statuses[i] = StatusCodes.GoodEntryInserted;
                        EvictRawIfNeeded(archive, key.SourceTimestamp.ToDateTime());
                    }
                    result[batchIndex] =
                        CreateOutcome<DataValue>(statuses);
                }
            }
            return new ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>>(
                result);
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyUpdate(context, nodeId, values, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> UpdateAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyUpdate(context, nodeId, values, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> InsertAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyTransactionalUpdate(context, nodeId, values, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyTransactionalUpdate(context, nodeId, values, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> UpdateAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyTransactionalUpdate(context, nodeId, values, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<IHistorianStructuredDataKeySelector> GetKeySelectorAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            lock (m_lock)
            {
                return new ValueTask<IHistorianStructuredDataKeySelector>(GetKeySelector(nodeId));
            }
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> InsertStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyStructuredUpdate(context, nodeId, values, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyStructuredUpdate(context, nodeId, values, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> UpdateStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyStructuredUpdate(context, nodeId, values, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<DataValue>> RemoveStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                ApplyStructuredUpdate(context, nodeId, values, HistoryUpdateType.Delete));
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

            lock (m_lock)
            {
                if (!m_archives.TryGetValue(nodeId, out NodeArchive? archive))
                {
                    return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                        CreateOutcome<DataValue>([StatusCodes.GoodNoData]));
                }

                var start = startTime.ToDateTime();
                var end = endTime.ToDateTime();
                if (start > end)
                {
                    (start, end) = (end, start);
                }

                int removed = 0;
                var oldValues = new List<DataValue>();
                if (isDeleteModified)
                {
                    for (int i = archive.ModifiedLog.Count - 1; i >= 0; i--)
                    {
                        ModificationEntry entry = archive.ModifiedLog[i];
                        var timestamp = entry.Value.SourceTimestamp.ToDateTime();
                        if (timestamp >= start && timestamp < end)
                        {
                            oldValues.Add(CloneValue(entry.Value));
                            archive.ModifiedLog.RemoveAt(i);
                            removed++;
                        }
                    }
                }
                else
                {
                    foreach (HistoricalValueKey key in (List<HistoricalValueKey>)
                        [.. archive.Raw.Keys.Where(k => IsInRange(k, start, end))])
                    {
                        DataValue prior = archive.Raw[key];
                        archive.Raw.Remove(key);
                        oldValues.Add(CloneValue(prior));
                        LogModification(archive, prior, HistoryUpdateType.Delete, context.DefaultModificationInfo);
                        removed++;
                    }
                    if (removed > 0)
                    {
                        RefreshLatestRawTimestamp(archive);
                    }
                }

                return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                    CreateOutcome(
                        [removed > 0 ? StatusCodes.Good : StatusCodes.GoodNoData],
                        oldValues));
            }
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

            var statuses = new StatusCode[timestamps.Count];
            var oldValues = new List<DataValue>();
            lock (m_lock)
            {
                if (!m_archives.TryGetValue(nodeId, out NodeArchive? archive))
                {
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                    return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                        CreateOutcome<DataValue>(statuses));
                }

                for (int i = 0; i < timestamps.Count; i++)
                {
                    // A structured node may hold several entries at one
                    // timestamp; DeleteAtTime removes the complete set.
                    List<HistoricalValueKey> keys = GetKeysAt(archive, timestamps[i].ToDateTime());
                    if (keys.Count == 0)
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                        continue;
                    }

                    foreach (HistoricalValueKey key in keys)
                    {
                        DataValue prior = archive.Raw[key];
                        archive.Raw.Remove(key);
                        oldValues.Add(CloneValue(prior));
                        LogModification(archive, prior, HistoryUpdateType.Delete, context.DefaultModificationInfo);
                    }
                    statuses[i] = StatusCodes.Good;
                }
                if (statuses.Any(StatusCode.IsGood))
                {
                    RefreshLatestRawTimestamp(archive);
                }
            }
            return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                CreateOutcome(statuses, oldValues));
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

                bool hasResume = TryDecodeModifiedCursor(
                    archive,
                    resumeToken,
                    out ModifiedResumePosition resumePosition);
                return new ValueTask<HistorianPage<ModifiedDataValue>>(
                    ReadModifiedPage(
                        archive,
                        request,
                        hasResume,
                        resumePosition));
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
        public ValueTask<HistorianUpdateOutcome<Annotation>> InsertAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<Annotation>>(
                ApplyAnnotation(nodeId, annotations, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<Annotation>> ReplaceAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<Annotation>>(
                ApplyAnnotation(nodeId, annotations, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<Annotation>> UpdateAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<Annotation>>(
                ApplyAnnotation(nodeId, annotations, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<Annotation>> DeleteAnnotationsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DateTimeUtc> annotationTimes,
            CancellationToken ct)
        {
            if (annotationTimes.IsNull)
            {
                throw new ArgumentNullException(nameof(annotationTimes));
            }

            var statuses = new StatusCode[annotationTimes.Count];
            var oldValues = new List<Annotation>();
            lock (m_lock)
            {
                if (!m_archives.TryGetValue(nodeId, out NodeArchive? archive))
                {
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                    return new ValueTask<HistorianUpdateOutcome<Annotation>>(
                        CreateOutcome<Annotation>(statuses));
                }

                for (int i = 0; i < annotationTimes.Count; i++)
                {
                    var key = annotationTimes[i].ToDateTime();
                    if (archive.Annotations.TryGetValue(key, out Annotation? annotation))
                    {
                        oldValues.Add(CloneAnnotation(annotation));
                        archive.Annotations.Remove(key);
                        statuses[i] = StatusCodes.Good;
                    }
                    else
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                }
            }
            return new ValueTask<HistorianUpdateOutcome<Annotation>>(
                CreateOutcome(statuses, oldValues));
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
            EventEntry[] snapshot;
            lock (m_lock)
            {
                if (!m_events.TryGetValue(request.NodeId, out List<EventEntry>? list))
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

            uint cap = request.MaxValues > 0
                ? Math.Min(request.MaxValues, kMaxValuesPerPage)
                : kMaxValuesPerPage;
            HistorianResumeCursor cursor = resumeToken.TryGetCursor(
                out HistorianResumeCursor decoded)
                ? decoded
                : default;
            IEnumerable<EventEntry> ordered = request.IsForward
                ? snapshot
                    .OrderBy(entry => entry.Record.SourceTimestamp)
                    .ThenBy(entry => entry.Sequence)
                : snapshot
                    .OrderByDescending(entry => entry.Record.SourceTimestamp)
                    .ThenByDescending(entry => entry.Sequence);
            var candidates = new List<EventEntry>();
            foreach (EventEntry entry in ordered)
            {
                var timestamp = entry.Record.SourceTimestamp.ToDateTime();
                if (timestamp < lo || timestamp >= hi)
                {
                    continue;
                }
                if (cursor.Sequence != 0)
                {
                    int timestampComparison = entry.Record.SourceTimestamp.CompareTo(
                        cursor.Timestamp);
                    if (request.IsForward &&
                        (timestampComparison < 0 ||
                            (timestampComparison == 0 &&
                                entry.Sequence <= cursor.Sequence)))
                    {
                        continue;
                    }
                    if (!request.IsForward &&
                        (timestampComparison > 0 ||
                            (timestampComparison == 0 &&
                                entry.Sequence >= cursor.Sequence)))
                    {
                        continue;
                    }
                }
                candidates.Add(entry);
            }

            int count = Math.Min(candidates.Count, (int)cap);
            var page = new List<HistorianEventRecord>(count);
            for (int i = 0; i < count; i++)
            {
                page.Add(candidates[i].Record);
            }
            if (candidates.Count > count)
            {
                EventEntry last = candidates[count - 1];
                return new ValueTask<HistorianPage<HistorianEventRecord>>(
                    new HistorianPage<HistorianEventRecord>(
                        page,
                        HistorianResumeToken.FromCursor(
                            new HistorianResumeCursor(
                                last.Record.SourceTimestamp,
                                last.Record.EventId,
                                last.Sequence))));
            }
            return new ValueTask<HistorianPage<HistorianEventRecord>>(
                new HistorianPage<HistorianEventRecord>(page));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> InsertEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>(
                ApplyEventUpdate(nodeId, events, HistoryUpdateType.Insert));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> ReplaceEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>(
                ApplyEventUpdate(nodeId, events, HistoryUpdateType.Replace));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> UpdateEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            CancellationToken ct)
        {
            return new ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>(
                ApplyEventUpdate(nodeId, events, HistoryUpdateType.Update));
        }

        /// <inheritdoc/>
        public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>> DeleteEventsAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<ByteString> eventIds,
            CancellationToken ct)
        {
            if (eventIds.IsNull)
            {
                throw new ArgumentNullException(nameof(eventIds));
            }
            var statuses = new StatusCode[eventIds.Count];
            var oldValues = new List<HistorianEventRecord>();
            lock (m_lock)
            {
                if (!m_events.TryGetValue(nodeId, out List<EventEntry>? list))
                {
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                    return new ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>(
                        CreateOutcome<HistorianEventRecord>(statuses));
                }
                for (int i = 0; i < eventIds.Count; i++)
                {
                    ByteString id = eventIds[i];
                    int idx = list.FindIndex(entry => entry.Record.EventId == id);
                    if (idx >= 0)
                    {
                        oldValues.Add(list[idx].Record);
                        list.RemoveAt(idx);
                        statuses[i] = StatusCodes.Good;
                    }
                    else
                    {
                        statuses[i] = StatusCodes.BadNoEntryExists;
                    }
                }
            }
            return new ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>(
                CreateOutcome(statuses, oldValues));
        }

        private HistorianUpdateOutcome<HistorianEventRecord> ApplyEventUpdate(
            NodeId nodeId,
            ArrayOf<HistorianEventRecord> events,
            HistoryUpdateType updateType)
        {
            if (events.IsNull)
            {
                throw new ArgumentNullException(nameof(events));
            }
            var statuses = new StatusCode[events.Count];
            var oldValues = new List<HistorianEventRecord>();

            lock (m_lock)
            {
                if (!m_events.TryGetValue(nodeId, out List<EventEntry>? list))
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
                        ? list.FindIndex(entry => entry.Record.EventId == rec.EventId)
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
                                list.Add(new EventEntry(rec, ++m_eventSequence));
                                statuses[i] = StatusCodes.GoodEntryInserted;
                            }
                            break;
                        case HistoryUpdateType.Replace:
                            if (idx < 0)
                            {
                                statuses[i] = StatusCodes.BadNoEntryExists;
                            }
                            else if (!TryMergeEventRecord(
                                list[idx].Record,
                                rec,
                                out HistorianEventRecord merged,
                                out StatusCode mergeStatus))
                            {
                                statuses[i] = mergeStatus;
                            }
                            else
                            {
                                HistorianEventRecord prior = list[idx].Record;
                                oldValues.Add(prior);
                                list[idx] = list[idx] with
                                {
                                    Record = merged
                                };
                                statuses[i] = StatusCodes.GoodEntryReplaced;
                            }
                            break;
                        case HistoryUpdateType.Update:
                            if (idx >= 0)
                            {
                                HistorianEventRecord prior = list[idx].Record;
                                if (!TryMergeEventRecord(
                                    prior,
                                    rec,
                                    out HistorianEventRecord merged,
                                    out StatusCode mergeStatus))
                                {
                                    statuses[i] = mergeStatus;
                                }
                                else
                                {
                                    oldValues.Add(prior);
                                    list[idx] = list[idx] with
                                    {
                                        Record = merged
                                    };
                                    statuses[i] = StatusCodes.GoodEntryReplaced;
                                }
                            }
                            else
                            {
                                list.Add(new EventEntry(rec, ++m_eventSequence));
                                statuses[i] = StatusCodes.GoodEntryInserted;
                            }
                            break;
                        default:
                            statuses[i] = StatusCodes.BadInvalidArgument;
                            break;
                    }
                }
            }
            return CreateOutcome(statuses, oldValues);
        }

        private static bool TryMergeEventRecord(
            HistorianEventRecord prior,
            HistorianEventRecord update,
            out HistorianEventRecord merged,
            out StatusCode statusCode)
        {
            // .NET Framework has no Dictionary(IEnumerable<KeyValuePair<,>>)
            // constructor, so the prior fields are copied explicitly.
            var fields = new Dictionary<string, Variant>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Variant> field in prior.Fields)
            {
                fields[field.Key] = field.Value;
            }
            var qualifiedFields = new Dictionary<HistorianEventFieldKey, Variant>();
            foreach (KeyValuePair<HistorianEventFieldKey, Variant> field in
                prior.QualifiedFields)
            {
                qualifiedFields[field.Key] = field.Value;
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
                    qualifiedFields[field.Key] = field.Value;
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
                if (!qualifiedFields.TryGetValue(
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
                qualifiedFields[targetKey] = target;
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
                QualifiedFields = qualifiedFields.ToArrayOf()
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
            return key.AttributeId == Attributes.Value &&
                key.BrowsePath.Count == 1 &&
                key.BrowsePath[0].NamespaceIndex == 0 &&
                string.Equals(
                    key.BrowsePath[0].Name,
                    BrowseNames.EventId,
                    StringComparison.Ordinal);
        }

        private HistorianUpdateOutcome<DataValue> ApplyUpdate(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            HistoryUpdateType updateType)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (values.IsNull)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var buffer = new DataValue[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                buffer[i] = values[i];
            }
            lock (m_lock)
            {
                return ApplyUpdateCore(context, nodeId, buffer, updateType);
            }
        }

        private HistorianUpdateOutcome<DataValue> ApplyStructuredUpdate(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            HistoryUpdateType updateType)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (values.IsNull)
            {
                throw new ArgumentNullException(nameof(values));
            }

            lock (m_lock)
            {
                return ApplyUpdateCore(context, nodeId, values.Span, updateType);
            }
        }

        private HistorianUpdateOutcome<DataValue> ApplyUpdateCore(
            HistorianOperationContext context,
            NodeId nodeId,
            ReadOnlySpan<DataValue> values,
            HistoryUpdateType updateType)
        {
            var statuses = new StatusCode[values.Length];
            var oldValues = new List<DataValue>();
            NodeArchive archive = GetOrCreateArchive(nodeId);
            IHistorianStructuredDataKeySelector selector = GetKeySelector(nodeId);

            for (int i = 0; i < values.Length; i++)
            {
                DataValue value = values[i];
                if (value.IsNull)
                {
                    statuses[i] = StatusCodes.BadInvalidArgument;
                    continue;
                }
                if (!TryCreateKey(selector, in value, out HistoricalValueKey key))
                {
                    statuses[i] = StatusCodes.BadTypeMismatch;
                    continue;
                }

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
                            EvictRawIfNeeded(archive, key.SourceTimestamp.ToDateTime());
                        }
                        break;
                    case HistoryUpdateType.Replace:
                        if (!exists)
                        {
                            // The entry identity changed (a uniqueness field
                            // was edited) or it was never stored: the client
                            // has to remove and insert instead.
                            statuses[i] = StatusCodes.BadNoEntryExists;
                        }
                        else
                        {
                            oldValues.Add(CloneValue(prior));
                            LogModification(archive, prior, HistoryUpdateType.Replace, context.DefaultModificationInfo);
                            archive.Raw[key] = CloneValue(value);
                            statuses[i] = StatusCodes.GoodEntryReplaced;
                        }
                        break;
                    case HistoryUpdateType.Update:
                        if (exists)
                        {
                            oldValues.Add(CloneValue(prior));
                            LogModification(archive, prior, HistoryUpdateType.Update, context.DefaultModificationInfo);
                            archive.Raw[key] = CloneValue(value);
                            statuses[i] = StatusCodes.GoodEntryReplaced;
                        }
                        else
                        {
                            archive.Raw[key] = CloneValue(value);
                            statuses[i] = StatusCodes.GoodEntryInserted;
                            EvictRawIfNeeded(archive, key.SourceTimestamp.ToDateTime());
                        }
                        break;
                    case HistoryUpdateType.Delete:
                        if (exists)
                        {
                            oldValues.Add(CloneValue(prior));
                            archive.Raw.Remove(key);
                            LogModification(archive, prior, HistoryUpdateType.Delete, context.DefaultModificationInfo);
                            RefreshLatestRawTimestamp(archive);
                            statuses[i] = StatusCodes.Good;
                        }
                        else
                        {
                            statuses[i] = StatusCodes.BadNoEntryExists;
                        }
                        break;
                    default:
                        statuses[i] = StatusCodes.BadInvalidArgument;
                        break;
                }
            }
            return CreateOutcome(statuses, oldValues);
        }

        private HistorianUpdateOutcome<DataValue> ApplyTransactionalUpdate(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            HistoryUpdateType updateType)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (values.IsNull)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var statuses = new StatusCode[values.Count];
            var virtualValues = new Dictionary<HistoricalValueKey, DataValue>(
                HistoricalValueKeyComparer.Instance);

            // Per the IHistorianTransactionalProvider contract: pre-flight
            // every value and only commit if every value would succeed.
            // Hold the lock for the entire pre-flight + commit pair so
            // concurrent writers cannot squeeze in.
            lock (m_lock)
            {
                NodeArchive archive = GetOrCreateArchive(nodeId);
                IHistorianStructuredDataKeySelector selector = GetKeySelector(nodeId);

                // Pre-flight pass
                for (int i = 0; i < values.Count; i++)
                {
                    DataValue value = values[i];
                    if (value.IsNull)
                    {
                        FillRollback(statuses, StatusCodes.BadTransactionFailed);
                        statuses[i] = StatusCodes.BadInvalidArgument;
                        return CreateOutcome<DataValue>(
                            statuses,
                            transactionRolledBack: true);
                    }

                    if (!TryCreateKey(selector, in value, out HistoricalValueKey key))
                    {
                        FillRollback(statuses, StatusCodes.BadTransactionFailed);
                        statuses[i] = StatusCodes.BadTypeMismatch;
                        return CreateOutcome<DataValue>(
                            statuses,
                            transactionRolledBack: true);
                    }

                    bool exists;
                    if (virtualValues.TryGetValue(key, out DataValue staged))
                    {
                        exists = !staged.IsNull;
                    }
                    else
                    {
                        exists = archive.Raw.TryGetValue(key, out _);
                    }

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
                        FillRollback(statuses, StatusCodes.BadTransactionFailed);
                        statuses[i] = preflightResult;
                        return CreateOutcome<DataValue>(
                            statuses,
                            transactionRolledBack: true);
                    }
                    statuses[i] = preflightResult;
                    virtualValues[key] = value;
                }

                // Commit pass: at this point we know every value will succeed.
                DateTime? newestInsertedTimestamp = null;
                var oldValues = new List<DataValue>();
                for (int i = 0; i < values.Count; i++)
                {
                    DataValue value = values[i];
                    if (!TryCreateKey(selector, in value, out HistoricalValueKey key))
                    {
                        continue;
                    }
                    var timestamp = key.SourceTimestamp.ToDateTime();
                    if (archive.Raw.TryGetValue(key, out DataValue prior))
                    {
                        oldValues.Add(CloneValue(prior));
                        LogModification(
                            archive,
                            prior,
                            updateType,
                            context.DefaultModificationInfo);
                    }
                    archive.Raw[key] = CloneValue(value);
                    if (statuses[i].Code == StatusCodes.GoodEntryInserted.Code)
                    {
                        if (!newestInsertedTimestamp.HasValue || timestamp > newestInsertedTimestamp.Value)
                        {
                            newestInsertedTimestamp = timestamp;
                        }
                        EvictRawIfNeeded(archive, timestamp);
                    }
                }
                if (newestInsertedTimestamp.HasValue)
                {
                    EvictRawIfNeeded(archive, newestInsertedTimestamp.Value);
                }
                return CreateOutcome(statuses, oldValues);
            }
        }

        private static void FillRollback(StatusCode[] statuses, StatusCode code)
        {
            for (int j = 0; j < statuses.Length; j++)
            {
                statuses[j] = code;
            }
        }

        private HistorianUpdateOutcome<Annotation> ApplyAnnotation(
            NodeId nodeId,
            ArrayOf<Annotation> annotations,
            HistoryUpdateType updateType)
        {
            if (annotations.IsNull)
            {
                throw new ArgumentNullException(nameof(annotations));
            }

            var statuses = new StatusCode[annotations.Count];
            var oldValues = new List<Annotation>();
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
                                oldValues.Add(CloneAnnotation(archive.Annotations[key]));
                                archive.Annotations[key] = CloneAnnotation(annotation);
                                statuses[i] = StatusCodes.GoodEntryReplaced;
                            }
                            break;
                        case HistoryUpdateType.Update:
                            if (exists)
                            {
                                oldValues.Add(CloneAnnotation(archive.Annotations[key]));
                            }
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
            return CreateOutcome(statuses, oldValues);
        }

        private static HistorianPage<HistoricalDataValue> ReadRawPage(
            NodeArchive archive,
            HistorianRawReadRequest request,
            bool hasResume,
            HistoricalValueKey resumeKey)
        {
            var start = request.StartTime.ToDateTime();
            var end = request.EndTime.ToDateTime();
            DateTime lo = request.IsForward ? start : end;
            DateTime hi = request.IsForward ? end : start;
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }
            DateTime windowMin = lo;
            DateTime windowMax = hi;

            uint cap = request.MaxValues > 0 ? request.MaxValues : kMaxValuesPerPage;
            var output = new List<HistoricalDataValue>((int)Math.Min(cap, kMaxValuesPerPage));
            HistoricalValueKey lastEmitted = default;

            if (windowMin == windowMax)
            {
                // Read at an exact instant. A structured node can hold more
                // than one entry there, so the complete set is returned and
                // paged with the composite cursor.
                List<HistoricalValueKey> exact = GetKeysAt(archive, windowMin);
                if (exact.Count > 0)
                {
                    foreach (HistoricalValueKey key in exact)
                    {
                        if (hasResume && key <= resumeKey)
                        {
                            continue;
                        }
                        if (output.Count >= cap)
                        {
                            return new HistorianPage<HistoricalDataValue>(
                                output,
                                EncodeCursor(lastEmitted));
                        }
                        output.Add(new HistoricalDataValue(
                            CloneValue(archive.Raw[key]),
                            request.ReturnBounds));
                        lastEmitted = key;
                    }

                    if (!request.ReturnBounds || request.MaxValues == 1)
                    {
                        return new HistorianPage<HistoricalDataValue>(output);
                    }

                    foreach (KeyValuePair<HistoricalValueKey, DataValue> entry in archive.Raw)
                    {
                        if (entry.Key.SourceTimestamp.ToDateTime() > windowMax)
                        {
                            if (output.Count >= cap)
                            {
                                return new HistorianPage<HistoricalDataValue>(
                                    output,
                                    EncodeCursor(lastEmitted));
                            }
                            output.Add(new HistoricalDataValue(CloneValue(entry.Value), IsBound: true));
                            break;
                        }
                    }
                    return new HistorianPage<HistoricalDataValue>(output);
                }

                if (!request.ReturnBounds)
                {
                    return new HistorianPage<HistoricalDataValue>(output);
                }
            }

            IEnumerable<KeyValuePair<HistoricalValueKey, DataValue>> source = request.IsForward
                ? archive.Raw
                : archive.Raw.Reverse();

            bool leadingBoundarySpecified = request.IsForward
                ? request.StartTime != DateTimeUtc.MinValue
                : request.EndTime != DateTimeUtc.MaxValue;
            bool trailingBoundarySpecified = request.IsForward
                ? request.EndTime != DateTimeUtc.MaxValue
                : request.StartTime != DateTimeUtc.MinValue;
            bool isOpenEnded = request.MaxValues > 0 &&
                (request.StartTime == DateTimeUtc.MinValue || request.EndTime == DateTimeUtc.MaxValue);

            if (request.ReturnBounds && leadingBoundarySpecified && !hasResume)
            {
                DateTime leadingBoundary = request.IsForward ? windowMin : windowMax;
                if (!ContainsTimestamp(archive, leadingBoundary))
                {
                    if (TryComputeLeadingBound(
                        archive,
                        request.IsForward,
                        windowMin,
                        windowMax,
                        out HistoricalDataValue bound,
                        out HistoricalValueKey boundKey))
                    {
                        output.Add(bound);
                        lastEmitted = boundKey;
                    }
                    else
                    {
                        output.Add(CreateMissingBound(leadingBoundary));
                        lastEmitted = HistoricalValueKey.FromTimestamp(leadingBoundary);
                    }
                }
            }

            bool capReached = output.Count >= cap;
            foreach (KeyValuePair<HistoricalValueKey, DataValue> entry in source)
            {
                var timestamp = entry.Key.SourceTimestamp.ToDateTime();
                if (request.IsForward)
                {
                    if (timestamp < windowMin || (hasResume && entry.Key <= resumeKey))
                    {
                        continue;
                    }
                    if (timestamp >= windowMax)
                    {
                        if (request.ReturnBounds && trailingBoundarySpecified)
                        {
                            if (capReached)
                            {
                                return new HistorianPage<HistoricalDataValue>(
                                    output,
                                    EncodeCursor(lastEmitted));
                            }
                            output.Add(new HistoricalDataValue(CloneValue(entry.Value), IsBound: true));
                        }
                        return new HistorianPage<HistoricalDataValue>(output);
                    }
                }
                else
                {
                    if (timestamp > windowMax || (hasResume && entry.Key >= resumeKey))
                    {
                        continue;
                    }
                    if (timestamp <= windowMin)
                    {
                        if (request.ReturnBounds && trailingBoundarySpecified)
                        {
                            if (capReached)
                            {
                                return new HistorianPage<HistoricalDataValue>(
                                    output,
                                    EncodeCursor(lastEmitted));
                            }
                            output.Add(new HistoricalDataValue(CloneValue(entry.Value), IsBound: true));
                        }
                        return new HistorianPage<HistoricalDataValue>(output);
                    }
                }

                // The page is already full and here is another qualifying in-window value:
                // this is the proof that more data remains, so page now with a resume token.
                // Deferring the token until the next value is seen avoids emitting a spurious
                // ContinuationPoint on the final page (OPC UA Part 11; CTT HA Read Raw 008/009).
                if (capReached)
                {
                    if (isOpenEnded)
                    {
                        return new HistorianPage<HistoricalDataValue>(output);
                    }
                    return new HistorianPage<HistoricalDataValue>(output, EncodeCursor(lastEmitted));
                }

                output.Add(new HistoricalDataValue(CloneValue(entry.Value)));
                lastEmitted = entry.Key;
                capReached = output.Count >= cap;
            }

            if (request.ReturnBounds && trailingBoundarySpecified)
            {
                if (capReached)
                {
                    return new HistorianPage<HistoricalDataValue>(
                        output,
                        EncodeCursor(lastEmitted));
                }
                output.Add(CreateMissingBound(request.IsForward ? windowMax : windowMin));
            }
            else if (request.ReturnBounds && isOpenEnded && output.Count > 0 && output.Count < cap)
            {
                var previousTimestamp = output[^1].Value.SourceTimestamp.ToDateTime();
                output.Add(CreateMissingBound(previousTimestamp.AddSeconds(request.IsForward ? 1 : -1)));
            }

            return new HistorianPage<HistoricalDataValue>(output);
        }

        private static bool TryComputeLeadingBound(
            NodeArchive archive,
            bool isForward,
            DateTime windowMin,
            DateTime windowMax,
            out HistoricalDataValue bound,
            out HistoricalValueKey boundKey)
        {
            if (isForward)
            {
                DataValue candidate = DataValue.Null;
                HistoricalValueKey candidateKey = default;
                foreach (KeyValuePair<HistoricalValueKey, DataValue> entry in archive.Raw)
                {
                    if (entry.Key.SourceTimestamp.ToDateTime() >= windowMin)
                    {
                        break;
                    }
                    candidate = entry.Value;
                    candidateKey = entry.Key;
                }
                if (!candidate.IsNull)
                {
                    bound = new HistoricalDataValue(CloneValue(candidate), IsBound: true);
                    boundKey = candidateKey;
                    return true;
                }
            }
            else
            {
                foreach (KeyValuePair<HistoricalValueKey, DataValue> entry in archive.Raw)
                {
                    if (entry.Key.SourceTimestamp.ToDateTime() > windowMax)
                    {
                        bound = new HistoricalDataValue(CloneValue(entry.Value), IsBound: true);
                        boundKey = entry.Key;
                        return true;
                    }
                }
            }

            bound = default;
            boundKey = default;
            return false;
        }

        private static HistoricalDataValue CreateMissingBound(DateTime timestamp)
        {
            return new HistoricalDataValue(
                new DataValue(
                    Variant.Null,
                    StatusCodes.BadBoundNotFound,
                    sourceTimestamp: timestamp,
                    serverTimestamp: DateTimeUtc.MinValue),
                IsBound: true);
        }

        private static HistorianPage<ModifiedDataValue> ReadModifiedPage(
            NodeArchive archive,
            HistorianModifiedReadRequest request,
            bool hasResume,
            ModifiedResumePosition resumePosition)
        {
            var start = request.StartTime.ToDateTime();
            var end = request.EndTime.ToDateTime();
            DateTime lo = start <= end ? start : end;
            DateTime hi = start <= end ? end : start;

            uint cap = request.MaxValues > 0 ? request.MaxValues : kMaxValuesPerPage;
            var output = new List<ModifiedDataValue>((int)Math.Min(cap, kMaxValuesPerPage));

            var source = archive.ModifiedLog
                .Where(entry =>
                    request.IsForward
                        ? entry.Value.SourceTimestamp >= lo &&
                            entry.Value.SourceTimestamp < hi
                        : entry.Value.SourceTimestamp > lo &&
                            entry.Value.SourceTimestamp <= hi)
                .ToList();
            source.Sort(CompareModifiedEntries);
            if (!request.IsForward)
            {
                source.Reverse();
            }
            ModificationEntry? lastEmitted = null;
            bool capReached = false;

            foreach (ModificationEntry entry in source)
            {
                if (hasResume)
                {
                    if (resumePosition.UseLegacySequenceOnly)
                    {
                        int timestampComparison =
                            entry.Value.SourceTimestamp.CompareTo(
                                resumePosition.SourceTimestamp);
                        if (request.IsForward &&
                            (timestampComparison < 0 ||
                                (timestampComparison == 0 &&
                                    entry.Sequence >=
                                        resumePosition.Sequence)))
                        {
                            continue;
                        }
                        if (!request.IsForward &&
                            (timestampComparison > 0 ||
                                (timestampComparison == 0 &&
                                    entry.Sequence <=
                                        resumePosition.Sequence)))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        int comparison = CompareModifiedPosition(
                            entry,
                            resumePosition);
                        if ((request.IsForward && comparison <= 0) ||
                            (!request.IsForward && comparison >= 0))
                        {
                            continue;
                        }
                    }
                }

                if (capReached)
                {
                    return new HistorianPage<ModifiedDataValue>(
                        output,
                        EncodeModifiedCursor(lastEmitted!));
                }
                output.Add(new ModifiedDataValue(CloneValue(entry.Value), CloneInfo(entry.Info)));
                lastEmitted = entry;
                capReached = output.Count >= cap;
            }

            return new HistorianPage<ModifiedDataValue>(output);
        }

        private static int CompareModifiedEntries(
            ModificationEntry left,
            ModificationEntry right)
        {
            int comparison = left.Value.SourceTimestamp.CompareTo(
                right.Value.SourceTimestamp);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = right.Info.ModificationTime.CompareTo(
                left.Info.ModificationTime);
            return comparison != 0
                ? comparison
                : right.Sequence.CompareTo(left.Sequence);
        }

        private static int CompareModifiedPosition(
            ModificationEntry entry,
            ModifiedResumePosition position)
        {
            int comparison = entry.Value.SourceTimestamp.CompareTo(
                position.SourceTimestamp);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = position.ModificationTime.CompareTo(
                entry.Info.ModificationTime);
            return comparison != 0
                ? comparison
                : position.Sequence.CompareTo(entry.Sequence);
        }

        private static HistorianResumeToken EncodeModifiedCursor(
            ModificationEntry entry)
        {
            byte[] key = new byte[kModifiedCursorKeyLength];
            Span<byte> span = key;
            BinaryPrimitives.WriteInt32LittleEndian(
                span,
                kModifiedCursorKeyMagic);
            BinaryPrimitives.WriteInt32LittleEndian(
                span[sizeof(int)..],
                kModifiedCursorKeyVersion);
            BinaryPrimitives.WriteInt64LittleEndian(
                span[(2 * sizeof(int))..],
                entry.Info.ModificationTime.ToDateTime().ToBinary());
            return HistorianResumeToken.FromCursor(
                new HistorianResumeCursor(
                    entry.Value.SourceTimestamp,
                    ByteString.From(key),
                    entry.Sequence));
        }

        private static bool TryDecodeModifiedCursor(
            NodeArchive archive,
            HistorianResumeToken token,
            out ModifiedResumePosition position)
        {
            if (token.IsEmpty)
            {
                position = default;
                return false;
            }
            if (!token.TryGetCursor(out HistorianResumeCursor cursor) ||
                cursor.Sequence <= 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid);
            }
            if (cursor.Key.IsEmpty)
            {
                ModificationEntry? boundary = archive.ModifiedLog.Find(
                    entry =>
                        entry.Value.SourceTimestamp == cursor.Timestamp &&
                        entry.Sequence == cursor.Sequence);
                position = boundary == null
                    ? new ModifiedResumePosition(
                        cursor.Timestamp,
                        default,
                        cursor.Sequence,
                        UseLegacySequenceOnly: true)
                    : new ModifiedResumePosition(
                        cursor.Timestamp,
                        boundary.Info.ModificationTime,
                        cursor.Sequence,
                        UseLegacySequenceOnly: false);
                return true;
            }

            ReadOnlySpan<byte> key = cursor.Key.Span;
            if (key.Length != kModifiedCursorKeyLength ||
                BinaryPrimitives.ReadInt32LittleEndian(key) !=
                    kModifiedCursorKeyMagic ||
                BinaryPrimitives.ReadInt32LittleEndian(key[sizeof(int)..]) !=
                    kModifiedCursorKeyVersion)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid);
            }
            try
            {
                position = new ModifiedResumePosition(
                    cursor.Timestamp,
                    new DateTimeUtc(DateTime.FromBinary(
                        BinaryPrimitives.ReadInt64LittleEndian(
                            key[(2 * sizeof(int))..]))),
                    cursor.Sequence,
                    UseLegacySequenceOnly: false);
                return true;
            }
            catch (ArgumentException exception)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid,
                    exception.Message,
                    exception);
            }
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
            bool capReached = false;
            foreach (KeyValuePair<DateTime, Annotation> entry in source)
            {
                bool outside = request.IsForward
                    ? entry.Key < lo || entry.Key >= hi
                    : entry.Key <= lo || entry.Key > hi;
                if (outside)
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

                if (capReached)
                {
                    return new HistorianPage<Annotation>(
                        output,
                        EncodeTimestamp(lastEmittedKey));
                }
                output.Add(CloneAnnotation(entry.Value));
                lastEmittedKey = entry.Key;
                capReached = output.Count >= cap;
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

        /// <summary>
        /// Returns the uniqueness-key selector registered for the node, or
        /// the timestamp-only default used by ordinary raw history.
        /// Callers hold <see cref="m_lock"/>.
        /// </summary>
        private IHistorianStructuredDataKeySelector GetKeySelector(NodeId nodeId)
        {
            return m_keySelectors.TryGetValue(
                nodeId,
                out IHistorianStructuredDataKeySelector? selector)
                ? selector
                : TimestampStructuredDataKeySelector.Instance;
        }

        private static bool TryCreateKey(
            IHistorianStructuredDataKeySelector selector,
            in DataValue value,
            out HistoricalValueKey key)
        {
            if (!selector.TryGetUniquenessKey(in value, out ByteString uniquenessKey))
            {
                key = default;
                return false;
            }
            key = new HistoricalValueKey(value.SourceTimestamp, uniquenessKey);
            return true;
        }

        private static bool IsInRange(HistoricalValueKey key, DateTime start, DateTime end)
        {
            var timestamp = key.SourceTimestamp.ToDateTime();
            return timestamp >= start && timestamp < end;
        }

        /// <summary>
        /// Returns every key stored at the timestamp, in archive order.
        /// Structured nodes can hold more than one.
        /// </summary>
        private static List<HistoricalValueKey> GetKeysAt(NodeArchive archive, DateTime timestamp)
        {
            var keys = new List<HistoricalValueKey>();
            foreach (HistoricalValueKey key in archive.Raw.Keys)
            {
                var candidate = key.SourceTimestamp.ToDateTime();
                if (candidate > timestamp)
                {
                    break;
                }
                if (candidate == timestamp)
                {
                    keys.Add(key);
                }
            }
            return keys;
        }

        private static bool ContainsTimestamp(NodeArchive archive, DateTime timestamp)
        {
            foreach (HistoricalValueKey key in archive.Raw.Keys)
            {
                var candidate = key.SourceTimestamp.ToDateTime();
                if (candidate > timestamp)
                {
                    return false;
                }
                if (candidate == timestamp)
                {
                    return true;
                }
            }
            return false;
        }

        private void EvictRawIfNeeded(NodeArchive archive, DateTime newestInsertedTimestamp)
        {
            if (newestInsertedTimestamp > archive.LatestRawTimestamp)
            {
                archive.LatestRawTimestamp = newestInsertedTimestamp;
            }

            if (m_options.RawDataRetentionPeriod > TimeSpan.Zero)
            {
                long cutoffTicks = archive.LatestRawTimestamp.Ticks > m_options.RawDataRetentionPeriod.Ticks
                    ? archive.LatestRawTimestamp.Ticks - m_options.RawDataRetentionPeriod.Ticks
                    : DateTime.MinValue.Ticks;
                var cutoff = new DateTime(cutoffTicks, archive.LatestRawTimestamp.Kind);

                while (archive.Raw.Count > 0)
                {
                    HistoricalValueKey oldest = archive.Raw.Keys.First();
                    if (oldest.SourceTimestamp.ToDateTime() >= cutoff)
                    {
                        break;
                    }
                    archive.Raw.Remove(oldest);
                }
            }

            if (m_options.MaxSamplesPerNode > 0)
            {
                while (archive.Raw.Count > m_options.MaxSamplesPerNode)
                {
                    archive.Raw.Remove(archive.Raw.Keys.First());
                }
            }
        }

        private static void RefreshLatestRawTimestamp(NodeArchive archive)
        {
            archive.LatestRawTimestamp = archive.Raw.Count > 0
                ? archive.Raw.Keys.Last().SourceTimestamp.ToDateTime()
                : DateTime.MinValue;
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

        private static HistorianUpdateOutcome<T> CreateOutcome<T>(
            StatusCode[] statuses,
            List<T>? oldValues = null,
            bool transactionRolledBack = false)
        {
            return new HistorianUpdateOutcome<T>(
                statuses.ToArrayOf(),
                oldValues == null ? [] : oldValues.ToArrayOf(),
                transactionRolledBack: transactionRolledBack);
        }

        private static HistorianResumeToken EncodeTimestamp(DateTime timestamp)
        {
            byte[] buffer = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, timestamp.ToBinary());
            return new HistorianResumeToken(ByteString.From(buffer));
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

        /// <summary>
        /// Encodes an exclusive composite cursor. Paging resumes strictly
        /// after this key, so entries that share a source timestamp are
        /// neither lost nor repeated across a page boundary.
        /// </summary>
        private static HistorianResumeToken EncodeCursor(HistoricalValueKey key)
        {
            return HistorianResumeToken.FromCursor(
                new HistorianResumeCursor(key.SourceTimestamp, key.UniquenessKey, 1));
        }

        private static bool TryDecodeCursor(
            HistorianResumeToken token,
            out HistoricalValueKey key)
        {
            if (token.IsEmpty)
            {
                key = default;
                return false;
            }
            if (!token.TryGetCursor(out HistorianResumeCursor cursor))
            {
                throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
            }
            key = new HistoricalValueKey(cursor.Timestamp, cursor.Key);
            return true;
        }

        private const int kMaxValuesPerPage = 1000;
        private const int kModifiedCursorKeyMagic = 0x4D434D31;
        private const int kModifiedCursorKeyVersion = 1;
        private const int kModifiedCursorKeyLength =
            (2 * sizeof(int)) + sizeof(long);

        private readonly Lock m_lock = new();
        private readonly InMemoryHistorianOptions m_options;
        private readonly NodeIdDictionary<NodeArchive> m_archives = [];
        private readonly NodeIdDictionary<HistorianNodeCapabilities> m_capabilities = [];
        private readonly NodeIdDictionary<List<EventEntry>> m_events = [];
        private readonly NodeIdDictionary<IHistorianStructuredDataKeySelector> m_keySelectors = [];
        private long m_eventSequence;

        private sealed class NodeArchive
        {
            public SortedDictionary<HistoricalValueKey, DataValue> Raw { get; }
                = new(HistoricalValueKeyComparer.Instance);

            public List<ModificationEntry> ModifiedLog { get; } = [];
            public SortedDictionary<DateTime, Annotation> Annotations { get; } = [];
            public DateTime LatestRawTimestamp { get; set; } = DateTime.MinValue;
            public int SequenceCounter;
        }

        private sealed record ModificationEntry(DataValue Value, ModificationInfo Info, int Sequence);

        private readonly record struct ModifiedResumePosition(
            DateTimeUtc SourceTimestamp,
            DateTimeUtc ModificationTime,
            long Sequence,
            bool UseLegacySequenceOnly);

        private sealed record EventEntry(HistorianEventRecord Record, long Sequence);
    }
}
