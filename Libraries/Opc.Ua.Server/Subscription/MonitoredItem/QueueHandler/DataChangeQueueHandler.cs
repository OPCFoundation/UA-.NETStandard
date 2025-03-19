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
    /// <summary>
    /// Mangages a data value queue for a data change monitoredItem
    /// </summary>
    public interface IDataChangeQueueHandler : IDisposable
    {
        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        /// <param name="diagnosticsMasks">Specifies which diagnostics which should be kept in the queue.</param>
        void SetQueueSize(uint queueSize, bool discardOldest, DiagnosticsMasks diagnosticsMasks);

        /// <summary>
        /// Set the sampling interval of the queue
        /// </summary>
        /// <param name="samplingInterval">the sampling interval</param>
        void SetSamplingInterval(double samplingInterval);
        /// <summary>
        /// Number of DataValues in the queue
        /// </summary>
        int ItemsInQueue { get; }
        /// <summary>
        /// Queues a value
        /// </summary>
        /// <param name="value">the dataValue</param>
        /// <param name="error">the error</param>
        void QueueValue(DataValue value, ServiceResult error);

        /// <summary>
        /// Dequeues the last item
        /// </summary>
        /// <returns>true if an item was dequeued</returns>
        bool PublishSingleValue(out DataValue value, out ServiceResult error, bool noEventLog = false);
    }
    /// <summary>
    /// Mangages a data value queue for a data change monitoredItem
    /// </summary>
    public class DataChangeQueueHandler : IDataChangeQueueHandler
    {
        /// <summary>
        /// Creates a new Queue
        /// </summary>
        /// <param name="monitoredItemId">the id of the monitored item</param>
        /// <param name="createDurable">true if a durable queue shall be created</param>
        /// <param name="queueFactory">the factory for <see cref="IDataChangeMonitoredItemQueue"/></param>
        /// <param name="discardedValueHandler"></param>
        public DataChangeQueueHandler(uint monitoredItemId, bool createDurable, IMonitoredItemQueueFactory queueFactory, Action discardedValueHandler = null)
        {
            m_dataValueQueue = queueFactory.CreateDataChangeQueue(createDurable, monitoredItemId);

            m_discardedValueHandler = discardedValueHandler;
            m_monitoredItemId = monitoredItemId;
            m_discardOldest = false;
            m_overflow = null;
            m_nextSampleTime = 0;
            m_samplingInterval = 0;
        }

        /// <summary>
        /// Create a DatachangeQueueHandler from an existing queue
        /// Used for restore after a server restart
        /// </summary>
        public DataChangeQueueHandler(
            IDataChangeMonitoredItemQueue dataValueQueue,
            bool discardOldest,
            double samplingInterval,
            Action discardedValueHandler = null)
        {
            m_dataValueQueue = dataValueQueue;
            m_monitoredItemId = dataValueQueue.QueueSize;
            m_discardOldest = discardOldest;
            m_discardedValueHandler = discardedValueHandler;
            m_nextSampleTime = 0;
            m_overflow = null;
            SetSamplingInterval(samplingInterval);
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

            m_discardOldest = discardOldest;

            // copy existing values.
            List<DataValue> existingValues = null;
            List<ServiceResult> existingErrors = null;

            if (ItemsInQueue > 0)
            {
                existingValues = new List<DataValue>((int)queueSize);
                existingErrors = new List<ServiceResult>((int)queueSize);

                while (PublishSingleValue(out DataValue value, out ServiceResult error, true))
                {
                    existingValues.Add(value);
                    existingErrors.Add(error);
                }
            }

            m_dataValueQueue.ResetQueue(queueSize, queueErrors);

            m_overflow = null;

            // requeue the data.
            if (existingValues != null)
            {
                for (int ii = 0; ii < existingValues.Count; ii++)
                {
                    Enqueue(existingValues[ii], existingErrors[ii]);
                }
            }
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
        }
        /// <summary>
        /// Deques the last item
        /// </summary>
        public bool PublishSingleValue(out DataValue value, out ServiceResult error, bool noEventLog = false)
        {
            if (m_dataValueQueue.Dequeue(out value, out error))
            {
                if (m_overflow != null && m_overflow == value)
                {
                    SetOverflowBit(ref value, ref error);
                    m_overflow = null;
                }

                if (!noEventLog)
                {
                    ServerUtils.EventLog.DequeueValue(value.WrappedValue, value.StatusCode);
                }

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
            if (m_dataValueQueue.PeekLastValue()?.StatusCode == StatusCodes.BadWaitingForInitialData)
            {
                // overwrite the last value
                m_dataValueQueue.OverwriteLastValue(value, error);

                return;
            }

            // check if queue is full.
            if (m_dataValueQueue.ItemsInQueue == m_dataValueQueue.QueueSize)
            {
                m_discardedValueHandler?.Invoke();

                if (!m_discardOldest)
                {
                    ServerUtils.ReportDiscardedValue(null, m_monitoredItemId, m_dataValueQueue.PeekLastValue());

                    //set overflow bit in newest value
                    m_overflow = value;

                    // overwrite last value
                    m_dataValueQueue.OverwriteLastValue(value, error);

                    return;
                }
                // remove oldest value.
                if (m_dataValueQueue.Dequeue(out DataValue discardedValue, out _))
                {
                    ServerUtils.ReportDiscardedValue(null, m_monitoredItemId, discardedValue);
                }
                else
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError, "Error queueing DataValue. DataValueQueue was full but it was not possible to discard the oldest value.");
                }
                //set overflow bit in oldest value
                m_overflow = m_dataValueQueue.PeekOldestValue();
            }
            else
            {
                ServerUtils.EventLog.EnqueueValue(value.WrappedValue);
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
        private DataValue m_overflow;
    }

}
