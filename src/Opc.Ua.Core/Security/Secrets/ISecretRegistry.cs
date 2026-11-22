// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// A multi-store dispatcher for <see cref="SecretIdentifier"/>
    /// lookups. Routes each identifier to the registered
    /// <see cref="ISecretStore"/> whose
    /// <see cref="ISecretStore.StoreType"/> matches.
    /// </summary>
    public interface ISecretRegistry
    {
        /// <summary>
        /// Registers a store. If a store with the same
        /// <see cref="ISecretStore.StoreType"/> is already registered,
        /// it is replaced.
        /// </summary>
        void RegisterStore(ISecretStore store);

        /// <summary>
        /// Synchronous fast-path lookup. Returns a fresh
        /// <see cref="ISecret"/> when the matching store can answer
        /// without I/O, otherwise <see langword="null"/>.
        /// </summary>
        ISecret? TryGet(SecretIdentifier id);

        /// <summary>
        /// Resolves a secret via the matching store. Returns
        /// <see langword="null"/> when no store is registered for the
        /// identifier's <see cref="SecretIdentifier.StoreType"/>, or
        /// when the store has no entry for the identifier.
        /// </summary>
        ValueTask<ISecret?> GetAsync(
            SecretIdentifier id,
            CancellationToken ct = default);
    }
}
