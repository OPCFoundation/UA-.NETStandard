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
using System.Runtime.InteropServices;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Client
{
	/// <summary>
	/// Manages a connection point with a COM server.
	/// </summary>
	public class ConnectionPoint : IDisposable
	{
		#region Constructors
		/// <summary>
		/// Initializes the object by finding the specified connection point.
		/// </summary>
		public ConnectionPoint(object server, Guid iid)
		{
            OpcRcw.Comn.IConnectionPointContainer cpc = server as OpcRcw.Comn.IConnectionPointContainer;

            if (cpc == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, "Server does not support the IConnectionPointContainer interface.");
            }

            cpc.FindConnectionPoint(ref iid, out m_server);
		}

		/// <summary>
		/// Sets private members to default values.
		/// </summary>
		private void Initialize()
		{
			m_server = null;
			m_cookie = 0;
			m_refs   = 0;
		}
		#endregion
    
	    #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~ConnectionPoint() 
        {
            Dispose(false);
        }
        
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            object server = System.Threading.Interlocked.Exchange(ref m_server, null);

            if (server != null)
            {
                ComUtils.ReleaseServer(server);
            }
        }
        #endregion
        
		#region Public Properties
        /// <summary>
        /// The cookie returned by the server.
        /// </summary>
        public int Cookie
        {
            get { return m_cookie; }
        }
        #endregion

		#region IConnectionPoint Members
		/// <summary>
		/// Establishes a connection, if necessary and increments the reference count.
		/// </summary>
		public int Advise(object callback)
		{
            if (m_refs++ == 0)
            {
                m_server.Advise(callback, out m_cookie);
            }

			return m_refs;
		}

		/// <summary>
		/// Decrements the reference count and closes the connection if no more references.
		/// </summary>
		public int Unadvise()
		{
			if (--m_refs == 0) m_server.Unadvise(m_cookie);
			return m_refs;
		}
		#endregion

		#region Private Members
        private OpcRcw.Comn.IConnectionPoint m_server;
		private int m_cookie;
		private int m_refs;
		#endregion
	}
}
