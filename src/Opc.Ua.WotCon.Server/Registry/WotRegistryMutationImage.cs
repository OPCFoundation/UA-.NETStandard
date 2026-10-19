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

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Captures the registry state to be installed by a prepared mutation publication.
    /// </summary>
    internal sealed class WotRegistryMutationImage
    {
        /// <summary>
        /// Initializes a new <c>WotRegistryMutationImage</c> instance.
        /// </summary>
        /// <param name="previous">The previous used to initialize the instance.</param>
        /// <param name="desired">The desired used to initialize the instance.</param>
        /// <param name="changedResourceXids">The changed resource xids used to initialize the instance.</param>
        public WotRegistryMutationImage(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot desired,
            ArrayOf<string> changedResourceXids)
        {
            Previous = previous ?? throw new ArgumentNullException(nameof(previous));
            Desired = desired ?? throw new ArgumentNullException(nameof(desired));
            if (desired.Generation != previous.Generation ||
                desired.RefreshGeneration != previous.RefreshGeneration)
            {
                throw new ArgumentException("Mutation planning cannot allocate a committed generation.", nameof(desired));
            }
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (string xid in changedResourceXids)
            {
                if (string.IsNullOrWhiteSpace(xid) || !distinct.Add(xid) ||
                    previous.FindResourceByXid(xid) is null && desired.FindResourceByXid(xid) is null &&
                    !HasGroup(previous, xid) && !HasGroup(desired, xid))
                {
                    throw new ArgumentException(
                        "Mutation changes must identify distinct Resources in the previous or desired image.",
                        nameof(changedResourceXids));
                }
            }
            if (distinct.Count == 0)
            {
                throw new ArgumentException("A mutation must identify its changed Resources.", nameof(changedResourceXids));
            }
            ChangedResourceXids = [.. changedResourceXids];
        }

        /// <summary>
        /// Gets the previous for <c>WotRegistryMutationImage</c>.
        /// </summary>
        public WotRegistrySnapshot Previous { get; }

        /// <summary>
        /// Gets the desired for <c>WotRegistryMutationImage</c>.
        /// </summary>
        public WotRegistrySnapshot Desired { get; }

        /// <summary>
        /// Gets the changed resource xids for <c>WotRegistryMutationImage</c>.
        /// </summary>
        public ArrayOf<string> ChangedResourceXids { get; }

        private static bool HasGroup(WotRegistrySnapshot snapshot, string xid)
        {
            foreach (WotResourceGroup group in snapshot.Groups.Values)
            {
                if (group.Xid == xid)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Exposes the mutation image associated with a prepared registry publication.
    /// </summary>
    internal interface IWotRegistryMutationPublication : IWotRegistryPublication
    {
        /// <summary>
        /// Prepares a registry mutation image together with its resource projections.
        /// </summary>
        /// <param name="mutation">The WoT registry mutation image.</param>
        /// <param name="projections">The resource projections to publish.</param>
        /// <param name="refreshGeneration">The refresh generation.</param>
        /// <param name="canonicalViewGraphState">The canonical view graph state.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<IWotPreparedRegistryPublication> PrepareMutationAsync(
            WotRegistryMutationImage mutation,
            ArrayOf<WotResourceProjection> projections,
            uint refreshGeneration,
            ByteString canonicalViewGraphState = default,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Carries the WoT registry lifecycle plan data.
    /// </summary>
    /// <param name="Mutation">The WoT registry mutation image.</param>
    /// <param name="Resource">The WoT resource.</param>
    /// <param name="Result">The WoT delete result.</param>
    internal sealed record WotRegistryLifecyclePlan(
        WotRegistryMutationImage? Mutation, WotResource? Resource, WotDeleteResult Result);
}
