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
using Opc.Ua.Server;

namespace Opc.Ua.AutoID.Server
{
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// The client name of a command the device executed on its own.
        /// </summary>
        private const string DeviceClient = "AutoID device";

        /// <summary>
        /// The client name of a call whose session does not describe the client application.
        /// </summary>
        private const string UnnamedClient = "OPC UA client";

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
                await PublishPresenceAsync(presence).ConfigureAwait(false);
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
                await PublishLogEntryAsync(entry).ConfigureAwait(false);
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
            return ReportAccessCoreAsync(command, client, results, cancellationToken);
        }

        /// <summary>
        /// Retains report admission through diagnostic formatting, notifications and event publication.
        /// </summary>
        private async ValueTask ReportAccessCoreAsync(
            string command, string client, ArrayOf<AccessResult> results, CancellationToken cancellationToken)
        {
            try
            {
                await RaiseAccessEventAsync(
                    client ?? DeviceClient, command, results, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <summary>
        /// Gets the name of the client application that originated a call (the <c>Client</c> of
        /// OPC 30010 6.1.3.3 and 7.10): the application name of the client description the session
        /// was created with, its application URI, or a generic name. The user of the session is not
        /// the client application.
        /// </summary>
        internal static string ClientOf(ISystemContext? context)
        {
            if ((context as ServerSystemContext)?.OperationContext?.Session is { } session)
            {
                string? name = session.ReadDiagnostics(
                    diagnostics => diagnostics.ClientDescription?.ApplicationName.Text);
                if (!string.IsNullOrEmpty(name))
                {
                    return name!;
                }
                string? uri = session.ClientApplicationUri;
                if (!string.IsNullOrEmpty(uri))
                {
                    return uri!;
                }
            }
            return UnnamedClient;
        }

        /// <summary>
        /// Raises the scan event of the reader kind (OPC 30010 7.2 to 7.7).
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask RaiseScanEventAsync(ArrayOf<ScanResult> results, CancellationToken cancellationToken)
        {
            ISystemContext context = Runtime.SystemContext;
            var message = new LocalizedText("Scan result");
            IFilterTarget e;
            switch (Kind)
            {
                case AutoIdReaderKind.Rfid:
                    RfidScanEventState rfid = context.CreateInstanceOfRfidScanEventType(Device, default);
                    rfid.Initialize(context, Device, EventSeverity.Medium, message);
                    rfid.DeviceName!.Value = DeviceNameOf();
                    rfid.ScanResult!.Value = Cast<RfidScanResult>(results);
                    e = rfid;
                    break;
                case AutoIdReaderKind.Ocr:
                    OcrScanEventState ocr = context.CreateInstanceOfOcrScanEventType(Device, default);
                    ocr.Initialize(context, Device, EventSeverity.Medium, message);
                    ocr.DeviceName!.Value = DeviceNameOf();
                    ocr.ScanResult!.Value = Cast<OcrScanResult>(results);
                    e = ocr;
                    break;
                case AutoIdReaderKind.Optical:
                    OpticalScanEventState optical = context.CreateInstanceOfOpticalScanEventType(Device, default);
                    optical.Initialize(context, Device, EventSeverity.Medium, message);
                    optical.DeviceName!.Value = DeviceNameOf();
                    optical.ScanResult!.Value = Cast<OpticalScanResult>(results);
                    e = optical;
                    break;
                case AutoIdReaderKind.OpticalVerifier:
                    OpticalVerifierScanEventState verifier =
                        context.CreateInstanceOfOpticalVerifierScanEventType(Device, default);
                    verifier.Initialize(context, Device, EventSeverity.Medium, message);
                    verifier.DeviceName!.Value = DeviceNameOf();
                    verifier.ScanResult!.Value = Cast<OpticalVerifierScanResult>(results);
                    e = verifier;
                    break;
                case AutoIdReaderKind.Rtls:
                    RtlsLocationEventState rtls = context.CreateInstanceOfRtlsLocationEventType(Device, default);
                    rtls.Initialize(context, Device, EventSeverity.Medium, message);
                    rtls.DeviceName!.Value = DeviceNameOf();
                    rtls.ScanResult!.Value = Cast<RtlsLocationResult>(results);
                    e = rtls;
                    break;
                default:
                    throw new InvalidOperationException("Unknown reader kind.");
            }
            await Device.ReportEventAsync(context, e, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Projects the access report and raises its event (OPC 30010 7.10 and 7.11) when enabled.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private async ValueTask TryRaiseAccessEventAsync(
            string client, string command, ArrayOf<AccessResult> results)
        {
            // The operation has already happened: a failure to publish its diagnostics or event must not
            // turn an executed command into a failed call.
            try
            {
                await RaiseAccessEventAsync(
                    client, command, results, tolerateDiagnosticCopyFailure: true).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.ReaderShutdownStepFailed(ex, Name.Name, "publishing the access diagnostics or event");
            }
        }

        /// <summary>
        /// Projects and reports an access asynchronously without blocking on asynchronous sinks.
        /// </summary>
        private async ValueTask RaiseAccessEventAsync(
            string client, string command, ArrayOf<AccessResult> results, bool tolerateDiagnosticCopyFailure = false,
            CancellationToken cancellationToken = default)
        {
            if (results.Count == 0)
            {
                return;
            }
            ValidateAccessResults(results);
            await PublishLastAccessAsync(client, command, results[0], tolerateDiagnosticCopyFailure, cancellationToken)
                .ConfigureAwait(false);
            if (CreateAccessEvent(client, command, results) is { } e)
            {
                await Device.ReportEventAsync(Runtime.SystemContext, e, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validates reports consumed by diagnostics or event publication.
        /// </summary>
        private void ValidateAccessResults(ArrayOf<AccessResult> results)
        {
            if (Device.Diagnostics != null || Runtime.Options.PublishAccessEvents)
            {
                for (int index = 0; index < results.Count; index++)
                {
                    if (results[index] is null ||
                        (Kind == AutoIdReaderKind.Rfid && results[index] is not RfidAccessResult))
                    {
                        throw new ArgumentException(
                            "The access result type does not match the reader.", nameof(results));
                    }
                }
            }
        }

        /// <summary>
        /// Creates the configured access event, or returns null when event publication is disabled.
        /// </summary>
        private IFilterTarget? CreateAccessEvent(string client, string command, ArrayOf<AccessResult> results)
        {
            if (!Runtime.Options.PublishAccessEvents)
            {
                return null;
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
                return rfid;
            }
            AutoIdAccessEventState access = context.CreateInstanceOfAutoIdAccessEventType(Device, default);
            access.Initialize(context, Device, EventSeverity.Low, message);
            access.AddClient(context).AddCommand(context);
            access.DeviceName!.Value = DeviceNameOf();
            access.Client!.Value = client;
            access.Command!.Value = command;
            access.AccessResult!.Value = results;
            return access;
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
    }
}
