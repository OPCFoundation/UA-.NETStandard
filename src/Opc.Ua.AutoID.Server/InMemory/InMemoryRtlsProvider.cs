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
    /// A simulated real-time locating system that knows the objects registered with
    /// <see cref="Locate"/>.
    /// </summary>
    /// <remarks>
    /// It supports <c>Scan</c> (every known object), <c>ScanStart</c>, <c>GetLocation</c> and
    /// <c>GetSupportedLocationTypes</c>. <c>GetLocation</c> returns the stored result when its
    /// location is of the requested type. Otherwise the optional location converter translates
    /// the stored location, for example from the local coordinate system the system was
    /// configured with (OPC 30010 9.3.4) into WGS84. Without a converter, or when the converter
    /// cannot translate the location, the stored result is returned unchanged and the reader
    /// answers <c>Bad_InvalidState</c>: the requested type is supported but not available for
    /// that object. An object that was never registered is reported as unknown.
    /// </remarks>
    public sealed class InMemoryRtlsProvider : IAutoIdScanProvider, IAutoIdAsyncScanProvider, IAutoIdRtlsProvider, IDisposable
    {
        /// <summary>
        /// Creates a locating system.
        /// </summary>
        /// <param name="supportedLocationTypes">
        /// The location types the system supports; the first is the default. Defaults to WGS84 and local.
        /// </param>
        /// <param name="cycleInterval">
        /// The duration of one scan cycle, which paces <c>Scan</c> and the scan events of
        /// <c>ScanStart</c>; defaults to one second.
        /// </param>
        /// <param name="locationConverter">
        /// Translates a stored location into another location type, or returns <see langword="null"/>
        /// when it cannot. <c>GetLocation</c> calls it when the stored location is not of the
        /// requested type. Optional.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The cycle interval is not positive.</exception>
        public InMemoryRtlsProvider(
            ArrayOf<LocationTypeEnumeration> supportedLocationTypes = default,
            TimeSpan? cycleInterval = null,
            Func<Location, LocationTypeEnumeration, Location?>? locationConverter = null)
        {
            SupportedLocationTypes = supportedLocationTypes.Count == 0
                ? new[] { LocationTypeEnumeration.WGS84, LocationTypeEnumeration.LOCAL }.ToArrayOf()
                : supportedLocationTypes;
            m_locationConverter = locationConverter;
            m_engine = new InMemoryScanEngine(Snapshot, cycleInterval);
        }

        /// <inheritdoc/>
        public AutoIdReaderKind Kind => AutoIdReaderKind.Rtls;

        /// <summary>
        /// Gets the duration of one scan cycle.
        /// </summary>
        public TimeSpan CycleInterval => m_engine.CycleInterval;

        /// <inheritdoc/>
        public ArrayOf<LocationTypeEnumeration> SupportedLocationTypes { get; }

        /// <summary>
        /// Registers or updates a located object.
        /// </summary>
        /// <param name="id">The identifier of the object.</param>
        /// <param name="result">Its location.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void Locate(ByteString id, RtlsLocationResult result)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            if (id.Length == 0)
            {
                throw new ArgumentException("An object identifier is required.", nameof(id));
            }
            lock (m_lock)
            {
                m_known[id] = result;
            }
        }

        /// <summary>
        /// Forgets a located object.
        /// </summary>
        /// <param name="id">The identifier of the object.</param>
        /// <returns><see langword="true"/> when the object was known.</returns>
        public bool Forget(ByteString id)
        {
            lock (m_lock)
            {
                return m_known.Remove(id);
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

        /// <inheritdoc/>
        public ValueTask<RtlsLocationResult?> GetLocationAsync(
            ScanData identifier,
            string codeType,
            LocationTypeEnumeration locationType,
            CancellationToken cancellationToken = default)
        {
            ByteString id = identifier.SwitchField switch
            {
                ScanDataFields.ByteString => identifier.ByteString,
                ScanDataFields.Epc => identifier.Epc.UId,
                _ => ByteString.Empty
            };
            RtlsLocationResult? found = null;
            lock (m_lock)
            {
                if (id.Length > 0)
                {
                    m_known.TryGetValue(id, out found);
                }
            }
            if (found?.Location is not { } location ||
                location.SwitchField == LocationFields.None ||
                AutoIdReader.IsLocationOfType(location, locationType) ||
                m_locationConverter == null)
            {
                return new ValueTask<RtlsLocationResult?>(found);
            }
            if (m_locationConverter(location, locationType) is { } converted &&
                AutoIdReader.IsLocationOfType(converted, locationType))
            {
                // The stored result stays as registered: the caller gets a copy in the requested type.
                var copy = (RtlsLocationResult)found.Clone();
                copy.Location = converted;
                copy.EncodingMask |= (uint)ScanResultFields.Location;
                return new ValueTask<RtlsLocationResult?>(copy);
            }
            return new ValueTask<RtlsLocationResult?>(found);
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
                var results = new ScanResult[m_known.Count];
                int index = 0;
                foreach (RtlsLocationResult result in m_known.Values)
                {
                    results[index++] = result;
                }
                return results.ToArrayOf();
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<ByteString, RtlsLocationResult> m_known = [];
        private readonly InMemoryScanEngine m_engine;
        private readonly Func<Location, LocationTypeEnumeration, Location?>? m_locationConverter;
    }
}
