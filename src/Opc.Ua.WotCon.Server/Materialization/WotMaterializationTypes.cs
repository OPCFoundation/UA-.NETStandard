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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// A request to refresh (re-project) the registry into the AddressSpace.
    /// Mirrors the generated <c>WoTRegistryType.Refresh</c> Method signature.
    /// </summary>
    public sealed class WotRefreshRequest
    {
        /// <summary>
        /// Gets or sets the resource selectors; empty selects all resources.
        /// </summary>
        public ImmutableArray<WoTResourceSelectorDataType> Selection { get; set; }
            = [];

        /// <summary>
        /// Gets or sets the refresh options.
        /// </summary>
        public WoTRefreshOptionsDataType Options { get; set; } = new WoTRefreshOptionsDataType();

        /// <summary>
        /// Gets or sets the caller's expected refresh generation. When non-zero
        /// and it does not match the current generation, the refresh is rejected.
        /// </summary>
        public uint ExpectedGeneration { get; set; }

        /// <summary>
        /// Gets or sets an opaque request id echoed in the summary.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the executing request's operation context, or <c>null</c> for an
        /// external or background caller that does not run inside a server request.
        /// </summary>
        public IOperationContext? CallerContext { get; set; }
    }

    /// <summary>
    /// The detailed result of a refresh, matching the generated
    /// <c>WoTRegistryType.Refresh</c> output arguments.
    /// </summary>
    public sealed class WotRefreshResult
    {
        /// <summary>
        /// Captures the summary, per-resource outcomes, and generation produced by a refresh.
        /// </summary>
        /// <param name="summary">The aggregate refresh summary.</param>
        /// <param name="results">The per-resource outcomes returned by the refresh.</param>
        /// <param name="newGeneration">The registry generation resulting from the refresh.</param>
        internal WotRefreshResult(
            WoTRefreshSummaryDataType summary,
            ImmutableArray<WoTResourceLoadResultDataType> results,
            uint newGeneration)
        {
            Summary = summary;
            Results = results;
            NewGeneration = newGeneration;
        }

        /// <summary>
        /// Gets the overall refresh summary.
        /// </summary>
        public WoTRefreshSummaryDataType Summary { get; }

        /// <summary>
        /// Gets the per-resource results.
        /// </summary>
        public ImmutableArray<WoTResourceLoadResultDataType> Results { get; }

        /// <summary>
        /// Gets the committed refresh generation.
        /// </summary>
        public uint NewGeneration { get; }
    }

    /// <summary>
    /// What to delete from the registry, and under which WoT Connectivity
    /// delete policy.
    /// </summary>
    public sealed class WotDeleteRequest
    {
        /// <summary>
        /// Gets or sets the owning group.
        /// </summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource to delete.
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the delete policy. The default, <c>Reject</c>, refuses
        /// while anything still depends on the document.
        /// </summary>
        public WoTDeletePolicyEnum Policy { get; set; } = WoTDeletePolicyEnum.Reject;

        /// <summary>
        /// Gets or sets the resource epoch the caller last observed, or
        /// <c>null</c> to delete regardless of concurrent changes.
        /// </summary>
        public long? ExpectedEpoch { get; set; }

        /// <summary>
        /// Gets or sets an opaque request id echoed in the summary.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the executing request's operation context, or <c>null</c> for an
        /// external or background caller that does not run inside a server request.
        /// </summary>
        public IOperationContext? CallerContext { get; set; }
    }

    /// <summary>
    /// The result of a policy-driven delete: what the registry did, and what
    /// reconciling the AddressSpace afterwards produced.
    /// </summary>
    public sealed class WotDeleteOutcome
    {
        /// <summary>
        /// Captures the deletion result together with refresh outcomes and the resulting generation.
        /// </summary>
        /// <param name="delete">The registry deletion result.</param>
        /// <param name="summary">The aggregate refresh summary.</param>
        /// <param name="results">The per-resource outcomes returned by the refresh.</param>
        /// <param name="generation">The generation assigned to the committed publication.</param>
        internal WotDeleteOutcome(
            WotDeleteResult delete,
            WoTRefreshSummaryDataType summary,
            ImmutableArray<WoTResourceLoadResultDataType> results,
            uint generation)
        {
            Delete = delete;
            Summary = summary;
            Results = results.IsDefault ? [] : results;
            Generation = generation;
        }

        /// <summary>
        /// Gets what the delete policy did to stored state.
        /// </summary>
        public WotDeleteResult Delete { get; }

        /// <summary>
        /// Gets the summary of the reconciliation that followed. A rejected
        /// delete reconciles nothing, so its counts are all zero.
        /// </summary>
        public WoTRefreshSummaryDataType Summary { get; }

        /// <summary>
        /// Gets the per-resource results of the reconciliation.
        /// </summary>
        public ImmutableArray<WoTResourceLoadResultDataType> Results { get; }

        /// <summary>
        /// Gets the refresh generation after the operation.
        /// </summary>
        public uint Generation { get; }
    }

    /// <summary>
    /// The kind of materialization event emitted by the coordinator.
    /// </summary>
    public enum WotMaterializationEventKind
    {
        /// <summary>
        /// A refresh completed.
        /// </summary>
        RefreshCompleted,

        /// <summary>
        /// A resource projection changed state.
        /// </summary>
        Resource,

        /// <summary>
        /// A resource failed format/compatibility validation.
        /// </summary>
        ValidationFailure,

        /// <summary>
        /// A resource failed to load/project.
        /// </summary>
        LoadFailure,

        /// <summary>
        /// A binding failed.
        /// </summary>
        BindingFailure
    }

    /// <summary>
    /// The payload the coordinator raises for each material event. The NodeManager
    /// maps failures and refresh completion to their concrete generated EventTypes.
    /// Resource notifications remain available to application subscribers without
    /// instantiating the abstract <c>WoTResourceEventType</c> on the server.
    /// </summary>
    public sealed class WotMaterializationEventArgs : EventArgs
    {
        /// <summary>
        /// Creates event arguments identifying the materialization event kind.
        /// </summary>
        /// <param name="kind">The materialization event kind to report.</param>
        internal WotMaterializationEventArgs(WotMaterializationEventKind kind)
        {
            Kind = kind;
        }

        /// <summary>
        /// Gets the event kind.
        /// </summary>
        public WotMaterializationEventKind Kind { get; }

        /// <summary>
        /// Gets or sets the affected resource xid.
        /// </summary>
        public string Xid { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource id.
        /// </summary>
        public string ResourceId { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the version id.
        /// </summary>
        public string VersionId { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the document kind.
        /// </summary>
        public WoTDocumentKindEnum DocumentKind { get; init; }

        /// <summary>
        /// Gets or sets the refresh generation.
        /// </summary>
        public uint Generation { get; init; }

        /// <summary>
        /// Gets or sets the phase reached.
        /// </summary>
        public WoTPhaseEnum Phase { get; init; }

        /// <summary>
        /// Gets or sets the outcome.
        /// </summary>
        public WoTOutcomeEnum Outcome { get; init; }

        /// <summary>
        /// Gets or sets the resulting load state.
        /// </summary>
        public WoTLoadStateEnum LoadState { get; init; }

        /// <summary>
        /// Gets or sets the validation outcome, if any.
        /// </summary>
        public WoTValidationOutcomeDataType? Validation { get; init; }

        /// <summary>
        /// Gets or sets the failing node id, if any.
        /// </summary>
        public NodeId FailedNodeId { get; init; }

        /// <summary>
        /// Gets or sets the binding URI, if any.
        /// </summary>
        public string BindingUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets a human-readable reason/message.
        /// </summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the refresh summary (RefreshCompleted only).
        /// </summary>
        public WoTRefreshSummaryDataType? Summary { get; init; }

        /// <summary>
        /// Gets or sets the request id (RefreshCompleted only).
        /// </summary>
        public string RequestId { get; init; } = string.Empty;
    }

    /// <summary>
    /// An <see cref="IWotThingResolver"/> that resolves referenced TD/TM
    /// documents from a registry snapshot, so a Thing Description synthesized by
    /// the converter can pull in the Thing Models it depends on.
    /// </summary>
    internal sealed class SnapshotThingResolver :
        IWotVersionedThingResolver, IWotCapturedDataTypeDefinitions, IWotArchiveContextProvider
    {
        /// <summary>
        /// Initializes a new <c>SnapshotThingResolver</c> instance.
        /// </summary>
        /// <param name="snapshot">The WoT registry snapshot used to initialize the instance.</param>
        /// <param name="contents">The contents used to initialize the instance.</param>
        /// <param name="dataTypeDefinitions">The data type definitions used to initialize the instance.</param>
        /// <param name="archiveContexts">The factory for the captured archive context index, if available.</param>
        public SnapshotThingResolver(
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents,
            ArrayOf<WotDataTypeDefinitionSource> dataTypeDefinitions = default,
            Func<WotNodeSetConverter.ArchiveContextIndex>? archiveContexts = null)
        {
            m_snapshot = snapshot;
            m_contents = contents ?? throw new ArgumentNullException(nameof(contents));
            DataTypeDefinitions = dataTypeDefinitions.IsNull ? [] : dataTypeDefinitions;
            m_archiveContexts = archiveContexts;
        }

        /// <inheritdoc/>
        public UANodeSet? GetArchiveContext(WotDocument document)
        {
            return m_archiveContexts?.Invoke().Find(document);
        }

        /// <summary>
        /// Gets the data type definitions for <c>SnapshotThingResolver</c>.
        /// </summary>
        public ArrayOf<WotDataTypeDefinitionSource> DataTypeDefinitions { get; }

        /// <summary>
        /// Resolves a Thing Description or Thing Model reference from the registry snapshot.
        /// </summary>
        public ValueTask<WotResolverResult> ResolveThingAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            return ResolveThingAsync(new WotReferenceRequest("ref", reference), context, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveThingAsync(
            WotReferenceRequest reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WotResource? resource = WotDependencyGraph.Resolve(m_snapshot, reference.Target);
            if (resource is null)
            {
                return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
            }
            string target = reference.Target.Split('#')[0];
            WotResourceVersion? exact = resource.Versions.FirstOrDefault(candidate =>
                WotDependencyGraph.VersionXid(resource, candidate) == target);
            WotResourceVersion? version = exact ?? resource.DefaultVersion;
            if (exact is null && resource.Kind == WoTDocumentKindEnum.ThingModel &&
                (reference.MinimumVersion is not null ||
                    resource.Versions.All(candidate => WotSemanticVersion.TryParse(candidate.VersionId, out _))))
            {
                WotResourceSnapshot snapshot = WotDependencyGraph.VersionSnapshot(resource);
                WotResolutionResult selected = WotVersionResolver.Resolve([snapshot],
                    reference with { Target = snapshot.WotId }, "urn:opcua:registry-snapshot");
                version = resource.FindVersion(selected.VersionId);
            }
            WotResolverResult result = version is not null &&
                m_contents.TryGetValue(version.DigestHex, out ByteString content)
                ? WotResolverResult.FromBytes(content.Span.ToArray()) : WotResolverResult.NotFound;
            return new ValueTask<WotResolverResult>(result);
        }

        private readonly WotRegistrySnapshot m_snapshot;
        private readonly IReadOnlyDictionary<string, ByteString> m_contents;
        private readonly Func<WotNodeSetConverter.ArchiveContextIndex>? m_archiveContexts;
    }
}
