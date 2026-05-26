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
#if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
    /// <summary>
    /// The base interface for all services exposed by UA servers.
    /// </summary>
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
    public interface IEndpointBase
    {
        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Response message.</returns>
        [OperationContract(
            Action = Namespaces.OpcUaWsdl + "/InvokeService",
            ReplyAction = Namespaces.OpcUaWsdl + "/InvokeServiceResponse"
        )]
        InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request);
    }
#else
    /// <summary>
    /// The base asynchronous interface for all services exposed by UA servers.
    /// </summary>
    public interface IEndpointBase
    {
        /// <summary>
        /// The operation contract for the InvokeService service.
        /// </summary>
        Task<InvokeServiceResponseMessage> InvokeServiceAsync(
            InvokeServiceMessage request,
            SecureChannelContext secureChannelContext,
            CancellationToken cancellationToken);
    }
#endif
}
