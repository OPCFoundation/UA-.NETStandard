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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Low-level experimental Service delegations. Retained operations use a same-Session request context.
    /// </summary>
    public partial class ManagedSession
    {
        /// <inheritdoc/>
        public ValueTask<SubmitServiceResponse> SubmitServiceAsync(
            RequestHeader? requestHeader,
            Uuid clientOperationId,
            ExtensionObject request,
            double executionAllowance,
            double resultRetentionTime,
            double waitTime,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, clientOperationId, request, executionAllowance, resultRetentionTime, waitTime),
                static (session, args, token) => session.SubmitServiceAsync(
                    args.requestHeader, args.clientOperationId, args.request,
                    args.executionAllowance, args.resultRetentionTime, args.waitTime, token),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<GetServiceResultResponse> GetServiceResultAsync(
            RequestHeader? requestHeader,
            Uuid clientOperationId,
            ByteString serverToken,
            double waitTime,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, clientOperationId, serverToken, waitTime),
                static (session, args, token) => session.GetServiceResultAsync(
                    args.requestHeader, args.clientOperationId, args.serverToken, args.waitTime, token),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<ControlServiceExecutionResponse> ControlServiceExecutionAsync(
            RequestHeader? requestHeader,
            ByteString serverToken,
            ServiceOperationControlAction action,
            CancellationToken ct)
        {
            return CallServiceAsync(
                (requestHeader, serverToken, action),
                static (session, args, token) => session.ControlServiceExecutionAsync(
                    args.requestHeader, args.serverToken, args.action, token),
                ct);
        }
    }
}
