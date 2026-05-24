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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Allows tests and diagnostics tools to mutate the service response
    /// produced by the server before it is returned to the client. The
    /// mutator is invoked from <see cref="EndpointBase"/> after the
    /// service has produced the response and before the response is
    /// sent on the wire. Production servers should leave
    /// <see cref="IServerBase.ResponseMutator"/> null. Test code can
    /// install an implementation to inject service-result error codes,
    /// alter individual response fields (e.g. nonces, IDs, arrays), or
    /// otherwise simulate a misbehaving server for client conformance
    /// testing.
    /// </summary>
    public interface IServiceResponseMutator
    {
        /// <summary>
        /// Returns the (possibly mutated) response that should be sent
        /// back to the client. Implementations are free to return the
        /// same instance, modify it in-place, or return a new instance.
        /// Implementations must be thread-safe — multiple service calls
        /// can be in flight on the same server simultaneously.
        /// </summary>
        /// <param name="request">The service request being processed.</param>
        /// <param name="response">The response produced by the server.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The response to send to the client.</returns>
        ValueTask<IServiceResponse> MutateResponseAsync(
            IServiceRequest request,
            IServiceResponse response,
            CancellationToken cancellationToken = default);
    }
}
