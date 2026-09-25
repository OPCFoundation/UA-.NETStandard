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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;
using Opc.Ua.XRegistry;
using Wot = Opc.Ua.WotCon;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed class WotRefreshTask : CompanionTaskInput
    {
        public WotRefreshTask(
            RegistryLifecycleTask request, NodeId versionNode, ByteString contentDigest, uint generation)
        {
            Request = request;
            VersionNode = versionNode;
            m_contentDigest = contentDigest.Copy();
            Generation = generation;
            RequestId = Guid.NewGuid().ToString("N");
        }

        public RegistryLifecycleTask Request { get; }
        public NodeId VersionNode { get; }
        public ByteString ContentDigest => m_contentDigest.Copy();
        public uint Generation { get; }
        public string RequestId { get; }

        public override string Review => Request.Review +
            $"\nRefresh exactly one existing Version: {VersionNode}; generation: {Generation}; " +
            $"request: {RequestId}.\n" +
            $"Content SHA-256: {Convert.ToHexString(m_contentDigest.Span)}.\n" +
            "No Force, dependents or whole-registry selection. Server materialization and endpoint policy apply.";

        private readonly ByteString m_contentDigest;
    }

    internal sealed partial class WotCompanionProvider
    {
        private static async ValueTask<WotRefreshTask> PrepareRefreshAsync(
            CompanionContext context, RegistryLifecycleTask request, uint generation,
            CancellationToken cancellationToken)
        {
            IndustrialCompanionAccess.CheckFields(context, 7);
            await RequireRefreshGenerationAsync(context, request.Target.NodeId, generation, cancellationToken)
                .ConfigureAwait(false);
            (NodeId version, ByteString digest) = await ReadRefreshSelectionAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
            return new WotRefreshTask(request, version, digest, generation);
        }

        private static async ValueTask<CompanionOperationResult> ExecuteRefreshAsync(
            CompanionContext context, WotRefreshTask task, CancellationToken cancellationToken)
        {
            RegistryLifecycleTask request = task.Request;
            await RequireRefreshGenerationAsync(context, request.Target.NodeId, task.Generation, cancellationToken)
                .ConfigureAwait(false);
            (NodeId version, ByteString digest) = await ReadRefreshSelectionAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
            if (version != task.VersionNode || digest != task.ContentDigest)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The reviewed WoT Version changed.");
            }
            var registry = new WotRegistryClient(context.Session, request.Target.NodeId, context.Telemetry);
            WoTDocumentKindEnum kind = request.Group == WotRegistryClient.ThingModelsGroupId
                ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription;
            WotRegistryRefreshResult result = await registry.RefreshAsync(
                [new WoTResourceSelectorDataType
                {
                    Kind = kind, GroupId = request.Group, ResourceId = request.Identifier, VersionId = request.Version
                }],
                new WoTRefreshOptionsDataType
                {
                    Atomicity = WoTAtomicityEnum.PerResource,
                    DeletePolicy = WoTDeletePolicyEnum.Reject,
                    IncludeDependents = false,
                    Force = false,
                    DryRun = false,
                    MaxParallelism = 1,
                    Timeout = 5000
                },
                task.Generation, task.RequestId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            WoTRefreshSummaryDataType summary = result.Summary;
            ulong accounted = (ulong)summary.Succeeded + summary.Unchanged + summary.Failed + summary.Skipped;
            if (result.Results.Count != 1 ||
                result.Summary.Total != 1 ||
                result.Summary.RequestId != task.RequestId ||
                result.Summary.Generation != result.NewGeneration ||
                result.Summary.Atomicity != WoTAtomicityEnum.PerResource ||
                !Enum.IsDefined(result.Summary.Outcome) ||
                accounted != summary.Total)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "Refresh did not return the exact selected Version and correlation.");
            }
            WoTResourceLoadResultDataType item = result.Results[0];
            if (item is null ||
                item.GroupId != request.Group ||
                item.ResourceId != request.Identifier ||
                item.VersionId != request.Version ||
                item.Kind != kind ||
                item.ContentDigest != task.ContentDigest ||
                item.Generation != result.NewGeneration ||
                result.NewGeneration < task.Generation ||
                !Enum.IsDefined(item.Outcome) ||
                !Enum.IsDefined(item.LoadState))
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "Refresh returned unrelated or inconsistent materialization evidence.");
            }
            if (result.HasFailures || result.Summary.Failed != 0 || item.LoadState == WoTLoadStateEnum.Failed)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The selected refresh reported failure.");
            }
            IndustrialCompanionAccess.CheckFields(context, 7);
            return new CompanionOperationResult(
                "The server returned correlated materialization evidence for the selected Version. " +
                "This is not proof of reachability or successful operations on external endpoints.",
                [
                    new("Version NodeId", Variant.From(task.VersionNode)),
                    new("Request ID", Variant.From(task.RequestId)),
                    new("Generation", Variant.From(result.NewGeneration)),
                    new("Outcome", Variant.From((int)item.Outcome)),
                    new("Load state", Variant.From((int)item.LoadState)),
                    new("Root NodeId", Variant.From(item.RootNodeId)),
                    new("Materialized nodes", Variant.From(item.MaterializedNodeCount))
                ]);
        }

        private static async ValueTask<(NodeId Version, ByteString Digest)> ReadRefreshSelectionAsync(
            CompanionContext context, RegistryLifecycleTask request, CancellationToken cancellationToken)
        {
            NodeId group = await IndustrialCompanionAccess.ResolveChildAsync(
                context, request.Target.NodeId, Wot.Namespaces.WotCon, request.Group, false, cancellationToken)
                .ConfigureAwait(false);
            await RequireLifecycleGroupAsync(context, group, request.Group, cancellationToken).ConfigureAwait(false);
            NodeId resource = await IndustrialCompanionAccess.ResolveChildAsync(
                context, group, Wot.Namespaces.WotCon, request.Identifier, false, cancellationToken)
                    .ConfigureAwait(false);
            NodeId versions = await IndustrialCompanionAccess.ResolveChildAsync(
                context, resource, XRegistryWellKnown.XRegistryNamespaceUri, "Versions", false, cancellationToken)
                .ConfigureAwait(false);
            NodeId version = await IndustrialCompanionAccess.ResolveChildAsync(
                context, versions, Wot.Namespaces.WotCon, request.Version, false, cancellationToken)
                    .ConfigureAwait(false);
            bool model = request.Group == WotRegistryClient.ThingModelsGroupId;
            string kind = model ? kThingModelKind : kThingDescriptionKind;
            await IndustrialCompanionAccess.RequireTargetAsync(context,
                new CompanionTarget("wot", version, request.Version, kind), "wot",
                [new IndustrialCompanionType(model ? Wot.ObjectTypeIds.ThingModelFileType :
                    Wot.ObjectTypeIds.ThingDescriptionFileType, kind)], cancellationToken).ConfigureAwait(false);
            ArrayOf<CompanionValue> fields = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, version, Wot.Namespaces.WotCon, ["ContentDigest"], cancellationToken).ConfigureAwait(false);
            if (!fields[0].Value.TryGetValue(out ByteString digest) || digest.Length != 32)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoData, "The selected Version has no SHA-256 content evidence.");
            }
            return (version, digest.Copy());
        }

        private static async ValueTask RequireRefreshGenerationAsync(
            CompanionContext context, NodeId registry, uint expected, CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> fields = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, registry, Wot.Namespaces.WotCon, ["RefreshGeneration"], cancellationToken)
                    .ConfigureAwait(false);
            if (expected == 0 || !fields[0].Value.TryGetValue(out uint generation) || generation != expected)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The WoT refresh generation changed.");
            }
        }
    }
}
