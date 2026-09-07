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
using System.Runtime.ExceptionServices;
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

        /// <summary>
        /// Creates a protected continuation store whose message context is supplied later.
        /// </summary>
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
            m_deleteChannel = Channel.CreateUnbounded<DeleteRequest>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            m_deleteTask = Task.Run(() => DrainDeletesAsync(m_disposeCts.Token));
        }

        /// <summary>
        /// Sets the envelope serialization context, rejecting a different context after initialization.
        /// </summary>
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
            string key = KeyFor(envelope.OwnerSessionId, envelope.Id);
            for (int attempt = 0; attempt < kMaxStoreAttempts; attempt++)
            {
                if (await CompareAndSwapResolvedAsync(
                        key,
                        default,
                        protectedPayload,
                        cancellationToken).ConfigureAwait(false))
                {
                    RememberIncarnation(key, protectedPayload);
                    return;
                }
                (bool found, ByteString current) = await m_store.TryGetAsync(
                    key,
                    cancellationToken).ConfigureAwait(false);
                if (!found)
                {
                    continue;
                }
                if (!IsRecoverableValue(
                        current,
                        envelope.OwnerSessionId,
                        envelope.Id))
                {
                    break;
                }
                if (await CompareAndSwapResolvedAsync(
                        key,
                        current,
                        protectedPayload,
                        cancellationToken).ConfigureAwait(false))
                {
                    RememberIncarnation(key, protectedPayload);
                    return;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadEntryExists,
                "The history continuation identifier already exists.");
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
                ForgetIncarnation(key, value);
                QueueDelete(key, value);
                return false;
            }
            if (TryGetMarkerKind(payload, out MarkerKind markerKind))
            {
                ForgetIncarnation(key, value);
                if (markerKind == MarkerKind.Claim)
                {
                    QueueDelete(key, value);
                }
                return false;
            }
            HistoryContinuationPointEnvelope? envelope = Decode(payload);
            if (envelope == null ||
                envelope.Id != id ||
                envelope.OwnerSessionId != ownerSessionId)
            {
                ForgetIncarnation(key, value);
                QueueDelete(key, value);
                return false;
            }
            RememberIncarnation(key, value);
            ByteString claimMarker = CreateMarker(MarkerKind.Claim);
            bool claimed = await CompareAndSwapClaimResolvedAsync(
                    key,
                    value,
                    claimMarker,
                    cancellationToken)
                .ConfigureAwait(false);
            if (claimed)
            {
                ForgetIncarnation(key, value);
                QueueDelete(key, claimMarker);
            }
            else
            {
                ForgetIncarnation(key, value);
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
            string key = KeyFor(ownerSessionId, id);
            if (TryTakeRememberedIncarnation(key, out ByteString expectedValue))
            {
                QueueDelete(key, expectedValue);
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
                    ForgetIncarnation(pair.Key, pair.Value);
                    QueueDelete(pair.Key, pair.Value);
                    continue;
                }
                if (TryGetMarkerKind(payload, out MarkerKind markerKind))
                {
                    ForgetIncarnation(pair.Key, pair.Value);
                    if (markerKind == MarkerKind.Claim)
                    {
                        QueueDelete(pair.Key, pair.Value);
                    }
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
                    RememberIncarnation(pair.Key, pair.Value);
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
                    ForgetIncarnation(pair.Key, pair.Value);
                    QueueDelete(pair.Key, pair.Value);
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
            Task completedTask = await Task.WhenAny(
                m_deleteTask,
                Task.Delay(s_deleteShutdownTimeout)).ConfigureAwait(false);
            if (completedTask != m_deleteTask)
            {
                m_logger?.HistoryContinuationCleanupShutdownTimedOut();
                m_disposeCts.Cancel();
            }
            try
            {
                await m_deleteTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_disposeCts.IsCancellationRequested)
            {
            }
            finally
            {
                m_disposeCts.Dispose();
            }
        }

        /// <summary>
        /// Builds the shared-store key for a continuation within its owning session's keyspace.
        /// </summary>
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
            encoder.WriteByteString(
                null,
                ByteString.From(Guid.NewGuid().ToByteArray()));
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
                int formatVersion = decoder.ReadInt32(null);
                if (formatVersion == kFormatVersion)
                {
                    ByteString incarnation = decoder.ReadByteString(null);
                    if (incarnation.Length != 16 ||
                        new Guid(incarnation.ToArray()) == Guid.Empty)
                    {
                        return null;
                    }
                }
                else if (formatVersion != kLegacyFormatVersion)
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
            await foreach (DeleteRequest queuedRequest in m_deleteChannel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                bool removed = false;
                for (int attempt = 0;
                    attempt < kMaxDeleteAttempts &&
                    !cancellationToken.IsCancellationRequested;
                    attempt++)
                {
                    if (await TryDeleteExpectedAsync(
                            queuedRequest,
                            cancellationToken).ConfigureAwait(false))
                    {
                        removed = true;
                        break;
                    }
                    if (attempt + 1 < kMaxDeleteAttempts)
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(1),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                if (!removed && !cancellationToken.IsCancellationRequested)
                {
                    m_logger?.HistoryContinuationDeleteRetriesExhausted(
                        queuedRequest.Key);
                }
            }
        }

        private async ValueTask<bool> TryDeleteExpectedAsync(
            DeleteRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                _ = await m_store.CompareAndSwapAsync(
                    request.Key,
                    request.ExpectedValue,
                    default,
                    cancellationToken).ConfigureAwait(false);
                ForgetIncarnation(request.Key, request.ExpectedValue);
                return true;
            }
            catch (Exception compareException) when (
                IsDeferredCleanupException(compareException))
            {
                try
                {
                    (bool found, ByteString current) =
                        await m_store.TryGetAsync(
                            request.Key,
                            cancellationToken).ConfigureAwait(false);
                    if (!found || current != request.ExpectedValue)
                    {
                        ForgetIncarnation(
                            request.Key,
                            request.ExpectedValue);
                        return true;
                    }
                }
                catch (Exception resolutionException) when (
                    IsDeferredCleanupException(resolutionException))
                {
                    return false;
                }
                return false;
            }
        }

        private bool IsRecoverableValue(
            ByteString value,
            NodeId ownerSessionId,
            Guid id)
        {
            if (value.IsEmpty)
            {
                return true;
            }
            if (!m_protector.TryUnprotect(value, out ByteString payload))
            {
                return false;
            }
            if (TryGetMarkerKind(payload, out _))
            {
                return true;
            }
            if (payload.IsEmpty || payload.Length > m_maxPayloadBytes)
            {
                return true;
            }
            HistoryContinuationPointEnvelope? envelope = Decode(payload);
            return envelope == null ||
                envelope.Id != id ||
                envelope.OwnerSessionId != ownerSessionId;
        }

        private void RememberIncarnation(string key, ByteString value)
        {
            lock (m_incarnationLock)
            {
                m_knownIncarnations[key] = value;
            }
        }

        private void ForgetIncarnation(string key, ByteString expectedValue)
        {
            lock (m_incarnationLock)
            {
                if (m_knownIncarnations.TryGetValue(
                        key,
                        out ByteString current) &&
                    current == expectedValue)
                {
                    m_knownIncarnations.Remove(key);
                }
            }
        }

        private bool TryTakeRememberedIncarnation(
            string key,
            out ByteString value)
        {
            lock (m_incarnationLock)
            {
                if (m_knownIncarnations.TryGetValue(key, out value))
                {
                    m_knownIncarnations.Remove(key);
                    return true;
                }
                value = default;
                return false;
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
                    QueueDelete(key, value);
                    throw CreateIndeterminateCasException(
                        compareException,
                        resolutionException);
                }
                QueueDelete(key, value);
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
                bool unchanged;
                try
                {
                    (bool found, ByteString current) =
                        await m_store.TryGetAsync(
                            key,
                            CancellationToken.None).ConfigureAwait(false);
                    if (found && current == claimMarker)
                    {
                        return true;
                    }
                    unchanged = found && current == expected;
                }
                catch (Exception resolutionException)
                {
                    throw CreateIndeterminateCasException(
                        compareException,
                        resolutionException);
                }
                if (unchanged)
                {
                    ExceptionDispatchInfo.Capture(compareException).Throw();
                }
                return false;
            }
        }

        private ByteString CreateMarker(MarkerKind markerKind)
        {
            byte[] marker = new byte[kMarkerLength];
            marker[0] = (byte)'H';
            marker[1] = (byte)'C';
            marker[2] = (byte)'P';
            marker[3] = kMarkerVersion;
            marker[4] = (byte)markerKind;
            Guid.NewGuid().ToByteArray().CopyTo(marker, kMarkerHeaderLength);
            return m_protector.Protect(ByteString.From(marker));
        }

        private static bool TryGetMarkerKind(
            ByteString payload,
            out MarkerKind markerKind)
        {
            markerKind = default;
            if (payload.Length != kMarkerLength ||
                payload[0] != (byte)'H' ||
                payload[1] != (byte)'C' ||
                payload[2] != (byte)'P' ||
                payload[3] != kMarkerVersion)
            {
                return false;
            }
            markerKind = (MarkerKind)payload[4];
            return markerKind is MarkerKind.Claim or MarkerKind.Deleted;
        }

        private void QueueDelete(string key, ByteString expectedValue)
        {
            QueueDelete(new DeleteRequest(key, expectedValue));
        }

        private void QueueDelete(DeleteRequest request)
        {
            if (!m_deleteChannel.Writer.TryWrite(request))
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

        private static bool IsDeferredCleanupException(
            Exception exception)
        {
            return exception is
                ServiceResultException or
                IOException or
                TimeoutException or
                InvalidOperationException or
                NotSupportedException;
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
        private const int kLegacyFormatVersion = 1;
        private const int kFormatVersion = 2;
        private const int kMaxStoreAttempts = 5;
        private const int kMaxDeleteAttempts = 5;
        private const byte kMarkerVersion = 1;
        private const int kMarkerHeaderLength = 5;
        private const int kMarkerLength = kMarkerHeaderLength + 16;

        private static readonly TimeSpan s_deleteShutdownTimeout =
            TimeSpan.FromSeconds(1);

        private readonly ISharedKeyValueStore m_store;
        private readonly Lock m_contextLock = new();
        private readonly Lock m_incarnationLock = new();
        private readonly Dictionary<string, ByteString> m_knownIncarnations = [];
        private IServiceMessageContext? m_messageContext;
        private readonly IRecordProtector m_protector;
        private readonly int m_maxPayloadBytes;
        private readonly int m_maxEnvelopesPerSession;
        private readonly TimeSpan m_retentionTime;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<SharedKeyValueHistoryContinuationStore>? m_logger;
        private readonly Channel<DeleteRequest> m_deleteChannel;
        private readonly CancellationTokenSource m_disposeCts = new();
        private readonly Task m_deleteTask;
        private int m_disposed;

        private readonly struct DeleteRequest
        {
            public DeleteRequest(string key, ByteString expectedValue)
            {
                Key = key;
                ExpectedValue = expectedValue;
            }

            public string Key { get; }

            public ByteString ExpectedValue { get; }
        }

        private enum MarkerKind : byte
        {
            Claim = 1,
            Deleted = 2
        }
    }

    /// <summary>
    /// Defines log messages for shared history continuation cleanup failures.
    /// </summary>
    internal static partial class SharedKeyValueHistoryContinuationStoreLog
    {
        /// <summary>
        /// Logs an exception that terminated the continuation cleanup worker.
        /// </summary>
        [LoggerMessage(
            EventId = RedundancyServerEventIds.SharedHistoryContinuationStore + 0,
            Level = LogLevel.Error,
            Message = "The shared history continuation cleanup worker faulted.")]
        public static partial void HistoryContinuationCleanupWorkerFaulted(
            this ILogger logger,
            Exception exception);

        /// <summary>
        /// Logs skipped cleanup because the continuation deletion queue is closed.
        /// </summary>
        [LoggerMessage(
            EventId = RedundancyServerEventIds.SharedHistoryContinuationStore + 1,
            Level = LogLevel.Warning,
            Message = "The shared history continuation cleanup queue is closed; cleanup was skipped.")]
        public static partial void HistoryContinuationCleanupQueueClosed(
            this ILogger logger);

        /// <summary>
        /// Logs exhaustion of the retry budget for deleting a shared continuation.
        /// </summary>
        [LoggerMessage(
            EventId = RedundancyServerEventIds.SharedHistoryContinuationStore + 2,
            Level = LogLevel.Warning,
            Message = "Deleting shared history continuation {Key} exhausted the retry budget.")]
        public static partial void HistoryContinuationDeleteRetriesExhausted(
            this ILogger logger,
            string key);

        /// <summary>
        /// Logs cancellation of cleanup after the shutdown drain deadline was exceeded.
        /// </summary>
        [LoggerMessage(
            EventId = RedundancyServerEventIds.SharedHistoryContinuationStore + 3,
            Level = LogLevel.Warning,
            Message = "Cancelling shared history continuation cleanup after the shutdown drain budget was exceeded.")]
        public static partial void HistoryContinuationCleanupShutdownTimedOut(
            this ILogger logger);
    }
}
