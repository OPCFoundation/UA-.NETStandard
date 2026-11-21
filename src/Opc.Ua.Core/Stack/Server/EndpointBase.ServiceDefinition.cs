// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;

namespace Opc.Ua
{
    public abstract partial class EndpointBase
    {
        /// <summary>
        /// Stores the definition of a service supported by the server.
        /// </summary>
        protected class ServiceDefinition
        {
            /// <summary>
            /// Initializes the object with its request type and implementation.
            /// </summary>
            /// <param name="requestType">Type of the request.</param>
            /// <param name="invokeService">The async invoke method.</param>
            public ServiceDefinition(Type requestType, InvokeService invokeService)
            {
                RequestType = requestType;
                m_invokeServiceAsync = invokeService;
            }

            /// <summary>
            /// The system type of the request object.
            /// </summary>
            /// <value>The type of the request.</value>
            public Type RequestType { get; }

            /// <summary>
            /// The system type of the request object.
            /// </summary>
            /// <value>The type of the response.</value>
            public Type ResponseType => RequestType;

            /// <summary>
            /// Processes the request asynchronously.
            /// </summary>
            /// <param name="request">The request.</param>
            /// <param name="secureChannelContext">The secure channel context.</param>
            /// <param name="requestLifetime">The request lifetime.</param>
            /// <returns></returns>
            public ValueTask<IServiceResponse> InvokeAsync(
                IServiceRequest request,
                SecureChannelContext secureChannelContext,
                RequestLifetime requestLifetime)
            {
                return m_invokeServiceAsync(request, secureChannelContext, requestLifetime);
            }

            private readonly InvokeService m_invokeServiceAsync;
        }
    }
}
