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
using Opc.Ua.PackML;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Client;
using ScalesSample.Client;

ScalesClientOptions options = ScalesClientOptions.Parse(args);

ITelemetryContext telemetry = DefaultTelemetry.Create(
    logging => logging.AddConsole().SetMinimumLevel(LogLevel.Warning));

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
CancellationToken ct = cancellation.Token;

SampleSession connection = await SampleSession.ConnectAsync(options, telemetry, ct).ConfigureAwait(false);
await using ConfiguredAsyncDisposable _ = connection.ConfigureAwait(false);

// session.Scales(...) also teaches the session the OPC 40200 structures, so
// WeightType and PrintableWeightType decode into their generated types.
ScalesClient scales = connection.Session.Scales(telemetry);
if (!scales.IsSupported)
{
    Console.Error.WriteLine("The server does not publish the OPC 40200 Scales namespace.");
    return 1;
}

// Discovery looks in the DI DeviceSet and the Machinery Machines folder and
// returns each scale once; a scale system's scales are below its SubDevices.
var all = new List<ScaleEntry>();
ArrayOf<ScaleEntry> discovered = await scales.DiscoverScalesAsync(ct).ConfigureAwait(false);
for (int ii = 0; ii < discovered.Count; ii++)
{
    ScaleEntry entry = discovered[ii];
    all.Add(entry);
    if (entry.IsScaleSystem)
    {
        await foreach (ScaleEntry member in scales.EnumerateSystemScalesAsync(entry.NodeId, ct).ConfigureAwait(false))
        {
            all.Add(member);
        }
    }
}

ScaleEntry? firstScale = null;
foreach (ScaleEntry entry in all)
{
    if (entry.IsScaleSystem)
    {
        ScaleIdentification system = await scales.ReadIdentificationAsync(entry.NodeId, ct).ConfigureAwait(false);
        uint? systemState = await scales.ReadPackMLStateAsync(entry.NodeId, ct).ConfigureAwait(false);
        Console.WriteLine($"Scale system {entry.DisplayName.Text} ({system.Manufacturer.Text} {system.SerialNumber}), PackML {StateName(systemState)}");
        continue;
    }
    firstScale ??= entry;
    ScaleSnapshot snapshot = await scales.ReadSnapshotAsync(entry, ct).ConfigureAwait(false);
    PrintScale(snapshot);
}

if (firstScale == null)
{
    Console.Error.WriteLine("The server publishes no scales.");
    return 1;
}

// Drive the first scale: tare, register a weight and switch the product.
NodeId scaleId = firstScale.NodeId;
Console.WriteLine();
Console.WriteLine($"Driving {firstScale.DisplayName.Text}:");
await TryAsync("SetTare", () => scales.SetTareAsync(scaleId, ct)).ConfigureAwait(false);
await TryAsync("ClearTare", () => scales.ClearTareAsync(scaleId, ct)).ConfigureAwait(false);
await TryAsync("RegisterWeight", async () =>
{
    ScaleReading? registered = await scales.RegisterWeightAsync(scaleId, ct).ConfigureAwait(false);
    Console.WriteLine($"  registered {Format(registered)}");
}).ConfigureAwait(false);
ArrayOf<ScaleProductInfo> products = await scales.ReadProductsAsync(scaleId, ct).ConfigureAwait(false);
if (products.Count > 1)
{
    await TryAsync($"SwitchProduct {products[1].ProductId}", () => scales.SwitchProductAsync(scaleId, products[1].ProductId, ct))
        .ConfigureAwait(false);
    ArrayOf<string> current = await scales.ReadCurrentProductsAsync(scaleId, ct).ConfigureAwait(false);
    Console.WriteLine($"  current products: {string.Join(", ", current.ToList())}");
}

if (options.WatchSeconds > 0)
{
    Console.WriteLine();
    Console.WriteLine($"Streaming weights and notifications of {firstScale.DisplayName.Text} for {options.WatchSeconds} s ...");
    using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
    watch.CancelAfter(TimeSpan.FromSeconds(options.WatchSeconds));
    Task weights = PrintAsync(scales.ObserveWeightAsync(scaleId, cancellationToken: watch.Token), r => $"  weight {Format(r)}");
    Task notifications = PrintAsync(
        scales.ObserveNotificationsAsync(scaleId, cancellationToken: watch.Token),
        n => $"  {(n.IsAlarm ? (n.Active == true ? "ALARM" : "cleared") : "event")} {n.NotificationId} {n.Category}: {n.Message.Text}");
    await Task.WhenAll(weights, notifications).ConfigureAwait(false);
}
return 0;

static void PrintScale(ScaleSnapshot scale)
{
    Console.WriteLine();
    Console.WriteLine($"{scale.Scale.DisplayName.Text} - {scale.Scale.Kind} scale");
    Console.WriteLine($"  {scale.Identification.Manufacturer.Text} {scale.Identification.Model.Text}, serial {scale.Identification.SerialNumber}");
    foreach (WeighingRangeDefinition range in scale.WeighingRanges)
    {
        Console.WriteLine(FormattableString.Invariant(
            $"  range {range.Low} .. {range.High}, d = {range.ActualScaleInterval}, e = {range.VerificationScaleInterval}"));
    }
    Console.WriteLine($"  current    {Format(scale.CurrentWeight)}");
    if (scale.RegisteredWeight != null)
    {
        Console.WriteLine($"  registered {Format(scale.RegisteredWeight)}");
    }
    if (scale.Products.Count > 0)
    {
        string names = string.Join(", ", ProductNames(scale.Products));
        string current = string.Join(", ", scale.CurrentProducts.ToList());
        Console.WriteLine($"  products   {names}; in processing: {current}");
    }
    if (scale.PackMLState.HasValue)
    {
        Console.WriteLine($"  PackML     {StateName(scale.PackMLState)}");
    }
}

static IEnumerable<string> ProductNames(ArrayOf<ScaleProductInfo> products)
{
    for (int ii = 0; ii < products.Count; ii++)
    {
        ScaleProductInfo product = products[ii];
        yield return $"{product.ProductId} ({product.ProductName.Text})";
    }
}

static string Format(ScaleReading? reading)
{
    if (reading == null)
    {
        return "-";
    }
    if (double.IsNaN(reading.Gross) && double.IsNaN(reading.Net) && double.IsNaN(reading.Tare))
    {
        // The variable is published but has no value yet (nothing registered).
        return "none yet";
    }
    // A streamed reading carries no units; only a full read has them.
    string unit = reading.EngineeringUnits is { } units ? " " + units.DisplayName.Text : string.Empty;
    string flags = (reading.Overload ? " OVERLOAD" : string.Empty) +
        (reading.Underload ? " UNDERLOAD" : string.Empty) +
        (reading.Stable == false ? " unstable" : string.Empty);
    return FormattableString.Invariant(
        $"gross {reading.Gross}{unit}, net {reading.Net}, tare {reading.Tare} ({reading.TareMode}){flags}");
}

static string StateName(uint? state)
{
    return state switch
    {
        null => "n/a",
        PackMLStateNumbers.Idle => "Idle",
        PackMLStateNumbers.Execute => "Execute",
        PackMLStateNumbers.Stopped => "Stopped",
        PackMLStateNumbers.Held => "Held",
        PackMLStateNumbers.Aborted => "Aborted",
        PackMLStateNumbers.Complete => "Complete",
        _ => state.Value.ToString(CultureInfo.InvariantCulture)
    };
}

static async Task TryAsync(string what, Func<ValueTask> action)
{
    try
    {
        await action().ConfigureAwait(false);
        Console.WriteLine($"  {what}: done");
    }
    catch (ServiceResultException ex)
    {
        Console.WriteLine($"  {what}: {ex.StatusCode}");
    }
}

static async Task PrintAsync<T>(IAsyncEnumerable<T> stream, Func<T, string> format)
{
    try
    {
        await foreach (T item in stream.ConfigureAwait(false))
        {
            Console.WriteLine(format(item));
        }
    }
    catch (OperationCanceledException)
    {
        // The watch period ended.
    }
}
