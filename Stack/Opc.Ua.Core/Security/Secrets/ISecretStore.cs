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
