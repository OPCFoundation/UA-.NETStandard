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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides a queue for data changes.
    /// </summary>
    public class MonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public MonitoredItemQueue(uint monitoredItemId, DiscardedValueHandler discardedValueHandler = null)
        {
            m_monitoredItemId = monitoredItemId;
            m_values = null;
            m_errors = null;
            m_start = -1;
            m_end = -1;
            m_overflow = -1;
            m_discardOldest = false;
            m_nextSampleTime = 0;
            m_samplingInterval = 0;
            m_discardedValueHandler = discardedValueHandler;
        }

        #region Public Methods
        /// <summary>
        /// The delegate for the discarded value handler.
        /// </summary>
        public delegate void DiscardedValueHandler();

        /// <summary>
        /// Gets the current queue size.
        /// </summary>
        public uint QueueSize
        {
            get
            {
                if (m_values == null)
                {
                    return 0;
                }

                return (uint)m_values.Length;
            }
        }

        /// <summary>
        /// Gets number of elements actually contained in value queue.
        /// </summary>
        public int ItemsInQueue
        {
            get
            {
                if (m_values == null)
                {
                    return 0;
                }

                if (m_start < m_end)
                {
                    return m_end - m_start - 1;
                }

                return m_values.Length - m_start + m_end - 1;
            }
        }

        /// <summary>
        /// Sets the sampling interval used when queuing values.
        /// </summary>
        /// <param name="samplingInterval">The new sampling interval.</param>
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
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        /// <param name="diagnosticsMasks">Specifies which diagnostics which should be kept in the queue.</param>
        public void SetQueueSize(uint queueSize, bool discardOldest, DiagnosticsMasks diagnosticsMasks)
        {
            int length = (int)queueSize;

            if (length < 1)
            {
                length = 1;
            }

            int start = m_start;
            int end = m_end;

            // create new queue.
            DataValue[] values = new DataValue[length];
            ServiceResult[] errors = null;

            if ((diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0)
            {
                errors = new ServiceResult[length];
            }

            // copy existing values.
            List<DataValue> existingValues = null;
            List<ServiceResult> existingErrors = null;

            if (m_start >= 0)
            {
                existingValues = new List<DataValue>();
                existingErrors = new List<ServiceResult>();

                DataValue value = null;
                ServiceResult error = null;

                while (Dequeue(out value, out error))
                {
                    existingValues.Add(value);
                    existingErrors.Add(error);
                }
            }

            // update internals.
            m_values = values;
            m_errors = errors;
            m_start = -1;
            m_end = 0;
            m_overflow = -1;
            m_discardOldest = discardOldest;

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
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        public void QueueValue(DataValue value, ServiceResult error)
        {
            long now = HiResClock.TickCount64;

            if (m_start >= 0)
            {
                // check if too soon for another sample.
                if (now < m_nextSampleTime)
                {
                    int last = m_end - 1;

                    if (last < 0)
                    {
                        last = m_values.Length - 1;
                    }

                    // replace last value and error.
                    m_values[last] = value;

                    if (m_errors != null)
                    {
                        m_errors[last] = error;
                    }

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
        /// Publishes the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="error">The error associated with the value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        public bool Publish(out DataValue value, out ServiceResult error)
        {
            return Dequeue(out value, out error);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds the value to the queue. Discards values if the queue is full.
        /// </summary>
        /// <param name="value">The value to add.</param>
        /// <param name="error">The error to add.</param>
        private void Enqueue(DataValue value, ServiceResult error)
        {
            // check for empty queue.
            if (m_start < 0)
            {
                m_start = 0;
                m_end = 1;
                m_overflow = -1;

                Utils.Trace("ENQUEUE VALUE: Value={0}", value.WrappedValue);

                m_values[m_start] = value;

                if (m_errors != null)
                {
                    m_errors[m_start] = error;
                }

                return;
            }

            int next = m_end;

            // check if the latest value has initial dummy data
            if (m_values[m_end - 1].StatusCode != StatusCodes.BadWaitingForInitialData)
            {
                // check for wrap around.
                if (next >= m_values.Length)
                {
                    next = 0;
                }

                // check if queue is full.
                if (m_start == next)
                {
                    m_discardedValueHandler?.Invoke();

                    if (!m_discardOldest)
                    {
                        m_overflow = m_end - 1;
                        ServerUtils.ReportDiscardedValue(null, m_monitoredItemId, value);

                        // overwrite last value
                        m_values[m_overflow] = value;

                        if (m_errors != null)
                        {
                            m_errors[m_overflow] = error;
                        }

                        return;
                    }

                    // remove oldest value.
                    m_start++;

                    if (m_start >= m_values.Length)
                    {
                        m_start = 0;
                    }

                    // set overflow bit.
                    m_overflow = m_start;
                    ServerUtils.ReportDiscardedValue(null, m_monitoredItemId, m_values[m_overflow]);
                }
                else
                {
                    Utils.Trace("ENQUEUE VALUE: Value={0}", value.WrappedValue);
                }
            }
            else
            {
                // overwrite the last value
                next = m_end - 1;
            }

            // add value.
            m_values[next] = value;

            if (m_errors != null)
            {
                m_errors[next] = error;
            }

            m_end = next + 1;
        }

        /// <summary>
        /// Removes a value and an error from the queue.
        /// </summary>
        /// <param name="value">The value removed from the queue.</param>
        /// <param name="error">The error removed from the queue.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        private bool Dequeue(out DataValue value, out ServiceResult error)
        {
            value = null;
            error = null;

            // check for empty queue.
            if (m_start < 0)
            {
                return false;
            }

            value = m_values[m_start];
            m_values[m_start] = null;

            if (m_errors != null)
            {
                error = m_errors[m_start];
                m_errors[m_start] = null;
            }

            // set the overflow bit.
            if (m_overflow == m_start)
            {
                SetOverflowBit(ref value, ref error);
                m_overflow = -1;
            }

            m_start++;

            // check if queue has been emptied.
            if (m_start == m_end)
            {
                m_start = -1;
                m_end = 0;
            }

            // check for wrap around.
            else if (m_start >= m_values.Length)
            {
                m_start = 0;
            }

            Utils.Trace("DEQUEUE VALUE: Value={0} CODE={1}<{1:X8}> OVERFLOW={2}", value.WrappedValue, value.StatusCode.Code, value.StatusCode.Overflow);

            return true;
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
        #endregion

        #region Private Fields
        private uint m_monitoredItemId;
        private DataValue[] m_values;
        private ServiceResult[] m_errors;
        private int m_start;
        private int m_end;
        private int m_overflow;
        private bool m_discardOldest;
        private long m_nextSampleTime;
        private long m_samplingInterval;
        DiscardedValueHandler m_discardedValueHandler;
        #endregion
    }
}
