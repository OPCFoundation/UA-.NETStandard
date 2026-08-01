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
using System.Collections.Concurrent;
using System.Threading;
using Opc.Ua;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// In-memory, thread-safe sink (used by tests and diagnostics).
    /// </summary>
    public sealed class MockUsdSink : IUsdSink
    {
        private readonly ConcurrentDictionary<string, (Variant Value, int Count)> m_state = new();
        private readonly ConcurrentDictionary<string, (OpenUsdCompositionArc Arc, string? Asset, bool Active)> m_prims = new();
        private int m_total;
        private int m_timeSamples;

        /// <summary>
        /// Total number of <see cref="SetAttribute"/> writes observed.
        /// </summary>
        public int TotalWrites => Volatile.Read(ref m_total);

        /// <summary>
        /// Total number of <see cref="SetTimeSample"/> writes observed.
        /// </summary>
        public int TimeSampleWrites => Volatile.Read(ref m_timeSamples);

        /// <inheritdoc/>
        public void SetAttribute(string primPath, string propertyName, Variant value)
        {
            m_state.AddOrUpdate(primPath + "." + propertyName, (value, 1),
                (_, prev) => (value, prev.Count + 1));
            Interlocked.Increment(ref m_total);
        }

        /// <inheritdoc/>
        public void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value)
        {
            m_state.AddOrUpdate(primPath + "." + propertyName, (value, 1),
                (_, prev) => (value, prev.Count + 1));
            Interlocked.Increment(ref m_timeSamples);
        }

        /// <inheritdoc/>
        public void ComposePrim(string primPath, OpenUsdCompositionArc arc,
            string? assetReference, bool active)
            => m_prims[primPath] = (arc, assetReference, active);

        /// <inheritdoc/>
        public IDisposable BeginBatch() => NoopScope.Instance;

        /// <summary>
        /// Returns <c>true</c> if the given prim was composed at least once.
        /// </summary>
        public bool WasPrimComposed(string primPath) => m_prims.ContainsKey(primPath);

        /// <summary>
        /// Returns <c>true</c> if the given prim was composed and is currently active.
        /// </summary>
        public bool IsPrimActive(string primPath)
            => m_prims.TryGetValue(primPath, out (OpenUsdCompositionArc Arc, string? Asset, bool Active) p) && p.Active;

        /// <summary>
        /// Number of distinct prims composed.
        /// </summary>
        public int ComposedPrimCount => m_prims.Count;

        /// <summary>
        /// Returns <c>true</c> if a value was written for the given prim property.
        /// </summary>
        public bool WasWritten(string primPath, string propertyName)
            => m_state.TryGetValue(primPath + "." + propertyName, out (Variant Value, int Count) v)
               && v.Count > 0;

        /// <summary>
        /// Gets the most recent value written for a prim property, for test assertions.
        /// </summary>
        public bool TryGetWritten(string primPath, string propertyName, out Variant value)
        {
            if (m_state.TryGetValue(primPath + "." + propertyName, out (Variant Value, int Count) v))
            {
                value = v.Value;
                return true;
            }
            value = default;
            return false;
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
                // No buffering: nothing to flush.
            }
        }
    }
}
