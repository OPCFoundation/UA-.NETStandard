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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// In-memory <see cref="IIsa95GeoSpatialLocationProvider"/> that can be
    /// constructed directly as a fallback when no dependency-injection
    /// registration is available. The current value can be mutated through
    /// <see cref="Update(Isa95GeoSpatialLocation)"/> and observed through
    /// <see cref="SubscribeAsync"/>; a fault can be injected with
    /// <see cref="Fault"/> to surface an error to readers.
    /// </summary>
    public sealed class Isa95GeoSpatialLocationProvider
        : IIsa95GeoSpatialLocationProvider, IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="Isa95GeoSpatialLocationProvider"/> class with an initial
        /// good-quality value.
        /// </summary>
        /// <param name="initialValue">
        /// The initial geospatial location literal, or <c>null</c>.
        /// </param>
        public Isa95GeoSpatialLocationProvider(string? initialValue = null)
            : this(Isa95GeoSpatialLocation.Good(initialValue))
        {
        }

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="Isa95GeoSpatialLocationProvider"/> class.
        /// </summary>
        /// <param name="initial">
        /// The initial geospatial location snapshot.
        /// </param>
        public Isa95GeoSpatialLocationProvider(Isa95GeoSpatialLocation initial)
        {
            m_current = initial;
        }

        /// <summary>
        /// Gets the current snapshot held by the provider.
        /// </summary>
        public Isa95GeoSpatialLocation Current
        {
            get
            {
                lock (m_lock)
                {
                    return m_current;
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<Isa95GeoSpatialLocation> GetCurrentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Exception? fault;
            Isa95GeoSpatialLocation current;
            lock (m_lock)
            {
                ThrowIfDisposed();
                fault = m_fault;
                current = m_current;
            }

            if (fault != null)
            {
                return new ValueTask<Isa95GeoSpatialLocation>(
                    Task.FromException<Isa95GeoSpatialLocation>(fault));
            }
            return new ValueTask<Isa95GeoSpatialLocation>(current);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<Isa95GeoSpatialLocation> SubscribeAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var subscription = new Subscription();
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_subscribers.Add(subscription);
            }

            try
            {
                while (true)
                {
                    bool cancelled = false;
                    try
                    {
                        await subscription.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    if (cancelled)
                    {
                        yield break;
                    }

                    if (!subscription.TryDequeue(out Isa95GeoSpatialLocation location))
                    {
                        yield break;
                    }

                    yield return location;
                }
            }
            finally
            {
                lock (m_lock)
                {
                    m_subscribers.Remove(subscription);
                }
                subscription.Dispose();
            }
        }

        /// <summary>
        /// Replaces the current value and notifies subscribers. Clears any
        /// previously injected fault.
        /// </summary>
        /// <param name="location">
        /// The new geospatial location snapshot.
        /// </param>
        public void Update(Isa95GeoSpatialLocation location)
        {
            List<Subscription> subscribers;
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_current = location;
                m_fault = null;
                subscribers = [.. m_subscribers];
            }

            foreach (Subscription subscriber in subscribers)
            {
                subscriber.Enqueue(location);
            }
        }

        /// <summary>
        /// Replaces the current value and notifies subscribers.
        /// </summary>
        /// <param name="value">
        /// The geospatial location literal.
        /// </param>
        /// <param name="statusCode">
        /// The status code describing the quality of the value.
        /// </param>
        /// <param name="sourceTimestamp">
        /// The UTC source timestamp for the value.
        /// </param>
        public void Update(string? value, StatusCode statusCode, DateTime sourceTimestamp)
        {
            Update(new Isa95GeoSpatialLocation(value, statusCode, sourceTimestamp));
        }

        /// <summary>
        /// Injects a fault that causes subsequent <see cref="GetCurrentAsync"/>
        /// calls to fail until the next <see cref="Update(Isa95GeoSpatialLocation)"/>.
        /// </summary>
        /// <param name="error">
        /// The error to surface to readers.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public void Fault(Exception error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_fault = error;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            List<Subscription> subscribers;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                subscribers = [.. m_subscribers];
                m_subscribers.Clear();
            }

            foreach (Subscription subscriber in subscribers)
            {
                subscriber.Complete();
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(Isa95GeoSpatialLocationProvider));
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<Subscription> m_subscribers = [];
        private Isa95GeoSpatialLocation m_current;
        private Exception? m_fault;
        private bool m_disposed;

        /// <summary>
        /// Per-consumer buffered subscription used to hand geospatial updates to
        /// a single <see cref="SubscribeAsync"/> enumerator.
        /// </summary>
        private sealed class Subscription : IDisposable
        {
            public void Enqueue(Isa95GeoSpatialLocation location)
            {
                lock (m_gate)
                {
                    if (m_disposed || m_completed)
                    {
                        return;
                    }
                    m_queue.Enqueue(location);
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

            public bool TryDequeue(out Isa95GeoSpatialLocation location)
            {
                lock (m_gate)
                {
                    if (m_queue.Count > 0)
                    {
                        location = m_queue.Dequeue();
                        return true;
                    }
                }
                location = default;
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
            private readonly Queue<Isa95GeoSpatialLocation> m_queue = new();
            private bool m_completed;
            private bool m_disposed;
        }
    }
}
