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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;
using RegistryTypes = Opc.Ua.XRegistry.ObjectTypeIds;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Generic xRegistry metadata, bounded document inspection and idempotent
/// fixed-sample registration through the shared generated registry lifecycle.
/// </summary>
internal sealed class RegistryCompanionProvider : ICompanionProvider
{
    public CompanionDescriptor Descriptor { get; } = new(
        "xregistry", "xRegistry", XRegistryWellKnown.XRegistryNamespaceUri,
        "Draft abstract xRegistry 0.6.0, pinned 2026-09-05; provisional OPC UA NodeIds");

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
        var client = CreateClient(context, target.NodeId);
        if (target.TypeName == kRegistryKind)
        {
            IndustrialCompanionAccess.CheckFields(context, 5);
            RegistryTypeClient registry = client.GetRegistry(target.NodeId);
            ArrayOf<CompanionValue> metadata = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, registry.ObjectId, XRegistryWellKnown.XRegistryNamespaceUri,
                ["RegistryId", "SpecVersion", "Epoch", "Name"], cancellationToken).ConfigureAwait(false);
            NodeId model = await IndustrialCompanionAccess.ResolveChildAsync(
                context, registry.ObjectId, XRegistryWellKnown.XRegistryNamespaceUri,
                "Model", true, cancellationToken).ConfigureAwait(false);
            ArrayOf<CompanionOperation> operations = model.IsNull
                ? s_registryOperations
                : [.. s_registryOperations, new("inspect-model", "Inspect bounded model JSON",
                    CompanionOperationSafety.ReadOnly)];
            return new CompanionInspection(
                [.. metadata, new("Model file NodeId", Variant.From(model))], operations,
                "Live registry specification version and model-file identity. The generic task creates only " +
                "ualens-samples/ualens-sample-<suffix>/versions/1. Domain registries may reject generic JSON; " +
                "that rejection is propagated. Existing document versions are never overwritten.");
        }
        if (target.TypeName == kGroupKind)
        {
            GroupTypeClient group = client.GetGroup(target.NodeId);
            ArrayOf<CompanionValue> metadata = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, group.ObjectId, XRegistryWellKnown.XRegistryNamespaceUri,
                ["GroupId", "Epoch", "Name", "CreatedAt", "ModifiedAt"], cancellationToken).ConfigureAwait(false);
            return new CompanionInspection(
                metadata, [new("refresh", "Read group metadata again", CompanionOperationSafety.ReadOnly)],
                "Read-only group metadata. Group deletion, labels and arbitrary resource authoring are not offered.");
        }

        ResourceTypeClient resource = client.GetResource(target.NodeId);
        ArrayOf<CompanionValue> fields = await IndustrialCompanionAccess.ReadPropertiesAsync(
            context, resource.ObjectId, XRegistryWellKnown.XRegistryNamespaceUri,
            ["ResourceId", "VersionId", "Format", "ContentType", "Epoch", "CreatedAt", "ModifiedAt"],
            cancellationToken).ConfigureAwait(false);
        return new CompanionInspection(
            fields,
            [
                new("refresh", "Read version metadata again", CompanionOperationSafety.ReadOnly),
                new("inspect-json", "Inspect bounded document JSON", CompanionOperationSafety.ReadOnly)
            ],
            "Read-only resource/version metadata. JSON inspection is limited to 64 KiB and depth 32 and returns " +
            "only length, SHA-256 and member count. Credential fields and federation URLs are not exposed " +
            "or followed by this client.");
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
        IndustrialCompanionAccess.CheckFields(context, 3);
        var client = CreateClient(context, target.NodeId);
        if (operationId == "register-sample" && target.TypeName == kRegistryKind)
        {
            string resourceId = IndustrialCompanionAccess.SampleId(input);
            GroupRegistrationResult group = await client.GetOrCreateGroupAsync(
                target.NodeId, "ualens-samples", cancellationToken).ConfigureAwait(false);
            ResourceRegistrationResult registered = await client.GetOrRegisterResourceAsync(
                group.GroupNodeId, resourceId, s_sampleDocument, "1", 1024, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                registered.Created
                    ? "Registered the fixed generic JSON sample as version 1."
                    : "Version 1 already existed. No document bytes were written.",
                [
                    new("Version NodeId", Variant.From(registered.ResourceNodeId)),
                    new("Version ID", Variant.From(registered.AssignedVersionId)),
                    new("Created", Variant.From(registered.Created))
                ]);
        }
        IndustrialCompanionAccess.RequireNoInput(input);
        FileTypeClient file;
        if (operationId == "inspect-json" && target.TypeName == kResourceKind)
        {
            file = client.GetResource(target.NodeId);
        }
        else if (operationId == "inspect-model" && target.TypeName == kRegistryKind)
        {
            RegistryTypeClient registry = client.GetRegistry(target.NodeId);
            _ = await IndustrialCompanionAccess.ResolveChildAsync(
                context, registry.ObjectId, XRegistryWellKnown.XRegistryNamespaceUri,
                "Model", false, cancellationToken).ConfigureAwait(false);
            file = await registry.GetModelAsync(context.Telemetry, cancellationToken).ConfigureAwait(false)
                ?? throw IndustrialCompanionAccess.Unsupported("The registry Model file is not available.");
        }
        else
        {
            throw IndustrialCompanionAccess.Unsupported("This xRegistry operation is not supported for this instance.");
        }
        ByteString document = await IndustrialCompanionAccess.ReadDocumentAsync(file, cancellationToken)
            .ConfigureAwait(false);
        return new CompanionOperationResult(
            "Read bounded JSON through the typed file proxy. Raw document data and credentials were not returned.",
            IndustrialCompanionAccess.DescribeDocument(document));
    }

    private static GenericXRegistryClient CreateClient(CompanionContext context, NodeId root)
    {
        return new GenericXRegistryClient(
            context.Session, XRegistryWellKnown.XRegistryNamespaceUri, root, context.Telemetry);
    }

    private const string kRegistryKind = "Registry";
    private const string kGroupKind = "Registry group";
    private const string kResourceKind = "Registry resource/version";
    private static readonly ArrayOf<IndustrialCompanionType> s_types =
    [
        new(RegistryTypes.RegistryType, kRegistryKind),
        new(RegistryTypes.GroupType, kGroupKind),
        new(RegistryTypes.ResourceType, kResourceKind)
    ];
    private static readonly ArrayOf<CompanionOperation> s_registryOperations =
    [
        new("refresh", "Read registry metadata again", CompanionOperationSafety.ReadOnly),
        new("register-sample", "Register fixed generic JSON sample", CompanionOperationSafety.SampleMutation,
            "1–32 lowercase ASCII letters, digits or hyphens; ID becomes ualens-sample-<suffix>.")
    ];
    private static readonly ByteString s_sampleDocument = ByteString.From(
        """{"kind":"ualens-sample","version":1,"description":"Fixed local sample; no credentials or endpoints."}"""u8);
}
