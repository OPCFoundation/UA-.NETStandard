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
    /// Lease updates use compare-and-swap so multiple PubSub instances can elect one active owner per lease key with
    /// monotonic fencing tokens across processes.
    /// </remarks>
    public sealed class SharedStorePubSubLeaseStore : IPubSubLeaseStore
    {
        /// <summary>
        /// Initializes a new <see cref="SharedStorePubSubLeaseStore"/>.
        /// </summary>
        /// <param name="store">Shared key/value backend used for lease records.</param>
        /// <param name="timeProvider">
        /// Clock used to evaluate lease expiry. Defaults to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public SharedStorePubSubLeaseStore(ISharedKeyValueStore store, TimeProvider? timeProvider = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubLease?> TryAcquireAsync(
            string leaseKey,
            string ownerId,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(leaseKey))
            {
                throw new ArgumentException("leaseKey is required.", nameof(leaseKey));
            }
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("ownerId is required.", nameof(ownerId));
            }

            string storeKey = PubSubRedundancyStoreKeys.LeasePrefix + leaseKey;
            for (int attempt = 0; attempt < s_maxCompareAndSwapAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (bool found, ByteString currentBytes) = await m_store
                    .TryGetAsync(storeKey, cancellationToken)
                    .ConfigureAwait(false);
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                LeaseRecord current = found && TryDecodeLease(currentBytes, out LeaseRecord decoded)
                    ? decoded
                    : default;
                bool liveCurrent = found && current.ExpiresAt > now;

                if (liveCurrent && !string.Equals(current.OwnerId, ownerId, StringComparison.Ordinal))
                {
                    return null;
                }

                long fencingToken = liveCurrent && string.Equals(current.OwnerId, ownerId, StringComparison.Ordinal)
                    ? current.FencingToken
                    : current.FencingToken + 1;
                var lease = new PubSubLease(leaseKey, ownerId, fencingToken, now + duration);
                ByteString expected = found ? currentBytes : default;
                if (await m_store
                    .CompareAndSwapAsync(storeKey, expected, EncodeLease(ownerId, fencingToken, lease.ExpiresAt),
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return lease;
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
            cancellationToken.ThrowIfCancellationRequested();
            string storeKey = PubSubRedundancyStoreKeys.LeasePrefix + lease.LeaseKey;
            (bool found, ByteString currentBytes) = await m_store
                .TryGetAsync(storeKey, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            if (!found ||
                !TryDecodeLease(currentBytes, out LeaseRecord current) ||
                current.FencingToken != lease.FencingToken ||
                !string.Equals(current.OwnerId, lease.OwnerId, StringComparison.Ordinal) ||
                current.ExpiresAt <= now)
            {
                return null;
            }

            var renewed = new PubSubLease(lease.LeaseKey, lease.OwnerId, lease.FencingToken, now + duration);
            ByteString renewedBytes = EncodeLease(renewed.OwnerId, renewed.FencingToken, renewed.ExpiresAt);
            bool swapped = await m_store
                .CompareAndSwapAsync(storeKey, currentBytes, renewedBytes, cancellationToken)
                .ConfigureAwait(false);
            return swapped
                ? renewed
                : null;
        }

        /// <inheritdoc/>
        public async ValueTask ReleaseAsync(
            PubSubLease lease,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string storeKey = PubSubRedundancyStoreKeys.LeasePrefix + lease.LeaseKey;
            (bool found, ByteString currentBytes) = await m_store
                .TryGetAsync(storeKey, cancellationToken)
                .ConfigureAwait(false);
            if (!found ||
                !TryDecodeLease(currentBytes, out LeaseRecord current) ||
                current.FencingToken != lease.FencingToken ||
                !string.Equals(current.OwnerId, lease.OwnerId, StringComparison.Ordinal))
            {
                return;
            }

            ByteString releasedBytes = EncodeLease(string.Empty, lease.FencingToken, DateTimeOffset.MinValue);
            await m_store
                .CompareAndSwapAsync(storeKey, currentBytes, releasedBytes, cancellationToken)
                .ConfigureAwait(false);
        }

        private static ByteString EncodeLease(string ownerId, long fencingToken, DateTimeOffset expiresAt)
        {
            byte[] ownerBytes = System.Text.Encoding.UTF8.GetBytes(ownerId);
            byte[] buffer = new byte[4 + 4 + ownerBytes.Length + 8 + 8];
            Span<byte> span = buffer;
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], s_encodingVersion);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), ownerBytes.Length);
            ownerBytes.CopyTo(buffer, 8);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8 + ownerBytes.Length, 8), fencingToken);
            BinaryPrimitives.WriteInt64LittleEndian(
                span.Slice(16 + ownerBytes.Length, 8),
                expiresAt.ToUniversalTime().UtcTicks);
            return new ByteString(buffer);
        }

        private static bool TryDecodeLease(ByteString raw, out LeaseRecord lease)
        {
            lease = default;
            if (raw.IsNull)
            {
                return false;
            }

            ReadOnlySpan<byte> span = raw.Span;
            if (span.Length < 24 ||
                BinaryPrimitives.ReadInt32LittleEndian(span[..4]) != s_encodingVersion)
            {
                return false;
            }

            int ownerLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
            if (ownerLength < 0 || span.Length != 24 + ownerLength)
            {
                return false;
            }

            string ownerId = System.Text.Encoding.UTF8.GetString(span.Slice(8, ownerLength).ToArray());
            long fencingToken = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8 + ownerLength, 8));
            long expiryTicks = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(16 + ownerLength, 8));
            if (expiryTicks < DateTimeOffset.MinValue.UtcTicks || expiryTicks > DateTimeOffset.MaxValue.UtcTicks)
            {
                return false;
            }

            lease = new LeaseRecord(ownerId, fencingToken, new DateTimeOffset(expiryTicks, TimeSpan.Zero));
            return true;
        }

        private readonly record struct LeaseRecord(string OwnerId, long FencingToken, DateTimeOffset ExpiresAt);

        private const int s_encodingVersion = 1;
        private const int s_maxCompareAndSwapAttempts = 5;

        private readonly ISharedKeyValueStore m_store;
        private readonly TimeProvider m_timeProvider;
    }
}
