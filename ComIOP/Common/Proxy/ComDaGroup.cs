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
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Client;

namespace Opc.Ua.Com
{
    /// <summary>
    /// A class that implements a COM DA group.
    /// </summary>
    public class ComDaGroup : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaGroup"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="serverHandle">The server handle.</param>
        public ComDaGroup(string name, int serverHandle)
		{
            m_name = name;
            m_serverHandle = serverHandle;
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~ComDaGroup() 
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
            if (disposing)
            {
                // TBD
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the handle.
        /// </summary>
        /// <value>The handle.</value>
        public IntPtr Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// Gets the name of the group.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return m_name; }
        }

        /// <summary>
        /// Gets the server handle.
        /// </summary>
        /// <value>The server handle.</value>
        public int ServerHandle
        {
            get { return m_serverHandle; }
        }

        /// <summary>
        /// Gets the update rate.
        /// </summary>
        /// <value>The update rate.</value>
        public int UpdateRate
        {
            get { return m_updateRate; }
        }

        /// <summary>
        /// Gets the actual update rate.
        /// </summary>
        /// <value>The actual update rate.</value>
        public int ActualUpdateRate
        {
            get { return m_actualUpdateRate; }
        }

        /// <summary>
        /// Creates the specified update rate.
        /// </summary>
        /// <param name="updateRate">The update rate.</param>
        public void Create(int updateRate)
        {
            m_updateRate = updateRate;
            m_actualUpdateRate = updateRate;
        }
        #endregion

        #region Private Methods
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private IntPtr m_handle;
        private int m_serverHandle;
        private string m_name;
        private int m_updateRate;
        private int m_actualUpdateRate;
        #endregion
	}
}
