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
