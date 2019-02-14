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

namespace Opc.Ua.Com.Client
{        
    /// <summary>
    /// A class that implements the IOPCShutdown interface.
    /// </summary>
    public class ShutdownCallback : OpcRcw.Comn.IOPCShutdown, IDisposable
    {
	    #region Constructors
	    /// <summary>
	    /// Initializes the object with the containing subscription object.
	    /// </summary>
	    public ShutdownCallback(object server, ServerShutdownEventHandler handler)
	    { 
		    try
		    {
                m_server  = server;
                m_handler = handler;

			    // create connection point.
			    m_connectionPoint = new ConnectionPoint(server, typeof(OpcRcw.Comn.IOPCShutdown).GUID);

			    // advise.
			    m_connectionPoint.Advise(this);
		    }
		    catch (Exception e)
		    {
			    throw new ServiceResultException(e, StatusCodes.BadOutOfService);
		    }
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
                m_connectionPoint.Dispose();
                m_connectionPoint = null;
            }
        }
        #endregion

	    #region IOPCShutdown Members
	    /// <summary>
	    /// Called when a data changed event is received.
	    /// </summary>
	    public void ShutdownRequest(string szReason)
	    {
		    try
		    {
                if (m_handler != null)
                {
                    m_handler(m_server, szReason);
                }
		    }
		    catch (Exception e) 
		    { 
                Utils.Trace(e, "Unexpected error processing callback.");
		    }
	    }
	    #endregion

	    #region Private Members
	    private object m_server;
	    private ServerShutdownEventHandler m_handler;
	    private ConnectionPoint m_connectionPoint;
	    #endregion
    }
    
    /// <summary>
    /// A delegate used to receive server shutdown events.
    /// </summary>
    public delegate void ServerShutdownEventHandler(object sender, string reason);
}
