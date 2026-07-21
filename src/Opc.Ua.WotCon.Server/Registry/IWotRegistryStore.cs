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
    /// Persists the immutable registry snapshot. Implementations are
    /// responsible for durability and, for file-backed stores, bounded atomic
    /// replacement. Invalid documents are stored together with their failure
    /// state so a restart restores exactly the last observed registry contents.
    /// </summary>
    public interface IWotRegistryStore
    {
        /// <summary>
        /// Loads the persisted registry into an immutable snapshot. Returns
        /// <see cref="WotRegistrySnapshot.Empty"/> when no state is persisted.
        /// </summary>
        ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Durably upserts a group (its metadata; resources are persisted
        /// separately through <see cref="UpsertResourceAsync"/>).
        /// </summary>
        ValueTask UpsertGroupAsync(
            WotResourceGroup group,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Durably upserts a resource, including all of its version source
        /// bytes and its load/validation state, using an atomic replace.
        /// </summary>
        ValueTask UpsertResourceAsync(
            WotResource resource,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Durably persists the registry-level label set and its epoch (the
        /// snapshot generation at the time of the mutation). Group and
        /// resource labels are persisted as part of their owning entity
        /// through <see cref="UpsertGroupAsync"/> / <see cref="UpsertResourceAsync"/>.
        /// </summary>
        ValueTask UpsertRegistryLabelsAsync(
            System.Collections.Immutable.ImmutableSortedDictionary<string, string> labels,
            long epoch,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Durably removes a resource and all of its versions.
        /// </summary>
        ValueTask RemoveResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Durably removes a group and all of its resources.
        /// </summary>
        ValueTask RemoveGroupAsync(
            string groupId,
            CancellationToken cancellationToken = default);
    }
}
