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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// A content-addressed store that writes into a staging area and reads from
    /// a committed area first, so that document bytes can be made durable before
    /// the manifest that names them is switched in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry writes a document's bytes before it commits the manifest
    /// that references them, so that a manifest never names bytes that are not
    /// yet on stable storage. Writing them straight into the committed area
    /// would break the opposite guarantee: a committed blob area that exists
    /// without a manifest is how <see cref="FileWotRegistryStore"/> recognises a
    /// lost generation or a crashed commit, and it fails closed rather than
    /// report an empty registry and discard data.
    /// </para>
    /// <para>
    /// Staging separates the two. Writes land in the staging area, which carries
    /// no such meaning, and the commit promotes the entries its snapshot
    /// references into the committed area as artifacts it owns. Reads prefer the
    /// committed area and fall back to staging so that content written but not
    /// yet committed is still readable within the transaction that wrote it.
    /// </para>
    /// <para>
    /// A staged entry that never gets promoted is orphaned, and orphans are safe
    /// to delete: nothing can reference a blob until a manifest names it, and a
    /// manifest only names promoted entries.
    /// </para>
    /// </remarks>
    internal sealed class StagedResourceStore : IXRegistryResourceStore, IDisposable
    {
        /// <summary>
        /// Initializes a staged store over a committed and a staging area.
        /// </summary>
        /// <param name="committed">The area holding promoted content.</param>
        /// <param name="staging">The area receiving writes.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="committed"/> or <paramref name="staging"/> is null.
        /// </exception>
        public StagedResourceStore(
            IXRegistryResourceStore committed,
            IXRegistryResourceStore staging)
        {
            Committed = committed ?? throw new ArgumentNullException(nameof(committed));
            Staging = staging ?? throw new ArgumentNullException(nameof(staging));
        }

        /// <summary>
        /// Gets the area holding promoted content.
        /// </summary>
        public IXRegistryResourceStore Committed { get; }

        /// <summary>
        /// Gets the area that receives writes until they are promoted.
        /// </summary>
        public IXRegistryResourceStore Staging { get; }

        /// <summary>
        /// Disposes both areas when they own disposable resources.
        /// </summary>
        public void Dispose()
        {
            (Committed as IDisposable)?.Dispose();
            (Staging as IDisposable)?.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ReadAsync(
            string resourceKey,
            long offset,
            int count,
            CancellationToken ct = default)
        {
            IXRegistryResourceStore source = await ResolveReadSourceAsync(resourceKey, ct)
                .ConfigureAwait(false);
            return await source.ReadAsync(resourceKey, offset, count, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            string resourceKey,
            long offset,
            ByteString data,
            CancellationToken ct = default)
        {
            return Staging.WriteAsync(resourceKey, offset, data, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<long> GetLengthAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            long committed = await Committed.GetLengthAsync(resourceKey, ct)
                .ConfigureAwait(false);
            if (committed >= 0)
            {
                return committed;
            }
            return await Staging.GetLengthAsync(resourceKey, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            bool removedStaged = await Staging.DeleteAsync(resourceKey, ct)
                .ConfigureAwait(false);
            bool removedCommitted = await Committed.DeleteAsync(resourceKey, ct)
                .ConfigureAwait(false);
            return removedStaged || removedCommitted;
        }

        /// <summary>
        /// Reads a staged entry in full, or returns a null
        /// <see cref="ByteString"/> when the key is not staged.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="ct">The cancellation token.</param>
        public async ValueTask<ByteString> ReadStagedAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            long length = await Staging.GetLengthAsync(resourceKey, ct).ConfigureAwait(false);
            if (length < 0)
            {
                return default;
            }
            if (length > int.MaxValue)
            {
                throw new NotSupportedException(
                    $"Staged WoT registry document '{resourceKey}' is {length} bytes, " +
                    "which exceeds the largest promotable size.");
            }
            return await Staging.ReadAsync(resourceKey, 0, (int)length, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns <c>true</c> when the key already exists in the committed area.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="ct">The cancellation token.</param>
        public async ValueTask<bool> IsCommittedAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            return await Committed.GetLengthAsync(resourceKey, ct).ConfigureAwait(false) >= 0;
        }

        /// <summary>
        /// Removes the staged copies of the supplied keys, ignoring keys that
        /// are not staged. Used to sweep entries once they are promoted.
        /// </summary>
        /// <param name="resourceKeys">The store keys to unstage.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="resourceKeys"/> is null.
        /// </exception>
        public async ValueTask UnstageAsync(
            IEnumerable<string> resourceKeys,
            CancellationToken ct = default)
        {
            if (resourceKeys is null)
            {
                throw new ArgumentNullException(nameof(resourceKeys));
            }
            foreach (string key in resourceKeys)
            {
                await Staging.DeleteAsync(key, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask<IXRegistryResourceStore> ResolveReadSourceAsync(
            string resourceKey,
            CancellationToken ct)
        {
            // Committed content wins so that a promoted blob is served from the
            // area whose durability the store guarantees. GetLengthAsync is the
            // only existence probe the contract offers, and it validates the key
            // the same way ReadAsync does, so an invalid key still throws.
            long committed = await Committed.GetLengthAsync(resourceKey, ct)
                .ConfigureAwait(false);
            return committed >= 0 ? Committed : Staging;
        }
    }
}
