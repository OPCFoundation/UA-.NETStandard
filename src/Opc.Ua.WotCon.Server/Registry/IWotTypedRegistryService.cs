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
    /// Additive exact-authority provisioning capability. Allocation and structural creation
    /// commit together through the registry's existing store.
    /// </summary>
    public interface IWotTypedRegistryService
    {
        /// <summary>
        /// Creates a group for an exact kind/catalogue authority, rejecting an existing group.
        /// </summary>
        ValueTask<WotDocumentGroupResult> CreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically resolves or creates a group for an exact kind/catalogue authority.
        /// </summary>
        ValueTask<WotDocumentGroupResult> GetOrCreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an exact Version under the group's exact document authority.
        /// Empty VersionId requests the next allocation, not a pending or default Version.
        /// </summary>
        ValueTask<WotDocumentResourceResult> CreateDocumentResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId = "",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically resolves or creates a Resource and exact Version. Empty VersionId
        /// selects an existing Resource's default, or allocates its first Version.
        /// </summary>
        ValueTask<WotDocumentResourceResult> GetOrCreateDocumentResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId = "",
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The committed group allocation and whether this invocation created it.
    /// </summary>
    /// <param name="Group">The complete immutable group, including its exact CatalogUri.</param>
    /// <param name="Created">Whether this invocation committed a new group.</param>
    public sealed record WotDocumentGroupResult(WotResourceGroup Group, bool Created);

    /// <summary>
    /// The committed logical Resource and exact Version allocation.
    /// </summary>
    /// <param name="Resource">The logical Resource with immutable source identity.</param>
    /// <param name="Version">The exact Version selected or created.</param>
    /// <param name="CreatedResource">Whether this invocation created the logical Resource.</param>
    /// <param name="CreatedVersion">Whether this invocation created the exact Version.</param>
    public sealed record WotDocumentResourceResult(
        WotResource Resource,
        WotResourceVersion Version,
        bool CreatedResource,
        bool CreatedVersion);
}
