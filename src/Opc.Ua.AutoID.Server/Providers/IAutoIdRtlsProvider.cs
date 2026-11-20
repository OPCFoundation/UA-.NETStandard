/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// A provider for a real-time locating system: it executes the location methods of
    /// <c>RtlsDeviceType</c> (OPC 30010 6.6.3).
    /// </summary>
    /// <remarks>
    /// <see cref="IAutoIdReaderProvider.Kind"/> must be <see cref="AutoIdReaderKind.Rtls"/>. A
    /// provider that also implements <see cref="IAutoIdScanProvider"/> returns
    /// <see cref="RtlsLocationResult"/> instances from its scans.
    /// </remarks>
    public interface IAutoIdRtlsProvider : IAutoIdReaderProvider
    {
        /// <summary>
        /// Gets the location types the system supports (<c>GetSupportedLocationTypes</c>,
        /// OPC 30010 6.6.3.3). At least one; the first is the default. Must not change.
        /// </summary>
        ArrayOf<LocationTypeEnumeration> SupportedLocationTypes { get; }

        /// <summary>
        /// Returns the location of an object (<c>GetLocation</c>, OPC 30010 6.6.3.2): the location
        /// just acquired or the last known one, which the result's timestamp tells apart.
        /// </summary>
        /// <remarks>
        /// The location of the result must be of <paramref name="locationType"/>. When the system
        /// knows the object but cannot provide its location as that type, return the result with
        /// the location it has: the reader answers <c>Bad_InvalidState</c> (the command is not
        /// available) instead of passing a location of another type to the client.
        /// </remarks>
        /// <param name="identifier">The identifier of the located object.</param>
        /// <param name="codeType">The format of <paramref name="identifier"/>.</param>
        /// <param name="locationType">The requested location type, one of <see cref="SupportedLocationTypes"/>.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The location, or <see langword="null"/> when the system does not know the object.</returns>
        ValueTask<RtlsLocationResult?> GetLocationAsync(
            ScanData identifier,
            string codeType,
            LocationTypeEnumeration locationType,
            CancellationToken cancellationToken = default);
    }
}
