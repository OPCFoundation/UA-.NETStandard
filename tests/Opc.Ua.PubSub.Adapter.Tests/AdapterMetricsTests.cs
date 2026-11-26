// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Diagnostics;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AdapterMetrics"/> counter instrumentation.
    /// </summary>
    [TestFixture]
    public sealed class AdapterMetricsTests
    {
        [Test]
        public void RecordMethodsIncrementSuccessAndFailureCounters()
        {
            var measurements = new Dictionary<string, long>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AdapterMetrics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            {
                measurements.TryGetValue(instrument.Name, out long current);
                measurements[instrument.Name] = current + value;
            });
            listener.Start();
            using var metrics = new AdapterMetrics();

            metrics.RecordRead(3, true);
            metrics.RecordRead(1, false);
            metrics.RecordWrite(true);
            metrics.RecordWrite(false);
            metrics.RecordCall(true);
            metrics.RecordCall(false);
            metrics.RecordMetadataResolution(true);
            metrics.RecordMetadataResolution(false);
            listener.RecordObservableInstruments();

            Assert.That(measurements["opcua.pubsub.adapter.reads"], Is.EqualTo(2));
            Assert.That(measurements["opcua.pubsub.adapter.read.failures"], Is.EqualTo(1));
            Assert.That(measurements["opcua.pubsub.adapter.writes"], Is.EqualTo(2));
            Assert.That(measurements["opcua.pubsub.adapter.write.failures"], Is.EqualTo(1));
            Assert.That(measurements["opcua.pubsub.adapter.calls"], Is.EqualTo(2));
            Assert.That(measurements["opcua.pubsub.adapter.call.failures"], Is.EqualTo(1));
            Assert.That(measurements["opcua.pubsub.adapter.metadata.resolutions"], Is.EqualTo(2));
            Assert.That(measurements["opcua.pubsub.adapter.metadata.failures"], Is.EqualTo(1));
        }
    }
}
