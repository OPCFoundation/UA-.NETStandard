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
