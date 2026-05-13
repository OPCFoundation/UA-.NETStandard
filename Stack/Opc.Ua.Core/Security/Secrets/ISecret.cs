/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 *
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

#nullable enable

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
