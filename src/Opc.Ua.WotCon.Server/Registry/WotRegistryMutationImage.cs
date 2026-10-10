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
    internal sealed class WotRegistryMutationImage
    {
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

        public WotRegistrySnapshot Previous { get; }
        public WotRegistrySnapshot Desired { get; }
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

    internal interface IWotRegistryMutationPublication : IWotRegistryPublication
    {
        ValueTask<IWotPreparedRegistryPublication> PrepareMutationAsync(
            WotRegistryMutationImage mutation,
            ArrayOf<WotResourceProjection> projections,
            uint refreshGeneration,
            ByteString canonicalViewGraphState = default,
            CancellationToken cancellationToken = default);
    }

    internal sealed record WotRegistryLifecyclePlan(
        WotRegistryMutationImage? Mutation, WotResource? Resource, WotDeleteResult Result);
}
