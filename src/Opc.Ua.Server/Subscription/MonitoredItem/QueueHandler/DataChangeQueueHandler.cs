/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;

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
        /// <returns>true of overflow occured</returns>
        bool QueueValue(in DataValue value, ServiceResult error);

        /// <summary>
        /// Dequeues the last item
        /// </summary>
        /// <returns>true if an item was dequeued</returns>
        bool PublishSingleValue(
            out DataValue value,
            out ServiceResult error,
            bool noEventLog = false);
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
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="discardedValueHandler">Handler for discarded values</param>
        public DataChangeQueueHandler(
            uint monitoredItemId,
            bool createDurable,
            IMonitoredItemQueueFactory queueFactory,
            ITelemetryContext telemetry,
            Action? discardedValueHandler = null)
            : this(monitoredItemId, createDurable, queueFactory, telemetry, discardedValueHandler, null)
        {
        }

        /// <summary>
        /// Creates a new Queue with an explicit <see cref="TimeProvider"/> so
        /// the per-sample timestamp used for sampling-interval throttling can
        /// be mocked in tests.
        /// </summary>
        public DataChangeQueueHandler(
            uint monitoredItemId,
            bool createDurable,
            IMonitoredItemQueueFactory queueFactory,
            ITelemetryContext telemetry,
            Action? discardedValueHandler,
            TimeProvider? timeProvider)
        {
            m_logger = telemetry.CreateLogger<DataChangeQueueHandler>();
            m_dataValueQueue = queueFactory.CreateDataChangeQueue(createDurable, monitoredItemId);
            m_timeProvider = timeProvider ?? TimeProvider.System;

            m_discardedValueHandler = discardedValueHandler!;
            m_monitoredItemId = monitoredItemId;
            m_discardOldest = false;
            m_overflow = default;
            m_overflowPending = false;
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
            ITelemetryContext telemetry,
            Action? discardedValueHandler = null)
            : this(
                dataValueQueue,
                dataValueQueue.QueueSize,
                discardOldest,
                samplingInterval,
                DiagnosticsMasks.None,
                telemetry,
                discardedValueHandler,
                null)
        {
        }

        /// <summary>
        /// Create a DatachangeQueueHandler from an existing queue with an
        /// explicit <see cref="TimeProvider"/>.
        /// </summary>
        public DataChangeQueueHandler(
            IDataChangeMonitoredItemQueue dataValueQueue,
            bool discardOldest,
            double samplingInterval,
            ITelemetryContext telemetry,
            Action? discardedValueHandler,
            TimeProvider? timeProvider)
            : this(
                dataValueQueue,
                dataValueQueue.QueueSize,
                discardOldest,
                samplingInterval,
                DiagnosticsMasks.None,
                telemetry,
                discardedValueHandler,
                timeProvider)
        {
        }

        /// <summary>
        /// Creates a queue handler from an existing queue and restores protected lifecycle markers.
        /// </summary>
        internal DataChangeQueueHandler(
            IDataChangeMonitoredItemQueue dataValueQueue,
            uint queueSize,
            bool discardOldest,
            double samplingInterval,
            DiagnosticsMasks diagnosticsMasks,
            ITelemetryContext telemetry,
            Action? discardedValueHandler,
            TimeProvider? timeProvider = null)
        {
            m_logger = telemetry.CreateLogger<DataChangeQueueHandler>();

            m_dataValueQueue = dataValueQueue;
            m_monitoredItemId = dataValueQueue.MonitoredItemId;
            m_queueSize = Math.Max(queueSize, 1);
            m_queueErrors = (diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0;
            m_discardOldest = discardOldest;
            m_discardedValueHandler = discardedValueHandler!;
            m_nextSampleTime = 0;
            m_overflow = default;
            m_overflowPending = false;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            RestoreQueueState();
            SetSamplingInterval(samplingInterval);
        }

        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        /// <param name="diagnosticsMasks">Specifies which diagnostics which should be kept in the queue.</param>
        public void SetQueueSize(
            uint queueSize,
            bool discardOldest,
            DiagnosticsMasks diagnosticsMasks)
        {
            m_queueSize = Math.Max(queueSize, 1);
            m_queueErrors = (diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0;
            m_discardOldest = discardOldest;

            List<QueuedValue> existingValues = DrainQueue();
            uint physicalQueueSize = Math.Max(m_queueSize, (uint)existingValues.Count);
            m_dataValueQueue.ResetQueue(physicalQueueSize, m_queueErrors);
            m_overflow = default;
            m_overflowPending = false;
            ResetQueueState();
            foreach (QueuedValue queuedValue in existingValues)
            {
                if (queuedValue.Required)
                {
                    EnqueueRequired(queuedValue.Value, queuedValue.Error);
                }
                else
                {
                    Enqueue(queuedValue.Value, queuedValue.Error, out _);
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
        /// <returns>true of overflow occured</returns>
        public bool QueueValue(in DataValue value, ServiceResult error)
        {
            return QueueValue(value, error, out _);
        }

        /// <summary>
        /// Queues a value and reports whether the value was retained.
        /// </summary>
        internal bool QueueValue(
            in DataValue value,
            ServiceResult error,
            out bool queued)
        {
            if (m_lifecycleBoundaryQueued)
            {
                return Enqueue(value, error, out queued);
            }

            long now = m_timeProvider.GetTimestampMilliseconds();

            if (m_dataValueQueue.ItemsInQueue > 0)
            {
                // check if too soon for another sample.
                if (now < m_nextSampleTime)
                {
                    m_dataValueQueue.TryPeekLastValue(out DataValue overwrittenValue);
                    if (m_lastEnqueuedRequired)
                    {
                        return Enqueue(value, error, out queued);
                    }

                    m_logger.OVERWRITTENVALUETOOSOONFORANOTHERSAMPLE(
                        overwrittenValue.WrappedValue,
                        overwrittenValue.StatusCode.Code,
                        m_samplingInterval,
                        now,
                        m_nextSampleTime);

                    m_dataValueQueue.OverwriteLastValue(value, error);

                    m_discardedValueHandler?.Invoke();

                    queued = true;
                    return false;
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
            return Enqueue(value, error, out queued);
        }

        /// <summary>
        /// Queues a required missing-node marker without sampling or overflow replacement.
        /// </summary>
        internal void QueueRequiredValue(in DataValue value, ServiceResult error)
        {
            EnqueueRequired(value, error);
        }

        /// <summary>
        /// Gets whether a required missing-node marker is pending.
        /// </summary>
        internal bool HasRequiredValues => m_requiredValueCount > 0;

        /// <summary>
        /// Gets whether values remain in an active lifecycle publication sequence.
        /// </summary>
        internal bool HasLifecycleValues =>
            m_lifecycleBoundaryQueued && m_dataValueQueue.ItemsInQueue > 0;

        /// <summary>
        /// Deques the last item
        /// </summary>
        public bool PublishSingleValue(
            out DataValue value,
            out ServiceResult error,
            bool noEventLog = false)
        {
            return PublishSingleValue(out value, out error, out _, noEventLog);
        }

        /// <summary>
        /// Dequeues the oldest item and reports whether it is a protected lifecycle marker.
        /// </summary>
        internal bool PublishSingleValue(
            out DataValue value,
            out ServiceResult error,
            out bool required,
            bool noEventLog = false)
        {
            if (m_dataValueQueue.Dequeue(out value, out error))
            {
                required = DequeueRequiredFlag(value, error);
                if (required)
                {
                    m_requiredValueCount--;
                }
                else if (m_lifecycleBoundaryQueued)
                {
                    if (m_ordinaryValuesBeforeLifecycle > 0)
                    {
                        m_ordinaryValuesBeforeLifecycle--;
                    }
                    else
                    {
                        m_lifecycleOrdinaryValueCount--;
                    }
                }
                else
                {
                    m_ordinaryValuesBeforeLifecycle--;
                }

                if (m_overflowPending && m_overflow == value)
                {
                    SetOverflowBit(ref value, ref error);
                    m_overflow = default;
                    m_overflowPending = false;
                }

                if (!noEventLog)
                {
                    m_logger.DequeueValue(
                        value.WrappedValue,
                        value.StatusCode.Code,
                        value.StatusCode.Overflow);
                }

                if (m_dataValueQueue.ItemsInQueue == 0)
                {
                    ResetQueueState();
                }

                return true;
            }
            required = false;
            return false;
        }

        /// <summary>
        /// Enque value
        /// </summary>
        /// <returns>true of overflow occured</returns>
        private bool Enqueue(
            DataValue value,
            ServiceResult error,
            out bool queued)
        {
            int ordinaryValueCount = m_lifecycleBoundaryQueued
                ? m_lifecycleOrdinaryValueCount
                : m_ordinaryValuesBeforeLifecycle;
            uint ordinaryValueLimit = m_lifecycleBoundaryQueued
                ? GetLifecycleOrdinaryValueLimit(m_requiredValueCount)
                : m_queueSize;

            // check for empty queue.
            if (m_dataValueQueue.ItemsInQueue == 0)
            {
                m_logger.ENQUEUEVALUEValueValue(value.WrappedValue);

                EnsurePhysicalCapacity(1);
                m_dataValueQueue.Enqueue(value, error);
                AppendRequiredFlag(false);
                IncrementOrdinaryValueCount();

                queued = true;
                return false;
            }

            // check if the latest value has initial dummy data
            if (m_dataValueQueue.TryPeekLastValue(out DataValue lastValue) &&
                lastValue.StatusCode == StatusCodes.BadWaitingForInitialData)
            {
                // overwrite the last value
                m_dataValueQueue.OverwriteLastValue(value, error);

                queued = true;
                return false;
            }

            // check if queue is full.
            if (ordinaryValueLimit == 0 || (uint)ordinaryValueCount >= ordinaryValueLimit)
            {
                m_discardedValueHandler?.Invoke();

                if (!RemoveOrdinaryValue(fromEnd: !m_discardOldest, out DataValue discardedValue))
                {
                    // Only protected lifecycle markers remain queued. They must still be
                    // delivered, so the incoming value is discarded instead of a queued one.
                    ServerUtils.ReportDiscardedValue(default, m_monitoredItemId, value);
                    queued = false;
                    return true;
                }

                ServerUtils.ReportDiscardedValue(default, m_monitoredItemId, discardedValue);

                if (!m_discardOldest)
                {
                    //set overflow bit in newest value
                    SetOverflowBit(ref value, ref error);
                }
                else if (!SetOverflowOnOldestOrdinaryValue() && m_queueSize > 1)
                {
                    // No ordinary value remains to carry the overflow bit, so the incoming
                    // value becomes the oldest one and reports the loss instead. A queue of
                    // size one is a last value cache, which never reports an overflow.
                    SetOverflowBit(ref value, ref error);
                }
            }
            else
            {
                m_logger.ENQUEUEVALUEValueValue(value.WrappedValue);
            }

            EnsurePhysicalCapacity((uint)m_dataValueQueue.ItemsInQueue + 1);
            m_dataValueQueue.Enqueue(value, error);
            AppendRequiredFlag(false);
            IncrementOrdinaryValueCount();

            queued = true;
            return (uint)ordinaryValueCount >= ordinaryValueLimit;
        }

        private void EnqueueRequired(DataValue value, ServiceResult error)
        {
            if (!m_lifecycleBoundaryQueued)
            {
                m_lifecycleBoundaryQueued = true;
            }

            EnsurePhysicalCapacity((uint)m_dataValueQueue.ItemsInQueue + 1);
            m_dataValueQueue.Enqueue(value, error);
            AppendRequiredFlag(true);
            m_requiredValueCount++;
        }

        private uint GetLifecycleOrdinaryValueLimit(int requiredValueCount)
        {
            return (uint)requiredValueCount >= m_queueSize
                ? 0
                : m_queueSize - (uint)requiredValueCount;
        }

        private void IncrementOrdinaryValueCount()
        {
            if (m_lifecycleBoundaryQueued)
            {
                m_lifecycleOrdinaryValueCount++;
            }
            else
            {
                m_ordinaryValuesBeforeLifecycle++;
            }
        }

        private bool RemoveOrdinaryValue(bool fromEnd, out DataValue discardedValue)
        {
            List<QueuedValue> values = DrainQueue();
            int firstRequired = values.FindIndex(queuedValue => queuedValue.Required);
            int index = -1;
            if (fromEnd)
            {
                for (int ii = values.Count - 1; ii >= 0; ii--)
                {
                    if (!values[ii].Required &&
                        (!m_lifecycleBoundaryQueued || ii > firstRequired))
                    {
                        index = ii;
                        break;
                    }
                }
            }
            else
            {
                for (int ii = 0; ii < values.Count; ii++)
                {
                    if (!values[ii].Required &&
                        (!m_lifecycleBoundaryQueued || ii > firstRequired))
                    {
                        index = ii;
                        break;
                    }
                }
            }

            if (index < 0)
            {
                RestorePhysicalQueue(values);
                discardedValue = default;
                return false;
            }

            discardedValue = values[index].Value;
            values.RemoveAt(index);
            RestorePhysicalQueue(values);
            if (m_lifecycleBoundaryQueued)
            {
                m_lifecycleOrdinaryValueCount--;
            }
            else
            {
                m_ordinaryValuesBeforeLifecycle--;
            }
            return true;
        }

        private bool SetOverflowOnOldestOrdinaryValue()
        {
            List<QueuedValue> values = DrainQueue();
            int firstRequired = values.FindIndex(queuedValue => queuedValue.Required);
            bool overflowSet = false;
            for (int ii = 0; ii < values.Count; ii++)
            {
                if (!values[ii].Required &&
                    (!m_lifecycleBoundaryQueued || ii > firstRequired))
                {
                    DataValue value = values[ii].Value;
                    ServiceResult error = values[ii].Error;
                    SetOverflowBit(ref value, ref error);
                    values[ii] = values[ii] with { Value = value, Error = error };
                    overflowSet = true;
                    break;
                }
            }
            RestorePhysicalQueue(values);
            return overflowSet;
        }

        private void EnsurePhysicalCapacity(uint requiredCapacity)
        {
            if (m_dataValueQueue.QueueSize >= requiredCapacity)
            {
                return;
            }

            List<QueuedValue> values = DrainQueue();
            m_dataValueQueue.ResetQueue(requiredCapacity, m_queueErrors);
            RefillPhysicalQueue(values);
        }

        private List<QueuedValue> DrainQueue()
        {
            int itemCount = m_dataValueQueue.ItemsInQueue;
            var values = new List<QueuedValue>(itemCount);
            var spinWait = new SpinWait();
            // Durable queues may temporarily return false while restoring a persisted batch.
            // Drain the captured item count exactly so a resize cannot drop a partially restored batch.
            while (values.Count < itemCount)
            {
                if (m_dataValueQueue.Dequeue(out DataValue value, out ServiceResult error))
                {
                    values.Add(
                        new QueuedValue(value, error, DequeueRequiredFlag(value, error)));
                    spinWait.Reset();
                }
                else
                {
                    spinWait.SpinOnce();
                }
            }
            return values;
        }

        private void RestorePhysicalQueue(List<QueuedValue> values)
        {
            uint physicalQueueSize = Math.Max(
                m_dataValueQueue.QueueSize,
                Math.Max(m_queueSize, (uint)values.Count));
            m_dataValueQueue.ResetQueue(physicalQueueSize, m_queueErrors);
            RefillPhysicalQueue(values);
        }

        private void RestoreQueueState()
        {
            List<QueuedValue> values = DrainQueue();
            bool lifecycleBoundaryFound = false;
            for (int ii = 0; ii < values.Count; ii++)
            {
                // A restored durable queue carries no marker flags, so the status code is the
                // only signal available here. This best-effort classification is deliberately
                // limited to restore: live values are classified by QueueRequiredValue alone.
                bool required = IsRequiredMarker(values[ii].Value, values[ii].Error);
                values[ii] = values[ii] with { Required = required };

                if (required)
                {
                    lifecycleBoundaryFound = true;
                    m_lifecycleBoundaryQueued = true;
                    m_requiredValueCount++;
                }
                else if (lifecycleBoundaryFound)
                {
                    m_lifecycleOrdinaryValueCount++;
                }
                else
                {
                    m_ordinaryValuesBeforeLifecycle++;
                }
            }

            RefillPhysicalQueue(values);
        }

        private void ResetQueueState()
        {
            m_lifecycleBoundaryQueued = false;
            m_ordinaryValuesBeforeLifecycle = 0;
            m_lifecycleOrdinaryValueCount = 0;
            m_requiredValueCount = 0;
            m_requiredFlags.Clear();
            m_lastEnqueuedRequired = false;
        }

        /// <summary>
        /// Records whether the value appended to the physical queue is a protected
        /// lifecycle marker.
        /// </summary>
        private void AppendRequiredFlag(bool required)
        {
            m_requiredFlags.Enqueue(required);
            m_lastEnqueuedRequired = required;
        }

        /// <summary>
        /// Consumes the marker flag of the value removed from the front of the queue.
        /// </summary>
        private bool DequeueRequiredFlag(in DataValue value, ServiceResult? error)
        {
            if (m_requiredFlags.Count > 0)
            {
                return m_requiredFlags.Dequeue();
            }

            // The physical queue was populated without the handler, so fall back to the
            // best-effort status code classification used for restored durable queues.
            return IsRequiredMarker(value, error);
        }

        /// <summary>
        /// Refills the physical queue and rebuilds the marker flags from the drained values.
        /// </summary>
        private void RefillPhysicalQueue(List<QueuedValue> values)
        {
            m_requiredFlags.Clear();
            m_lastEnqueuedRequired = false;
            foreach (QueuedValue queuedValue in values)
            {
                m_dataValueQueue.Enqueue(queuedValue.Value, queuedValue.Error);
                AppendRequiredFlag(queuedValue.Required);
            }
        }

        private static bool IsRequiredMarker(in DataValue value, ServiceResult? error)
        {
            return error?.StatusCode.Code == StatusCodes.BadNodeIdUnknown.Code ||
                (!value.IsNull && value.StatusCode.Code == StatusCodes.BadNodeIdUnknown.Code);
        }

        /// <summary>
        /// Sets the overflow bit in the value and error.
        /// </summary>
        /// <param name="value">The value to update.</param>
        /// <param name="error">The error to update.</param>
        private static void SetOverflowBit(ref DataValue value, ref ServiceResult error)
        {
            value = value.WithStatus(value.StatusCode.SetOverflow(true));

            if (error != null)
            {
                // have to copy before updating because the ServiceResult is invariant.
                error = new ServiceResult(
                    error.NamespaceUri,
                    error.StatusCode.SetOverflow(true),
                    error.LocalizedText,
                    error.AdditionalInfo,
                    error.InnerResult);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overridable method to dispose of resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_dataValueQueue?.Dispose();
            }
        }

        private readonly IDataChangeMonitoredItemQueue m_dataValueQueue;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly uint m_monitoredItemId;
        private uint m_queueSize;
        private bool m_queueErrors;
        private bool m_discardOldest;
        private long m_nextSampleTime;
        private long m_samplingInterval;
        private readonly Action m_discardedValueHandler;
        private DataValue m_overflow;
        private bool m_overflowPending;
        private bool m_lifecycleBoundaryQueued;
        private int m_ordinaryValuesBeforeLifecycle;
        private int m_lifecycleOrdinaryValueCount;
        private int m_requiredValueCount;
        private readonly Queue<bool> m_requiredFlags = [];
        private bool m_lastEnqueuedRequired;

        private readonly record struct QueuedValue(
            DataValue Value,
            ServiceResult Error,
            bool Required);
    }

    /// <summary>
    /// Source-generated log messages for DataChangeQueueHandler.
    /// </summary>
    internal static partial class DataChangeQueueHandlerLog
    {
        [LoggerMessage(EventId = ServerEventIds.DataChangeQueueHandler + 0, Level = LogLevel.Trace,
            Message = "OVERWRITTEN VALUE (TOO SOON FOR ANOTHER SAMPLE): Value={Value} CODE={Code}<{Code:X8}> " +
                "SamplingInterval={SamplingInterval}QueueValueCall {Now} NextSampleTime {NextSampleTime}")]
        public static partial void OVERWRITTENVALUETOOSOONFORANOTHERSAMPLE(
            this ILogger logger,
            Variant value,
            uint code,
            long samplingInterval,
            long now,
            long nextSampleTime);


        [LoggerMessage(EventId = ServerEventIds.DataChangeQueueHandler + 1, Level = LogLevel.Trace,
            Message = "ENQUEUE VALUE: Value={Value}")]
        public static partial void ENQUEUEVALUEValueValue(this ILogger logger, Variant value);
    }

}
