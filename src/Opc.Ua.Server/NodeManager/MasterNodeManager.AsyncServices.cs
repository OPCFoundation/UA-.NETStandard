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
                    systemContext, QualifiedName.From("SupportsRecoverableExecution"));
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
                default:
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
        }

        /// <summary>
        /// Reads current effective access metadata, including UserAccessLevel callbacks, without reading the Value.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask ValidateRetainedValueAccessAsync(
            OperationContext context, NodeId nodeId, CancellationToken ct)
        {
            (object? handle, IAsyncNodeManager? owner) = await GetManagerHandleAsync(nodeId, ct).ConfigureAwait(false);
            NodeMetadata? metadata = handle == null || owner == null
                ? null
                : await owner.GetNodeMetadataAsync(context, handle, BrowseResultMask.NodeClass, ct)
                    .ConfigureAwait(false);
            if (metadata == null ||
                (metadata.NodeClass == NodeClass.Variable && (metadata.AccessLevel & AccessLevels.CurrentRead) == 0))
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
