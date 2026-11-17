/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Node-management surface of <see cref="MasterNodeManager"/>: the AddNodes,
    /// DeleteNodes, AddReferences and DeleteReferences services (OPC 10000-4 §5.8)
    /// plus the NodeId-based cross-manager reference plumbing, with per-item routing,
    /// authorization, and rollback compensation.
    /// Code in this file acquires <c>m_dynamicMutationSemaphore</c> for the four TAP
    /// node-mutation services so address-space mutation serializes with NodeManager
    /// lifecycle operations, and it reads the routing snapshot for dispatch. It must
    /// not acquire the startup/shutdown semaphore, touch retired-generation or
    /// preparing-NodeManager state, or use <see cref="IDynamicNodeManagerHost"/>.
    /// </summary>
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Adds the references to the target.
        /// </summary>
        [Obsolete("Use AddReferencesAsync instead.")]
        public virtual void AddReferences(NodeId sourceId, IList<IReference> references)
        {
            AddReferencesAsync(sourceId, references).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual async ValueTask AddReferencesAsync(NodeId sourceId,
            IList<IReference> references,
            CancellationToken cancellationToken = default)
        {
            // find source node.
            (object? sourceHandle, IAsyncNodeManager? nodeManager) = await GetManagerHandleAsync(sourceId, cancellationToken)
                .ConfigureAwait(false);
            if (sourceHandle == null)
            {
                return;
            }

            var map = new Dictionary<NodeId, IList<IReference>> { { sourceId, references } };
            await nodeManager!.AddReferencesAsync(map, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the references to the target.
        /// </summary>
        [Obsolete("Use DeleteReferencesAsync")]
        public virtual void DeleteReferences(NodeId targetId, IList<IReference> references)
        {
            DeleteReferencesAsync(targetId, references).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual async ValueTask DeleteReferencesAsync(NodeId targetId,
            IList<IReference> references,
            CancellationToken cancellationToken = default)
        {
            foreach (ReferenceNode reference in references.OfType<ReferenceNode>())
            {
                var sourceId = ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris);

                // find source node.
                (object? sourceHandle, IAsyncNodeManager? nodeManager) = await GetManagerHandleAsync(sourceId, cancellationToken)
                .ConfigureAwait(false);

                if (sourceHandle == null)
                {
                    continue;
                }

                // delete the reference.
                await nodeManager!.DeleteReferenceAsync(
                        sourceHandle,
                        reference.ReferenceTypeId,
                        !reference.IsInverse,
                        targetId,
                        false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete("Use RemoveReferencesAsync instead.")]
        public void RemoveReferences(List<LocalReference> referencesToRemove)
        {
            RemoveReferencesAsync(referencesToRemove).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async ValueTask RemoveReferencesAsync(
            List<LocalReference> referencesToRemove,
            CancellationToken cancellationToken = default)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.DeleteReferences, RequestLifetime.None);
            context.PinRouting(m_nodeManagers);

            for (int ii = 0; ii < referencesToRemove.Count; ii++)
            {
                LocalReference reference = referencesToRemove[ii];

                // find source node.
                (object? sourceHandle, IAsyncNodeManager? nodeManager) = await m_serviceDispatch.GetManagerHandleAsync(
                        context,
                        reference.SourceId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (sourceHandle == null)
                {
                    continue;
                }

                // delete the reference.
                ServiceResult result = await nodeManager!.DeleteReferenceAsync(
                        sourceHandle,
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        reference.TargetId,
                        false,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
            }
        }

        /// <inheritdoc/>
        public virtual ValueTask<(ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            AddNodesAsync(
                OperationContext context,
                ArrayOf<AddNodesItem> nodesToAdd,
                CancellationToken cancellationToken = default)
        {
            ValueTask<(ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)> response =
                AddNodesCoreAsync(context, nodesToAdd, cancellationToken);
            m_hostBoundary?.AddNodesDispatched();
            return response;
        }

        /// <summary>
        /// Dispatches AddNodes under address-space mutation admission.
        /// </summary>
        private async ValueTask<(ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            AddNodesCoreAsync(
                OperationContext context,
                ArrayOf<AddNodesItem> nodesToAdd,
                CancellationToken cancellationToken)
        {
            bool isLifecycleDecision = m_currentLifecycleDecision.Value is { IsCompleted: false };
            if (isLifecycleDecision &&
                TryCreatePreRoutingAddNodesResponse(context, nodesToAdd, out var decisionResponse))
            {
                return decisionResponse;
            }
            ThrowIfNodeManagementFromDecision(isLifecycleDecision);
            bool mutationAdmissionHeld = false;
            bool invalidatePermissionCache = false;
            if (HasActiveCallbackPermit(
                    CaptureLifecycleCallbackPermit(),
                    NodeManagerAdmissionCapability.AddressSpaceMutation))
            {
                await AcquireDynamicMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                while (true)
                {
                    await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    Task? decisionWindow = Volatile.Read(ref m_lifecycleDecisionCompletion)?.Task;
                    if (decisionWindow is null)
                    {
                        mutationAdmissionHeld = true;
                        break;
                    }
                    try
                    {
                        if (TryCreatePreRoutingAddNodesResponse(context, nodesToAdd, out var preRoutingResponse))
                        {
                            return preRoutingResponse;
                        }
                    }
                    finally
                    {
                        m_dynamicMutationSemaphore.Release();
                    }
                    await decisionWindow.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            try
            {
                using OperationContext.RoutingScope requestRouting = m_serviceDispatch.CaptureRouting(context);

                var results = new AddNodesResult[nodesToAdd.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToAdd.Count];
                bool anyDiagnostics = false;
                invalidatePermissionCache = nodesToAdd.Count > 0;
                if (invalidatePermissionCache)
                {
                    NodeState.InvalidateRolePermissionsCache();
                }

                for (int ii = 0; ii < nodesToAdd.Count; ii++)
                {
                    AddNodesItem item = nodesToAdd[ii];
                    ServiceResult result;
                    NodeId addedNodeId = NodeId.Null;
                    try
                    {
                        (result, addedNodeId) = await DispatchAddNodeAsync(
                            context, item, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        result = CreateNodeManagementError(exception);
                    }

                    results[ii] = new AddNodesResult
                    {
                        StatusCode = result.StatusCode,
                        AddedNodeId = addedNodeId
                    };
                    if (ServiceResult.IsBad(result) &&
                        (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            result,
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
            }
            finally
            {
                if (invalidatePermissionCache)
                {
                    // Retire verdicts resolved while the address-space mutation was in progress.
                    NodeState.InvalidateRolePermissionsCache();
                }
                if (mutationAdmissionHeld)
                {
                    m_dynamicMutationSemaphore.Release();
                }
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            DeleteNodesAsync(
                OperationContext context,
                ArrayOf<DeleteNodesItem> nodesToDelete,
                CancellationToken cancellationToken = default)
        {
            bool mutationAdmissionHeld = await AcquireDynamicMutationAdmissionAsync(cancellationToken)
                .ConfigureAwait(false);
            bool invalidatePermissionCache = false;
            try
            {
                using OperationContext.RoutingScope requestRouting = m_serviceDispatch.CaptureRouting(context);

                var results = new StatusCode[nodesToDelete.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToDelete.Count];
                bool anyDiagnostics = false;
                invalidatePermissionCache = nodesToDelete.Count > 0;
                if (invalidatePermissionCache)
                {
                    NodeState.InvalidateRolePermissionsCache();
                }

                for (int ii = 0; ii < nodesToDelete.Count; ii++)
                {
                    DeleteNodesItem item = nodesToDelete[ii];
                    ServiceResult result;
                    try
                    {
                        result = await DispatchDeleteNodeAsync(
                            context, item, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        result = CreateNodeManagementError(exception);
                    }

                    results[ii] = result.StatusCode;
                    if (ServiceResult.IsBad(result) &&
                        (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            result,
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
            }
            finally
            {
                if (invalidatePermissionCache)
                {
                    // Retire verdicts resolved while the address-space mutation was in progress.
                    NodeState.InvalidateRolePermissionsCache();
                }
                if (mutationAdmissionHeld)
                {
                    m_dynamicMutationSemaphore.Release();
                }
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            AddReferencesAsync(
                OperationContext context,
                ArrayOf<AddReferencesItem> referencesToAdd,
                CancellationToken cancellationToken = default)
        {
            await WaitForLifecycleDecisionAsync(cancellationToken).ConfigureAwait(false);
            using OperationContext.RoutingScope requestRouting = m_serviceDispatch.CaptureRouting(context);

            var results = new StatusCode[referencesToAdd.Count];
            var diagnosticInfos = new DiagnosticInfo[referencesToAdd.Count];
            bool anyDiagnostics = false;

            for (int ii = 0; ii < referencesToAdd.Count; ii++)
            {
                AddReferencesItem item = referencesToAdd[ii];
                ServiceResult result;
                try
                {
                    result = await DispatchAddReferenceAsync(
                        context, item, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    result = CreateNodeManagementError(exception);
                }

                results[ii] = result.StatusCode;

                if (ServiceResult.IsBad(result) &&
                    (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    anyDiagnostics = true;
                    diagnosticInfos[ii] = new DiagnosticInfo(
                        result,
                        context.DiagnosticsMask,
                        false,
                        context.StringTable,
                        m_logger);
                }
            }

            return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            DeleteReferencesAsync(
                OperationContext context,
                ArrayOf<DeleteReferencesItem> referencesToDelete,
                CancellationToken cancellationToken = default)
        {
            await WaitForLifecycleDecisionAsync(cancellationToken).ConfigureAwait(false);
            using OperationContext.RoutingScope requestRouting = m_serviceDispatch.CaptureRouting(context);

            var results = new StatusCode[referencesToDelete.Count];
            var diagnosticInfos = new DiagnosticInfo[referencesToDelete.Count];
            bool anyDiagnostics = false;

            for (int ii = 0; ii < referencesToDelete.Count; ii++)
            {
                DeleteReferencesItem item = referencesToDelete[ii];
                ServiceResult result;
                try
                {
                    result = await DispatchDeleteReferenceAsync(
                        context, item, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    result = CreateNodeManagementError(exception);
                }

                results[ii] = result.StatusCode;

                if (ServiceResult.IsBad(result) &&
                    (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    anyDiagnostics = true;
                    diagnosticInfos[ii] = new DiagnosticInfo(
                        result,
                        context.DiagnosticsMask,
                        false,
                        context.StringTable,
                        m_logger);
                }
            }

            return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
        }

        private ServiceResult CreateNodeManagementError(Exception exception)
        {
            m_logger.NodeManagementOperationFailed(exception);
            return exception is ServiceResultException serviceException
                ? new ServiceResult(serviceException)
                : ServiceResult.Create(exception, StatusCodes.BadUnexpectedError,
                    "The node manager failed a node-management operation.");
        }

        private bool TryCreatePreRoutingAddNodesResponse(
            OperationContext context,
            ArrayOf<AddNodesItem> nodesToAdd,
            out (ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) response)
        {
            if (nodesToAdd.Count == 0)
            {
                response = default;
                return false;
            }

            for (int index = 0; index < nodesToAdd.Count; index++)
            {
                if (GetPreRoutingAddNodeStatus(nodesToAdd[index]) == StatusCodes.Good)
                {
                    response = default;
                    return false;
                }
            }

            var results = new AddNodesResult[nodesToAdd.Count];
            var diagnosticInfos = new DiagnosticInfo[nodesToAdd.Count];
            bool anyDiagnostics = false;
            for (int index = 0; index < nodesToAdd.Count; index++)
            {
                ServiceResult result = new(GetPreRoutingAddNodeStatus(nodesToAdd[index]));
                results[index] = new AddNodesResult
                {
                    StatusCode = result.StatusCode,
                    AddedNodeId = NodeId.Null
                };
                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    anyDiagnostics = true;
                    diagnosticInfos[index] = new DiagnosticInfo(
                        result,
                        context.DiagnosticsMask,
                        false,
                        context.StringTable,
                        m_logger);
                }
            }

            response = (
                results.ToArrayOf(),
                anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
            return true;
        }

        private static StatusCode GetPreRoutingAddNodeStatus(AddNodesItem item)
        {
            if (item is null)
            {
                return StatusCodes.BadNothingToDo;
            }

            if (item.BrowseName.IsNull || string.IsNullOrEmpty(item.BrowseName.Name))
            {
                return StatusCodes.BadBrowseNameInvalid;
            }

            return item.ParentNodeId.IsNull
                ? StatusCodes.BadParentNodeIdInvalid
                : StatusCodes.Good;
        }

        /// <summary>
        /// Returns true when the ReferenceType is abstract. No Reference of an abstract
        /// ReferenceType shall exist (Part 3 5.3.1), so AddNodes and AddReferences reject it.
        /// </summary>
        private async ValueTask<bool> IsAbstractReferenceTypeAsync(
            OperationContext context,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            return await m_serviceDispatch.FindNodeInAddressSpaceAsync(context, referenceTypeId, cancellationToken)
                .ConfigureAwait(false) is ReferenceTypeState { IsAbstract: true };
        }

        private async ValueTask<(ServiceResult result, NodeId addedNodeId)> DispatchAddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken)
        {
            StatusCode preRoutingStatus = GetPreRoutingAddNodeStatus(item);
            if (preRoutingStatus != StatusCodes.Good)
            {
                return (new ServiceResult(preRoutingStatus), NodeId.Null);
            }

            if (item.ReferenceTypeId.IsNull ||
                !Server.TypeTree.IsKnown(item.ReferenceTypeId) ||
                !Server.TypeTree.IsTypeOf(item.ReferenceTypeId, ReferenceTypeIds.References))
            {
                return (new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid), NodeId.Null);
            }

            if (await IsAbstractReferenceTypeAsync(context, item.ReferenceTypeId, cancellationToken)
                .ConfigureAwait(false))
            {
                return (new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid), NodeId.Null);
            }

            if (!Server.TypeTree.IsTypeOf(
                    item.ReferenceTypeId,
                    ReferenceTypeIds.HierarchicalReferences))
            {
                return (new ServiceResult(StatusCodes.BadReferenceNotAllowed), NodeId.Null);
            }

            var parentNodeId = ExpandedNodeId.ToNodeId(item.ParentNodeId, Server.NamespaceUris);
            if (parentNodeId.IsNull)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            bool hasRequestedNodeId = !item.RequestedNewNodeId.IsNull;
            ushort targetNamespaceIndex;
            IAsyncNodeManager? nodeManagement = null;
            if (hasRequestedNodeId)
            {
                var requestedNodeId = ExpandedNodeId.ToNodeId(
                    item.RequestedNewNodeId, Server.NamespaceUris);
                if (requestedNodeId.IsNull)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }

                targetNamespaceIndex = requestedNodeId.NamespaceIndex;

                if (!NamespaceManagers.TryGetValue(
                        targetNamespaceIndex,
                        out IReadOnlyList<IAsyncNodeManager>? namespaceOwners) ||
                    namespaceOwners.Count == 0)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }

                nodeManagement = FindNodeManagementOwner(targetNamespaceIndex);
                if (nodeManagement == null)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }
            }
            else
            {
                targetNamespaceIndex = item.BrowseName.NamespaceIndex;
            }

            (object? parentHandle, IAsyncNodeManager? parentOwner) =
                await m_serviceDispatch.GetManagerHandleAsync(context, parentNodeId, cancellationToken)
                    .ConfigureAwait(false);
            if (parentHandle == null || parentOwner == null)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            NodeMetadata? parentMetadata;
            try
            {
                parentMetadata = await parentOwner.GetNodeMetadataAsync(
                    context,
                    parentHandle,
                    BrowseResultMask.NodeClass,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return (new ServiceResult(ex), NodeId.Null);
            }

            if (parentMetadata == null)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            if (!IsReferenceAllowedForNodeClasses(
                    item.ReferenceTypeId,
                    parentMetadata.NodeClass,
                    item.NodeClass))
            {
                return (new ServiceResult(StatusCodes.BadReferenceNotAllowed), NodeId.Null);
            }

            if (nodeManagement == null)
            {
                if (!NamespaceManagers.TryGetValue(
                        targetNamespaceIndex,
                        out IReadOnlyList<IAsyncNodeManager>? namespaceOwners) ||
                    namespaceOwners.Count == 0)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }

                nodeManagement = FindNodeManagementOwner(targetNamespaceIndex);
                if (nodeManagement == null)
                {
                    return (new ServiceResult(StatusCodes.BadUserAccessDenied), NodeId.Null);
                }
            }

            ServiceResult permissionResult = await ValidateAddNodeNamespacePermissionAsync(
                context,
                targetNamespaceIndex,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return (permissionResult, NodeId.Null);
            }

            permissionResult = await ValidatePermissionsAsync(
                context,
                parentOwner,
                parentHandle,
                PermissionType.AddReference,
                null,
                permissionsOnly: true,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return (permissionResult, NodeId.Null);
            }

            try
            {
                await using var callbackPermit = new NodeManagerCallbackPermit(
                    m_lifecycleCallbackPermit,
                    NodeManagerAdmissionCapability.AddressSpaceMutation).ConfigureAwait(false);
                return await nodeManagement.AddNodeAsync(context, item, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return (new ServiceResult(ex), NodeId.Null);
            }
        }

        private async ValueTask<ServiceResult> DispatchDeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo);
            }

            if (item.NodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadNodeIdInvalid);
            }

            (object? handle, IAsyncNodeManager? owner) =
                await m_serviceDispatch.GetManagerHandleAsync(context, item.NodeId, cancellationToken)
                    .ConfigureAwait(false);
            if (handle == null || owner == null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }

            if (!owner.AllowNodeManagement)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }

            ServiceResult permissionResult = await ValidatePermissionsAsync(
                context,
                owner,
                handle,
                PermissionType.DeleteNode,
                null,
                permissionsOnly: true,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return permissionResult;
            }

            try
            {
                await using var callbackPermit = new NodeManagerCallbackPermit(
                    m_lifecycleCallbackPermit,
                    NodeManagerAdmissionCapability.AddressSpaceMutation).ConfigureAwait(false);
                return await owner.DeleteNodeAsync(context, item, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }
        }

        private async ValueTask<ServiceResult> DispatchAddReferenceAsync(
            OperationContext context,
            AddReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo);
            }

            if (item.SourceNodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (item.TargetNodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
            }

            if (item.ReferenceTypeId.IsNull ||
                !Server.TypeTree.IsKnown(item.ReferenceTypeId) ||
                !Server.TypeTree.IsTypeOf(item.ReferenceTypeId, ReferenceTypeIds.References))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            if (await IsAbstractReferenceTypeAsync(context, item.ReferenceTypeId, cancellationToken)
                .ConfigureAwait(false))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            (object? sourceHandle, IAsyncNodeManager? sourceOwner) =
                await m_serviceDispatch.GetManagerHandleAsync(context, item.SourceNodeId, cancellationToken)
                    .ConfigureAwait(false);
            if (sourceHandle == null || sourceOwner == null)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (!sourceOwner.AllowNodeManagement)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }

            (ServiceResult permissionResult, NodeMetadata? sourceMetadata) =
                await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                    context,
                    sourceOwner,
                    sourceHandle,
                    PermissionType.AddReference,
                    null,
                    permissionsOnly: true,
                    metadataRequired: true,
                    cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return permissionResult;
            }

            IAsyncNodeManager? targetOwner = null;
            NodeMetadata? targetMetadata = null;
            object? targetHandle = null;

            if (TryGetExplicitLocalTargetNodeId(
                item.TargetServerUri,
                item.TargetNodeId,
                out NodeId targetNodeId))
            {
                (targetHandle, targetOwner) = await m_serviceDispatch.GetManagerHandleAsync(
                    context,
                    targetNodeId,
                    cancellationToken).ConfigureAwait(false);
                if (targetHandle == null || targetOwner == null)
                {
                    return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
                }

                if (!ReferenceEquals(targetOwner, sourceOwner) &&
                    !targetOwner.AllowNodeManagement)
                {
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                }

                (permissionResult, targetMetadata) =
                    await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                        context,
                        targetOwner,
                        targetHandle,
                        PermissionType.AddReference,
                        null,
                        permissionsOnly: true,
                        metadataRequired: true,
                        cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(permissionResult))
                {
                    return permissionResult;
                }
            }

            bool localTarget = targetOwner != null;

            _ =
                localTarget &&
                !ReferenceEquals(targetOwner, sourceOwner);
            if (localTarget &&
                (sourceMetadata == null || sourceMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }
            if (localTarget &&
                (targetMetadata == null || targetMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
            }

            // The data-model checks read the References the mutation changes, so concurrent
            // requests must not both pass a check (e.g. two HasTypeDefinition targets).
            await m_addReferenceSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await AddCheckedReferenceAsync(
                    context,
                    item,
                    sourceOwner,
                    sourceHandle,
                    sourceMetadata,
                    localTarget ? targetOwner : null,
                    targetHandle,
                    targetMetadata,
                    targetNodeId,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_addReferenceSemaphore.Release();
            }
        }

        /// <summary>
        /// Validates the data-model constraints of a reference and adds it with its mirrored
        /// inverse. Must be called with <c>m_addReferenceSemaphore</c> held.
        /// </summary>
        private async ValueTask<ServiceResult> AddCheckedReferenceAsync(
            OperationContext context,
            AddReferencesItem item,
            IAsyncNodeManager sourceOwner,
            object sourceHandle,
            NodeMetadata? sourceMetadata,
            IAsyncNodeManager? targetOwner,
            object? targetHandle,
            NodeMetadata? targetMetadata,
            NodeId targetNodeId,
            CancellationToken cancellationToken)
        {
            bool localTarget = targetOwner != null;
            ServiceResult constraintResult = ValidateAddReferenceConstraints(
                item,
                sourceMetadata?.NodeClass ?? NodeClass.Unspecified,
                GetNodeState(sourceHandle),
                localTarget ? targetNodeId : NodeId.Null,
                localTarget ? targetMetadata!.NodeClass : NodeClass.Unspecified,
                GetNodeState(targetHandle));
            if (ServiceResult.IsBad(constraintResult))
            {
                return constraintResult;
            }

            ServiceResult sourceResult;
            try
            {
                sourceResult = await sourceOwner.AddReferenceAsync(context, item, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }

            if (ServiceResult.IsBad(sourceResult))
            {
                return sourceResult;
            }

            // Write the complementary edge into the target's owning manager when the
            // target is explicitly local. Roll back the source edge if the target mutation fails.
            if (localTarget)
            {
                var inverseItem = new AddReferencesItem
                {
                    SourceNodeId = targetNodeId,
                    ReferenceTypeId = item.ReferenceTypeId,
                    IsForward = !item.IsForward,
                    TargetServerUri = string.Empty,
                    TargetNodeId = item.SourceNodeId,
                    TargetNodeClass = sourceMetadata!.NodeClass
                };

                try
                {
                    ServiceResult inverseResult = await targetOwner!.AddReferenceAsync(
                        context, inverseItem, cancellationToken).ConfigureAwait(false);
                    if (ServiceResult.IsBad(inverseResult) &&
                        inverseResult.StatusCode != StatusCodes.BadDuplicateReferenceNotAllowed)
                    {
                        m_logger.AddReferencesFailedToMirrorInverseEdgeRefType(
                            item.ReferenceTypeId,
                            item.SourceNodeId,
                            item.TargetNodeId,
                            inverseResult.StatusCode);
                        await RollbackAddedReferenceAsync(
                            sourceOwner,
                            context,
                            item).ConfigureAwait(false);
                        return inverseResult;
                    }
                }
                catch (ServiceResultException ex)
                    when (ex.StatusCode == StatusCodes.BadDuplicateReferenceNotAllowed)
                {
                    return sourceResult;
                }
                catch (Exception ex)
                {
                    m_logger.AddReferencesFailedToMirrorInverseEdgeRefType2(
                        ex,
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId);
                    await RollbackAddedReferenceAsync(
                        sourceOwner,
                        context,
                        item).ConfigureAwait(false);
                    if (ex is ServiceResultException serviceResultException)
                    {
                        return new ServiceResult(serviceResultException);
                    }

                    throw;
                }
            }

            return sourceResult;
        }

        private async ValueTask<ServiceResult> DispatchDeleteReferenceAsync(
            OperationContext context,
            DeleteReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo);
            }

            if (item.SourceNodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (item.ReferenceTypeId.IsNull ||
                !Server.TypeTree.IsKnown(item.ReferenceTypeId) ||
                !Server.TypeTree.IsTypeOf(item.ReferenceTypeId, ReferenceTypeIds.References))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            // Part 3 5.3.1: no Reference of an abstract ReferenceType exists to delete.
            if (await IsAbstractReferenceTypeAsync(context, item.ReferenceTypeId, cancellationToken)
                .ConfigureAwait(false))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            (object? sourceHandle, IAsyncNodeManager? sourceOwner) =
                await m_serviceDispatch.GetManagerHandleAsync(context, item.SourceNodeId, cancellationToken)
                    .ConfigureAwait(false);
            if (sourceHandle == null || sourceOwner == null)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (!sourceOwner.AllowNodeManagement)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }

            (ServiceResult permissionResult, _) =
                await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                    context,
                    sourceOwner,
                    sourceHandle,
                    PermissionType.RemoveReference,
                    null,
                    permissionsOnly: true,
                    metadataRequired: true,
                    cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return permissionResult;
            }

            NodeId targetNodeId = NodeId.Null;
            IAsyncNodeManager? targetOwner = null;
            NodeMetadata? targetMetadata = null;
            bool explicitlyLocalTarget =
                item.DeleteBidirectional &&
                TryGetExplicitLocalTargetNodeId(
                    targetServerUri: null,
                    item.TargetNodeId,
                    out targetNodeId);
            if (explicitlyLocalTarget)
            {
                object? targetHandle;
                (targetHandle, targetOwner) = await m_serviceDispatch.GetManagerHandleAsync(
                    context,
                    targetNodeId,
                    cancellationToken).ConfigureAwait(false);
                if (targetHandle == null || targetOwner == null)
                {
                    return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
                }

                if (!ReferenceEquals(targetOwner, sourceOwner) &&
                    !targetOwner.AllowNodeManagement)
                {
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                }

                (permissionResult, targetMetadata) =
                    await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                        context,
                        targetOwner,
                        targetHandle,
                        PermissionType.RemoveReference,
                        null,
                        permissionsOnly: true,
                        metadataRequired: true,
                        cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(permissionResult))
                {
                    return permissionResult;
                }
            }

            bool crossManagerTarget =
                targetOwner != null &&
                !ReferenceEquals(targetOwner, sourceOwner);
            if (crossManagerTarget &&
                (targetMetadata == null || targetMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
            }

            DeleteReferencesItem sourceItem = item;
            if (item.DeleteBidirectional)
            {
                sourceItem = new DeleteReferencesItem
                {
                    SourceNodeId = item.SourceNodeId,
                    ReferenceTypeId = item.ReferenceTypeId,
                    IsForward = item.IsForward,
                    TargetNodeId = item.TargetNodeId,
                    DeleteBidirectional = false
                };
            }

            ServiceResult sourceResult;
            try
            {
                sourceResult = await sourceOwner.DeleteReferenceAsync(
                    context,
                    sourceItem,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }

            if (ServiceResult.IsBad(sourceResult))
            {
                return sourceResult;
            }

            if (!item.DeleteBidirectional ||
                !explicitlyLocalTarget)
            {
                return sourceResult;
            }

            var inverseItem = new DeleteReferencesItem
            {
                SourceNodeId = targetNodeId,
                ReferenceTypeId = item.ReferenceTypeId,
                IsForward = !item.IsForward,
                TargetNodeId = item.SourceNodeId,
                DeleteBidirectional = false
            };

            try
            {
                ServiceResult inverseResult = await targetOwner!.DeleteReferenceAsync(
                    context, inverseItem, cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(inverseResult) &&
                    inverseResult.StatusCode != StatusCodes.BadNoMatch)
                {
                    m_logger.DeleteReferencesFailedToMirrorInverseDeleteRefType(
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId,
                        inverseResult.StatusCode);
                    await RollbackDeletedReferenceAsync(
                        sourceOwner,
                        context,
                        item,
                        targetMetadata!.NodeClass).ConfigureAwait(false);
                    return inverseResult;
                }
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNoMatch)
            {
                return sourceResult;
            }
            catch (Exception ex)
            {
                m_logger.DeleteReferencesFailedToMirrorInverseDeleteRefType2(
                    ex,
                    item.ReferenceTypeId,
                    item.SourceNodeId,
                    item.TargetNodeId);
                await RollbackDeletedReferenceAsync(
                    sourceOwner,
                    context,
                    item,
                    targetMetadata!.NodeClass).ConfigureAwait(false);
                if (ex is ServiceResultException serviceResultException)
                {
                    return new ServiceResult(serviceResultException);
                }

                throw;
            }

            return sourceResult;
        }

        private bool TryGetExplicitLocalTargetNodeId(
            string? targetServerUri,
            ExpandedNodeId targetNodeId,
            out NodeId localNodeId)
        {
            if (!string.IsNullOrEmpty(targetServerUri) ||
                targetNodeId.ServerIndex != 0)
            {
                localNodeId = NodeId.Null;
                return false;
            }

            localNodeId = ExpandedNodeId.ToNodeId(targetNodeId, Server.NamespaceUris);
            return !localNodeId.IsNull;
        }

        private async ValueTask RollbackAddedReferenceAsync(
            IAsyncNodeManager sourceOwner,
            OperationContext context,
            AddReferencesItem item)
        {
            var rollbackItem = new DeleteReferencesItem
            {
                SourceNodeId = item.SourceNodeId,
                ReferenceTypeId = item.ReferenceTypeId,
                IsForward = item.IsForward,
                TargetNodeId = item.TargetNodeId,
                DeleteBidirectional = false
            };

            using var cleanupCts = new CancellationTokenSource(
                s_nodeManagementCompensationTimeout);
            try
            {
                ServiceResult rollbackResult = await sourceOwner.DeleteReferenceAsync(
                    context,
                    rollbackItem,
                    cleanupCts.Token).ConfigureAwait(false);
                if (ServiceResult.IsBad(rollbackResult))
                {
                    m_logger.AddReferencesRollbackFailed(
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId,
                        rollbackResult.StatusCode);
                }
            }
            catch (Exception ex)
            {
                m_logger.AddReferencesRollbackFailed2(
                    ex,
                    item.ReferenceTypeId,
                    item.SourceNodeId,
                    item.TargetNodeId);
            }
        }

        private async ValueTask RollbackDeletedReferenceAsync(
            IAsyncNodeManager sourceOwner,
            OperationContext context,
            DeleteReferencesItem item,
            NodeClass targetNodeClass)
        {
            var rollbackItem = new AddReferencesItem
            {
                SourceNodeId = item.SourceNodeId,
                ReferenceTypeId = item.ReferenceTypeId,
                IsForward = item.IsForward,
                TargetServerUri = string.Empty,
                TargetNodeId = item.TargetNodeId,
                TargetNodeClass = targetNodeClass
            };

            using var cleanupCts = new CancellationTokenSource(
                s_nodeManagementCompensationTimeout);
            try
            {
                ServiceResult rollbackResult = await sourceOwner.AddReferenceAsync(
                    context,
                    rollbackItem,
                    cleanupCts.Token).ConfigureAwait(false);
                if (ServiceResult.IsBad(rollbackResult))
                {
                    m_logger.DeleteReferencesRollbackFailed(
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId,
                        rollbackResult.StatusCode);
                }
            }
            catch (Exception ex)
            {
                m_logger.DeleteReferencesRollbackFailed2(
                    ex,
                    item.ReferenceTypeId,
                    item.SourceNodeId,
                    item.TargetNodeId);
            }
        }

        private async ValueTask<ServiceResult> ValidateAddNodeNamespacePermissionAsync(
            OperationContext context,
            ushort namespaceIndex,
            CancellationToken cancellationToken)
        {
            if (ConfigurationNodeManager == null ||
                (context.Session == null && context.UserIdentity == null && context.ChannelContext == null))
            {
                return StatusCodes.Good;
            }

            try
            {
                // the defaults are read for the caller, so OnReadValue callbacks see the
                // caller's identity and channel, and a failed read or decode denies.
                NamespaceDefaultPermissions defaults = await NamespaceDefaultPermissionReader.ReadAsync(
                    Server,
                    Server.DefaultSystemContext.Copy(context),
                    namespaceIndex,
                    cancellationToken).ConfigureAwait(false);
                if (StatusCode.IsBad(defaults.Status))
                {
                    return ServiceResult.Create(
                        defaults.Status,
                        "Cannot read the default permissions of namespace {0}.",
                        namespaceIndex);
                }

                if (!defaults.HasDefaultAccessRestrictions &&
                    !defaults.HasDefaultRolePermissions &&
                    !defaults.HasDefaultUserRolePermissions)
                {
                    return StatusCodes.Good;
                }

                var metadata = new NodeMetadata(ConfigurationNodeManager, new NodeId(0u, namespaceIndex))
                {
                    DefaultAccessRestrictions = defaults.DefaultAccessRestrictions,
                    DefaultRolePermissions = defaults.DefaultRolePermissions,
                    DefaultUserRolePermissions = defaults.DefaultUserRolePermissions
                };

                return m_serviceDispatch.ValidatePermissionMetadata(context, metadata, PermissionType.AddNode);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Checks the NodeClass constraints defined in OPC UA Part 3 for concrete
        /// standard hierarchical ReferenceTypes.
        /// </summary>
        private static bool IsReferenceAllowedForNodeClasses(
            NodeId referenceTypeId,
            NodeClass parentNodeClass,
            NodeClass newNodeClass)
        {
            if (newNodeClass == NodeClass.Unspecified)
            {
                return true;
            }

            if (referenceTypeId == ReferenceTypeIds.HasSubtype)
            {
                return parentNodeClass == newNodeClass &&
                    parentNodeClass is NodeClass.ObjectType or
                        NodeClass.VariableType or
                        NodeClass.DataType or
                        NodeClass.ReferenceType;
            }

            if (referenceTypeId == ReferenceTypeIds.Organizes)
            {
                return parentNodeClass is NodeClass.Object or NodeClass.ObjectType or NodeClass.View;
            }

            if (referenceTypeId == ReferenceTypeIds.HasComponent)
            {
                return (parentNodeClass is NodeClass.Object or
                            NodeClass.Variable or
                            NodeClass.ObjectType or
                            NodeClass.VariableType) &&
                    newNodeClass is NodeClass.Object or NodeClass.Variable or NodeClass.Method;
            }

            if (referenceTypeId == ReferenceTypeIds.HasProperty)
            {
                return newNodeClass == NodeClass.Variable;
            }

            return true;
        }

        /// <summary>
        /// Checks the data-model rules of OPC UA Part 3 a new Reference must not
        /// break: Bad_NodeClassInvalid when the TargetNodeClass of the request does
        /// not match the local TargetNode and Bad_ReferenceNotAllowed for a data-model
        /// violation (Part 4 5.8.4).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rules are evaluated on the forward direction of the Reference, so a
        /// request with IsForward=false is checked with SourceNode and TargetNode
        /// swapped. The rules are:
        /// </para>
        /// <list type="bullet">
        /// <item>HasSubtype only between two types of the same NodeClass. The server
        /// TypeTree is not updated by AddReferences, so subtype queries keep the type
        /// hierarchy that was loaded.</item>
        /// <item>HasTypeDefinition only from an Object to an ObjectType or from a
        /// Variable to a VariableType, never to an abstract type or an Interface
        /// (Part 3 4.10.2), and only when the Node has no other TypeDefinition
        /// (Part 3 5.5.1 and 5.6.2).</item>
        /// <item>HasModellingRule only from an Object, Variable or Method to an Object,
        /// and only when the Node has no other ModellingRule.</item>
        /// <item>A Property is never the SourceNode of a hierarchical Reference (Part 3
        /// 5.6.3) and the NodeClass constraints of Organizes, HasComponent and
        /// HasProperty (Part 3 7.6 - 7.9) apply to their subtypes as well.</item>
        /// </list>
        /// <para>
        /// Rules that need the existing References of a Node are only evaluated when
        /// its NodeManager exposes it as a <see cref="NodeState"/>; rules that need the
        /// NodeClass of a remote TargetNode use the TargetNodeClass of the request.
        /// </para>
        /// </remarks>
        private ServiceResult ValidateAddReferenceConstraints(
            AddReferencesItem item,
            NodeClass sourceNodeClass,
            NodeState? sourceNode,
            NodeId localTargetNodeId,
            NodeClass localTargetNodeClass,
            NodeState? targetNode)
        {
            NodeClass targetNodeClass = localTargetNodeClass;
            if (item.TargetNodeClass != NodeClass.Unspecified)
            {
                if (targetNodeClass == NodeClass.Unspecified)
                {
                    targetNodeClass = item.TargetNodeClass;
                }
                else if (item.TargetNodeClass != targetNodeClass)
                {
                    return new ServiceResult(StatusCodes.BadNodeClassInvalid);
                }
            }

            bool isForward = item.IsForward;
            NodeClass fromNodeClass = isForward ? sourceNodeClass : targetNodeClass;
            NodeClass toNodeClass = isForward ? targetNodeClass : sourceNodeClass;
            NodeState? fromNode = isForward ? sourceNode : targetNode;
            NodeState? toNode = isForward ? targetNode : sourceNode;
            NodeId toNodeId = isForward ? localTargetNodeId : item.SourceNodeId;
            NodeId referenceTypeId = item.ReferenceTypeId;
            TypeTable types = Server.TypeTree;

            if (types.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasSubtype))
            {
                return fromNodeClass == toNodeClass &&
                    fromNodeClass is NodeClass.ObjectType or
                        NodeClass.VariableType or
                        NodeClass.DataType or
                        NodeClass.ReferenceType
                    ? ServiceResult.Good
                    : new ServiceResult(StatusCodes.BadReferenceNotAllowed);
            }

            if (types.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasTypeDefinition))
            {
                bool allowed = fromNodeClass switch
                {
                    NodeClass.Object => toNodeClass is NodeClass.Unspecified or NodeClass.ObjectType,
                    NodeClass.Variable => toNodeClass is NodeClass.Unspecified or NodeClass.VariableType,
                    NodeClass.Unspecified =>
                        toNodeClass is NodeClass.Unspecified or NodeClass.ObjectType or NodeClass.VariableType,
                    _ => false
                };
                if (!allowed ||
                    types.IsTypeOf(toNodeId, ObjectTypeIds.BaseInterfaceType) ||
                    toNode is BaseTypeState { IsAbstract: true } ||
                    HasOtherForwardTarget(fromNode, ReferenceTypeIds.HasTypeDefinition, toNodeId))
                {
                    return new ServiceResult(StatusCodes.BadReferenceNotAllowed);
                }
                return ServiceResult.Good;
            }

            if (types.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasModellingRule))
            {
                if (fromNodeClass is not (NodeClass.Unspecified or
                        NodeClass.Object or
                        NodeClass.Variable or
                        NodeClass.Method) ||
                    toNodeClass is not (NodeClass.Unspecified or NodeClass.Object) ||
                    HasOtherForwardTarget(fromNode, ReferenceTypeIds.HasModellingRule, toNodeId))
                {
                    return new ServiceResult(StatusCodes.BadReferenceNotAllowed);
                }
                return ServiceResult.Good;
            }

            if (!types.IsTypeOf(referenceTypeId, ReferenceTypeIds.HierarchicalReferences))
            {
                return ServiceResult.Good;
            }

            if (IsProperty(fromNode))
            {
                return new ServiceResult(StatusCodes.BadReferenceNotAllowed);
            }

            NodeId constrainedReferenceTypeId;
            if (types.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasProperty))
            {
                constrainedReferenceTypeId = ReferenceTypeIds.HasProperty;
            }
            else if (types.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasComponent))
            {
                constrainedReferenceTypeId = ReferenceTypeIds.HasComponent;
            }
            else if (types.IsTypeOf(referenceTypeId, ReferenceTypeIds.Organizes))
            {
                constrainedReferenceTypeId = ReferenceTypeIds.Organizes;
            }
            else
            {
                return ServiceResult.Good;
            }

            if (fromNodeClass != NodeClass.Unspecified &&
                !IsReferenceAllowedForNodeClasses(constrainedReferenceTypeId, fromNodeClass, toNodeClass))
            {
                return new ServiceResult(StatusCodes.BadReferenceNotAllowed);
            }

            // HasProperty makes the TargetNode a Property, which must not already be the
            // SourceNode of a hierarchical Reference (Part 3 5.6.3).
            if (constrainedReferenceTypeId == ReferenceTypeIds.HasProperty &&
                HasForwardHierarchicalReference(toNode))
            {
                return new ServiceResult(StatusCodes.BadReferenceNotAllowed);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns true when the node is the SourceNode of a hierarchical Reference: it has
        /// a child, or a forward hierarchical Reference added through AddReferences.
        /// </summary>
        private bool HasForwardHierarchicalReference(NodeState? node)
        {
            if (node == null)
            {
                return false;
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(Server.DefaultSystemContext, children);
            if (children.Count > 0)
            {
                return true;
            }

            var references = new List<IReference>();
            node.GetReferences(Server.DefaultSystemContext, references);
            foreach (IReference reference in references)
            {
                if (!reference.IsInverse &&
                    Server.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HierarchicalReferences))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the in-memory node behind a NodeManager handle, if any.
        /// </summary>
        private static NodeState? GetNodeState(object? handle)
        {
            return handle is NodeHandle { Node: { } node } ? node : null;
        }

        /// <summary>
        /// Returns true when the node already has a forward Reference of the given
        /// ReferenceType (or a subtype) to a node other than <paramref name="targetId"/>.
        /// HasTypeDefinition and HasModellingRule of an instance are intrinsic, so the
        /// TypeDefinitionId and ModellingRuleId are checked as well.
        /// </summary>
        private bool HasOtherForwardTarget(NodeState? node, NodeId referenceTypeId, NodeId targetId)
        {
            if (node == null)
            {
                return false;
            }

            if (node is BaseInstanceState instance)
            {
                NodeId intrinsicTargetId = referenceTypeId == ReferenceTypeIds.HasTypeDefinition
                    ? instance.TypeDefinitionId
                    : instance.ModellingRuleId;
                if (!intrinsicTargetId.IsNull && intrinsicTargetId != targetId)
                {
                    return true;
                }
            }

            var references = new List<IReference>();
            node.GetReferences(Server.DefaultSystemContext, references);
            foreach (IReference reference in references)
            {
                if (reference.IsInverse ||
                    !Server.TypeTree.IsTypeOf(reference.ReferenceTypeId, referenceTypeId))
                {
                    continue;
                }

                NodeId existingTargetId = ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris);
                if (existingTargetId.IsNull || existingTargetId != targetId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when the node is a Property: a Variable of PropertyType or one
        /// referenced by HasProperty (Part 3 5.6.3).
        /// </summary>
        private bool IsProperty(NodeState? node)
        {
            if (node is not BaseVariableState variable)
            {
                return false;
            }

            if (variable is PropertyState ||
                Server.TypeTree.IsTypeOf(variable.TypeDefinitionId, VariableTypeIds.PropertyType) ||
                (variable.Parent != null &&
                    Server.TypeTree.IsTypeOf(variable.ReferenceTypeId, ReferenceTypeIds.HasProperty)))
            {
                return true;
            }

            var references = new List<IReference>();
            variable.GetReferences(Server.DefaultSystemContext, references);
            foreach (IReference reference in references)
            {
                if (reference.IsInverse &&
                    Server.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasProperty))
                {
                    return true;
                }

                // a type definition added through AddReferences is stored as a reference
                // rather than in TypeDefinitionId.
                if (!reference.IsInverse &&
                    Server.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasTypeDefinition) &&
                    Server.TypeTree.IsTypeOf(
                        ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris),
                        VariableTypeIds.PropertyType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the first NodeManager registered against the given namespace
        /// index that has opted in to NodeManagement, or <c>null</c> when none has.
        /// </summary>
        private IAsyncNodeManager? FindNodeManagementOwner(ushort namespaceIndex)
        {
            if (!NamespaceManagers.TryGetValue(namespaceIndex, out IReadOnlyList<IAsyncNodeManager>? nodeManagers))
            {
                return null;
            }

            for (int ii = 0; ii < nodeManagers.Count; ii++)
            {
                if (nodeManagers[ii].AllowNodeManagement)
                {
                    return nodeManagers[ii];
                }
            }
            return null;
        }

        private static readonly TimeSpan s_nodeManagementCompensationTimeout =
            TimeSpan.FromSeconds(5);
    }
}
