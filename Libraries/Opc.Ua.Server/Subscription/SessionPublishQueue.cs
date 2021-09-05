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
using System.Text;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the publish queues for a session.
    /// </summary>
    public class SessionPublishQueue : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Creates a new queue.
        /// </summary>
        public SessionPublishQueue(IServerInternal server, Session session, int maxPublishRequests)
        {
            if (server == null)  throw new ArgumentNullException(nameof(server));
            if (session == null) throw new ArgumentNullException(nameof(session));

            m_server              = server;
            m_session             = session;
            m_publishEvent        = new ManualResetEvent(false);
            m_queuedRequests      = new LinkedList<QueuedRequest>();
            m_queuedSubscriptions = new List<QueuedSubscription>();
            m_maxPublishRequests  = maxPublishRequests;
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
                    m_publishEvent.Set();

                    while (m_queuedRequests.Count > 0)
                    {
                        QueuedRequest request = m_queuedRequests.First.Value;
                        m_queuedRequests.RemoveFirst();

                        try
                        {
                            request.Error = StatusCodes.BadServerHalted;
                            request.Dispose();
                        }
                        catch (Exception)
                        {
                            // ignore errors.
                        }
                    }

                    m_queuedSubscriptions.Clear();
                    m_publishEvent.Dispose();
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Clears the queues because the session is closing.
        /// </summary>
        /// <returns>The list of subscriptions in the queue.</returns>
        public IList<Subscription> Close()
        {            
            lock (m_lock)
            {
                // TraceState("SESSION CLOSED");

                // wake up any waiting publish requests.
                m_publishEvent.Set();

                while (m_queuedRequests.Count > 0)
                {
                    QueuedRequest request = m_queuedRequests.First.Value;
                    m_queuedRequests.RemoveFirst();
                    request.Error = StatusCodes.BadSessionClosed;
                    request.Set();
                }
                
                // tell the subscriptions that the session is closed.
                Subscription[] subscriptions = new Subscription[m_queuedSubscriptions.Count];

                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    subscriptions[ii] = m_queuedSubscriptions[ii].Subscription;
                    subscriptions[ii].SessionClosed();
                }

                // clear the queue.
                m_queuedSubscriptions.Clear();

                return subscriptions;
            }
        }

        /// <summary>
        /// Adds a subscription from the publish queue.
        /// </summary>
        public void Add(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            lock (m_lock)
            {
                QueuedSubscription queuedSubscription = new QueuedSubscription();

                queuedSubscription.ReadyToPublish = false;
                queuedSubscription.Timestamp = DateTime.UtcNow;
                queuedSubscription.Subscription = subscription;

                m_queuedSubscriptions.Add(queuedSubscription);      

                // TraceState("SUBSCRIPTION QUEUED");          
            }
        }
        
        /// <summary>
        /// Removes a subscription from the publish queue.
        /// </summary>
        public void Remove(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            lock (m_lock)
            {
                // remove the subscription from the queue.
                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    if (Object.ReferenceEquals(m_queuedSubscriptions[ii].Subscription, subscription))
                    {
                        m_queuedSubscriptions.RemoveAt(ii);
                        break;
                    }
                }

                // remove any outstanding publishes.
                if (m_queuedSubscriptions.Count == 0)
                {
                    while (m_queuedRequests.Count > 0)
                    {
                        QueuedRequest request = m_queuedRequests.First.Value;
                        request.Error = StatusCodes.BadNoSubscription;
                        request.Set();
                        m_queuedRequests.RemoveFirst();
                    }
                }
                
                // TraceState("SUBSCRIPTION REMOVED");
            }
        }

        /// <summary>
        /// Processes acknowledgements for previously published messages.
        /// </summary>
        public void Acknowledge(
            OperationContext                      context,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements, 
            out StatusCodeCollection              acknowledgeResults, 
            out DiagnosticInfoCollection          acknowledgeDiagnosticInfos)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (subscriptionAcknowledgements == null) throw new ArgumentNullException(nameof(subscriptionAcknowledgements));

            lock (m_lock)
            {                
                bool diagnosticsExist = false;
                acknowledgeResults = new StatusCodeCollection(subscriptionAcknowledgements.Count);
                acknowledgeDiagnosticInfos = new DiagnosticInfoCollection(subscriptionAcknowledgements.Count);

                for (int ii = 0; ii < subscriptionAcknowledgements.Count; ii++)
                {
                    SubscriptionAcknowledgement acknowledgement = subscriptionAcknowledgements[ii];

                    bool found = false;
                    
                    for (int jj = 0; jj < m_queuedSubscriptions.Count; jj++)
                    {
                        QueuedSubscription subscription = m_queuedSubscriptions[jj];

                        if (subscription.Subscription.Id == acknowledgement.SubscriptionId)
                        {
                            ServiceResult result = subscription.Subscription.Acknowledge(context, acknowledgement.SequenceNumber);

                            if (ServiceResult.IsGood(result))
                            {
                                acknowledgeResults.Add(StatusCodes.Good);
                                
                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    acknowledgeDiagnosticInfos.Add(null);
                                }
                            }
                            else
                            {
                                acknowledgeResults.Add(result.Code);
                                
                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {                                    
                                    DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_server, context, result);
                                    acknowledgeDiagnosticInfos.Add(diagnosticInfo);
                                    diagnosticsExist = true;
                                }
                            }

                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        ServiceResult result = new ServiceResult(StatusCodes.BadSubscriptionIdInvalid);
                        acknowledgeResults.Add(result.Code);
                        
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(m_server, context, result);
                            acknowledgeDiagnosticInfos.Add(diagnosticInfo);
                            diagnosticsExist = true;
                        }
                    }
                }

                if (!diagnosticsExist)
                {
                    acknowledgeDiagnosticInfos.Clear();
                }
            }
        }
                
        /// <summary>
        /// Returns a subscription that is ready to publish.
        /// </summary>
        public Subscription Publish(uint clientHandle, DateTime deadline, bool requeue, AsyncPublishOperation operation)
        {
            QueuedRequest request = null;

            // DateTime queueTime = DateTime.UtcNow;
            // DateTime dequeueTime = DateTime.UtcNow;
            
            lock (m_lock)
            {
                if (m_queuedSubscriptions.Count == 0)
                {
                    // TraceState("PUBLISH ERROR (BadNoSubscription)");
                    throw new ServiceResultException(StatusCodes.BadNoSubscription);
                }

                // find the waiting subscription with the highest priority.
                List<QueuedSubscription> subscriptions = new List<QueuedSubscription>();

                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    QueuedSubscription subscription = m_queuedSubscriptions[ii];
                    
                    if (subscription.ReadyToPublish && !subscription.Publishing)
                    {
                        subscriptions.Add(subscription);
                    }
                }
                
                // find waiting the subscription that has been waiting the longest.
                if (subscriptions.Count > 0)
                {
                    byte maxPriority = 0;
                    DateTime earliestTimestamp = DateTime.MaxValue;
                    QueuedSubscription subscriptionToPublish = null;

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        QueuedSubscription subscription = subscriptions[ii];
                        byte priority = subscription.Subscription.Priority;
                        if (priority > maxPriority)
                        {
                            maxPriority = priority;
                            earliestTimestamp = DateTime.MaxValue;
                        }

                        if (priority >= maxPriority && earliestTimestamp > subscription.Timestamp)
                        {
                            earliestTimestamp = subscription.Timestamp;
                            subscriptionToPublish = subscription;
                        }
                    }

                    // reset subscriptions flag.
                    m_subscriptionsWaiting = false;

                    for (int jj = 0; jj < m_queuedSubscriptions.Count; jj++)
                    {
                        if (m_queuedSubscriptions[jj].ReadyToPublish)
                        {
                            m_subscriptionsWaiting = true;
                            break;
                        }
                    }

                    // TraceState("REQUEST #{0} ASSIGNED TO WAITING SUBSCRIPTION", clientHandle);
                    subscriptionToPublish.Publishing = true;
                    return subscriptionToPublish.Subscription;
                }

                // queue request because there is nothing waiting.
                if (subscriptions.Count == 0)
                {
                    LinkedListNode<QueuedRequest> node = m_queuedRequests.First;
                    
                    while (node != null)
                    {
                        LinkedListNode<QueuedRequest> next = node.Next;
                        QueuedRequest queuedRequest = node.Value;
                        StatusCode requestStatus = StatusCodes.Good;

                        // check if expired.
                        if (queuedRequest.Deadline < DateTime.MaxValue && queuedRequest.Deadline.AddMilliseconds(500) < DateTime.UtcNow)
                        {
                            requestStatus = StatusCodes.BadTimeout;
                        }

                        // check secure channel.
                        else if (!m_session.IsSecureChannelValid(queuedRequest.SecureChannelId))
                        {
                            requestStatus = StatusCodes.BadSecureChannelIdInvalid;
                        }

                        // remove bad requests.
                        if (StatusCode.IsBad(requestStatus))
                        {
                            queuedRequest.Error = requestStatus;
                            queuedRequest.Set();
                            m_queuedRequests.Remove(node);
                        }

                        node = next;
                    }

                    // clear excess requests - keep the newest ones.
                    while (m_maxPublishRequests > 0 && m_queuedRequests.Count >= m_maxPublishRequests)
                    {
                        request = m_queuedRequests.First.Value;
                        request.Error = StatusCodes.BadTooManyPublishRequests;
                        request.Set();
                        m_queuedRequests.RemoveFirst();
                    }

                    request = new QueuedRequest();
                    
                    request.SecureChannelId = SecureChannelContext.Current.SecureChannelId;
                    request.Deadline = deadline;
                    request.Subscription = null;
                    request.Error = StatusCodes.Good;

                    if (operation == null)
                    {
                        request.Event = new ManualResetEvent(false);
                    }
                    else
                    {
                        request.Operation = operation;
                    }

                    if (requeue)
                    {
                        m_queuedRequests.AddFirst(request);
                        // TraceState("REQUEST #{0} RE-QUEUED", clientHandle);
                    }
                    else
                    {
                        m_queuedRequests.AddLast(request);
                        // TraceState("REQUEST #{0} QUEUED", clientHandle);
                    }
                }                 
            }

            // check for non-blocking operation.
            if (operation != null)
            {
                // TraceState("PUBLISH: #{0} Async Request Queued.", clientHandle);
                return null;
            }

            // wait for subscription.
            ServiceResult error = request.Wait(Timeout.Infinite);

            // check for error.
            if (ServiceResult.IsGood(error))
            {
                if (StatusCode.IsBad(request.Error))
                {
                    error = request.Error;
                }
            }

            // must reassign subscription on error.
            if (ServiceResult.IsBad(request.Error))
            {
                if (request.Subscription != null)
                {
                    lock (m_lock)
                    {
                        request.Subscription.Publishing = false;
                        AssignSubscriptionToRequest(request.Subscription);
                    }
                }

                // TraceState("REQUEST #{0} PUBLISH ERROR ({1})", clientHandle, error.StatusCode);
                throw new ServiceResultException(request.Error);
            }

            // must be shuting down if this is null but no error.
            if (request.Subscription == null)
            {
                throw new ServiceResultException(StatusCodes.BadNoSubscription);
            }

            // TraceState("REQUEST #{0} ASSIGNED", clientHandle);
            // return whatever was assigned.
            return request.Subscription.Subscription;
        }

        /// <summary>
        /// Completes the publish.
        /// </summary>
        /// <param name="requeue">if set to <c>true</c> the request must be requeued.</param>
        /// <param name="operation">The asynchronous operation.</param>
        /// <param name="calldata">The calldata.</param>
        /// <returns></returns>
        public Subscription CompletePublish(
            bool requeue, 
            AsyncPublishOperation operation,
            object calldata)
        {
            Utils.Trace("PUBLISH: #{0} Completing", operation.RequestHandle, requeue);

            QueuedRequest request = (QueuedRequest)calldata;

            // check if need to requeue.
            lock (m_lock)
            {
                if (requeue)
                {
                    request.Subscription = null;
                    request.Error = StatusCodes.Good;
                    m_queuedRequests.AddFirst(request);
                    return null;
                }
            }

            // must reassign subscription on error.
            if (ServiceResult.IsBad(request.Error))
            {
                Utils.Trace("PUBLISH: #{0} Reassigned ERROR({1})", operation.RequestHandle, request.Error);

                if (request.Subscription != null)
                {
                    lock (m_lock)
                    {
                        request.Subscription.Publishing = false;
                        AssignSubscriptionToRequest(request.Subscription);
                    }
                }

                // TraceState("REQUEST #{0} PUBLISH ERROR ({1})", clientHandle, error.StatusCode);
                throw new ServiceResultException(request.Error);
            }

            // must be shuting down if this is null but no error.
            if (request.Subscription == null)
            {
                throw new ServiceResultException(StatusCodes.BadNoSubscription);
            }

            // return whatever was assigned.
            return request.Subscription.Subscription;
        }
        
        /// <summary>
        /// Adds a subscription back into the queue because it has more notifications to publish.
        /// </summary>
        public void PublishCompleted(Subscription subscription, bool moreNotifications)
        {
            lock (m_lock)
            {
                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    if (Object.ReferenceEquals(m_queuedSubscriptions[ii].Subscription, subscription))
                    {
                        m_queuedSubscriptions[ii].Publishing = false;

                        if (moreNotifications)
                        {
                            AssignSubscriptionToRequest(m_queuedSubscriptions[ii]);
                        }
                        else
                        {
                            m_queuedSubscriptions[ii].ReadyToPublish = false;
                            m_queuedSubscriptions[ii].Timestamp = DateTime.UtcNow;
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Checks the state of the subscriptions.
        /// </summary>
        public void PublishTimerExpired()
        {
            List<Subscription> subscriptionsToDelete = new List<Subscription>();

            lock (m_lock)
            {
                List<QueuedSubscription> liveSubscriptions = new List<QueuedSubscription>(m_queuedSubscriptions.Count);

                // check each available subscription.
                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    QueuedSubscription subscription = m_queuedSubscriptions[ii];

                    PublishingState state = subscription.Subscription.PublishTimerExpired();

                    // check for expired subscription.
                    if (state == PublishingState.Expired)
                    {
                        subscriptionsToDelete.Add(subscription.Subscription);
                        ((SubscriptionManager)m_server.SubscriptionManager).SubscriptionExpired(subscription.Subscription);
                        continue;
                    }

                    liveSubscriptions.Add(subscription);
                    
                    // check if idle.
                    if (state == PublishingState.Idle)
                    {               
                        subscription.ReadyToPublish = false;
                        continue;
                    }

                    // do nothing if subscription has already been flagged as available.
                    if (subscription.ReadyToPublish)
                    {
                        if (subscription.ReadyToPublish && m_queuedRequests.Count == 0)
                        {
                            if (!m_subscriptionsWaiting)
                            {
                                m_subscriptionsWaiting = true;
                                // TraceState("SUBSCRIPTIONS WAITING");
                            }
                        }
   
                        continue;
                    }

                    // assign subscription to request if one is available.
                    if (!subscription.Publishing)
                    {
                        AssignSubscriptionToRequest(subscription);
                    }
                }

                // only keep the live subscriptions.
                m_queuedSubscriptions = liveSubscriptions;

                // schedule cleanup on a background thread.
                SubscriptionManager.CleanupSubscriptions(m_server, subscriptionsToDelete);
            }
        }
        
        /// <summary>
        /// Checks the state of the subscriptions.
        /// </summary>
        private void AssignSubscriptionToRequest(QueuedSubscription subscription)
        {            
            // find a request.
            for (LinkedListNode<QueuedRequest> node = m_queuedRequests.First; node != null; node = node.Next)
            {
                QueuedRequest request = node.Value;

                StatusCode error = StatusCodes.Good;

                // check if expired.
                if (request.Deadline < DateTime.MaxValue && request.Deadline.AddMilliseconds(500) < DateTime.UtcNow)
                {
                    error = StatusCodes.BadTimeout;
                }

                // check secure channel.
                else if (!m_session.IsSecureChannelValid(request.SecureChannelId))
                {
                    error = StatusCodes.BadSecureChannelIdInvalid;
                    Utils.Trace("Publish abandoned because the secure channel changed.");
                }

                if (StatusCode.IsBad(error))
                {                         
                    // remove request.
                    LinkedListNode<QueuedRequest> next = node.Next;
                    m_queuedRequests.Remove(node);
                    node = next;

                    // wake up thread with error.
                    request.Error = error;
                    request.Set();

                    if (node == null)
                    {
                        break;
                    }

                    continue;
                }

                // remove request.
                m_queuedRequests.Remove(node);

                Utils.Trace("PUBLISH: #000 Assigned To Subscription({0}).", subscription.Subscription.Id);

                request.Error = StatusCodes.Good;
                request.Subscription = subscription;
                request.Subscription.Publishing = true;
                request.Set();
                return;
            }

            // mark it as available.
            subscription.ReadyToPublish = true;
            subscription.Timestamp = DateTime.UtcNow;
        }
        #endregion
        
        #region QueuedRequest Class
        /// <summary>
        /// A request queued while waiting for a subscription.
        /// </summary>
        private class QueuedRequest : IDisposable
        {
            public ManualResetEvent Event;
            public AsyncPublishOperation Operation;
            public DateTime Deadline;
            public StatusCode Error;
            public QueuedSubscription Subscription;
            public string SecureChannelId;

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
                    this.Error = StatusCodes.BadServerHalted;

                    if (this.Operation != null)
                    {
                        this.Operation.Dispose();
                        this.Operation = null;
                    }
                    
                    if (this.Event != null)
                    {
                        try
                        {
                            this.Event.Set();
                            this.Event.Dispose();
                        }
                        catch (Exception)
                        {
                            // ignore errors.
                        }
                    }
                }
            }
            #endregion

            /// <summary>
            /// Waits for the request to be processed.
            /// </summary>
            public ServiceResult Wait(int timeout)
            {
                try
                {
                    // do not block for an async operation.
                    if (Operation != null)
                    {
                        return StatusCodes.BadWouldBlock;
                    }

                    if (!Event.WaitOne(timeout))
                    {
                        return StatusCodes.BadTimeout;
                    }

                    return ServiceResult.Good;
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadTimeout, "Unexpected error waiting for subscription.");
                }
                finally
                {
                    try
                    {
                        Event.Dispose();
                    }
                    catch (Exception)
                    {
                        // ignore errors on close.                       
                    }
                }
            }

            /// <summary>
            /// Sets the event that wakes up the publish thread.
            /// </summary>
            public void Set()
            {
                try
                {
                    if (Operation != null)
                    {
                        Operation.CompletePublish(this);
                        return;
                    }

                    Event.Set();
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Publish request no longer available.");
                }
            }
        }
        #endregion

        #region QueuedSubscription Class
        /// <summary>
        /// Stores a subscription that has notifications ready to be sent back to the client.
        /// </summary>
        private class QueuedSubscription
        {
            public Subscription Subscription;
            public DateTime Timestamp;
            public bool ReadyToPublish;
            public bool Publishing;
        }
        #endregion

        #region Private Members
        /// <summary>
        /// Dumps the current state of the session queue.
        /// </summary>
        internal void TraceState(string context, params object[] args)
        {
            if ((Utils.TraceMask & Utils.TraceMasks.Information) == 0)
            {
                return;
            }

            StringBuilder buffer = new StringBuilder();

            lock (m_lock)
            {
                buffer.Append("PublishQueue ");
                buffer.AppendFormat(context, args);

                buffer.Append(", SessionId=");

                if (m_session != null)
                {
                    buffer.AppendFormat("{0}", m_session.Id);
                }
                else
                {
                    buffer.AppendFormat(", SessionId={0}", m_session.Id);
                }

                buffer.AppendFormat(", SubscriptionCount={0}", m_queuedSubscriptions.Count);
                buffer.AppendFormat(", RequestCount={0}", m_queuedRequests.Count);

                int readyToPublish = 0;

                for (int ii = 0; ii < m_queuedSubscriptions.Count; ii++)
                {
                    if (m_queuedSubscriptions[ii].ReadyToPublish)
                    {
                        readyToPublish++;
                    }
                }

                buffer.AppendFormat(", ReadyToPublishCount={0}", readyToPublish);

                int expiredRequests = 0;

                for (LinkedListNode<QueuedRequest> ii = m_queuedRequests.First; ii != null; ii = ii.Next)
                {
                    if (ii.Value.Deadline < DateTime.UtcNow)
                    {
                        expiredRequests++;
                    }
                }

                buffer.AppendFormat(", ExpiredCount={0}", expiredRequests);
            }

            Utils.Trace("{0}", buffer.ToString());
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private IServerInternal m_server;
        private Session m_session;
        private ManualResetEvent m_publishEvent;
        private LinkedList<QueuedRequest> m_queuedRequests;
        private List<QueuedSubscription> m_queuedSubscriptions;
        private int m_maxPublishRequests;
        private bool m_subscriptionsWaiting;
        #endregion
    }
}
