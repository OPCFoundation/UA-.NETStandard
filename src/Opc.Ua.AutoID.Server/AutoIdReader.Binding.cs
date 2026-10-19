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

namespace Opc.Ua.AutoID.Server
{
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// Wires the method nodes of the device to the reader. Called once, after the device is
        /// registered, for the methods the builder instantiated.
        /// </summary>
        internal void Bind()
        {
            BindScanMethods();
            BindDeviceMethods();
            switch (Device)
            {
                case RfidReaderDeviceState rfid:
                    BindRfid(rfid);
                    break;
                case RtlsDeviceState rtls:
                    BindRtls(rtls);
                    break;
            }
        }

        private void BindScanMethods()
        {
            switch (Device)
            {
                case RfidReaderDeviceState { Scan: { } scan }:
                    scan.OnCallAsync = async (context, method, objectId, setting, ct) =>
                    {
                        ScanCallOutcome o = await ScanCoreAsync(context, setting, ct).ConfigureAwait(false);
                        return new ScanRfidReaderDeviceTypeMethodStateResult
                        {
                            ServiceResult = o.ServiceResult,
                            ResultsOut = ServiceResult.IsGood(o.ServiceResult) ? Cast<RfidScanResult>(o.Results) : default,
                            Status = o.Status
                        };
                    };
                    break;
                case OcrReaderDeviceState { Scan: { } scan }:
                    scan.OnCallAsync = async (context, method, objectId, setting, ct) =>
                    {
                        ScanCallOutcome o = await ScanCoreAsync(context, setting, ct).ConfigureAwait(false);
                        return new ScanOcrReaderDeviceTypeMethodStateResult
                        {
                            ServiceResult = o.ServiceResult,
                            ResultsOut = ServiceResult.IsGood(o.ServiceResult) ? Cast<OcrScanResult>(o.Results) : default,
                            Status = o.Status
                        };
                    };
                    break;
                case OpticalReaderDeviceState { Scan: { } scan }:
                    scan.OnCallAsync = async (context, method, objectId, setting, ct) =>
                    {
                        ScanCallOutcome o = await ScanCoreAsync(context, setting, ct).ConfigureAwait(false);
                        return new ScanOpticalReaderDeviceTypeMethodStateResult
                        {
                            ServiceResult = o.ServiceResult,
                            ResultsOut = ServiceResult.IsGood(o.ServiceResult)
                                ? Cast<OpticalScanResult>(o.Results)
                                : default,
                            Status = o.Status
                        };
                    };
                    break;
                case RtlsDeviceState { Scan: { } scan }:
                    scan.OnCallAsync = async (context, method, objectId, setting, ct) =>
                    {
                        ScanCallOutcome o = await ScanCoreAsync(context, setting, ct).ConfigureAwait(false);
                        return new ScanRtlsDeviceTypeMethodStateResult
                        {
                            ServiceResult = o.ServiceResult,
                            ResultsOut = ServiceResult.IsGood(o.ServiceResult)
                                ? Cast<RtlsLocationResult>(o.Results)
                                : default,
                            Status = o.Status
                        };
                    };
                    break;
            }
        }

        private void BindDeviceMethods()
        {
            Device.ScanStart?.OnCallAsync = async (context, method, objectId, settings, ct) =>
                {
                    (ServiceResult service, AutoIdOperationStatusEnumeration status) =
                        await ScanStartCoreAsync(context, settings, ct).ConfigureAwait(false);
                    return new ScanStartMethodStateResult { ServiceResult = service, Status = status };
                };
            Device.ScanStop?.OnCallMethod2Async = (context, method, objectId, inputs, outputs, ct) =>
                    ScanStopCoreAsync(ct);
            Device.GetDeviceLocation?.OnCallAsync = (context, method, objectId, locationType, ct) =>
                    GetDeviceLocationCallAsync(locationType);
        }

        private void BindRfid(RfidReaderDeviceState rfid)
        {
            rfid.ReadTag?.OnCallAsync = (context, method, objectId, identifier, codeType, region, offset, length, password, ct) =>
                    ReadTagCallAsync(context, identifier, codeType, region, offset, length, password, ct);
            rfid.WriteTag?.OnCallAsync = (context, method, objectId, identifier, codeType, region, offset, data, password, ct) =>
                    WriteTagCallAsync(context, identifier, codeType, region, offset, data, password, ct);
            rfid.WriteTagID?.OnCallAsync = (context, method, objectId, identifier, codeType, newUId, afi, toggle, password, ct) =>
                    WriteTagIdCallAsync(context, identifier, codeType, newUId, afi, toggle, password, ct);
            rfid.KillTag?.OnCallAsync = (context, method, objectId, identifier, codeType, killPassword, ct) =>
                    KillTagCallAsync(context, identifier, codeType, killPassword, ct);
            rfid.LockTag?.OnCallAsync = (context, method, objectId, identifier, codeType, password, region, lockOperation, offset, length, ct) =>
                    LockTagCallAsync(context, identifier, codeType, password, region, lockOperation, offset, length, ct);
            rfid.SetTagPassword?.OnCallAsync = (context, method, objectId, identifier, codeType, passwordType, accessPassword, newPassword, ct) =>
                    SetTagPasswordCallAsync(context, identifier, codeType, passwordType, accessPassword, newPassword, ct);
        }

        private void BindRtls(RtlsDeviceState rtls)
        {
            rtls.GetLocation?.OnCallAsync = (context, method, objectId, identifier, locationType, codeType, ct) =>
                    GetLocationCallAsync(context, identifier, locationType, codeType, ct);
            rtls.GetSupportedLocationTypes?.OnCallAsync = (context, method, objectId, ct) =>
                    GetSupportedLocationTypesCallAsync();
        }
    }
}
