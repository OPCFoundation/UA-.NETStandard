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

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// A request to materialize (or update) the OPC UA <c>View</c> Node for one
    /// projection document. The View Node's NodeId and the document resource
    /// Node's NodeId are assigned deterministically by the coordinator so the
    /// same request re-materializes the same View across refreshes.
    /// </summary>
    public sealed class WotViewProjectionRequest
    {
        /// <summary>
        /// Initializes a new request.
        /// </summary>
        /// <param name="closureKey">The stable closure key the document belongs to.</param>
        /// <param name="resourceXid">The document resource's registry Xid.</param>
        /// <param name="resourceNodeId">
        /// The document resource Node the <c>HasWoTProjection</c> reference points
        /// from.
        /// </param>
        /// <param name="viewNodeId">The deterministic NodeId of the View Node.</param>
        /// <param name="plan">The plan describing the View's membership.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="plan"/> is <c>null</c>.
        /// </exception>
        public WotViewProjectionRequest(
            string closureKey,
            string resourceXid,
            NodeId resourceNodeId,
            NodeId viewNodeId,
            WotViewProjectionPlan plan)
        {
            ClosureKey = closureKey ?? string.Empty;
            ResourceXid = resourceXid ?? string.Empty;
            ResourceNodeId = resourceNodeId.IsNull ? NodeId.Null : resourceNodeId;
            ViewNodeId = viewNodeId.IsNull ? NodeId.Null : viewNodeId;
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        /// <summary>
        /// Gets the stable closure key the document belongs to.
        /// </summary>
        public string ClosureKey { get; }

        /// <summary>
        /// Gets the document resource's registry Xid.
        /// </summary>
        public string ResourceXid { get; }

        /// <summary>
        /// Gets the document resource Node the <c>HasWoTProjection</c> reference
        /// points from (navigable back as <c>WoTProjectionOf</c>).
        /// </summary>
        public NodeId ResourceNodeId { get; }

        /// <summary>
        /// Gets the deterministic NodeId of the View Node to create or update.
        /// </summary>
        public NodeId ViewNodeId { get; }

        /// <summary>
        /// Gets the plan describing the View's membership.
        /// </summary>
        public WotViewProjectionPlan Plan { get; }
    }

    /// <summary>
    /// An opaque handle to a materialized projection-document View. It records
    /// the View NodeId used as the resource's <c>RootNodeId</c>, the count of
    /// Nodes the materializer created (the View plus organizational Objects), and
    /// a non-fatal message (for example the omission notes for out-of-address-space
    /// sources).
    /// </summary>
    public sealed class WotViewProjectionHandle
    {
        /// <summary>
        /// Initializes a new handle.
        /// </summary>
        /// <param name="resourceXid">The document resource's registry Xid.</param>
        /// <param name="viewNodeId">The View Node's NodeId.</param>
        /// <param name="materializedNodeCount">
        /// The count of Nodes the materializer created.
        /// </param>
        /// <param name="message">A non-fatal message, or the empty string.</param>
        /// <param name="omissions">
        /// The members the materializer could not organize, each with the reason,
        /// or the empty array. These are discovered while applying the plan — a
        /// selected affordance can resolve to a NodeId that no NodeManager owns —
        /// so they are distinct from the omissions the builder already recorded.
        /// </param>
        public WotViewProjectionHandle(
            string resourceXid,
            NodeId viewNodeId,
            int materializedNodeCount,
            string message = "",
            ArrayOf<string> omissions = default)
        {
            ResourceXid = resourceXid ?? string.Empty;
            ViewNodeId = viewNodeId.IsNull ? NodeId.Null : viewNodeId;
            MaterializedNodeCount = materializedNodeCount;
            Message = message ?? string.Empty;
            Omissions = omissions.IsNull ? ArrayOf<string>.Empty : omissions;
        }

        /// <summary>
        /// Gets the document resource's registry Xid.
        /// </summary>
        public string ResourceXid { get; }

        /// <summary>
        /// Gets the View Node's NodeId, recorded as the resource's
        /// <c>RootNodeId</c>.
        /// </summary>
        public NodeId ViewNodeId { get; }

        /// <summary>
        /// Gets the count of Nodes the materializer created: the View plus every
        /// organizational Object.
        /// </summary>
        public int MaterializedNodeCount { get; }

        /// <summary>
        /// Gets a non-fatal message describing the materialization, such as the
        /// omission notes for sources that are not in this address space.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the members the materializer could not organize, each with its
        /// reason. A selected affordance can resolve to a NodeId that no
        /// NodeManager owns; organizing it would leave the View with a reference
        /// a client can never follow, so it is dropped and named here.
        /// </summary>
        public ArrayOf<string> Omissions { get; }
    }

    /// <summary>
    /// The seam between the materialization coordinator and the address space for
    /// projection-document Views. The coordinator hands the seam a
    /// <see cref="WotViewProjectionRequest"/>; an implementation creates or
    /// updates the <c>View</c> Node, makes it <c>Organizes</c> the already
    /// materialized member Nodes, grows the organizational Objects, and wires the
    /// <c>HasWoTProjection</c> reference. The production implementation mutates
    /// the live server; a test double records the requests without a running
    /// server.
    /// </summary>
    public interface IWotViewProjectionHost
    {
        /// <summary>
        /// Creates or updates the View for a projection document and returns a
        /// handle to it.
        /// </summary>
        /// <param name="request">The materialization request.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The handle to the materialized View.</returns>
        ValueTask<WotViewProjectionHandle> ApplyAsync(
            WotViewProjectionRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a materialized View.
        /// </summary>
        /// <param name="handle">The handle to remove.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        ValueTask RemoveAsync(
            WotViewProjectionHandle handle,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An in-memory <see cref="IWotViewProjectionHost"/> that records the applied
    /// requests and echoes the deterministic View NodeId and node count back to
    /// the coordinator without touching a live server. It is the default seam
    /// when no live host is configured, and is directly usable as a test double.
    /// </summary>
    public sealed class InMemoryWotViewProjectionHost : IWotViewProjectionHost
    {
        /// <summary>
        /// Gets a snapshot of the currently applied requests, keyed by resource
        /// Xid.
        /// </summary>
        public IReadOnlyList<WotViewProjectionRequest> Applied
        {
            get
            {
                lock (m_lock)
                {
                    var applied = new List<WotViewProjectionRequest>(m_applied.Count);
                    foreach (AppliedView view in m_applied.Values)
                    {
                        applied.Add(view.Request);
                    }
                    return applied;
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<WotViewProjectionHandle> ApplyAsync(
            WotViewProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            var handle = new WotViewProjectionHandle(
                request.ResourceXid,
                request.ViewNodeId,
                request.Plan.MaterializedNodeCount,
                JoinOmissions(request.Plan.Omissions));
            lock (m_lock)
            {
                m_applied[request.ResourceXid] = new AppliedView(request, handle);
            }
            return new ValueTask<WotViewProjectionHandle>(handle);
        }

        /// <inheritdoc/>
        public ValueTask RemoveAsync(
            WotViewProjectionHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (handle is not null)
            {
                lock (m_lock)
                {
                    // A refresh applies the replacement before retiring the handle
                    // it supersedes, and both carry the same ResourceXid. Removing
                    // by Xid alone would therefore delete the View that was just
                    // applied. Only the handle this entry was issued for may
                    // retire it.
                    if (m_applied.TryGetValue(handle.ResourceXid, out AppliedView view) &&
                        ReferenceEquals(view.Handle, handle))
                    {
                        m_applied.Remove(handle.ResourceXid);
                    }
                }
            }
            return default;
        }

        private readonly record struct AppliedView(
            WotViewProjectionRequest Request,
            WotViewProjectionHandle Handle);

        private static string JoinOmissions(ArrayOf<string> omissions)
        {
            if (omissions.Count == 0)
            {
                return string.Empty;
            }
            var parts = new string[omissions.Count];
            for (int i = 0; i < omissions.Count; i++)
            {
                parts[i] = omissions[i];
            }
            return string.Join(" ", parts);
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, AppliedView> m_applied =
            new(StringComparer.Ordinal);
    }
}
