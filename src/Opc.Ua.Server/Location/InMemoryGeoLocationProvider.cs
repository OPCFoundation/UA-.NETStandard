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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// In-memory <see cref="IGeoLocationProvider"/> for tests, samples and
    /// servers whose locations are pushed in from elsewhere.
    /// </summary>
    /// <remarks>
    /// Sources appear the first time they are written with
    /// <see cref="Update(string, GeoLocationSample)"/>. Readers observe changes
    /// through <see cref="WatchAsync"/>, and <see cref="Fault"/> makes a source
    /// fail so error handling can be exercised.
    /// </remarks>
    public sealed class InMemoryGeoLocationProvider : IGeoLocationProvider, IDisposable
    {
        /// <inheritdoc/>
        public bool SupportsPush => true;

        /// <inheritdoc/>
        public ValueTask<GeoLocationSample> ReadAsync(
            string sourceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSourceId(sourceId);

            Exception? fault;
            GeoLocationSample current;
            lock (m_lock)
            {
                ThrowIfDisposed();
                if (!m_sources.TryGetValue(sourceId, out SourceState? source))
                {
                    return new ValueTask<GeoLocationSample>(
                        Task.FromException<GeoLocationSample>(
                            ServiceResultException.Create(
                                StatusCodes.BadNotFound,
                                "No location is held for source '{0}'.",
                                sourceId)));
                }
                fault = source.Fault;
                current = source.Current;
            }

            return fault != null
                ? new ValueTask<GeoLocationSample>(
                    Task.FromException<GeoLocationSample>(fault))
                : new ValueTask<GeoLocationSample>(current);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<GeoLocationSample> WatchAsync(
            string sourceId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ValidateSourceId(sourceId);

            var subscription = new Subscription();
            lock (m_lock)
            {
                ThrowIfDisposed();
                GetOrAddSource(sourceId).Subscribers.Add(subscription);
            }

            try
            {
                while (true)
                {
                    bool cancelled = false;
                    try
                    {
                        await subscription.WaitAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    if (cancelled ||
                        !subscription.TryDequeue(out GeoLocationSample sample))
                    {
                        yield break;
                    }

                    yield return sample;
                }
            }
            finally
            {
                lock (m_lock)
                {
                    if (m_sources.TryGetValue(sourceId, out SourceState? source))
                    {
                        source.Subscribers.Remove(subscription);
                    }
                }
                subscription.Dispose();
            }
        }

        /// <summary>
        /// Replaces the location of a source and notifies its watchers,
        /// clearing any fault previously injected for it.
        /// </summary>
        /// <param name="sourceId">The source to update.</param>
        /// <param name="sample">The new sample.</param>
        public void Update(string sourceId, GeoLocationSample sample)
        {
            ValidateSourceId(sourceId);

            List<Subscription> subscribers;
            lock (m_lock)
            {
                ThrowIfDisposed();
                SourceState source = GetOrAddSource(sourceId);
                source.Current = sample;
                source.Fault = null;
                subscribers = [.. source.Subscribers];
            }

            for (int ii = 0; ii < subscribers.Count; ii++)
            {
                subscribers[ii].Enqueue(sample);
            }
        }

        /// <summary>
        /// Replaces the location of a source with a good-quality position.
        /// </summary>
        /// <param name="sourceId">The source to update.</param>
        /// <param name="position">The new position.</param>
        /// <param name="sourceTimestamp">
        /// When the reading was taken; omit to let the consumer substitute the
        /// current UTC time.
        /// </param>
        public void Update(
            string sourceId,
            GeoPosition position,
            DateTimeUtc sourceTimestamp = default)
        {
            Update(sourceId, GeoLocationSample.Good(position, sourceTimestamp));
        }

        /// <summary>
        /// Makes a source fail until it is next updated.
        /// </summary>
        /// <param name="sourceId">The source to fault.</param>
        /// <param name="error">The error to surface to readers.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="error"/> is <c>null</c>.
        /// </exception>
        public void Fault(string sourceId, Exception error)
        {
            ValidateSourceId(sourceId);
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                GetOrAddSource(sourceId).Fault = error;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var subscribers = new List<Subscription>();
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                foreach (SourceState source in m_sources.Values)
                {
                    subscribers.AddRange(source.Subscribers);
                    source.Subscribers.Clear();
                }
                m_sources.Clear();
            }

            for (int ii = 0; ii < subscribers.Count; ii++)
            {
                subscribers[ii].Complete();
            }
        }

        private SourceState GetOrAddSource(string sourceId)
        {
            if (!m_sources.TryGetValue(sourceId, out SourceState? source))
            {
                source = new SourceState();
                m_sources.Add(sourceId, source);
            }
            return source;
        }

        private static void ValidateSourceId(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException(
                    "A stable source identifier is required.",
                    nameof(sourceId));
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(InMemoryGeoLocationProvider));
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, SourceState> m_sources = [];
        private bool m_disposed;

        /// <summary>
        /// The value, fault and watchers held for one source.
        /// </summary>
        private sealed class SourceState
        {
            public List<Subscription> Subscribers { get; } = [];

            public GeoLocationSample Current { get; set; }

            public Exception? Fault { get; set; }
        }

        /// <summary>
        /// Per-consumer buffered subscription handing updates to a single
        /// <see cref="WatchAsync"/> enumerator.
        /// </summary>
        private sealed class Subscription : IDisposable
        {
            public void Enqueue(GeoLocationSample sample)
            {
                lock (m_gate)
                {
                    if (m_disposed || m_completed)
                    {
                        return;
                    }
                    m_queue.Enqueue(sample);
                    m_signal.Release();
                }
            }

            public void Complete()
            {
                lock (m_gate)
                {
                    if (m_disposed || m_completed)
                    {
                        return;
                    }
                    m_completed = true;
                    m_signal.Release();
                }
            }

            public Task WaitAsync(CancellationToken cancellationToken)
            {
                return m_signal.WaitAsync(cancellationToken);
            }

            public bool TryDequeue(out GeoLocationSample sample)
            {
                lock (m_gate)
                {
                    if (m_queue.Count > 0)
                    {
                        sample = m_queue.Dequeue();
                        return true;
                    }
                }
                sample = default;
                return false;
            }

            public void Dispose()
            {
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                }
                m_signal.Dispose();
            }

            private readonly Lock m_gate = new();
            private readonly SemaphoreSlim m_signal = new(0);
            private readonly Queue<GeoLocationSample> m_queue = new();
            private bool m_completed;
            private bool m_disposed;
        }
    }
}
