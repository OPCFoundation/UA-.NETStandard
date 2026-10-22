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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AMB.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Plugins.Companions.Providers;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        private static Task VerifyMachineryInspectionAsync(
            IRepositorySampleService samples, RepositorySampleSnapshot ready, string runParent, ITelemetryContext telemetry)
        {
            return WithOwnedCompanionSessionAsync(ready, runParent, telemetry, async (context, token) =>
            {
                var source = new MachineryDataSource();
                var policy = new RepositorySampleCompanionPolicy(samples);
                ArrayOf<MachineryMachine> machines = await source.DiscoverAsync(context, token).ConfigureAwait(false);
                Assert.That(machines, Has.Count.EqualTo(1));
                MachineryMachine machine = machines[0];
                Assert.That(machine.Name, Is.EqualTo("HydraPress-1"));
                MachineryPage overview = await source.ReadAsync(
                    context, machine, MachinerySection.Overview, 0, 32, token).ConfigureAwait(false);
                Assert.That(overview.Overview.Count, Is.LessThanOrEqualTo(16));
                Assert.That(overview.Overview.ToList()
                    .Single(value => value.Name == "Manufacturer").SourceNodeId.IsNull, Is.False);
                foreach (MachinerySection section in new[]
                    { MachinerySection.ProcessValues, MachinerySection.Jobs, MachinerySection.Energy, MachinerySection.Results })
                {
                    MachineryPage page = await source.ReadAsync(
                        context, machine, section, 0, 32, token).ConfigureAwait(false);
                    if (section == MachinerySection.Results)
                    {
                        while (page.Items.IsEmpty)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
                            page = await source.ReadAsync(context, machine, section, 0, 32, token).ConfigureAwait(false);
                        }
                    }
                    Assert.That(page.HasMore, Is.False);
                    if (section == MachinerySection.ProcessValues)
                    {
                        Assert.That(page.Items, Has.Count.EqualTo(1));
                        Assert.That(page.Items[0].SourceNodeId.IsNull, Is.False);
                        Assert.That(page.Items[0].Summary, Does.Contain("Good"));
                    }
                    else if (section == MachinerySection.Energy)
                    {
                        Assert.That(page.Items, Has.Count.EqualTo(3));
                        Assert.That(page.Items.ToList().All(item => item.Values.IsEmpty), Is.True);
                    }
                    foreach (MachineryItem item in page.Items.ToList())
                    {
                        ArrayOf<CompanionValue> values = await source.InspectAsync(
                            context, machine, section, item, token).ConfigureAwait(false);
                        if (section == MachinerySection.Results && item == page.Items[0])
                        {
                            while (values.ToList().Single(value => value.Name == "Published result").Value.IsNull)
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
                                values = await source.InspectAsync(context, machine, section, item, token)
                                    .ConfigureAwait(false);
                            }
                        }
                        Assert.That(values.IsEmpty, Is.False);
                        Assert.That(values.ToList().Any(value => !value.SourceNodeId.IsNull), Is.True);
                        var selection = new MachineryActionSelection(
                            context, machine, section, item, values, token.ThrowIfCancellationRequested);
                        if (section == MachinerySection.ProcessValues)
                        {
                            var adjustment = new MachineryZeroPointProvider(selection, policy);
                            var processTasks = new CompanionWorkspace([adjustment], telemetry, deploymentPolicy: policy);
                            await using (processTasks.ConfigureAwait(false))
                            {
                                await processTasks.BindAsync(context.Session, token).ConfigureAwait(false);
                                CompanionTarget process = (await processTasks.DiscoverAsync("machinery", token)
                                    .ConfigureAwait(false))[0];
                                CompanionInspection available = await processTasks.InspectAsync(process, token)
                                    .ConfigureAwait(false);
                                Assert.That(available.Operations, Has.Count.EqualTo(1));
                                CompanionOperationDraft prepared = await processTasks.PrepareTaskAsync(
                                    process, "zero-point-adjustment", [], token).ConfigureAwait(false);
                                Assert.That(prepared.DeploymentGrant!.RuleId, Does.StartWith("owned-machinery-"));
                                CompanionOperationResult adjusted = await processTasks.ExecuteTaskAsync(
                                    prepared, false, true, null, token).ConfigureAwait(false);
                                Assert.That(Value(adjusted, "Method result"), Is.EqualTo(Variant.From(StatusCodes.Good)));
                                await Assert.ThatAsync(() => processTasks.ExecuteTaskAsync(
                                    prepared, false, true, null, token), Throws.TypeOf<InvalidOperationException>())
                                    .ConfigureAwait(false);
                            }
                            string snapshotPath = System.IO.Path.Combine(runParent, "selected-process.json");
                            try
                            {
                                await CompanionSnapshotExporter.WriteAsync(snapshotPath, selection.Target, values,
                                    context.Session.NamespaceUris, selection.RequireCurrent, token).ConfigureAwait(false);
                                Assert.That(new System.IO.FileInfo(snapshotPath).Length, Is.GreaterThan(0));
                            }
                            finally
                            {
                                System.IO.File.Delete(snapshotPath);
                            }
                        }
                        if (section == MachinerySection.Results)
                        {
                            if (values.ToList().Single(value => value.Name == "Published result").Value.IsNull)
                            {
                                Assert.That(values.ToList().Any(value => value.Name == "Result ID"), Is.False);
                            }
                            else
                            {
                                Assert.That(values.ToList().Single(value => value.Name == "Result ID").Value
                                    .TryGetValue(out string? resultId), Is.True);
                                Assert.That(resultId, Does.StartWith("stroke-"));
                                if (item == page.Items[0])
                                {
                                    Assert.That(selection.RequireResultId(), Is.EqualTo(resultId));
                                    string resultPath = System.IO.Path.Combine(runParent, "selected-result.bin");
                                    try
                                    {
                                        long transferred = await MachineryResultExport.WriteAsync(
                                            resultPath, selection, new MachineryResultSource(), token).ConfigureAwait(false);
                                        Assert.That(transferred, Is.InRange(1L, CompanionLocalExport.MaximumBytes));
                                        Assert.That(new System.IO.FileInfo(resultPath).Length, Is.EqualTo(transferred));
                                        Assert.That(System.IO.Directory.EnumerateFiles(runParent, "*.pending"), Is.Empty);
                                    }
                                    finally
                                    {
                                        System.IO.File.Delete(resultPath);
                                    }
                                }
                            }
                        }
                    }
                }
                var jobs = new Isa95CompanionProvider();
                var workspace = new CompanionWorkspace([jobs], telemetry, deploymentPolicy: policy);
                await using (workspace.ConfigureAwait(false))
                {
                    await workspace.BindAsync(context.Session, token).ConfigureAwait(false);
                    ArrayOf<CompanionTarget> endpoints = await workspace.DiscoverAsync("isa95", token)
                        .ConfigureAwait(false);
                    NodeId management = await IndustrialCompanionAccess.ResolveChildAsync(
                        context, machine.NodeId, Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs, "JobManagement",
                        false, token).ConfigureAwait(false);
                    NodeId receiver = await IndustrialCompanionAccess.ResolveChildAsync(
                        context, management, Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs, "JobOrderControl",
                        false, token).ConfigureAwait(false);
                    CompanionTarget target = endpoints.ToList().Single(value => value.NodeId == receiver);
                    CompanionInspection inspection = await workspace.InspectAsync(target, token).ConfigureAwait(false);
                    Assert.That(inspection.Operations.Contains(operation => operation.Id == "store-job"), Is.True);
                    const string jobId = "ualens-owned-press-job";
                    CompanionOperationDraft store = await workspace.PrepareTaskAsync(target, "store-job",
                    [
                        new("jobOrder", Variant.FromStructure(
                            new Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderDataType { JobOrderID = jobId })),
                        new("comment", Variant.From(ArrayOf<LocalizedText>.Empty))
                    ], token).ConfigureAwait(false);
                    Assert.That(store.DeploymentGrant!.RuleId, Does.StartWith("owned-isa95-"));
                    CompanionOperationResult stored = await workspace.ExecuteTaskAsync(store, false, true, null, token)
                        .ConfigureAwait(false);
                    Assert.That(Value(stored, "ISA-95 return status"), Is.EqualTo(Variant.From(1UL)));
                    MachineryPage storedPage = await source.ReadAsync(
                        context, machine, MachinerySection.Jobs, 0, 32, token).ConfigureAwait(false);
                    MachineryItem storedJob = storedPage.Items.ToList().Single(item => item.ItemId == jobId);
                    ArrayOf<CompanionValue> jobValues = await source.InspectAsync(
                        context, machine, MachinerySection.Jobs, storedJob, token).ConfigureAwait(false);
                    var selectedJob = new MachineryActionSelection(
                        context, machine, MachinerySection.Jobs, storedJob, jobValues, token.ThrowIfCancellationRequested);
                    var routedJobs = new MachineryJobTaskProvider(selectedJob, jobs, token);
                    var jobTasks = new CompanionWorkspace([routedJobs], telemetry, deploymentPolicy: policy);
                    await using (jobTasks.ConfigureAwait(false))
                    {
                        await jobTasks.BindAsync(context.Session, token).ConfigureAwait(false);
                        ArrayOf<CompanionTarget> routed = await jobTasks.DiscoverAsync("isa95", token).ConfigureAwait(false);
                        Assert.That(routed, Has.Count.EqualTo(1));
                        Assert.That(routed[0].NodeId, Is.EqualTo(receiver));
                        _ = await jobTasks.InspectAsync(routed[0], token).ConfigureAwait(false);
                        CompanionOperationDraft start = await jobTasks.PrepareTaskAsync(routed[0], "start-job",
                            [new("jobOrderId", Variant.From(jobId)),
                                new("comment", Variant.From(ArrayOf<LocalizedText>.Empty))], token).ConfigureAwait(false);
                        CompanionOperationResult accepted = await jobTasks.ExecuteTaskAsync(
                            start, false, true, null, token).ConfigureAwait(false);
                        Assert.That(Value(accepted, "ISA-95 return status"), Is.EqualTo(Variant.From(1UL)));
                    }
                    MachineryPage observed = await source.ReadAsync(
                        context, machine, MachinerySection.Jobs, 0, 32, token).ConfigureAwait(false);
                    Assert.That(observed.Items.ToList().Single(item => item.ItemId == jobId).Name, Is.EqualTo(jobId));
                }
                Assert.That(context.Session.Connected, Is.True);
            });
        }

        private static Task VerifyPositioningInspectionAsync(
            RepositorySampleSnapshot ready, string runParent, ITelemetryContext telemetry)
        {
            return WithOwnedCompanionSessionAsync(ready, runParent, telemetry, async (context, token) =>
            {
                var provider = new PositioningCompanionProvider();
                var workspace = new CompanionWorkspace([provider], telemetry);
                await using (workspace.ConfigureAwait(false))
                {
                    await workspace.BindAsync(context.Session, token).ConfigureAwait(false);
                    ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("positioning", token)
                        .ConfigureAwait(false);
                    Assert.That(targets, Has.Count.EqualTo(5));
                    Assert.That(targets.ToList().Count(target => target.TypeName == "Spatial object"), Is.EqualTo(2));
                    Assert.That(targets.ToList().Count(target => target.TypeName == "Global location"), Is.EqualTo(2));
                    foreach (CompanionTarget target in targets.ToList())
                    {
                        CompanionInspection snapshot = await workspace.InspectAsync(target, token)
                            .ConfigureAwait(false);
                        Assert.That(snapshot.Operations.ToList()
                            .All(operation => operation.Safety == CompanionOperationSafety.ReadOnly), Is.True);
                        if (target.TypeName == "Spatial object")
                        {
                            CompanionValue x = snapshot.Values.ToList()
                                .Single(value => value.Name == "Position frame / X");
                            Assert.That(x.SourceNodeId.IsNull, Is.False);
                            Assert.That(x.Quality, Is.EqualTo(StatusCodes.Good));
                            Assert.That(x.SourceTimestamp, Is.Not.Null);
                            Assert.That(x.EngineeringMetadata, Is.EqualTo("m"));
                            Assert.That(snapshot.Values.ToList()
                                .Single(value => value.Name == "Position frame / C").EngineeringMetadata,
                                Is.EqualTo("deg"));
                            CompanionOperationResult world = await provider.ExecuteAsync(
                                context, target, "world-degrees", null, token).ConfigureAwait(false);
                            Assert.That(Value(world, "Frame chain").TryGetValue(out ArrayOf<NodeId> frames), Is.True);
                            Assert.That(frames.Count, Is.GreaterThanOrEqualTo(2));
                        }
                        else if (target.TypeName == "Global location")
                        {
                            Variant longitudeValue = snapshot.Values.ToList()
                                .Single(value => value.Name == "Longitude (degrees)").Value;
                            Variant latitudeValue = snapshot.Values.ToList()
                                .Single(value => value.Name == "Latitude (degrees)").Value;
                            Assert.That(longitudeValue.TryGetValue(out double longitude), Is.True);
                            Assert.That(latitudeValue.TryGetValue(out double latitude), Is.True);
                            Assert.That(longitude, Is.InRange(11.57, 11.58));
                            Assert.That(latitude, Is.InRange(48.13, 48.14));
                            Assert.That(snapshot.Values.ToList().Any(value => value.Name == "ECEF X (metres)"),
                                Is.True);
                        }
                        Assert.That(CompanionSnapshotExporter.Render(
                            target, snapshot.Values, context.Session.NamespaceUris).Length,
                            Is.GreaterThan(0).And.LessThanOrEqualTo(CompanionLocalExport.MaximumBytes));
                    }
                    CompanionTarget zone = targets.ToList().Single(target => target.TypeName == "Global zone");
                    CompanionInspection calibration = await workspace.InspectAsync(zone, token).ConfigureAwait(false);
                    Assert.That(calibration.Values.ToList().Single(value => value.Name == "Dimension").Value,
                        Is.EqualTo(Variant.From(3)));
                    Assert.That(calibration.Values.ToList().Single(value => value.Name == "Invertible").Value,
                        Is.EqualTo(Variant.From(true)));
                    CompanionOperationDraft preview = await workspace.PrepareTaskAsync(zone, "preview",
                        [new("x", Variant.From(0.0)), new("y", Variant.From(0.0)), new("z", Variant.From(0.0))],
                        token).ConfigureAwait(false);
                    CompanionOperationResult result = await workspace.ExecuteTaskAsync(
                        preview, false, false, null, token).ConfigureAwait(false);
                    Assert.That(Value(result, "Longitude").TryGetValue(out double previewLongitude), Is.True);
                    Assert.That(Value(result, "Latitude").TryGetValue(out double previewLatitude), Is.True);
                    Assert.That(previewLongitude, Is.EqualTo(11.576124).Within(1e-7));
                    Assert.That(previewLatitude, Is.EqualTo(48.137154).Within(1e-7));
                    Assert.That(result.Summary, Does.Contain("no position was written"));
                }
                Assert.That(context.Session.Connected, Is.True);
            });
        }

        private static Task VerifyAssetInspectionAsync(
            IRepositorySampleService samples, RepositorySampleSnapshot ready, string runParent,
            ITelemetryContext telemetry)
        {
            return WithOwnedCompanionSessionAsync(ready, runParent, telemetry, async (context, token) =>
            {
                var policy = new RepositorySampleCompanionPolicy(samples);
                var provider = new AmbCompanionProvider(policy);
                var client = new AmbClient(context.Session, telemetry, new AmbClientOptions());
                ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, token).ConfigureAwait(false);
                Assert.That(targets, Has.Count.EqualTo(3));
                CompanionTarget? pump = null;
                foreach (CompanionTarget target in targets.ToList())
                {
                    AssetIdentificationRecord identity = await client.ReadIdentificationAsync(target.NodeId, token)
                        .ConfigureAwait(false);
                    Assert.That(identity.ProductInstanceUri, Is.Not.Null.And.Not.Empty);
                    CompanionInspection overview = await provider.InspectAsync(context, target, token)
                        .ConfigureAwait(false);
                    Assert.That(overview.Values.ToList().Any(value => !value.SourceNodeId.IsNull), Is.True);
                    if (identity.AssetId == "CP-01")
                    {
                        pump = target;
                    }
                }
                Assert.That(pump, Is.Not.Null);
                string[] sections =
                    ["health", "maintenance", "documentation", "locations", "requirements", "capabilities", "versions",
                        "sub-assets", "relations"];
                foreach (string section in sections)
                {
                    CompanionOperationResult result = await provider.ExecuteAsync(
                        context, pump!, section, null, token).ConfigureAwait(false);
                    Assert.That(result.Values.Count, Is.LessThanOrEqualTo(context.MaxFields));
                }
                ArrayOf<DocumentationLinkRecord> links = await client.ReadDocumentationLinksAsync(pump!.NodeId, token)
                    .ConfigureAwait(false);
                Assert.That(links.ToList().Any(link => link.BrowseName.Name == "OperatorHandbook"), Is.True);
                var workspace = new CompanionWorkspace([provider], telemetry, deploymentPolicy: policy);
                await using (workspace.ConfigureAwait(false))
                {
                    await workspace.BindAsync(context.Session, token).ConfigureAwait(false);
                    _ = await workspace.DiscoverAsync("amb", token).ConfigureAwait(false);
                    async Task<CompanionOperationResult> RunAsync(string operationId, ArrayOf<CompanionValue> inputs)
                    {
                        _ = await workspace.InspectAsync(pump, token).ConfigureAwait(false);
                        CompanionOperationDraft draft = await workspace.PrepareTaskAsync(
                            pump, operationId, inputs, token).ConfigureAwait(false);
                        return await workspace.ExecuteTaskAsync(
                            draft, false, true, null, token).ConfigureAwait(false);
                    }
                    await RunAsync("set-asset-id", [new("assetId", Variant.From("UALENS-CP-TEST"))])
                        .ConfigureAwait(false);
                    AssetIdentificationRecord renamed = await client.ReadIdentificationAsync(pump.NodeId, token)
                        .ConfigureAwait(false);
                    Assert.That(renamed.AssetId, Is.EqualTo("UALENS-CP-TEST"));
                    var name = new QualifiedName("UaLensManual", pump.NodeId.NamespaceIndex);
                    CompanionOperationResult added = await RunAsync("add-link",
                    [
                        new("uri", Variant.From("https://example.invalid/manual")),
                        new("browseName", Variant.From(name)),
                        new("displayName", Variant.From(new LocalizedText("UaLens manual"))),
                        new("description", Variant.From(LocalizedText.Null))
                    ]).ConfigureAwait(false);
                    Assert.That(added.Values.ToList().Single(value => value.Name == "Target").Value.TryGetValue(
                        out NodeId linkId), Is.True);
                    ArrayOf<DocumentationLinkRecord> afterAdd = await client.ReadDocumentationLinksAsync(
                        pump.NodeId, token).ConfigureAwait(false);
                    DocumentationLinkRecord created = afterAdd.ToList().Single(link => link.NodeId == linkId);
                    Assert.That(created.IsUserLink, Is.True);
                    await RunAsync("write-link",
                        [
                            new("linkId", Variant.From(linkId)),
                            new("uri", Variant.From("https://example.invalid/revised"))
                        ])
                        .ConfigureAwait(false);
                    ArrayOf<DocumentationLinkRecord> afterWrite = await client.ReadDocumentationLinksAsync(
                        pump.NodeId, token).ConfigureAwait(false);
                    Assert.That(afterWrite.ToList().Single(link => link.NodeId == linkId).Uri,
                        Is.EqualTo("https://example.invalid/revised"));
                    await RunAsync("remove-link", [new("linkId", Variant.From(linkId))]).ConfigureAwait(false);
                    ArrayOf<DocumentationLinkRecord> afterRemove = await client.ReadDocumentationLinksAsync(
                        pump.NodeId, token).ConfigureAwait(false);
                    Assert.That(afterRemove.Contains(link => link.NodeId == linkId), Is.False);
                    AssetAlarmRecord? current = null;
                    while (current is null)
                    {
                        ArrayOf<AssetAlarmRecord> alarms = await client.ReadHealthAlarmsAsync(pump.NodeId, token)
                            .ConfigureAwait(false);
                        current = alarms.ToList().FirstOrDefault(alarm =>
                            alarm.SourceNode == pump.NodeId && alarm.Retain && !alarm.IsAcknowledged);
                        if (current is null)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
                        }
                    }
                    await RunAsync("acknowledge-health",
                    [
                        new("conditionId", Variant.From(current.ConditionId)),
                        new("eventId", Variant.From(current.EventId)),
                        new("comment", Variant.From("UaLens owned-sample acknowledgement"))
                    ]).ConfigureAwait(false);
                    ArrayOf<AssetAlarmRecord> acknowledged = await client.ReadHealthAlarmsAsync(pump.NodeId, token)
                        .ConfigureAwait(false);
                    Assert.That(acknowledged.ToList().Single(alarm => alarm.ConditionId == current.ConditionId)
                        .IsAcknowledged, Is.True);
                }
            });
        }

        private static Task VerifyPumpsInspectionAsync(
            RepositorySampleSnapshot ready, string runParent, ITelemetryContext telemetry)
        {
            return WithOwnedCompanionSessionAsync(ready, runParent, telemetry, async (context, token) =>
            {
                var provider = new PumpsCompanionProvider();
                ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, token).ConfigureAwait(false);
                Assert.That(targets, Has.Count.EqualTo(2));
                foreach (CompanionTarget target in targets.ToList())
                {
                    CompanionInspection identification = await provider.InspectAsync(context, target, token)
                        .ConfigureAwait(false);
                    Assert.That(identification.Values.IsEmpty, Is.False);
                    Assert.That(identification.Operations.ToList()
                        .All(operation => operation.Safety == CompanionOperationSafety.ReadOnly), Is.True);
                    CompanionOperationResult measurements = await provider.ExecuteAsync(
                        context, target, "measurements", null, token).ConfigureAwait(false);
                    Assert.That(measurements.Values.IsEmpty, Is.False);
                    Assert.That(measurements.Values.ToList().All(value => !value.SourceNodeId.IsNull), Is.True);
                    Assert.That(measurements.Values.ToList().Any(value =>
                        value.EngineeringMetadata is { } metadata &&
                        !metadata.StartsWith("units not exposed", StringComparison.Ordinal)), Is.True);
                    Assert.That(CompanionSnapshotExporter.Render(
                        target, measurements.Values, context.Session.NamespaceUris).Length,
                        Is.GreaterThan(0).And.LessThanOrEqualTo(CompanionLocalExport.MaximumBytes));
                    foreach (CompanionOperation operation in identification.Operations.ToList())
                    {
                        CompanionOperationResult section = await provider.ExecuteAsync(
                            context, target, operation.Id, null, token).ConfigureAwait(false);
                        Assert.That(section.Summary, Is.Not.Empty);
                        Assert.That(section.Values.Count, Is.LessThanOrEqualTo(context.MaxFields));
                    }
                }
            });
        }

        private static Task VerifyWeighingOperationAsync(
            IRepositorySampleService samples, RepositorySampleSnapshot ready, string runParent,
            ITelemetryContext telemetry)
        {
            return WithOwnedCompanionSessionAsync(ready, runParent, telemetry, async (context, token) =>
            {
                var policy = new RepositorySampleCompanionPolicy(samples);
                var provider = new WeighingCompanionProvider(policy);
                NodeId packmlRoot = default;
                var workspace = new CompanionWorkspace([provider], telemetry, deploymentPolicy: policy);
                await using (workspace.ConfigureAwait(false))
                {
                    await workspace.BindAsync(context.Session, token).ConfigureAwait(false);
                    ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("weighing", token)
                        .ConfigureAwait(false);
                    CompanionTarget system = targets.ToList().Single(
                        target => target.DisplayName == "PackingLine1" && target.TypeName == "Scale system");
                    packmlRoot = await IndustrialCompanionAccess.ResolveChildAsync(
                        context, system.NodeId, Opc.Ua.Scales.Namespaces.Scales, Opc.Ua.Scales.BrowseNames.SystemState,
                        false, token).ConfigureAwait(false);
                    CompanionInspection inspection = await workspace.InspectAsync(system, token).ConfigureAwait(false);
                    Assert.That(inspection.Operations.ToList().Any(operation => operation.Id == "reset-statistics"),
                        Is.True);
                    CompanionOperationDraft draft = await workspace.PrepareTaskAsync(
                        system, "reset-statistics", [], token).ConfigureAwait(false);
                    Assert.That(draft.DeploymentGrant, Is.Not.Null);
                    Assert.That(draft.DeploymentGrant!.ExpiresAt,
                        Is.LessThanOrEqualTo(ready.CompanionOperationsExpireAt!.Value));
                    CompanionOperationResult result = await workspace.ExecuteTaskAsync(
                        draft, confirmLocalSample: false, confirmDeployment: true, progress: null,
                        cancellationToken: token).ConfigureAwait(false);
                    CompanionValue returned = result.Values.ToList().Single(value => value.Name == "Method result");
                    Assert.That(returned.Value.TryGetValue(out StatusCode status), Is.True);
                    Assert.That(status, Is.EqualTo(StatusCodes.Good));
                    CompanionOperationResult statistics = await provider.ExecuteAsync(
                        context, system, "statistics", null, token).ConfigureAwait(false);
                    CompanionValue condition = statistics.Values.ToList()
                        .Single(value => value.Name == "statistics / ResetCondition");
                    Assert.That(StatusCode.IsGood(condition.Quality), Is.True);
                    Assert.That(condition.Value.TryGetValue(out string? reason), Is.True);
                    Assert.That(reason, Is.EqualTo("ResetGlobalStatistics"));
                    CompanionValue start = statistics.Values.ToList()
                        .Single(value => value.Name == "statistics / StartTime");
                    Assert.That(StatusCode.IsGood(start.Quality), Is.True);
                    Assert.That(start.Value.TryGetValue(out DateTimeUtc timestamp), Is.True);
                    Assert.That(timestamp, Is.Not.Default);
                }
                var packml = new PackMlCompanionProvider(policy);
                var packWorkspace = new CompanionWorkspace([packml], telemetry, deploymentPolicy: policy);
                await using (packWorkspace.ConfigureAwait(false))
                {
                    await packWorkspace.BindAsync(context.Session, token).ConfigureAwait(false);
                    ArrayOf<CompanionTarget> machines = await packWorkspace.DiscoverAsync("packml", token)
                        .ConfigureAwait(false);
                    CompanionTarget machine = machines.ToList().Single(target => target.NodeId == packmlRoot);
                    CompanionInspection initial = await packWorkspace.InspectAsync(machine, token)
                        .ConfigureAwait(false);
                    Assert.That(initial.Operations.Contains(operation => operation.Id == "start"), Is.True,
                        initial.Summary);
                    CompanionOperationDraft start = await packWorkspace.PrepareTaskAsync(machine, "start",
                        [
                            new("parameters",
                                Variant.FromStructure(ArrayOf<Opc.Ua.PackML.PackMLDescriptorDataType>.Empty))
                        ],
                        token).ConfigureAwait(false);
                    CompanionOperationResult accepted = await packWorkspace.ExecuteTaskAsync(
                        start, false, true, null, token)
                        .ConfigureAwait(false);
                    Assert.That(accepted.Summary, Does.Contain("accepted").And.Contain("equipment-owned"));
                    CompanionInspection executing = await packWorkspace.InspectAsync(machine, token)
                        .ConfigureAwait(false);
                    CompanionValue state = executing.Values.ToList()
                        .Single(value => value.Name == "PackML execute state ID");
                    Assert.That(state.Value.TryGetValue(out NodeId id) &&
                        id.TryGetValue(out uint numeric) &&
                        Opc.Ua.PackML.PackMLStateNumbers.FromStateId(numeric) ==
                            Opc.Ua.PackML.PackMLStateNumbers.Execute,
                        Is.True);
                    CompanionOperationDraft stop = await packWorkspace.PrepareTaskAsync(machine, "stop", [], token)
                        .ConfigureAwait(false);
                    _ = await packWorkspace.ExecuteTaskAsync(stop, false, true, null, token).ConfigureAwait(false);
                    CompanionInspection stopped = await packWorkspace.InspectAsync(machine, token)
                        .ConfigureAwait(false);
                    CompanionValue stoppedState = stopped.Values.ToList()
                        .Single(value => value.Name == "PackML machine state ID");
                    Assert.That(stoppedState.Value.TryGetValue(out NodeId stopId) &&
                        stopId.TryGetValue(out uint stopNumeric) &&
                        Opc.Ua.PackML.PackMLStateNumbers.FromStateId(stopNumeric) ==
                            Opc.Ua.PackML.PackMLStateNumbers.Stopped,
                        Is.True);
                }
            });
        }
    }
}
