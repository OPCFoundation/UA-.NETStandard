// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
