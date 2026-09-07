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

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// The materialization state of a registry resource, independent of the
    /// companion specification that stores it.
    /// </summary>
    /// <remarks>
    /// A companion specification that publishes its own load-state enumeration
    /// maps onto this one at the node manager boundary. The order of the members
    /// is the order a resource passes through them.
    /// </remarks>
    public enum XRegistryLoadState
    {
        /// <summary>
        /// The document is stored but has no active projection.
        /// </summary>
        Unloaded,

        /// <summary>
        /// A replacement generation is being prepared.
        /// </summary>
        Loading,

        /// <summary>
        /// The materialized nodes are serving client requests.
        /// </summary>
        Active,

        /// <summary>
        /// A newer generation has been committed over this one.
        /// </summary>
        Superseded,

        /// <summary>
        /// The superseded generation is draining retained work.
        /// </summary>
        Retiring,

        /// <summary>
        /// The generation has been retired.
        /// </summary>
        Retired,

        /// <summary>
        /// The stored document failed validation or projection.
        /// </summary>
        Failed
    }

    /// <summary>
    /// The per-resource outcome of one refresh.
    /// </summary>
    public enum XRegistryRefreshOutcome
    {
        /// <summary>
        /// The resource projected without a diagnostic.
        /// </summary>
        Success,

        /// <summary>
        /// The resource projected but the generation is degraded.
        /// </summary>
        Warning,

        /// <summary>
        /// Nothing changed, so no projection work was performed.
        /// </summary>
        Unchanged,

        /// <summary>
        /// The resource was not considered by this refresh.
        /// </summary>
        Skipped,

        /// <summary>
        /// The refresh was refused before it did any work, because the caller's
        /// expected generation no longer matched.
        /// </summary>
        Rejected,

        /// <summary>
        /// The resource failed to validate or to project.
        /// </summary>
        Failed
    }

    /// <summary>
    /// The pipeline phase a refresh reached for one resource.
    /// </summary>
    /// <remarks>
    /// The phase is what makes a failure actionable: it separates "the document
    /// could not be read" from "the document is invalid" from "the address space
    /// refused the projection", which need different fixes.
    /// </remarks>
    public enum XRegistryRefreshPhase
    {
        /// <summary>
        /// Selecting the resources the request targets.
        /// </summary>
        Selection,

        /// <summary>
        /// Reading the stored document bytes.
        /// </summary>
        Fetch,

        /// <summary>
        /// Resolving the dependency closure the resource belongs to.
        /// </summary>
        DependencyResolution,

        /// <summary>
        /// Validating the document against its format.
        /// </summary>
        Validation,

        /// <summary>
        /// Converting the document into a projectable representation.
        /// </summary>
        Conversion,

        /// <summary>
        /// Building the address-space projection.
        /// </summary>
        Projection,

        /// <summary>
        /// Preparing or activating protocol bindings.
        /// </summary>
        Binding,

        /// <summary>
        /// Activating the prepared generation.
        /// </summary>
        Activation,

        /// <summary>
        /// Retiring a superseded generation.
        /// </summary>
        Retirement
    }

    /// <summary>
    /// Selects how far a failure is allowed to propagate within one refresh.
    /// </summary>
    public enum XRegistryRefreshAtomicity
    {
        /// <summary>
        /// Each dependency closure commits independently; a failed closure
        /// retains its previously active generation and leaves the others alone.
        /// </summary>
        PerClosure,

        /// <summary>
        /// Any failure abandons the whole refresh.
        /// </summary>
        All
    }

    /// <summary>
    /// Selects the resources a refresh considers. An empty selector list selects
    /// every enabled resource.
    /// </summary>
    /// <remarks>
    /// The members are combined with AND, and an unset member matches anything,
    /// so a selector carrying only a group id selects that whole group.
    /// </remarks>
    public sealed class XRegistryResourceSelector
    {
        /// <summary>
        /// Gets or sets the exact xid to select.
        /// </summary>
        public string Xid { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the group id to select.
        /// </summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource id to select.
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// A request to refresh (re-project) a registry into the address space.
    /// </summary>
    public sealed class XRegistryRefreshRequest
    {
        /// <summary>
        /// Gets or sets the resource selectors; empty selects every resource.
        /// </summary>
        public ArrayOf<XRegistryResourceSelector> Selection { get; set; } = [];

        /// <summary>
        /// Gets or sets whether unchanged resources are rebuilt anyway.
        /// </summary>
        public bool Force { get; set; }

        /// <summary>
        /// Gets or sets whether the refresh only reports what it would do.
        /// </summary>
        /// <remarks>
        /// A dry run commits nothing and does not advance the generation, so the
        /// reported generation is zero.
        /// </remarks>
        public bool DryRun { get; set; }

        /// <summary>
        /// Gets or sets how far a failure propagates.
        /// </summary>
        public XRegistryRefreshAtomicity Atomicity { get; set; } =
            XRegistryRefreshAtomicity.PerClosure;

        /// <summary>
        /// Gets or sets the generation the caller believes is current.
        /// </summary>
        /// <remarks>
        /// When non-zero and it does not match, the refresh is rejected without
        /// doing any work, which is what lets two callers refresh concurrently
        /// without silently overwriting each other.
        /// </remarks>
        public uint ExpectedGeneration { get; set; }

        /// <summary>
        /// Gets or sets an opaque request id echoed back in the summary.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;
    }

    /// <summary>
    /// The outcome of one refresh for one resource.
    /// </summary>
    public sealed class XRegistryRefreshItemResult
    {
        /// <summary>
        /// Gets or sets the resource xid.
        /// </summary>
        public string Xid { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the owning group id.
        /// </summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource id.
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the version that was considered.
        /// </summary>
        public string VersionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the domain document kind, as the domain spells it.
        /// </summary>
        /// <remarks>
        /// The engine never interprets this; it carries it so a node manager can
        /// map it back onto the companion specification's own enumeration.
        /// </remarks>
        public int DocumentKind { get; set; }

        /// <summary>
        /// Gets or sets the outcome.
        /// </summary>
        public XRegistryRefreshOutcome Outcome { get; set; }

        /// <summary>
        /// Gets or sets the phase the refresh reached.
        /// </summary>
        public XRegistryRefreshPhase Phase { get; set; }

        /// <summary>
        /// Gets or sets the resulting load state.
        /// </summary>
        public XRegistryLoadState LoadState { get; set; }

        /// <summary>
        /// Gets or sets the generation this result belongs to.
        /// </summary>
        public uint Generation { get; set; }

        /// <summary>
        /// Gets or sets the number of nodes the resource materialized.
        /// </summary>
        public uint MaterializedNodeCount { get; set; }

        /// <summary>
        /// Gets or sets the root node of the materialized projection.
        /// </summary>
        public NodeId RootNodeId { get; set; } = NodeId.Null;

        /// <summary>
        /// Gets or sets the content digest that produced this result.
        /// </summary>
        public ByteString ContentDigest { get; set; } = ByteString.Empty;

        /// <summary>
        /// Gets or sets the human-readable message, if any.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// The aggregate summary of one refresh.
    /// </summary>
    public sealed class XRegistryRefreshSummary
    {
        /// <summary>
        /// Gets or sets the echoed request id.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the committed generation, or zero for a dry run.
        /// </summary>
        public uint Generation { get; set; }

        /// <summary>
        /// Gets or sets the overall outcome.
        /// </summary>
        public XRegistryRefreshOutcome Outcome { get; set; }

        /// <summary>
        /// Gets or sets the atomicity the refresh ran under.
        /// </summary>
        public XRegistryRefreshAtomicity Atomicity { get; set; }

        /// <summary>
        /// Gets or sets when the refresh started.
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets when the refresh finished.
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the number of reported results.
        /// </summary>
        public uint Total { get; set; }

        /// <summary>
        /// Gets or sets the number of resources that projected.
        /// </summary>
        public uint Succeeded { get; set; }

        /// <summary>
        /// Gets or sets the number of unchanged resources.
        /// </summary>
        public uint Unchanged { get; set; }

        /// <summary>
        /// Gets or sets the number of failed resources.
        /// </summary>
        public uint Failed { get; set; }

        /// <summary>
        /// Gets or sets the number of skipped resources.
        /// </summary>
        public uint Skipped { get; set; }

        /// <summary>
        /// Gets or sets the number of retired projections.
        /// </summary>
        public uint Retired { get; set; }
    }

    /// <summary>
    /// The detailed result of one refresh.
    /// </summary>
    public sealed class XRegistryRefreshResult
    {
        /// <summary>
        /// Initializes a refresh result.
        /// </summary>
        /// <param name="summary">The aggregate summary.</param>
        /// <param name="results">The per-resource results.</param>
        /// <param name="newGeneration">The committed generation, zero for a dry run.</param>
        /// <exception cref="ArgumentNullException"><paramref name="summary"/> is <c>null</c>.</exception>
        public XRegistryRefreshResult(
            XRegistryRefreshSummary summary,
            ArrayOf<XRegistryRefreshItemResult> results,
            uint newGeneration)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Results = results;
            NewGeneration = newGeneration;
        }

        /// <summary>
        /// Gets the aggregate summary.
        /// </summary>
        public XRegistryRefreshSummary Summary { get; }

        /// <summary>
        /// Gets the per-resource results.
        /// </summary>
        public ArrayOf<XRegistryRefreshItemResult> Results { get; }

        /// <summary>
        /// Gets the committed generation, or zero for a dry run.
        /// </summary>
        public uint NewGeneration { get; }
    }

    /// <summary>
    /// The neutral description of one registry resource considered by a refresh.
    /// </summary>
    /// <remarks>
    /// A domain projects its own resource model onto this type once, at the
    /// boundary of <see cref="IXRegistryRefreshStrategy"/>. Everything the engine
    /// does - selection, closure grouping, unchanged detection, result reporting -
    /// is expressed in terms of these members, which is what keeps the engine free
    /// of any companion specification's vocabulary.
    /// </remarks>
    public sealed class XRegistryRefreshMember
    {
        /// <summary>
        /// Initializes a refresh member.
        /// </summary>
        /// <param name="xid">The stable resource xid.</param>
        /// <exception cref="ArgumentException"><paramref name="xid"/> is null or empty.</exception>
        public XRegistryRefreshMember(string xid)
        {
            if (string.IsNullOrEmpty(xid))
            {
                throw new ArgumentException("The resource xid is required.", nameof(xid));
            }
            Xid = xid;
        }

        /// <summary>
        /// Gets the stable resource xid, which identifies the member everywhere.
        /// </summary>
        public string Xid { get; }

        /// <summary>
        /// Gets the owning group id.
        /// </summary>
        public string GroupId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the resource id.
        /// </summary>
        public string ResourceId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the version the refresh should materialize.
        /// </summary>
        public string VersionId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the version that is currently active, when one is.
        /// </summary>
        /// <remarks>
        /// An unchanged member reports this rather than the desired version,
        /// because nothing was re-projected and the active generation still
        /// serves what it always served.
        /// </remarks>
        public string ActiveVersionId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the number of nodes the member currently has materialized.
        /// </summary>
        public uint MaterializedNodeCount { get; init; }

        /// <summary>
        /// Gets the domain document kind, as the domain spells it.
        /// </summary>
        public int DocumentKind { get; init; }

        /// <summary>
        /// Gets the digest of the content behind <see cref="VersionId"/>.
        /// </summary>
        /// <remarks>
        /// The engine hashes the digests of a closure's members to decide whether
        /// the closure changed, so a domain that leaves this empty forfeits
        /// unchanged detection and re-projects on every refresh.
        /// </remarks>
        public ByteString ContentDigest { get; init; } = ByteString.Empty;

        /// <summary>
        /// Gets the member's current load state.
        /// </summary>
        public XRegistryLoadState LoadState { get; init; }
    }

    /// <summary>
    /// The kind of event the refresh engine raises.
    /// </summary>
    public enum XRegistryRefreshEventKind
    {
        /// <summary>
        /// A refresh completed and its summary is available.
        /// </summary>
        RefreshCompleted,

        /// <summary>
        /// A resource projection changed state.
        /// </summary>
        Resource,

        /// <summary>
        /// A resource failed format or compatibility validation.
        /// </summary>
        ValidationFailure,

        /// <summary>
        /// A resource failed to load or project.
        /// </summary>
        LoadFailure,

        /// <summary>
        /// A protocol binding failed.
        /// </summary>
        BindingFailure
    }

    /// <summary>
    /// The payload the refresh engine raises for each material event, which a
    /// node manager maps onto its companion specification's event types.
    /// </summary>
    public sealed class XRegistryRefreshEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes event arguments.
        /// </summary>
        /// <param name="kind">The event kind.</param>
        public XRegistryRefreshEventArgs(XRegistryRefreshEventKind kind)
        {
            Kind = kind;
        }

        /// <summary>
        /// Gets the event kind.
        /// </summary>
        public XRegistryRefreshEventKind Kind { get; }

        /// <summary>
        /// Gets the affected resource xid.
        /// </summary>
        public string Xid { get; init; } = string.Empty;

        /// <summary>
        /// Gets the owning group id.
        /// </summary>
        public string GroupId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the resource id.
        /// </summary>
        public string ResourceId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the version id.
        /// </summary>
        public string VersionId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the domain document kind, as the domain spells it.
        /// </summary>
        public int DocumentKind { get; init; }

        /// <summary>
        /// Gets the refresh generation.
        /// </summary>
        public uint Generation { get; init; }

        /// <summary>
        /// Gets the phase reached.
        /// </summary>
        public XRegistryRefreshPhase Phase { get; init; }

        /// <summary>
        /// Gets the outcome.
        /// </summary>
        public XRegistryRefreshOutcome Outcome { get; init; }

        /// <summary>
        /// Gets the resulting load state.
        /// </summary>
        public XRegistryLoadState LoadState { get; init; }

        /// <summary>
        /// Gets the failing node id, if any.
        /// </summary>
        public NodeId FailedNodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// Gets the binding URI, if any.
        /// </summary>
        public string BindingUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets the human-readable reason.
        /// </summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>
        /// Gets the refresh summary, set only for
        /// <see cref="XRegistryRefreshEventKind.RefreshCompleted"/>.
        /// </summary>
        public XRegistryRefreshSummary? Summary { get; init; }

        /// <summary>
        /// Gets the request id, set only for
        /// <see cref="XRegistryRefreshEventKind.RefreshCompleted"/>.
        /// </summary>
        public string RequestId { get; init; } = string.Empty;
    }
}
