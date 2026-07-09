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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Receives immutable monitored-item queue snapshots from mirroring queues so they can be persisted to a
    /// shared key/value store.
    /// </summary>
    internal interface IMirroredQueueSink
    {
        /// <summary>
        /// Records the latest data-change queue snapshot for asynchronous mirroring.
        /// </summary>
        /// <param name="monitoredItemId">The owning monitored-item id.</param>
        /// <param name="snapshot">The snapshot to mirror.</param>
        void MirrorDataChangeQueue(uint monitoredItemId, DataChangeQueueSnapshot snapshot);

        /// <summary>
        /// Records the latest event queue snapshot for asynchronous mirroring.
        /// </summary>
        /// <param name="monitoredItemId">The owning monitored-item id.</param>
        /// <param name="snapshot">The snapshot to mirror.</param>
        void MirrorEventQueue(uint monitoredItemId, EventQueueSnapshot snapshot);

        /// <summary>
        /// Removes any mirrored state for the monitored item (e.g. when its queue is disposed).
        /// </summary>
        /// <param name="monitoredItemId">The owning monitored-item id.</param>
        void RemoveQueue(uint monitoredItemId);
    }

    /// <summary>
    /// An immutable snapshot of a data-change queue's contents ordered oldest to newest.
    /// </summary>
    internal sealed class DataChangeQueueSnapshot
    {
        /// <summary>
        /// The queue capacity.
        /// </summary>
        public uint QueueSize { get; init; }

        /// <summary>
        /// Whether the queue tracks per-value errors.
        /// </summary>
        public bool QueueErrors { get; init; }

        /// <summary>
        /// The queued values with their status codes, oldest first.
        /// </summary>
        public (DataValue Value, StatusCode Status)[] Values { get; init; } = [];
    }

    /// <summary>
    /// An immutable snapshot of an event queue's contents ordered oldest to newest.
    /// </summary>
    internal sealed class EventQueueSnapshot
    {
        /// <summary>
        /// The queue capacity.
        /// </summary>
        public uint QueueSize { get; init; }

        /// <summary>
        /// The queued events, oldest first.
        /// </summary>
        public EventFieldList[] Events { get; init; } = [];
    }

    /// <summary>
    /// A shared key/value backed <see cref="IMonitoredItemQueueFactory"/> that continuously mirrors monitored-item
    /// data/event queues so a promoted replica can restore queued-but-unpublished values after a failover
    /// (OPC 10000-4 §6.6; extension).
    /// </summary>
    /// <remarks>
    /// Every mutation of a created queue hands an immutable snapshot to a non-blocking background drain that
    /// coalesces per monitored item and persists it to the shared store, protected at rest by the configured
    /// <see cref="IRecordProtector"/>. On promotion, <see cref="RestoreDataChangeQueueAsync"/> /
    /// <see cref="RestoreEventQueueAsync"/> read the snapshot back and rebuild a mirroring queue that keeps
    /// mirroring for a subsequent failover. Durable (large, disk-backed) queues are out of scope, so
    /// <see cref="SupportsDurableQueues"/> is <c>false</c>.
    /// </remarks>
    public sealed class SharedKeyValueMonitoredItemQueueFactory :
        IMonitoredItemQueueFactory,
        IMirroredQueueSink,
        IAsyncDisposable
    {
        /// <summary>
        /// Creates a shared key/value backed monitored-item queue factory.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="context">The message context used for encoding.</param>
        /// <param name="protector">
        /// Optional record protector applied to every persisted queue snapshot; defaults to pass-through.
        /// </param>
        /// <param name="telemetry">The telemetry context used to create queues and a logger.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/>, <paramref name="context"/> or <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public SharedKeyValueMonitoredItemQueueFactory(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector? protector = null,
            ITelemetryContext? telemetry = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_protector = protector ?? NullRecordProtector.Instance;
            m_logger = telemetry.CreateLogger<SharedKeyValueMonitoredItemQueueFactory>();
            m_channel = Channel.CreateBounded<QueueMirrorCommand>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            m_drainTask = Task.Run(DrainAsync);
        }

        /// <inheritdoc/>
        public bool SupportsDurableQueues => false;

        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(bool isDurable, uint monitoredItemId)
        {
            return new MirroringDataChangeMonitoredItemQueue(monitoredItemId, this, m_telemetry);
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue CreateEventQueue(bool isDurable, uint monitoredItemId)
        {
            return new MirroringEventMonitoredItemQueue(monitoredItemId, this, m_telemetry);
        }

        /// <summary>
        /// Restores a data-change queue from the shared store, rebuilding a mirroring queue when a snapshot exists.
        /// </summary>
        /// <param name="monitoredItemId">The owning monitored-item id.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The restored queue, or <c>null</c> when no snapshot is stored.</returns>
        public async ValueTask<IDataChangeMonitoredItemQueue?> RestoreDataChangeQueueAsync(
            uint monitoredItemId,
            CancellationToken cancellationToken = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(DataChangeKeyFor(monitoredItemId), cancellationToken)
                .ConfigureAwait(false);
            if (!found || !m_protector.TryUnprotect(value, out ByteString payload))
            {
                return null;
            }

            DataChangeQueueSnapshot? snapshot = DecodeDataChangeSnapshot(payload);
            if (snapshot == null || snapshot.QueueSize == 0)
            {
                return null;
            }

            var queue = new MirroringDataChangeMonitoredItemQueue(monitoredItemId, this, m_telemetry);
            queue.RestoreFrom(snapshot);
            return queue;
        }

        /// <summary>
        /// Restores an event queue from the shared store, rebuilding a mirroring queue when a snapshot exists.
        /// </summary>
        /// <param name="monitoredItemId">The owning monitored-item id.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The restored queue, or <c>null</c> when no snapshot is stored.</returns>
        public async ValueTask<IEventMonitoredItemQueue?> RestoreEventQueueAsync(
            uint monitoredItemId,
            CancellationToken cancellationToken = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(EventKeyFor(monitoredItemId), cancellationToken)
                .ConfigureAwait(false);
            if (!found || !m_protector.TryUnprotect(value, out ByteString payload))
            {
                return null;
            }

            EventQueueSnapshot? snapshot = DecodeEventSnapshot(payload);
            if (snapshot == null || snapshot.QueueSize == 0)
            {
                return null;
            }

            var queue = new MirroringEventMonitoredItemQueue(monitoredItemId, this, m_telemetry);
            queue.RestoreFrom(snapshot);
            return queue;
        }

        /// <summary>
        /// Deletes mirrored queue snapshots that no longer belong to a live monitored item.
        /// </summary>
        /// <param name="liveMonitoredItemIds">The set of monitored-item ids to keep.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public async ValueTask CleanupAsync(
            IReadOnlyCollection<uint> liveMonitoredItemIds,
            CancellationToken cancellationToken = default)
        {
            var live = new HashSet<uint>(liveMonitoredItemIds ?? []);
            var operations = new List<Task>();

            await foreach (KeyValuePair<string, ByteString> pair in m_store
                .ScanAsync(DataChangePrefix, cancellationToken)
                .ConfigureAwait(false))
            {
                if (TryParseId(pair.Key, DataChangePrefix, out uint id) && !live.Contains(id))
                {
                    operations.Add(m_store.DeleteAsync(pair.Key, cancellationToken).AsTask());
                }
            }

            await foreach (KeyValuePair<string, ByteString> pair in m_store
                .ScanAsync(EventPrefix, cancellationToken)
                .ConfigureAwait(false))
            {
                if (TryParseId(pair.Key, EventPrefix, out uint id) && !live.Contains(id))
                {
                    operations.Add(m_store.DeleteAsync(pair.Key, cancellationToken).AsTask());
                }
            }

            if (operations.Count > 0)
            {
                await Task.WhenAll(operations).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        void IMirroredQueueSink.MirrorDataChangeQueue(uint monitoredItemId, DataChangeQueueSnapshot snapshot)
        {
            lock (m_pendingLock)
            {
                m_pendingDataChange[monitoredItemId] = snapshot;
                m_pendingRemovals.Remove(monitoredItemId);
            }

            SignalDrain();
        }

        /// <inheritdoc/>
        void IMirroredQueueSink.MirrorEventQueue(uint monitoredItemId, EventQueueSnapshot snapshot)
        {
            lock (m_pendingLock)
            {
                m_pendingEvent[monitoredItemId] = snapshot;
                m_pendingRemovals.Remove(monitoredItemId);
            }

            SignalDrain();
        }

        /// <inheritdoc/>
        void IMirroredQueueSink.RemoveQueue(uint monitoredItemId)
        {
            lock (m_pendingLock)
            {
                m_pendingDataChange.Remove(monitoredItemId);
                m_pendingEvent.Remove(monitoredItemId);
                m_pendingRemovals.Add(monitoredItemId);
            }

            SignalDrain();
        }

        /// <summary>
        /// Flushes queued mirror commands. Intended for tests and graceful shutdown.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        internal async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await m_channel.Writer
                .WriteAsync(new QueueMirrorCommand(completion), cancellationToken)
                .ConfigureAwait(false);
            await completion.Task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_channel.Writer.TryComplete();
            m_drainCts.Cancel();
            m_drainCts.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_channel.Writer.TryComplete();
            m_drainCts.Cancel();
            try
            {
                await m_drainTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            m_drainCts.Dispose();
        }

        private void SignalDrain()
        {
            if (m_channel.Writer.TryWrite(QueueMirrorCommand.Signal))
            {
                return;
            }

            if (Interlocked.Exchange(ref m_overflowWarningWritten, 1) == 0)
            {
                m_logger.LogWarning(
                    "The monitored-item queue mirror channel is full; updates are coalesced until the drain catches up.");
            }
        }

        private async Task DrainAsync()
        {
            try
            {
                await foreach (QueueMirrorCommand command in m_channel.Reader
                    .ReadAllAsync(m_drainCts.Token)
                    .ConfigureAwait(false))
                {
                    try
                    {
                        await DrainPendingAsync(m_drainCts.Token).ConfigureAwait(false);
                        command.Completion?.SetResult(true);
                    }
                    catch (OperationCanceledException) when (m_drainCts.IsCancellationRequested)
                    {
                        command.Completion?.SetCanceled();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(ex, "Failed to mirror monitored-item queue state.");
                        command.Completion?.SetException(ex);
                    }
                }
            }
            catch (OperationCanceledException) when (m_drainCts.IsCancellationRequested)
            {
            }
        }

        private async ValueTask DrainPendingAsync(CancellationToken cancellationToken)
        {
            KeyValuePair<uint, DataChangeQueueSnapshot>[] dataChange;
            KeyValuePair<uint, EventQueueSnapshot>[] events;
            uint[] removals;
            lock (m_pendingLock)
            {
                dataChange = [.. m_pendingDataChange];
                events = [.. m_pendingEvent];
                removals = [.. m_pendingRemovals];
                m_pendingDataChange.Clear();
                m_pendingEvent.Clear();
                m_pendingRemovals.Clear();
            }

            var operations = new List<Task>();
            foreach (KeyValuePair<uint, DataChangeQueueSnapshot> entry in dataChange)
            {
                ByteString payload = m_protector.Protect(EncodeDataChangeSnapshot(entry.Value));
                operations.Add(m_store.SetAsync(DataChangeKeyFor(entry.Key), payload, cancellationToken).AsTask());
            }
            foreach (KeyValuePair<uint, EventQueueSnapshot> entry in events)
            {
                ByteString payload = m_protector.Protect(EncodeEventSnapshot(entry.Value));
                operations.Add(m_store.SetAsync(EventKeyFor(entry.Key), payload, cancellationToken).AsTask());
            }
            foreach (uint id in removals)
            {
                operations.Add(m_store.DeleteAsync(DataChangeKeyFor(id), cancellationToken).AsTask());
                operations.Add(m_store.DeleteAsync(EventKeyFor(id), cancellationToken).AsTask());
            }

            if (operations.Count == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(operations).ConfigureAwait(false);
            }
            catch
            {
                Requeue(dataChange, events, removals);
                throw;
            }
        }

        private void Requeue(
            KeyValuePair<uint, DataChangeQueueSnapshot>[] dataChange,
            KeyValuePair<uint, EventQueueSnapshot>[] events,
            uint[] removals)
        {
            lock (m_pendingLock)
            {
                foreach (KeyValuePair<uint, DataChangeQueueSnapshot> entry in dataChange)
                {
                    if (!m_pendingDataChange.ContainsKey(entry.Key) && !m_pendingRemovals.Contains(entry.Key))
                    {
                        m_pendingDataChange[entry.Key] = entry.Value;
                    }
                }
                foreach (KeyValuePair<uint, EventQueueSnapshot> entry in events)
                {
                    if (!m_pendingEvent.ContainsKey(entry.Key) && !m_pendingRemovals.Contains(entry.Key))
                    {
                        m_pendingEvent[entry.Key] = entry.Value;
                    }
                }
                foreach (uint id in removals)
                {
                    if (!m_pendingDataChange.ContainsKey(id) && !m_pendingEvent.ContainsKey(id))
                    {
                        m_pendingRemovals.Add(id);
                    }
                }
            }
        }

        private ByteString EncodeDataChangeSnapshot(DataChangeQueueSnapshot snapshot)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteInt32(null, QueueSnapshotFormatVersion);
            encoder.WriteStringArray(null, m_context.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, m_context.ServerUris.ToArrayOf());
            encoder.WriteUInt32(null, snapshot.QueueSize);
            encoder.WriteBoolean(null, snapshot.QueueErrors);
            encoder.WriteInt32(null, snapshot.Values.Length);
            foreach ((DataValue value, StatusCode status) in snapshot.Values)
            {
                encoder.WriteDataValue(null, value);
                encoder.WriteStatusCode(null, status);
            }
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : ByteString.From(buffer);
        }

        private DataChangeQueueSnapshot? DecodeDataChangeSnapshot(ByteString payload)
        {
            using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
            int version = decoder.ReadInt32(null);
            if (version != QueueSnapshotFormatVersion)
            {
                return null;
            }

            decoder.SetMappingTables(
                CreateNamespaceTable(decoder.ReadStringArray(null)),
                CreateStringTable(decoder.ReadStringArray(null)));
            uint queueSize = decoder.ReadUInt32(null);
            bool queueErrors = decoder.ReadBoolean(null);
            int count = decoder.ReadInt32(null);
            var values = new (DataValue, StatusCode)[count];
            for (int index = 0; index < count; index++)
            {
                DataValue value = decoder.ReadDataValue(null);
                StatusCode status = decoder.ReadStatusCode(null);
                values[index] = (value, status);
            }

            return new DataChangeQueueSnapshot
            {
                QueueSize = queueSize,
                QueueErrors = queueErrors,
                Values = values
            };
        }

        private ByteString EncodeEventSnapshot(EventQueueSnapshot snapshot)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteInt32(null, QueueSnapshotFormatVersion);
            encoder.WriteStringArray(null, m_context.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, m_context.ServerUris.ToArrayOf());
            encoder.WriteUInt32(null, snapshot.QueueSize);
            encoder.WriteInt32(null, snapshot.Events.Length);
            foreach (EventFieldList value in snapshot.Events)
            {
                encoder.WriteEncodeable(null, value);
            }
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : ByteString.From(buffer);
        }

        private EventQueueSnapshot? DecodeEventSnapshot(ByteString payload)
        {
            using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
            int version = decoder.ReadInt32(null);
            if (version != QueueSnapshotFormatVersion)
            {
                return null;
            }

            decoder.SetMappingTables(
                CreateNamespaceTable(decoder.ReadStringArray(null)),
                CreateStringTable(decoder.ReadStringArray(null)));
            uint queueSize = decoder.ReadUInt32(null);
            int count = decoder.ReadInt32(null);
            var events = new EventFieldList[count];
            for (int index = 0; index < count; index++)
            {
                events[index] = decoder.ReadEncodeable<EventFieldList>(null);
            }

            return new EventQueueSnapshot
            {
                QueueSize = queueSize,
                Events = events
            };
        }

        private static NamespaceTable CreateNamespaceTable(ArrayOf<string?> namespaceUris)
        {
            return new NamespaceTable([.. namespaceUris.Memory.ToArray().Where(s => s != null).Select(s => s!)]);
        }

        private static StringTable CreateStringTable(ArrayOf<string?> serverUris)
        {
            return new StringTable([.. serverUris.Memory.ToArray().Where(s => s != null).Select(s => s!)]);
        }

        private static string DataChangeKeyFor(uint monitoredItemId)
        {
            return DataChangePrefix + monitoredItemId.ToString("D", CultureInfo.InvariantCulture);
        }

        private static string EventKeyFor(uint monitoredItemId)
        {
            return EventPrefix + monitoredItemId.ToString("D", CultureInfo.InvariantCulture);
        }

        private static bool TryParseId(string key, string prefix, out uint monitoredItemId)
        {
#if NETFRAMEWORK
            return uint.TryParse(
                key[prefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out monitoredItemId);
#else
            return uint.TryParse(
                key.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out monitoredItemId);
#endif
        }

        private const int QueueSnapshotFormatVersion = 1;
        private const int ChannelCapacity = 1024;
        private const string DataChangePrefix = "monitored-item-queue/data/";
        private const string EventPrefix = "monitored-item-queue/event/";
        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger<SharedKeyValueMonitoredItemQueueFactory> m_logger;
        private readonly Channel<QueueMirrorCommand> m_channel;
        private readonly CancellationTokenSource m_drainCts = new();
        private readonly Task m_drainTask;
        private readonly Lock m_pendingLock = new();
        private readonly Dictionary<uint, DataChangeQueueSnapshot> m_pendingDataChange = [];
        private readonly Dictionary<uint, EventQueueSnapshot> m_pendingEvent = [];
        private readonly HashSet<uint> m_pendingRemovals = [];
        private int m_overflowWarningWritten;
        private int m_disposed;

        /// <summary>
        /// Command enqueued to the mirror worker; carries an optional completion source used to await the mirror
        /// operation, or acts as a wake-up signal when none is supplied.
        /// </summary>
        private sealed class QueueMirrorCommand
        {
            /// <summary>
            /// Gets a shared signal-only command used to wake the mirror worker without awaiting completion.
            /// </summary>
            public static QueueMirrorCommand Signal { get; } = new(null);

            /// <summary>
            /// Initializes a new instance of the <see cref="QueueMirrorCommand"/> class.
            /// </summary>
            /// <param name="completion">
            /// The completion source signalled when the command has been processed, or <c>null</c> for a
            /// signal-only command.
            /// </param>
            public QueueMirrorCommand(TaskCompletionSource<bool>? completion)
            {
                Completion = completion;
            }

            /// <summary>
            /// Gets the completion source signalled when the command has been processed, if any.
            /// </summary>
            public TaskCompletionSource<bool>? Completion { get; }
        }
    }
}
