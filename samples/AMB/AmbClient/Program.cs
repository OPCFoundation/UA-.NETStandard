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
using AmbSample.Client;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;

AmbClientOptions options = AmbClientOptions.Parse(args);

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

// session.AssetManagement(...) also teaches the session the AMB structures,
// so root causes and maintenance details decode into their generated types.
AmbClient amb = connection.Session.AssetManagement(telemetry);
if (!amb.IsSupported)
{
    Console.Error.WriteLine("The server does not publish the OPC 10000-110 AMB namespace.");
    return 1;
}

// Every asset of the server, once, from the AMB alias categories.
ArrayOf<NodeId> assets = await amb.DiscoverAssetsAsync(ct).ConfigureAwait(false);
Console.WriteLine($"{assets.Count} manageable assets");
var snapshots = new List<AssetSnapshot>();
foreach (NodeId asset in assets.ToList())
{
    AssetSnapshot snapshot = await amb.ReadAssetAsync(asset, ct).ConfigureAwait(false);
    snapshots.Add(snapshot);
    Print(snapshot);
}

Console.WriteLine();
foreach (AssetLocationKind kind in new[] { AssetLocationKind.Hierarchical, AssetLocationKind.Operational })
{
    Console.WriteLine($"{kind} locations:");
    foreach (AssetLocationNode location in (await amb.BrowseLocationsAsync(kind, ct).ConfigureAwait(false)).ToList())
    {
        Console.WriteLine($"  {location.Path} ({location.Assets.Count} assets)");
    }
}

// Add a link of our own to the first asset that has the AddIn, read it
// back and remove it again.
AssetSnapshot? withLinks = snapshots.Find(snapshot => snapshot.DocumentationLinks.Count > 0);
if (withLinks != null)
{
    Console.WriteLine();
    await TryAsync("AddLink / RemoveLink", async () =>
    {
        NodeId link = await amb.AddDocumentationLinkAsync(
            withLinks.Asset,
            "https://plant.example/wiring/line2",
            new QualifiedName("WiringDiagram", withLinks.Asset.NamespaceIndex),
            new LocalizedText("en", "Wiring diagram of line 2"),
            cancellationToken: ct).ConfigureAwait(false);
        Console.WriteLine($"  added {link}");
        foreach (DocumentationLinkRecord record in
            (await amb.ReadDocumentationLinksAsync(withLinks.Asset, ct).ConfigureAwait(false)).ToList())
        {
            string editable = record.IsWritable ? " (editable)" : string.Empty;
            Console.WriteLine($"  {record.BrowseName.Name,-20} {UriOf(record)}{editable}");
        }
        await amb.RemoveDocumentationLinkAsync(withLinks.Asset, link, ct).ConfigureAwait(false);
        Console.WriteLine("  removed it again");
    }).ConfigureAwait(false);
}

// Write an AssetId; the server moves the asset in AssetsByAssetId.
AssetSnapshot? configurable = snapshots.Find(snapshot => snapshot.Identification.AssetId != null);
if (options.AssetId != null && configurable != null)
{
    Console.WriteLine();
    await TryAsync($"WriteAssetId {options.AssetId}", async () =>
    {
        await amb.WriteAssetIdAsync(configurable.Asset, options.AssetId, ct).ConfigureAwait(false);
        ArrayOf<AssetAlias> found = await amb
            .FindAssetsAsync(options.AssetId, AssetAliasCategory.ByAssetId, ct)
            .ConfigureAwait(false);
        Console.WriteLine($"  found {found.Count} asset(s) by the new AssetId");
    }).ConfigureAwait(false);
}

if (options.WatchSeconds > 0)
{
    Console.WriteLine();
    Console.WriteLine($"Streaming health alarms and maintenance for {options.WatchSeconds} s ...");
    using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
    watch.CancelAfter(TimeSpan.FromSeconds(options.WatchSeconds));
    Task alarms = WatchAlarmsAsync(amb, watch.Token);
    Task maintenance = WatchMaintenanceAsync(amb, watch.Token);
    await Task.WhenAll(alarms, maintenance).ConfigureAwait(false);
}
return 0;

static void Print(AssetSnapshot snapshot)
{
    AssetIdentificationRecord id = snapshot.Identification;
    Console.WriteLine();
    Console.WriteLine($"{id.ProductInstanceUri}  ({snapshot.Asset})");
    Console.WriteLine(
        $"  {id.Manufacturer.Text} {id.Model.Text}, serial {id.SerialNumber}, AssetId {id.AssetId ?? "-"}");
    if (id.HardwareRevision != null || id.SoftwareRevision != null)
    {
        Console.WriteLine(
            $"  hardware {id.HardwareRevision ?? "-"}, software {id.SoftwareRevision ?? "-"}, " +
            $"revision counter {id.RevisionCounter}");
    }
    if (snapshot.DeviceHealth != null)
    {
        Console.WriteLine($"  DeviceHealth {snapshot.DeviceHealth}");
    }
    foreach (AssetAlarmRecord alarm in snapshot.HealthAlarms.ToList())
    {
        string state = alarm.IsActive ? "active" : "inactive";
        Console.WriteLine($"  alarm {alarm.ConditionName}: {state}, {alarm.ConditionClass?.Name}");
    }
    foreach (MaintenanceActivityRecord activity in snapshot.MaintenanceActivities.ToList())
    {
        Console.WriteLine(
            $"  maintenance {activity.ConditionName}: {activity.State}, planned {Format(activity.PlannedDate)}");
    }
    foreach (DocumentationLinkRecord link in snapshot.DocumentationLinks.ToList())
    {
        Console.WriteLine($"  link {link.BrowseName.Name}: {UriOf(link)}");
    }
    AssetContextRecord context = snapshot.Context;
    if (context.HierarchicalLocation != null || context.OperationalLocation != null)
    {
        Console.WriteLine($"  located at {context.HierarchicalLocation ?? context.OperationalLocation}");
    }
    if (context.LocalTime != null)
    {
        Console.WriteLine($"  local time UTC{context.LocalTime.Offset / 60.0:+0.#;-0.#}");
    }
    foreach (ExpandedNodeId entry in context.Classifications.ToList())
    {
        Console.WriteLine($"  classified as {entry}");
    }
}

static async Task WatchAlarmsAsync(AmbClient amb, CancellationToken ct)
{
    try
    {
        await foreach (AssetAlarmRecord alarm in amb
            .ObserveHealthAlarmsAsync(cancellationToken: ct)
            .ConfigureAwait(false))
        {
            string causes = string.Join(
                ", ",
                alarm.PotentialRootCauses.ToList().ConvertAll(cause => cause.RootCause.Text));
            string acknowledged = alarm.IsAcknowledged ? "acknowledged" : "unacknowledged";
            string comment = alarm.Comment.IsNullOrEmpty ? string.Empty : $" - comment: {alarm.Comment.Text}";
            Console.WriteLine(
                $"  {alarm.SourceName}/{alarm.ConditionName}: {(alarm.IsActive ? "ACTIVE" : "cleared")}, " +
                $"{acknowledged}, severity {alarm.Severity} ({alarm.FaultCategory?.ToString() ?? "inactive"}) " +
                alarm.Message.Text +
                (causes.Length > 0 ? $" - root causes: {causes}" : string.Empty) +
                comment);

            // Acknowledge an alarm once it is cleared, so the server stops
            // retaining it.
            if (!alarm.IsActive && !alarm.IsAcknowledged && alarm.Retain)
            {
                await amb.AcknowledgeAsync(alarm.ConditionId, alarm.EventId, new LocalizedText("en", "seen"), ct)
                    .ConfigureAwait(false);
            }
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // The watch ended.
    }
}

static async Task WatchMaintenanceAsync(AmbClient amb, CancellationToken ct)
{
    try
    {
        await foreach (MaintenanceActivityRecord activity in amb
            .ObserveMaintenanceAsync(cancellationToken: ct)
            .ConfigureAwait(false))
        {
            Console.WriteLine($"  maintenance {activity.ConditionName}: {activity.State} - {activity.Message.Text}");
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // The watch ended.
    }
}

static string UriOf(DocumentationLinkRecord link)
{
    return string.IsNullOrEmpty(link.Uri) ? "(not set)" : link.Uri;
}

static string Format(DateTimeUtc value)
{
    return value.IsNull
        ? "-"
        : ((DateTime)value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

static async Task TryAsync(string name, Func<Task> action)
{
    Console.WriteLine($"{name}:");
    try
    {
        await action().ConfigureAwait(false);
    }
    catch (ServiceResultException ex)
    {
        Console.WriteLine($"  refused: {ex.StatusCode}");
    }
}
