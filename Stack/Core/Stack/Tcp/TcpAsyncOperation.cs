/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Stores the results of an asynchronous operation.
    /// </summary>
    public class TcpAsyncOperation<T> : IAsyncResult, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with a callback
        /// </summary>
        public TcpAsyncOperation(int timeout, AsyncCallback callback, object asyncState)
        {               
            m_callback    = callback;
            m_asyncState  = asyncState;
            m_synchronous = false;
            m_completed   = false;

            if (timeout > 0 && timeout != Int32.MaxValue)
            {
                m_timer = new Timer(new TimerCallback(OnTimeout), null, timeout, Timeout.Infinite);
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
            if (disposing) 
            {
                lock (m_lock)
                {
                    Utils.SilentDispose(m_timer);
                    m_timer = null;

                    if (m_event != null)
                    {
                        m_event.Set();
                        m_event.Dispose();
                        m_event = null;
                    }
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Complete(T response)
        {
            return InternalComplete(true, response);
        }
        
        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Complete(bool doNotBlock, T response)
        {
            return InternalComplete(doNotBlock, response);
        }
        
        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(ServiceResult error)
        {
            return InternalComplete(true, error);
        }
        
        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(bool doNotBlock, ServiceResult error)
        {
            return InternalComplete(doNotBlock, error);
        }
        
        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(uint code, string format, params object[] args)
        {
            return InternalComplete(true, ServiceResult.Create(code, format, args));
        }
        
        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(bool doNotBlock, uint code, string format, params object[] args)
        {
            return InternalComplete(doNotBlock, ServiceResult.Create(code, format, args));
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(Exception e, uint defaultCode, string format, params object[] args)
        {
            return InternalComplete(true, ServiceResult.Create(e, defaultCode, format, args));
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        public bool Fault(bool doNotBlock, Exception e, uint defaultCode, string format, params object[] args)
        {
            return InternalComplete(doNotBlock, ServiceResult.Create(e, defaultCode, format, args));
        }

        /// <summary>
        /// The response returned from the server.
        /// </summary>
        public T End(int timeout)
        {
            // check if the request has already completed.
            bool mustWait = false;

            lock (m_lock)
            {
                mustWait = !m_completed;

                if (mustWait)
                {
                    m_event = new ManualResetEvent(false);
                }
            }

            // wait for completion.
            if (mustWait)
            {
                if (!m_event.WaitOne(timeout))
                {
                    throw new ServiceResultException(StatusCodes.BadRequestInterrupted);
                }
            }

            // return the response.
            lock (m_lock)
            {
                if (m_error != null)
                {
                    throw new ServiceResultException(m_error);
                }

                return m_response;
            }
        }

        /// <summary>
        /// Stores additional state information associated with the operation.
        /// </summary>
        public IDictionary<string,object> Properties
        {
            get
            {                
                lock (m_lock)
                {
                    if (m_properties == null)
                    {
                        m_properties = new Dictionary<string,object>();
                    }

                    return m_properties;
                }
            }
        }
        #endregion

        #region IAsyncResult Members
        /// <summary cref="IAsyncResult.AsyncState" />
        public object AsyncState
        {
            get 
            {
                lock (m_lock)
                {
                    return m_asyncState;
                }
            }
        }
        
        /// <summary cref="IAsyncResult.AsyncWaitHandle" />
        public WaitHandle AsyncWaitHandle
        {
            get 
            {
                lock (m_lock)
                {
                    if (m_event == null)
                    {
                        m_event = new ManualResetEvent(m_completed);
                    }

                    return m_event;
                }
            }
        }
        
        /// <summary cref="IAsyncResult.CompletedSynchronously" />
        public bool CompletedSynchronously
        {
            get 
            {
                lock (m_lock)
                {
                    return m_synchronous;
                }
            }
        }
        
        /// <summary cref="IAsyncResult.IsCompleted" />
        public bool IsCompleted
        {
            get 
            {
                lock (m_lock)
                {
                    return m_completed;
                }
            }
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Called when the operation times out.
        /// </summary>
        private void OnTimeout(object state)
        {
            InternalComplete(false, new ServiceResult(StatusCodes.BadRequestTimeout));
        }

        /// <summary>
        /// Called when an asynchronous operation completes.
        /// </summary>
        protected virtual bool InternalComplete(bool doNotBlock, object result)
        {
            lock (m_lock)
            {
                // ignore multiple calls (i.e. a timeout after a response or vise versa).
                if (m_completed)
                {
                    return false;
                }

                if (result is T)
                {
                    m_response = (T)result;
                }
                else
                {
                    m_error = result as ServiceResult;
                }

                m_completed = true;

                if (m_timer != null)
                {
                    m_timer.Dispose();
                    m_timer = null;
                }

                if (m_event != null)
                {
                    m_event.Set();
                }
            }

            if (m_callback != null)
            {
                if (doNotBlock)
                {
                    Task.Run(() =>
                    {
                        m_callback(this);
                    });
                }
                else
                {
                    try
                    {
                        m_callback(this);
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "TcpClientChannel: Unexpected error invoking AsyncCallback.");
                    }
                }
            }
                    
            return true;
        }
        #endregion
        
        #region Private Fields
        private object m_lock = new object();
        private AsyncCallback m_callback;
        private object m_asyncState;
        private bool m_synchronous;
        private bool m_completed;
        private ManualResetEvent m_event;
        private T m_response;
        private ServiceResult m_error;
        private Timer m_timer;
        private Dictionary<string,object> m_properties;
        #endregion
    }
}
