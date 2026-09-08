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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Protected strong-store lease with a monotonically increasing historian
    /// fencing epoch.
    /// </summary>
    public sealed class SharedKeyValueHistorianFencingAuthority :
        IHistorianFencingAuthority
    {
        /// <summary>
        /// Creates a historian fencing authority.
        /// </summary>
        public SharedKeyValueHistorianFencingAuthority(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            ILeaderElection election,
            TimeSpan leaseDuration,
            TimeProvider? timeProvider = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (store is not ISharedKeyValueStoreConsistency consistency ||
                !consistency.IsLinearizable(kFenceKey) ||
                consistency.IsProcessLocal(kFenceKey))
            {
                throw new InvalidOperationException(
                    "Historian fencing requires a cross-process linearizable shared store.");
            }
            m_protector = protector ??
                throw new ArgumentNullException(nameof(protector));
            if (protector is NullRecordProtector)
            {
                throw new InvalidOperationException(
                    "Historian fencing requires authenticated record protection.");
            }
            m_election = election ??
                throw new ArgumentNullException(nameof(election));
            if (leaseDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseDuration));
            }
            m_leaseDuration = leaseDuration;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async ValueTask<HistorianWriterFence?> TryAcquireOrRenewAsync(
            CancellationToken ct = default)
        {
            if (!m_election.IsLeader)
            {
                return null;
            }
            for (int attempt = 0; attempt < kMaxAttempts; attempt++)
            {
                (bool found, ByteString stored) = await m_store.TryGetAsync(
                    kFenceKey,
                    ct).ConfigureAwait(false);
                FenceLease current = found
                    ? Decode(stored)
                    : default;
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (found &&
                    current.ExpiresAt > now &&
                    current.WriterId != m_writerId)
                {
                    return null;
                }

                long epoch = found &&
                    current.WriterId == m_writerId &&
                    current.ExpiresAt > now
                    ? current.Epoch
                    : checked(current.Epoch + 1);
                var next = new FenceLease(
                    m_writerId,
                    epoch,
                    now.Add(m_leaseDuration));
                ByteString nextRecord = Encode(next);
                bool exchanged = await CompareAndSwapResolvedAsync(
                    found ? stored : default,
                    nextRecord,
                    ct).ConfigureAwait(false);
                if (exchanged)
                {
                    return new HistorianWriterFence(m_writerId, epoch);
                }
            }
            return null;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsCurrentAsync(
            HistorianWriterFence fence,
            CancellationToken ct = default)
        {
            if (!m_election.IsLeader ||
                fence.WriterId == Guid.Empty ||
                fence.Epoch <= 0)
            {
                return false;
            }
            (bool found, ByteString stored) = await m_store.TryGetAsync(
                kFenceKey,
                ct).ConfigureAwait(false);
            if (!found)
            {
                return false;
            }
            FenceLease current = Decode(stored);
            return current.WriterId == fence.WriterId &&
                current.Epoch == fence.Epoch &&
                current.ExpiresAt > m_timeProvider.GetUtcNow();
        }

        private async ValueTask<bool> CompareAndSwapResolvedAsync(
            ByteString expected,
            ByteString value,
            CancellationToken ct)
        {
            try
            {
                return await m_store.CompareAndSwapAsync(
                    kFenceKey,
                    expected,
                    value,
                    ct).ConfigureAwait(false);
            }
            catch
            {
                (bool found, ByteString current) = await m_store.TryGetAsync(
                    kFenceKey,
                    CancellationToken.None).ConfigureAwait(false);
                if (found && current == value)
                {
                    return true;
                }
                throw;
            }
        }

        private ByteString Encode(FenceLease lease)
        {
            byte[] plaintext = new byte[
                sizeof(int) +
                16 +
                sizeof(long) +
                sizeof(long)];
            Span<byte> span = plaintext;
            BinaryPrimitives.WriteInt32LittleEndian(span, kFormatVersion);
            lease.WriterId.ToByteArray().AsSpan().CopyTo(
                span[sizeof(int)..]);
            BinaryPrimitives.WriteInt64LittleEndian(
                span[(sizeof(int) + 16)..],
                lease.Epoch);
            BinaryPrimitives.WriteInt64LittleEndian(
                span[(sizeof(int) + 16 + sizeof(long))..],
                lease.ExpiresAt.UtcTicks);
            ByteString record = m_protector.Protect(
                ByteString.From(plaintext));
            if (record.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed);
            }
            return record;
        }

        private FenceLease Decode(ByteString record)
        {
            if (record.IsEmpty ||
                !m_protector.TryUnprotect(record, out ByteString plaintext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Historian fence authentication failed.");
            }
            byte[] bytes = plaintext.ToArray();
            const int expectedLength =
                sizeof(int) + 16 + sizeof(long) + sizeof(long);
            if (bytes.Length != expectedLength ||
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan()) !=
                    kFormatVersion)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "The historian fence record is invalid.");
            }
            var writerId = new Guid(
                bytes.AsSpan(sizeof(int), 16).ToArray());
            long epoch = BinaryPrimitives.ReadInt64LittleEndian(
                bytes.AsSpan(sizeof(int) + 16, sizeof(long)));
            long expiryTicks = BinaryPrimitives.ReadInt64LittleEndian(
                bytes.AsSpan(
                    sizeof(int) + 16 + sizeof(long),
                    sizeof(long)));
            if (writerId == Guid.Empty ||
                epoch <= 0 ||
                expiryTicks <= DateTimeOffset.MinValue.UtcTicks ||
                expiryTicks > DateTimeOffset.MaxValue.UtcTicks)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "The historian fence record values are invalid.");
            }
            return new FenceLease(
                writerId,
                epoch,
                new DateTimeOffset(expiryTicks, TimeSpan.Zero));
        }

        private const string kFenceKey = "historian/v1/fence";
        private const int kFormatVersion = 1;
        private const int kMaxAttempts = 8;
        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly ILeaderElection m_election;
        private readonly TimeSpan m_leaseDuration;
        private readonly TimeProvider m_timeProvider;
        private readonly Guid m_writerId = Guid.NewGuid();

        private readonly record struct FenceLease(
            Guid WriterId,
            long Epoch,
            DateTimeOffset ExpiresAt);
    }
}
