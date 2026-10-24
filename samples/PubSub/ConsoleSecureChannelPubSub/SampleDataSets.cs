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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// An injectable, finite application source; the existing WriterGroup owns sampling and serialization.
    /// </summary>
    internal sealed class SampleSource(int offset) : IPublishedDataSetSource
    {
        /// <inheritdoc/>
        public DataSetMetaDataType BuildMetaData()
        {
            return SampleConfiguration.MetaData(
                offset == 0 ? SampleConfiguration.TelemetryDataSet : SampleConfiguration.OtherDataSet);
        }

        /// <inheritdoc/>
        public async ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default)
        {
            int counter = offset + await m_samples.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new PublishedDataSetSnapshot(
                metaData.ConfigurationVersion,
                [
                    new DataSetField { Name = "Counter", Value = new Variant(counter) },
                    new DataSetField { Name = "Toggle", Value = new Variant((counter & 1) == 0) },
                    new DataSetField { Name = "Temperature", Value = new Variant(20.0 + (counter * 0.25)) }
                ],
                DateTimeUtc.From(DateTimeOffset.UtcNow));
        }

        /// <summary>
        /// Makes exactly one changing snapshot available to the normal publishing scheduler.
        /// </summary>
        internal ValueTask ReleaseAsync(int sequence, CancellationToken cancellationToken)
        {
            return m_samples.Writer.WriteAsync(sequence, cancellationToken);
        }

        private readonly Channel<int> m_samples = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// Validates decoded, metadata-typed values independently of raw message capture.
    /// </summary>
    internal sealed class SampleSink(int offset, int count) : ISubscribedDataSetSink
    {
        /// <summary>
        /// Gets concrete value-delivery progress.
        /// </summary>
        internal ProgressCounter Progress { get; } = new();

        /// <summary>
        /// Gets successfully decoded Int32 counters.
        /// </summary>
        internal int[] Counters => [.. m_counters];

        /// <summary>
        /// Gets successfully decoded Boolean values.
        /// </summary>
        internal bool[] Toggles => [.. m_toggles];

        /// <summary>
        /// Gets successfully decoded Double values.
        /// </summary>
        internal double[] Temperatures => [.. m_temperatures];

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            IReadOnlyList<DataSetField> fields,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int expected = offset + Progress.Count + 1;
            if (Progress.Count >= count ||
                fields.Count != 3 ||
                fields[0].Name != "Counter" ||
                !fields[0].Value.TryGetValue(out int counter) ||
                counter != expected ||
                fields[1].Name != "Toggle" ||
                !fields[1].Value.TryGetValue(out bool toggle) ||
                toggle != ((expected & 1) == 0) ||
                fields[2].Name != "Temperature" ||
                !fields[2].Value.TryGetValue(out double temperature) ||
                temperature != 20.0 + (expected * 0.25))
            {
                var failure = new InvalidOperationException(
                    $"Decoded writer values or metadata differ from sample {expected}.");
                m_failure.TrySetException(failure);
                return ValueTask.FromException(failure);
            }
            m_counters.Enqueue(counter);
            m_toggles.Enqueue(toggle);
            m_temperatures.Enqueue(temperature);
            Progress.Increment();
            return default;
        }

        /// <summary>
        /// Fails immediately on a decoding mismatch rather than trusting packet counts.
        /// </summary>
        internal async Task WaitAsync(int target, CancellationToken cancellationToken)
        {
            Task progress = Progress.WaitAsync(target, cancellationToken);
            Task completed = await Task.WhenAny(progress, m_failure.Task).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }

        private readonly ConcurrentQueue<int> m_counters = new();
        private readonly ConcurrentQueue<bool> m_toggles = new();
        private readonly ConcurrentQueue<double> m_temperatures = new();
        private readonly TaskCompletionSource m_failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Records any delivery that escaped an ordinary PublisherId or DataSetWriterId filter.
    /// </summary>
    internal sealed class UnmatchedSink : ISubscribedDataSetSink
    {
        /// <summary>
        /// Gets the number of incorrectly routed deliveries.
        /// </summary>
        internal int Deliveries => Volatile.Read(ref m_deliveries);

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            IReadOnlyList<DataSetField> fields,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref m_deliveries);
            return default;
        }

        private int m_deliveries;
    }
}
