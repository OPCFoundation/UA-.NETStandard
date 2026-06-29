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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Shared key/value backed <see cref="ISubscriptionStore"/> for HotAndMirrored/Transparent subscription state
    /// synchronization.
    /// </summary>
    /// <remarks>
    /// This store supports OPC 10000-4 §6.6.2.4.4 and §6.6.2.4.5.5 by mirroring subscription definitions,
    /// retransmission state for <c>Republish</c>, and continuation-point envelopes (§6.6.2.2). Continuation
    /// mirroring is envelope-only: the opaque continuation token, owner SessionId, kind, and expiry metadata are
    /// persisted so a backup can reject, release, or correlate the token, but Browse/Query/History continuation
    /// enumerator internals owned by a local node manager remain process-local runtime state and are not resumed by
    /// this store.
    /// Monitored-item data/event queues remain runtime state and are not restored by this store.
    /// </remarks>
    public sealed class SharedKeyValueSubscriptionStore :
        ISubscriptionStore,
        ISubscriptionRetransmissionDeltaStore,
        IContinuationPointStore,
        IAsyncDisposable
    {
        /// <summary>
        /// Creates a subscription definition store over a shared key/value backend.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="context">The message context for encoding.</param>
        /// <param name="protector">
        /// Optional record protector applied to every encoded subscription entry; defaults to pass-through.
        /// </param>
        /// <param name="logger">Optional logger for asynchronous mirror failures.</param>
        public SharedKeyValueSubscriptionStore(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector? protector = null,
            ILogger<SharedKeyValueSubscriptionStore>? logger = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = protector ?? NullRecordProtector.Instance;
            m_logger = logger;
            m_definitionCache = s_definitionCaches.GetValue(store, static _ => new SharedDefinitionCache());
            m_channel = Channel.CreateBounded<MirrorCommand>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            m_drainTask = Task.Run(DrainAsync);
        }

        /// <inheritdoc/>
        public bool StoreSubscriptions(IEnumerable<IStoredSubscription> subscriptions)
        {
            if (subscriptions == null)
            {
                throw new ArgumentNullException(nameof(subscriptions));
            }

            List<StoredSubscription> snapshot = subscriptions.Select(CloneSubscription).ToList();
            var liveIds = new HashSet<uint>();
            foreach (StoredSubscription subscription in snapshot)
            {
                liveIds.Add(subscription.Id);
                string key = KeyFor(subscription.Id);
                Complete(m_store.SetAsync(key, m_protector.Protect(Encode(subscription))));
            }

            uint[] removedIds;
            lock (m_definitionCache.Lock)
            {
                removedIds = m_definitionCache.Subscriptions.Keys
                    .Where(id => !liveIds.Contains(id))
                    .ToArray();
                foreach (StoredSubscription subscription in snapshot)
                {
                    m_definitionCache.Subscriptions[subscription.Id] = CloneSubscription(subscription);
                }
                foreach (uint subscriptionId in removedIds)
                {
                    m_definitionCache.Subscriptions.Remove(subscriptionId);
                }
            }

            foreach (uint subscriptionId in removedIds)
            {
                Complete(m_store.DeleteAsync(KeyFor(subscriptionId)));
                DeleteRetransmissionState(subscriptionId);
            }

            return true;
        }

        /// <inheritdoc/>
        public RestoreSubscriptionResult RestoreSubscriptions()
        {
            lock (m_definitionCache.Lock)
            {
                return new RestoreSubscriptionResult(
                    true,
                    m_definitionCache.Subscriptions.Values.Select(CloneSubscription).ToList());
            }
        }

        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(uint monitoredItemId)
        {
            return null!;
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId)
        {
            return null!;
        }

        /// <inheritdoc/>
        public void OnSubscriptionRestoreComplete(Dictionary<uint, ArrayOf<uint>> createdSubscriptions)
        {
            if (createdSubscriptions == null)
            {
                throw new ArgumentNullException(nameof(createdSubscriptions));
            }

            uint[] removedIds;
            lock (m_definitionCache.Lock)
            {
                var liveIds = new HashSet<uint>(createdSubscriptions.Keys);
                removedIds = m_definitionCache.Subscriptions.Keys
                    .Where(id => !liveIds.Contains(id))
                    .ToArray();
                foreach (uint subscriptionId in removedIds)
                {
                    m_definitionCache.Subscriptions.Remove(subscriptionId);
                }
            }

            foreach (uint subscriptionId in removedIds)
            {
                Complete(m_store.DeleteAsync(KeyFor(subscriptionId)));
                DeleteRetransmissionState(subscriptionId);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<SubscriptionRetransmissionState?> LoadRetransmissionStateAsync(
            uint subscriptionId,
            CancellationToken cancellationToken = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(RetransmissionStateKeyFor(subscriptionId), cancellationToken)
                .ConfigureAwait(false);
            if (!found || !m_protector.TryUnprotect(value, out ByteString payload))
            {
                return null;
            }

            using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
            int version = decoder.ReadInt32(null);
            if (version is not RetransmissionStateFormatVersion and not LegacyRetransmissionStateFormatVersion)
            {
                return null;
            }

            var state = new SubscriptionRetransmissionState
            {
                NextSequenceNumber = decoder.ReadUInt32(null)
            };
            NamespaceTable? namespaceUris = null;
            StringTable? serverUris = null;
            if (version == RetransmissionStateFormatVersion)
            {
                namespaceUris = CreateNamespaceTable(decoder.ReadStringArray(null));
                serverUris = CreateStringTable(decoder.ReadStringArray(null));
            }

            var messages = new List<NotificationMessage>();
            await foreach (KeyValuePair<string, ByteString> pair in m_store
                .ScanAsync(RetransmissionMessagePrefixFor(subscriptionId), cancellationToken)
                .ConfigureAwait(false))
            {
                if (m_protector.TryUnprotect(pair.Value, out ByteString messagePayload))
                {
                    NotificationMessage message = DecodeNotificationMessage(messagePayload, namespaceUris, serverUris);
                    messages.Add(message);
                }
            }
            messages.Sort(static (left, right) => left.SequenceNumber.CompareTo(right.SequenceNumber));
            state.SentMessages = [.. messages];
            return state;
        }

        /// <inheritdoc/>
        public void StoreRetransmissionState(
            uint subscriptionId,
            uint nextSequenceNumber,
            ArrayOf<NotificationMessage> sentMessages)
        {
            lock (m_retransmissionLock)
            {
                PendingRetransmissionState state = GetPendingState(subscriptionId);
                state.NextSequenceNumber = nextSequenceNumber;
                state.StateDirty = true;

                var liveSequences = new HashSet<uint>();
                foreach (NotificationMessage message in sentMessages)
                {
                    liveSequences.Add(message.SequenceNumber);
                    if (state.KnownMessages.Add(message.SequenceNumber))
                    {
                        state.PendingMessages[message.SequenceNumber] = message;
                    }
                    state.PendingDeletes.Remove(message.SequenceNumber);
                }

                foreach (uint known in state.KnownMessages.ToArray())
                {
                    if (!liveSequences.Contains(known))
                    {
                        state.KnownMessages.Remove(known);
                        state.PendingMessages.Remove(known);
                        state.PendingDeletes.Add(known);
                    }
                }
            }

            SignalDrain();
        }

        /// <inheritdoc/>
        public void StoreRetransmissionStateDelta(
            uint subscriptionId,
            uint nextSequenceNumber,
            ArrayOf<NotificationMessage> addedMessages,
            ArrayOf<uint> removedSequenceNumbers)
        {
            lock (m_retransmissionLock)
            {
                PendingRetransmissionState state = GetPendingState(subscriptionId);
                state.NextSequenceNumber = nextSequenceNumber;
                state.StateDirty = true;

                foreach (uint sequenceNumber in removedSequenceNumbers)
                {
                    state.KnownMessages.Remove(sequenceNumber);
                    state.PendingMessages.Remove(sequenceNumber);
                    state.PendingDeletes.Add(sequenceNumber);
                }

                foreach (NotificationMessage message in addedMessages)
                {
                    state.KnownMessages.Add(message.SequenceNumber);
                    state.PendingMessages[message.SequenceNumber] = message;
                    state.PendingDeletes.Remove(message.SequenceNumber);
                }
            }

            SignalDrain();
        }

        /// <summary>
        /// Flushes queued retransmission mirror commands.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        internal async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await m_channel.Writer
                .WriteAsync(new MirrorCommand(completion), cancellationToken)
                .ConfigureAwait(false);
            await completion.Task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_channel.Writer.TryComplete();
            await m_drainTask.ConfigureAwait(false);
            m_drainCts.Dispose();
        }

        private PendingRetransmissionState GetPendingState(uint subscriptionId)
        {
            if (!m_pendingRetransmission.TryGetValue(subscriptionId, out PendingRetransmissionState? state))
            {
                state = new PendingRetransmissionState();
                m_pendingRetransmission[subscriptionId] = state;
            }
            return state;
        }

        /// <inheritdoc/>
        public void AcknowledgeNotification(uint subscriptionId, uint sequenceNumber)
        {
            lock (m_retransmissionLock)
            {
                PendingRetransmissionState state = GetPendingState(subscriptionId);
                state.KnownMessages.Remove(sequenceNumber);
                state.PendingMessages.Remove(sequenceNumber);
                state.PendingDeletes.Add(sequenceNumber);
            }

            SignalDrain();
        }

        /// <inheritdoc/>
        public void StoreContinuationPoint(ContinuationPointEnvelope envelope)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            lock (m_continuationPointLock)
            {
                string key = ContinuationPointKeyFor(envelope.OwnerSessionId, envelope.Kind, envelope.Id);
                m_pendingContinuationPointStores[key] = envelope;
                m_pendingContinuationPointDeletes.Remove(
                    key);
            }

            SignalDrain();
        }

        /// <inheritdoc/>
        public void RemoveContinuationPoint(NodeId ownerSessionId, ContinuationPointKind kind, Guid id)
        {
            if (ownerSessionId.IsNull)
            {
                return;
            }

            string key = ContinuationPointKeyFor(ownerSessionId, kind, id);
            lock (m_continuationPointLock)
            {
                m_pendingContinuationPointStores.Remove(key);
                m_pendingContinuationPointDeletes.Add(key);
            }

            SignalDrain();
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ContinuationPointEnvelope>> LoadContinuationPointsAsync(
            NodeId ownerSessionId,
            CancellationToken cancellationToken = default)
        {
            if (ownerSessionId.IsNull)
            {
                return [];
            }

            var envelopes = new List<ContinuationPointEnvelope>();
            await foreach (KeyValuePair<string, ByteString> pair in m_store
                .ScanAsync(ContinuationPointPrefixFor(ownerSessionId), cancellationToken)
                .ConfigureAwait(false))
            {
                if (m_protector.TryUnprotect(pair.Value, out ByteString payload))
                {
                    ContinuationPointEnvelope? envelope = DecodeContinuationPointEnvelope(payload);
                    if (envelope != null)
                    {
                        envelopes.Add(envelope);
                    }
                }
            }
            return [.. envelopes];
        }

        private void SignalDrain()
        {
            if (m_channel.Writer.TryWrite(MirrorCommand.Signal))
            {
                return;
            }

            if (Interlocked.Exchange(ref m_overflowWarningWritten, 1) == 0)
            {
                m_logger?.LogWarning(
                    "The shared-state mirror channel is full; updates are coalesced until the drain catches up.");
            }
        }

        private void DeleteRetransmissionState(uint subscriptionId)
        {
            lock (m_retransmissionLock)
            {
                PendingRetransmissionState state = GetPendingState(subscriptionId);
                state.ClearRequested = true;
                state.StateDirty = false;
                state.KnownMessages.Clear();
                state.PendingMessages.Clear();
                state.PendingDeletes.Clear();
            }

            SignalDrain();
        }

        private async Task DrainAsync()
        {
            await foreach (MirrorCommand command in m_channel.Reader
                .ReadAllAsync(m_drainCts.Token)
                .ConfigureAwait(false))
            {
                try
                {
                    await DrainPendingAsync(m_drainCts.Token).ConfigureAwait(false);
                    command.Completion?.SetResult(true);
                }
                catch (Exception ex)
                {
                    m_logger?.LogWarning(ex, "Failed to mirror subscription retransmission state.");
                    command.Completion?.SetException(ex);
                }
            }
        }

        private async ValueTask DrainPendingAsync(CancellationToken cancellationToken)
        {
            List<RetransmissionBatch> batches = TakePendingBatches();
            foreach (RetransmissionBatch batch in batches)
            {
                try
                {
                    if (batch.ClearRequested)
                    {
                        await m_store.DeleteAsync(
                                RetransmissionStateKeyFor(batch.SubscriptionId),
                                cancellationToken)
                            .ConfigureAwait(false);
                        await foreach (KeyValuePair<string, ByteString> pair in m_store
                            .ScanAsync(RetransmissionMessagePrefixFor(batch.SubscriptionId), cancellationToken)
                            .ConfigureAwait(false))
                        {
                            await m_store.DeleteAsync(pair.Key, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (batch.StateDirty)
                    {
                        await m_store.SetAsync(
                                RetransmissionStateKeyFor(batch.SubscriptionId),
                                m_protector.Protect(EncodeRetransmissionState(batch.NextSequenceNumber)),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var operations = new List<Task>(batch.Messages.Length + batch.Deletes.Length);
                    foreach (NotificationMessage message in batch.Messages)
                    {
                        operations.Add(m_store.SetAsync(
                                RetransmissionMessageKeyFor(batch.SubscriptionId, message.SequenceNumber),
                                m_protector.Protect(EncodeNotificationMessage(message)),
                                cancellationToken)
                            .AsTask());
                    }

                    foreach (uint sequenceNumber in batch.Deletes)
                    {
                        operations.Add(m_store.DeleteAsync(
                                RetransmissionMessageKeyFor(batch.SubscriptionId, sequenceNumber),
                                cancellationToken)
                            .AsTask());
                    }
                    await RunBatchOperationsAsync(operations).ConfigureAwait(false);
                }
                catch
                {
                    Requeue(batch);
                    throw;
                }
            }

            ContinuationPointBatch continuationPointBatch = TakePendingContinuationPointBatch();
            try
            {
                foreach (ContinuationPointEnvelope envelope in continuationPointBatch.Stores)
                {
                    await m_store.SetAsync(
                            ContinuationPointKeyFor(envelope.OwnerSessionId, envelope.Kind, envelope.Id),
                            m_protector.Protect(EncodeContinuationPointEnvelope(envelope)),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                foreach (string key in continuationPointBatch.Deletes)
                {
                    await m_store.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                Requeue(continuationPointBatch);
                throw;
            }
        }

        private List<RetransmissionBatch> TakePendingBatches()
        {
            lock (m_retransmissionLock)
            {
                var batches = new List<RetransmissionBatch>(m_pendingRetransmission.Count);
                var completedClears = new List<uint>();
                foreach (KeyValuePair<uint, PendingRetransmissionState> entry in m_pendingRetransmission)
                {
                    uint subscriptionId = entry.Key;
                    PendingRetransmissionState state = entry.Value;
                    if (!state.ClearRequested &&
                        !state.StateDirty &&
                        state.PendingMessages.Count == 0 &&
                        state.PendingDeletes.Count == 0)
                    {
                        continue;
                    }

                    batches.Add(new RetransmissionBatch(
                        subscriptionId,
                        state.NextSequenceNumber,
                        state.StateDirty,
                        state.PendingMessages.Values.ToArray(),
                        state.PendingDeletes.ToArray(),
                        state.ClearRequested));
                    if (state.ClearRequested &&
                        !state.StateDirty &&
                        state.PendingMessages.Count == 0)
                    {
                        completedClears.Add(subscriptionId);
                    }
                    state.ClearRequested = false;
                    state.StateDirty = false;
                    state.PendingMessages.Clear();
                    state.PendingDeletes.Clear();
                }

                foreach (uint subscriptionId in completedClears)
                {
                    m_pendingRetransmission.Remove(subscriptionId);
                }

                return batches;
            }
        }

        private void Requeue(RetransmissionBatch batch)
        {
            lock (m_retransmissionLock)
            {
                PendingRetransmissionState state = GetPendingState(batch.SubscriptionId);
                state.ClearRequested |= batch.ClearRequested;
                state.NextSequenceNumber = batch.NextSequenceNumber;
                state.StateDirty |= batch.StateDirty;
                foreach (NotificationMessage message in batch.Messages)
                {
                    state.PendingMessages[message.SequenceNumber] = message;
                }
                foreach (uint sequenceNumber in batch.Deletes)
                {
                    state.PendingDeletes.Add(sequenceNumber);
                }
            }

            SignalDrain();
        }

        private ContinuationPointBatch TakePendingContinuationPointBatch()
        {
            lock (m_continuationPointLock)
            {
                var batch = new ContinuationPointBatch(
                    m_pendingContinuationPointStores.Values.ToArray(),
                    m_pendingContinuationPointDeletes.ToArray());
                m_pendingContinuationPointStores.Clear();
                m_pendingContinuationPointDeletes.Clear();
                return batch;
            }
        }

        private void Requeue(ContinuationPointBatch batch)
        {
            lock (m_continuationPointLock)
            {
                foreach (ContinuationPointEnvelope envelope in batch.Stores)
                {
                    m_pendingContinuationPointStores[
                        ContinuationPointKeyFor(envelope.OwnerSessionId, envelope.Kind, envelope.Id)] = envelope;
                }
                foreach (string key in batch.Deletes)
                {
                    m_pendingContinuationPointDeletes.Add(key);
                }
            }

            SignalDrain();
        }

        /// <summary>
        /// Computes the shared-store key for a subscription definition.
        /// </summary>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <returns>The store key.</returns>
        internal static string KeyFor(uint subscriptionId)
        {
            return Prefix + subscriptionId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string RetransmissionStateKeyFor(uint subscriptionId)
        {
            return RetransmissionPrefix +
                subscriptionId.ToString("D", System.Globalization.CultureInfo.InvariantCulture) +
                "/state";
        }

        internal static string RetransmissionMessageKeyFor(uint subscriptionId, uint sequenceNumber)
        {
            return RetransmissionMessagePrefixFor(subscriptionId) +
                sequenceNumber.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string ContinuationPointKeyFor(
            NodeId ownerSessionId,
            ContinuationPointKind kind,
            Guid id)
        {
            return ContinuationPointPrefixFor(ownerSessionId) +
                ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "/" +
                id.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static StoredSubscription CloneSubscription(IStoredSubscription subscription)
        {
            return new StoredSubscription
            {
                Id = subscription.Id,
                IsDurable = subscription.IsDurable,
                LifetimeCounter = subscription.LifetimeCounter,
                MaxLifetimeCount = subscription.MaxLifetimeCount,
                MaxKeepaliveCount = subscription.MaxKeepaliveCount,
                MaxMessageCount = subscription.MaxMessageCount,
                MaxNotificationsPerPublish = subscription.MaxNotificationsPerPublish,
                PublishingInterval = subscription.PublishingInterval,
                Priority = subscription.Priority,
                LastSentMessage = subscription.LastSentMessage,
                SequenceNumber = subscription.SequenceNumber,
                UserIdentityToken = subscription.UserIdentityToken,
                SentMessages = subscription.SentMessages ?? [],
                MonitoredItems = subscription.MonitoredItems.Select(CloneMonitoredItem).ToList()
            };
        }

        private static StoredMonitoredItem CloneMonitoredItem(IStoredMonitoredItem item)
        {
            return new StoredMonitoredItem
            {
                IsRestored = item.IsRestored,
                AlwaysReportUpdates = item.AlwaysReportUpdates,
                AttributeId = item.AttributeId,
                ClientHandle = item.ClientHandle,
                DiagnosticsMasks = item.DiagnosticsMasks,
                DiscardOldest = item.DiscardOldest,
                Encoding = item.Encoding,
                Id = item.Id,
                IndexRange = item.IndexRange,
                ParsedIndexRange = item.ParsedIndexRange,
                IsDurable = item.IsDurable,
                LastError = item.LastError,
                LastValue = item.LastValue,
                MonitoringMode = item.MonitoringMode,
                NodeId = item.NodeId,
                FilterToUse = item.FilterToUse,
                OriginalFilter = item.OriginalFilter,
                QueueSize = item.QueueSize,
                Range = item.Range,
                SamplingInterval = item.SamplingInterval,
                SourceSamplingInterval = item.SourceSamplingInterval,
                SubscriptionId = item.SubscriptionId,
                TimestampsToReturn = item.TimestampsToReturn,
                TypeMask = item.TypeMask
            };
        }

        private void Complete(ValueTask operation)
        {
            if (!operation.IsCompletedSuccessfully)
            {
                _ = ObserveAsync(operation);
            }
        }

        private void Complete<T>(ValueTask<T> operation)
        {
            if (!operation.IsCompletedSuccessfully)
            {
                _ = ObserveAsync(operation);
            }
        }

        private async Task ObserveAsync(ValueTask operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger?.LogWarning(ex, "A shared subscription-store operation failed.");
            }
        }

        private async Task ObserveAsync<T>(ValueTask<T> operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger?.LogWarning(ex, "A shared subscription-store operation failed.");
            }
        }

        private static async ValueTask RunBatchOperationsAsync(List<Task> operations)
        {
            if (operations.Count == 0)
            {
                return;
            }

            await Task.WhenAll(operations).ConfigureAwait(false);
        }

        private ByteString Encode(StoredSubscription subscription)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteInt32(null, DefinitionFormatVersion);
            encoder.WriteStringArray(null, m_context.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, m_context.ServerUris.ToArrayOf());
            EncodeSubscription(encoder, subscription);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : ByteString.From(buffer);
        }

        private ByteString EncodeRetransmissionState(uint nextSequenceNumber)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteInt32(null, RetransmissionStateFormatVersion);
            encoder.WriteUInt32(null, nextSequenceNumber);
            encoder.WriteStringArray(null, m_context.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, m_context.ServerUris.ToArrayOf());
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : ByteString.From(buffer);
        }

        private ByteString EncodeNotificationMessage(NotificationMessage message)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteInt32(null, NotificationMessageFormatVersion);
            encoder.WriteEncodeable(null, message);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : ByteString.From(buffer);
        }

        private ByteString EncodeContinuationPointEnvelope(ContinuationPointEnvelope envelope)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteInt32(null, ContinuationPointFormatVersion);
            encoder.WriteByteString(null, ByteString.From(envelope.Id.ToByteArray()));
            encoder.WriteNodeId(null, envelope.OwnerSessionId);
            encoder.WriteEnumerated(null, envelope.Kind);
            encoder.WriteNodeId(null, envelope.BrowseNodeId);
            encoder.WriteBoolean(null, envelope.View != null);
            if (envelope.View != null)
            {
                encoder.WriteEncodeable(null, envelope.View);
            }
            encoder.WriteUInt32(null, envelope.MaxResultsToReturn);
            encoder.WriteEnumerated(null, envelope.BrowseDirection);
            encoder.WriteNodeId(null, envelope.ReferenceTypeId);
            encoder.WriteBoolean(null, envelope.IncludeSubtypes);
            encoder.WriteUInt32(null, envelope.NodeClassMask);
            encoder.WriteEnumerated(null, envelope.ResultMask);
            encoder.WriteInt32(null, envelope.Index);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : ByteString.From(buffer);
        }

        private NotificationMessage DecodeNotificationMessage(
            ByteString payload,
            NamespaceTable? namespaceUris,
            StringTable? serverUris)
        {
            try
            {
                using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
                int version = decoder.ReadInt32(null);
                if (version != NotificationMessageFormatVersion ||
                    namespaceUris == null ||
                    serverUris == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }
                decoder.SetMappingTables(namespaceUris, serverUris);
                return decoder.ReadEncodeable<NotificationMessage>(null);
            }
            catch (Exception ex) when (ex is ServiceResultException or ArgumentException or InvalidOperationException)
            {
                using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
                ArrayOf<string?> legacyNamespaceUris = decoder.ReadStringArray(null);
                ArrayOf<string?> legacyServerUris = decoder.ReadStringArray(null);
                decoder.SetMappingTables(
                    CreateNamespaceTable(legacyNamespaceUris),
                    CreateStringTable(legacyServerUris));
                return decoder.ReadEncodeable<NotificationMessage>(null);
            }
        }

        private ContinuationPointEnvelope? DecodeContinuationPointEnvelope(ByteString payload)
        {
            using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
            int version = decoder.ReadInt32(null);
            if (version != ContinuationPointFormatVersion)
            {
                return null;
            }

            ByteString idBytes = decoder.ReadByteString(null);
            if (idBytes.Length != 16)
            {
                return null;
            }

            return new ContinuationPointEnvelope
            {
                Id = new Guid(idBytes.ToArray()),
                OwnerSessionId = decoder.ReadNodeId(null),
                Kind = decoder.ReadEnumerated<ContinuationPointKind>(null),
                BrowseNodeId = decoder.ReadNodeId(null),
                View = decoder.ReadBoolean(null) ? decoder.ReadEncodeable<ViewDescription>(null) : null,
                MaxResultsToReturn = decoder.ReadUInt32(null),
                BrowseDirection = decoder.ReadEnumerated<BrowseDirection>(null),
                ReferenceTypeId = decoder.ReadNodeId(null),
                IncludeSubtypes = decoder.ReadBoolean(null),
                NodeClassMask = decoder.ReadUInt32(null),
                ResultMask = decoder.ReadEnumerated<BrowseResultMask>(null),
                Index = decoder.ReadInt32(null)
            };
        }

        private StoredSubscription Decode(ByteString payload)
        {
            using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
            int version = decoder.ReadInt32(null);
            if (version != DefinitionFormatVersion)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Unsupported subscription record version.");
            }

            var namespaceUris = decoder.ReadStringArray(null);
            var serverUris = decoder.ReadStringArray(null);
            decoder.SetMappingTables(CreateNamespaceTable(namespaceUris), CreateStringTable(serverUris));
            return DecodeSubscription(decoder);
        }

        private static void EncodeSubscription(BinaryEncoder encoder, StoredSubscription subscription)
        {
            encoder.WriteUInt32(null, subscription.Id);
            encoder.WriteBoolean(null, subscription.IsDurable);
            encoder.WriteUInt32(null, subscription.LifetimeCounter);
            encoder.WriteUInt32(null, subscription.MaxLifetimeCount);
            encoder.WriteUInt32(null, subscription.MaxKeepaliveCount);
            encoder.WriteUInt32(null, subscription.MaxMessageCount);
            encoder.WriteUInt32(null, subscription.MaxNotificationsPerPublish);
            encoder.WriteDouble(null, subscription.PublishingInterval);
            encoder.WriteByte(null, subscription.Priority);
            encoder.WriteInt32(null, subscription.LastSentMessage);
            encoder.WriteUInt32(null, subscription.SequenceNumber);
            encoder.WriteExtensionObject(
                null,
                subscription.UserIdentityToken != null
                    ? new ExtensionObject(subscription.UserIdentityToken)
                    : ExtensionObject.Null);

            List<StoredMonitoredItem> items = subscription.MonitoredItems
                .Select(CloneMonitoredItem)
                .ToList();
            encoder.WriteInt32(null, items.Count);
            foreach (StoredMonitoredItem item in items)
            {
                EncodeMonitoredItem(encoder, item);
            }
        }

        private static StoredSubscription DecodeSubscription(BinaryDecoder decoder)
        {
            var subscription = new StoredSubscription
            {
                Id = decoder.ReadUInt32(null),
                IsDurable = decoder.ReadBoolean(null),
                LifetimeCounter = decoder.ReadUInt32(null),
                MaxLifetimeCount = decoder.ReadUInt32(null),
                MaxKeepaliveCount = decoder.ReadUInt32(null),
                MaxMessageCount = decoder.ReadUInt32(null),
                MaxNotificationsPerPublish = decoder.ReadUInt32(null),
                PublishingInterval = decoder.ReadDouble(null),
                Priority = decoder.ReadByte(null),
                LastSentMessage = decoder.ReadInt32(null),
                SequenceNumber = decoder.ReadUInt32(null),
                SentMessages = []
            };

            ExtensionObject token = decoder.ReadExtensionObject(null);
            if (!token.IsNull &&
                token.TryGetValue(out IEncodeable? tokenBody) &&
                tokenBody is UserIdentityToken userIdentityToken)
            {
                subscription.UserIdentityToken = userIdentityToken;
            }

            int itemCount = decoder.ReadInt32(null);
            var items = new List<IStoredMonitoredItem>(itemCount);
            for (int ii = 0; ii < itemCount; ii++)
            {
                items.Add(DecodeMonitoredItem(decoder));
            }
            subscription.MonitoredItems = items;
            return subscription;
        }

        private static void EncodeMonitoredItem(BinaryEncoder encoder, StoredMonitoredItem item)
        {
            encoder.WriteBoolean(null, item.IsRestored);
            encoder.WriteBoolean(null, item.AlwaysReportUpdates);
            encoder.WriteUInt32(null, item.AttributeId);
            encoder.WriteUInt32(null, item.ClientHandle);
            encoder.WriteEnumerated(null, item.DiagnosticsMasks);
            encoder.WriteBoolean(null, item.DiscardOldest);
            encoder.WriteQualifiedName(null, item.Encoding);
            encoder.WriteUInt32(null, item.Id);
            encoder.WriteString(null, item.IndexRange);
            encoder.WriteString(null, item.ParsedIndexRange.ToString());
            encoder.WriteBoolean(null, item.IsDurable);
            encoder.WriteStatusCode(null, item.LastError?.StatusCode ?? StatusCodes.Good);
            encoder.WriteDataValue(null, item.LastValue);
            encoder.WriteEnumerated(null, item.MonitoringMode);
            encoder.WriteNodeId(null, item.NodeId);
            EncodeFilter(encoder, item.FilterToUse);
            EncodeFilter(encoder, item.OriginalFilter);
            encoder.WriteUInt32(null, item.QueueSize);
            encoder.WriteDouble(null, item.Range);
            encoder.WriteDouble(null, item.SamplingInterval);
            encoder.WriteInt32(null, item.SourceSamplingInterval);
            encoder.WriteUInt32(null, item.SubscriptionId);
            encoder.WriteEnumerated(null, item.TimestampsToReturn);
            encoder.WriteInt32(null, item.TypeMask);
        }

        private static StoredMonitoredItem DecodeMonitoredItem(BinaryDecoder decoder)
        {
            var item = new StoredMonitoredItem
            {
                IsRestored = decoder.ReadBoolean(null),
                AlwaysReportUpdates = decoder.ReadBoolean(null),
                AttributeId = decoder.ReadUInt32(null),
                ClientHandle = decoder.ReadUInt32(null),
                DiagnosticsMasks = decoder.ReadEnumerated<DiagnosticsMasks>(null),
                DiscardOldest = decoder.ReadBoolean(null),
                Encoding = decoder.ReadQualifiedName(null),
                Id = decoder.ReadUInt32(null),
                IndexRange = decoder.ReadString(null) ?? string.Empty
            };

            string? rangeText = decoder.ReadString(null);
            item.ParsedIndexRange = string.IsNullOrEmpty(rangeText) ? NumericRange.Null : NumericRange.Parse(rangeText);
            item.IsDurable = decoder.ReadBoolean(null);
            StatusCode lastError = decoder.ReadStatusCode(null);
            item.LastError = lastError == StatusCodes.Good ? null! : new ServiceResult(lastError);
            item.LastValue = decoder.ReadDataValue(null);
            item.MonitoringMode = decoder.ReadEnumerated<MonitoringMode>(null);
            item.NodeId = decoder.ReadNodeId(null);

            item.FilterToUse = DecodeFilter(decoder);
            item.OriginalFilter = DecodeFilter(decoder);

            item.QueueSize = decoder.ReadUInt32(null);
            item.Range = decoder.ReadDouble(null);
            item.SamplingInterval = decoder.ReadDouble(null);
            item.SourceSamplingInterval = decoder.ReadInt32(null);
            item.SubscriptionId = decoder.ReadUInt32(null);
            item.TimestampsToReturn = decoder.ReadEnumerated<TimestampsToReturn>(null);
            item.TypeMask = decoder.ReadInt32(null);
            return item;
        }

        private static void EncodeFilter(BinaryEncoder encoder, MonitoringFilter? filter)
        {
            switch (filter)
            {
                case null:
                    encoder.WriteInt32(null, FilterKindNone);
                    break;
                case DataChangeFilter dataChangeFilter:
                    encoder.WriteInt32(null, FilterKindDataChange);
                    encoder.WriteEncodeable(null, dataChangeFilter);
                    break;
                case EventFilter eventFilter:
                    encoder.WriteInt32(null, FilterKindEvent);
                    encoder.WriteEncodeable(null, eventFilter);
                    break;
                case AggregateFilter aggregateFilter:
                    encoder.WriteInt32(null, FilterKindAggregate);
                    encoder.WriteEncodeable(null, aggregateFilter);
                    break;
                default:
                    encoder.WriteInt32(null, FilterKindExtensionObject);
                    encoder.WriteExtensionObject(null, new ExtensionObject(filter));
                    break;
            }
        }

        private static MonitoringFilter DecodeFilter(BinaryDecoder decoder)
        {
            int kind = decoder.ReadInt32(null);
            return kind switch
            {
                FilterKindNone => null!,
                FilterKindDataChange => decoder.ReadEncodeable<DataChangeFilter>(null),
                FilterKindEvent => decoder.ReadEncodeable<EventFilter>(null),
                FilterKindAggregate => decoder.ReadEncodeable<AggregateFilter>(null),
                FilterKindExtensionObject => DecodeExtensionObjectFilter(decoder),
                _ => throw new ServiceResultException(StatusCodes.BadDecodingError, "Unsupported monitoring filter kind.")
            };
        }

        private static MonitoringFilter DecodeExtensionObjectFilter(BinaryDecoder decoder)
        {
            ExtensionObject filter = decoder.ReadExtensionObject(null);
            if (!filter.IsNull &&
                filter.TryGetValue(out IEncodeable? filterBody) &&
                filterBody is MonitoringFilter monitoringFilter)
            {
                return monitoringFilter;
            }

            return null!;
        }

        private static NamespaceTable CreateNamespaceTable(ArrayOf<string?> namespaceUris)
        {
            return new NamespaceTable(namespaceUris.Memory.ToArray().Where(s => s != null).Select(s => s!).ToArray());
        }

        private static StringTable CreateStringTable(ArrayOf<string?> serverUris)
        {
            return new StringTable(serverUris.Memory.ToArray().Where(s => s != null).Select(s => s!).ToArray());
        }

        private static string RetransmissionMessagePrefixFor(uint subscriptionId)
        {
            return RetransmissionPrefix +
                subscriptionId.ToString("D", System.Globalization.CultureInfo.InvariantCulture) +
                "/message/";
        }

        private static string ContinuationPointPrefixFor(NodeId ownerSessionId)
        {
            return ContinuationPointPrefix +
                Uri.EscapeDataString(ownerSessionId.ToString()) +
                "/";
        }

        private const int DefinitionFormatVersion = 1;
        private const int ContinuationPointFormatVersion = 1;
        private const int LegacyRetransmissionStateFormatVersion = 1;
        private const int RetransmissionStateFormatVersion = 2;
        private const int NotificationMessageFormatVersion = 2;
        private const int FilterKindNone = 0;
        private const int FilterKindDataChange = 1;
        private const int FilterKindEvent = 2;
        private const int FilterKindAggregate = 3;
        private const int FilterKindExtensionObject = 4;
        private const int ChannelCapacity = 1024;
        private const string Prefix = "subscription/";
        private const string RetransmissionPrefix = "subscription-retransmission/";
        private const string ContinuationPointPrefix = "continuation-point/";
        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly ILogger<SharedKeyValueSubscriptionStore>? m_logger;
        private readonly SharedDefinitionCache m_definitionCache;
        private readonly Channel<MirrorCommand> m_channel;
        private readonly CancellationTokenSource m_drainCts = new();
        private readonly Task m_drainTask;
        private readonly object m_retransmissionLock = new();
        private readonly object m_continuationPointLock = new();
        private readonly Dictionary<uint, PendingRetransmissionState> m_pendingRetransmission = [];
        private readonly Dictionary<string, ContinuationPointEnvelope> m_pendingContinuationPointStores = [];
        private readonly HashSet<string> m_pendingContinuationPointDeletes = [];
        private static readonly ConditionalWeakTable<ISharedKeyValueStore, SharedDefinitionCache> s_definitionCaches =
            new();
        private int m_overflowWarningWritten;

        private sealed class SharedDefinitionCache
        {
            public object Lock { get; } = new();

            public Dictionary<uint, StoredSubscription> Subscriptions { get; } = [];
        }

        private sealed class PendingRetransmissionState
        {
            public uint NextSequenceNumber { get; set; }

            public bool ClearRequested { get; set; }

            public bool StateDirty { get; set; }

            public HashSet<uint> KnownMessages { get; } = [];

            public Dictionary<uint, NotificationMessage> PendingMessages { get; } = [];

            public HashSet<uint> PendingDeletes { get; } = [];
        }

        private readonly record struct RetransmissionBatch(
            uint SubscriptionId,
            uint NextSequenceNumber,
            bool StateDirty,
            NotificationMessage[] Messages,
            uint[] Deletes,
            bool ClearRequested);

        private readonly record struct ContinuationPointBatch(
            ContinuationPointEnvelope[] Stores,
            string[] Deletes);

        private sealed class MirrorCommand
        {
            public static MirrorCommand Signal { get; } = new(null);

            public MirrorCommand(TaskCompletionSource<bool>? completion)
            {
                Completion = completion;
            }

            public TaskCompletionSource<bool>? Completion { get; }
        }
    }
}
