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
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// A pure, side-effect-free description of how one WoT <em>projection
    /// document</em> (WoT Binding Section 12) materializes into a single OPC UA
    /// <c>View</c> Node. It records the already-materialized Nodes the View
    /// <c>Organizes</c>, the organizational Objects grown from its
    /// <c>ua:Organizes</c> links, the affordances that were dropped because
    /// their source is not in this server's address space, and a deterministic
    /// <see cref="ViewVersion"/>. It defines no affordance Node of its own: a
    /// projection selects, it never defines.
    /// </summary>
    /// <remarks>
    /// This is the address-space-mapping ("projection") sense of the word, not
    /// the runtime-NodeSet-closure sense carried by
    /// <see cref="WotProjectionDocument"/> and <see cref="WotProjectionSource"/>.
    /// The plan is produced by <see cref="WotProjectionViewBuilder"/> and applied
    /// to the live address space by an <see cref="IWotViewProjectionHost"/>.
    /// </remarks>
    public sealed class WotViewProjectionPlan
    {
        /// <summary>
        /// Initializes a new projection-document View plan.
        /// </summary>
        /// <param name="scenario">The <c>uav:scenario</c> the view serves.</param>
        /// <param name="documentKind">
        /// Whether the projection document resolves to a Thing Description
        /// (instance) or a Thing Model (type) view.
        /// </param>
        /// <param name="organizedNodeIds">
        /// The already-materialized Node the outermost View directly
        /// <c>Organizes</c>, in resolved order.
        /// </param>
        /// <param name="groups">
        /// The organizational Objects grown from the document's
        /// <c>ua:Organizes</c> links, in authored order.
        /// </param>
        /// <param name="viewVersion">
        /// The deterministic membership version; see <see cref="ViewVersion"/>.
        /// </param>
        /// <param name="omissions">
        /// The human-readable notes describing every selected affordance that
        /// was omitted because its source is not in this address space.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="scenario"/> is <c>null</c>.
        /// </exception>
        public WotViewProjectionPlan(
            string scenario,
            WotDocumentKind documentKind,
            ArrayOf<NodeId> organizedNodeIds,
            ArrayOf<WotOrganizationalGroup> groups,
            uint viewVersion,
            ArrayOf<string> omissions)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            DocumentKind = documentKind;
            OrganizedNodeIds = organizedNodeIds.IsNull ? ArrayOf<NodeId>.Empty : organizedNodeIds;
            Groups = groups.IsNull ? ArrayOf<WotOrganizationalGroup>.Empty : groups;
            ViewVersion = viewVersion;
            Omissions = omissions.IsNull ? ArrayOf<string>.Empty : omissions;
        }

        /// <summary>
        /// Gets the <c>uav:scenario</c> IRI naming the purpose the view serves.
        /// </summary>
        public string Scenario { get; }

        /// <summary>
        /// Gets whether the projection document resolves to a Thing Description
        /// (an instance-level View over Object Nodes) or a Thing Model (a
        /// type-level View over type / declaration Nodes). Both materialize to a
        /// <c>View</c>.
        /// </summary>
        public WotDocumentKind DocumentKind { get; }

        /// <summary>
        /// Gets the already-materialized Nodes the outermost View directly
        /// <c>Organizes</c>, in resolved order. Never contains a Node created by
        /// the projection itself.
        /// </summary>
        public ArrayOf<NodeId> OrganizedNodeIds { get; }

        /// <summary>
        /// Gets the organizational Objects grown from the document's
        /// <c>ua:Organizes</c> links, in authored order. Only the outermost
        /// materialization is a <c>View</c>; every group here is an Object.
        /// </summary>
        public ArrayOf<WotOrganizationalGroup> Groups { get; }

        /// <summary>
        /// Gets the deterministic membership version copied to the View's
        /// standard <c>ViewVersion</c> attribute. It is a stable hash over the
        /// ordered resolved membership (the organized Node set plus every
        /// group's <c>uav:refName</c> and members, depth-first), so it is
        /// unchanged across a refresh that resolves the same membership and
        /// changes whenever the membership changes.
        /// </summary>
        public uint ViewVersion { get; }

        /// <summary>
        /// Gets the human-readable notes describing every selected affordance
        /// omitted because its source is not in this server's address space (for
        /// example a cross-server federation source). Omission is not a failure;
        /// the resource still reaches <c>Active</c>.
        /// </summary>
        public ArrayOf<string> Omissions { get; }

        /// <summary>
        /// Gets the count of Nodes the materializer creates: the outermost View
        /// plus every organizational Object, counted recursively. It never
        /// counts the Nodes the View organizes, because those are materialized
        /// from their own source documents.
        /// </summary>
        public int MaterializedNodeCount => 1 + CountGroups(Groups);

        private static int CountGroups(ArrayOf<WotOrganizationalGroup> groups)
        {
            int count = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                count += 1 + CountGroups(groups[i].Groups);
            }
            return count;
        }
    }

    /// <summary>
    /// An organizational Object grown from a single <c>ua:Organizes</c> link of
    /// a projection document (WoT Binding Section 12.7). The Object
    /// <c>Organizes</c> the already-materialized member Nodes of the organized
    /// sub-projection and any further nested organizational Objects. The
    /// organizing document does not absorb the organized document's affordances:
    /// they stay in the group.
    /// </summary>
    public sealed class WotOrganizationalGroup
    {
        /// <summary>
        /// Initializes a new organizational group.
        /// </summary>
        /// <param name="refName">The <c>uav:refName</c> that names the group.</param>
        /// <param name="organizedNodeIds">
        /// The already-materialized member Nodes this group <c>Organizes</c>.
        /// </param>
        /// <param name="groups">The nested organizational Objects.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="refName"/> is <c>null</c>.
        /// </exception>
        public WotOrganizationalGroup(
            string refName,
            ArrayOf<NodeId> organizedNodeIds,
            ArrayOf<WotOrganizationalGroup> groups)
        {
            RefName = refName ?? throw new ArgumentNullException(nameof(refName));
            OrganizedNodeIds = organizedNodeIds.IsNull ? ArrayOf<NodeId>.Empty : organizedNodeIds;
            Groups = groups.IsNull ? ArrayOf<WotOrganizationalGroup>.Empty : groups;
        }

        /// <summary>
        /// Gets the <c>uav:refName</c> that names this group.
        /// </summary>
        public string RefName { get; }

        /// <summary>
        /// Gets the already-materialized member Nodes this group
        /// <c>Organizes</c>, in resolved order.
        /// </summary>
        public ArrayOf<NodeId> OrganizedNodeIds { get; }

        /// <summary>
        /// Gets the nested organizational Objects, in authored order.
        /// </summary>
        public ArrayOf<WotOrganizationalGroup> Groups { get; }
    }

    /// <summary>
    /// The outcome of building a <see cref="WotViewProjectionPlan"/>: the plan
    /// together with the diagnostics that describe how it was produced. The plan
    /// is <c>null</c> when an error diagnostic was reported (for example an
    /// organizing cycle), in which case the projection document does not
    /// materialize a View.
    /// </summary>
    public sealed class WotViewProjectionResult
    {
        /// <summary>
        /// Initializes a new build result.
        /// </summary>
        /// <param name="plan">The built plan, or <c>null</c> on error.</param>
        /// <param name="diagnostics">The diagnostics produced.</param>
        public WotViewProjectionResult(
            WotViewProjectionPlan? plan,
            IReadOnlyList<WotDiagnostic> diagnostics)
        {
            Plan = plan;
            Diagnostics = (diagnostics ?? []).ToArrayOf();
        }

        /// <summary>
        /// Gets the built plan, or <c>null</c> when an error diagnostic was
        /// reported.
        /// </summary>
        public WotViewProjectionPlan? Plan { get; }

        /// <summary>
        /// Gets the diagnostics produced while building the plan.
        /// </summary>
        public ArrayOf<WotDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets whether an error diagnostic was reported.
        /// </summary>
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Diagnostics.Count; i++)
                {
                    if (Diagnostics[i].Severity == WotDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Gets whether the plan was built without error.
        /// </summary>
        public bool Success => Plan is not null && !HasErrors;
    }
}
