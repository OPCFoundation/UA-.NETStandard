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
    /// Code in this file may acquire <c>m_dynamicMutationSemaphore</c> (AddNodesAsync
    /// and DeleteNodesAsync only) so address-space mutation serializes with NodeManager
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
        public async ValueTask RemoveReferencesAsync(List<LocalReference> referencesToRemove, CancellationToken cancellationToken = default)
        {
            for (int ii = 0; ii < referencesToRemove.Count; ii++)
            {
                LocalReference reference = referencesToRemove[ii];

                // find source node.
                (object? sourceHandle, IAsyncNodeManager? nodeManager) = await GetManagerHandleAsync(
                        reference.SourceId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (sourceHandle == null)
                {
                    continue;
                }

                // delete the reference.
                await nodeManager!.DeleteReferenceAsync(
                        sourceHandle,
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        reference.TargetId,
                        false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            AddNodesAsync(
                OperationContext context,
                ArrayOf<AddNodesItem> nodesToAdd,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var results = new AddNodesResult[nodesToAdd.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToAdd.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToAdd.Count; ii++)
                {
                    AddNodesItem item = nodesToAdd[ii];
                    (ServiceResult result, NodeId addedNodeId) = await DispatchAddNodeAsync(
                        context,
                        item,
                        cancellationToken).ConfigureAwait(false);

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
                m_dynamicMutationSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            DeleteNodesAsync(
                OperationContext context,
                ArrayOf<DeleteNodesItem> nodesToDelete,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var results = new StatusCode[nodesToDelete.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToDelete.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToDelete.Count; ii++)
                {
                    DeleteNodesItem item = nodesToDelete[ii];
                    ServiceResult result = await DispatchDeleteNodeAsync(
                        context,
                        item,
                        cancellationToken).ConfigureAwait(false);

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
                m_dynamicMutationSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            AddReferencesAsync(
                OperationContext context,
                ArrayOf<AddReferencesItem> referencesToAdd,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var results = new StatusCode[referencesToAdd.Count];
            var diagnosticInfos = new DiagnosticInfo[referencesToAdd.Count];
            bool anyDiagnostics = false;

            for (int ii = 0; ii < referencesToAdd.Count; ii++)
            {
                AddReferencesItem item = referencesToAdd[ii];
                ServiceResult result = await DispatchAddReferenceAsync(
                    context,
                    item,
                    cancellationToken).ConfigureAwait(false);

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
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var results = new StatusCode[referencesToDelete.Count];
            var diagnosticInfos = new DiagnosticInfo[referencesToDelete.Count];
            bool anyDiagnostics = false;

            for (int ii = 0; ii < referencesToDelete.Count; ii++)
            {
                DeleteReferencesItem item = referencesToDelete[ii];
                ServiceResult result = await DispatchDeleteReferenceAsync(
                    context,
                    item,
                    cancellationToken).ConfigureAwait(false);

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

        private async ValueTask<(ServiceResult result, NodeId addedNodeId)> DispatchAddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return (new ServiceResult(StatusCodes.BadNothingToDo), NodeId.Null);
            }

            if (item.BrowseName.IsNull)
            {
                return (new ServiceResult(StatusCodes.BadBrowseNameInvalid), NodeId.Null);
            }

            if (item.ParentNodeId.IsNull)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            if (item.ReferenceTypeId.IsNull ||
                !Server.TypeTree.IsKnown(item.ReferenceTypeId))
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
                await GetManagerHandleAsync(parentNodeId, cancellationToken).ConfigureAwait(false);
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
                await GetManagerHandleAsync(item.NodeId, cancellationToken).ConfigureAwait(false);
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
                !Server.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            (object? sourceHandle, IAsyncNodeManager? sourceOwner) =
                await GetManagerHandleAsync(item.SourceNodeId, cancellationToken).ConfigureAwait(false);
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

            if (TryGetExplicitLocalTargetNodeId(
                item.TargetServerUri,
                item.TargetNodeId,
                out NodeId targetNodeId))
            {
                object? targetHandle;
                (targetHandle, targetOwner) = await GetManagerHandleAsync(
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

            bool crossManagerTarget =
                targetOwner != null &&
                !ReferenceEquals(targetOwner, sourceOwner);
            if (crossManagerTarget &&
                (sourceMetadata == null || sourceMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }
            if (crossManagerTarget &&
                (targetMetadata == null || targetMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
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
            if (crossManagerTarget)
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
                    if (ServiceResult.IsBad(inverseResult))
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
                !Server.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            (object? sourceHandle, IAsyncNodeManager? sourceOwner) =
                await GetManagerHandleAsync(item.SourceNodeId, cancellationToken).ConfigureAwait(false);
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
                (targetHandle, targetOwner) = await GetManagerHandleAsync(
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
            if (item.DeleteBidirectional &&
                (!explicitlyLocalTarget || crossManagerTarget))
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

            if (!crossManagerTarget)
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
                if (ServiceResult.IsBad(inverseResult))
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
            if (context.Session == null || ConfigurationNodeManager == null)
            {
                return StatusCodes.Good;
            }

            try
            {
                NamespaceMetadataState? namespaceMetadata =
                    await ConfigurationNodeManager.GetNamespaceMetadataStateAsync(
                        namespaceIndex,
                        cancellationToken).ConfigureAwait(false);
                if (namespaceMetadata == null)
                {
                    return StatusCodes.Good;
                }

                var metadata = new NodeMetadata(
                    namespaceMetadata,
                    new NodeId(0u, namespaceIndex));

                if (namespaceMetadata.DefaultAccessRestrictions != null)
                {
                    metadata.DefaultAccessRestrictions =
                        (AccessRestrictionType)namespaceMetadata.DefaultAccessRestrictions.Value;
                }

                if (namespaceMetadata.DefaultRolePermissions != null)
                {
                    metadata.DefaultRolePermissions =
                        namespaceMetadata.DefaultRolePermissions.Value;
                }

                if (namespaceMetadata.DefaultUserRolePermissions != null)
                {
                    metadata.DefaultUserRolePermissions =
                        namespaceMetadata.DefaultUserRolePermissions.Value;
                }

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
