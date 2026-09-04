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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Protected shared-store implementation for portable HistoryRead
    /// continuation points.
    /// </summary>
    public sealed class SharedKeyValueHistoryContinuationStore :
        IHistoryContinuationPointStore,
        IStrongKeyspaceProvider,
        IAsyncDisposable
    {
        /// <summary>
        /// Creates a shared history continuation store.
        /// </summary>
        public SharedKeyValueHistoryContinuationStore(
            ISharedKeyValueStore store,
            IServiceMessageContext messageContext,
            IRecordProtector protector,
            int maxPayloadBytes = 4 * 1024 * 1024,
            int maxEnvelopesPerSession = 10_000,
            TimeSpan retentionTime = default,
            TimeProvider? timeProvider = null,
            ILogger<SharedKeyValueHistoryContinuationStore>? logger = null)
            : this(
                store,
                protector,
                maxPayloadBytes,
                maxEnvelopesPerSession,
                retentionTime,
                timeProvider,
                logger)
        {
            Initialize(messageContext);
        }

        internal SharedKeyValueHistoryContinuationStore(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            int maxPayloadBytes = 4 * 1024 * 1024,
            int maxEnvelopesPerSession = 10_000,
            TimeSpan retentionTime = default,
            TimeProvider? timeProvider = null,
            ILogger<SharedKeyValueHistoryContinuationStore>? logger = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (store is not ISharedKeyValueStoreConsistency consistency ||
                !consistency.IsLinearizable(kPrefix) ||
                consistency.IsProcessLocal(kPrefix))
            {
                throw new InvalidOperationException(
                    "Portable history continuations require a cross-process linearizable shared store.");
            }
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            if (protector is NullRecordProtector)
            {
                throw new InvalidOperationException(
                    "Portable history continuations require authenticated record protection.");
            }
            if (maxPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
            }
            if (maxEnvelopesPerSession <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxEnvelopesPerSession));
            }
            m_maxPayloadBytes = maxPayloadBytes;
            m_maxEnvelopesPerSession = maxEnvelopesPerSession;
            m_retentionTime = retentionTime == TimeSpan.Zero
                ? TimeSpan.FromDays(1)
                : retentionTime;
            if (m_retentionTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(retentionTime));
            }
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = logger;
            m_deleteChannel = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            m_deleteTask = Task.Run(() => DrainDeletesAsync(m_disposeCts.Token));
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
                        "The shared history continuation store is already initialized with another message context.");
                }
                m_messageContext = messageContext;
            }
        }

        /// <inheritdoc/>
        public async ValueTask StoreAsync(
            HistoryContinuationPointEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }
            if (envelope.Id == Guid.Empty ||
                envelope.OwnerSessionId.IsNull ||
                string.IsNullOrWhiteSpace(envelope.CodecId) ||
                envelope.Payload.IsEmpty)
            {
                throw new ArgumentException(
                    "The history continuation envelope is incomplete.",
                    nameof(envelope));
            }
            if (envelope.Payload.Length > m_maxPayloadBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "History continuation payload exceeds the configured limit.");
            }
            ByteString payload = Encode(envelope);
            if (payload.Length > m_maxPayloadBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "History continuation payload exceeds the configured limit.");
            }
            ByteString protectedPayload = m_protector.Protect(payload);
            if (protectedPayload.IsEmpty ||
                protectedPayload.Length > m_maxPayloadBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Protected history continuation payload is invalid or too large.");
            }
            bool stored = await CompareAndSwapResolvedAsync(
                KeyFor(envelope.OwnerSessionId, envelope.Id),
                default,
                protectedPayload,
                cancellationToken).ConfigureAwait(false);
            if (!stored)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEntryExists,
                    "The history continuation identifier already exists.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> TryTakeAsync(
            NodeId ownerSessionId,
            Guid id,
            CancellationToken cancellationToken = default)
        {
            if (ownerSessionId.IsNull || id == Guid.Empty)
            {
                return false;
            }
            string key = KeyFor(ownerSessionId, id);
            (bool found, ByteString value) = await m_store
                .TryGetAsync(key, cancellationToken)
                .ConfigureAwait(false);
            if (!found || value.IsEmpty)
            {
                return false;
            }
            if (!m_protector.TryUnprotect(value, out ByteString payload) ||
                payload.IsEmpty ||
                payload.Length > m_maxPayloadBytes)
            {
                QueueDelete(key);
                return false;
            }
            HistoryContinuationPointEnvelope? envelope = Decode(payload);
            if (envelope == null ||
                envelope.Id != id ||
                envelope.OwnerSessionId != ownerSessionId)
            {
                QueueDelete(key);
                return false;
            }
            ByteString claimMarker = m_protector.Protect(
                ByteString.From(Guid.NewGuid().ToByteArray()));
            bool claimed = await CompareAndSwapClaimResolvedAsync(
                    key,
                    value,
                    claimMarker,
                    cancellationToken)
                .ConfigureAwait(false);
            if (claimed)
            {
                try
                {
                    if (!await m_store.DeleteAsync(
                            key,
                            cancellationToken).ConfigureAwait(false))
                    {
                        QueueDelete(key);
                    }
                }
                catch (Exception)
                {
                    QueueDelete(key);
                }
            }
            return claimed;
        }

        /// <inheritdoc/>
        public void ScheduleRemove(NodeId ownerSessionId, Guid id)
        {
            if (ownerSessionId.IsNull)
            {
                throw new ArgumentNullException(nameof(ownerSessionId));
            }
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "The continuation identifier must not be empty.",
                    nameof(id));
            }
            if (m_deleteTask.IsFaulted)
            {
                m_logger?.HistoryContinuationCleanupWorkerFaulted(
                    m_deleteTask.Exception?.InnerException ??
                    m_deleteTask.Exception!);
                return;
            }
            if (!m_deleteChannel.Writer.TryWrite(
                    KeyFor(ownerSessionId, id)))
            {
                m_logger?.HistoryContinuationCleanupQueueClosed();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<HistoryContinuationPointEnvelope>> LoadAsync(
            NodeId ownerSessionId,
            CancellationToken cancellationToken = default)
        {
            if (ownerSessionId.IsNull)
            {
                return [];
            }

            var result = new List<HistoryContinuationPointEnvelope>();
            await foreach (KeyValuePair<string, ByteString> pair in m_store
                .ScanAsync(PrefixFor(ownerSessionId), cancellationToken)
                .ConfigureAwait(false))
            {
                if (pair.Value.IsEmpty ||
                    !m_protector.TryUnprotect(pair.Value, out ByteString payload) ||
                    payload.Length > m_maxPayloadBytes)
                {
                    QueueDelete(pair.Key);
                    continue;
                }
                HistoryContinuationPointEnvelope? envelope = Decode(payload);
                if (envelope != null &&
                    envelope.OwnerSessionId == ownerSessionId &&
                    string.Equals(
                        pair.Key,
                        KeyFor(ownerSessionId, envelope.Id),
                        StringComparison.Ordinal))
                {
                    result.Add(envelope);
                    if (result.Count > m_maxEnvelopesPerSession)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations,
                            "The session has too many persisted history continuations.");
                    }
                }
                else
                {
                    QueueDelete(pair.Key);
                }
            }
            return [.. result];
        }

        /// <inheritdoc/>
        public ArrayOf<string> GetStrongKeyPrefixes()
        {
            return [kPrefix];
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            m_deleteChannel.Writer.TryComplete();
            m_disposeCts.Cancel();
            try
            {
                await m_deleteTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                m_disposeCts.Dispose();
            }
        }

        internal static string KeyFor(NodeId ownerSessionId, Guid id)
        {
            return PrefixFor(ownerSessionId) +
                id.ToString("N", CultureInfo.InvariantCulture);
        }

        private static string PrefixFor(NodeId ownerSessionId)
        {
            return kPrefix + Uri.EscapeDataString(ownerSessionId.ToString()) + "/";
        }

        private ByteString Encode(HistoryContinuationPointEnvelope envelope)
        {
            using var encoder = new BinaryEncoder(GetMessageContext());
            encoder.WriteInt32(null, kFormatVersion);
            encoder.WriteDateTime(
                null,
                m_timeProvider.GetUtcNow().Add(m_retentionTime).UtcDateTime);
            encoder.WriteByteString(null, ByteString.From(envelope.Id.ToByteArray()));
            encoder.WriteNodeId(null, envelope.OwnerSessionId);
            encoder.WriteString(null, envelope.CodecId);
            encoder.WriteUInt32(null, envelope.CodecVersion);
            encoder.WriteByteString(null, envelope.Payload);
            byte[]? payload = encoder.CloseAndReturnBuffer();
            return payload == null ? ByteString.Empty : ByteString.From(payload);
        }

        private HistoryContinuationPointEnvelope? Decode(ByteString payload)
        {
            try
            {
                using var decoder = new BinaryDecoder(
                    payload.ToArray(),
                    GetMessageContext());
                if (decoder.ReadInt32(null) != kFormatVersion)
                {
                    return null;
                }
                DateTimeUtc expiresAt = decoder.ReadDateTime(null);
                if (expiresAt <= m_timeProvider.GetUtcNow().UtcDateTime)
                {
                    return null;
                }
                ByteString id = decoder.ReadByteString(null);
                if (id.Length != 16)
                {
                    return null;
                }
                var envelope = new HistoryContinuationPointEnvelope
                {
                    Id = new Guid(id.ToArray()),
                    OwnerSessionId = decoder.ReadNodeId(null),
                    CodecId = decoder.ReadString(null) ?? string.Empty,
                    CodecVersion = decoder.ReadUInt32(null),
                    Payload = decoder.ReadByteString(null)
                };
                if (envelope.Id == Guid.Empty ||
                    envelope.OwnerSessionId.IsNull ||
                    string.IsNullOrWhiteSpace(envelope.CodecId) ||
                    envelope.Payload.IsEmpty ||
                    envelope.Payload.Length > m_maxPayloadBytes)
                {
                    return null;
                }
                if (decoder.Position != payload.Length)
                {
                    return null;
                }
                return envelope;
            }
            catch (Exception exception) when (exception is
                ServiceResultException or
                ArgumentException or
                InvalidOperationException or
                EndOfStreamException or
                IOException or
                OverflowException or
                IndexOutOfRangeException)
            {
                return null;
            }
        }

        private async Task DrainDeletesAsync(CancellationToken cancellationToken)
        {
            await foreach (string key in m_deleteChannel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                bool removed = false;
                for (int attempt = 0;
                    attempt < kMaxDeleteAttempts &&
                    !cancellationToken.IsCancellationRequested;
                    attempt++)
                {
                    try
                    {
                        if (await m_store.DeleteAsync(
                                key,
                                cancellationToken).ConfigureAwait(false))
                        {
                            removed = true;
                            break;
                        }
                        (bool found, _) = await m_store.TryGetAsync(
                            key,
                            cancellationToken).ConfigureAwait(false);
                        if (!found)
                        {
                            removed = true;
                            break;
                        }
                    }
                    catch (Exception exception) when (
                        IsRetryableDeleteException(exception))
                    {
                    }
                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        cancellationToken).ConfigureAwait(false);
                }
                if (!removed && !cancellationToken.IsCancellationRequested)
                {
                    m_logger?.HistoryContinuationDeleteRetriesExhausted(key);
                }
            }
        }

        private async ValueTask<bool> CompareAndSwapResolvedAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken cancellationToken)
        {
            try
            {
                return await m_store.CompareAndSwapAsync(
                    key,
                    expected,
                    value,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception compareException)
            {
                try
                {
                    (bool found, ByteString current) =
                        await m_store.TryGetAsync(
                            key,
                            CancellationToken.None).ConfigureAwait(false);
                    if (found && current == value)
                    {
                        return true;
                    }
                }
                catch (Exception resolutionException)
                {
                    throw CreateIndeterminateCasException(
                        compareException,
                        resolutionException);
                }
                throw;
            }
        }

        private async ValueTask<bool> CompareAndSwapClaimResolvedAsync(
            string key,
            ByteString expected,
            ByteString claimMarker,
            CancellationToken cancellationToken)
        {
            try
            {
                return await m_store.CompareAndSwapAsync(
                    key,
                    expected,
                    claimMarker,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception compareException)
            {
                try
                {
                    (bool found, ByteString current) =
                        await m_store.TryGetAsync(
                            key,
                            CancellationToken.None).ConfigureAwait(false);
                    return found && current == claimMarker;
                }
                catch (Exception resolutionException)
                {
                    throw CreateIndeterminateCasException(
                        compareException,
                        resolutionException);
                }
            }
        }

        private void QueueDelete(string key)
        {
            if (!m_deleteChannel.Writer.TryWrite(key))
            {
                m_logger?.HistoryContinuationCleanupQueueClosed();
            }
        }

        private IServiceMessageContext GetMessageContext()
        {
            lock (m_contextLock)
            {
                return m_messageContext ??
                    throw new InvalidOperationException(
                        "The shared history continuation store has not been initialized by the server host.");
            }
        }

        private static bool IsRetryableDeleteException(
            Exception exception)
        {
            return exception is
                ServiceResultException or
                IOException or
                TimeoutException or
                InvalidOperationException;
        }

        private static ServiceResultException CreateIndeterminateCasException(
            Exception compareException,
            Exception resolutionException)
        {
            return new ServiceResultException(
                StatusCodes.BadUnexpectedError,
                "The shared history continuation compare-and-swap outcome is indeterminate.",
                new AggregateException(
                    compareException,
                    resolutionException));
        }

        private const string kPrefix = "history-continuation/v1/";
        private const int kFormatVersion = 1;
        private const int kMaxDeleteAttempts = 5;
        private readonly ISharedKeyValueStore m_store;
        private readonly Lock m_contextLock = new();
        private IServiceMessageContext? m_messageContext;
        private readonly IRecordProtector m_protector;
        private readonly int m_maxPayloadBytes;
        private readonly int m_maxEnvelopesPerSession;
        private readonly TimeSpan m_retentionTime;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<SharedKeyValueHistoryContinuationStore>? m_logger;
        private readonly Channel<string> m_deleteChannel;
        private readonly CancellationTokenSource m_disposeCts = new();
        private readonly Task m_deleteTask;
        private int m_disposed;
    }

    internal static partial class SharedKeyValueHistoryContinuationStoreLog
    {
        [LoggerMessage(
            EventId = RedundancyServerEventIds.SharedHistoryContinuationStore + 0,
            Level = LogLevel.Error,
            Message = "The shared history continuation cleanup worker faulted.")]
        public static partial void HistoryContinuationCleanupWorkerFaulted(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = RedundancyServerEventIds.SharedHistoryContinuationStore + 1,
            Level = LogLevel.Warning,
            Message = "The shared history continuation cleanup queue is closed; cleanup was skipped.")]
        public static partial void HistoryContinuationCleanupQueueClosed(
            this ILogger logger);

        [LoggerMessage(
            EventId = RedundancyServerEventIds.SharedHistoryContinuationStore + 2,
            Level = LogLevel.Warning,
            Message = "Deleting shared history continuation {Key} exhausted the retry budget.")]
        public static partial void HistoryContinuationDeleteRetriesExhausted(
            this ILogger logger,
            string key);
    }
}
