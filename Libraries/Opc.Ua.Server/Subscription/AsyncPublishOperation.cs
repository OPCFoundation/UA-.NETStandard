/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores the state of an asynchronous publish operation.
    /// </summary>
    public class AsyncPublishOperation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncPublishOperation"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="request">The request.</param>
        /// <param name="server">The server.</param>
        public AsyncPublishOperation(
            OperationContext context,
            IEndpointIncomingRequest request,
            StandardServer server)
        {
            Context = context;
            m_request = request;
            m_server = server;
            Response = new PublishResponse();
            m_request.Calldata = this;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_request.OperationCompleted(null, StatusCodes.BadServerHalted);
            }
        }

        /// <summary>
        /// Gets the context.
        /// </summary>
        /// <value>The context.</value>
        public OperationContext Context { get; }

        /// <summary>
        /// Gets the request handle.
        /// </summary>
        /// <value>The request handle.</value>
        public uint RequestHandle => m_request.Request.RequestHeader.RequestHandle;

        /// <summary>
        /// Gets the response.
        /// </summary>
        /// <value>The response.</value>
        public PublishResponse Response { get; }

        /// <summary>
        /// Gets the calldata.
        /// </summary>
        /// <value>The calldata.</value>
        public object Calldata { get; private set; }

        /// <summary>
        /// Schedules a thread to complete the request.
        /// </summary>
        /// <param name="calldata">The data that is used to complete the operation</param>
        public void CompletePublish(object calldata)
        {
            Calldata = calldata;
            m_server.ScheduleIncomingRequest(m_request);
        }

        private readonly IEndpointIncomingRequest m_request;
        private readonly StandardServer m_server;
    }
}
