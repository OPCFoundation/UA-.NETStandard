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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoIdSample.Client;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AutoID;
using Opc.Ua.AutoID.Client;

var options = AutoIdClientOptions.Parse(args);

ITelemetryContext telemetry = DefaultTelemetry.Create(
    logging => logging.AddConsole().SetMinimumLevel(LogLevel.Warning));

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
CancellationToken ct = cancellation.Token;

SampleSession connection = await SampleSession
    .ConnectAsync(options, telemetry, ct)
    .ConfigureAwait(false);
await using ConfiguredAsyncDisposable _ = connection.ConfigureAwait(false);

// session.AutoId(...) also teaches the session the OPC 30010 structures, so scan results and the
// ScanData union decode into their generated types.
AutoIdClient autoId = connection.Session.AutoId(telemetry);
if (!autoId.IsSupported)
{
    Console.Error.WriteLine("The server does not publish the OPC 30010 AutoID namespace.");
    return 1;
}

// Discovery looks in the DI DeviceSet and returns readers of the five concrete kinds.
AutoIdReaderEntry? rfid = null;
await foreach (AutoIdReaderEntry entry in autoId.EnumerateReadersAsync(ct).ConfigureAwait(false))
{
    Console.WriteLine($"{entry.Kind} reader {entry.BrowseName.Name}  ({entry.NodeId})");
    if (entry.Kind == AutoIdReaderKind.Rfid)
    {
        rfid ??= entry;
    }
}
if (rfid is null)
{
    Console.Error.WriteLine("The server publishes no RFID reader.");
    return 1;
}

AutoIdDeviceSnapshot device = await autoId.ReadDeviceAsync(rfid, cancellationToken: ct).ConfigureAwait(false);
Console.WriteLine();
Console.WriteLine($"{device.DeviceName}: {device.DeviceStatus}, {device.Manufacturer.Text} {device.Model.Text}, " +
    $"serial {device.SerialNumber}");
Console.WriteLine($"  {device.DeviceInfo} at {device.DeviceLocationName}");
Console.WriteLine($"  antennas: {string.Join(", ", device.AntennaNames.ToArray()!.Select(a => $"{a.AntennaId}={a.AntennaName}"))}");

// A synchronous scan: the method returns after the pallets in range were read.
AutoIdScanResponse scan = await autoId.ScanAsync(
    rfid, new ScanSettings { Duration = 1000, DataAvailable = true }, ct).ConfigureAwait(false);
Console.WriteLine();
Console.WriteLine($"Scan: {scan.Status}, {scan.Results.Count} tag(s)");
foreach (ScanResult result in scan.Results)
{
    var tag = (RfidScanResult)result;
    RfidSighting? sighting = tag.Sighting.Count > 0 ? tag.Sighting[0] : null;
    Console.WriteLine($"  {Hex(tag.ScanData.Epc.UId)}  " +
        $"antenna {sighting?.Antenna}, {sighting?.Strength} dBm");
}

// Read the user memory of the first tag, write it and read it back.
if (scan.Results.Count > 0)
{
    ScanData id = scan.Results[0].ScanData;
    AutoIdTagReadResponse label = await autoId.ReadTagAsync(rfid, id, "EPC", 3, 0, 11, cancellationToken: ct)
        .ConfigureAwait(false);
    Console.WriteLine();
    Console.WriteLine($"ReadTag user memory: {label.Status}, \"{Encoding.ASCII.GetString(label.Data.Memory.ToArray())}\"");
    AutoIdOperationStatusEnumeration written = await autoId.WriteTagAsync(
        rfid, id, "EPC", 3, 16, ByteString.From(Encoding.ASCII.GetBytes("CHECKED")), cancellationToken: ct)
        .ConfigureAwait(false);
    AutoIdTagReadResponse back = await autoId.ReadTagAsync(rfid, id, "EPC", 3, 16, 7, cancellationToken: ct)
        .ConfigureAwait(false);
    Console.WriteLine($"WriteTag: {written}; read back: \"{Encoding.ASCII.GetString(back.Data.Memory.ToArray())}\"");
}

if (options.WatchSeconds > 0)
{
    // ScanStart delivers the results as events until ScanStop.
    Console.WriteLine();
    Console.WriteLine($"ScanStart, watching scan events for {options.WatchSeconds} s ...");
    AutoIdOperationStatusEnumeration started = await autoId.ScanStartAsync(rfid, new ScanSettings(), ct)
        .ConfigureAwait(false);
    Console.WriteLine($"ScanStart: {started}");
    using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
    window.CancelAfter(TimeSpan.FromSeconds(options.WatchSeconds));
    Task presence = WatchPresenceAsync(autoId, connection, rfid, window.Token);
    try
    {
        await foreach (AutoIdScanEventTypeRecord scanEvent in autoId
            .ObserveScanEventsAsync(connection.Streaming, rfid, window.Token).ConfigureAwait(false))
        {
            Console.WriteLine($"  scan event at {scanEvent.Time:HH:mm:ss}: " +
                string.Join(", ", scanEvent.ScanResult!.Select(r =>
                    Hex(r.ScanData.Epc.UId)[^6..])));
        }
    }
    catch (OperationCanceledException) when (window.IsCancellationRequested)
    {
        // The watch time is up.
    }
    await presence.ConfigureAwait(false);
    await autoId.ScanStopAsync(rfid, CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine("ScanStop: stopped");
}

return 0;

static string Hex(ByteString value)
{
#if NET5_0_OR_GREATER
    return Convert.ToHexString(value.Span);
#else
    return BitConverter.ToString(value.Memory.ToArray()).Replace("-", string.Empty);
#endif
}

static async Task WatchPresenceAsync(
    AutoIdClient autoId,
    SampleSession connection,
    AutoIdReaderEntry reader,
    CancellationToken cancellationToken)
{
    try
    {
        await foreach (AutoIdPresenceEventTypeRecord record in autoId
            .ObservePresenceEventsAsync(connection.Streaming, reader, cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine($"  presence: {record.Presence} tag(s) in range");
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // The watch time is up.
    }
}
