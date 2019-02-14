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
using System.Text;
using System.Globalization;
using System.Security.Principal;
using System.Threading;
using System.Runtime.InteropServices;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Da;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A base class for COM server wrappers.
    /// </summary>
    public class ComObject : IDisposable
    {
        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~ComObject() 
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
            // release references to the server during garbage collection.
            if (!disposing)
            {
                ReleaseServer();
            }

            // clean up managed objects if 
            if (disposing)
            {
                lock (m_lock)
                {
                    m_disposed = true;

                    // only release server if there are no outstanding calls.
                    // if it is not released here it will be released when the last call completes.
                    if (m_outstandingCalls <= 0)
                    {
                        ReleaseServer();
                    }
                }
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets an object which is used to synchronize access to the COM object.
        /// </summary>
        /// <value>An object which is used to synchronize access to the COM object.</value>
        public object Lock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="ComObject"/> is disposed.
        /// </summary>
        /// <value><c>true</c> if disposed; otherwise, <c>false</c>.</value>
        public bool Disposed
        {
            get { return m_disposed; }
        }

        /// <summary>
        /// Gets or sets the COM server.
        /// </summary>
        /// <value>The COM server.</value>
        public object Unknown
        {
            get { return m_unknown; }
            set { m_unknown = value; }
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// Releases all references to the server.
        /// </summary>
        protected virtual void ReleaseServer()
        {
            lock (m_lock)
            {
                ComUtils.ReleaseServer(m_unknown);
                m_unknown = null;
            }
        }

        /// <summary>
        /// Checks if the server supports the specified interface.
        /// </summary>
        /// <typeparam name="T">The interface to check.</typeparam>
        /// <returns>True if the server supports the interface.</returns>
        protected bool SupportsInterface<T>() where T : class
        {
            lock (m_lock)
            {
                return m_unknown is T;
            }
        }

        /// <summary>
        /// Must be called before any COM call.
        /// </summary>
        /// <typeparam name="T">The interface to used when making the call.</typeparam>
        /// <param name="methodName">Name of the method.</param>
        /// <param name="throwOnError">if set to <c>true</c> an exception is thrown on error.</param>
        /// <returns></returns>
        protected T BeginComCall<T>(string methodName, bool throwOnError) where T : class
        {
            Utils.Trace(Utils.TraceMasks.ExternalSystem, "{0} called.", methodName);

            lock (m_lock)
            {
                m_outstandingCalls++;

                if (m_disposed)
                {
                    if (throwOnError)
                    {
                        throw new ObjectDisposedException("The COM server has been disposed.");
                    }

                    return null;
                }

                T server = m_unknown as T;

                if (throwOnError && server == null)
                {
                    throw new NotSupportedException(Utils.Format("COM interface '{0}' is not supported by server.", typeof(T).Name));
                }

                return server;
            }
        }

        /// <summary>
        /// Must called if a COM call returns an unexpected exception.
        /// </summary>
        /// <param name="methodName">Name of the method.</param>
        /// <param name="e">The exception.</param>
        /// <remarks>Note that some COM calls are expected to return errors.</remarks>
        protected void ComCallError(string methodName, Exception e)
        {
            ComUtils.TraceComError(e, methodName);
        }

        /// <summary>
        /// Must be called in the finally block after making a COM call.
        /// </summary>
        /// <param name="methodName">Name of the method.</param>
        protected void EndComCall(string methodName)
        {
            Utils.Trace(Utils.TraceMasks.ExternalSystem, "{0} completed.", methodName);

            lock (m_lock)
            {
                m_outstandingCalls--;

                if (m_disposed && m_outstandingCalls <= 0)
                {
                    ComUtils.ReleaseServer(m_unknown);
                }
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private int m_outstandingCalls;
        private bool m_disposed;
        private object m_unknown;
        #endregion
    }
}
