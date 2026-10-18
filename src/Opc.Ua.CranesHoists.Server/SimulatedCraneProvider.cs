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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// A bounded, explicitly simulated, single-host provider for examples and deterministic tests.
    /// </summary>
    /// <remarks>
    /// Only caller-supplied samples are returned. Accepted requests are recorded independently;
    /// they never change physical state or active-limit samples. Use application adapters for
    /// real equipment or distributed state. An independent authorizer is still required.
    /// </remarks>
    public sealed class SimulatedCraneProvider : ICraneDataProvider, ICraneLimitProvider, IAsyncDisposable
    {
        /// <summary>
        /// Creates a simulation with explicit samples and optional request acceptance.
        /// </summary>
        public SimulatedCraneProvider(
            ArrayOf<CraneSample> samples,
            bool acceptLimitRequests = false,
            int maximumSamples = 1024,
            int maximumRequests = 128)
        {
            if (maximumSamples is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSamples));
            }
            if (maximumRequests is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRequests));
            }
            m_maximumSamples = maximumSamples;
            m_maximumRequests = maximumRequests;
            m_acceptRequests = acceptLimitRequests;
            m_samples = Validate(samples);
        }

        /// <summary>
        /// Gets a bounded copy of accepted requests, not a report of active hardware limits.
        /// </summary>
        public ArrayOf<CraneLimitRequest> AcceptedRequests
        {
            get
            {
                lock (m_gate)
                {
                    ThrowIfDisposed();
                    return m_requests.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Replaces the simulated acquisition atomically without changing request history.
        /// </summary>
        public ValueTask SetSamplesAsync(ArrayOf<CraneSample> samples, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<CraneSignalAddress, CraneSample> next = Validate(samples);
            lock (m_gate)
            {
                ThrowIfDisposed();
                m_samples = next;
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<CraneSample>> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ThrowIfDisposed();
                return new ValueTask<ArrayOf<CraneSample>>(new List<CraneSample>(m_samples.Values).ToArrayOf());
            }
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> RequestAsync(
            CraneLimitRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ThrowIfDisposed();
                if (!m_acceptRequests || !CraneSignals.IsRequest(request.Address.Signal))
                {
                    return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
                }
                StatusCode validation = CraneSystemHandle.ValidateValue(request.Address.Signal, request.Value);
                if (StatusCode.IsBad(validation))
                {
                    return new ValueTask<StatusCode>(validation);
                }
                if (m_requests.Count >= m_maximumRequests)
                {
                    return new ValueTask<StatusCode>(StatusCodes.BadResourceUnavailable);
                }
                m_requests.Add(request);
                return new ValueTask<StatusCode>(StatusCodes.Good);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_disposed = true;
                m_samples.Clear();
                m_requests.Clear();
            }
            return default;
        }

        /// <summary>
        /// Builds a bounded replacement before changing published simulation state.
        /// </summary>
        private Dictionary<CraneSignalAddress, CraneSample> Validate(ArrayOf<CraneSample> samples)
        {
            if (samples.IsNull)
            {
                throw new ArgumentNullException(nameof(samples));
            }
            if (samples.Count > m_maximumSamples)
            {
                throw new ArgumentOutOfRangeException(nameof(samples));
            }
            var result = new Dictionary<CraneSignalAddress, CraneSample>();
            foreach (CraneSample sample in samples)
            {
                if (sample == null || result.ContainsKey(sample.Address))
                {
                    throw new ArgumentException("Samples must have distinct, non-null identities.", nameof(samples));
                }
                result.Add(sample.Address, sample);
            }
            return result;
        }

        /// <summary>
        /// Rejects access after shutdown instead of returning empty success.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(SimulatedCraneProvider));
            }
        }

        private readonly Lock m_gate = new();
        private readonly bool m_acceptRequests;
        private readonly int m_maximumSamples;
        private readonly int m_maximumRequests;
        private readonly List<CraneLimitRequest> m_requests = [];
        private Dictionary<CraneSignalAddress, CraneSample> m_samples;
        private bool m_disposed;
    }
}
