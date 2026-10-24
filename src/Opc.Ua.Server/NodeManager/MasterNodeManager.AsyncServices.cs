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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reuses ordinary node permission checks when a retained Service response is selected for delivery.
    /// </summary>
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Rejects a recoverable Call batch before any dispatch when a callable Method declares current ineligibility.
        /// Missing declarations remain undeclared; ordinary per-item permission and validation failures are preserved.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask ValidateRecoverableServiceEligibilityAsync(
            OperationContext context, IServiceRequest request, CancellationToken ct)
        {
            if (request is not CallRequest call)
            {
                return;
            }
            ServerSystemContext systemContext = Server.DefaultSystemContext.Copy(context);
            for (int i = 0; i < call.MethodsToCall.Count; i++)
            {
                CallMethodRequest item = call.MethodsToCall[i];
                ServiceResult validation = await m_serviceDispatch.ValidateCallRequestItemAsync(
                    context, item, permissionsOnly: true, cancellationToken: ct).ConfigureAwait(false);
                if (ServiceResult.IsBad(validation))
                {
                    continue;
                }
                (_, IAsyncNodeManager? owner) = await GetManagerHandleAsync(item.ObjectId, ct).ConfigureAwait(false);
                if (owner == null)
                {
                    continue;
                }
                MethodState method;
                try
                {
                    method = await owner.FindMethodStateAsync(context, item, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // A resolution failure remains an ordinary per-item Call result.
                    continue;
                }
                if (method == null ||
                    !ReadBooleanAttribute(method, systemContext, Attributes.Executable, out bool executable) ||
                    !executable ||
                    !ReadBooleanAttribute(method, systemContext, Attributes.UserExecutable, out bool userExecutable) ||
                    !userExecutable)
                {
                    continue;
                }
                BaseInstanceState? hint = method.FindChild(
                    systemContext, new QualifiedName(
                        "SupportsRecoverableExecution", Server.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse)));
                if (hint != null &&
                    (hint is not BaseVariableState property ||
                        !ReadBooleanAttribute(property, systemContext, Attributes.Value, out bool supported) ||
                        !supported))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServiceUnsupported,
                        "A Method does not support recoverable execution under the current Server policy.");
                }
            }
        }

        /// <summary>
        /// Re-evaluates the original successful permission checks without replaying any original work.
        /// </summary>
        internal async ValueTask ValidateRetainedPermissionsAsync(
            OperationContext context, ArrayOf<RecoverableNodePermission> permissions, CancellationToken ct)
        {
            for (int i = 0; i < permissions.Count; i++)
            {
                RecoverableNodePermission permission = permissions[i];
                if ((permission.Permission & PermissionType.AddNode) != 0 &&
                    permission.NodeId.TryGetValue(out uint identifier) &&
                    identifier == 0)
                {
                    RequireAccess(await ValidateAddNodeNamespacePermissionAsync(
                        context, permission.NodeId.NamespaceIndex, ct).ConfigureAwait(false));
                }
                else
                {
                    RequireAccess(await m_serviceDispatch.ValidatePermissionsAsync(
                        context, permission.NodeId, permission.Permission, permissionsOnly: true, cancellationToken: ct)
                        .ConfigureAwait(false));
                }
            }
        }

        /// <summary>
        /// Rechecks the invoked manager Method's current Call permission and executable attributes after waiting.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask ValidateRecoverableManagerMethodAsync(
            OperationContext context, CallMethodRequest request, CancellationToken ct)
        {
            RequireAccess(await m_serviceDispatch.ValidateCallRequestItemAsync(
                context, request, permissionsOnly: true, cancellationToken: ct).ConfigureAwait(false));
            (_, IAsyncNodeManager? owner) = await GetManagerHandleAsync(request.ObjectId, ct).ConfigureAwait(false);
            if (owner == null)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            MethodState method = await owner.FindMethodStateAsync(context, request, ct).ConfigureAwait(false);
            ServerSystemContext systemContext = Server.DefaultSystemContext.Copy(context);
            if (method == null ||
                !ReadBooleanAttribute(method, systemContext, Attributes.Executable, out bool executable) ||
                !executable ||
                !ReadBooleanAttribute(method, systemContext, Attributes.UserExecutable, out bool userExecutable) ||
                !userExecutable)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Revalidates successful per-item results without executing any item or changing the stored response.
        /// Original permission failures remain original per-item results, not a new admission transaction.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask ValidateRetainedServiceResultAsync(
            OperationContext context, IServiceRequest request, IServiceResponse response, CancellationToken ct)
        {
            if (response is ServiceFault)
            {
                return;
            }
            switch (request)
            {
                case ReadRequest read when response is ReadResponse result &&
                    result.Results.Count == read.NodesToRead.Count:
                    for (int i = 0; i < read.NodesToRead.Count; i++)
                    {
                        if (!StatusCode.IsBad(result.Results[i].StatusCode))
                        {
                            RequireAccess(await m_serviceDispatch.ValidateReadRequestAsync(
                                context, read.NodesToRead[i], permissionsOnly: true, cancellationToken: ct)
                                .ConfigureAwait(false));
                            if (read.NodesToRead[i].AttributeId == Attributes.Value)
                            {
                                await ValidateRetainedValueAccessAsync(context, read.NodesToRead[i].NodeId, ct)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                    return;
                case WriteRequest write when response is WriteResponse result &&
                    result.Results.Count == write.NodesToWrite.Count:
                    for (int i = 0; i < write.NodesToWrite.Count; i++)
                    {
                        if (!StatusCode.IsBad(result.Results[i]))
                        {
                            RequireAccess(await m_serviceDispatch.ValidateWriteRequestAsync(
                                context, write.NodesToWrite[i], permissionsOnly: true, cancellationToken: ct)
                                .ConfigureAwait(false));
                        }
                    }
                    return;
                case CallRequest call when response is CallResponse result &&
                    result.Results.Count == call.MethodsToCall.Count:
                    for (int i = 0; i < call.MethodsToCall.Count; i++)
                    {
                        if (StatusCode.IsBad(result.Results[i].StatusCode))
                        {
                            continue;
                        }
                        CallMethodRequest item = call.MethodsToCall[i];
                        RequireAccess(await m_serviceDispatch.ValidatePermissionsAsync(
                            context, item.ObjectId, PermissionType.Call, permissionsOnly: true, cancellationToken: ct)
                            .ConfigureAwait(false));
                        RequireAccess(await m_serviceDispatch.ValidateCallRequestItemAsync(
                            context, item, permissionsOnly: true, cancellationToken: ct).ConfigureAwait(false));
                    }
                    return;
                case HistoryReadRequest history when response is HistoryReadResponse result &&
                    result.Results.Count == history.NodesToRead.Count:
                    for (int i = 0; i < history.NodesToRead.Count; i++)
                    {
                        if (!StatusCode.IsBad(result.Results[i].StatusCode))
                        {
                            RequireAccess(await m_serviceDispatch.ValidateHistoryReadRequestAsync(
                                context, history.NodesToRead[i], ct).ConfigureAwait(false));
                            await ValidateRetainedValueAccessAsync(
                                context, history.NodesToRead[i].NodeId, ct, AccessLevels.HistoryRead)
                                .ConfigureAwait(false);
                        }
                    }
                    return;
                case HistoryUpdateRequest history when response is HistoryUpdateResponse result &&
                    result.Results.Count == history.HistoryUpdateDetails.Count:
                    for (int i = 0; i < history.HistoryUpdateDetails.Count; i++)
                    {
                        if (!StatusCode.IsBad(result.Results[i].StatusCode))
                        {
                            if (!history.HistoryUpdateDetails[i].TryGetValue(out HistoryUpdateDetails? details))
                            {
                                throw new ServiceResultException(StatusCodes.BadDecodingError);
                            }
                            RequireAccess(await m_serviceDispatch.ValidateHistoryUpdateRequestAsync(
                                context, details, ct).ConfigureAwait(false));
                        }
                    }
                    return;
                case BrowseRequest browse when response is BrowseResponse result &&
                    result.Results.Count == browse.NodesToBrowse.Count:
                    await ValidateRetainedBrowseTargetsAsync(context, result.Results, ct).ConfigureAwait(false);
                    return;
                case BrowseNextRequest browse when response is BrowseNextResponse result &&
                    (result.Results.Count == browse.ContinuationPoints.Count ||
                        (browse.ReleaseContinuationPoints && result.Results.Count == 0)):
                    await ValidateRetainedBrowseTargetsAsync(context, result.Results, ct).ConfigureAwait(false);
                    return;
                case TranslateBrowsePathsToNodeIdsRequest translate
                    when response is TranslateBrowsePathsToNodeIdsResponse result &&
                        result.Results.Count == translate.BrowsePaths.Count:
                    for (int i = 0; i < result.Results.Count; i++)
                    {
                        BrowsePathResult path = result.Results[i];
                        if (!StatusCode.IsBad(path.StatusCode))
                        {
                            for (int j = 0; j < path.Targets.Count; j++)
                            {
                                await ValidateRetainedBrowseTargetAsync(context, path.Targets[j].TargetId, ct)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                    return;
                case RegisterNodesRequest register when response is RegisterNodesResponse result &&
                    result.RegisteredNodeIds.Count == register.NodesToRegister.Count:
                    return;
                case UnregisterNodesRequest when response is UnregisterNodesResponse:
                    return;
                case AddNodesRequest add when response is AddNodesResponse result &&
                    result.Results.Count == add.NodesToAdd.Count:
                    return;
                case AddReferencesRequest add when response is AddReferencesResponse result &&
                    result.Results.Count == add.ReferencesToAdd.Count:
                    return;
                case DeleteNodesRequest delete when response is DeleteNodesResponse result &&
                    result.Results.Count == delete.NodesToDelete.Count:
                    return;
                case DeleteReferencesRequest delete when response is DeleteReferencesResponse result &&
                    result.Results.Count == delete.ReferencesToDelete.Count:
                    return;
                default:
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
        }

        /// <summary>
        /// Checks current visibility of returned reference targets while preserving the immutable result.
        /// </summary>
        private async ValueTask ValidateRetainedBrowseTargetsAsync(
            OperationContext context, ArrayOf<BrowseResult> results, CancellationToken ct)
        {
            for (int i = 0; i < results.Count; i++)
            {
                BrowseResult result = results[i];
                if (!StatusCode.IsBad(result.StatusCode))
                {
                    for (int j = 0; j < result.References.Count; j++)
                    {
                        await ValidateRetainedBrowseTargetAsync(context, result.References[j].NodeId, ct)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Revalidates local targets; an external target remains governed by its local source's captured check.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask ValidateRetainedBrowseTargetAsync(
            OperationContext context, ExpandedNodeId target, CancellationToken ct)
        {
            if (target.ServerIndex == 0)
            {
                var local = ExpandedNodeId.ToNodeId(target, Server.NamespaceUris);
                if (local.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                RequireAccess(await m_serviceDispatch.ValidatePermissionsAsync(
                    context, local, PermissionType.Browse, permissionsOnly: true, cancellationToken: ct)
                    .ConfigureAwait(false));
            }
        }

        /// <summary>
        /// Reads current effective access metadata, including UserAccessLevel callbacks, without reading the Value.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask ValidateRetainedValueAccessAsync(
            OperationContext context,
            NodeId nodeId,
            CancellationToken ct,
            byte requiredAccess = AccessLevels.CurrentRead)
        {
            (object? handle, IAsyncNodeManager? owner) = await GetManagerHandleAsync(nodeId, ct).ConfigureAwait(false);
            NodeMetadata? metadata = handle == null || owner == null
                ? null
                : await owner.GetNodeMetadataAsync(context, handle, BrowseResultMask.NodeClass, ct)
                    .ConfigureAwait(false);
            if (metadata == null ||
                (metadata.NodeClass == NodeClass.Variable && (metadata.AccessLevel & requiredAccess) == 0))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Denies disclosure when current permissions or target metadata no longer establish access.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void RequireAccess(ServiceResult? result)
        {
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Evaluates a current Boolean attribute through its normal metadata callback without invoking a Method.
        /// </summary>
        private static bool ReadBooleanAttribute(
            NodeState node, ISystemContext context, uint attributeId, out bool value)
        {
            Variant attribute = default;
            DateTimeUtc timestamp = default;
            ServiceResult result = node.ReadAttribute(context, attributeId, ref attribute, ref timestamp);
            value = false;
            return ServiceResult.IsGood(result) && attribute.TryGetValue(out value);
        }
    }
}
