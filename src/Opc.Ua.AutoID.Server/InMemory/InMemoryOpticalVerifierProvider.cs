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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// A simulated optical code verifier that reports the print quality of the codes presented to it.
    /// </summary>
    /// <remarks>
    /// A verifier reports through <c>ScanStart</c> and the scan events: the NodeSet declares the
    /// verifier's synchronous <c>Scan</c> method with a result type that contradicts OPC 30010
    /// 6.4.3.1, so the runtime does not publish it.
    /// </remarks>
    public sealed class InMemoryOpticalVerifierProvider : IAutoIdAsyncScanProvider, IDisposable
    {
        /// <summary>
        /// Creates a verifier.
        /// </summary>
        /// <param name="cycleInterval">
        /// The duration of one scan cycle, which paces <c>Scan</c> and the scan events of
        /// <c>ScanStart</c>; defaults to one second.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The cycle interval is not positive.</exception>
        public InMemoryOpticalVerifierProvider(TimeSpan? cycleInterval = null)
        {
            m_engine = new InMemoryScanEngine(Snapshot, cycleInterval);
        }

        /// <inheritdoc/>
        public AutoIdReaderKind Kind => AutoIdReaderKind.OpticalVerifier;

        /// <summary>
        /// Gets the duration of one scan cycle.
        /// </summary>
        public TimeSpan CycleInterval => m_engine.CycleInterval;

        /// <summary>
        /// Puts a verified code into the range of the verifier.
        /// </summary>
        /// <param name="result">The verification result.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Present(OpticalVerifierScanResult result)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            lock (m_lock)
            {
                m_present.Add(result);
            }
        }

        /// <summary>
        /// Removes every code from the range of the verifier.
        /// </summary>
        public void Clear()
        {
            lock (m_lock)
            {
                m_present.Clear();
            }
        }

        /// <inheritdoc/>
        public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
        {
            m_engine.Attach(sink ?? throw new ArgumentNullException(nameof(sink)));
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DetachAsync(CancellationToken cancellationToken = default)
        {
            return m_engine.DetachAsync();
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdOperationStatusEnumeration> StartScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<AutoIdOperationStatusEnumeration>(m_engine.Start(settings));
        }

        /// <inheritdoc/>
        public ValueTask StopScanAsync(CancellationToken cancellationToken = default)
        {
            return m_engine.StopAsync();
        }

        /// <summary>
        /// Stops a running scan loop. Detaching the provider stops it in an orderly way; disposing
        /// is the fallback for a provider that was never attached to a reader.
        /// </summary>
        public void Dispose()
        {
            m_engine.Dispose();
        }

        private ArrayOf<ScanResult> Snapshot()
        {
            lock (m_lock)
            {
                return m_present.ToArray().ToArrayOf();
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<ScanResult> m_present = [];
        private readonly InMemoryScanEngine m_engine;
    }
}
