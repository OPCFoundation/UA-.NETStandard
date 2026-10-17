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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Client
{
    public sealed partial class AutoIdClient
    {
        /// <summary>
        /// Runs the synchronous <c>Scan</c> method (OPC 30010 6.1.3.4) of a reader.
        /// </summary>
        /// <remarks>
        /// The device must reject a scan with every termination condition infinite
        /// (<c>Bad_InvalidArgument</c>) and a scan while another is active
        /// (<c>Bad_InvalidState</c>); both are thrown as <see cref="ServiceResultException"/>. The
        /// call stays open for the scan; raise the <c>TimeoutHint</c> of the session for long scans.
        /// An optical verifier has no usable <c>Scan</c>: the NodeSet types its results as
        /// <c>WGS84Coordinate</c>, contradicting OPC 30010 6.4.3.1. Use <see cref="ScanStartAsync"/>
        /// and the scan events for a verifier.
        /// </remarks>
        /// <param name="reader">The reader.</param>
        /// <param name="settings">The scan settings.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="NotSupportedException">The reader is an optical verifier.</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<AutoIdScanResponse> ScanAsync(
            AutoIdReaderEntry reader,
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            switch (reader.Kind)
            {
                case AutoIdReaderKind.Rfid:
                    (ArrayOf<RfidScanResult> rfid, AutoIdOperationStatusEnumeration rfidStatus) =
                        await Rfid(reader).ScanAsync(settings, cancellationToken).ConfigureAwait(false);
                    return new AutoIdScanResponse(Upcast(rfid), rfidStatus);
                case AutoIdReaderKind.Ocr:
                    (ArrayOf<OcrScanResult> ocr, AutoIdOperationStatusEnumeration ocrStatus) =
                        await new OcrReaderDeviceTypeClient(Session, reader.NodeId, Telemetry)
                            .ScanAsync(settings, cancellationToken).ConfigureAwait(false);
                    return new AutoIdScanResponse(Upcast(ocr), ocrStatus);
                case AutoIdReaderKind.Optical:
                    (ArrayOf<OpticalScanResult> optical, AutoIdOperationStatusEnumeration opticalStatus) =
                        await new OpticalReaderDeviceTypeClient(Session, reader.NodeId, Telemetry)
                            .ScanAsync(settings, cancellationToken).ConfigureAwait(false);
                    return new AutoIdScanResponse(Upcast(optical), opticalStatus);
                case AutoIdReaderKind.Rtls:
                    (ArrayOf<RtlsLocationResult> rtls, AutoIdOperationStatusEnumeration rtlsStatus) =
                        await new RtlsDeviceTypeClient(Session, reader.NodeId, Telemetry)
                            .ScanAsync(settings, cancellationToken).ConfigureAwait(false);
                    return new AutoIdScanResponse(Upcast(rtls), rtlsStatus);
                default:
                    throw new NotSupportedException(
                        "The Scan method of an optical verifier is declared with WGS84Coordinate results " +
                        "in the NodeSet, which contradicts OPC 30010 6.4.3.1; use ScanStartAsync and the scan events.");
            }
        }

        /// <summary>
        /// Starts the asynchronous scan (OPC 30010 6.1.3.5) whose results arrive as scan events.
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="settings">The scan settings; infinite termination conditions are allowed.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The status the device reports for the start; anything but success means no scan started.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ValueTask<AutoIdOperationStatusEnumeration> ScanStartAsync(
            AutoIdReaderEntry reader,
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            return Device(reader.NodeId).ScanStartAsync(settings, cancellationToken);
        }

        /// <summary>
        /// Stops the asynchronous scan (OPC 30010 6.1.3.6).
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="ServiceResultException">No scan is active (<c>Bad_InvalidState</c>).</exception>
        public ValueTask ScanStopAsync(AutoIdReaderEntry reader, CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            return Device(reader.NodeId).ScanStopAsync(cancellationToken);
        }

        /// <summary>
        /// Gets the location of a reader (<c>GetDeviceLocation</c>, OPC 30010 6.1.3.7).
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="locationType">The type of location to return.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        public ValueTask<Location> GetDeviceLocationAsync(
            AutoIdReaderEntry reader,
            LocationTypeEnumeration locationType,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            return Device(reader.NodeId).GetDeviceLocationAsync(locationType, cancellationToken);
        }

        /// <summary>
        /// Reads an area of a tag memory (<c>ReadTag</c>, OPC 30010 6.5.3.7).
        /// </summary>
        /// <param name="reader">An RFID reader.</param>
        /// <param name="identifier">The identifier of the tag, as returned by a scan.</param>
        /// <param name="codeType">The format of the identifier, for example <c>EPC</c>.</param>
        /// <param name="region">The memory bank; 0 when the tag has no banks.</param>
        /// <param name="offset">The start address in bytes.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="password">The read password, empty if none.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RFID reader.</exception>
        public async ValueTask<AutoIdTagReadResponse> ReadTagAsync(
            AutoIdReaderEntry reader,
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            uint length,
            ByteString password = default,
            CancellationToken cancellationToken = default)
        {
            RequireRfid(reader, identifier, codeType);
            (ByteString data, AutoIdOperationStatusEnumeration status) = await Rfid(reader)
                .ReadTagAsync(identifier, codeType, region, offset, length, password, cancellationToken)
                .ConfigureAwait(false);
            return new AutoIdTagReadResponse(data, status);
        }

        /// <summary>
        /// Writes data to a tag memory (<c>WriteTag</c>, OPC 30010 6.5.3.8).
        /// </summary>
        /// <param name="reader">An RFID reader.</param>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of the identifier.</param>
        /// <param name="region">The memory bank; 0 when the tag has no banks.</param>
        /// <param name="offset">The start address in bytes.</param>
        /// <param name="data">The data to write.</param>
        /// <param name="password">The write password, empty if none.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RFID reader.</exception>
        public ValueTask<AutoIdOperationStatusEnumeration> WriteTagAsync(
            AutoIdReaderEntry reader,
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            ByteString data,
            ByteString password = default,
            CancellationToken cancellationToken = default)
        {
            RequireRfid(reader, identifier, codeType);
            return Rfid(reader).WriteTagAsync(
                identifier, codeType, region, offset, data, password, cancellationToken);
        }

        /// <summary>
        /// Initialises the EPC of a tag (<c>WriteTagID</c>, OPC 30010 6.5.3.9).
        /// </summary>
        /// <param name="reader">An RFID reader.</param>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of the identifier.</param>
        /// <param name="newUId">The new identifier, an even number of bytes.</param>
        /// <param name="afi">The application family identifier.</param>
        /// <param name="toggle">The numbering system identifier toggle.</param>
        /// <param name="password">The write password, empty if none.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RFID reader.</exception>
        public ValueTask<AutoIdOperationStatusEnumeration> WriteTagIdAsync(
            AutoIdReaderEntry reader,
            ScanData identifier,
            string codeType,
            ByteString newUId,
            byte afi = 0,
            bool toggle = false,
            ByteString password = default,
            CancellationToken cancellationToken = default)
        {
            RequireRfid(reader, identifier, codeType);
            return Rfid(reader).WriteTagIDAsync(
                identifier, codeType, newUId, afi, toggle, password, cancellationToken);
        }

        /// <summary>
        /// Permanently disables a tag (<c>KillTag</c>, OPC 30010 6.5.3.4).
        /// </summary>
        /// <param name="reader">An RFID reader.</param>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of the identifier.</param>
        /// <param name="killPassword">The kill password of the tag.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RFID reader.</exception>
        public ValueTask<AutoIdOperationStatusEnumeration> KillTagAsync(
            AutoIdReaderEntry reader,
            ScanData identifier,
            string codeType,
            ByteString killPassword,
            CancellationToken cancellationToken = default)
        {
            RequireRfid(reader, identifier, codeType);
            return Rfid(reader).KillTagAsync(identifier, codeType, killPassword, cancellationToken);
        }

        /// <summary>
        /// Protects an area of a tag memory (<c>LockTag</c>, OPC 30010 6.5.3.5).
        /// </summary>
        /// <param name="reader">An RFID reader.</param>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of the identifier.</param>
        /// <param name="password">The access password of the tag.</param>
        /// <param name="region">The memory area to protect.</param>
        /// <param name="lockOperation">The lock action.</param>
        /// <param name="offset">The start address of the area in bytes.</param>
        /// <param name="length">The length of the area in bytes.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RFID reader.</exception>
        public ValueTask<AutoIdOperationStatusEnumeration> LockTagAsync(
            AutoIdReaderEntry reader,
            ScanData identifier,
            string codeType,
            ByteString password,
            RfidLockRegionEnumeration region,
            RfidLockOperationEnumeration lockOperation,
            uint offset = 0,
            uint length = 0,
            CancellationToken cancellationToken = default)
        {
            RequireRfid(reader, identifier, codeType);
            return Rfid(reader).LockTagAsync(
                identifier, codeType, password, region, lockOperation, offset, length, cancellationToken);
        }

        /// <summary>
        /// Changes a password of a tag (<c>SetTagPassword</c>, OPC 30010 6.5.3.6). OPC 30010 asks
        /// for a call over an encrypted secure channel.
        /// </summary>
        /// <param name="reader">An RFID reader.</param>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of the identifier.</param>
        /// <param name="passwordType">The password to change.</param>
        /// <param name="accessPassword">The old password.</param>
        /// <param name="newPassword">The new password.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RFID reader.</exception>
        public ValueTask<AutoIdOperationStatusEnumeration> SetTagPasswordAsync(
            AutoIdReaderEntry reader,
            ScanData identifier,
            string codeType,
            RfidPasswordTypeEnumeration passwordType,
            ByteString accessPassword,
            ByteString newPassword,
            CancellationToken cancellationToken = default)
        {
            RequireRfid(reader, identifier, codeType);
            return Rfid(reader).SetTagPasswordAsync(
                identifier, codeType, passwordType, accessPassword, newPassword, cancellationToken);
        }

        /// <summary>
        /// Gets the location of an object (<c>GetLocation</c>, OPC 30010 6.6.3.2).
        /// </summary>
        /// <remarks>
        /// The NodeSet orders the arguments identifier, location type, code type; the
        /// specification text orders them identifier, code type, location type. The generated proxy
        /// follows the NodeSet, which is what servers expose on the wire.
        /// </remarks>
        /// <param name="reader">An RTLS device.</param>
        /// <param name="identifier">The identifier of the located object.</param>
        /// <param name="codeType">The format of the identifier.</param>
        /// <param name="locationType">The requested location type.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RTLS device.</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public ValueTask<RtlsLocationResult> GetLocationAsync(
            AutoIdReaderEntry reader,
            ScanData identifier,
            string codeType,
            LocationTypeEnumeration locationType,
            CancellationToken cancellationToken = default)
        {
            RequireRtls(reader);
            if (identifier is null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }
            return Rtls(reader).GetLocationAsync(identifier, locationType, codeType, cancellationToken);
        }

        /// <summary>
        /// Gets the location types an RTLS device supports; the first is the default
        /// (<c>GetSupportedLocationTypes</c>, OPC 30010 6.6.3.3).
        /// </summary>
        /// <param name="reader">An RTLS device.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RTLS device.</exception>
        public ValueTask<ArrayOf<LocationTypeEnumeration>> GetSupportedLocationTypesAsync(
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            RequireRtls(reader);
            return Rtls(reader).GetSupportedLocationTypesAsync(cancellationToken);
        }

        private RfidReaderDeviceTypeClient Rfid(AutoIdReaderEntry reader)
        {
            return new RfidReaderDeviceTypeClient(Session, reader.NodeId, Telemetry);
        }

        private RtlsDeviceTypeClient Rtls(AutoIdReaderEntry reader)
        {
            return new RtlsDeviceTypeClient(Session, reader.NodeId, Telemetry);
        }

        private static void RequireReader(AutoIdReaderEntry reader)
        {
            if (reader is null)
            {
                throw new ArgumentNullException(nameof(reader));
            }
        }

        private static void RequireRfid(AutoIdReaderEntry reader, ScanData identifier, string codeType)
        {
            RequireReader(reader);
            if (reader.Kind != AutoIdReaderKind.Rfid)
            {
                throw new InvalidOperationException(
                    $"The tag access methods belong to RFID readers; '{reader.BrowseName.Name}' is a {reader.Kind} reader.");
            }
            if (identifier is null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }
            if (string.IsNullOrEmpty(codeType))
            {
                throw new ArgumentException("A code type is required.", nameof(codeType));
            }
        }

        private static void RequireRtls(AutoIdReaderEntry reader)
        {
            RequireReader(reader);
            if (reader.Kind != AutoIdReaderKind.Rtls)
            {
                throw new InvalidOperationException(
                    $"The location methods belong to RTLS devices; '{reader.BrowseName.Name}' is a {reader.Kind} reader.");
            }
        }
    }
}
