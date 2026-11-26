// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Encodes and decodes shared-store values that carry a fencing token with the payload.
    /// </summary>
    internal static class FencedSharedStoreValue
    {
        /// <summary>
        /// Requires an actual freshness view unless a process-local memory store was explicitly supplied.
        /// </summary>
        internal static ISharedKeyValueStore RequireStore(ISharedKeyValueStore store)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            return store is FreshnessProtectedKeyValueStore or InMemorySharedKeyValueStore
                ? store
                : throw new ArgumentException(
                    "External PubSub records require a FreshnessProtectedKeyValueStore.", nameof(store));
        }

        /// <summary>
        /// Reads an authoritative record without treating prior deletion as a new record.
        /// </summary>
        internal static ValueTask<(bool Found, ByteString Value)> ReadRetainedAsync(
            ISharedKeyValueStore store,
            string key,
            CancellationToken cancellationToken)
        {
            return store is FreshnessProtectedKeyValueStore protectedStore
                ? protectedStore.TryGetRetainedAsync(key, cancellationToken)
                : store.TryGetAsync(key, cancellationToken);
        }

        /// <summary>
        /// Commits a record without recreating a previously deleted authoritative key.
        /// </summary>
        internal static ValueTask<bool> CompareAndSwapRetainedAsync(
            ISharedKeyValueStore store,
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken cancellationToken)
        {
            return store is FreshnessProtectedKeyValueStore protectedStore
                ? protectedStore.CompareAndSwapRetainedAsync(key, expected, value, cancellationToken)
                : store.CompareAndSwapAsync(key, expected, value, cancellationToken);
        }

        /// <summary>
        /// Decodes a fixed-size record with a positive fence, or a legacy process-local payload.
        /// </summary>
        internal static bool TryExtractFixedPayload(
            ByteString stored,
            int payloadLength,
            bool allowUnfenced,
            out ByteString payload,
            out long fencingToken)
        {
            payload = default;
            fencingToken = 0;
            ReadOnlySpan<byte> span = stored.Span;
            if (allowUnfenced && span.Length == payloadLength)
            {
                payload = stored;
                return true;
            }
            if (TryDecodeEnvelope(span, out long token, out int offset, out int length) &&
                token > 0 && length == payloadLength)
            {
                payload = ByteString.From(span.Slice(offset, length).ToArray());
                fencingToken = token;
                return true;
            }
            if (!allowUnfenced)
            {
                throw InvalidRecord();
            }
            return false;
        }

        /// <summary>
        /// Commits a fixed-size state record without allowing a stored fence to be lowered or discarded.
        /// </summary>
        internal static async ValueTask StoreFixedAsync(
            ISharedKeyValueStore store,
            string key,
            ByteString payload,
            long? fencingToken,
            CancellationToken cancellationToken)
        {
            bool allowUnfenced = store is InMemorySharedKeyValueStore;
            if ((!allowUnfenced && !fencingToken.HasValue) || fencingToken is <= 0)
            {
                throw new InvalidOperationException("The PubSub write requires a positive activation fence.");
            }
            ByteString next = fencingToken.HasValue ? Wrap(payload, fencingToken.Value) : payload;
            for (int attempt = 0; attempt < s_maxCompareAndSwapAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (bool found, ByteString current) = await ReadRetainedAsync(store, key, cancellationToken)
                    .ConfigureAwait(false);
                long currentFence = 0;
                if (found && !TryExtractFixedPayload(current, payload.Length, allowUnfenced, out _, out currentFence))
                {
                    throw InvalidRecord();
                }
                if (fencingToken.GetValueOrDefault() < currentFence)
                {
                    throw new InvalidOperationException("A newer activation fence already wrote this PubSub record.");
                }
                if (await CompareAndSwapRetainedAsync(store, key, current, next, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return;
                }
            }
            throw new InvalidOperationException("Unable to commit a fenced PubSub record due to concurrent writes.");
        }

        /// <summary>
        /// Attempts to unwrap a stored shared-store value into its payload and fencing token.
        /// </summary>
        /// <param name="stored">The stored value to inspect.</param>
        /// <param name="payload">The extracted payload, or the legacy unwrapped value.</param>
        /// <param name="fencingToken">The extracted fencing token, or zero for legacy values.</param>
        /// <returns>
        /// <c>true</c> when the value contains a payload or legacy unwrapped bytes; otherwise, <c>false</c>.
        /// </returns>
        public static bool TryExtractPayload(ByteString stored, out ByteString payload, out long fencingToken)
        {
            payload = default;
            fencingToken = 0;

            if (stored.IsNull)
            {
                return false;
            }

            ReadOnlySpan<byte> span = stored.Span;
            if (!TryDecodeEnvelope(span, out fencingToken, out int payloadOffset, out int payloadLength))
            {
                payload = stored;
                fencingToken = 0;
                return true;
            }

            payload = payloadLength == 0
                ? ByteString.Empty
                : new ByteString(span.Slice(payloadOffset, payloadLength).ToArray());
            return true;
        }

        /// <summary>
        /// Stores a payload in the shared store, wrapping it with a fencing token when one is supplied.
        /// </summary>
        /// <param name="store">The shared key/value store to update.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="payload">The payload to store.</param>
        /// <param name="fencingToken">The optional fencing token that guards against stale writes.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that completes when the value has been committed.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static async ValueTask StoreAsync(
            ISharedKeyValueStore store,
            string key,
            ByteString payload,
            long? fencingToken,
            CancellationToken cancellationToken)
        {
            if (!fencingToken.HasValue)
            {
                await store.SetAsync(key, payload, cancellationToken).ConfigureAwait(false);
                return;
            }

            for (int attempt = 0; attempt < s_maxCompareAndSwapAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (bool found, ByteString currentBytes) = await store
                    .TryGetAsync(key, cancellationToken)
                    .ConfigureAwait(false);
                long currentToken = 0;
                if (found && !currentBytes.IsNull)
                {
                    _ = TryExtractPayload(currentBytes, out _, out currentToken);
                }

                if (fencingToken.Value < currentToken)
                {
                    throw new InvalidOperationException(
                        $"Rejecting stale fenced write for '{key}'. Current token {currentToken} is newer than " +
                        $"caller token {fencingToken.Value}.");
                }

                ByteString expected = found ? currentBytes : default;
                ByteString next = Wrap(payload, fencingToken.Value);
                if (await store.CompareAndSwapAsync(key, expected, next, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Unable to commit fenced write for '{key}' after {s_maxCompareAndSwapAttempts} compare-and-swap " +
                "attempts.");
        }

        /// <summary>
        /// Encodes a payload with its fencing token for a conditional write.
        /// </summary>
        internal static ByteString Wrap(ByteString payload, long fencingToken)
        {
            byte[]? payloadBytes = payload.IsNull ? null : payload.ToArray();
            int payloadLength = payloadBytes?.Length ?? 0;
            byte[] buffer = new byte[s_headerLength + payloadLength];
            Span<byte> span = buffer;
            BinaryPrimitives.WriteUInt32LittleEndian(span[..4], s_magic);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), s_version);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8, 8), fencingToken);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16, 4), payloadLength);
            if (payloadLength > 0 && payloadBytes is not null)
            {
                payloadBytes.CopyTo(buffer, s_headerLength);
            }

            return new ByteString(buffer);
        }

        private static bool TryDecodeEnvelope(
            ReadOnlySpan<byte> span,
            out long fencingToken,
            out int payloadOffset,
            out int payloadLength)
        {
            fencingToken = 0;
            payloadOffset = 0;
            payloadLength = 0;

            if (span.Length < s_headerLength)
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != s_magic ||
                BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)) != s_version)
            {
                return false;
            }

            fencingToken = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8, 8));
            payloadLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16, 4));
            if (payloadLength < 0 || span.Length != s_headerLength + payloadLength)
            {
                fencingToken = 0;
                payloadLength = 0;
                return false;
            }

            payloadOffset = s_headerLength;
            return true;
        }

        private static ServiceResultException InvalidRecord()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The shared PubSub fence or payload is invalid.");
        }

        private const uint s_magic = 0x46535048;
        private const int s_version = 1;
        private const int s_headerLength = 20;
        private const int s_maxCompareAndSwapAttempts = 5;
    }
}
