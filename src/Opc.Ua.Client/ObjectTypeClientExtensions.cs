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
