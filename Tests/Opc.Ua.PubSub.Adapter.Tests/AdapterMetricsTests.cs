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
