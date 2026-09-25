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
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class RegistryCompanionProvider
    {
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId,
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, XRegistryWellKnown.XRegistryNamespaceUri, cancellationToken);
            cancellationToken = lifetime.Token;
            RegistryTaskDefinition task = FindTask(target, operationId);
            RequireFields(task.Operation, inputs);
            uint epoch = RegistryTaskForms.ExpectedEpoch(inputs);
            string identifier = operationId is "create-group" or "create-version"
                ? RegistryTaskForms.Id(inputs, "id") : string.Empty;
            string version = operationId == "create-version"
                ? RegistryTaskForms.Id(inputs, "version") : string.Empty;
            ByteString document = operationId == "create-version" ? RegistryTaskForms.Json(inputs) : default;
            if (operationId.StartsWith("delete-", StringComparison.Ordinal) &&
                !RegistryTaskForms.Boolean(inputs, "includeChildren"))
            {
                throw new ArgumentException("Confirm the full deletion scope before preparing.");
            }
            RegistryMutationScope scope = await CheckLifecycleAsync(
                context, target, task.Method, epoch, cancellationToken).ConfigureAwait(false);
            return new RegistryLifecycleTask(target, operationId, epoch, identifier, version, document, scope: scope);
        }

        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, XRegistryWellKnown.XRegistryNamespaceUri, cancellationToken);
            cancellationToken = lifetime.Token;
            if (input is not RegistryLifecycleTask prepared ||
                prepared.Target != target ||
                prepared.OperationId != operationId)
            {
                throw new ArgumentException("Prepare the exact registry lifecycle request first.", nameof(input));
            }
            RegistryTaskDefinition task = FindTask(target, operationId);
            RegistryMutationScope scope = await CheckLifecycleAsync(
                context, target, task.Method, prepared.Epoch, cancellationToken)
                .ConfigureAwait(false);
            if (scope != prepared.Scope)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The registry mutation scope changed.");
            }
            GenericXRegistryClient client = CreateClient(context, target.NodeId);
            progress?.Report(new CompanionTaskProgress("Calling the reviewed registry operation."));
            ArrayOf<CompanionValue> outcome;
            switch (operationId)
            {
                case "create-group":
                    NodeId group = await client.CreateGroupAsync(
                        target.NodeId, prepared.Identifier, cancellationToken).ConfigureAwait(false);
                    if (group.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                            "The server returned no created group identity. Do not retry automatically.");
                    }
                    outcome = [new("Group NodeId", Variant.From(group))];
                    break;
                case "create-version":
                    ResourceRegistrationResult version = await client.RegisterResourceAsync(
                        target.NodeId, prepared.Identifier, prepared.Document, prepared.Version, 4096,
                        cancellationToken).ConfigureAwait(false);
                    if (version.ResourceNodeId.IsNull || version.AssignedVersionId != prepared.Version)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                            "The returned version does not match the reviewed request. Inspect before retrying.");
                    }
                    outcome =
                    [
                        new("Version NodeId", Variant.From(version.ResourceNodeId)),
                        new("Version ID", Variant.From(version.AssignedVersionId))
                    ];
                    break;
                case "delete-group":
                    await client.DeleteGroupAsync(target.NodeId, prepared.Epoch, cancellationToken)
                        .ConfigureAwait(false);
                    outcome = [new("Requested group deletion", Variant.From(target.NodeId))];
                    break;
                case "delete-resource":
                    await client.DeleteResourceAsync(target.NodeId, prepared.Epoch, cancellationToken)
                        .ConfigureAwait(false);
                    outcome = [new("Requested resource/version deletion", Variant.From(target.NodeId))];
                    break;
                default:
                    throw IndustrialCompanionAccess.Unsupported("Unsupported registry lifecycle operation.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                "The server accepted the reviewed registry operation. Refresh to observe the current tree. " +
                "Creation does not overwrite existing versions; deletion enforces the server's expected epoch.",
                outcome);
        }

        private static async ValueTask<ArrayOf<CompanionOperation>> AddLifecycleOperationsAsync(
            CompanionContext context, CompanionTarget target, ArrayOf<CompanionOperation> existing,
            CancellationToken cancellationToken)
        {
            var operations = existing.ToList();
            ArrayOf<RegistryTaskDefinition> tasks = Tasks(target);
            for (int index = 0; index < tasks.Count; index++)
            {
                RegistryTaskDefinition task = tasks[index];
                if (await IndustrialCompanionAccess.IsExecutableAsync(context, target.NodeId,
                    XRegistryWellKnown.XRegistryNamespaceUri, task.Method, cancellationToken).ConfigureAwait(false))
                {
                    operations.Add(task.Operation);
                }
            }
            return [.. operations];
        }

        private static async ValueTask<RegistryMutationScope> CheckLifecycleAsync(
            CompanionContext context, CompanionTarget target, string method, uint epoch,
            CancellationToken cancellationToken)
        {
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, "xregistry", s_types, cancellationToken).ConfigureAwait(false);
            RegistryMutationScope scope = await RegistryTaskForms.ReadScopeAsync(
                context, target.NodeId, target.TypeName == kResourceKind, false, cancellationToken)
                    .ConfigureAwait(false);
            await RegistryTaskForms.RequireEpochAsync(
                context, target.NodeId, epoch, scope.EpochName, cancellationToken)
                .ConfigureAwait(false);
            await RegistryTaskForms.RequireMethodAsync(
                context, target.NodeId, XRegistryWellKnown.XRegistryNamespaceUri, method, cancellationToken)
                .ConfigureAwait(false);
            return scope;
        }

        private static RegistryTaskDefinition FindTask(CompanionTarget target, string id)
        {
            foreach (RegistryTaskDefinition task in Tasks(target))
            {
                if (task.Operation.Id == id)
                {
                    return task;
                }
            }
            throw IndustrialCompanionAccess.Unsupported("This lifecycle operation is not offered for the target.");
        }

        private static void RequireFields(CompanionOperation operation, ArrayOf<CompanionValue> inputs)
        {
            if (inputs.Count != operation.Inputs.Count)
            {
                throw new ArgumentException("Supply the exact registry form fields.", nameof(inputs));
            }
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Name != operation.Inputs[i].Name ||
                    !inputs[i].Value.TypeInfo.IsScalar ||
                    inputs[i].Value.TypeInfo.BuiltInType != operation.Inputs[i].DataType)
                {
                    throw new ArgumentException("Registry task fields have incorrect names or types.", nameof(inputs));
                }
            }
        }

        private static ArrayOf<RegistryTaskDefinition> Tasks(CompanionTarget target)
        {
            return target.TypeName switch
            {
                kRegistryKind =>
                [
                    new(RegistryTaskForms.Create("create-group", "Create a registry group",
                        [RegistryTaskForms.Epoch, RegistryTaskForms.Identifier("id", "Group ID")]), "CreateGroup")
                ],
                kGroupKind =>
                [
                    new(RegistryTaskForms.Create("create-version", "Create a new immutable document version",
                        [RegistryTaskForms.Epoch, RegistryTaskForms.Identifier("id", "Resource ID"),
                            RegistryTaskForms.Identifier("version", "New version ID"), RegistryTaskForms.Document]),
                        "CreateResource"),
                    new(RegistryTaskForms.Create("delete-group", "Delete group and all resources/versions",
                        [RegistryTaskForms.Epoch, RegistryTaskForms.DeleteConfirmation]), "Delete")
                ],
                kResourceKind =>
                [
                    new(RegistryTaskForms.Create("delete-resource", "Delete selected resource/version",
                        [RegistryTaskForms.Epoch, RegistryTaskForms.DeleteConfirmation]), "Delete")
                ],
                _ => []
            };
        }

        private sealed record RegistryTaskDefinition(CompanionOperation Operation, string Method);
    }
}
