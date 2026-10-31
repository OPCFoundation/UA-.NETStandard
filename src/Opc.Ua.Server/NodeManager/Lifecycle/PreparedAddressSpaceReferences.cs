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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns reference edits discovered against one pinned view before publication serialization.
    /// </summary>
    internal sealed class PreparedAddressSpaceReferences : IDisposable
    {
        /// <summary>
        /// Creates the reference plan whose snapshots are revalidated before the durable decision.
        /// </summary>
        internal PreparedAddressSpaceReferences(
            NodeManagerRoutingTable.RoutingSnapshot view,
            Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> previous,
            Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> next,
            Dictionary<NodeState.ReferenceUpdate, NodeManagerGeneration> owners,
            Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> additions,
            Dictionary<IAsyncNodeManager, List<LocalReference>> deferredRemovals)
        {
            View = view;
            Previous = previous;
            Next = next;
            Owners = owners;
            Additions = additions;
            DeferredRemovals = deferredRemovals;
        }

        /// <summary>
        /// Gets the serving view used to resolve every reference owner.
        /// </summary>
        internal NodeManagerRoutingTable.RoutingSnapshot View { get; }

        /// <summary>
        /// Gets the original contribution map, whose identity must remain unchanged.
        /// </summary>
        internal Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> Previous { get; }

        /// <summary>
        /// Gets the contribution map installed at publication.
        /// </summary>
        internal Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> Next { get; }

        /// <summary>
        /// Gets versioned reference edits and the generations owning their observers.
        /// </summary>
        internal Dictionary<NodeState.ReferenceUpdate, NodeManagerGeneration> Owners { get; }

        /// <summary>
        /// Gets external owner additions reconciled after publication.
        /// </summary>
        internal Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> Additions { get; }

        /// <summary>
        /// Gets removals that remain owned by retiring contributors.
        /// </summary>
        internal Dictionary<IAsyncNodeManager, List<LocalReference>> DeferredRemovals { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (NodeState.ReferenceUpdate update in Owners.Keys)
            {
                update.Dispose();
            }
        }
    }
}
