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

namespace Opc.Ua.AutoID.Server
{
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// The client name of a command the device executed on its own.
        /// </summary>
        private const string DeviceClient = "AutoID device";

        /// <inheritdoc/>
        public async ValueTask ReportPresenceAsync(ushort presence, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_operations.TryEnter())
            {
                throw new ObjectDisposedException(nameof(AutoIdReader));
            }
            try
            {
                ISystemContext context = Runtime.SystemContext;
                AutoIdPresenceEventState e = context.CreateInstanceOfAutoIdPresenceEventType(Device, default);
                e.Initialize(context, Device, EventSeverity.Low, new LocalizedText("Presence changed"));
                e.DeviceName!.Value = DeviceNameOf();
                e.Presence!.Value = presence;
                await Device.ReportEventAsync(context, e, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ReportLogEntryAsync(string entry, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(entry))
            {
                throw new ArgumentException("A logbook entry is required.", nameof(entry));
            }
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_operations.TryEnter())
            {
                throw new ObjectDisposedException(nameof(AutoIdReader));
            }
            try
            {
                ISystemContext context = Runtime.SystemContext;
                AutoIdLogEntryEventState e = context.CreateInstanceOfAutoIdLogEntryEventType(Device, default);
                e.Initialize(context, Device, EventSeverity.Low, new LocalizedText(entry));
                e.DeviceName!.Value = DeviceNameOf();
                await Device.ReportEventAsync(context, e, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <inheritdoc/>
        public ValueTask ReportAccessAsync(
            string command,
            string client,
            ArrayOf<AccessResult> results,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(command))
            {
                throw new ArgumentException("A command name is required.", nameof(command));
            }
            if (results.Count == 0)
            {
                throw new ArgumentException("An access report needs at least one result.", nameof(results));
            }
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_operations.TryEnter())
            {
                throw new ObjectDisposedException(nameof(AutoIdReader));
            }
            try
            {
                RaiseAccessEvent(client ?? DeviceClient, command, results);
            }
            finally
            {
                m_operations.Exit();
            }
            return default;
        }

        /// <summary>
        /// Raises the scan event of the reader kind (OPC 30010 7.2 to 7.7).
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void RaiseScanEvent(ArrayOf<ScanResult> results)
        {
            ISystemContext context = Runtime.SystemContext;
            var message = new LocalizedText("Scan result");
            switch (Kind)
            {
                case AutoIdReaderKind.Rfid:
                    RfidScanEventState rfid = context.CreateInstanceOfRfidScanEventType(Device, default);
                    rfid.Initialize(context, Device, EventSeverity.Medium, message);
                    rfid.DeviceName!.Value = DeviceNameOf();
                    rfid.ScanResult!.Value = Cast<RfidScanResult>(results);
                    Device.ReportEvent(context, rfid);
                    break;
                case AutoIdReaderKind.Ocr:
                    OcrScanEventState ocr = context.CreateInstanceOfOcrScanEventType(Device, default);
                    ocr.Initialize(context, Device, EventSeverity.Medium, message);
                    ocr.DeviceName!.Value = DeviceNameOf();
                    ocr.ScanResult!.Value = Cast<OcrScanResult>(results);
                    Device.ReportEvent(context, ocr);
                    break;
                case AutoIdReaderKind.Optical:
                    OpticalScanEventState optical = context.CreateInstanceOfOpticalScanEventType(Device, default);
                    optical.Initialize(context, Device, EventSeverity.Medium, message);
                    optical.DeviceName!.Value = DeviceNameOf();
                    optical.ScanResult!.Value = Cast<OpticalScanResult>(results);
                    Device.ReportEvent(context, optical);
                    break;
                case AutoIdReaderKind.OpticalVerifier:
                    OpticalVerifierScanEventState verifier =
                        context.CreateInstanceOfOpticalVerifierScanEventType(Device, default);
                    verifier.Initialize(context, Device, EventSeverity.Medium, message);
                    verifier.DeviceName!.Value = DeviceNameOf();
                    verifier.ScanResult!.Value = Cast<OpticalVerifierScanResult>(results);
                    Device.ReportEvent(context, verifier);
                    break;
                case AutoIdReaderKind.Rtls:
                    RtlsLocationEventState rtls = context.CreateInstanceOfRtlsLocationEventType(Device, default);
                    rtls.Initialize(context, Device, EventSeverity.Medium, message);
                    rtls.DeviceName!.Value = DeviceNameOf();
                    rtls.ScanResult!.Value = Cast<RtlsLocationResult>(results);
                    Device.ReportEvent(context, rtls);
                    break;
                default:
                    throw new InvalidOperationException("Unknown reader kind.");
            }
        }

        /// <summary>
        /// Raises the access event (OPC 30010 7.10 and 7.11) when access events are enabled.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private void RaiseAccessEvent(string client, string command, ArrayOf<AccessResult> results)
        {
            if (!Runtime.Options.PublishAccessEvents || results.Count == 0)
            {
                return;
            }
            ISystemContext context = Runtime.SystemContext;
            var message = new LocalizedText(command + " accessed an identifier");
            if (Kind == AutoIdReaderKind.Rfid)
            {
                var typed = new RfidAccessResult[results.Count];
                for (int index = 0; index < typed.Length; index++)
                {
                    typed[index] = results[index] as RfidAccessResult ??
                        throw new ArgumentException(
                            "The access results of an RFID reader must be RfidAccessResult instances.",
                            nameof(results));
                }
                RfidAccessEventState rfid = context.CreateInstanceOfRfidAccessEventType(Device, default);
                rfid.Initialize(context, Device, EventSeverity.Low, message);
                rfid.AddClient(context).AddCommand(context);
                rfid.DeviceName!.Value = DeviceNameOf();
                rfid.Client!.Value = client;
                rfid.Command!.Value = command;
                rfid.AccessResult!.Value = typed.ToArrayOf();
                Device.ReportEvent(context, rfid);
                return;
            }
            AutoIdAccessEventState access = context.CreateInstanceOfAutoIdAccessEventType(Device, default);
            access.Initialize(context, Device, EventSeverity.Low, message);
            access.AddClient(context).AddCommand(context);
            access.DeviceName!.Value = DeviceNameOf();
            access.Client!.Value = client;
            access.Command!.Value = command;
            access.AccessResult!.Value = results;
            Device.ReportEvent(context, access);
        }

        /// <summary>
        /// Builds the access results of scan results: the identifier, its code type and when it was seen.
        /// </summary>
        private ArrayOf<AccessResult> AccessResultsOf(ArrayOf<ScanResult> results)
        {
            var access = new AccessResult[results.Count];
            for (int index = 0; index < access.Length; index++)
            {
                ScanResult result = results[index];
                access[index] = Kind == AutoIdReaderKind.Rfid && result is RfidScanResult rfid
                    ? RfidAccessOf(rfid)
                    : new AccessResult
                    {
                        EncodingMask = (uint)(AccessResultFields.CodeType |
                            AccessResultFields.Identifier |
                            AccessResultFields.Timestamp),
                        CodeType = result.CodeType,
                        Identifier = result.ScanData,
                        Timestamp = result.Timestamp
                    };
            }
            return access.ToArrayOf();
        }

        private static RfidAccessResult RfidAccessOf(RfidScanResult result)
        {
            var access = new RfidAccessResult
            {
                EncodingMask = (uint)(RfidAccessResultFields.CodeType |
                    RfidAccessResultFields.Identifier |
                    RfidAccessResultFields.Timestamp),
                CodeType = result.CodeType,
                Identifier = result.ScanData,
                Timestamp = result.Timestamp
            };
            if (result.Sighting.Count > 0)
            {
                RfidSighting sighting = result.Sighting[0];
                access.Antenna = sighting.Antenna;
                access.Strength = sighting.Strength;
                access.CurrentPowerLevel = sighting.CurrentPowerLevel;
                access.EncodingMask |= (uint)(RfidAccessResultFields.Antenna |
                    RfidAccessResultFields.Strength |
                    RfidAccessResultFields.CurrentPowerLevel);
            }
            if (result.ScanData is { SwitchField: ScanDataFields.Epc } data)
            {
                access.PC = data.Epc.PC;
                access.EncodingMask |= (uint)RfidAccessResultFields.PC;
            }
            return access;
        }

        /// <summary>
        /// Gets the <c>DeviceName</c> value of the device.
        /// </summary>
        private string DeviceNameOf()
        {
            return Device.DeviceName?.Value ?? Name.Name ?? string.Empty;
        }

        /// <summary>
        /// Gets the name of the client of a call: the session's user, or a generic name.
        /// </summary>
        private static string ClientOf(ISystemContext? context)
        {
            string? name = (context as ISessionSystemContext)?.UserIdentity?.DisplayName;
            return string.IsNullOrEmpty(name) ? "OPC UA client" : name!;
        }
    }
}
