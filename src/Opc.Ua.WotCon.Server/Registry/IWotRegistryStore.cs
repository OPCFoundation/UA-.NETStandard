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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Persists the immutable registry snapshot behind a <em>transactional
    /// commit</em> contract. A store owns exactly one committed generation and
    /// exposes it through <see cref="LoadAsync"/>; a mutation is made durable by
    /// committing a complete replacement generation through
    /// <see cref="CommitAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CommitAsync"/> is all-or-nothing. It must make the entire
    /// <see cref="WotRegistrySnapshot"/> durable and then switch the committed
    /// generation in a single atomic step, so a crash (or an injected failure)
    /// either leaves the previous generation fully intact or exposes the new
    /// generation in full — never a partially written mix. Invalid documents are
    /// committed together with their failure state, so a restart restores exactly
    /// the last committed registry contents.
    /// </para>
    /// <para>
    /// The <see cref="WotRegistryService"/> relies on this guarantee: it commits
    /// <em>before</em> it publishes the new snapshot or raises its change
    /// notification. When a commit throws, the caller-visible current snapshot is
    /// left unchanged, no notification is raised, and a retry re-attempts the same
    /// commit.
    /// </para>
    /// </remarks>
    public interface IWotRegistryStore
    {
        /// <summary>
        /// Loads the last committed registry generation into an immutable
        /// snapshot. Returns <see cref="WotRegistrySnapshot.Empty"/> when no
        /// generation has ever been committed. Never observes a partially
        /// written (staged, not-yet-committed) generation.
        /// </summary>
        ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Durably and atomically commits <paramref name="snapshot"/> as the new
        /// committed generation, replacing the previous one in full. Stages all
        /// backing state first and only switches the committed generation once it
        /// is durable, so the operation is all-or-nothing: on failure the store
        /// retains its previous committed generation unchanged.
        /// </summary>
        /// <param name="snapshot">The complete registry generation to commit.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask CommitAsync(
            WotRegistrySnapshot snapshot,
            CancellationToken cancellationToken = default);
    }
}
