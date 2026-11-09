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

namespace Opc.Ua.Server.Fluent
{
    public abstract partial class FluentNodeManagerBase
    {
        /// <summary>
        /// Registers one composite lifetime resource for a currently indexed node.
        /// The manager awaits its disposal on actual node removal and on manager disposal.
        /// </summary>
        /// <remarks>
        /// A resource must make concurrent DisposeAsync calls join the same cleanup.
        /// A failed registration leaves ownership with the caller. Unlike a change-mask callback,
        /// this lifetime is invoked by the removal operation, not by an unrelated value publisher.
        /// </remarks>
        /// <param name="node">The exact indexed node which owns the resource.</param>
        /// <param name="resource">The asynchronous lifetime resource.</param>
        public void RegisterNodeResource(NodeState node, IAsyncDisposable resource)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            using NodeManagerOperation operation = BeginNodeManagerOperation();
            lock (m_nodeResourceGate)
            {
                if (m_nodeResourceAdmissionClosed)
                {
                    throw new ObjectDisposedException(nameof(FluentNodeManagerBase));
                }
                if (!ReferenceEquals(FindPredefinedNode<NodeState>(node.NodeId), node))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                        "The resource owner must be the exact indexed node.");
                }
                if (m_nodeResources.ContainsKey(node.NodeId))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The node already has a composite lifetime resource.");
                }
                m_nodeResources.Add(node.NodeId, new OwnedNodeResource(node, resource));
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnNodeRemovedAsync(
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await ReleaseOwnedNodeResourceAsync(node).ConfigureAwait(false);
            }
            finally
            {
                await base.OnNodeRemovedAsync(node, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases a removed node without holding the registry gate while its callbacks run.
        /// </summary>
        private async ValueTask ReleaseOwnedNodeResourceAsync(NodeState node)
        {
            OwnedNodeResource? owned;
            lock (m_nodeResourceGate)
            {
                if (!m_nodeResources.TryGetValue(node.NodeId, out owned) || !ReferenceEquals(owned.Node, node))
                {
                    return;
                }
            }
            try
            {
                await owned.Resource.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                lock (m_nodeResourceGate)
                {
                    if (m_nodeResources.TryGetValue(node.NodeId, out OwnedNodeResource? current) &&
                        ReferenceEquals(current, owned))
                    {
                        m_nodeResources.Remove(node.NodeId);
                    }
                }
            }
        }

        /// <summary>
        /// Drains resources left by disposal-only shutdown, including another operation's pending node removal.
        /// </summary>
        private async ValueTask<List<Exception>> ReleaseOwnedNodeResourcesAsync()
        {
            List<OwnedNodeResource> owned;
            lock (m_nodeResourceGate)
            {
                owned = [.. m_nodeResources.Values];
            }
            var failures = new List<Exception>();
            foreach (OwnedNodeResource entry in owned)
            {
                try
                {
                    await ReleaseOwnedNodeResourceAsync(entry.Node).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    failures.Add(error);
                }
            }
            return failures;
        }

        /// <summary>
        /// Prevents teardown callbacks from creating new ownership after the cleanup snapshot is taken.
        /// </summary>
        private void StopNodeResourceAdmission()
        {
            lock (m_nodeResourceGate)
            {
                m_nodeResourceAdmissionClosed = true;
            }
        }

        /// <summary>
        /// Retains the exact node identity while asynchronous cleanup is pending.
        /// </summary>
        private sealed record OwnedNodeResource(NodeState Node, IAsyncDisposable Resource);

        /// <summary>Protects registry membership only, never asynchronous disposal.</summary>
        private readonly Lock m_nodeResourceGate = new();

        /// <summary>Owned node lifetimes, retained until their cleanup has completed.</summary>
        private readonly Dictionary<NodeId, OwnedNodeResource> m_nodeResources = [];

        /// <summary>Closes registration before manager teardown starts invoking callbacks.</summary>
        private bool m_nodeResourceAdmissionClosed;
    }
}
