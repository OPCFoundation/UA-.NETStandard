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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;
using Wot = Opc.Ua.WotCon;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Published WoT asset management and pinned preview registry tasks. Sample
/// documents contain no endpoints, credentials or external model dependencies.
/// </summary>
internal sealed class WotCompanionProvider : ICompanionProvider
{
    public CompanionDescriptor Descriptor { get; } = new(
        "wot", "WoT Connectivity", Wot.Namespaces.WotCon,
        "OPC 10100-1 v1.02 asset management; WoT Connectivity 1.1 draft, pinned 2026-09-05");

    public ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        return IndustrialCompanionAccess.DiscoverAsync(
            context, Descriptor.Id, s_types, [Opc.Ua.ObjectIds.ObjectsFolder], cancellationToken);
    }

    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        switch (target.TypeName)
        {
            case kManagementKind:
                var connectivity = new WotConnectivityClient(context.Session, target.NodeId, context.Telemetry);
                ArrayOf<CompanionValue> supported = await IndustrialCompanionAccess.ReadPropertiesAsync(
                    context, connectivity.ManagementObjectId, Wot.Namespaces.WotCon, ["SupportedWoTBindings"],
                    cancellationToken).ConfigureAwait(false);
                Variant bindingValue = supported[0].Value;
                if (bindingValue.TryGetValue(out ArrayOf<string> bindings))
                {
                    IndustrialCompanionAccess.CheckFields(context, bindings.Count);
                    supported = [new("Supported binding count", Variant.From(bindings.Count))];
                }
                else if (!bindingValue.TryGetValue(out StatusCode _))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch, "SupportedWoTBindings is not a URI array.");
                }
                return new CompanionInspection(
                    supported,
                    [
                        new("refresh", "Read management capabilities again", CompanionOperationSafety.ReadOnly),
                        new("create-sample-asset", "Create a disconnected sample asset",
                            CompanionOperationSafety.SampleMutation, kSampleHint)
                    ],
                    "Published asset-management surface. No network discovery or connection test is run during " +
                    "inspection. The sample uploads a fixed Thing Description without forms or endpoints.");
            case kRegistryKind:
                IndustrialCompanionAccess.CheckFields(context, 7);
                var registry = new WotRegistryClient(context.Session, target.NodeId, context.Telemetry);
                ArrayOf<CompanionValue> identity = await IndustrialCompanionAccess.ReadPropertiesAsync(
                    context, registry.RegistryNodeId, XRegistryWellKnown.XRegistryNamespaceUri,
                    ["RegistryId", "SpecVersion", "Epoch"], cancellationToken).ConfigureAwait(false);
                ArrayOf<CompanionValue> policy = await IndustrialCompanionAccess.ReadPropertiesAsync(
                    context, registry.RegistryNodeId, Wot.Namespaces.WotCon,
                    ["AutoRefresh", "RefreshGeneration", "VocabularyVersion", "StrictValidation"],
                    cancellationToken).ConfigureAwait(false);
                return new CompanionInspection(
                    [.. identity, .. policy],
                    [
                        new("refresh", "Read registry metadata again", CompanionOperationSafety.ReadOnly),
                        new("register-sample-model", "Register a fixed sample Thing Model",
                            CompanionOperationSafety.SampleMutation, kSampleHint),
                        new("refresh-sample-model", "Refresh only the fixed sample Thing Model",
                            CompanionOperationSafety.SampleMutation, kSampleHint)
                    ],
                    "WoT Connectivity 1.1 draft registry on xRegistry 0.6.0 (2026-09-05). Registration reuses " +
                    "existing version 1 without replacing it. Server AutoRefresh policy still applies. " +
                    "Explicit refresh verifies the sample bytes and never selects the whole registry or dependents.");
            case kAssetFileKind:
                ArrayOf<CompanionValue> file = await IndustrialCompanionAccess.ReadPropertiesAsync(
                    context, target.NodeId, Opc.Ua.Namespaces.OpcUa, ["Size", "MimeType"], cancellationToken)
                    .ConfigureAwait(false);
                return DocumentInspection(file);
            default:
                ArrayOf<CompanionValue> document = await IndustrialCompanionAccess.ReadPropertiesAsync(
                    context, target.NodeId, Wot.Namespaces.WotCon,
                    ["Enabled", "LoadState", "DesiredVersionId", "ActiveVersionId", "ContentDigest", "RootNodeId"],
                    cancellationToken).ConfigureAwait(false);
                return DocumentInspection(document);
        }
    }

    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operationId == "refresh")
        {
            IndustrialCompanionAccess.RequireNoInput(input);
            CompanionInspection inspection = await InspectAsync(context, target, cancellationToken)
                .ConfigureAwait(false);
            return new CompanionOperationResult(inspection.Summary, inspection.Values);
        }
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        if (operationId == "inspect-json" &&
            target.TypeName is kAssetFileKind or kThingDescriptionKind or kThingModelKind)
        {
            IndustrialCompanionAccess.RequireNoInput(input);
            IndustrialCompanionAccess.CheckFields(context, 3);
            FileTypeClient file = target.TypeName switch
            {
                kAssetFileKind => new WoTAssetFileTypeClient(context.Session, target.NodeId, context.Telemetry),
                kThingDescriptionKind => new ThingDescriptionFileTypeClient(
                    context.Session, target.NodeId, context.Telemetry),
                _ => new ThingModelFileTypeClient(context.Session, target.NodeId, context.Telemetry)
            };
            ByteString bytes = await IndustrialCompanionAccess.ReadDocumentAsync(file, cancellationToken)
                .ConfigureAwait(false);
            return new CompanionOperationResult(
                "Read and parsed at most 64 KiB of JSON. Only size, digest and member count leave the provider; " +
                "document values, security definitions and URLs are not displayed.",
                IndustrialCompanionAccess.DescribeDocument(bytes));
        }
        if (operationId == "create-sample-asset" && target.TypeName == kManagementKind)
        {
            string sampleId = IndustrialCompanionAccess.SampleId(input);
            IndustrialCompanionAccess.CheckFields(context, 2);
            return await CreateSampleAssetAsync(context, target.NodeId, sampleId, cancellationToken)
                .ConfigureAwait(false);
        }
        if (target.TypeName != kRegistryKind ||
            operationId is not ("register-sample-model" or "refresh-sample-model"))
        {
            throw IndustrialCompanionAccess.Unsupported(
                "This WoT operation is not supported for the selected instance.");
        }
        string resourceId = IndustrialCompanionAccess.SampleId(input);
        var registry = new WotRegistryClient(context.Session, target.NodeId, context.Telemetry);
        if (operationId == "register-sample-model")
        {
            IndustrialCompanionAccess.CheckFields(context, 3);
            // The convenience group opener probes/caches legacy hierarchy even on a
            // transport failure. The generated group Method and strict type check do not.
            (NodeId group, _) = await registry.Proxy.GetOrCreateGroupAsync(
                WotRegistryClient.ThingModelsGroupId, cancellationToken).ConfigureAwait(false);
            await RequireModelGroupAsync(context, group, cancellationToken).ConfigureAwait(false);
            ResourceRegistrationResult registered = await registry.GetOrRegisterResourceAsync(
                group, resourceId, s_sampleModel, "1", 1024, cancellationToken).ConfigureAwait(false);
            return new CompanionOperationResult(
                registered.Created
                    ? "Registered fixed sample Thing Model version 1. No explicit refresh was requested."
                    : "Version 1 already existed; its document was not replaced.",
                [
                    new("Version NodeId", Variant.From(registered.ResourceNodeId)),
                    new("Version ID", Variant.From(registered.AssignedVersionId)),
                    new("Created", Variant.From(registered.Created))
                ]);
        }
        return await RefreshSampleAsync(context, registry, resourceId, cancellationToken).ConfigureAwait(false);
    }

    private static CompanionInspection DocumentInspection(ArrayOf<CompanionValue> values)
    {
        return new CompanionInspection(
            values,
            [
                new("refresh", "Read document metadata again", CompanionOperationSafety.ReadOnly),
                new("inspect-json", "Inspect bounded JSON without exposing content", CompanionOperationSafety.ReadOnly)
            ],
            "Read-only document metadata. JSON inspection is capped at 64 KiB and depth 32; raw content, " +
            "credential fields and endpoint URLs are never returned.");
    }

    private static async ValueTask<CompanionOperationResult> CreateSampleAssetAsync(
        CompanionContext context,
        NodeId managementId,
        string sampleId,
        CancellationToken cancellationToken)
    {
        var client = new WotConnectivityClient(context.Session, managementId, context.Telemetry);
        NodeId assetId = await client.Proxy.CreateAssetAsync(sampleId, cancellationToken).ConfigureAwait(false);
        try
        {
            WotAssetClient asset = await client.OpenAssetAsync(assetId, cancellationToken).ConfigureAwait(false);
            using var source = new MemoryStream(s_sampleDescription.Span.ToArray(), writable: false);
            await asset.UploadThingDescriptionAsync(source, cancellationToken).ConfigureAwait(false);
            return new CompanionOperationResult(
                "Created and uploaded the disconnected sample Thing Description. No external endpoint was supplied.",
                [
                    new("Asset NodeId", Variant.From(asset.AssetId)),
                    new("WoT file NodeId", Variant.From(asset.File.ObjectId))
                ]);
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await client.DeleteAssetAsync(assetId, cleanup.Token).ConfigureAwait(false);
            }
            catch
            {
                // Only the newly created asset is eligible for compensation. Preserve the original failure.
            }
            throw;
        }
    }

    private static async ValueTask<CompanionOperationResult> RefreshSampleAsync(
        CompanionContext context,
        WotRegistryClient registry,
        string resourceId,
        CancellationToken cancellationToken)
    {
        IndustrialCompanionAccess.CheckFields(context, 5);
        ArrayOf<CompanionValue> generation = await IndustrialCompanionAccess.ReadPropertiesAsync(
            context, registry.RegistryNodeId, Wot.Namespaces.WotCon, ["RefreshGeneration"], cancellationToken)
            .ConfigureAwait(false);
        if (!generation[0].Value.TryGetValue(out uint expectedGeneration))
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "RefreshGeneration must be a UInt32.");
        }
        NodeId group = await IndustrialCompanionAccess.ResolveChildAsync(
            context, registry.RegistryNodeId, Wot.Namespaces.WotCon,
            WotRegistryClient.ThingModelsGroupId, false, cancellationToken).ConfigureAwait(false);
        await RequireModelGroupAsync(context, group, cancellationToken).ConfigureAwait(false);
        NodeId resource = await IndustrialCompanionAccess.ResolveChildAsync(
            context, group, Wot.Namespaces.WotCon, resourceId, false, cancellationToken).ConfigureAwait(false);
        NodeId version = await ResolveSampleVersionAsync(context, resource, cancellationToken)
            .ConfigureAwait(false);
        ByteString content = await IndustrialCompanionAccess.ReadDocumentAsync(
            new ThingModelFileTypeClient(context.Session, version, context.Telemetry), cancellationToken)
            .ConfigureAwait(false);
        if (!content.Span.SequenceEqual(s_sampleModel.Span))
        {
            throw IndustrialCompanionAccess.Unsupported(
                "Refresh is restricted to the provider's exact sample Thing Model.");
        }
        WotRegistryRefreshResult result = await registry.RefreshAsync(
            [
                new WoTResourceSelectorDataType
                {
                    Kind = WoTDocumentKindEnum.ThingModel,
                    GroupId = WotRegistryClient.ThingModelsGroupId,
                    ResourceId = resourceId,
                    VersionId = "1"
                }
            ],
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
            expectedGeneration, resourceId, cancellationToken).ConfigureAwait(false);
        if (result.Results.Count > 1 || result.Summary.Total > 1)
        {
            throw IndustrialCompanionAccess.Limit(
                "The server refreshed more than the single selected sample resource.");
        }
        if (result.HasFailures || result.Summary.Failed != 0)
        {
            throw new ServiceResultException(StatusCodes.BadInvalidState, "The sample refresh reported a failure.");
        }
        return new CompanionOperationResult(
            "Completed the bounded sample-only refresh. The typed outcome and counts are reported, " +
            "not document diagnostics.",
            [
                new("Generation", Variant.From(result.NewGeneration)),
                new("Outcome", Variant.From((int)result.Summary.Outcome)),
                new("Succeeded", Variant.From(result.Summary.Succeeded)),
                new("Unchanged", Variant.From(result.Summary.Unchanged)),
                new("Skipped", Variant.From(result.Summary.Skipped))
            ]);
    }

    private static async ValueTask<NodeId> ResolveSampleVersionAsync(
        CompanionContext context,
        NodeId resourceId,
        CancellationToken cancellationToken)
    {
        NodeId versions = await IndustrialCompanionAccess.ResolveChildAsync(
            context, resourceId, XRegistryWellKnown.XRegistryNamespaceUri,
            "Versions", true, cancellationToken).ConfigureAwait(false);
        if (versions.IsNull)
        {
            throw IndustrialCompanionAccess.Unsupported(
                "Sample refresh requires the xRegistry 0.6.0 distinct Versions hierarchy.");
        }
        var budget = new IndustrialBrowseBudget(context);
        NodeId found = NodeId.Null;
        await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
            context, versions, BrowseDirection.Forward, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
            NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
        {
            if (reference.BrowseName.Name != "1")
            {
                continue;
            }
            string? kind = await IndustrialCompanionAccess.ClassifyAsync(
                context, reference.TypeDefinition, s_types, budget, cancellationToken).ConfigureAwait(false);
            NodeId nodeId = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
            if (kind != kThingModelKind || nodeId.IsNull || !found.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "Sample version 1 is not a unique Thing Model.");
            }
            found = nodeId;
        }
        if (found.IsNull)
        {
            throw IndustrialCompanionAccess.Unsupported("Sample Thing Model version 1 is not registered.");
        }
        return found;
    }

    private static ValueTask RequireModelGroupAsync(
        CompanionContext context,
        NodeId group,
        CancellationToken cancellationToken)
    {
        return IndustrialCompanionAccess.RequireTargetAsync(
            context, new CompanionTarget("wot", group, "Thing Model group", "Thing Model group"), "wot",
            [new IndustrialCompanionType(Wot.ObjectTypeIds.ThingModelGroupType, "Thing Model group")],
            cancellationToken);
    }

    private const string kManagementKind = "WoT asset management";
    private const string kRegistryKind = "WoT registry";
    private const string kAssetFileKind = "WoT asset document";
    private const string kThingDescriptionKind = "Thing Description";
    private const string kThingModelKind = "Thing Model";
    private const string kSampleHint =
        "1–32 lowercase ASCII letters, digits or hyphens; ID becomes ualens-sample-<suffix>.";
    private static readonly ArrayOf<IndustrialCompanionType> s_types =
    [
        new(Wot.ObjectTypeIds.WoTAssetConnectionManagementType, kManagementKind),
        new(Wot.ObjectTypeIds.WoTRegistryType, kRegistryKind),
        new(Wot.ObjectTypeIds.WoTAssetFileType, kAssetFileKind),
        new(Wot.ObjectTypeIds.ThingDescriptionFileType, kThingDescriptionKind),
        new(Wot.ObjectTypeIds.ThingModelFileType, kThingModelKind)
    ];
    private static readonly ByteString s_sampleModel = ByteString.From(
        """
        {"@context":"https://www.w3.org/2022/wot/td/v1.1","@type":"tm:ThingModel",
        "id":"urn:ualens:sample:model","title":"UaLens disconnected sample model",
        "properties":{"sample":{"type":"number","readOnly":true}}}
        """u8);
    private static readonly ByteString s_sampleDescription = ByteString.From(
        """
        {"@context":"https://www.w3.org/2022/wot/td/v1.1",
        "id":"urn:ualens:sample:asset","title":"UaLens disconnected sample asset",
        "securityDefinitions":{"nosec_sc":{"scheme":"nosec"}},"security":["nosec_sc"],"properties":{}}
        """u8);
}
