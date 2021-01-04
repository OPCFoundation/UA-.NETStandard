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
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages requests from within the server.
    /// </summary>
    public class RequestManager : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initilizes the manager.
        /// </summary>
        /// <param name="server"></param>
        public RequestManager(IServerInternal server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            m_server = server;
            m_requests = new Dictionary<uint, OperationContext>();
            m_requestTimer = null;
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_requestTimer")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                List<OperationContext> operations = null;

                lock (m_requestsLock)
                {
                    operations = new List<OperationContext>(m_requests.Values);
                    m_requests.Clear();
                }

                foreach (OperationContext operation in operations)
                {
                    operation.SetStatusCode(StatusCodes.BadSessionClosed);
                }

                Utils.SilentDispose(m_requestTimer);
                m_requestTimer = null;
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Raised when the status of an outstanding request changes.
        /// </summary>
        public event RequestCancelledEventHandler RequestCancelled
        {
            add
            {
                lock (m_lock)
                {
                    m_RequestCancelled += value;
                }
            }

            remove
            {
                lock (m_lock)
                {
                    m_RequestCancelled -= value;
                }
            }
        }

        /// <summary>
        /// Called when a new request arrives.
        /// </summary>
        /// <param name="context"></param>
        public void RequestReceived(OperationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            lock (m_requestsLock)
            {
                m_requests.Add(context.RequestId, context);

                if (context.OperationDeadline < DateTime.MaxValue && m_requestTimer == null)
                {
                    m_requestTimer = new Timer(OnTimerExpired, null, 1000, 1000);
                }
            }
        }

        /// <summary>
        /// Called when a request completes (normally or abnormally).
        /// </summary>
        public void RequestCompleted(OperationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            lock (m_requestsLock)
            {
                // find the completed request.
                bool deadlineExists = false;

                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.RequestId == context.RequestId)
                    {
                        continue;
                    }

                    if (request.OperationDeadline < DateTime.MaxValue)
                    {
                        deadlineExists = true;
                    }
                }

                // check if the timer can be cancelled.
                if (m_requestTimer != null && !deadlineExists)
                {
                    m_requestTimer.Dispose();
                    m_requestTimer = null;
                }

                // remove the request.
                m_requests.Remove(context.RequestId);
            }
        }

        /// <summary>
        /// Called when the client wishes to cancel one or more requests.
        /// </summary>
        public void CancelRequests(uint requestHandle, out uint cancelCount)
        {
            List<uint> cancelledRequests = new List<uint>();

            // flag requests as cancelled.
            lock (m_requestsLock)
            {
                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.ClientHandle == requestHandle)
                    {
                        request.SetStatusCode(StatusCodes.BadRequestCancelledByRequest);
                        cancelledRequests.Add(request.RequestId);
                    }
                }
            }

            // return the number of requests found.
            cancelCount = (uint)cancelledRequests.Count;

            // raise notifications.
            lock (m_lock)
            {
                for (int ii = 0; ii < cancelledRequests.Count; ii++)
                {
                    if (m_RequestCancelled != null)
                    {
                        try
                        {
                            m_RequestCancelled(this, cancelledRequests[ii], StatusCodes.BadRequestCancelledByRequest);
                        }
                        catch (Exception e)
                        {
                            Utils.Trace(e, "Unexpected error reporting RequestCancelled event.");
                        }
                    }
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Checks for any expired requests and changes their status.
        /// </summary>
        private void OnTimerExpired(object state)
        {
            List<uint> expiredRequests = new List<uint>();

            // flag requests as expired.
            lock (m_requestsLock)
            {
                foreach (OperationContext request in m_requests.Values)
                {
                    if (request.OperationDeadline < DateTime.UtcNow)
                    {
                        request.SetStatusCode(StatusCodes.BadTimeout);
                        expiredRequests.Add(request.RequestId);
                    }
                }
            }

            // raise notifications.
            lock (m_lock)
            {
                for (int ii = 0; ii < expiredRequests.Count; ii++)
                {
                    if (m_RequestCancelled != null)
                    {
                        try
                        {
                            m_RequestCancelled(this, expiredRequests[ii], StatusCodes.BadTimeout);
                        }
                        catch (Exception e)
                        {
                            Utils.Trace(e, "Unexpected error reporting RequestCancelled event.");
                        }
                    }
                }
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private IServerInternal m_server;
        private Dictionary<uint, OperationContext> m_requests;
        private object m_requestsLock = new object();
        private Timer m_requestTimer;
        private event RequestCancelledEventHandler m_RequestCancelled;
        #endregion
    }

    /// <summary>
    /// Called when a request is cancelled.
    /// </summary>
    public delegate void RequestCancelledEventHandler(
        RequestManager source,
        uint requestId,
        StatusCode statusCode);
}
