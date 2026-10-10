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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Optional graph-wide View preparation on the same owner as the live View host.
    /// It contributes to the coordinator's publication; it does not own a separate decision.
    /// </summary>
    public interface IWotPreparedViewProjectionHost : IWotViewProjectionHost
    {
        /// <summary>
        /// Gets whether the host can retain private View candidates and participate in one lifecycle batch.
        /// </summary>
        bool SupportsPreparedPublication { get; }

        /// <summary>
        /// Plans the complete remaining View graph from a captured committed image and requested changes.
        /// Shared children, role-keyed wrappers and every affected ancestor belong to the same candidate.
        /// Preparation must not mutate the currently live View image.
        /// </summary>
        ValueTask<IWotPreparedViewPublication> PrepareAsync(
            ArrayOf<WotViewProjectionRequest> updates,
            ArrayOf<WotViewProjectionHandle> removals,
            WotCommittedPublicationState expectedPublication,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Restores a committed canonical graph through the existing prepared publication owner.
    /// </summary>
    public interface IWotRecoverableViewProjectionHost : IWotPreparedViewProjectionHost
    {
        /// <summary>
        /// Prepares the exact durable graph without allocating new identities or advancing its tokens.
        /// Existing handles, when supplied, must belong to this host's current runtime image.
        /// </summary>
        ValueTask<IWotPreparedViewPublication> PrepareRecoveryAsync(
            WotCommittedPublicationState committedPublication,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns a graph-wide private View participant until C1 publishes or aborts its aggregate batch.
    /// </summary>
    public interface IWotPreparedViewPublication : IAsyncDisposable
    {
        /// <summary>
        /// Gets the ordered lifecycle changes contributed to the aggregate batch.
        /// </summary>
        ArrayOf<NodeManagerBatchChange> Changes { get; }

        /// <summary>
        /// Binds the corresponding addition/replacement registrations after aggregate preparation.
        /// Returns a complete candidate graph and all affected Resources, not only the selected parents.
        /// No live mutation or durable publication occurs here.
        /// </summary>
        WotPreparedViewGraphState BindPreparedRegistrations(ArrayOf<NodeManagerRegistration> registrations);

        /// <summary>
        /// Acknowledges the coordinator's committed publication through non-failing, non-I/O bookkeeping.
        /// It cannot allocate generations, change live Nodes, or make another persistence decision.
        /// Disposal must not undo the acknowledged publication.
        /// </summary>
        void OnPublished(WotCommittedPublicationState publication);
    }

    /// <summary>
    /// Supplies immutable membership metadata from a prepared canonical View NodeManager generation.
    /// Implementations retain this image while captured operations drain after retirement.
    /// </summary>
    public interface IWotCanonicalViewReadImage
    {
        /// <summary>
        /// Gets the full membership digest of an active logical projection Resource in this image.
        /// The lookup must not consult a newer global registry or mutate publication state.
        /// </summary>
        /// <param name="resourceNodeId">The local NodeId of the logical Resource.</param>
        /// <param name="digest">The full membership digest when the Resource is active in this image.</param>
        /// <returns>Whether this image contains an active canonical projection for the Resource.</returns>
        bool TryGetMembershipDigest(NodeId resourceNodeId, out ByteString digest);
    }

    /// <summary>
    /// The immutable runtime image of one authoritative materialization decision.
    /// Constructing this value does not publish it; only the coordinator advances the active image.
    /// </summary>
    public sealed class WotCommittedPublicationState
    {
        /// <summary>
        /// Initializes an immutable committed image.
        /// </summary>
        public WotCommittedPublicationState(
            WotRegistrySnapshot registrySnapshot,
            ArrayOf<WotViewProjectionHandle> views = default,
            ArrayOf<WotBindingPlan> activeBindingPlans = default)
        {
            RegistrySnapshot = registrySnapshot ?? throw new ArgumentNullException(nameof(registrySnapshot));
            Views = views.IsNull ? ArrayOf<WotViewProjectionHandle>.Empty : [.. views];
            m_activeBindingPlans = CopyBindingPlans(activeBindingPlans);
        }

        /// <summary>
        /// Gets the registry snapshot committed with this runtime image.
        /// </summary>
        public WotRegistrySnapshot RegistrySnapshot { get; }

        /// <summary>
        /// Gets the materialization generation from the authoritative registry snapshot.
        /// </summary>
        public uint RefreshGeneration => RegistrySnapshot.RefreshGeneration;

        /// <summary>
        /// Gets the complete current View image, excluding retired-only owners.
        /// </summary>
        public ArrayOf<WotViewProjectionHandle> Views { get; }

        /// <summary>
        /// Gets the actual plans of current committed execution owners, not registered capabilities.
        /// Mutable capability descriptors are returned as detached snapshots.
        /// </summary>
        public ArrayOf<WotBindingPlan> ActiveBindingPlans => CopyBindingPlans(m_activeBindingPlans);

        private static ArrayOf<WotBindingPlan> CopyBindingPlans(ArrayOf<WotBindingPlan> plans)
        {
            if (plans.IsNull)
            {
                return ArrayOf<WotBindingPlan>.Empty;
            }
            return plans.ConvertAll(plan =>
            {
                _ = plan ?? throw new ArgumentException("A binding plan cannot be null.", nameof(plans));
                return new WotBindingPlan(
                    plan.ResourceXid,
                    [.. plan.Capabilities.Select(capability => CoreUtils.Clone(capability) ??
                        throw new ArgumentException("A binding capability cannot be null.", nameof(plans)))],
                    plan.CompiledForms,
                    plan.UnsupportedForms,
                    plan.Diagnostics)
                    .WithProjectedAffordances(plan.ProjectedAffordances)
                    .WithDeclarationContext(plan.IsDeclarationContext);
            });
        }

        private readonly ArrayOf<WotBindingPlan> m_activeBindingPlans;
    }

    /// <summary>
    /// A complete private View graph and its resource-level publication footprint.
    /// Canonical graph validation and serialization belong to the View planner.
    /// </summary>
    public sealed class WotPreparedViewGraphState
    {
        /// <summary>
        /// Initializes a private graph result. Empty graph bytes explicitly clear the graph;
        /// Null is not a complete candidate.
        /// </summary>
        public WotPreparedViewGraphState(
            ByteString canonicalViewGraphState,
            ArrayOf<WotViewProjectionHandle> views,
            ArrayOf<string> affectedResourceXids)
            : this(canonicalViewGraphState, views, affectedResourceXids, default)
        {
        }

        /// <summary>
        /// Initializes a private graph with the namespace mapping used by its local NodeIds.
        /// </summary>
        public WotPreparedViewGraphState(
            ByteString canonicalViewGraphState,
            ArrayOf<WotViewProjectionHandle> views,
            ArrayOf<string> affectedResourceXids,
            ArrayOf<string> capturedNamespaceUris)
        {
            if (canonicalViewGraphState.IsNull)
            {
                throw new ArgumentException("A complete graph image is required.", nameof(canonicalViewGraphState));
            }
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (string xid in affectedResourceXids)
            {
                if (string.IsNullOrWhiteSpace(xid) || !identities.Add(xid))
                {
                    throw new ArgumentException(
                        "Affected Resources must have unique, non-empty Xids.", nameof(affectedResourceXids));
                }
            }
            CanonicalViewGraphState = ByteString.From(canonicalViewGraphState.Span.ToArray());
            Views = views.IsNull ? ArrayOf<WotViewProjectionHandle>.Empty : [.. views];
            AffectedResourceXids = affectedResourceXids.IsNull ? ArrayOf<string>.Empty : [.. affectedResourceXids];
            CapturedNamespaceUris = capturedNamespaceUris.IsNull ? ArrayOf<string>.Empty : [.. capturedNamespaceUris];
        }

        /// <summary>
        /// Gets the complete portable canonical graph candidate.
        /// </summary>
        public ByteString CanonicalViewGraphState { get; }

        /// <summary>
        /// Gets the complete resulting View handle image, including unchanged shared Views.
        /// </summary>
        public ArrayOf<WotViewProjectionHandle> Views { get; }

        /// <summary>
        /// Gets every affected Resource, including changed ancestors and retired Views.
        /// </summary>
        public ArrayOf<string> AffectedResourceXids { get; }

        /// <summary>
        /// Gets the private mapping used by the candidate's local NodeIds.
        /// These namespaces need not have been allocated in the serving image.
        /// </summary>
        public ArrayOf<string> CapturedNamespaceUris { get; }
    }
}
