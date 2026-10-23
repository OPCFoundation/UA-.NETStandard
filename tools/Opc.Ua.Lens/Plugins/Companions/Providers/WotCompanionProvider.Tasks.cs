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
using Opc.Ua.Wot;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;
using Wot = Opc.Ua.WotCon;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class WotCompanionProvider
    {
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, Wot.Namespaces.WotCon, cancellationToken);
            cancellationToken = lifetime.Token;
            WotTaskDefinition task = FindLifecycle(target, operationId);
            if (inputs.Count != task.Operation.Inputs.Count)
            {
                throw new ArgumentException("Supply the exact WoT task form.", nameof(inputs));
            }
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Name != task.Operation.Inputs[i].Name ||
                    !inputs[i].Value.TypeInfo.IsScalar ||
                    inputs[i].Value.TypeInfo.BuiltInType != task.Operation.Inputs[i].DataType)
                {
                    throw new ArgumentException("The WoT field names or types changed.", nameof(inputs));
                }
            }
            uint epoch = operationId is "create-asset" or "delete-asset" ? 0 : RegistryTaskForms.ExpectedEpoch(inputs);
            string id = operationId is "create-asset" or "register-version" or "refresh-resource"
                ? RegistryTaskForms.Id(inputs, "id") : string.Empty;
            string version = operationId is "register-version" or "select-default" or "refresh-resource"
                ? RegistryTaskForms.Id(inputs, "version") : string.Empty;
            string group = operationId is "register-version" or "refresh-resource"
                ? RegistryTaskForms.Id(inputs, "group") : string.Empty;
            ByteString document = operationId is "create-asset" or "register-version"
                ? RegistryTaskForms.Json(inputs, wot: true,
                    requiredKind: operationId == "create-asset" || group == WotRegistryClient.ThingDescriptionsGroupId
                        ? WotDocumentKind.ThingDescription : WotDocumentKind.ThingModel)
                : default;
            bool enabled = operationId == "set-enabled" && RegistryTaskForms.Boolean(inputs, "enabled");
            if (operationId is "delete-document" or "delete-asset" &&
                !RegistryTaskForms.Boolean(inputs, "includeChildren"))
            {
                throw new ArgumentException("Confirm deletion of the logical resource and its versions.");
            }
            if (operationId is "register-version" or "refresh-resource")
            {
                if (group is not (WotRegistryClient.ThingModelsGroupId or WotRegistryClient.ThingDescriptionsGroupId))
                {
                    throw new ArgumentException("Choose thingmodels or thingdescriptions.");
                }
                if (operationId == "register-version")
                {
                    enabled = RegistryTaskForms.Boolean(inputs, "acceptAutoRefresh");
                    await RequireAutoRefreshAsync(context, target.NodeId, enabled, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (!RegistryTaskForms.Boolean(inputs, "acceptRefresh"))
                {
                    throw new ArgumentException("Confirm the selected Version's materialization effects.");
                }
            }
            RegistryMutationScope scope = await RequireLifecycleAsync(
                context, target, task, epoch, cancellationToken).ConfigureAwait(false);
            var request = new RegistryLifecycleTask(
                target, operationId, epoch, id, version, document, enabled, group, scope);
            if (operationId == "delete-asset")
            {
                if (!RegistryTaskForms.Field(inputs, "asset").TryGetValue(out NodeId asset))
                {
                    throw new ArgumentException("An exact asset NodeId is required.");
                }
                return await PrepareAssetDeleteAsync(context, request, asset, cancellationToken).ConfigureAwait(false);
            }
            if (operationId == "refresh-resource")
            {
                if (!RegistryTaskForms.Field(inputs, "generation").TryGetValue(out uint generation) || generation == 0)
                {
                    throw new ArgumentException("A nonzero reviewed refresh generation is required.");
                }
                return await PrepareRefreshAsync(context, request, generation, cancellationToken).ConfigureAwait(false);
            }
            return request;
        }

        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, Wot.Namespaces.WotCon, cancellationToken);
            cancellationToken = lifetime.Token;
            RegistryLifecycleTask prepared = input switch
            {
                RegistryLifecycleTask lifecycle => lifecycle,
                WotRefreshTask refresh => refresh.Request,
                WotAssetDeleteTask asset => asset.Request,
                _ => throw new ArgumentException("Prepare the exact WoT task first.", nameof(input))
            };
            if (prepared.Target != target || prepared.OperationId != operationId)
            {
                throw new ArgumentException("Prepare the exact WoT task first.", nameof(input));
            }
            WotTaskDefinition task = FindLifecycle(target, operationId);
            RegistryMutationScope scope = await RequireLifecycleAsync(
                context, target, task, prepared.Epoch, cancellationToken).ConfigureAwait(false);
            if (scope != prepared.Scope)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The WoT mutation scope changed.");
            }
            progress?.Report(new CompanionTaskProgress("Executing the reviewed WoT request."));
            if (operationId == "delete-asset")
            {
                return input is WotAssetDeleteTask asset
                    ? await ExecuteAssetDeleteAsync(context, asset, cancellationToken).ConfigureAwait(false)
                    : throw new ArgumentException("Prepare the exact asset deletion first.", nameof(input));
            }
            if (operationId == "refresh-resource")
            {
                return input is WotRefreshTask refresh
                    ? await ExecuteRefreshAsync(context, refresh, cancellationToken).ConfigureAwait(false)
                    : throw new ArgumentException("Prepare the exact Version refresh first.", nameof(input));
            }
            if (operationId == "create-asset")
            {
                var manager = new WotConnectivityClient(context.Session, target.NodeId, context.Telemetry);
                NodeId assetId = await manager.Proxy.CreateAssetAsync(prepared.Identifier, cancellationToken)
                    .ConfigureAwait(false);
                if (assetId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                        "The server returned no created asset identity. Do not retry automatically.");
                }
                try
                {
                    WotAssetClient asset = await manager.OpenAssetAsync(assetId, cancellationToken)
                        .ConfigureAwait(false);
                    using var stream = new MemoryStream(prepared.Document.Span.ToArray(), writable: false);
                    await asset.UploadThingDescriptionAsync(stream, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new CompanionOperationResult(
                        "Created the asset and committed its Thing Description. Server validation and " +
                        "configured endpoint/discovery policy remain authoritative.",
                        [new("Asset NodeId", Variant.From(asset.AssetId))]);
                }
                catch (Exception failure) when (failure is ServiceResultException or IOException or
                    OperationCanceledException or InvalidOperationException)
                {
                    using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        try
                        {
                            await manager.DeleteAssetAsync(assetId, cleanup.Token).ConfigureAwait(false);
                        }
                        catch (Exception cleanupFailure) when (
                            cleanupFailure is ServiceResultException or IOException or
                            OperationCanceledException or InvalidOperationException)
                        {
                            throw new AggregateException(
                                "Asset upload failed and cleanup of the newly created asset could not be confirmed.",
                                failure, cleanupFailure);
                        }
                    }
                    throw;
                }
            }
            if (operationId == "register-version")
            {
                await RequireAutoRefreshAsync(
                    context, target.NodeId, prepared.Enabled, cancellationToken).ConfigureAwait(false);
                var registry = new WotRegistryClient(context.Session, target.NodeId, context.Telemetry);
                (NodeId group, _) = await registry.Proxy.GetOrCreateGroupAsync(
                    prepared.Group, cancellationToken).ConfigureAwait(false);
                await RequireLifecycleGroupAsync(context, group, prepared.Group, cancellationToken)
                    .ConfigureAwait(false);
                ResourceRegistrationResult registered = await registry.RegisterResourceAsync(
                    group, prepared.Identifier, prepared.Document, prepared.Version, 4096, cancellationToken)
                    .ConfigureAwait(false);
                if (registered.ResourceNodeId.IsNull || registered.AssignedVersionId != prepared.Version)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                        "The returned WoT version differs from the reviewed request. Inspect before retrying.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new CompanionOperationResult(
                    "Committed a new immutable WoT version. It was not silently overwritten. " +
                    "The configured server AutoRefresh policy applies; select a default separately.",
                    [new("Version NodeId", Variant.From(registered.ResourceNodeId)),
                        new("Version ID", Variant.From(registered.AssignedVersionId))]);
            }
            WoTDocumentTypeClient document = target.TypeName == kThingModelKind
                ? new ThingModelFileTypeClient(context.Session, target.NodeId, context.Telemetry)
                : new ThingDescriptionFileTypeClient(context.Session, target.NodeId, context.Telemetry);
            switch (operationId)
            {
                case "set-enabled":
                    await document.SetEnabledAsync(prepared.Enabled, prepared.Epoch, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "select-default":
                    await document.SetDefaultVersionAsync(prepared.Version, prepared.Epoch, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "delete-document":
                    await document.DeleteAsync(prepared.Epoch, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw IndustrialCompanionAccess.Unsupported("The WoT lifecycle operation is unavailable.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                "The server accepted the reviewed epoch-bound WoT operation. Refresh for current materialization " +
                "state; acceptance alone is not proof that external resources were reached.",
                [new("Target NodeId", Variant.From(target.NodeId)),
                    new("Expected epoch", Variant.From(prepared.Epoch))]);
        }

        private static async ValueTask RequireAutoRefreshAsync(
            CompanionContext context, NodeId registry, bool accepted, CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> policy = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, registry, Wot.Namespaces.WotCon, ["AutoRefresh"], cancellationToken).ConfigureAwait(false);
            if (!policy[0].Value.TryGetValue(out bool autoRefresh))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "AutoRefresh policy is unavailable.");
            }
            if (autoRefresh && !accepted)
            {
                throw new InvalidOperationException("The server automatically refreshes uploaded content. " +
                    "Accept that effect explicitly or change server policy before uploading.");
            }
        }

        private static ValueTask RequireLifecycleGroupAsync(
            CompanionContext context, NodeId group, string groupId, CancellationToken cancellationToken)
        {
            return groupId == WotRegistryClient.ThingModelsGroupId
                ? RequireModelGroupAsync(context, group, cancellationToken)
                : IndustrialCompanionAccess.RequireTargetAsync(context,
                    new CompanionTarget("wot", group, "Thing Description group", "Thing Description group"), "wot",
                    [new IndustrialCompanionType(
                        Wot.ObjectTypeIds.ThingDescriptionGroupType, "Thing Description group")],
                    cancellationToken);
        }

        private static async ValueTask<ArrayOf<CompanionOperation>> WithLifecycleAsync(
            CompanionContext context, CompanionTarget target, ArrayOf<CompanionOperation> existing,
            CancellationToken cancellationToken)
        {
            var operations = existing.ToList();
            ArrayOf<WotTaskDefinition> tasks = LifecycleTasks(target);
            for (int i = 0; i < tasks.Count; i++)
            {
                WotTaskDefinition task = tasks[i];
                if (await IndustrialCompanionAccess.IsExecutableAsync(context, target.NodeId,
                    task.NamespaceUri, task.Method, cancellationToken).ConfigureAwait(false))
                {
                    operations.Add(task.Operation);
                }
            }
            return [.. operations];
        }

        private static async ValueTask<RegistryMutationScope> RequireLifecycleAsync(
            CompanionContext context, CompanionTarget target, WotTaskDefinition task, uint epoch,
            CancellationToken cancellationToken)
        {
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, "wot", s_types, cancellationToken).ConfigureAwait(false);
            RegistryMutationScope scope = target.TypeName == kManagementKind
                ? new RegistryMutationScope(string.Empty, "no server epoch",
                    task.Operation.Id == "create-asset"
                        ? "new asset on the selected manager" : "selected asset subtree")
                : await RegistryTaskForms.ReadScopeAsync(
                    context, target.NodeId, target.TypeName is kThingDescriptionKind or kThingModelKind,
                    task.Operation.Id is "set-enabled" or "select-default", cancellationToken).ConfigureAwait(false);
            if (target.TypeName != kManagementKind)
            {
                await RegistryTaskForms.RequireEpochAsync(
                    context, target.NodeId, epoch, scope.EpochName, cancellationToken)
                    .ConfigureAwait(false);
            }
            await RegistryTaskForms.RequireMethodAsync(
                context, target.NodeId, task.NamespaceUri, task.Method, cancellationToken).ConfigureAwait(false);
            return scope;
        }

        private static WotTaskDefinition FindLifecycle(CompanionTarget target, string id)
        {
            foreach (WotTaskDefinition task in LifecycleTasks(target))
            {
                if (task.Operation.Id == id)
                {
                    return task;
                }
            }
            throw IndustrialCompanionAccess.Unsupported("This lifecycle operation is not offered for the WoT target.");
        }

        private static ArrayOf<WotTaskDefinition> LifecycleTasks(CompanionTarget target)
        {
            return target.TypeName switch
            {
                kManagementKind =>
                [
                    new(RegistryTaskForms.Create("create-asset", "Create asset from a reviewed Thing Description",
                        [RegistryTaskForms.Identifier("id", "Asset name"), RegistryTaskForms.Document]),
                        "CreateAsset", Wot.Namespaces.WotCon),
                    new(RegistryTaskForms.Create("delete-asset", "Delete one reviewed asset subtree",
                        [new("asset", "Existing asset NodeId", BuiltInType.NodeId,
                            "The asset must belong to this manager; its current WoT file is pinned."),
                            RegistryTaskForms.DeleteConfirmation]), "DeleteAsset", Wot.Namespaces.WotCon)
                ],
                kRegistryKind =>
                [
                    new(RegistryTaskForms.Create("register-version", "Register a new WoT document version",
                        [RegistryTaskForms.Epoch, RegistryTaskForms.Identifier("group", "Group"),
                            RegistryTaskForms.Identifier("id", "Resource ID"),
                            RegistryTaskForms.Identifier("version", "New version ID"), RegistryTaskForms.Document,
                            new("acceptAutoRefresh", "Accept server AutoRefresh", BuiltInType.Boolean,
                                "Upload can materialize the document according to server policy.")]),
                        "GetOrCreateGroup", XRegistryWellKnown.XRegistryNamespaceUri),
                    new(RegistryTaskForms.Create("refresh-resource", "Refresh one existing WoT Version",
                        [RegistryTaskForms.Epoch,
                            new("generation", "Expected refresh generation", BuiltInType.UInt32,
                                "Nonzero current generation."),
                            RegistryTaskForms.Identifier("group", "Group"),
                            RegistryTaskForms.Identifier("id", "Resource ID"),
                            RegistryTaskForms.Identifier("version", "Existing version ID"),
                            new("acceptRefresh", "Accept materialization effects", BuiltInType.Boolean,
                                "Server endpoint policy applies; no Force, dependents or whole-registry refresh.")]),
                        "Refresh", Wot.Namespaces.WotCon)
                ],
                kThingDescriptionKind or kThingModelKind =>
                [
                    new(RegistryTaskForms.Create("set-enabled", "Set document enabled state",
                        [RegistryTaskForms.Epoch,
                            new("enabled", "Enabled", BuiltInType.Boolean, "Desired enabled state.")]),
                        "SetEnabled", Wot.Namespaces.WotCon),
                    new(RegistryTaskForms.Create("select-default", "Select an existing default version",
                        [RegistryTaskForms.Epoch, RegistryTaskForms.Identifier("version", "Existing version ID")]),
                        "SetDefaultVersion", Wot.Namespaces.WotCon),
                    new(RegistryTaskForms.Create("delete-document", "Delete document and its logical resource versions",
                        [RegistryTaskForms.Epoch, RegistryTaskForms.DeleteConfirmation]),
                        "Delete", XRegistryWellKnown.XRegistryNamespaceUri)
                ],
                _ => []
            };
        }

        private sealed record WotTaskDefinition(CompanionOperation Operation, string Method, string NamespaceUri);
    }
}
