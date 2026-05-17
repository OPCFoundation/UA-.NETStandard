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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Reference server overrides for the OPC UA Node Management service set
    /// (AddNodes / DeleteNodes / AddReferences / DeleteReferences). Newly
    /// added nodes live in the writable <see cref="ReferenceNodeManager"/>
    /// namespace regardless of the parent node's namespace; an inverse
    /// reference is also added to the parent so the new node is reachable
    /// via Browse.
    /// </summary>
    /// <remarks>
    /// TODO (follow-up, see PR #3750 review comment): move this
    /// implementation into <see cref="StandardServer"/> and resolve it
    /// through <see cref="MasterNodeManager"/> so AddNodes / DeleteNodes
    /// dispatch is centralised in the SDK rather than reimplemented in
    /// each derived server.
    /// </remarks>
    public partial class ReferenceServer
    {
        /// <summary>
        /// Implements the AddNodes service to allow conformance tests to
        /// exercise the Node Management service set against the reference
        /// server. Newly added nodes live in the writable
        /// <see cref="ReferenceNodeManager"/> namespace regardless of the
        /// parent node's namespace; an inverse reference is also added to
        /// the parent so the new node is reachable via Browse.
        /// </summary>
        /// <remarks>
        /// TODO (follow-up, see PR #3750 review comment): move this
        /// implementation into <see cref="StandardServer"/> and resolve it
        /// through <see cref="MasterNodeManager"/> so AddNodes / DeleteNodes
        /// dispatch is centralised in the SDK rather than reimplemented in
        /// each derived server. The current routing keeps writes constrained
        /// to <see cref="ReferenceNodeManager"/> which is acceptable for the
        /// CTT but not the desired long-term shape.
        /// </remarks>
        public override async ValueTask<AddNodesResponse> AddNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.AddNodes,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    nodesToAdd,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new AddNodesResult[nodesToAdd.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToAdd.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToAdd.Count; ii++)
                {
                    (StatusCode statusCode, NodeId addedNodeId) =
                        await TryAddNodeAsync(
                            context,
                            nodesToAdd[ii],
                            requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = new AddNodesResult
                    {
                        StatusCode = statusCode,
                        AddedNodeId = addedNodeId
                    };

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new AddNodesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Implements the DeleteNodes service for the reference server. Only
        /// nodes managed by the writable <see cref="ReferenceNodeManager"/>
        /// can be removed; attempts to delete nodes from other node managers
        /// (for example, the core address space) return
        /// <see cref="StatusCodes.BadUserAccessDenied"/>.
        /// </summary>
        public override async ValueTask<DeleteNodesResponse> DeleteNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteNodes,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    nodesToDelete,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new StatusCode[nodesToDelete.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToDelete.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToDelete.Count; ii++)
                {
                    StatusCode statusCode = await TryDeleteNodeAsync(
                        context,
                        nodesToDelete[ii],
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = statusCode;

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new DeleteNodesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Implements the AddReferences service. Forward references are added
        /// through the master node manager which dispatches the change to the
        /// node manager that owns the source node.
        /// </summary>
        public override async ValueTask<AddReferencesResponse> AddReferencesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.AddReferences,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    referencesToAdd,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new StatusCode[referencesToAdd.Count];
                var diagnosticInfos = new DiagnosticInfo[referencesToAdd.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < referencesToAdd.Count; ii++)
                {
                    StatusCode statusCode = await TryAddReferenceAsync(
                        referencesToAdd[ii],
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = statusCode;

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new AddReferencesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Implements the DeleteReferences service. The change is dispatched
        /// to the node manager that owns the source node through the master
        /// node manager.
        /// </summary>
        public override async ValueTask<DeleteReferencesResponse> DeleteReferencesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteReferences,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValidateOperationLimits(
                    referencesToDelete,
                    ServerInternal.ServerObject.ServerCapabilities.OperationLimits
                        .MaxNodesPerNodeManagement);

                var results = new StatusCode[referencesToDelete.Count];
                var diagnosticInfos = new DiagnosticInfo[referencesToDelete.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < referencesToDelete.Count; ii++)
                {
                    StatusCode statusCode = await TryDeleteReferenceAsync(
                        referencesToDelete[ii],
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                    results[ii] = statusCode;

                    if (StatusCode.IsBad(statusCode))
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            new ServiceResult(statusCode),
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return new DeleteReferencesResponse
                {
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = anyDiagnostics
                        ? diagnosticInfos.ToArrayOf()
                        : default,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;
                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Validates a single AddNodes item and creates the node in the
        /// reference node manager when the request is acceptable.
        /// </summary>
        private async ValueTask<(StatusCode statusCode, NodeId addedNodeId)> TryAddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken)
        {
            ReferenceNodeManager nodeManager = m_referenceNodeManager;
            if (nodeManager == null)
            {
                return (StatusCodes.BadNotSupported, NodeId.Null);
            }

            if (item == null || item.BrowseName.IsNull)
            {
                return (StatusCodes.BadBrowseNameInvalid, NodeId.Null);
            }

            if (!IsSupportedNodeClass(item.NodeClass))
            {
                return (StatusCodes.BadNodeClassInvalid, NodeId.Null);
            }

            // Validate the parent node id and resolve it to the local server.
            NodeId parentNodeId = ExpandedNodeId.ToNodeId(
                item.ParentNodeId,
                ServerInternal.NamespaceUris);

            if (parentNodeId.IsNull)
            {
                return (StatusCodes.BadParentNodeIdInvalid, NodeId.Null);
            }

            (object parentHandle, _) = await ServerInternal.NodeManager
                .GetManagerHandleAsync(parentNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (parentHandle == null)
            {
                return (StatusCodes.BadParentNodeIdInvalid, NodeId.Null);
            }

            // Validate the reference type.
            if (item.ReferenceTypeId.IsNull ||
                !ServerInternal.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return (StatusCodes.BadReferenceTypeIdInvalid, NodeId.Null);
            }

            if (!ServerInternal.TypeTree.IsTypeOf(
                item.ReferenceTypeId,
                ReferenceTypeIds.HierarchicalReferences))
            {
                return (StatusCodes.BadReferenceNotAllowed, NodeId.Null);
            }

            // Reject client-provided NodeIds — the server assigns NodeIds.
            if (!item.RequestedNewNodeId.IsNull)
            {
                return (StatusCodes.BadNodeIdRejected, NodeId.Null);
            }

            // Validate the type definition.
            NodeId typeDefinitionId = ExpandedNodeId.ToNodeId(
                item.TypeDefinition,
                ServerInternal.NamespaceUris);

            BaseInstanceState instance;
            try
            {
                instance = CreateInstanceFromAddNodesItem(item, typeDefinitionId);
            }
            catch (ServiceResultException ex)
            {
                return (ex.StatusCode, NodeId.Null);
            }

            // Detect duplicate browse names under the same parent before adding.
            if (await BrowseNameExistsUnderParentAsync(
                    context,
                    parentNodeId,
                    item.BrowseName,
                    item.ReferenceTypeId,
                    cancellationToken).ConfigureAwait(false))
            {
                return (StatusCodes.BadBrowseNameDuplicated, NodeId.Null);
            }

            try
            {
                NodeId addedNodeId = await nodeManager.AddInstanceNodeAsync(
                    new ServerSystemContext(ServerInternal, context),
                    parentNodeId,
                    item.ReferenceTypeId,
                    instance,
                    cancellationToken).ConfigureAwait(false);

                return (StatusCodes.Good, addedNodeId);
            }
            catch (ServiceResultException ex)
            {
                return (ex.StatusCode, NodeId.Null);
            }
        }

        /// <summary>
        /// Validates a single DeleteNodes item and removes the node when it
        /// is owned by the reference node manager.
        /// </summary>
        private async ValueTask<StatusCode> TryDeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null || item.NodeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            (object handle, IAsyncNodeManager nodeManager) = await ServerInternal
                .NodeManager.GetManagerHandleAsync(item.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (handle == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // Only allow deletion in the writable reference namespace to avoid
            // breaking the core address space exposed by the SDK.
            if (nodeManager is not ReferenceNodeManager referenceNodeManager ||
                referenceNodeManager != m_referenceNodeManager)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            bool removed = await referenceNodeManager.DeleteNodeAsync(
                new ServerSystemContext(ServerInternal, context),
                item.NodeId,
                cancellationToken).ConfigureAwait(false);

            return removed ? (StatusCode)StatusCodes.Good : StatusCodes.BadNodeIdUnknown;
        }

        /// <summary>
        /// Adds a single reference using the master node manager.
        /// </summary>
        private async ValueTask<StatusCode> TryAddReferenceAsync(
            AddReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null ||
                item.SourceNodeId.IsNull ||
                item.ReferenceTypeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            if (!ServerInternal.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return StatusCodes.BadReferenceTypeIdInvalid;
            }

            (object sourceHandle, _) = await ServerInternal.NodeManager
                .GetManagerHandleAsync(item.SourceNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (sourceHandle == null)
            {
                return StatusCodes.BadSourceNodeIdInvalid;
            }

            NodeId targetNodeId = ExpandedNodeId.ToNodeId(
                item.TargetNodeId,
                ServerInternal.NamespaceUris);
            if (targetNodeId.IsNull)
            {
                return StatusCodes.BadTargetNodeIdInvalid;
            }

            (object targetHandle, _) = await ServerInternal.NodeManager
                .GetManagerHandleAsync(targetNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (targetHandle == null)
            {
                return StatusCodes.BadTargetNodeIdInvalid;
            }

            try
            {
                var references = new List<IReference>
                {
                    new NodeStateReference(
                        item.ReferenceTypeId,
                        !item.IsForward,
                        targetNodeId)
                };

                await ServerInternal.NodeManager.AddReferencesAsync(
                    item.SourceNodeId,
                    references,
                    cancellationToken).ConfigureAwait(false);

                return StatusCodes.Good;
            }
            catch (ServiceResultException ex)
            {
                return ex.StatusCode;
            }
        }

        /// <summary>
        /// Deletes a single reference using the master node manager.
        /// </summary>
        private async ValueTask<StatusCode> TryDeleteReferenceAsync(
            DeleteReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null ||
                item.SourceNodeId.IsNull ||
                item.ReferenceTypeId.IsNull)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            (object sourceHandle, IAsyncNodeManager nodeManager) = await ServerInternal
                .NodeManager.GetManagerHandleAsync(item.SourceNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (sourceHandle == null)
            {
                return StatusCodes.BadSourceNodeIdInvalid;
            }

            try
            {
                ServiceResult result = await nodeManager.DeleteReferenceAsync(
                    sourceHandle,
                    item.ReferenceTypeId,
                    !item.IsForward,
                    item.TargetNodeId,
                    item.DeleteBidirectional,
                    cancellationToken).ConfigureAwait(false);

                return result == null ? StatusCodes.Good : result.StatusCode;
            }
            catch (ServiceResultException ex)
            {
                return ex.StatusCode;
            }
        }

        /// <summary>
        /// Returns true when the supplied NodeClass is one this server
        /// permits clients to add at runtime.
        /// </summary>
        private static bool IsSupportedNodeClass(NodeClass nodeClass)
        {
            return nodeClass is NodeClass.Object or NodeClass.Variable;
        }

        /// <summary>
        /// Creates the NodeState for an AddNodes request based on the
        /// requested node class and provided attributes.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown when the supplied attributes are not valid for the node
        /// class.
        /// </exception>
        private static BaseInstanceState CreateInstanceFromAddNodesItem(
            AddNodesItem item,
            NodeId typeDefinitionId)
        {
            switch (item.NodeClass)
            {
                case NodeClass.Variable:
                {
                    var variable = new BaseDataVariableState(null)
                    {
                        BrowseName = item.BrowseName,
                        DisplayName = new LocalizedText(item.BrowseName.Name),
                        TypeDefinitionId = typeDefinitionId.IsNull
                            ? VariableTypeIds.BaseDataVariableType
                            : typeDefinitionId,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        DataType = DataTypeIds.BaseDataType,
                        ValueRank = ValueRanks.Scalar
                    };

                    if (!item.NodeAttributes.IsNull)
                    {
                        if (!item.NodeAttributes.TryGetValue(out VariableAttributes va))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeAttributesInvalid);
                        }
                        ApplyVariableAttributes(variable, va);
                    }

                    return variable;
                }
                case NodeClass.Object:
                {
                    var instance = new BaseObjectState(null)
                    {
                        BrowseName = item.BrowseName,
                        DisplayName = new LocalizedText(item.BrowseName.Name),
                        TypeDefinitionId = typeDefinitionId.IsNull
                            ? ObjectTypeIds.BaseObjectType
                            : typeDefinitionId
                    };

                    if (!item.NodeAttributes.IsNull)
                    {
                        if (!item.NodeAttributes.TryGetValue(out ObjectAttributes oa))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeAttributesInvalid);
                        }
                        ApplyObjectAttributes(instance, oa);
                    }

                    return instance;
                }
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadNodeClassInvalid);
            }
        }

        private static void ApplyVariableAttributes(
            BaseDataVariableState variable,
            VariableAttributes attributes)
        {
            uint mask = attributes.SpecifiedAttributes;
            if ((mask & (uint)NodeAttributesMask.DisplayName) != 0 &&
                !attributes.DisplayName.IsNull)
            {
                variable.DisplayName = attributes.DisplayName;
            }
            if ((mask & (uint)NodeAttributesMask.Description) != 0 &&
                !attributes.Description.IsNull)
            {
                variable.Description = attributes.Description;
            }
            if ((mask & (uint)NodeAttributesMask.DataType) != 0 &&
                !attributes.DataType.IsNull)
            {
                variable.DataType = attributes.DataType;
            }
            if ((mask & (uint)NodeAttributesMask.ValueRank) != 0)
            {
                variable.ValueRank = attributes.ValueRank;
            }
            if ((mask & (uint)NodeAttributesMask.AccessLevel) != 0)
            {
                variable.AccessLevel = attributes.AccessLevel;
            }
            if ((mask & (uint)NodeAttributesMask.UserAccessLevel) != 0)
            {
                variable.UserAccessLevel = attributes.UserAccessLevel;
            }
            if ((mask & (uint)NodeAttributesMask.Historizing) != 0)
            {
                variable.Historizing = attributes.Historizing;
            }
            if ((mask & (uint)NodeAttributesMask.MinimumSamplingInterval) != 0)
            {
                variable.MinimumSamplingInterval = attributes.MinimumSamplingInterval;
            }
            if ((mask & (uint)NodeAttributesMask.Value) != 0)
            {
                variable.Value = attributes.Value;
            }
        }

        private static void ApplyObjectAttributes(
            BaseObjectState instance,
            ObjectAttributes attributes)
        {
            uint mask = attributes.SpecifiedAttributes;
            if ((mask & (uint)NodeAttributesMask.DisplayName) != 0 &&
                !attributes.DisplayName.IsNull)
            {
                instance.DisplayName = attributes.DisplayName;
            }
            if ((mask & (uint)NodeAttributesMask.Description) != 0 &&
                !attributes.Description.IsNull)
            {
                instance.Description = attributes.Description;
            }
            if ((mask & (uint)NodeAttributesMask.EventNotifier) != 0)
            {
                instance.EventNotifier = attributes.EventNotifier;
            }
        }

        /// <summary>
        /// Returns true if a node with the requested browse name already exists
        /// directly under the parent for the given hierarchical reference type.
        /// </summary>
        private async ValueTask<bool> BrowseNameExistsUnderParentAsync(
            OperationContext context,
            NodeId parentNodeId,
            QualifiedName browseName,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            var browseDescriptions = new BrowseDescription[]
            {
                new()
                {
                    NodeId = parentNodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.BrowseName
                }
            };

            try
            {
                (ArrayOf<BrowseResult> results, _) = await ServerInternal.NodeManager
                    .BrowseAsync(
                        context,
                        null,
                        0,
                        browseDescriptions.ToArrayOf(),
                        cancellationToken).ConfigureAwait(false);

                if (results.Count == 0 || results[0].References.IsNull)
                {
                    return false;
                }

                foreach (ReferenceDescription reference in results[0].References)
                {
                    if (reference.BrowseName == browseName)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }
    }
}

