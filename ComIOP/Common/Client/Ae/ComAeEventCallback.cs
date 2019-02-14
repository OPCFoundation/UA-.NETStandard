/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;
using OpcRcw.Ae;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A class that implements the IOPCEventSink interface.
    /// </summary>
    internal class ComAeEventSink : OpcRcw.Ae.IOPCEventSink, IDisposable
    {
	    #region Constructors
	    /// <summary>
	    /// Initializes the object with the containing subscription object.
	    /// </summary>
	    public ComAeEventSink(ComAeSubscriptionClient subscription)
	    { 
            // save group.
            m_subscription = subscription;

		    // create connection point.
		    m_connectionPoint = new ConnectionPoint(subscription.Unknown, typeof(IOPCEventSink).GUID);

		    // advise.
		    m_connectionPoint.Advise(this);
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
            if (m_connectionPoint != null)
            {
                if (disposing)
                {
                    m_connectionPoint.Dispose();
                    m_connectionPoint = null;
                }
            }
        }
        #endregion

	    #region Public Properties
        /// <summary>
        /// Whether the callback is connected.
        /// </summary>
        public bool Connected 
        {
            get 
            {
                return m_connectionPoint != null;
            }
        }

        /// <summary>
        /// Gets the cookie returned by the server.
        /// </summary>
        /// <value>The cookie.</value>
        public int Cookie
        {
            get
            {
                if (m_connectionPoint != null)
                {
                    return m_connectionPoint.Cookie;
                }

                return 0;
            }
        }
        #endregion

	    #region ComAeEventSink Members
        /// <summary>
        /// Called when one or events are produce by the server.
        /// </summary>
        public void OnEvent(
            int hClientSubscription,
            int bRefresh,
            int bLastRefresh,
            int dwCount,
            ONEVENTSTRUCT[] pEvents)
	    {
		    try
		    {
                if (bRefresh == 0)
                {
			        m_subscription.OnEvent(pEvents);
                }
                else
                {
			        m_subscription.OnRefresh(pEvents, bLastRefresh != 0);
                }
		    }
		    catch (Exception e) 
		    {
                Utils.Trace(e, "Unexpected error processing OnEvent callback.");
		    }
	    }
	    #endregion

	    #region Private Members
	    private ComAeSubscriptionClient m_subscription;
	    private ConnectionPoint m_connectionPoint;
	    #endregion
    }
}
