// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.Redundancy;
using TextEncoding = System.Text.Encoding;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Shares fenced UADP nonce allocations across publisher instances.
    /// </summary>
    /// <remarks>
    /// An enrolled <see cref="FreshnessProtectedKeyValueStore"/> is required for reservations.
    /// A process-local store is accepted only to configure an unsecured, local application.
    /// Rotate keys before publishing after enrollment of a new epoch.
    /// </remarks>
    public sealed class SharedStorePubSubNonceRangeStore : IPubSubNonceRangeStore
    {
        /// <summary>
        /// Initializes a reservation store over a linearizable shared backend.
        /// </summary>
        /// <param name="store">The enrolled freshness view, or a store for unsecured local development.</param>
        public SharedStorePubSubNonceRangeStore(ISharedKeyValueStore store)
        {
            m_store = FencedSharedStoreValue.RequireStore(store);
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubNonceRange?> ReserveAsync(
            PublisherId publisherId,
            ushort writerGroupId,
            uint tokenId,
            string componentId,
            long fencingToken,
            uint count,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("A component identity is required.", nameof(componentId));
            }
            if (fencingToken <= 0 || count == 0 || tokenId == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "A positive fence, reservation size and token are required.");
            }

            string encodedPublisherId = Convert.ToBase64String(TextEncoding.UTF8.GetBytes(publisherId.ToString()));
            string scope = FormattableString.Invariant(
                $"{kPrefix}scope/{(int)publisherId.Type}/{encodedPublisherId}/{writerGroupId}");
            // Share the sequence budget across scopes that may encrypt with the same token.
            string generation = FormattableString.Invariant($"{kPrefix}token/{tokenId}");
            RequireLinearizability(scope);
            RequireLinearizability(generation);

            byte[] owner = TextEncoding.UTF8.GetBytes(componentId);
            ByteString fence = EncodeFence(owner, fencingToken);
            await ClaimScopeAsync(scope, owner, fence, fencingToken, cancellationToken).ConfigureAwait(false);

            for (int attempt = 0; attempt < kMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (bool found, ByteString current) = await FencedSharedStoreValue
                    .ReadRetainedAsync(m_store, generation, cancellationToken)
                    .ConfigureAwait(false);
                uint last = found ? DecodeSequence(current) : 0;
                if (last == uint.MaxValue)
                {
                    return null;
                }

                uint end = last + Math.Min(count, uint.MaxValue - last);
                byte[] bytes = new byte[sizeof(uint)];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, end);
                if (await FencedSharedStoreValue
                    .CompareAndSwapRetainedAsync(m_store, generation, current, ByteString.From(bytes), cancellationToken)
                    .ConfigureAwait(false))
                {
                    (bool stillHeld, ByteString currentFence) = await FencedSharedStoreValue
                        .ReadRetainedAsync(m_store, scope, cancellationToken)
                        .ConfigureAwait(false);
                    if (!stillHeld || currentFence != fence)
                    {
                        throw InvalidScope();
                    }
                    return new PubSubNonceRange(last + 1, end);
                }
            }
            throw Contended();
        }

        private async ValueTask ClaimScopeAsync(
            string scope,
            byte[] owner,
            ByteString fence,
            long fencingToken,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < kMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (bool found, ByteString current) = await FencedSharedStoreValue
                    .ReadRetainedAsync(m_store, scope, cancellationToken)
                    .ConfigureAwait(false);
                if (found)
                {
                    ReadOnlySpan<byte> bytes = current.Span;
                    if (bytes.Length <= sizeof(long) || !bytes[sizeof(long)..].SequenceEqual(owner))
                    {
                        throw InvalidScope();
                    }
                    long currentFence = BinaryPrimitives.ReadInt64LittleEndian(bytes);
                    if (currentFence <= 0 || currentFence > fencingToken)
                    {
                        throw InvalidScope();
                    }
                    if (currentFence == fencingToken)
                    {
                        return;
                    }
                }
                if (await FencedSharedStoreValue
                    .CompareAndSwapRetainedAsync(m_store, scope, current, fence, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return;
                }
            }
            throw Contended();
        }

        private void RequireLinearizability(string key)
        {
            if (m_store is not FreshnessProtectedKeyValueStore authority ||
                !authority.IsLinearizable(key))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Nonce reservations require an enrolled freshness authority.");
            }
        }

        private static ByteString EncodeFence(ReadOnlySpan<byte> owner, long fencingToken)
        {
            byte[] bytes = new byte[sizeof(long) + owner.Length];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, fencingToken);
            owner.CopyTo(bytes.AsSpan(sizeof(long)));
            return ByteString.From(bytes);
        }

        private static uint DecodeSequence(ByteString current)
        {
            if (current.Length != sizeof(uint) || BinaryPrimitives.ReadUInt32LittleEndian(current.Span) == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The committed PubSub nonce high-water mark is invalid.");
            }
            return BinaryPrimitives.ReadUInt32LittleEndian(current.Span);
        }

        private static ServiceResultException InvalidScope()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The PubSub nonce replay scope has a different owner or fence.");
        }

        private static ServiceResultException Contended()
        {
            return new ServiceResultException(
                StatusCodes.BadResourceUnavailable, "Could not commit a PubSub nonce reservation.");
        }

        private const string kPrefix = "nonce/pubsub/v1/";
        private const int kMaxAttempts = 16;
        private readonly ISharedKeyValueStore m_store;
    }
}
