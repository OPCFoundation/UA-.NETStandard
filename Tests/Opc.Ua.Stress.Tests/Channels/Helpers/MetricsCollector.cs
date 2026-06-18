/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Threading;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Collects channel-manager metrics and EventSource records during stress tests.
    /// </summary>
    public sealed class MetricsCollector : IDisposable
    {
        /// <summary>
        /// Initializes a new channel-manager metrics collector.
        /// </summary>
        public MetricsCollector()
        {
            m_eventListener = new ChannelManagerEventListener(this);
            m_meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ShouldCollectInstrument(instrument))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            m_meterListener.SetMeasurementEventCallback<int>(OnIntMeasurementRecorded);
            m_meterListener.SetMeasurementEventCallback<long>(OnLongMeasurementRecorded);
            m_meterListener.SetMeasurementEventCallback<float>(OnFloatMeasurementRecorded);
            m_meterListener.SetMeasurementEventCallback<double>(OnDoubleMeasurementRecorded);
            m_meterListener.Start();
        }

        /// <summary>
        /// Gets the measurements captured so far.
        /// </summary>
        public IReadOnlyList<MetricMeasurement> Measurements
        {
            get
            {
                lock (m_lock)
                {
                    return m_measurements.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets the EventSource records captured so far.
        /// </summary>
        public IReadOnlyList<EventRecord> Events
        {
            get
            {
                lock (m_lock)
                {
                    return m_events.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets the latest metric value with the supplied name and converts it to the requested type.
        /// </summary>
        /// <typeparam name="TMetric">The requested metric value type.</typeparam>
        /// <param name="name">The instrument name.</param>
        /// <returns>The latest metric value.</returns>
        public TMetric GetMetric<TMetric>(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            MetricMeasurement? measurement;
            lock (m_lock)
            {
                measurement = m_measurements.FindLast(metric =>
                    string.Equals(metric.Name, name, StringComparison.Ordinal));
            }

            if (measurement == null)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Metric '{name}' was not captured."));
            }

            if (typeof(TMetric) == typeof(MetricMeasurement))
            {
                return (TMetric)(object)measurement;
            }

            return (TMetric)Convert.ChangeType(
                measurement.Value,
                typeof(TMetric),
                CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Counts EventSource records with the supplied event name.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <returns>The number of matching records.</returns>
        public int CountEvents(string eventName)
        {
            if (eventName == null)
            {
                throw new ArgumentNullException(nameof(eventName));
            }

            int count = 0;
            lock (m_lock)
            {
                foreach (EventRecord eventRecord in m_events)
                {
                    if (string.Equals(eventRecord.Name, eventName, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Records observable gauge instruments now.
        /// </summary>
        public void RecordObservableInstruments()
        {
            m_meterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// Stops collecting metrics and events.
        /// </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }

                m_disposed = true;
            }

            m_meterListener.Dispose();
            m_eventListener.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// A captured instrument measurement.
        /// </summary>
        /// <param name="Name">The instrument name.</param>
        /// <param name="Value">The measurement value as a double.</param>
        /// <param name="Tags">The measurement tags.</param>
        /// <param name="Timestamp">The capture timestamp.</param>
        public record MetricMeasurement(
            string Name,
            double Value,
            TagList Tags,
            DateTimeOffset Timestamp);

        /// <summary>
        /// A captured EventSource record.
        /// </summary>
        /// <param name="Name">The event name.</param>
        /// <param name="Payload">The event payload keyed by payload name.</param>
        /// <param name="Timestamp">The capture timestamp.</param>
        public record EventRecord(
            string Name,
            IReadOnlyDictionary<string, object?> Payload,
            DateTimeOffset Timestamp);

        private static bool ShouldCollectInstrument(Instrument instrument)
        {
            return instrument.Name.StartsWith(ChannelMetricPrefix, StringComparison.Ordinal) ||
                string.Equals(instrument.Meter.Name, ChannelManagerName, StringComparison.Ordinal);
        }

        private static TagList CopyTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var tagList = new TagList();
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                tagList.Add(tag.Key, tag.Value);
            }

            return tagList;
        }

        private void OnIntMeasurementRecorded(
            Instrument instrument,
            int measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            AddMeasurement(instrument, measurement, tags);
        }

        private void OnLongMeasurementRecorded(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            AddMeasurement(instrument, measurement, tags);
        }

        private void OnFloatMeasurementRecorded(
            Instrument instrument,
            float measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            AddMeasurement(instrument, measurement, tags);
        }

        private void OnDoubleMeasurementRecorded(
            Instrument instrument,
            double measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            AddMeasurement(instrument, measurement, tags);
        }

        private void AddMeasurement(
            Instrument instrument,
            double measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var record = new MetricMeasurement(
                instrument.Name,
                measurement,
                CopyTags(tags),
                TimeProvider.System.GetUtcNow());
            lock (m_lock)
            {
                if (!m_disposed)
                {
                    m_measurements.Add(record);
                }
            }
        }

        private void AddEvent(EventWrittenEventArgs eventData)
        {
            string name = eventData.EventName ?? eventData.EventId.ToString(CultureInfo.InvariantCulture);
            var payload = new Dictionary<string, object?>();
            ReadOnlyCollection<object?>? payloadValues = eventData.Payload;
            ReadOnlyCollection<string>? payloadNames = eventData.PayloadNames;
            if (payloadValues != null)
            {
                for (int i = 0; i < payloadValues.Count; i++)
                {
                    string key = payloadNames != null && i < payloadNames.Count
                        ? payloadNames[i]
                        : i.ToString(CultureInfo.InvariantCulture);
                    payload[key] = payloadValues[i];
                }
            }

            var record = new EventRecord(
                name,
                new ReadOnlyDictionary<string, object?>(payload),
                TimeProvider.System.GetUtcNow());
            lock (m_lock)
            {
                if (!m_disposed)
                {
                    m_events.Add(record);
                }
            }
        }

        private sealed class ChannelManagerEventListener : EventListener
        {
            public ChannelManagerEventListener(MetricsCollector owner)
            {
                m_owner = owner;
                foreach (EventSource eventSource in EventSource.GetSources())
                {
                    EnableIfChannelManager(eventSource);
                }
            }

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                EnableIfChannelManager(eventSource);
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                m_owner?.AddEvent(eventData);
            }

            private void EnableIfChannelManager(EventSource eventSource)
            {
                if (string.Equals(eventSource.Name, ChannelManagerName, StringComparison.Ordinal))
                {
                    EnableEvents(eventSource, EventLevel.LogAlways);
                }
            }

            private MetricsCollector? m_owner;
        }

        private const string ChannelManagerName = "Opc.Ua.ChannelManager";
        private const string ChannelMetricPrefix = "opcua.channel.";
        private readonly Lock m_lock = new();
        private readonly MeterListener m_meterListener;
        private readonly ChannelManagerEventListener m_eventListener;
        private readonly List<MetricMeasurement> m_measurements = [];
        private readonly List<EventRecord> m_events = [];
        private bool m_disposed;
    }
}
