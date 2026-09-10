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
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns aggregate calculation and the historical-to-live handoff for one monitored item.
    /// </summary>
    internal sealed class AggregationFilterHandler : IMonitoringFilter
    {
        /// <summary>
        /// Initializes aggregate processing with the existing manager and processed-value receiver.
        /// </summary>
        internal AggregationFilterHandler(
            AggregateManager aggregateManager,
            ProcessedValueHandler queueProcessedValue,
            MonitoringFilter? filter)
        {
            m_aggregateManager = aggregateManager ?? throw new ArgumentNullException(nameof(aggregateManager));
            m_queueProcessedValue = queueProcessedValue ?? throw new ArgumentNullException(nameof(queueProcessedValue));
            Filter = filter;
        }

        /// <summary>
        /// Initializes the calculator and optional historical priming for an aggregate filter.
        /// </summary>
        internal AggregationFilterHandler(
            AggregateManager aggregateManager,
            ServerAggregateFilter filter,
            ProcessedValueHandler queueProcessedValue,
            bool primeInitialValue)
            : this(aggregateManager, queueProcessedValue, filter)
        {
            m_calculator = CreateCalculator(filter);
            m_initialValuePending = primeInitialValue && filter.PrimeInitialValue;
            m_initialValueKeySelector = filter.HistorianKeySelector ?? TimestampStructuredDataKeySelector.Instance;
        }

        /// <summary>
        /// Gets the committed filter, retaining the current definition while an aggregate change is prepared.
        /// </summary>
        public MonitoringFilter? Filter { get; private set; }

        /// <summary>
        /// Checks whether the active calculator's interval has ended.
        /// </summary>
        internal bool HasEndTimePassed(DateTime utcNow)
        {
            return m_calculator?.HasEndTimePassed(utcNow) == true;
        }

        /// <summary>
        /// Prepares a calculator for a prospective aggregate-filter modification.
        /// </summary>
        internal Modification? PrepareModification(ServerAggregateFilter filter)
        {
            m_preparedModification = null;
            if (IsEquivalentFilter(filter))
            {
                filter.PrimeInitialValue = false;
                return null;
            }

            Modification preparation = CreateModification(filter);
            m_preparedModification = preparation;
            return preparation;
        }

        /// <summary>
        /// Discards a matching preparation without disturbing a newer modification request.
        /// </summary>
        internal void CancelModification(Modification preparation)
        {
            if (ReferenceEquals(m_preparedModification, preparation))
            {
                m_preparedModification = null;
            }
        }

        /// <summary>
        /// Selects the calculator change to apply, reusing a matching preparation when available.
        /// </summary>
        internal Modification? PrepareChange(MonitoringFilter? filter)
        {
            if (filter is ServerAggregateFilter aggregateFilter)
            {
                if (IsEquivalentFilter(aggregateFilter))
                {
                    aggregateFilter.PrimeInitialValue = false;
                    return null;
                }

                if (m_preparedModification is { } prepared &&
                    ReferenceEquals(prepared.Filter, aggregateFilter))
                {
                    return prepared;
                }

                return CreateModification(aggregateFilter);
            }

            return m_calculator != null ? new Modification(null, null) : null;
        }

        /// <summary>
        /// Commits the effective filter and resets historical priming when its calculator changes.
        /// </summary>
        internal void CommitChange(MonitoringFilter? filter, Modification? change)
        {
            Filter = filter;
            if (change == null)
            {
                return;
            }

            m_calculator = change.Calculator;
            ClearInitialValueState();
            var aggregateFilter = filter as ServerAggregateFilter;
            m_initialValuePending = m_calculator != null && aggregateFilter?.PrimeInitialValue == true;
            m_initialValueKeySelector =
                aggregateFilter?.HistorianKeySelector ?? TimestampStructuredDataKeySelector.Instance;

            if (ReferenceEquals(m_preparedModification, change))
            {
                change.MarkCommitted();
                m_preparedModification = null;
            }
        }

        /// <summary>
        /// Defers live delivery during historical priming and records overflow of the bounded buffer.
        /// </summary>
        internal bool TryBufferLiveValue(in DataValue value, ServiceResult? error, bool ignoreFilters)
        {
            if (!m_initialValuePending)
            {
                return false;
            }

            if ((m_pendingValues?.Count ?? 0) >= kMaxPendingInitialValues)
            {
                m_initialValueOverflowed = true;
                return true;
            }

            (m_pendingValues ??= []).Add(new PendingValue(
                value.IsNull ? DataValue.Null : value.Copy(),
                error,
                ignoreFilters));
            return true;
        }

        /// <summary>
        /// Feeds a raw sample to the calculator, tracks accepted history, and queues completed aggregates.
        /// </summary>
        internal bool TryQueueValue(in DataValue value, bool initialValue, out bool accepted)
        {
            accepted = false;
            if (m_calculator == null)
            {
                return false;
            }

            accepted = m_calculator.QueueRawValue(value);
            if (accepted && initialValue && m_initialValuePending)
            {
                if (TryGetInitialValueKey(value, out HistoricalValueKey key))
                {
                    (m_initialValueKeys ??= []).Add(key);
                }
                else
                {
                    (m_initialValuesWithoutSourceTimestamp ??= []).Add(value);
                }
            }

            while (m_calculator.TryGetProcessedValue(false, out DataValue processedValue))
            {
                m_queueProcessedValue(processedValue);
            }

            return true;
        }

        /// <summary>
        /// Ends historical priming and replays buffered live values not already represented in history.
        /// </summary>
        /// <returns>
        /// Good on completion, or <see cref="StatusCodes.BadTooManyOperations"/> if the live buffer overflowed.
        /// </returns>
        internal ServiceResult CompleteInitialValue(LiveValueHandler queueLiveValue)
        {
            List<PendingValue>? pendingValues = m_pendingValues;
            HashSet<HistoricalValueKey>? initialValueKeys = m_initialValueKeys;
            List<DataValue>? initialValuesWithoutSourceTimestamp = m_initialValuesWithoutSourceTimestamp;
            bool overflowed = m_initialValueOverflowed;
            ClearInitialValueState();

            if (overflowed)
            {
                return StatusCodes.BadTooManyOperations;
            }

            if (pendingValues != null)
            {
                foreach (PendingValue pending in pendingValues)
                {
                    DataValue value = pending.Value;
                    if (!value.IsNull && pending.Error != null && pending.Error.StatusCode.Code != 0)
                    {
                        value = value.WithStatus(pending.Error.StatusCode);
                    }

                    // Timestamp/key identities cover archived samples; unkeyed values require full equality.
                    bool alreadyRepresented = ServiceResult.IsGood(pending.Error) &&
                        (TryGetInitialValueKey(value, out HistoricalValueKey key)
                            ? initialValueKeys?.Contains(key) == true
                            : initialValuesWithoutSourceTimestamp?.Exists(candidate => candidate.Equals(value)) == true);
                    if (!alreadyRepresented)
                    {
                        queueLiveValue(pending.Value, pending.Error, pending.IgnoreFilters);
                    }
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Publishes completed aggregates and an available partial interval when the calculator is ready.
        /// </summary>
        internal void Publish(DateTime utcNow)
        {
            if (m_calculator == null || !m_calculator.HasEndTimePassed(utcNow))
            {
                return;
            }

            while (m_calculator.TryGetProcessedValue(false, out DataValue processedValue))
            {
                m_queueProcessedValue(processedValue);
            }

            if (m_calculator.TryGetProcessedValue(true, out DataValue partialValue))
            {
                m_queueProcessedValue(partialValue);
            }
        }

        private Modification CreateModification(ServerAggregateFilter filter)
        {
            IAggregateCalculator? calculator = CreateCalculator(filter);
            if (calculator == null)
            {
                filter.PrimeInitialValue = false;
            }

            return new Modification(filter, calculator);
        }

        private IAggregateCalculator? CreateCalculator(ServerAggregateFilter filter)
        {
            return m_aggregateManager.CreateCalculator(
                filter.AggregateType,
                (DateTime)filter.StartTime,
                DateTime.MaxValue,
                filter.ProcessingInterval,
                filter.Stepped,
                filter.AggregateConfiguration);
        }

        private bool IsEquivalentFilter(ServerAggregateFilter filter)
        {
            return Filter is ServerAggregateFilter existing &&
                m_calculator != null &&
                existing.AggregateType == filter.AggregateType &&
                existing.ProcessingInterval == filter.ProcessingInterval &&
                existing.StartTime == filter.StartTime &&
                existing.Stepped == filter.Stepped &&
                existing.AggregateConfiguration.IsEqual(filter.AggregateConfiguration);
        }

        private bool TryGetInitialValueKey(in DataValue value, out HistoricalValueKey key)
        {
            if (value.SourceTimestamp == DateTimeUtc.MinValue ||
                !m_initialValueKeySelector.TryGetUniquenessKey(value, out ByteString uniquenessKey))
            {
                key = default;
                return false;
            }

            key = new HistoricalValueKey(value.SourceTimestamp, uniquenessKey);
            return true;
        }

        private void ClearInitialValueState()
        {
            m_initialValuePending = false;
            m_pendingValues = null;
            m_initialValueKeys = null;
            m_initialValuesWithoutSourceTimestamp = null;
            m_initialValueOverflowed = false;
        }

        /// <summary>
        /// Receives a processed aggregate for the monitored item's notification queue.
        /// </summary>
        internal delegate void ProcessedValueHandler(in DataValue value);

        /// <summary>
        /// Replays a buffered live value through the monitored item's ordinary filtering and queueing.
        /// </summary>
        internal delegate void LiveValueHandler(in DataValue value, ServiceResult? error, bool ignoreFilters);

        /// <summary>
        /// Carries a prepared calculator change and its commit and historical-priming requirements.
        /// </summary>
        internal sealed class Modification
        {
            /// <summary>
            /// Initializes a calculator change, including removal of aggregate filtering when both values are null.
            /// </summary>
            internal Modification(ServerAggregateFilter? filter, IAggregateCalculator? calculator)
            {
                Filter = filter;
                Calculator = calculator;
            }

            /// <summary>
            /// Gets the proposed server-revised aggregate filter.
            /// </summary>
            internal ServerAggregateFilter? Filter { get; }

            /// <summary>
            /// Gets the prepared calculator, or null when aggregate calculation is unavailable or removed.
            /// </summary>
            internal IAggregateCalculator? Calculator { get; }

            /// <summary>
            /// Gets whether this prevalidated change was applied to the monitored item.
            /// </summary>
            internal bool IsCommitted { get; private set; }

            /// <summary>
            /// Gets whether the prepared calculator requires historical initial-value input.
            /// </summary>
            internal bool RequiresInitialValue => Calculator != null && Filter?.PrimeInitialValue == true;

            /// <summary>
            /// Records that the prepared change was committed before subsequent notification callbacks.
            /// </summary>
            internal void MarkCommitted()
            {
                IsCommitted = true;
            }
        }

        private readonly record struct PendingValue(DataValue Value, ServiceResult? Error, bool IgnoreFilters);

        private readonly AggregateManager m_aggregateManager;
        private readonly ProcessedValueHandler m_queueProcessedValue;
        private IAggregateCalculator? m_calculator;
        private Modification? m_preparedModification;
        private bool m_initialValuePending;
        private List<PendingValue>? m_pendingValues;
        private HashSet<HistoricalValueKey>? m_initialValueKeys;
        private List<DataValue>? m_initialValuesWithoutSourceTimestamp;
        private IHistorianStructuredDataKeySelector m_initialValueKeySelector = TimestampStructuredDataKeySelector.Instance;
        private bool m_initialValueOverflowed;

        private const int kMaxPendingInitialValues = 100_000;
    }
}
