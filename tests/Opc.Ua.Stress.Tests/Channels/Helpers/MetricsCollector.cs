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
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Tests;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Collects channel-manager metrics and structured diagnostic-log records during stress tests.
    /// </summary>
    /// <remarks>
    /// Structured channel-manager records are captured through a dedicated
    /// <see cref="ITelemetryContext"/> (<see cref="Telemetry"/>) that owns a
    /// <see cref="RecordingLoggerProvider"/>. Call sites must construct the
    /// <see cref="ClientChannelManager"/> under test with <see cref="Telemetry"/>
    /// so its structured logs flow into this collector; the wiring is explicit
    /// and is torn down when the collector is disposed, rather than being
    /// registered permanently into a shared logger factory.
    /// </remarks>
    public sealed class MetricsCollector : IDisposable
    {
        /// <summary>
        /// Initializes a new channel-manager metrics collector.
        /// </summary>
        public MetricsCollector()
        {
            m_loggerProvider = new RecordingLoggerProvider();
            m_telemetry = DefaultTelemetry.Create(
                builder => builder.AddProvider(m_loggerProvider));
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
        /// Gets the telemetry context that call sites must pass into the
        /// <see cref="ClientChannelManager"/> under test so its structured
        /// channel-manager logs are captured by this collector.
        /// </summary>
        public ITelemetryContext Telemetry => m_telemetry;

        /// <summary>
        /// Gets the measurements captured so far.
        /// </summary>
        public IReadOnlyList<MetricMeasurement> Measurements
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_measurements];
                }
            }
        }

        /// <summary>
        /// Gets the structured channel-manager log records captured so far.
        /// </summary>
        public IReadOnlyList<EventRecord> Events
        {
            get
            {
                CaptureNewLogRecords();
                lock (m_lock)
                {
                    return [.. m_events];
                }
            }
        }

        /// <summary>
        /// Gets the latest metric value with the supplied name and converts it to the requested type.
        /// </summary>
        /// <typeparam name="TMetric">The requested metric value type.</typeparam>
        /// <param name="name">The instrument name.</param>
        /// <returns>The latest metric value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
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
        /// Counts structured channel-manager log records with the supplied event name.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <returns>The number of matching records.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is <c>null</c>.</exception>
        public int CountEvents(string eventName)
        {
            if (eventName == null)
            {
                throw new ArgumentNullException(nameof(eventName));
            }

            CaptureNewLogRecords();

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
        /// Stops collecting metrics and structured channel-manager logs.
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
            (m_telemetry as IDisposable)?.Dispose();
            m_loggerProvider.Dispose();
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
        /// A captured structured channel-manager log record.
        /// </summary>
        /// <param name="Name">The <c>EventId.Name</c> of the log record.</param>
        /// <param name="Payload">The structured log properties keyed by name.</param>
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

        /// <summary>
        /// Converts any not-yet-observed <see cref="RecordingLoggerProvider"/> records
        /// belonging to the channel-manager category into <see cref="EventRecord"/>
        /// instances, stamping each with the time it was first observed.
        /// </summary>
        private void CaptureNewLogRecords()
        {
            IReadOnlyList<RecordedLogRecord> records = m_loggerProvider.Records;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }

                for (int i = m_capturedLogRecordCount; i < records.Count; i++)
                {
                    RecordedLogRecord record = records[i];
                    if (!string.Equals(record.CategoryName, ChannelManagerName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string name = record.EventId.Name ??
                        record.EventId.Id.ToString(CultureInfo.InvariantCulture);
                    m_events.Add(
                        new EventRecord(
                            name,
                            CreatePayload(record.Properties),
                            TimeProvider.System.GetUtcNow()));
                }

                m_capturedLogRecordCount = records.Count;
            }
        }

        private static ReadOnlyDictionary<string, object?> CreatePayload(
            IReadOnlyDictionary<string, object?> properties)
        {
            var payload = new Dictionary<string, object?>(properties.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> property in properties)
            {
                if (string.Equals(property.Key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    continue;
                }

                payload[property.Key] = property.Value;
            }

            return new ReadOnlyDictionary<string, object?>(payload);
        }

        private const string ChannelManagerName = "Opc.Ua.ChannelManager";
        private const string ChannelMetricPrefix = "opc.ua.channel.";
        private readonly Lock m_lock = new();
        private readonly MeterListener m_meterListener;
        private readonly RecordingLoggerProvider m_loggerProvider;
        private readonly ITelemetryContext m_telemetry;
        private readonly List<MetricMeasurement> m_measurements = [];
        private readonly List<EventRecord> m_events = [];
        private int m_capturedLogRecordCount;
        private bool m_disposed;
    }
}
