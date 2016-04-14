/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
