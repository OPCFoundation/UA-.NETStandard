// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Commits authenticated shared records against a separate durable freshness authority.
    /// </summary>
    /// <remarks>
    /// The authority must be a linearizable, durable store that the payload-store writer
    /// cannot modify. The caller must enforce that separation with distinct access controls.
    /// Restore of the authority itself to an earlier state in the same epoch is not detected.
    /// Payload records are immutable and can be retained by snapshots and watches; do not
    /// delete them while replicas are running. The supplied dependencies are borrowed.
    /// </remarks>
    public sealed class FreshnessProtectedKeyValueStore : IDeadlineGuardedSharedKeyValueStore, ISharedKeyValueStoreConsistency
    {
        /// <summary>
        /// Creates a view bound to one deployment and its explicitly enrolled authority epoch.
        /// </summary>
        /// <param name="payloadStore">The store for staged encrypted payloads.</param>
        /// <param name="authorityStore">The independently protected durable authority.</param>
        /// <param name="protector">The record protector used by participating replicas.</param>
        /// <param name="deploymentId">The deployment identity shared by participating replicas.</param>
        /// <param name="epoch">The enrolled authority epoch shared by participating replicas.</param>
        public FreshnessProtectedKeyValueStore(
            ISharedKeyValueStore payloadStore,
            ISharedKeyValueStore authorityStore,
            IRecordProtector protector,
            Guid deploymentId,
            Guid epoch)
        {
            m_payloadStore = payloadStore ?? throw new ArgumentNullException(nameof(payloadStore));
            m_authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            if (ReferenceEquals(payloadStore, authorityStore))
            {
                throw new ArgumentException("The freshness authority must be independent of the payload store.",
                    nameof(authorityStore));
            }
            if (protector is NullRecordProtector)
            {
                throw new ArgumentException("Freshness-protected records require authenticated encryption.",
                    nameof(protector));
            }
            if (deploymentId == Guid.Empty)
            {
                throw new ArgumentException("A nonempty deployment identity is required.", nameof(deploymentId));
            }
            if (epoch == Guid.Empty)
            {
                throw new ArgumentException("A nonempty authority epoch is required.", nameof(epoch));
            }

            string root = $"freshness/v1/{deploymentId:N}/";
            m_epochKey = $"{root}epoch";
            m_epoch = ByteString.From(epoch.ToByteArray());
            m_generationPrefix = $"{root}{epoch:N}/generation/";
            m_payloadPrefix = $"{root}{epoch:N}/payload/";
            if (authorityStore is not ISharedKeyValueStoreConsistency consistency ||
                !IsSharedAndLinearizable(consistency, m_epochKey) ||
                !IsSharedAndLinearizable(consistency, m_generationPrefix))
            {
                throw new ArgumentException("The freshness authority must be shared and linearizable.",
                    nameof(authorityStore));
            }
        }

        /// <summary>
        /// Enrolls this epoch for a new deployment, or verifies a matching existing enrollment.
        /// This is an administrative bootstrap action and must not run automatically at startup.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask EnrollAsync(CancellationToken ct = default)
        {
            if (!await m_authorityStore.CompareAndSwapAsync(m_epochKey, default, m_epoch, ct).ConfigureAwait(false))
            {
                await VerifyEpochAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that the configured authority epoch exists and matches the enrollment.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask VerifyEpochAsync(CancellationToken ct = default)
        {
            (bool found, ByteString value) = await m_authorityStore.TryGetAsync(m_epochKey, ct).ConfigureAwait(false);
            if (!found || value != m_epoch)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The freshness authority is not enrolled for the configured deployment epoch.");
            }
        }

        /// <inheritdoc/>
        public bool IsLinearizable(string key)
        {
            return true;
        }

        /// <inheritdoc/>
        public bool IsProcessLocal(string key)
        {
            return false;
        }

        /// <summary>
        /// Gets whether the independent authority can enforce a guarded update in one commit.
        /// </summary>
        public bool SupportsGuardedWrites => m_authorityStore is IGuardedSharedKeyValueStore;

        /// <inheritdoc/>
        public bool SupportsGuardedDeadlines =>
            m_authorityStore is IDeadlineGuardedSharedKeyValueStore { SupportsGuardedDeadlines: true };

        /// <inheritdoc/>
        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            return ReadAsync(GetGenerationKey(key), retainHistory: false, ct);
        }

        /// <summary>
        /// Reads a record that must retain its history; a committed deletion is not an unused key.
        /// </summary>
        /// <param name="key">The record key.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Whether a record has been committed, and its authenticated value.</returns>
        public ValueTask<(bool Found, ByteString Value)> TryGetRetainedAsync(string key, CancellationToken ct = default)
        {
            return ReadAsync(GetGenerationKey(key), retainHistory: true, ct);
        }

        /// <summary>
        /// Writes a non-null record. Use <see cref="DeleteAsync"/> to remove the record.
        /// </summary>
        /// <inheritdoc/>
        public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            if (value.IsNull)
            {
                throw new ArgumentException("A non-null value is required.", nameof(value));
            }
            _ = await CommitAsync(GetGenerationKey(key), default, requireExisting: false, value, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            return CommitAsync(GetGenerationKey(key), (expected, true), requireExisting: false, value, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> CompareAndSwapWhenAsync(
            string key,
            ByteString expected,
            ByteString value,
            string guardKey,
            ByteString guardExpected,
            CancellationToken ct = default)
        {
            return CompareAndSwapWithGuardAsync(key, expected, value, guardKey, guardExpected, null, ct);
        }

        /// <inheritdoc/>
        public ValueTask<bool> CompareAndSwapBeforeAsync(
            string key,
            ByteString expected,
            ByteString value,
            string guardKey,
            ByteString guardExpected,
            DateTimeOffset deadline,
            CancellationToken ct = default)
        {
            if (!SupportsGuardedDeadlines)
            {
                throw new NotSupportedException("The freshness authority cannot enforce a deadline at commit.");
            }
            return CompareAndSwapWithGuardAsync(key, expected, value, guardKey, guardExpected, deadline, ct);
        }

        private async ValueTask<bool> CompareAndSwapWithGuardAsync(
            string key,
            ByteString expected,
            ByteString value,
            string guardKey,
            ByteString guardExpected,
            DateTimeOffset? deadline,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(guardKey) || key == guardKey || guardExpected.IsNull)
            {
                throw new ArgumentException("A separate guard with a non-null expected value is required.");
            }
            if (m_authorityStore is not IGuardedSharedKeyValueStore guarded)
            {
                throw new NotSupportedException("The freshness authority cannot atomically verify a guard.");
            }

            string target = GetGenerationKey(key);
            string guardGenerationKey = GetGenerationKey(guardKey);
            await VerifyEpochAsync(ct).ConfigureAwait(false);
            (bool found, ByteString pointer) = await guarded.TryGetAsync(guardGenerationKey, ct).ConfigureAwait(false);
            if (!found || await ReadValueAsync(guardGenerationKey, pointer, ct).ConfigureAwait(false) != guardExpected)
            {
                return false;
            }

            return await CommitAsync(
                target, (expected, true), requireExisting: false, value, ct,
                guard: new CommitGuard(guarded, guardGenerationKey, pointer, deadline)).ConfigureAwait(false);
        }

        /// <summary>
        /// Conditionally writes a non-null record without recreating a previously deleted key.
        /// </summary>
        /// <param name="key">The record key.</param>
        /// <param name="expected">The expected authenticated value, or null if the key has never been used.</param>
        /// <param name="value">The value to commit.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Whether the value was committed.</returns>
        public ValueTask<bool> CompareAndSwapRetainedAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            if (value.IsNull)
            {
                throw new ArgumentException("A retained record requires a non-null value.", nameof(value));
            }
            return CommitAsync(GetGenerationKey(key), (expected, true), requireExisting: false, value, ct,
                retainHistory: true);
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return CommitAsync(GetGenerationKey(key), default, requireExisting: true, default, ct);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await VerifyEpochAsync(ct).ConfigureAwait(false);
            await foreach (KeyValuePair<string, ByteString> item in m_authorityStore
                .ScanAsync(m_generationPrefix + keyPrefix, ct).ConfigureAwait(false))
            {
                ByteString value = await ReadValueAsync(item.Key, item.Value, ct).ConfigureAwait(false);
                if (!value.IsNull)
                {
                    yield return new KeyValuePair<string, ByteString>(item.Key[m_generationPrefix.Length..], value);
                }
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValueChange> WatchAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await VerifyEpochAsync(ct).ConfigureAwait(false);
            await foreach (KeyValueChange change in m_authorityStore
                .WatchAsync(m_generationPrefix + keyPrefix, ct).ConfigureAwait(false))
            {
                await VerifyEpochAsync(ct).ConfigureAwait(false);
                if (change.Kind != KeyValueChangeKind.Set)
                {
                    throw InvalidGeneration();
                }
                ByteString value = await ReadValueAsync(change.Key, change.Value, ct).ConfigureAwait(false);
                yield return new KeyValueChange
                {
                    Key = change.Key[m_generationPrefix.Length..],
                    Kind = value.IsNull ? KeyValueChangeKind.Delete : KeyValueChangeKind.Set,
                    Value = value
                };
            }
        }

        private async ValueTask<(bool Found, ByteString Value)> ReadAsync(
            string generationKey,
            bool retainHistory,
            CancellationToken ct)
        {
            await VerifyEpochAsync(ct).ConfigureAwait(false);
            (bool found, ByteString pointer) = await m_authorityStore.TryGetAsync(generationKey, ct)
                .ConfigureAwait(false);
            if (!found)
            {
                return (false, default);
            }

            ByteString value = await ReadValueAsync(generationKey, pointer, ct).ConfigureAwait(false);
            return value.IsNull && retainHistory ? throw DeletedRecord() : (!value.IsNull, value);
        }

        private async ValueTask<bool> CommitAsync(
            string generationKey,
            (ByteString Value, bool IsConditional) comparison,
            bool requireExisting,
            ByteString value,
            CancellationToken ct,
            bool retainHistory = false,
            CommitGuard? guard = null)
        {
            await VerifyEpochAsync(ct).ConfigureAwait(false);
            byte[]? digest = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                (bool found, ByteString currentPointer) = await m_authorityStore.TryGetAsync(generationKey, ct)
                    .ConfigureAwait(false);
                ByteString current = found
                    ? await ReadValueAsync(generationKey, currentPointer, ct).ConfigureAwait(false)
                    : default;
                if (found && current.IsNull && retainHistory)
                {
                    throw DeletedRecord();
                }
                if ((requireExisting && current.IsNull) ||
                    (comparison.IsConditional && current != comparison.Value))
                {
                    return false;
                }
                if (current == value)
                {
                    return guard is not CommitGuard unchangedGuard ||
                        await CommitUnderGuardAsync(unchangedGuard, generationKey, currentPointer, currentPointer, ct)
                            .ConfigureAwait(false);
                }
                if (digest == null && !value.IsNull)
                {
                    ByteString protectedValue = m_protector.Protect(
                        RecordProtectionContext.Create(kRecordType, generationKey), value);
                    digest = ComputeDigest(protectedValue);
                    await m_payloadStore.SetAsync(m_payloadPrefix + Utils.ToHexString(digest), protectedValue, ct)
                        .ConfigureAwait(false);
                }

                ulong version = found ? ReadVersion(currentPointer) : 0;
                if (version == ulong.MaxValue)
                {
                    throw InvalidGeneration();
                }
                byte[] pointer = new byte[sizeof(ulong) + (digest?.Length ?? 0)];
                BinaryPrimitives.WriteUInt64BigEndian(pointer, version + 1);
                digest?.CopyTo(pointer, sizeof(ulong));
                ByteString next = ByteString.From(pointer);
                if (guard is CommitGuard required)
                {
                    if (await CommitUnderGuardAsync(required, generationKey, currentPointer, next, ct)
                        .ConfigureAwait(false))
                    {
                        return true;
                    }
                    (bool guardFound, ByteString currentGuard) = await required.Authority
                        .TryGetAsync(required.Key, ct).ConfigureAwait(false);
                    if (!guardFound || currentGuard != required.Expected)
                    {
                        return false;
                    }
                }
                else if (await m_authorityStore.CompareAndSwapAsync(generationKey, currentPointer, next, ct)
                    .ConfigureAwait(false))
                {
                    return true;
                }
            }
        }

        private static ValueTask<bool> CommitUnderGuardAsync(
            in CommitGuard guard,
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct)
        {
            if (guard.Deadline is DateTimeOffset deadline)
            {
                if (guard.Authority is not IDeadlineGuardedSharedKeyValueStore { SupportsGuardedDeadlines: true } store)
                {
                    throw new NotSupportedException("The freshness authority cannot enforce a deadline at commit.");
                }
                return store.CompareAndSwapBeforeAsync(key, expected, value, guard.Key, guard.Expected, deadline, ct);
            }
            return guard.Authority.CompareAndSwapWhenAsync(key, expected, value, guard.Key, guard.Expected, ct);
        }

        private async ValueTask<ByteString> ReadValueAsync(
            string generationKey,
            ByteString pointer,
            CancellationToken ct)
        {
            _ = ReadVersion(pointer);
            if (pointer.Length == sizeof(ulong))
            {
                return default;
            }

            string payloadKey = m_payloadPrefix + Utils.ToHexString(pointer.Span[sizeof(ulong)..].ToArray());
            (bool found, ByteString protectedValue) = await m_payloadStore.TryGetAsync(payloadKey, ct)
                .ConfigureAwait(false);
            if (!found || protectedValue.IsNull)
            {
                throw UnavailablePayload();
            }

            ByteString envelope = ByteString.From(protectedValue.Span);
            if (!CryptoUtils.FixedTimeEquals(pointer.Span[sizeof(ulong)..], ComputeDigest(envelope)) ||
                !m_protector.TryUnprotect(
                    RecordProtectionContext.Create(kRecordType, generationKey), envelope, out ByteString value) ||
                value.IsNull)
            {
                throw UnavailablePayload();
            }
            return value;
        }

        private string GetGenerationKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A nonempty record key is required.", nameof(key));
            }
            return m_generationPrefix + key;
        }

        private static bool IsSharedAndLinearizable(ISharedKeyValueStoreConsistency consistency, string key)
        {
            return consistency.IsLinearizable(key) && !consistency.IsProcessLocal(key);
        }

        private static ulong ReadVersion(ByteString pointer)
        {
            if (pointer.Length != sizeof(ulong) && pointer.Length != sizeof(ulong) + kDigestSize)
            {
                throw InvalidGeneration();
            }
            ulong version = BinaryPrimitives.ReadUInt64BigEndian(pointer.Span);
            return version != 0 ? version : throw InvalidGeneration();
        }

        private static byte[] ComputeDigest(ByteString value)
        {
#if NET5_0_OR_GREATER
            return SHA256.HashData(value.Span);
#else
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(value.Span.ToArray());
#endif
        }

        private static ServiceResultException InvalidGeneration()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The freshness authority contains an invalid generation.");
        }

        private static ServiceResultException DeletedRecord()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "A shared record that must retain its history was deleted.");
        }

        private static ServiceResultException UnavailablePayload()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "The committed shared payload is unavailable or does not match the freshness authority.");
        }

        private readonly record struct CommitGuard(
            IGuardedSharedKeyValueStore Authority,
            string Key,
            ByteString Expected,
            DateTimeOffset? Deadline);

        private const string kRecordType = "freshness-v1";
        private const int kDigestSize = 32;
        private readonly ISharedKeyValueStore m_payloadStore;
        private readonly ISharedKeyValueStore m_authorityStore;
        private readonly IRecordProtector m_protector;
        private readonly string m_epochKey;
        private readonly string m_generationPrefix;
        private readonly string m_payloadPrefix;
        private readonly ByteString m_epoch;
    }
}
