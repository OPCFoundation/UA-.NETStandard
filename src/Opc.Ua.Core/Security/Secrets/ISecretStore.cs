/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Provider of <see cref="ISecret"/> material for a single
    /// <see cref="StoreType"/>. The store decides how the bytes are
    /// persisted, materialised, and released.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stores are typically registered with an <see cref="ISecretRegistry"/>
    /// which dispatches lookups by
    /// <see cref="SecretIdentifier.StoreType"/>.
    /// </para>
    /// <para>
    /// <see cref="ISecretStore"/> is intentionally NOT
    /// <see cref="IDisposable"/> in this API revision; secure-memory
    /// management of cached secrets is the implementation's concern and
    /// will be expanded in a follow-up phase.
    /// </para>
    /// </remarks>
    public interface ISecretStore
    {
        /// <summary>
        /// The store-type discriminator — must match
        /// <see cref="SecretIdentifier.StoreType"/> for entries this
        /// store serves (e.g. <c>"InMemory"</c>).
        /// </summary>
        string StoreType { get; }

        /// <summary>
        /// Synchronous fast-path lookup. Returns a fresh
        /// <see cref="ISecret"/> when the secret is materially present
        /// without I/O (e.g. cache hit), or <see langword="null"/> if a
        /// store call would be required.
        /// </summary>
        /// <remarks>
        /// Callers that want guaranteed lookup (cache + cold path) should
        /// use <see cref="GetAsync"/> instead. The returned secret MUST
        /// be disposed by the caller.
        /// </remarks>
        ISecret? TryGet(SecretIdentifier id);

        /// <summary>
        /// Resolves a secret, falling through to the store's cold path
        /// when no in-memory copy is available. Implementations that can
        /// answer synchronously should return a completed
        /// <see cref="ValueTask{TResult}"/> with no allocation.
        /// </summary>
        /// <returns>
        /// A fresh <see cref="ISecret"/> the caller must dispose, or
        /// <see langword="null"/> when the identifier is unknown.
        /// </returns>
        ValueTask<ISecret?> GetAsync(
            SecretIdentifier id,
            CancellationToken ct = default);

        /// <summary>
        /// Stores or replaces the bytes for the supplied identifier.
        /// </summary>
        ValueTask SetAsync(
            SecretIdentifier id,
            ReadOnlyMemory<byte> bytes,
            CancellationToken ct = default);

        /// <summary>
        /// Removes the secret if present.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when an entry was removed,
        /// <see langword="false"/> when the identifier was unknown.
        /// </returns>
        ValueTask<bool> RemoveAsync(
            SecretIdentifier id,
            CancellationToken ct = default);
    }
}
