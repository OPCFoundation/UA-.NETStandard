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
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Client;
using PumpsSample.Client;
using BrowseNames = Opc.Ua.Pumps.BrowseNames;

PumpsClientOptions options = PumpsClientOptions.Parse(args);

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

// session.Pumps(...) also teaches the session the OPC 40223 structures, so
// PhysicalAddress and the option sets decode into their generated types.
PumpsClient pumps = connection.Session.Pumps(telemetry);
if (!pumps.IsSupported)
{
    Console.Error.WriteLine("The server does not publish the OPC 40223 Pumps namespace.");
    return 1;
}

var watchList = new List<(string Label, NodeId NodeId)>();
NodeId firstPump = NodeId.Null;

// Discovery looks in the DI DeviceSet and the Machinery Machines folder and
// returns each pump once, vendor subtypes of PumpType included.
await foreach (PumpEntry entry in pumps.EnumeratePumpsAsync(ct).ConfigureAwait(false))
{
    if (firstPump.IsNull)
    {
        firstPump = entry.NodeId;
    }

    // One call for everything the pump publishes. Each group also has its own
    // Read…Async method for when you only need one of them.
    PumpSnapshot pump = await pumps.ReadPumpAsync(entry.NodeId, cancellationToken: ct).ConfigureAwait(false);
    PrintPump(pump);

    string name = pump.BrowseName.Name ?? pump.NodeId.ToString();
    AddWatch(watchList, $"{name} MassFlow", pump.Operational?.Measurements[BrowseNames.MassFlow]);
    AddWatch(watchList, $"{name} PumpOperation", pump.Operational?.Signals[BrowseNames.PumpOperation]);
    AddWatch(watchList, $"{name} Cavitation", pump.Supervision?.ProcessFluid[BrowseNames.Cavitation]);
    // Vibration measurements are nested groups of Measurements, one per
    // <Vibration> instance the server created.
    foreach ((string point, PumpValueSet vibration) in pump.Operational?.Measurements.Groups ??
        new Dictionary<string, PumpValueSet>())
    {
        AddWatch(watchList, $"{name} {point} vRMS", vibration[BrowseNames.OverallVibrationVelocityRMS]);
    }
}

if (firstPump.IsNull)
{
    Console.Error.WriteLine("The server publishes no pumps.");
    return 1;
}

// Units and ranges never change, so a client polling on a cycle reads them
// once (the snapshot above did) and then polls values only: one browse and
// one read per group instead of two reads.
PumpValueSet polled = await pumps
    .ReadMeasurementsAsync(firstPump, PumpReadOptions.ValuesOnly, ct)
    .ConfigureAwait(false);
Console.WriteLine();
Console.WriteLine(
    $"Polled {polled.Count} measurements with PumpReadOptions.ValuesOnly; " +
    $"MassFlow = {Format(polled[BrowseNames.MassFlow])}");

if (options.WatchSeconds > 0 && watchList.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(
        $"Watching {watchList.Count} values for {options.WatchSeconds}s " +
        "(the pumps swap duty every 90 s; cavitation appears once a minute) …");
    using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
    window.CancelAfter(TimeSpan.FromSeconds(options.WatchSeconds));

    var watchers = new List<Task>();
    foreach ((string label, NodeId nodeId) in watchList)
    {
        watchers.Add(WatchAsync(connection, label, nodeId, window.Token));
    }
    await Task.WhenAll(watchers).ConfigureAwait(false);
}

return 0;

static void PrintPump(PumpSnapshot pump)
{
    Console.WriteLine();
    Console.WriteLine($"Pump {pump.BrowseName.Name}  ({pump.NodeId})");

    PumpNameplate? nameplate = pump.Nameplate;
    if (nameplate != null)
    {
        Console.WriteLine(
            $"  {nameplate.Manufacturer.Text} {nameplate.Model.Text}, serial {nameplate.SerialNumber}, " +
            $"built {nameplate.YearOfConstruction}/{nameplate.MonthOfConstruction} in {nameplate.CountryOfOrigin}");
        Console.WriteLine($"  location: {nameplate.Location ?? "(not published)"}");
    }

    if (pump.Configuration is { } configuration)
    {
        PrintSet("Design", configuration.Design);
        PrintSet("Implementation", configuration.Implementation);
        PrintSet("SystemRequirements", configuration.SystemRequirements);
    }

    if (pump.Operational is { } operational)
    {
        PrintSet("Measurements", operational.Measurements);
        PrintSet("Signals", operational.Signals);
        if (operational.MultiPump is { } multiPump)
        {
            Console.WriteLine(
                $"  MultiPump: role {multiPump.PumpRole}, mode {multiPump.OperationMode}, " +
                $"{multiPump.MaximumNumberOfPumpsInOperation} of {multiPump.NumberOfPumps} pumps in operation");
        }
    }

    // ActiveSignals yields only the raised signals; empty is the healthy case.
    if (pump.Supervision is { } supervision)
    {
        if (supervision.HasActiveSignals)
        {
            foreach ((string group, PumpValue signal) in supervision.ActiveSignals)
            {
                Console.WriteLine($"  supervision: {group}/{signal.Name} RAISED");
            }
        }
        else
        {
            Console.WriteLine("  supervision: no active signals");
        }
    }

    if (pump.Maintenance is { } maintenance)
    {
        Console.WriteLine(
            $"  maintenance: {maintenance.StateOfTheItem}, {maintenance.MaintenanceLevel}, " +
            $"failure {maintenance.HasFailure}, operating time " +
            $"{Format(maintenance.General[BrowseNames.OperatingTime])}");
    }

    foreach (PumpPortDescriptor port in pump.Ports)
    {
        Console.WriteLine(
            $"  port {port.Name}: {port.Kind}, direction {port.Direction?.ToString() ?? "-"}, " +
            $"{port.Category ?? "no category"}");
        PrintSet($"{port.Name}/Measurements", port.Measurements);
    }

    foreach (PumpValue link in pump.Documentation)
    {
        Console.WriteLine($"  document {link.Name}: {link.AsString()}");
    }
}

static void PrintSet(string title, PumpValueSet values)
{
    if (values.IsEmpty && values.Groups.Count == 0)
    {
        return;
    }
    Console.WriteLine($"  {title}:");
    foreach (PumpValue value in values)
    {
        string range = value.EuRange is { } euRange
            ? FormattableString.Invariant($"  [{euRange.Low} … {euRange.High}]")
            : string.Empty;
        Console.WriteLine($"    {value.Name,-34} {Format(value)}{range}");
    }
    foreach ((string name, PumpValueSet group) in values.Groups)
    {
        PrintSet($"{title}/{name}", group);
    }
}

static string Format(PumpValue? value)
{
    if (value == null)
    {
        return "(not published)";
    }
    string unit = value.EngineeringUnits?.DisplayName.Text is { Length: > 0 } symbol ? " " + symbol : string.Empty;
    double? number = value.AsDouble();
    return number.HasValue
        ? number.Value.ToString("G6", CultureInfo.InvariantCulture) + unit
        : FormattableString.Invariant($"{value.Value}") + unit;
}

static void AddWatch(List<(string Label, NodeId NodeId)> watchList, string label, PumpValue? value)
{
    // PumpValue.NodeId is the node that carries the reading; for a discrete
    // signal object that is its DiscreteInputValue child, which is what a
    // monitored item has to watch.
    if (value != null && !value.NodeId.IsNull)
    {
        watchList.Add((label, value.NodeId));
    }
}

static async Task WatchAsync(
    SampleSession connection,
    string label,
    NodeId nodeId,
    CancellationToken ct)
{
    try
    {
        await foreach (DataValueChange change in connection.Streaming
            .SubscribeDataChangesAsync(nodeId, ct: ct)
            .ConfigureAwait(false))
        {
            DataValue value = change.Value;
            string text = value.WrappedValue.TryGetValue(out double number)
                ? number.ToString("G6", CultureInfo.InvariantCulture)
                : FormattableString.Invariant($"{value.WrappedValue}");
            Console.WriteLine($"  {DateTime.Now:HH:mm:ss}  {label,-30} {text}");
        }
    }
    catch (OperationCanceledException)
    {
        // The watch window elapsed or the user pressed Ctrl+C.
    }
}
