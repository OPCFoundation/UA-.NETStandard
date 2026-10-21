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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Programs
{
    public sealed partial class ProgramNodeManager
    {
        /// <inheritdoc/>
        public override async ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (item is null)
            {
                return (new ServiceResult(StatusCodes.BadNothingToDo), NodeId.Null);
            }
            NodeId typeId = ExpandedNodeId.ToNodeId(item.TypeDefinition, Server.NamespaceUris);
            bool enabled = false;
            foreach (ExpandedNodeId enabledType in m_addNodesTypes)
            {
                enabled |= ExpandedNodeId.ToNodeId(enabledType, Server.NamespaceUris) == typeId;
            }
            if (!enabled)
            {
                return (new ServiceResult(StatusCodes.BadUserAccessDenied), NodeId.Null);
            }
            if (item.NodeClass != NodeClass.Object)
            {
                return (new ServiceResult(StatusCodes.BadNodeClassInvalid), NodeId.Null);
            }
            if (item.ReferenceTypeId != ReferenceTypeIds.Organizes)
            {
                return (new ServiceResult(StatusCodes.BadReferenceNotAllowed), NodeId.Null);
            }
            if (item.ParentNodeId.ServerIndex != 0 || item.TypeDefinition.ServerIndex != 0 ||
                item.RequestedNewNodeId.ServerIndex != 0)
            {
                return (new ServiceResult(StatusCodes.BadNodeIdInvalid), NodeId.Null);
            }
            NodeId requestedId = ExpandedNodeId.ToNodeId(item.RequestedNewNodeId, Server.NamespaceUris);
            if (!item.RequestedNewNodeId.IsNull &&
                (requestedId.IsNull || requestedId.NamespaceIndex != NamespaceIndex))
            {
                return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
            }
            // Policy, permissions and notifier flags come from the registered model, not client attributes.
            ObjectAttributes? attributes = null;
            const uint allowedAttributes = (uint)(NodeAttributesMask.DisplayName | NodeAttributesMask.Description);
            if (!item.NodeAttributes.IsNull &&
                (!item.NodeAttributes.TryGetValue(out attributes) ||
                    attributes is null || (attributes.SpecifiedAttributes & ~allowedAttributes) != 0))
            {
                return (new ServiceResult(StatusCodes.BadNodeAttributesInvalid), NodeId.Null);
            }
            try
            {
                ProgramRuntime runtime = await RegisterInvocationAsync(
                    SystemContext.Copy(context),
                    typeId,
                    ExpandedNodeId.ToNodeId(item.ParentNodeId, Server.NamespaceUris),
                    item.BrowseName,
                    null,
                    requestedId,
                    true,
                    cancellationToken,
                    attributes).ConfigureAwait(false);
                return (ServiceResult.Good, runtime.Node.NodeId);
            }
            catch (ServiceResultException exception)
            {
                return (new ServiceResult(exception), NodeId.Null);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<ServiceResult> DeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (item is null)
            {
                return StatusCodes.BadNothingToDo;
            }
            using NodeManagerOperation operation = BeginNodeManagerOperation();
            await m_lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // No type, folder, descendant or ancestor removal can bypass invocation policy.
                if (!m_invocations.TryGetValue(item.NodeId, out ProgramRuntime? runtime))
                {
                    return Find(item.NodeId) is null
                        ? StatusCodes.BadNodeIdUnknown
                        : StatusCodes.BadUserAccessDenied;
                }
                (bool handled, ServiceResult? permission) = await TryValidatePermissionsAsync(
                    context,
                    new NodeHandle { NodeId = item.NodeId, Node = runtime.Node, Validated = true },
                    PermissionType.DeleteNode,
                    null,
                    true,
                    m_programLogger,
                    true,
                    cancellationToken).ConfigureAwait(false);
                if (!handled || ServiceResult.IsBad(permission))
                {
                    return permission ?? new ServiceResult(StatusCodes.BadUserAccessDenied);
                }
                return await RemoveInvocationAsync(runtime, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_lifecycle.Release();
            }
        }

        /// <inheritdoc/>
        public override ValueTask<ServiceResult> AddReferenceAsync(
            OperationContext context,
            AddReferencesItem item,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied));
        }

        /// <inheritdoc/>
        public override ValueTask<ServiceResult> DeleteReferenceAsync(
            OperationContext context,
            DeleteReferencesItem item,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied));
        }

        private TypeRegistration ConfigureType(BaseObjectTypeState type, ProgramDefinition definition)
        {
            SetTypeProperty(type, BrowseNames.Creatable, definition.Creatable, DataTypeIds.Boolean);
            SetTypeProperty(type, BrowseNames.MaxInstanceCount, definition.MaxInstanceCount, DataTypeIds.UInt32);
            SetTypeProperty(type, BrowseNames.MaxRecycleCount, definition.MaxRecycleCount, DataTypeIds.UInt32);
            PropertyState count = SetTypeProperty(type, BrowseNames.InstanceCount, 0u, DataTypeIds.UInt32);
            var children = new List<BaseInstanceState>();
            type.GetChildren(SystemContext, children);
            MethodState? create = null;
            foreach (BaseInstanceState child in children)
            {
                if (child.BrowseName.Name == Opc.Ua.Programs.BrowseNames.Create && child is MethodState method)
                {
                    if (create is not null)
                    {
                        throw new ArgumentException("The concrete Program type has ambiguous Create methods.");
                    }
                    create = method;
                }
            }
            if (create is null)
            {
                if (definition.CreationAdapter is not null)
                {
                    throw new ArgumentException("An application Create adapter requires a declared Create method.");
                }
                create = new MethodState(type)
                {
                    BrowseName = new QualifiedName(Opc.Ua.Programs.BrowseNames.Create,
                        Server.NamespaceUris.GetIndexOrAppend(Opc.Ua.Programs.Namespaces.OpcUaPrograms)),
                    DisplayName = new LocalizedText(Opc.Ua.Programs.BrowseNames.Create),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    MethodDeclarationId = ExpandedNodeId.ToNodeId(
                        Opc.Ua.Programs.MethodIds.ManagedProgramType_Create, Server.NamespaceUris)
                };
                create.InputArguments = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(
                    create,
                    [
                        new Argument
                        {
                            Name = "ParentId", DataType = DataTypeIds.NodeId, ValueRank = ValueRanks.Scalar
                        },
                        new Argument
                        {
                            Name = "BrowseName", DataType = DataTypeIds.QualifiedName, ValueRank = ValueRanks.Scalar
                        }
                    ]);
                create.InputArguments.BrowseName = new QualifiedName(BrowseNames.InputArguments);
                create.OutputArguments = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(
                    create,
                    [
                        new Argument
                        {
                            Name = "InvocationId", DataType = DataTypeIds.NodeId, ValueRank = ValueRanks.Scalar
                        }
                    ]);
                create.OutputArguments.BrowseName = new QualifiedName(BrowseNames.OutputArguments);
                create.Create(SystemContext, default, create.BrowseName, default, true);
                type.AddChild(create);
            }
            if (definition.CreationAdapter is null &&
                (create.InputArguments?.Value is not { Count: 2 } inputs ||
                inputs[0].DataType != DataTypeIds.NodeId || inputs[0].ValueRank != ValueRanks.Scalar ||
                inputs[1].DataType != DataTypeIds.QualifiedName || inputs[1].ValueRank != ValueRanks.Scalar ||
                create.OutputArguments?.Value is not { Count: 1 } outputs ||
                outputs[0].DataType != DataTypeIds.NodeId || outputs[0].ValueRank != ValueRanks.Scalar))
            {
                throw new ArgumentException(
                    "The Program Create method must declare ParentId, BrowseName and InvocationId.");
            }
            create.Executable = definition.Creatable;
            create.UserExecutable = definition.Creatable;
            create.OnCallMethodWithResultAsync = (context, method, objectId, inputs, cancellationToken) =>
                CreateFromMethodAsync(context, type.NodeId, method, objectId, inputs, cancellationToken);
            return new TypeRegistration(definition, count);
        }

        private PropertyState SetTypeProperty(
            BaseObjectTypeState type,
            string name,
            Variant value,
            NodeId dataType)
        {
            var browseName = new QualifiedName(name);
            PropertyState property;
            if (type.FindChildWithQualifiedName(SystemContext, browseName) is PropertyState existing)
            {
                property = existing;
            }
            else
            {
                property = new PropertyState(type);
                property.Create(SystemContext, default, browseName, default, true);
                type.AddChild(property);
            }
            property.DataType = dataType;
            property.ValueRank = ValueRanks.Scalar;
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            property.WrappedValue = value;
            return property;
        }

        private async ValueTask<MethodInvocationResult> CreateFromMethodAsync(
            ISystemContext context,
            NodeId typeId,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            CancellationToken cancellationToken)
        {
            if (objectId != typeId)
            {
                return new MethodInvocationResult(new ServiceResult(StatusCodes.BadMethodInvalid));
            }
            if (context is not ServerSystemContext serverContext || serverContext.OperationContext is null)
            {
                return new MethodInvocationResult(new ServiceResult(StatusCodes.BadUserAccessDenied));
            }
            if (!m_types.TryGetValue(typeId, out TypeRegistration? registration))
            {
                return new MethodInvocationResult(new ServiceResult(StatusCodes.BadNodeIdUnknown));
            }
            try
            {
                IProgramCreationAdapter? adapter = registration.Definition.CreationAdapter;
                ProgramCreationRequest request;
                if (adapter is null)
                {
                    if (inputs.Count != 2 || !inputs[0].TryGetValue(out NodeId parentId) ||
                        !inputs[1].TryGetValue(out QualifiedName browseName))
                    {
                        return new MethodInvocationResult(new ServiceResult(StatusCodes.BadInvalidArgument));
                    }
                    request = new ProgramCreationRequest(parentId, browseName);
                }
                else
                {
                    request = await adapter.ParseAsync(
                        new ProgramCreationContext(context, typeId, RootId, NamespaceIndex),
                        inputs, cancellationToken).ConfigureAwait(false)
                        ?? throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                            "The Program Create adapter returned no request.");
                }
                ProgramRuntime runtime = await RegisterInvocationAsync(
                    serverContext, typeId, request.ParentId, request.BrowseName, null, NodeId.Null,
                    true, cancellationToken, creationArguments: request.ProviderArguments, activate: adapter is null)
                    .ConfigureAwait(false);
                try
                {
                    ArrayOf<Variant> outputs = adapter is null
                        ? [new Variant(runtime.Node.NodeId)]
                        : await adapter.CompleteAsync(runtime.Node.NodeId, inputs, cancellationToken)
                            .ConfigureAwait(false);
                    ArrayOf<Argument> declarations = method.OutputArguments?.Value ?? default;
                    if (outputs.Count != declarations.Count)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                            "The Program Create adapter returned an incomplete output array.");
                    }
                    for (int index = 0; index < outputs.Count; index++)
                    {
                        Argument declaration = declarations[index];
                        if (TypeInfo.IsInstanceOfDataType(outputs[index], declaration.DataType,
                            declaration.ValueRank, context.NamespaceUris, context.TypeTable).IsUnknown)
                        {
                            throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                                "A Program Create output does not match its declaration.");
                        }
                    }
                    if (adapter is not null)
                    {
                        await runtime.ActivateAsync(cancellationToken).ConfigureAwait(false);
                    }
                    return new MethodInvocationResult(ServiceResult.Good, outputs);
                }
                catch
                {
                    await m_lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        ServiceResult removed = await RemoveInvocationAsync(
                            runtime, false, CancellationToken.None).ConfigureAwait(false);
                        if (ServiceResult.IsBad(removed) && removed.StatusCode != StatusCodes.BadNodeIdUnknown)
                        {
                            throw new ServiceResultException(removed);
                        }
                    }
                    finally
                    {
                        m_lifecycle.Release();
                    }
                    throw;
                }
            }
            catch (ServiceResultException exception)
            {
                return new MethodInvocationResult(new ServiceResult(exception));
            }
        }

        private async ValueTask<ServiceResult> ValidateCreationPermissionAsync(
            ServerSystemContext context,
            CancellationToken cancellationToken)
        {
            if (context.OperationContext is null || m_root is null)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // the namespace defaults are read for the caller; a failed read or decode denies.
            NamespaceDefaultPermissions defaults = await NamespaceDefaultPermissionReader.ReadAsync(
                Server, context, NamespaceIndex, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(defaults.Status))
            {
                return new ServiceResult(defaults.Status);
            }

            var metadata = new NodeMetadata(m_root, new NodeId(0u, NamespaceIndex))
            {
                DefaultRolePermissions = defaults.DefaultRolePermissions,
                DefaultUserRolePermissions = defaults.DefaultUserRolePermissions,
                DefaultAccessRestrictions = defaults.DefaultAccessRestrictions
            };
            ServiceResult permission = MasterNodeManager.ValidateRolePermissions(
                context.OperationContext, metadata, PermissionType.AddNode);
            if (ServiceResult.IsBad(permission))
            {
                return permission;
            }

            permission = MasterNodeManager.ValidateAccessRestrictions(
                context.OperationContext, metadata, PermissionType.AddNode);
            if (ServiceResult.IsBad(permission))
            {
                return permission;
            }

            (bool handled, ServiceResult? result) = await TryValidatePermissionsAsync(
                context.OperationContext,
                new NodeHandle { NodeId = m_root.NodeId, Node = m_root, Validated = true },
                PermissionType.AddReference,
                null,
                true,
                m_programLogger,
                true,
                cancellationToken).ConfigureAwait(false);
            return handled ? result! : new ServiceResult(StatusCodes.BadUserAccessDenied);
        }
    }
}
