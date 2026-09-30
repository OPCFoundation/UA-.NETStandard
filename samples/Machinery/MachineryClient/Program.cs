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
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MachinerySample.Client;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Di;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Result;

MachineryClientOptions options = MachineryClientOptions.Parse(args);

ITelemetryContext telemetry = DefaultTelemetry.Create(
    logging => logging.SetMinimumLevel(LogLevel.Warning));

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

SampleSession connection = await SampleSession
    .ConnectAsync(options, telemetry, cancellation.Token)
    .ConfigureAwait(false);
await using ConfiguredAsyncDisposable _ = connection.ConfigureAwait(false);

MachineryClient machinery = connection.Session.Machinery(telemetry);
if (machinery.MachinesFolderId.IsNull)
{
    Console.Error.WriteLine(
        "The server does not publish the OPC 40001-1 Machinery namespace.");
    return 1;
}

NodeId firstMachine = NodeId.Null;
await foreach (MachineEntry machine in machinery
    .EnumerateMachinesAsync(cancellation.Token)
    .ConfigureAwait(false))
{
    if (firstMachine.IsNull)
    {
        firstMachine = machine.NodeId;
    }

    Console.WriteLine($"Machine {machine.BrowseName} ({machine.NodeId})");

    MachineIdentification? identification = await machinery
        .ReadIdentificationAsync(machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
    if (identification != null)
    {
        Console.WriteLine(
            $"  {identification.Manufacturer.Text} {identification.Model.Text} " +
            $"— serial {identification.SerialNumber}");
        Console.WriteLine($"  location: {identification.Location ?? "(not published)"}");
    }

    FiniteStateSnapshot? state = await machinery
        .GetItemStateAsync(machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
    FiniteStateSnapshot? mode = await machinery
        .GetOperationModeAsync(machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
    Console.WriteLine(
        $"  state: {state?.CurrentState.Text ?? "(not published)"} / " +
        $"mode: {mode?.CurrentState.Text ?? "(not published)"}");

    await foreach (MachineEntry component in machinery
        .EnumerateComponentsAsync(machine.NodeId, cancellation.Token)
        .ConfigureAwait(false))
    {
        Console.WriteLine($"  component {component.BrowseName}");
    }

    // OPC 40001-1 §7.1: every building block a machinery item supports is
    // reachable through this one folder, so a client can discover them
    // instead of probing for each in turn.
    await foreach (MachineEntry block in machinery
        .EnumerateBuildingBlocksAsync(machine.NodeId, cancellation.Token)
        .ConfigureAwait(false))
    {
        Console.WriteLine($"  building block {block.BrowseName}");
    }

    DeviceHealthEnumeration? health = await machinery
        .ReadDeviceHealthAsync(machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
    if (health.HasValue)
    {
        Console.WriteLine($"  device health: {health.Value}");
    }

    await PrintCountersAsync(machinery, machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
    await PrintEquipmentAsync(machinery, machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
    await PrintProcessValuesAsync(machinery, machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
    await PrintEnergyAsync(machinery, machine.NodeId, cancellation.Token)
        .ConfigureAwait(false);
}

if (firstMachine.IsNull)
{
    Console.Error.WriteLine("The server publishes no machines.");
    return 1;
}

if (options.Download)
{
    await DownloadLatestResultAsync(machinery, firstMachine, cancellation.Token)
        .ConfigureAwait(false);
}

if (options.ObserveSeconds > 0)
{
    Console.WriteLine(
        $"Observing MachineryItemState for {options.ObserveSeconds}s " +
        "(OPC 40001-1 state changes always come from the server) …");
    using var observation = CancellationTokenSource.CreateLinkedTokenSource(
        cancellation.Token);
    observation.CancelAfter(TimeSpan.FromSeconds(options.ObserveSeconds));
    try
    {
        await foreach (FiniteStateSnapshot snapshot in machinery
            .ObserveItemStateAsync(
                firstMachine,
                connection.Streaming,
                cancellationToken: observation.Token)
            .ConfigureAwait(false))
        {
            Console.WriteLine(
                $"  {snapshot.Timestamp:HH:mm:ss}  -> {snapshot.CurrentState} " +
                $"(via {snapshot.LastTransition})");
        }
    }
    catch (OperationCanceledException)
    {
        // The observation window elapsed or the user pressed Ctrl+C.
    }
}

return 0;

static string Invariant(double? value, string format = "F1")
{
    return value.HasValue
        ? value.Value.ToString(format, CultureInfo.InvariantCulture)
        : "(not published)";
}

static string InvariantValue(Variant value)
{
    if (value.TryGetValue(out double number))
    {
        return number.ToString("G6", CultureInfo.InvariantCulture);
    }
    if (value.TryGetValue(out float single))
    {
        return single.ToString("G6", CultureInfo.InvariantCulture);
    }
    return FormattableString.Invariant($"{value}");
}

static async Task PrintCountersAsync(
    MachineryClient machinery,
    NodeId machine,
    CancellationToken cancellationToken)
{
    MachineryOperationCounters? counters = await machinery
        .ReadOperationCountersAsync(machine, cancellationToken)
        .ConfigureAwait(false);
    if (counters != null)
    {
        string powerOn = Invariant(counters.PowerOnDuration);
        string operating = Invariant(counters.OperationDuration);
        Console.WriteLine(
            $"  operation counters: power-on {powerOn} h, operating {operating} h");
    }

    await foreach (MachineryLifetimeVariable life in machinery
        .ReadLifetimeCountersAsync(machine, cancellationToken)
        .ConfigureAwait(false))
    {
        Console.WriteLine(
            $"  lifetime {life.BrowseName.Name}: {Invariant(life.Remaining, "F0")} of " +
            $"{Invariant(life.StartValue, "F0")} left");
    }
}

static async Task PrintEquipmentAsync(
    MachineryClient machinery,
    NodeId machine,
    CancellationToken cancellationToken)
{
    await foreach (MachineryEquipmentItem equipment in machinery
        .EnumerateEquipmentAsync(machine, cancellationToken)
        .ConfigureAwait(false))
    {
        Console.WriteLine(
            $"  equipment {equipment.BrowseName.Name} " +
            $"({equipment.MachineryEquipmentTypeId})");
        if (equipment.EquipmentLife?.Remaining is double remaining)
        {
            Console.WriteLine(
                $"    life: {Invariant(remaining, "F0")} of " +
                $"{Invariant(equipment.EquipmentLife.StartValue, "F0")} left");
        }
    }
}

static async Task PrintProcessValuesAsync(
    MachineryClient machinery,
    NodeId machine,
    CancellationToken cancellationToken)
{
    await foreach (MachineEntry entry in machinery
        .EnumerateProcessValuesAsync(machine, cancellationToken)
        .ConfigureAwait(false))
    {
        MachineryProcessValue? processValue = await machinery
            .ReadProcessValueAsync(entry.NodeId, cancellationToken)
            .ConfigureAwait(false);
        if (processValue == null)
        {
            continue;
        }
        string unit = processValue.EngineeringUnits?.DisplayName.Text ?? string.Empty;
        Console.WriteLine(
            $"  process value {entry.BrowseName.Name}: " +
            $"{Invariant(processValue.Value)} {unit} " +
            $"(setpoint {Invariant(processValue.Setpoint)}, " +
            $"{Invariant(processValue.PercentageValue)} % of range)");
        Console.WriteLine(
            $"    limits: {Invariant(processValue.LowLowLimit)} / " +
            $"{Invariant(processValue.LowLimit)} / " +
            $"{Invariant(processValue.HighLimit)} / " +
            $"{Invariant(processValue.HighHighLimit)}");
    }
}

static async Task PrintEnergyAsync(
    MachineryClient machinery,
    NodeId machine,
    CancellationToken cancellationToken)
{
    await foreach (MachineEntry resource in machinery
        .EnumerateEnergyResourcesAsync(machine, cancellationToken)
        .ConfigureAwait(false))
    {
        MachineryMeteringPoint? main = await machinery
            .ReadMainMeteringPointAsync(resource.NodeId, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(
            $"  energy {resource.BrowseName.Name}: " +
            $"Main = {main?.ApplicationTag ?? "(missing)"}");
        if (main == null)
        {
            continue;
        }
        foreach (MachineryMeasurementValue measurement in main.Measurements)
        {
            // Variant.ToString() formats with the current culture, which would
            // print a German decimal comma here. A measurement is data, so it
            // is formatted invariantly like every other number the sample
            // prints.
            Console.WriteLine(
                $"    {measurement.BrowseName.Name} = " +
                $"{InvariantValue(measurement.Value)}");
        }
    }
}

static async Task DownloadLatestResultAsync(
    MachineryClient machinery,
    NodeId machine,
    CancellationToken cancellationToken)
{
    ResultManagementTypeClient? results = await machinery
        .ResultManagementAsync(machine, cancellationToken)
        .ConfigureAwait(false);
    if (results == null)
    {
        Console.WriteLine("The machine publishes no OPC 40001-101 ResultManagement.");
        return;
    }

        (uint resultHandle, ResultDataType result, int error) latest = await results
        .GetLatestResultAsync(timeout: 5000, ct: cancellationToken)
        .ConfigureAwait(false);
    if (latest.error != 0)
    {
        Console.WriteLine("No result has been produced yet.");
        return;
    }
    string? resultId = latest.result?.ResultMetaData?.ResultId;
    if (string.IsNullOrEmpty(resultId))
    {
        Console.WriteLine("No result has been produced yet.");
        return;
    }

    Console.WriteLine($"Latest result: {resultId}");
    try
    {
        ByteString payload = await machinery
            .DownloadResultAsync(machine, resultId!, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(
            FormattableString.Invariant($"  downloaded {payload.Span.Length} bytes:"));
        foreach (string line in Encoding.UTF8
            .GetString(payload.Span.ToArray())
            .TrimEnd()
            .Split('\n'))
        {
            Console.WriteLine($"  {line}");
        }
    }
    finally
    {
        await results
            .ReleaseResultHandleAsync(latest.resultHandle, ct: cancellationToken)
            .ConfigureAwait(false);
    }
}
