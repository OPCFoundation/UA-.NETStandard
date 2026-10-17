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

        private ValueTask<GetDeviceLocationMethodStateResult> GetDeviceLocationCallAsync(
            LocationTypeEnumeration locationType)
        {
            Location? location = Device.DeviceLocation?.Value;
            if (location is null)
            {
                return new ValueTask<GetDeviceLocationMethodStateResult>(
                    new GetDeviceLocationMethodStateResult
                    {
                        ServiceResult = BadResult(StatusCodes.BadNotFound, "The device has no location configured.")
                    });
            }
            bool matches = (locationType, location.SwitchField) switch
            {
                (LocationTypeEnumeration.NMEA, LocationFields.NMEA) => true,
                (LocationTypeEnumeration.LOCAL, LocationFields.Local) => true,
                (LocationTypeEnumeration.WGS84, LocationFields.WGS84) => true,
                (LocationTypeEnumeration.NAME, LocationFields.Name) => true,
                _ => false
            };
            return new ValueTask<GetDeviceLocationMethodStateResult>(
                matches
                    ? new GetDeviceLocationMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Location = location
                    }
                    : new GetDeviceLocationMethodStateResult
                    {
                        ServiceResult = BadResult(StatusCodes.BadNotSupported, "The device location is not available as the requested location type.")
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
