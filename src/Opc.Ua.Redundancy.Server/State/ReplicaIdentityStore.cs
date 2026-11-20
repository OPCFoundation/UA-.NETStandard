// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Binds existing index-form address-space records to one replica-set identity contract.
    /// </summary>
    internal static class ReplicaIdentityStore
    {
        /// <summary>
        /// Validates the protected identity contract before address-space state is read or written.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Coordination is unavailable, the contract differs, or existing state has no identity contract.
        /// </exception>
        /// <exception cref="ServiceResultException">The stored contract cannot be authenticated.</exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> or <paramref name="protector"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="descriptor"/> is null or empty.</exception>
        internal static async ValueTask VerifyAsync(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            ByteString descriptor,
            CancellationToken cancellationToken)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            if (descriptor.IsNull || descriptor.IsEmpty)
            {
                throw new ArgumentException("A nonempty replica identity descriptor is required.", nameof(descriptor));
            }
            InMemoryNodeStateStore.ValidateCoordinator(store, Key);
            if (store is ISharedKeyValueStoreConsistency protectionConsistency &&
                !protectionConsistency.IsProcessLocal(Key) &&
                protector is NullRecordProtector)
            {
                throw new InvalidOperationException("A replicated identity contract requires record protection.");
            }
            (bool found, ByteString stored) = await store.TryGetAsync(Key, cancellationToken).ConfigureAwait(false);
            if (found)
            {
                Validate(protector, stored, descriptor);
                return;
            }

            if (store is not ISharedKeyValueStoreConsistency consistency ||
                !consistency.IsLinearizable("n/") ||
                !consistency.IsLinearizable("v/") ||
                !consistency.IsLinearizable("dlog/") ||
                !consistency.IsLinearizable("partition/"))
            {
                throw new InvalidOperationException(
                    "An eventual replica view cannot prove that a store is new. Provision the protected replica " +
                    "identity contract on a verified new store before connecting its replicated payload backend.");
            }

            foreach (string prefix in s_statePrefixes)
            {
                await foreach (KeyValuePair<string, ByteString> _ in store.ScanAsync(prefix, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "Existing address-space state has no replica identity contract. Validate and migrate its " +
                        "namespace layout before enabling replica NodeId identity; existing records were not changed.");
                }
            }

            ByteString encoded = protector.Protect(
                RecordProtectionContext.Create("replica-identity", Key), descriptor);
            if (await store.CompareAndSwapAsync(Key, default, encoded, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            (found, stored) = await store.TryGetAsync(Key, cancellationToken).ConfigureAwait(false);
            if (!found)
            {
                throw new InvalidOperationException("The replica identity contract disappeared during initialization.");
            }
            Validate(protector, stored, descriptor);
        }

        private static void Validate(IRecordProtector protector, ByteString stored, ByteString descriptor)
        {
            if (!protector.TryUnprotect(
                RecordProtectionContext.Create("replica-identity", Key), stored, out ByteString actual))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The stored replica identity contract cannot be authenticated.");
            }
            if (actual != descriptor)
            {
                throw new InvalidOperationException(
                    "The shared store belongs to a different replica identity contract. " +
                    "Replica-set identity, shared namespace slots and NodeId policy must agree.");
            }
        }

        /// <summary>
        /// The strong key binding this address-space store to a fixed identity contract.
        /// </summary>
        internal const string Key = "election/addressspace-identity/v1";

        private static readonly string[] s_statePrefixes = ["n/", "v/", "dlog/", "snapmeta/", "partition/"];
    }
}
