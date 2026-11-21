// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// A simulated OCR or optical (barcode) reader that scans the codes presented to it.
    /// </summary>
    /// <remarks>
    /// Present codes with <see cref="Present"/> and remove them with <see cref="Clear"/>; a scan
    /// reports every code in range. It supports the synchronous <c>Scan</c> and the asynchronous
    /// <c>ScanStart</c>. Use <see cref="InMemoryOpticalVerifierProvider"/> for a verifier.
    /// </remarks>
    public sealed class InMemoryScanReaderProvider : IAutoIdScanProvider, IAutoIdAsyncScanProvider, IDisposable
    {
        /// <summary>
        /// Creates a reader.
        /// </summary>
        /// <param name="kind"><see cref="AutoIdReaderKind.Ocr"/> or <see cref="AutoIdReaderKind.Optical"/>.</param>
        /// <param name="cycleInterval">
        /// The duration of one scan cycle, which paces <c>Scan</c> and the scan events of
        /// <c>ScanStart</c>; defaults to one second.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The kind is not OCR or optical, or the cycle interval is not positive.
        /// </exception>
        public InMemoryScanReaderProvider(AutoIdReaderKind kind, TimeSpan? cycleInterval = null)
        {
            if (kind is not (AutoIdReaderKind.Ocr or AutoIdReaderKind.Optical))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Use Ocr or Optical; the other reader kinds have their own simulated providers.");
            }
            Kind = kind;
            m_engine = new InMemoryScanEngine(Snapshot, cycleInterval);
        }

        /// <inheritdoc/>
        public AutoIdReaderKind Kind { get; }

        /// <summary>
        /// Gets the duration of one scan cycle.
        /// </summary>
        public TimeSpan CycleInterval => m_engine.CycleInterval;

        /// <summary>
        /// Puts a code into the range of the reader.
        /// </summary>
        /// <param name="result">
        /// An <see cref="OcrScanResult"/> for an OCR reader or an <see cref="OpticalScanResult"/>
        /// for an optical reader.
        /// </param>
        /// <exception cref="ArgumentException">The result does not fit the reader kind.</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void Present(ScanResult result)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            bool fits = Kind == AutoIdReaderKind.Ocr ? result is OcrScanResult : result is OpticalScanResult;
            if (!fits)
            {
                throw new ArgumentException(
                    $"A {Kind} reader scans {(Kind == AutoIdReaderKind.Ocr ? nameof(OcrScanResult) : nameof(OpticalScanResult))}.",
                    nameof(result));
            }
            lock (m_lock)
            {
                m_present.Add(result);
            }
        }

        /// <summary>
        /// Removes every code from the range of the reader.
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
        public ValueTask<AutoIdScanOutcome> ScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            return m_engine.ScanAsync(settings, cancellationToken);
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
