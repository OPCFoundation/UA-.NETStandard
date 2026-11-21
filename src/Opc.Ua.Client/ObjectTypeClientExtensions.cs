// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Preserves method statuses and argument diagnostics when invoking a generated ObjectType proxy.
    /// </summary>
    public static class ObjectTypeClientExtensions
    {
        /// <summary>
        /// Invokes one method without retrying and returns its complete result, including non-default Good statuses.
        /// A bad service response throws; a bad method result remains available for the caller to inspect.
        /// </summary>
        /// <exception cref="ArgumentNullException">The proxy is null.</exception>
        /// <exception cref="ServiceResultException">
        /// The method namespace is absent or the service response is invalid.
        /// </exception>
        public static async ValueTask<CallMethodResult> CallWithResultAsync(
            this ObjectTypeClient client,
            ExpandedNodeId methodId,
            ArrayOf<Variant> arguments,
            CancellationToken ct = default)
        {
            if (client is null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            var localMethod = ExpandedNodeId.ToNodeId(methodId, client.Session.MessageContext.NamespaceUris);
            if (methodId.ServerIndex != 0 || localMethod.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The method is not available in the current session's namespaces.");
            }
            var request = new CallMethodRequest
            {
                ObjectId = client.ObjectId,
                MethodId = localMethod,
                InputArguments = arguments
            };
            ArrayOf<CallMethodRequest> requests = [request];
            CallResponse response = await client.Session.CallAsync(null, requests, ct).ConfigureAwait(false);
            if (StatusCode.IsBad(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            return response.Results[0];
        }
    }
}
