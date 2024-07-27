/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    #region QueueHandler
    /// <summary>
    /// Mangages a data value queue for a data change monitoredItem
    /// </summary>
    public class DataValueQueueHandler : IDisposable
    {
        /// <summary>
        /// Creates a new Queue
        /// </summary>
        /// <param name="monitoredItemId">the id of the monitored item</param>
        /// <param name="createDurable">true if a durable queue shall be created</param>
        /// <param name="queueFactory">the factory for <see cref="IDataChangeMonitoredItemQueue"/></param>
        /// <param name="discardedValueHandler"></param>
        public DataValueQueueHandler(uint monitoredItemId, bool createDurable, IMonitoredItemQueueFactory queueFactory, Action discardedValueHandler = null)
        {
            m_dataValueQueue = queueFactory.CreateDataValueQueue(createDurable);

            m_discardedValueHandler = discardedValueHandler;
            m_monitoredItemId = monitoredItemId;
            m_discardOldest = false;
            m_nextSampleTime = 0;
            m_samplingInterval = 0;
        }

        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        /// <param name="diagnosticsMasks">Specifies which diagnostics which should be kept in the queue.</param>
        public void SetQueueSize(uint queueSize, bool discardOldest, DiagnosticsMasks diagnosticsMasks)
        {
            bool queueErrors = (diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0;
            m_dataValueQueue.SetQueueSize(queueSize, queueErrors);

            // update internals.
            m_discardOldest = discardOldest;
        }
        /// <summary>
        /// Set the sampling interval of the queue
        /// </summary>
        /// <param name="samplingInterval">the sampling interval</param>
        public void SetSamplingInterval(double samplingInterval)
        {
            // substract the previous sampling interval.
            if (m_samplingInterval < m_nextSampleTime)
            {
                m_nextSampleTime -= m_samplingInterval;
            }

            // calculate the next sampling interval.
            m_samplingInterval = (long)samplingInterval;

            if (m_samplingInterval > 0)
            {
                m_nextSampleTime += m_samplingInterval;
            }
            else
            {
                m_nextSampleTime = 0;
            }
        }
        /// <summary>
        /// Number of DataValues in the queue
        /// </summary>
        public int ItemsInQueue => m_dataValueQueue.ItemsInQueue;
        /// <summary>
        /// Queues a value
        /// </summary>
        /// <param name="value">the dataValue</param>
        /// <param name="error">the error</param>
        public void QueueValue(DataValue value, ServiceResult error)
        {
            long now = HiResClock.TickCount64;

            if (m_dataValueQueue.ItemsInQueue > 0)
            {
                // check if too soon for another sample.
                if (now < m_nextSampleTime)
                {
                    m_dataValueQueue.OverwriteLastValue(value, error);

                    m_discardedValueHandler?.Invoke();

                    return;
                }
            }

            // update next sample time.
            if (m_nextSampleTime > 0)
            {
                long delta = now - m_nextSampleTime;

                if (m_samplingInterval > 0 && delta >= 0)
                {
                    m_nextSampleTime += ((delta / m_samplingInterval) + 1) * m_samplingInterval;
                }
            }
            else
            {
                m_nextSampleTime = now + m_samplingInterval;
            }

            // queue next value.
            Enqueue(value, error);
            ServerUtils.EventLog.EnqueueValue(value.WrappedValue);
        }
        /// <summary>
        /// Deques the last item
        /// </summary>
        /// <param name="value"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool PublishSingleValue(out DataValue value, out ServiceResult error)
        {
            value = null;
            error = null;
            if (m_dataValueQueue.Dequeue(out value, out error))
            {
                ServerUtils.EventLog.DequeueValue(value.WrappedValue, value.StatusCode);

                return true;
            }
            return false;
        }

        private void Enqueue(DataValue value, ServiceResult error)
        {
            // check for empty queue.
            if (m_dataValueQueue.ItemsInQueue == 0)
            {
                ServerUtils.EventLog.EnqueueValue(value.WrappedValue);

                m_dataValueQueue.Enqueue(value, error);

                return;
            }


            // check if the latest value has initial dummy data
            if (m_dataValueQueue.PeekLastValue()?.StatusCode != StatusCodes.BadWaitingForInitialData)
            {
                // check if queue is full.
                if (m_dataValueQueue.ItemsInQueue == m_dataValueQueue.QueueSize)
                {
                    m_discardedValueHandler?.Invoke();

                    SetOverflowBit(ref value, ref error);

                    if (!m_discardOldest)
                    {
                        ServerUtils.ReportDiscardedValue(null, m_monitoredItemId, value);

                        // overwrite last value
                        m_dataValueQueue.OverwriteLastValue(value, error);

                        return;
                    }

                    // remove oldest value.
                    m_dataValueQueue.Dequeue(out var discardedValue, out var _);
                    ServerUtils.ReportDiscardedValue(null, m_monitoredItemId, discardedValue);
                }
                else
                {
                    ServerUtils.EventLog.EnqueueValue(value.WrappedValue);
                }
            }
            else
            {
                // overwrite the last value
                m_dataValueQueue.OverwriteLastValue(value, error);
            }

            m_dataValueQueue.Enqueue(value, error);
        }

        /// <summary>
        /// Sets the overflow bit in the value and error.
        /// </summary>
        /// <param name="value">The value to update.</param>
        /// <param name="error">The error to update.</param>
        private void SetOverflowBit(ref DataValue value, ref ServiceResult error)
        {
            if (value != null)
            {
                StatusCode status = value.StatusCode;
                status.Overflow = true;
                value.StatusCode = status;
            }

            if (error != null)
            {
                StatusCode status = error.StatusCode;
                status.Overflow = true;

                // have to copy before updating because the ServiceResult is invariant.
                ServiceResult copy = new ServiceResult(
                    status,
                    error.SymbolicId,
                    error.NamespaceUri,
                    error.LocalizedText,
                    error.AdditionalInfo,
                    error.InnerResult);

                error = copy;
            }
        }
        /// <summary>
        /// Dispose the queue
        /// </summary>
        public void Dispose()
        {

            Utils.SilentDispose(m_dataValueQueue);
        }

        private readonly IDataChangeMonitoredItemQueue m_dataValueQueue;
        private readonly uint m_monitoredItemId;
        private bool m_discardOldest;
        private long m_nextSampleTime;
        private long m_samplingInterval;
        private readonly Action m_discardedValueHandler;
    }
    #endregion

    #region EventQueueHandler
    /// <summary>
    /// Mangages an event queue for usage by a MonitoredItem
    /// </summary>
    public class EventQueueHandler : IDisposable
    {
        /// <summary>
        /// Creates a new Queue
        /// </summary>
        /// <param name="createDurable">create a durable queue</param>
        /// <param name="queueFactory">the factory for creating the the factory for <see cref="IEventMonitoredItemQueue"/></param>
        public EventQueueHandler(bool createDurable, IMonitoredItemQueueFactory queueFactory)
        {
            m_eventQueue = queueFactory.CreateEventQueue(createDurable);
            m_discardOldest = false;
            m_overflow = false;
        }

        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        public void SetQueueSize(uint queueSize, bool discardOldest)
        {
            m_discardOldest = discardOldest;
            m_eventQueue.SetQueueSize(queueSize, discardOldest);
        }
        /// <summary>
        /// The number of Items in the queue
        /// </summary>
        public int ItemsInQueue => m_eventQueue.ItemsInQueue;

        /// <summary>
        /// True if the queue is overflowing
        /// </summary>
        public bool Overflow => m_overflow;

        /// <summary>
        /// Checks the last 1k queue entries if the event is already in there
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            return m_eventQueue.IsEventContainedInQueue(instance);
        }

        /// <summary>
        /// true if queue is already full and discarding is not allowed
        /// </summary>
        /// <returns></returns>
        public bool SetQueueOverflowIfFull()
        {
            if (m_eventQueue.ItemsInQueue >= m_eventQueue.QueueSize)
            {
                if (!m_discardOldest)
                {
                    m_overflow = true;
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Dispose the queue
        /// </summary>
        public void Dispose()
        {
            Utils.SilentDispose(m_eventQueue);
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        public virtual void QueueEvent(EventFieldList fields)
        {
            // make space in the queue.
            if (m_eventQueue.ItemsInQueue >= m_eventQueue.QueueSize)
            {
                m_overflow = true;
                m_eventQueue.Dequeue(out _);
            }
            // queue the event.
            m_eventQueue.Enqueue(fields);
        }

        /// <summary>
        /// Publish Events
        /// </summary>
        /// <param name="context"></param>
        /// <param name="notifications"></param>
        /// <param name="maxNotificationsPerPublish">the maximum number of notifications to enqueue per call</param>
        public void Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish)
        {
            uint notificationCount = 0;
            while (notificationCount <= maxNotificationsPerPublish && m_eventQueue.Dequeue(out EventFieldList fields))
            {
                // apply any diagnostic masks.
                for (int jj = 0; jj < fields.EventFields.Count; jj++)
                {
                    object value = fields.EventFields[jj].Value;

                    if (value is StatusResult result)
                    {
                        result.ApplyDiagnosticMasks(context.DiagnosticsMask, context.StringTable);
                    }
                }

                notifications.Enqueue(fields);
                notificationCount++;
            }

            m_overflow = false;
        }

        private bool m_overflow;
        private bool m_discardOldest;
        private readonly IEventMonitoredItemQueue m_eventQueue;
    }
    #endregion
}
