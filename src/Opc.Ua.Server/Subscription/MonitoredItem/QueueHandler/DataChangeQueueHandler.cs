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
        /// <param name="timeProvider">
        /// Supplies the per-sample timestamp used for sampling-interval throttling, so it can be
        /// mocked in tests. Defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        public DataChangeQueueHandler(
            uint monitoredItemId,
            bool createDurable,
            IMonitoredItemQueueFactory queueFactory,
            ITelemetryContext telemetry,
            Action? discardedValueHandler = null,
            TimeProvider? timeProvider = null)
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
        /// <param name="dataValueQueue">The queue to take over.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        /// <param name="samplingInterval">The sampling interval.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="discardedValueHandler">Handler for discarded values</param>
        /// <param name="timeProvider">
        /// Supplies the per-sample timestamp used for sampling-interval throttling, so it can be
        /// mocked in tests. Defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        public DataChangeQueueHandler(
            IDataChangeMonitoredItemQueue dataValueQueue,
            bool discardOldest,
            double samplingInterval,
            ITelemetryContext telemetry,
            Action? discardedValueHandler = null,
            TimeProvider? timeProvider = null)
        {
            m_logger = telemetry.CreateLogger<DataChangeQueueHandler>();

            m_dataValueQueue = dataValueQueue;
            m_monitoredItemId = dataValueQueue.MonitoredItemId;
            m_discardOldest = discardOldest;
            m_discardedValueHandler = discardedValueHandler!;
            m_nextSampleTime = 0;
            m_overflow = default;
            m_overflowPending = false;
            m_timeProvider = timeProvider ?? TimeProvider.System;
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
            bool queueErrors = (diagnosticsMasks & DiagnosticsMasks.OperationAll) != 0;

            m_discardOldest = discardOldest;

            // copy existing values.
            List<DataValue>? existingValues = null;
            List<ServiceResult>? existingErrors = null;
            ServiceResult? requiredError = m_requiredError;
            int requiredIndex = -1;

            if (ItemsInQueue > 0)
            {
                existingValues = new List<DataValue>((int)queueSize);
                existingErrors = new List<ServiceResult>((int)queueSize);

                while (PublishSingleValue(
                    out DataValue value,
                    out ServiceResult error,
                    out bool wasRequired,
                    noEventLog: true,
                    retryOnEmpty: true))
                {
                    if (wasRequired)
                    {
                        requiredIndex = existingValues.Count;
                    }
                    existingValues.Add(value);
                    existingErrors.Add(error);
                }
            }

            m_dataValueQueue.ResetQueue(queueSize, queueErrors);

            m_overflow = default;
            m_overflowPending = false;
            m_required = default;
            m_requiredError = null;
            m_requiredPending = false;

            // requeue the data.
            if (existingValues != null)
            {
                for (int ii = 0; ii < existingValues.Count; ii++)
                {
                    if (ii == requiredIndex)
                    {
                        DataValue requiredValue = existingValues[ii];
                        ServiceResult requeueError =
                            requiredError ?? existingErrors![ii];
                        if (requiredValue.StatusCode.Overflow)
                        {
                            SetOverflowBit(
                                ref requiredValue,
                                ref requeueError);
                        }
                        EnqueueRequired(
                            requiredValue,
                            requeueError,
                            replaceExisting: false);
                    }
                    else
                    {
                        Enqueue(existingValues[ii], existingErrors![ii]);
                    }
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
            long now = m_timeProvider.GetTimestampMilliseconds();

            if (m_dataValueQueue.ItemsInQueue > 0)
            {
                // check if too soon for another sample.
                if (now < m_nextSampleTime)
                {
                    if (!m_dataValueQueue.TryPeekLastValue(out DataValue overwrittenValue) ||
                        IsRequiredMarker(overwrittenValue))
                    {
                        // The missing-node marker has to reach the Client, so it is never
                        // replaced. The sample arrived too soon to be queued in its own right,
                        // so it is dropped instead.
                        return false;
                    }

                    m_logger.OVERWRITTENVALUETOOSOONFORANOTHERSAMPLE(
                        overwrittenValue.WrappedValue,
                        overwrittenValue.StatusCode.Code,
                        m_samplingInterval,
                        now,
                        m_nextSampleTime);

                    m_dataValueQueue.OverwriteLastValue(value, error);

                    m_discardedValueHandler?.Invoke();

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
            return Enqueue(value, error);
        }

        /// <summary>
        /// Queues a required notification without sampling or overflow replacement.
        /// </summary>
        /// <param name="value">The required notification value.</param>
        /// <param name="error">The required notification error.</param>
        /// <param name="replaceExisting">
        /// Whether a different pending required notification is superseded.
        /// </param>
        internal void QueueRequiredValue(
            in DataValue value,
            ServiceResult error,
            bool replaceExisting = false)
        {
            EnqueueRequired(value, error, replaceExisting);
        }

        /// <summary>
        /// Gets whether a required missing-node marker is pending.
        /// </summary>
        internal bool HasRequiredValues => m_requiredPending;

        /// <summary>
        /// Gets the required notification currently protected by the queue.
        /// </summary>
        internal bool TryGetRequiredValue(
            out DataValue value,
            out ServiceResult error)
        {
            value = m_required;
            error = m_requiredError ?? ServiceResult.Good;
            if (m_requiredPending &&
                m_overflowPending &&
                m_overflow == m_required)
            {
                SetOverflowBit(ref value, ref error);
            }
            return m_requiredPending;
        }

        /// <summary>
        /// Restores a required notification and its protection, adding the
        /// notification when a restored queue snapshot does not contain it.
        /// </summary>
        internal void EnsureRequiredValue(
            in DataValue value,
            ServiceResult error)
        {
            var existingValues = new List<DataValue>(
                m_dataValueQueue.ItemsInQueue);
            var existingErrors = new List<ServiceResult>(
                existingValues.Capacity);
            int requiredIndex = -1;
            while (PublishSingleValue(
                out DataValue existingValue,
                out ServiceResult existingError,
                out _,
                noEventLog: true,
                retryOnEmpty: true))
            {
                if (requiredIndex < 0 &&
                    AreEquivalentRequiredValues(existingValue, value))
                {
                    requiredIndex = existingValues.Count;
                }
                existingValues.Add(existingValue);
                existingErrors.Add(existingError);
            }

            for (int ii = 0; ii < existingValues.Count; ii++)
            {
                if (ii == requiredIndex)
                {
                    DataValue requiredValue = value;
                    ServiceResult requiredError = error;
                    if (existingValues[ii].StatusCode.Overflow ||
                        requiredValue.StatusCode.Overflow)
                    {
                        SetOverflowBit(
                            ref requiredValue,
                            ref requiredError);
                    }
                    EnqueueRequired(
                        requiredValue,
                        requiredError,
                        replaceExisting: false);
                }
                else
                {
                    Enqueue(existingValues[ii], existingErrors[ii]);
                }
            }

            if (requiredIndex < 0)
            {
                EnqueueRequired(value, error, replaceExisting: false);
            }
        }

        /// <summary>
        /// Deques the last item
        /// </summary>
        public bool PublishSingleValue(
            out DataValue value,
            out ServiceResult error,
            bool noEventLog = false)
        {
            return PublishSingleValue(
                out value,
                out error,
                out _,
                noEventLog,
                retryOnEmpty: false);
        }

        private bool PublishSingleValue(
            out DataValue value,
            out ServiceResult error,
            out bool wasRequired,
            bool noEventLog,
            bool retryOnEmpty)
        {
            bool dequeued = retryOnEmpty
                ? DequeueWithRetry(out value, out error)
                : m_dataValueQueue.Dequeue(out value, out error);
            if (dequeued)
            {
                wasRequired = IsRequiredMarker(value);
                if (wasRequired)
                {
                    if (m_required.StatusCode.Overflow &&
                        !value.StatusCode.Overflow)
                    {
                        value = m_required;
                        error = m_requiredError ?? error;
                    }
                    m_required = default;
                    m_requiredError = null;
                    m_requiredPending = false;
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
                        value.StatusCode.Overflow,
                        m_monitoredItemId);
                }

                return true;
            }

            wasRequired = false;
            return false;
        }

        /// <summary>
        /// Enque value
        /// </summary>
        /// <returns>true of overflow occured</returns>
        private bool Enqueue(DataValue value, ServiceResult error)
        {
            // check for empty queue.
            if (m_dataValueQueue.ItemsInQueue == 0)
            {
                m_logger.ENQUEUEVALUEValueValue(value.WrappedValue);

                m_dataValueQueue.Enqueue(value, error);

                return false;
            }

            // check if the latest value has initial dummy data
            if (m_dataValueQueue.TryPeekLastValue(out DataValue lastValue) &&
                lastValue.StatusCode == StatusCodes.BadWaitingForInitialData)
            {
                // overwrite the last value
                m_dataValueQueue.OverwriteLastValue(value, error);

                return false;
            }

            // check if queue is full.
            if (m_dataValueQueue.ItemsInQueue == m_dataValueQueue.QueueSize)
            {
                if (!m_discardOldest)
                {
                    if (IsRequiredMarker(lastValue))
                    {
                        // The missing-node marker has to reach the Client, so the incoming value
                        // is discarded instead of the marker, and the marker reports the loss.
                        DiscardIncomingValue(value, lastValue);
                        return true;
                    }

                    m_discardedValueHandler?.Invoke();
                    ServerUtils.ReportDiscardedValue(default, m_monitoredItemId, lastValue);

                    // the newest value reports the loss.
                    m_overflow = value;
                    m_overflowPending = true;

                    // overwrite last value
                    m_dataValueQueue.OverwriteLastValue(value, error);

                    return true;
                }

                if (m_dataValueQueue.TryPeekOldestValue(out DataValue peekedOldest) &&
                    IsRequiredMarker(peekedOldest))
                {
                    // The missing-node marker has to reach the Client, so the incoming value is
                    // discarded instead of the marker, and the marker reports the loss.
                    DiscardIncomingValue(value, peekedOldest);
                    return true;
                }

                m_discardedValueHandler?.Invoke();

                // remove oldest value.
                if (DequeueWithRetry(out DataValue discardedValue, out _))
                {
                    ServerUtils.ReportDiscardedValue(default, m_monitoredItemId, discardedValue);
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInternalError,
                        "Error queueing DataValue. DataValueQueue was full but it was not possible to discard the oldest value.");
                }

                // the value that is now the oldest reports the loss.
                if (m_dataValueQueue.TryPeekOldestValue(out DataValue oldestValue))
                {
                    m_overflow = oldestValue;
                    m_overflowPending = true;
                }

                m_dataValueQueue.Enqueue(value, error);

                return true;
            }

            m_logger.ENQUEUEVALUEValueValue(value.WrappedValue);

            m_dataValueQueue.Enqueue(value, error);

            return false;
        }

        /// <summary>
        /// Queues the marker that tells the Client the monitored Node is gone. It is queued like
        /// any other value, so Part 4 5.13.1.5 ordering holds, and from then on it is the one
        /// value that is never discarded, so a full queue cannot swallow the notification that
        /// Part 4 5.8.4.1 requires.
        /// <para>
        /// The specification does not say how a mandatory data change Notification survives a full
        /// queue: the protected, over capacity entry it defines applies to
        /// EventQueueOverflowEventType only. Issue #4102 records the ambiguity and where to change
        /// this if the behaviour chosen here turns out to be non-compliant.
        /// </para>
        /// </summary>
        /// <param name="value">The marker value.</param>
        /// <param name="error">The marker error.</param>
        /// <param name="replaceExisting">
        /// Whether a different pending marker is superseded.
        /// </param>
        private void EnqueueRequired(
            DataValue value,
            ServiceResult error,
            bool replaceExisting)
        {
            if (m_requiredPending)
            {
                if (m_required.StatusCode == value.StatusCode ||
                    !replaceExisting)
                {
                    return;
                }
                ReplaceRequired(value, error);
                return;
            }

            Enqueue(value, error);
            m_required = value;
            m_requiredError = error;
            m_requiredPending = true;
        }

        private void ReplaceRequired(
            DataValue value,
            ServiceResult error)
        {
            var existingValues = new List<DataValue>(
                Math.Max(m_dataValueQueue.ItemsInQueue - 1, 0));
            var existingErrors = new List<ServiceResult>(
                existingValues.Capacity);
            while (PublishSingleValue(
                out DataValue existingValue,
                out ServiceResult existingError,
                out bool wasRequired,
                noEventLog: true,
                retryOnEmpty: true))
            {
                if (wasRequired)
                {
                    if (existingValue.StatusCode.Overflow)
                    {
                        SetOverflowBit(ref value, ref error);
                    }
                }
                else
                {
                    existingValues.Add(existingValue);
                    existingErrors.Add(existingError);
                }
            }

            for (int ii = 0; ii < existingValues.Count; ii++)
            {
                Enqueue(existingValues[ii], existingErrors[ii]);
            }
            Enqueue(value, error);
            m_required = value;
            m_requiredError = error;
            m_requiredPending = true;
        }

        /// <summary>
        /// Gets whether the queued value is the pending missing-node marker.
        /// </summary>
        /// <param name="value">The queued value.</param>
        private bool IsRequiredMarker(in DataValue value)
        {
            return m_requiredPending &&
                AreEquivalentRequiredValues(m_required, value);
        }

        private static bool AreEquivalentRequiredValues(
            in DataValue left,
            in DataValue right)
        {
            return left.WithStatus(left.StatusCode.SetOverflow(false)) ==
                right.WithStatus(right.StatusCode.SetOverflow(false));
        }

        /// <summary>
        /// Discards the incoming value because the value it would have displaced is the marker,
        /// and lets the marker report the loss instead.
        /// </summary>
        /// <param name="value">The value that is not queued.</param>
        /// <param name="marker">The marker that keeps its place.</param>
        private void DiscardIncomingValue(DataValue value, DataValue marker)
        {
            m_discardedValueHandler?.Invoke();
            ServerUtils.ReportDiscardedValue(default, m_monitoredItemId, value);
            m_overflow = marker;
            m_overflowPending = true;
        }

        /// <summary>
        /// Dequeues a value, tolerating a durable queue that transiently reports no value while it
        /// restores a persisted batch. The retry is bounded, because a queue that permanently
        /// stops handing back the values it reports as queued would otherwise spin a server
        /// thread forever.
        /// </summary>
        /// <param name="value">The value that was dequeued.</param>
        /// <param name="error">The error that belongs to the value.</param>
        private bool DequeueWithRetry(out DataValue value, out ServiceResult error)
        {
            var spinWait = new SpinWait();
            for (int attempt = 0; attempt <= kMaxDrainAttempts; attempt++)
            {
                if (m_dataValueQueue.Dequeue(out value, out error))
                {
                    return true;
                }

                if (m_dataValueQueue.ItemsInQueue == 0)
                {
                    return false;
                }

                spinWait.SpinOnce();
            }

            value = default;
            error = ServiceResult.Good;
            return false;
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

        /// <summary>
        /// The number of consecutive failed dequeue attempts tolerated while draining, before the
        /// queue is treated as broken. A durable queue only fails transiently while it restores a
        /// persisted batch, so exceeding this means it will not recover.
        /// </summary>
        private const int kMaxDrainAttempts = 10000;

        private readonly IDataChangeMonitoredItemQueue m_dataValueQueue;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly uint m_monitoredItemId;
        private bool m_discardOldest;
        private long m_nextSampleTime;
        private long m_samplingInterval;
        private readonly Action m_discardedValueHandler;
        private DataValue m_overflow;
        private bool m_overflowPending;
        private DataValue m_required;
        private ServiceResult? m_requiredError;
        private bool m_requiredPending;
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
