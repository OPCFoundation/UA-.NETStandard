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

using System;
using System.Collections;
using System.Diagnostics;
using System.Xml;
using System.Threading;

namespace Opc.Ua.Server 
{
    /// <summary>
    /// Stores the state of an asynchrounous publish operation.
    /// </summary>  
    public class AsyncPublishOperation
    {
        #region Constructors
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
            m_context = context;
            m_request = request;
            m_server = server;
            m_response = new PublishResponse();
            m_request.Calldata = this;
        }
        #endregion

        #region IDisposable Members
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
        #endregion

        #region Public Members
        /// <summary>
        /// Gets the context.
        /// </summary>
        /// <value>The context.</value>
        public OperationContext Context
        {
            get { return m_context; }
        }

        /// <summary>
        /// Gets the request handle.
        /// </summary>
        /// <value>The request handle.</value>
        public uint RequestHandle
        {
            get { return m_request.Request.RequestHeader.RequestHandle; }
        }

        /// <summary>
        /// Gets the response.
        /// </summary>
        /// <value>The response.</value>
        public PublishResponse Response
        {
            get { return m_response; }
        }

        /// <summary>
        /// Gets the calldata.
        /// </summary>
        /// <value>The calldata.</value>
        public object Calldata
        {
            get { return m_calldata; }
        }

        /// <summary>
        /// Schedules a thread to complete the request.
        /// </summary>
        /// <param name="calldata">The data that is used to complete the operation</param>
        public void CompletePublish(object calldata)
        {
            m_calldata = calldata;
            m_server.ScheduleIncomingRequest(m_request);
        }
        #endregion

        #region Private Fields
        private IEndpointIncomingRequest m_request;
        private StandardServer m_server;
        private OperationContext m_context;
        private PublishResponse m_response;
        private object m_calldata;
        #endregion
    }
}
