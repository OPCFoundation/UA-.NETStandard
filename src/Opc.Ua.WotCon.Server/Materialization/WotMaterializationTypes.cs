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
using System.Collections.Immutable;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// A request to refresh (re-project) the registry into the AddressSpace.
    /// Mirrors the generated <c>WoTRegistryType.Refresh</c> Method signature.
    /// </summary>
    public sealed class WotRefreshRequest
    {
        /// <summary>Gets or sets the resource selectors; empty selects all resources.</summary>
        public ImmutableArray<WoTResourceSelectorDataType> Selection { get; set; }
            = ImmutableArray<WoTResourceSelectorDataType>.Empty;

        /// <summary>Gets or sets the refresh options.</summary>
        public WoTRefreshOptionsDataType Options { get; set; } = new WoTRefreshOptionsDataType();

        /// <summary>
        /// Gets or sets the caller's expected registry generation. When non-zero
        /// and it does not match the current generation, the refresh is rejected.
        /// </summary>
        public uint ExpectedGeneration { get; set; }

        /// <summary>Gets or sets an opaque request id echoed in the summary.</summary>
        public string RequestId { get; set; } = string.Empty;
    }

    /// <summary>
    /// The detailed result of a refresh, matching the generated
    /// <c>WoTRegistryType.Refresh</c> output arguments.
    /// </summary>
    public sealed class WotRefreshResult
    {
        internal WotRefreshResult(
            WoTRefreshSummaryDataType summary,
            ImmutableArray<WoTResourceLoadResultDataType> results,
            uint newGeneration)
        {
            Summary = summary;
            Results = results;
            NewGeneration = newGeneration;
        }

        /// <summary>Gets the overall refresh summary.</summary>
        public WoTRefreshSummaryDataType Summary { get; }

        /// <summary>Gets the per-resource results.</summary>
        public ImmutableArray<WoTResourceLoadResultDataType> Results { get; }

        /// <summary>Gets the committed refresh generation.</summary>
        public uint NewGeneration { get; }
    }

    /// <summary>The kind of materialization event emitted by the coordinator.</summary>
    public enum WotMaterializationEventKind
    {
        /// <summary>A refresh completed.</summary>
        RefreshCompleted,

        /// <summary>A resource projection changed state.</summary>
        Resource,

        /// <summary>A resource failed format/compatibility validation.</summary>
        ValidationFailure,

        /// <summary>A resource failed to load/project.</summary>
        LoadFailure,

        /// <summary>A binding failed.</summary>
        BindingFailure
    }

    /// <summary>
    /// The payload the coordinator raises for each material event. The NodeManager
    /// maps it to the generated <c>WoTResourceEventType</c> /
    /// <c>WoTValidationFailureEventType</c> / <c>WoTLoadFailureEventType</c> /
    /// <c>WoTBindingFailureEventType</c> / <c>WoTRefreshCompletedEventType</c>.
    /// </summary>
    public sealed class WotMaterializationEventArgs : EventArgs
    {
        internal WotMaterializationEventArgs(WotMaterializationEventKind kind)
        {
            Kind = kind;
        }

        /// <summary>Gets the event kind.</summary>
        public WotMaterializationEventKind Kind { get; }

        /// <summary>Gets or sets the affected resource xid.</summary>
        public string Xid { get; init; } = string.Empty;

        /// <summary>Gets or sets the resource id.</summary>
        public string ResourceId { get; init; } = string.Empty;

        /// <summary>Gets or sets the version id.</summary>
        public string VersionId { get; init; } = string.Empty;

        /// <summary>Gets or sets the document kind.</summary>
        public WoTDocumentKindEnum DocumentKind { get; init; }

        /// <summary>Gets or sets the refresh generation.</summary>
        public uint Generation { get; init; }

        /// <summary>Gets or sets the phase reached.</summary>
        public WoTPhaseEnum Phase { get; init; }

        /// <summary>Gets or sets the outcome.</summary>
        public WoTOutcomeEnum Outcome { get; init; }

        /// <summary>Gets or sets the resulting load state.</summary>
        public WoTLoadStateEnum LoadState { get; init; }

        /// <summary>Gets or sets the validation outcome, if any.</summary>
        public WoTValidationOutcomeDataType? Validation { get; init; }

        /// <summary>Gets or sets the failing node id, if any.</summary>
        public NodeId? FailedNodeId { get; init; }

        /// <summary>Gets or sets the binding URI, if any.</summary>
        public string BindingUri { get; init; } = string.Empty;

        /// <summary>Gets or sets a human-readable reason/message.</summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>Gets or sets the refresh summary (RefreshCompleted only).</summary>
        public WoTRefreshSummaryDataType? Summary { get; init; }

        /// <summary>Gets or sets the request id (RefreshCompleted only).</summary>
        public string RequestId { get; init; } = string.Empty;
    }

    /// <summary>
    /// An <see cref="IWotThingResolver"/> that resolves referenced TD/TM
    /// documents from a registry snapshot, so a Thing Description synthesized by
    /// the converter can pull in the Thing Models it depends on.
    /// </summary>
    internal sealed class SnapshotThingResolver : IWotThingResolver
    {
        public SnapshotThingResolver(WotRegistrySnapshot snapshot)
        {
            m_snapshot = snapshot;
        }

        public WotResolverResult ResolveThing(string reference, WotResolutionContext context)
        {
            WotResource? resource = WotDependencyGraph.Resolve(m_snapshot, reference);
            WotResourceVersion? version = resource?.DefaultVersion;
            if (version is null)
            {
                return WotResolverResult.NotFound;
            }
            return WotResolverResult.FromBytes(version.Content);
        }

        private readonly WotRegistrySnapshot m_snapshot;
    }
}
