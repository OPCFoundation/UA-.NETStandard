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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Coordinates projecting registry documents into the AddressSpace. It parses
    /// and validates each document with <see cref="Opc.Ua.Wot"/>, builds the TD/TM
    /// dependency closures, converts each closure to one or more NodeSet2
    /// documents and projects them through the <see cref="IWotProjectionHost"/>
    /// (runtime NodeSet Add for first activation, ShadowReload for updates). The
    /// stable registry NodeManager is kept separate. Independent closures commit
    /// independently; a failed or invalid closure retains its previous active
    /// generation. An unchanged closure (same digest, options and binder version)
    /// returns <see cref="WoTOutcomeEnum.Unchanged"/> and emits no model change.
    /// </summary>
    public sealed class WotMaterializationCoordinator : IDisposable
    {
        /// <summary>Initializes a new coordinator.</summary>
        public WotMaterializationCoordinator(
            IWotRegistryService registry,
            IWotProjectionHost projectionHost,
            IWotBinderRegistry? binderRegistry = null,
            WotNodeSetConverterOptions? converterOptions = null,
            IWotDocumentConverter? documentConverter = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_host = projectionHost ?? throw new ArgumentNullException(nameof(projectionHost));
            m_binders = binderRegistry ?? NullWotBinderRegistry.Instance;
            m_converterOptions = converterOptions ?? new WotNodeSetConverterOptions();
            m_converter = documentConverter
                ?? new WotNodeSetDocumentConverter(m_converterOptions);
        }

        /// <summary>Raised for each materialization event (resource / validation / load / refresh).</summary>
        public event EventHandler<WotMaterializationEventArgs>? Event;

        /// <summary>Gets the current refresh generation.</summary>
        public uint Generation => m_generation;

        /// <summary>
        /// Refreshes (re-projects) the registry into the AddressSpace and returns
        /// the detailed result.
        /// </summary>
        public async ValueTask<WotRefreshResult> RefreshAsync(
            WotRefreshRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DateTime start = DateTime.UtcNow;
                WotRegistrySnapshot snapshot = m_registry.Current;

                if (request.ExpectedGeneration != 0 &&
                    request.ExpectedGeneration != (uint)snapshot.Generation)
                {
                    return RejectedResult(request, snapshot, start);
                }

                bool dryRun = request.Options?.DryRun ?? false;
                bool force = request.Options?.Force ?? false;
                bool strict = StrictBindings;
                var selectedXids = ResolveSelection(snapshot, request.Selection);

                var enabled = snapshot.AllResources()
                    .Where(r => r.Enabled && r.DefaultVersion is not null)
                    .ToList();
                ImmutableArray<WotDependencyClosure> closures =
                    WotDependencyGraph.BuildClosures(
                        snapshot, enabled, m_converterOptions.MaxJsonDepth);

                var targetKeys = new HashSet<string>(
                    closures.Select(c => c.Key), StringComparer.Ordinal);

                uint newGeneration = ++m_generation;
                var results = ImmutableArray.CreateBuilder<WoTResourceLoadResultDataType>();
                var projections = new List<WotResourceProjection>();
                int succeeded = 0, unchanged = 0, failed = 0, skipped = 0, retired = 0;

                // Retire tracked closures no longer desired (deleted / disabled /
                // membership changed) after their monitored items drain.
                retired += await ReconcileRetirementsAsync(
                    targetKeys, cancellationToken).ConfigureAwait(false);

                foreach (WotDependencyClosure closure in closures)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool inScope = selectedXids.Count == 0 ||
                        closure.OrderedResources.Any(r => selectedXids.Contains(r.Xid)) ||
                        MembersOf(closure).Any(r => selectedXids.Contains(r.Xid));

                    ClosureOutcome outcome = await ProcessClosureAsync(
                        snapshot, closure, newGeneration, force && inScope,
                        dryRun, strict, cancellationToken).ConfigureAwait(false);

                    foreach (WoTResourceLoadResultDataType result in outcome.Results)
                    {
                        string resultXid = result.Xid ?? string.Empty;
                        if (selectedXids.Count != 0 && !selectedXids.Contains(resultXid))
                        {
                            continue;
                        }
                        results.Add(result);
                        switch (result.Outcome)
                        {
                            case WoTOutcomeEnum.Success:
                            case WoTOutcomeEnum.Warning:
                                succeeded++;
                                break;
                            case WoTOutcomeEnum.Unchanged:
                                unchanged++;
                                break;
                            case WoTOutcomeEnum.Skipped:
                                skipped++;
                                break;
                            default:
                                failed++;
                                break;
                        }
                    }
                    projections.AddRange(outcome.Projections);
                }

                if (!dryRun && projections.Count > 0)
                {
                    await m_registry.ApplyProjectionResultsAsync(
                        projections, cancellationToken).ConfigureAwait(false);
                }

                WoTOutcomeEnum overall = failed > 0
                    ? (succeeded > 0 ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Failed)
                    : (succeeded > 0 ? WoTOutcomeEnum.Success : WoTOutcomeEnum.Unchanged);

                var summary = new WoTRefreshSummaryDataType
                {
                    RequestId = request.RequestId ?? string.Empty,
                    Generation = dryRun ? 0 : newGeneration,
                    Outcome = overall,
                    Atomicity = request.Options?.Atomicity ?? WoTAtomicityEnum.PerClosure,
                    StartTime = start,
                    EndTime = DateTime.UtcNow,
                    Total = (uint)results.Count,
                    Succeeded = (uint)succeeded,
                    Unchanged = (uint)unchanged,
                    Failed = (uint)failed,
                    Skipped = (uint)skipped,
                    Retired = (uint)retired
                };

                RaiseEvent(new WotMaterializationEventArgs(
                    WotMaterializationEventKind.RefreshCompleted)
                {
                    Generation = newGeneration,
                    RequestId = request.RequestId ?? string.Empty,
                    Outcome = overall,
                    Summary = summary
                });

                return new WotRefreshResult(
                    summary, results.ToImmutable(), dryRun ? 0u : newGeneration);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <summary>
        /// Removes all live projections (used during NodeManager shutdown).
        /// </summary>
        public async ValueTask RemoveAllAsync(CancellationToken cancellationToken = default)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (ClosureState state in m_closures.Values)
                {
                    // Deactivate bindings before removing the projection (before
                    // retirement / unload), then release the projection handle.
                    foreach (WotBindingPlan plan in state.BindingPlans)
                    {
                        await m_binders.DeactivateAsync(plan, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    if (state.Handle is not null)
                    {
                        await m_host.RemoveAsync(state.Handle, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                m_closures.Clear();
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <summary>Gets or sets whether unsupported forms fail a strict closure.</summary>
        public bool StrictBindings { get; set; }

        /// <summary>
        /// Gets the binding capability snapshots advertised by the registered
        /// binders. These populate the V2 registry <c>SelectedBindings</c> node and
        /// contribute to refresh unchanged-detection.
        /// </summary>
        public IReadOnlyList<WoTBindingCapabilityDataType> BindingCapabilities => m_binders.Capabilities;

        /// <summary>
        /// Gets or sets the live server namespace table used to resolve a
        /// projection's recorded root <see cref="ExpandedNodeId"/> into a
        /// concrete server NodeId after its owning namespace is registered by
        /// the projection host. When <c>null</c>, materialized root NodeIds are
        /// not reported.
        /// </summary>
        public NamespaceTable? ServerNamespaceUris { get; set; }

        /// <summary>Releases the mutex used to serialise refreshes.</summary>
        public void Dispose()
        {
            m_mutex.Dispose();
        }

        private static ByteString DigestOf(WotResource resource)
            => (ByteString)(resource.DefaultVersion?.Digest ?? Array.Empty<byte>());

        private async ValueTask<ClosureOutcome> ProcessClosureAsync(
            WotRegistrySnapshot snapshot,
            WotDependencyClosure closure,
            uint generation,
            bool force,
            bool dryRun,
            bool strict,
            CancellationToken cancellationToken)
        {
            var results = ImmutableArray.CreateBuilder<WoTResourceLoadResultDataType>();
            var projections = new List<WotResourceProjection>();
            IReadOnlyList<WotResource> members = MembersOf(closure);

            // Unprojectable closure: cycle or missing dependency. Retain the
            // previous active generation and mark members failed.
            if (!closure.IsProjectable)
            {
                WoTPhaseEnum phase = closure.HasMissingDependency
                    ? WoTPhaseEnum.DependencyResolution
                    : WoTPhaseEnum.DependencyResolution;
                string reason = string.Join("; ", closure.Diagnostics);
                foreach (WotResource member in members)
                {
                    results.Add(FailResult(member, generation, phase, reason));
                    projections.Add(FailProjection(member, reason));
                    RaiseLoadFailure(member, generation, reason);
                }
                return new ClosureOutcome(results.ToImmutable(), projections);
            }

            // Project in topological (dependency-first) order.
            members = closure.OrderedResources;

            byte[] aggregateDigest = ComputeAggregateDigest(members);
            m_closures.TryGetValue(closure.Key, out ClosureState? tracked);

            // Unchanged: same digest/options/binder version, and not forced.
            if (tracked?.Handle is not null &&
                !force &&
                WotContentDigest.Equal(tracked.AggregateDigest, aggregateDigest))
            {
                foreach (WotResource member in members)
                {
                    results.Add(UnchangedResult(member, tracked.Generation));
                }
                return new ClosureOutcome(results.ToImmutable(), projections);
            }

            // Convert every member to a NodeSet2 source in dependency order.
            var sources = ImmutableArray.CreateBuilder<WotProjectionSource>();
            var perMemberNodeCount = new Dictionary<string, int>(StringComparer.Ordinal);
            var perMemberRoot = new Dictionary<string, ExpandedNodeId>(StringComparer.Ordinal);
            var bindingPlans = new List<WotBindingPlan>();
            bool degraded = false;

            foreach (WotResource member in members)
            {
                WotResourceVersion? version = member.DefaultVersion;
                if (version is null)
                {
                    string reason = "Resource has no default version.";
                    results.Add(FailResult(member, generation, WoTPhaseEnum.Fetch, reason));
                    projections.Add(FailProjection(member, reason));
                    RaiseLoadFailure(member, generation, reason);
                    return new ClosureOutcome(results.ToImmutable(), projections);
                }

                (UANodeSet? nodeSet, ExpandedNodeId? root, string? conversionError) =
                    TryConvert(member, snapshot);
                if (nodeSet is null)
                {
                    WoTValidationOutcomeDataType validation = FormatFailure(conversionError);
                    results.Add(FailResult(
                        member, generation, WoTPhaseEnum.FormatValidation, conversionError));
                    projections.Add(FailProjection(member, conversionError, validation));
                    RaiseValidationFailure(member, generation, validation, conversionError);
                    return new ClosureOutcome(results.ToImmutable(), projections);
                }

                WotBindingPlan plan = m_binders.Prepare(BuildPlanRequest(member, version));
                bindingPlans.Add(plan);
                if (!plan.FullySupported)
                {
                    if (strict)
                    {
                        string reason = "Unsupported binding forms in a strict closure.";
                        results.Add(FailResult(
                            member, generation, WoTPhaseEnum.Projection, reason));
                        projections.Add(FailProjection(member, reason));
                        RaiseBindingFailure(member, reason);
                        return new ClosureOutcome(results.ToImmutable(), projections);
                    }
                    degraded = true;
                    RaiseBindingFailure(member,
                        "Unsupported binding forms materialized as degraded nodes.");
                }
                else if (plan.HasNonExecutableForms)
                {
                    // A validated plan whose binding has no runtime executor (for
                    // example a planner-only protocol): materialize the nodes but
                    // flag the closure as degraded so callers know they cannot be
                    // driven yet.
                    degraded = true;
                }

                byte[] xml = SerializeNodeSet(nodeSet);
                perMemberNodeCount[member.Xid] = nodeSet.Items?.Length ?? 0;
                if (root is { } rootId)
                {
                    perMemberRoot[member.Xid] = rootId;
                }
                sources.Add(new WotProjectionSource(
                    member.ResourceId, OwnedModelUris(nodeSet), xml));
            }

            if (dryRun)
            {
                foreach (WotResource member in members)
                {
                    results.Add(new WoTResourceLoadResultDataType
                    {
                        Xid = member.Xid,
                        GroupId = member.GroupId,
                        ResourceId = member.ResourceId,
                        VersionId = member.DefaultVersionId ?? string.Empty,
                        Kind = member.Kind,
                        Outcome = degraded ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Success,
                        Phase = WoTPhaseEnum.Projection,
                        LoadState = member.LoadState,
                        Generation = 0,
                        MaterializedNodeCount = (uint)(perMemberNodeCount.TryGetValue(
                            member.Xid, out int c) ? c : 0),
                        ContentDigest = DigestOf(member),
                        Message = "Dry run; no projection committed."
                    });
                }
                return new ClosureOutcome(results.ToImmutable(), projections);
            }

            var document = new WotProjectionDocument(closure.Key, sources.ToImmutable());
            WotProjectionHandle handle;
            try
            {
                handle = tracked?.Handle is not null
                    ? await m_host.ShadowReloadAsync(
                        tracked.Handle, document, cancellationToken).ConfigureAwait(false)
                    : await m_host.AddAsync(document, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Projection failed: retain the previous active generation and its
                // tracked binding plans. The shadow switch never happened, so the
                // old plans remain active and no deactivation is performed
                // (rollback: old plans survive when the new switch fails).
                foreach (WotResource member in members)
                {
                    results.Add(FailResult(
                        member, generation, WoTPhaseEnum.Activation, ex.Message));
                    projections.Add(FailProjection(member, ex.Message));
                    RaiseLoadFailure(member, generation, ex.Message);
                }
                return new ClosureOutcome(results.ToImmutable(), projections);
            }

            // The shadow switch (or first add) succeeded. On an update, retire the
            // previously tracked binding plans before publishing the new closure
            // state so they are not leaked. This runs after the successful switch
            // and before the closure state is replaced; deactivating the old plans
            // first (then activating the new plans below) keeps a resource that is
            // shared between the old and new plan sets continuously bound.
            if (tracked is not null)
            {
                foreach (WotBindingPlan plan in tracked.BindingPlans)
                {
                    await m_binders.DeactivateAsync(plan, cancellationToken).ConfigureAwait(false);
                }
            }

            m_closures[closure.Key] = new ClosureState
            {
                Key = closure.Key,
                Handle = handle,
                AggregateDigest = aggregateDigest,
                Generation = generation,
                MemberXids = members.Select(m => m.Xid).ToImmutableArray(),
                BindingPlans = bindingPlans.ToImmutableArray()
            };

            foreach (WotBindingPlan plan in bindingPlans)
            {
                await m_binders.ActivateAsync(plan, cancellationToken).ConfigureAwait(false);
            }

            WoTOutcomeEnum memberOutcome = degraded ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Success;
            foreach (WotResource member in members)
            {
                int nodeCount = perMemberNodeCount.TryGetValue(member.Xid, out int c) ? c : 0;
                NodeId? rootNodeId = perMemberRoot.TryGetValue(member.Xid, out ExpandedNodeId root)
                    ? ResolveRootNodeId(root)
                    : null;
                WoTValidationOutcomeDataType validation = SuccessValidation();
                results.Add(new WoTResourceLoadResultDataType
                {
                    Xid = member.Xid,
                    GroupId = member.GroupId,
                    ResourceId = member.ResourceId,
                    VersionId = member.DefaultVersionId ?? string.Empty,
                    Kind = member.Kind,
                    Outcome = memberOutcome,
                    Phase = WoTPhaseEnum.Activation,
                    LoadState = WoTLoadStateEnum.Active,
                    Generation = generation,
                    MaterializedNodeCount = (uint)nodeCount,
                    RootNodeId = rootNodeId ?? NodeId.Null,
                    ContentDigest = DigestOf(member),
                    Message = degraded ? "Projected with degraded bindings." : "Projected."
                });
                projections.Add(new WotResourceProjection(
                    member.GroupId,
                    member.ResourceId,
                    WoTLoadStateEnum.Active,
                    member.DefaultVersionId,
                    generation,
                    nodeCount,
                    rootNodeId,
                    validation,
                    ImmutableArray<string>.Empty,
                    DateTime.UtcNow));
                RaiseResource(member, generation, memberOutcome, WoTLoadStateEnum.Active);
            }

            return new ClosureOutcome(results.ToImmutable(), projections);
        }

        private async ValueTask<int> ReconcileRetirementsAsync(
            HashSet<string> targetKeys,
            CancellationToken cancellationToken)
        {
            int retired = 0;
            List<string> stale = m_closures.Keys
                .Where(k => !targetKeys.Contains(k))
                .ToList();
            foreach (string key in stale)
            {
                if (m_closures.TryGetValue(key, out ClosureState? state))
                {
                    if (state.Handle is not null)
                    {
                        // Deactivate bindings before retiring the projection.
                        foreach (WotBindingPlan plan in state.BindingPlans)
                        {
                            await m_binders.DeactivateAsync(plan, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        await m_host.RemoveAsync(state.Handle, cancellationToken)
                            .ConfigureAwait(false);
                        retired++;
                    }
                    m_closures.Remove(key);
                }
            }
            return retired;
        }

        private (UANodeSet? NodeSet, ExpandedNodeId? Root, string? Error) TryConvert(
            WotResource resource, WotRegistrySnapshot snapshot)
        {
            WotResourceVersion? version = resource.DefaultVersion;
            if (version is null)
            {
                return (null, null, "Resource has no default version.");
            }
            WotConversionOutput output = m_converter.Convert(resource, version.Content, snapshot);
            if (!output.Succeeded)
            {
                return (null, null, output.Errors.IsDefaultOrEmpty
                    ? "The document could not be converted to a NodeSet."
                    : string.Join("; ", output.Errors));
            }
            return (output.NodeSet, output.RootNodeId, null);
        }

        /// <summary>
        /// Resolves a projection root, recorded before lifecycle add as an
        /// absolute <see cref="ExpandedNodeId"/>, into a concrete server NodeId
        /// once its owning namespace has been registered by the projection host.
        /// Returns <c>null</c> when there is no root or the namespace table is
        /// unavailable or does not yet contain the owning namespace.
        /// </summary>
        private NodeId? ResolveRootNodeId(ExpandedNodeId? root)
        {
            if (root is not { } value || value.IsNull)
            {
                return null;
            }
            NamespaceTable? namespaces = ServerNamespaceUris;
            if (namespaces is null)
            {
                return null;
            }
            NodeId resolved = ExpandedNodeId.ToNodeId(value, namespaces);
            return resolved.IsNull ? null : resolved;
        }

        private WotBindingPlanRequest BuildPlanRequest(
            WotResource resource, WotResourceVersion version)
        {
            return WotBindingPlanRequest.FromDocument(
                resource.Xid, resource.Kind, version.Content, m_converterOptions.MaxJsonDepth);
        }

        private byte[] ComputeAggregateDigest(IReadOnlyList<WotResource> members)
        {
            using var sha = SHA256.Create();
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                foreach (WotResource member in members
                    .OrderBy(m => m.Xid, StringComparer.Ordinal))
                {
                    writer.Write(member.Xid);
                    writer.Write(member.DefaultVersionId ?? string.Empty);
                    byte[] digest = member.DefaultVersion?.Digest ?? Array.Empty<byte>();
                    writer.Write(digest.Length);
                    writer.Write(digest);
                }
                writer.Write(m_converterOptions.MaxJsonDepth);
                writer.Write(BinderVersion);
            }
            buffer.Position = 0;
            return sha.ComputeHash(buffer.ToArray());
        }

        private string BinderVersion
        {
            get
            {
                IReadOnlyList<WoTBindingCapabilityDataType> caps = m_binders.Capabilities;
                if (caps.Count == 0)
                {
                    return "none";
                }
                var builder = new StringBuilder();
                foreach (WoTBindingCapabilityDataType cap in caps)
                {
                    builder.Append(cap.BindingUri).Append(';').Append(cap.ProfileVersion).Append('|');
                }
                return builder.ToString();
            }
        }

        private static byte[] SerializeNodeSet(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static ImmutableArray<string> OwnedModelUris(UANodeSet nodeSet)
        {
            if (nodeSet.Models is { Length: > 0 })
            {
                var uris = new List<string>(nodeSet.Models.Length);
                foreach (ModelTableEntry model in nodeSet.Models)
                {
                    if (!string.IsNullOrEmpty(model.ModelUri))
                    {
                        uris.Add(model.ModelUri);
                    }
                }
                if (uris.Count > 0)
                {
                    return uris.ToImmutableArray();
                }
            }
            if (nodeSet.NamespaceUris is { Length: > 0 })
            {
                return nodeSet.NamespaceUris
                    .Where(u => !string.Equals(u, Opc.Ua.Namespaces.OpcUa, StringComparison.Ordinal))
                    .ToImmutableArray();
            }
            return ImmutableArray<string>.Empty;
        }

        private static IReadOnlyList<WotResource> MembersOf(WotDependencyClosure closure)
        {
            return closure.Members.IsDefaultOrEmpty
                ? Array.Empty<WotResource>()
                : (IReadOnlyList<WotResource>)closure.Members;
        }

        private HashSet<string> ResolveSelection(
            WotRegistrySnapshot snapshot,
            ImmutableArray<WoTResourceSelectorDataType> selectors)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (selectors.IsDefaultOrEmpty)
            {
                return set;
            }
            foreach (WoTResourceSelectorDataType selector in selectors)
            {
                foreach (WotResource resource in snapshot.AllResources())
                {
                    if (Matches(resource, selector))
                    {
                        set.Add(resource.Xid);
                    }
                }
            }
            return set;
        }

        private static bool Matches(WotResource resource, WoTResourceSelectorDataType selector)
        {
            if (!string.IsNullOrEmpty(selector.Xid) &&
                !string.Equals(selector.Xid, resource.Xid, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(selector.GroupId) &&
                !string.Equals(selector.GroupId, resource.GroupId, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(selector.ResourceId) &&
                !string.Equals(selector.ResourceId, resource.ResourceId, StringComparison.Ordinal))
            {
                return false;
            }
            return true;
        }

        private static string? FirstError(IReadOnlyList<WotDiagnostic> diagnostics)
        {
            foreach (WotDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == WotDiagnosticSeverity.Error)
                {
                    return diagnostic.ToString();
                }
            }
            return null;
        }

        private static WoTResourceLoadResultDataType FailResult(
            WotResource resource, uint generation, WoTPhaseEnum phase, string? message)
            => new WoTResourceLoadResultDataType
            {
                Xid = resource.Xid,
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                Kind = resource.Kind,
                Outcome = WoTOutcomeEnum.Failed,
                Phase = phase,
                LoadState = WoTLoadStateEnum.Failed,
                Generation = generation,
                MaterializedNodeCount = 0,
                ContentDigest = DigestOf(resource),
                Message = message ?? string.Empty
            };

        private static WoTResourceLoadResultDataType UnchangedResult(
            WotResource resource, uint generation)
            => new WoTResourceLoadResultDataType
            {
                Xid = resource.Xid,
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = resource.ActiveVersionId ?? resource.DefaultVersionId ?? string.Empty,
                Kind = resource.Kind,
                Outcome = WoTOutcomeEnum.Unchanged,
                Phase = WoTPhaseEnum.Activation,
                LoadState = WoTLoadStateEnum.Active,
                Generation = generation,
                MaterializedNodeCount = (uint)resource.MaterializedNodeCount,
                ContentDigest = DigestOf(resource),
                Message = "Content digest unchanged."
            };

        private static WotResourceProjection FailProjection(
            WotResource resource, string? message, WoTValidationOutcomeDataType? validation = null)
            => new WotResourceProjection(
                resource.GroupId,
                resource.ResourceId,
                WoTLoadStateEnum.Failed,
                activeVersionId: null,
                resource.RefreshGeneration,
                resource.MaterializedNodeCount,
                rootNodeId: null,
                validation,
                string.IsNullOrEmpty(message)
                    ? ImmutableArray<string>.Empty
                    : ImmutableArray.Create(message!),
                DateTime.UtcNow)
            {
                // Keep the previous active projection when a refresh fails.
                RetainPreviousActiveVersion = true
            };

        private static WoTValidationOutcomeDataType SuccessValidation()
            => new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Success,
                CompatibilityValidated = true,
                CompatibilityOutcome = WoTOutcomeEnum.Success,
                ValidatedAt = DateTime.UtcNow,
                VocabularyVersion = WotNodeSetConverter.VocabularyNamespace
            };

        private static WoTValidationOutcomeDataType FormatFailure(string? reason)
            => new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Failed,
                FormatReason = reason ?? string.Empty,
                CompatibilityValidated = false,
                CompatibilityOutcome = WoTOutcomeEnum.Skipped,
                ValidatedAt = DateTime.UtcNow,
                VocabularyVersion = WotNodeSetConverter.VocabularyNamespace
            };

        private void RaiseResource(
            WotResource resource, uint generation, WoTOutcomeEnum outcome, WoTLoadStateEnum state)
            => RaiseEvent(new WotMaterializationEventArgs(WotMaterializationEventKind.Resource)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                DocumentKind = resource.Kind,
                Generation = generation,
                Phase = WoTPhaseEnum.Activation,
                Outcome = outcome,
                LoadState = state
            });

        private void RaiseLoadFailure(WotResource resource, uint generation, string? reason)
            => RaiseEvent(new WotMaterializationEventArgs(WotMaterializationEventKind.LoadFailure)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                DocumentKind = resource.Kind,
                Generation = generation,
                Phase = WoTPhaseEnum.Projection,
                Outcome = WoTOutcomeEnum.Failed,
                LoadState = WoTLoadStateEnum.Failed,
                Reason = reason ?? string.Empty
            });

        private void RaiseValidationFailure(
            WotResource resource, uint generation,
            WoTValidationOutcomeDataType validation, string? reason)
            => RaiseEvent(new WotMaterializationEventArgs(
                WotMaterializationEventKind.ValidationFailure)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                DocumentKind = resource.Kind,
                Generation = generation,
                Phase = WoTPhaseEnum.FormatValidation,
                Outcome = WoTOutcomeEnum.Failed,
                LoadState = WoTLoadStateEnum.Failed,
                Validation = validation,
                Reason = reason ?? string.Empty
            });

        private void RaiseBindingFailure(WotResource resource, string? reason)
            => RaiseEvent(new WotMaterializationEventArgs(
                WotMaterializationEventKind.BindingFailure)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                DocumentKind = resource.Kind,
                Outcome = WoTOutcomeEnum.Failed,
                LoadState = WoTLoadStateEnum.Failed,
                Reason = reason ?? string.Empty
            });

        private void RaiseEvent(WotMaterializationEventArgs args)
            => Event?.Invoke(this, args);

        private WotRefreshResult RejectedResult(
            WotRefreshRequest request, WotRegistrySnapshot snapshot, DateTime start)
        {
            var summary = new WoTRefreshSummaryDataType
            {
                RequestId = request.RequestId ?? string.Empty,
                Generation = 0,
                Outcome = WoTOutcomeEnum.Rejected,
                StartTime = start,
                EndTime = DateTime.UtcNow
            };
            return new WotRefreshResult(
                summary, ImmutableArray<WoTResourceLoadResultDataType>.Empty,
                (uint)snapshot.Generation);
        }

        private sealed class ClosureState
        {
            public string Key { get; set; } = string.Empty;
            public WotProjectionHandle? Handle { get; set; }
            public byte[] AggregateDigest { get; set; } = Array.Empty<byte>();
            public uint Generation { get; set; }
            public ImmutableArray<string> MemberXids { get; set; } = ImmutableArray<string>.Empty;
            public ImmutableArray<WotBindingPlan> BindingPlans { get; set; }
                = ImmutableArray<WotBindingPlan>.Empty;
        }

        private sealed class ClosureOutcome
        {
            public ClosureOutcome(
                ImmutableArray<WoTResourceLoadResultDataType> results,
                List<WotResourceProjection> projections)
            {
                Results = results;
                Projections = projections;
            }

            public ImmutableArray<WoTResourceLoadResultDataType> Results { get; }
            public List<WotResourceProjection> Projections { get; }
        }

        private readonly IWotRegistryService m_registry;
        private readonly IWotProjectionHost m_host;
        private readonly IWotBinderRegistry m_binders;
        private readonly IWotDocumentConverter m_converter;
        private readonly WotNodeSetConverterOptions m_converterOptions;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private readonly Dictionary<string, ClosureState> m_closures =
            new(StringComparer.Ordinal);
        private uint m_generation;
    }
}
