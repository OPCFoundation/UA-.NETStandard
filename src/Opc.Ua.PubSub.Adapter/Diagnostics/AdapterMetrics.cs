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
using System.Diagnostics.Metrics;

namespace Opc.Ua.PubSub.Adapter.Diagnostics
{
    /// <summary>
    /// Observability instruments for the external-server PubSub adapters. The
    /// counters are published through a single <see cref="Meter"/> named
    /// <see cref="MeterName"/> so a host can subscribe with
    /// <c>System.Diagnostics.Metrics</c> (for example the OpenTelemetry metrics
    /// SDK) and observe the adapter's Read, Write, method-Call and metadata
    /// activity, including the success/failure split that complements the
    /// adapter's leveled logging.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton in the dependency-injection container by the
    /// adapter composition steps and injected into the adapter components. The
    /// type is also usable directly (the <c>AdapterMetrics</c> constructor) when
    /// the components are created without a container.
    /// </remarks>
    public sealed class AdapterMetrics : IDisposable
    {
        /// <summary>
        /// The <see cref="Meter.Name"/> the adapter publishes its instruments
        /// under.
        /// </summary>
        public const string MeterName = "Opc.Ua.PubSub.Adapter";

        private readonly Meter m_meter;
        private readonly Counter<long> m_reads;
        private readonly Counter<long> m_readFailures;
        private readonly Counter<long> m_writes;
        private readonly Counter<long> m_writeFailures;
        private readonly Counter<long> m_calls;
        private readonly Counter<long> m_callFailures;
        private readonly Counter<long> m_metadataResolutions;
        private readonly Counter<long> m_metadataFailures;

        /// <summary>
        /// Creates the adapter metric instruments.
        /// </summary>
        public AdapterMetrics()
        {
            m_meter = new Meter(MeterName);
            m_reads = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.reads",
                unit: "{read}",
                description: "Number of Read service calls issued to external servers.");
            m_readFailures = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.read.failures",
                unit: "{read}",
                description: "Number of failed Read service calls to external servers.");
            m_writes = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.writes",
                unit: "{write}",
                description: "Number of Write service calls issued to external servers.");
            m_writeFailures = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.write.failures",
                unit: "{write}",
                description: "Number of failed Write service calls to external servers.");
            m_calls = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.calls",
                unit: "{call}",
                description: "Number of method Call service calls issued to external servers.");
            m_callFailures = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.call.failures",
                unit: "{call}",
                description: "Number of failed method Call service calls to external servers.");
            m_metadataResolutions = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.metadata.resolutions",
                unit: "{resolution}",
                description: "Number of DataSet metadata resolutions from external servers.");
            m_metadataFailures = m_meter.CreateCounter<long>(
                "opcua.pubsub.adapter.metadata.failures",
                unit: "{resolution}",
                description: "Number of failed DataSet metadata resolutions from external servers.");
        }

        /// <summary>
        /// Records the outcome of a Read service call covering
        /// <paramref name="nodeCount"/> nodes.
        /// </summary>
        /// <param name="nodeCount">
        /// The number of nodes the Read covered.
        /// </param>
        /// <param name="success">
        /// <c>true</c> when the read succeeded; otherwise <c>false</c>.
        /// </param>
        public void RecordRead(int nodeCount, bool success)
        {
            m_reads.Add(1);
            if (!success)
            {
                m_readFailures.Add(1);
            }
        }

        /// <summary>
        /// Records the outcome of a Write service call.
        /// </summary>
        /// <param name="success">
        /// <c>true</c> when the write succeeded; otherwise <c>false</c>.
        /// </param>
        public void RecordWrite(bool success)
        {
            m_writes.Add(1);
            if (!success)
            {
                m_writeFailures.Add(1);
            }
        }

        /// <summary>
        /// Records the outcome of a method Call service call.
        /// </summary>
        /// <param name="success">
        /// <c>true</c> when the call succeeded; otherwise <c>false</c>.
        /// </param>
        public void RecordCall(bool success)
        {
            m_calls.Add(1);
            if (!success)
            {
                m_callFailures.Add(1);
            }
        }

        /// <summary>
        /// Records the outcome of a DataSet metadata resolution.
        /// </summary>
        /// <param name="success">
        /// <c>true</c> when the resolution completed against the server;
        /// otherwise <c>false</c>.
        /// </param>
        public void RecordMetadataResolution(bool success)
        {
            m_metadataResolutions.Add(1);
            if (!success)
            {
                m_metadataFailures.Add(1);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_meter.Dispose();
        }
    }
}
