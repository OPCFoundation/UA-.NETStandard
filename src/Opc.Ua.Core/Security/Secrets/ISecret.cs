/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A materialised secret produced by an <see cref="ISecretStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ISecret"/> is a per-call view onto the underlying
    /// material. Each <see cref="ISecretStore.TryGet"/> /
    /// <see cref="ISecretStore.GetAsync"/> returns a fresh instance which
    /// the caller MUST dispose when finished. Unlike a refcounted secret,
    /// there is no shared ownership: two consumers calling the registry
    /// receive two independent <see cref="ISecret"/> instances and each
    /// disposes its own.
    /// </para>
    /// <para>
    /// The store implementation chooses what disposal means. Examples:
    /// <list type="bullet">
    ///   <item>An <c>InMemorySecret</c> may simply drop its reference
    ///         (best-effort; secure clearing can be added later).</item>
    ///   <item>A leased / LRU-cached implementation may return the lease
    ///         to the cache on disposal, with the cache calling
    ///         <see cref="Array.Clear(Array,int,int)"/> on
    ///         eviction.</item>
    ///   <item>A Key Vault / Kubernetes / DPAPI implementation may
    ///         discard the locally materialised bytes, release the
    ///         watch handle, or clear the protected memory.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface ISecret : IDisposable
    {
        /// <summary>
        /// Returns a view of the secret's raw bytes. The span is only
        /// valid for the lifetime of this <see cref="ISecret"/>; do not
        /// retain it past <see cref="IDisposable.Dispose"/>.
        /// </summary>
        ReadOnlySpan<byte> Bytes { get; }
    }
}
