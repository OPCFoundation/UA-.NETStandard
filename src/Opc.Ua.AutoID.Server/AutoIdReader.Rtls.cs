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
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// Gets whether <paramref name="location"/> is a location of <paramref name="type"/>
        /// (OPC 30010 9.2.3 and 9.4.1).
        /// </summary>
        internal static bool IsLocationOfType(Location location, LocationTypeEnumeration type)
        {
            return (type, location.SwitchField) switch
            {
                (LocationTypeEnumeration.NMEA, LocationFields.NMEA) => true,
                (LocationTypeEnumeration.LOCAL, LocationFields.Local) => true,
                (LocationTypeEnumeration.WGS84, LocationFields.WGS84) => true,
                (LocationTypeEnumeration.NAME, LocationFields.Name) => true,
                _ => false
            };
        }

        private async ValueTask<GetLocationMethodStateResult> GetLocationCallAsync(
            ISystemContext context,
            ScanData identifier,
            LocationTypeEnumeration locationType,
            string codeType,
            CancellationToken cancellationToken)
        {
            IAutoIdRtlsProvider provider = m_rtlsProvider ??
                throw ServiceResultException.Create(
                    StatusCodes.BadMethodInvalid,
                    "The device does not support the GetLocation method.");
            ServiceResult invalid = ValidateIdentifier(identifier, codeType);
            if (ServiceResult.IsGood(invalid) && !SupportsLocationType(provider, locationType))
            {
                invalid = BadResult(StatusCodes.BadInvalidArgument, "The location type is not one of the supported location types.");
            }
            if (ServiceResult.IsBad(invalid))
            {
                return new GetLocationMethodStateResult { ServiceResult = invalid };
            }
            RtlsLocationResult? location = await RunOperationAsync(
                token => provider.GetLocationAsync(identifier, codeType, locationType, token),
                cancellationToken).ConfigureAwait(false);
            if (location is null)
            {
                return new GetLocationMethodStateResult
                {
                    ServiceResult = BadResult(StatusCodes.BadNotFound, "The location system does not know the identifier.")
                };
            }
            if (location.Location is not { } value ||
                !IsLocationOfType(value, locationType))
            {
                // OPC 30010 6.6.3.2: the result carries the location in the requested type. A supported
                // type the system cannot provide for this object is not available (Bad_InvalidState).
                return new GetLocationMethodStateResult
                {
                    ServiceResult = BadResult(
                        StatusCodes.BadInvalidState,
                        "The location of the identifier is not available as the requested location type.")
                };
            }
            ValidateResults([location]);
            return new GetLocationMethodStateResult { ServiceResult = ServiceResult.Good, Result = location };
        }

        private ValueTask<GetSupportedLocationTypesMethodStateResult> GetSupportedLocationTypesCallAsync()
        {
            IAutoIdRtlsProvider provider = m_rtlsProvider ??
                throw ServiceResultException.Create(
                    StatusCodes.BadMethodInvalid,
                    "The device does not support the GetSupportedLocationTypes method.");
            return new ValueTask<GetSupportedLocationTypesMethodStateResult>(
                new GetSupportedLocationTypesMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    SupportedLocationTypes = provider.SupportedLocationTypes
                });
        }

        /// <summary>
        /// Answers <c>GetDeviceLocation</c> (OPC 30010 6.1.3.7) with the same mapping as
        /// <c>GetLocation</c> (6.6.3.2): an undefined location type is <c>Bad_InvalidArgument</c>,
        /// and a location that is not available in the requested type is <c>Bad_InvalidState</c>.
        /// </summary>
        /// <remarks>
        /// 6.1.3.7 lists only the Call Service status codes of OPC 10000-4 5.12.2. There
        /// <c>Bad_NotSupported</c> means that the Method is not supported for the Object, which is
        /// wrong for a published method; 5.12.2.4 allows reusing a status code with the same canonical
        /// meaning, so the method reuses <c>Bad_InvalidState</c> like 6.6.3.2 does for "not available".
        /// </remarks>
        private ValueTask<GetDeviceLocationMethodStateResult> GetDeviceLocationCallAsync(
            LocationTypeEnumeration locationType)
        {
            if (!AutoIdCompat.IsDefined(locationType))
            {
                return new ValueTask<GetDeviceLocationMethodStateResult>(
                    new GetDeviceLocationMethodStateResult
                    {
                        ServiceResult = BadResult(StatusCodes.BadInvalidArgument, "The location type is not defined.")
                    });
            }
            Location? location = Device.DeviceLocation?.Value;
            if (location is null || !IsLocationOfType(location, locationType))
            {
                return new ValueTask<GetDeviceLocationMethodStateResult>(
                    new GetDeviceLocationMethodStateResult
                    {
                        ServiceResult = BadResult(
                            StatusCodes.BadInvalidState,
                            "The device location is not available as the requested location type.")
                    });
            }
            return new ValueTask<GetDeviceLocationMethodStateResult>(
                new GetDeviceLocationMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Location = location
                });
        }

        private static bool SupportsLocationType(IAutoIdRtlsProvider provider, LocationTypeEnumeration type)
        {
            foreach (LocationTypeEnumeration supported in provider.SupportedLocationTypes)
            {
                if (supported == type)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
