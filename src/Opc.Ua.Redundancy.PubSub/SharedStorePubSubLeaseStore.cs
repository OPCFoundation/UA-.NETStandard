// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Distributed <see cref="IPubSubLeaseStore"/> backed by an
    /// <see cref="ISharedKeyValueStore"/>.
    /// </summary>
    /// <remarks>
    /// External leases require an enrolled <see cref="FreshnessProtectedKeyValueStore"/>.
    /// Each store instance has a distinct incarnation, independent of the configured owner ID.
    /// Release retains the last allocated fence; it never deletes the record.
    /// </remarks>
    public sealed class SharedStorePubSubLeaseStore : IPubSubLeaseStore, IDisposable
    {
        /// <summary>
        /// Initializes a lease store with a one-minute maximum lease duration.
        /// </summary>
        /// <param name="store">An enrolled freshness-protected store or an explicit in-memory store.</param>
        /// <param name="timeProvider">
        /// Clock used to evaluate lease expiry. Defaults to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public SharedStorePubSubLeaseStore(ISharedKeyValueStore store, TimeProvider? timeProvider = null)
            : this(store, timeProvider, TimeSpan.FromMinutes(1))
        {
        }

        /// <summary>
        /// Initializes a lease store with the maximum duration accepted by all participating replicas.
        /// </summary>
        /// <param name="store">An enrolled freshness-protected store or an explicit in-memory store.</param>
        /// <param name="timeProvider">Clock used to evaluate lease expiry.</param>
        /// <param name="maximumLeaseDuration">The maximum acceptable lease lifetime.</param>
        public SharedStorePubSubLeaseStore(
            ISharedKeyValueStore store,
            TimeProvider? timeProvider,
            TimeSpan maximumLeaseDuration)
        {
            m_store = FencedSharedStoreValue.RequireStore(store);
            m_timeProvider = timeProvider ?? TimeProvider.System;
            if (maximumLeaseDuration <= TimeSpan.Zero ||
                maximumLeaseDuration > TimeSpan.FromMilliseconds(int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(maximumLeaseDuration));
            }
            m_maximumLeaseDuration = maximumLeaseDuration;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubLease?> TryAcquireAsync(
            string leaseKey,
            string ownerId,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            string storeKey = BuildKey(leaseKey);
            if (!IsValidOwner(ownerId))
            {
                throw new ArgumentException("A valid, nonempty lease owner is required.", nameof(ownerId));
            }
            ValidateDuration(duration);

            for (int attempt = 0; attempt < s_maxCompareAndSwapAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (bool found, ByteString currentBytes) = await FencedSharedStoreValue
                    .ReadRetainedAsync(m_store, storeKey, cancellationToken)
                    .ConfigureAwait(false);
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                LeaseRecord current = found ? DecodeLease(currentBytes, now) : default;
                bool liveCurrent = found && current.ExpiresAt > now;

                PubSubLease? previous = null;
                if (liveCurrent)
                {
                    if (current.Incarnation != m_incarnation ||
                        !string.Equals(current.OwnerId, ownerId, StringComparison.Ordinal))
                    {
                        return null;
                    }
                    var held = new PubSubLease(leaseKey, ownerId, current.FencingToken, current.ExpiresAt);
                    if (!HasGrant(held))
                    {
                        return null;
                    }
                    previous = held;
                }

                if (!liveCurrent && current.FencingToken == long.MaxValue)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The PubSub lease fence has been exhausted.");
                }
                long fencingToken = liveCurrent ? current.FencingToken : current.FencingToken + 1;
                if (m_disposed || GetExpiry(now, duration) is not DateTimeOffset expiry)
                {
                    return null;
                }
                if (liveCurrent && expiry < current.ExpiresAt)
                {
                    expiry = current.ExpiresAt;
                }
                var lease = new PubSubLease(leaseKey, ownerId, fencingToken, expiry);
                ByteString expected = found ? currentBytes : default;
                if (await FencedSharedStoreValue
                    .CompareAndSwapRetainedAsync(
                        m_store, storeKey, expected, EncodeLease(ownerId, fencingToken, expiry, m_incarnation),
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return ConfirmGrant(lease, previous, cancellationToken);
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubLease?> TryRenewAsync(
            PubSubLease lease,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDuration(duration);
            string storeKey = BuildKey(lease.LeaseKey);
            if (!HasGrant(lease))
            {
                return null;
            }
            (bool found, ByteString currentBytes) = await FencedSharedStoreValue
                .ReadRetainedAsync(m_store, storeKey, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            if (!found ||
                !MatchesLease(DecodeLease(currentBytes, now), lease) ||
                !HasGrant(lease) ||
                lease.ExpiresAt <= now ||
                GetExpiry(now, duration) is not DateTimeOffset expiry)
            {
                return null;
            }

            if (expiry < lease.ExpiresAt)
            {
                expiry = lease.ExpiresAt;
            }
            ByteString renewedBytes = EncodeLease(lease.OwnerId, lease.FencingToken, expiry, m_incarnation);
            bool swapped = await FencedSharedStoreValue
                .CompareAndSwapRetainedAsync(m_store, storeKey, currentBytes, renewedBytes, cancellationToken)
                .ConfigureAwait(false);
            return swapped ? ConfirmGrant(lease with { ExpiresAt = expiry }, lease, cancellationToken) : null;
        }

        /// <inheritdoc/>
        public async ValueTask ReleaseAsync(
            PubSubLease lease,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            string storeKey = BuildKey(lease.LeaseKey);
            if (!ForgetGrant(lease))
            {
                return;
            }
            (bool found, ByteString currentBytes) = await FencedSharedStoreValue
                .ReadRetainedAsync(m_store, storeKey, cancellationToken)
                .ConfigureAwait(false);
            if (!found || !MatchesLease(DecodeLease(currentBytes, m_timeProvider.GetUtcNow()), lease))
            {
                return;
            }

            ByteString releasedBytes = EncodeLease(string.Empty, lease.FencingToken, DateTimeOffset.MinValue,
                Guid.Empty);
            await FencedSharedStoreValue
                .CompareAndSwapRetainedAsync(m_store, storeKey, currentBytes, releasedBytes, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Invalidates this incarnation's local grants without modifying or disposing the shared store.
        /// </summary>
        public void Dispose()
        {
            lock (m_grantGate)
            {
                m_disposed = true;
                m_grants.Clear();
            }
        }

        private bool HasGrant(PubSubLease lease)
        {
            lock (m_grantGate)
            {
                return m_grants.TryGetValue(lease.LeaseKey, out PubSubLease held) && held == lease;
            }
        }

        private bool ForgetGrant(PubSubLease lease)
        {
            lock (m_grantGate)
            {
                return m_grants.TryGetValue(lease.LeaseKey, out PubSubLease held) && held == lease &&
                    m_grants.Remove(lease.LeaseKey);
            }
        }

        private PubSubLease? ConfirmGrant(PubSubLease updated, PubSubLease? previous, CancellationToken ct)
        {
            lock (m_grantGate)
            {
                ct.ThrowIfCancellationRequested();
                if (m_disposed || updated.ExpiresAt <= m_timeProvider.GetUtcNow())
                {
                    return null;
                }
                bool hasGrant = m_grants.TryGetValue(updated.LeaseKey, out PubSubLease held);
                if ((previous is { } expected && (!hasGrant || held != expected)) ||
                    (hasGrant && held.FencingToken > updated.FencingToken))
                {
                    return null;
                }
                m_grants[updated.LeaseKey] = updated;
                return updated;
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(SharedStorePubSubLeaseStore));
            }
        }

        private void ValidateDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero || duration > m_maximumLeaseDuration)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }
        }

        private static string BuildKey(string leaseKey)
        {
            if (string.IsNullOrWhiteSpace(leaseKey))
            {
                throw new ArgumentException("A nonempty lease key is required.", nameof(leaseKey));
            }
            return PubSubRedundancyStoreKeys.LeasePrefix + leaseKey;
        }

        private static DateTimeOffset? GetExpiry(DateTimeOffset now, TimeSpan duration)
        {
            return now.UtcTicks <= DateTimeOffset.MaxValue.UtcTicks - duration.Ticks ? now + duration : null;
        }

        private static bool IsValidOwner(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > s_maxOwnerLength)
            {
                return false;
            }
            try
            {
                return s_ownerEncoding.GetByteCount(ownerId) <= s_maxOwnerLength;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        private bool MatchesLease(LeaseRecord current, PubSubLease lease)
        {
            return current.Incarnation == m_incarnation &&
                string.Equals(current.OwnerId, lease.OwnerId, StringComparison.Ordinal) &&
                current.FencingToken == lease.FencingToken &&
                current.ExpiresAt == lease.ExpiresAt;
        }

        private static ByteString EncodeLease(
            string ownerId, long fencingToken, DateTimeOffset expiresAt, Guid incarnation)
        {
            byte[] ownerBytes = s_ownerEncoding.GetBytes(ownerId);
            byte[] buffer = new byte[s_legacyRecordSize + ownerBytes.Length + s_incarnationSize];
            Span<byte> span = buffer;
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], s_encodingVersion);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), ownerBytes.Length);
            ownerBytes.CopyTo(buffer, 8);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8 + ownerBytes.Length, 8), fencingToken);
            BinaryPrimitives.WriteInt64LittleEndian(
                span.Slice(16 + ownerBytes.Length, 8),
                expiresAt.ToUniversalTime().UtcTicks);
            incarnation.ToByteArray().CopyTo(buffer, s_legacyRecordSize + ownerBytes.Length);
            return new ByteString(buffer);
        }

        private LeaseRecord DecodeLease(ByteString raw, DateTimeOffset now)
        {
            ReadOnlySpan<byte> span = raw.Span;
            if (span.Length < s_legacyRecordSize)
            {
                throw InvalidLease();
            }

            int version = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
            int ownerLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
            if (ownerLength < 0 || ownerLength > s_maxOwnerLength ||
                version is not (s_legacyEncodingVersion or s_encodingVersion))
            {
                throw InvalidLease();
            }
            int incarnationOffset = s_legacyRecordSize + ownerLength;
            int recordSize = version == s_encodingVersion ? incarnationOffset + s_incarnationSize : incarnationOffset;
            if (span.Length != recordSize)
            {
                throw InvalidLease();
            }

            // Legacy live grants are visible but can never belong to this store incarnation.
            Guid incarnation = version == s_encodingVersion ? new Guid(span[incarnationOffset..].ToArray()) : default;
            long fencingToken = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8 + ownerLength, 8));
            long expiryTicks = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(16 + ownerLength, 8));
            long maximumExpiry = (GetExpiry(now, m_maximumLeaseDuration) ?? DateTimeOffset.MaxValue).UtcTicks;
            if (fencingToken <= 0 ||
                expiryTicks < DateTimeOffset.MinValue.UtcTicks ||
                expiryTicks > maximumExpiry ||
                (ownerLength == 0) != (expiryTicks == DateTimeOffset.MinValue.UtcTicks) ||
                (version == s_encodingVersion && (ownerLength == 0) != (incarnation == Guid.Empty)))
            {
                throw InvalidLease();
            }

            string ownerId;
            try
            {
                ownerId = s_ownerEncoding.GetString(span.Slice(8, ownerLength).ToArray());
            }
            catch (DecoderFallbackException)
            {
                throw InvalidLease();
            }
            if (ownerLength > 0 && string.IsNullOrWhiteSpace(ownerId))
            {
                throw InvalidLease();
            }
            return new LeaseRecord(ownerId, fencingToken, new DateTimeOffset(expiryTicks, TimeSpan.Zero), incarnation);
        }

        private static ServiceResultException InvalidLease()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The shared PubSub lease record is invalid.");
        }

        private readonly record struct LeaseRecord(
            string OwnerId, long FencingToken, DateTimeOffset ExpiresAt, Guid Incarnation);

        private const int s_legacyEncodingVersion = 1;
        private const int s_encodingVersion = 2;
        private const int s_legacyRecordSize = 24;
        private const int s_incarnationSize = 16;
        private const int s_maxCompareAndSwapAttempts = 5;
        private const int s_maxOwnerLength = 1024;
        private static readonly UTF8Encoding s_ownerEncoding = new(false, true);

        private readonly ISharedKeyValueStore m_store;
        private readonly TimeProvider m_timeProvider;
        private readonly TimeSpan m_maximumLeaseDuration;
        private readonly Guid m_incarnation = Guid.NewGuid();
        private readonly Lock m_grantGate = new();
        private readonly Dictionary<string, PubSubLease> m_grants = new(StringComparer.Ordinal);
        private volatile bool m_disposed;
    }
}
